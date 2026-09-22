/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Features.Conversation;
using UiharuMind.Features.Conversation.Pages;
using UiharuMind.Features.Conversation.SessionList;
using UiharuMind.Shared.Shell;

namespace UiharuMind.Features.DevAutomation;

/// <summary>
/// 开发脚本能用的那几步。
///
/// 刻意<b>只有读与导航</b>：看一眼现在什么样、跳到哪一页、打开哪个会话。
/// 没有「发一句话给模型」——那一步要花钱、要有模型在线，且一旦有了它，
/// 这份脚本就从「复现界面状态」变成「替用户说话」，是另一件事，该单独议。
/// </summary>
internal static class DevCommandRegistry
{
    /// <summary>造出全部步骤。撞名后者胜出（这里不会撞，留给以后拆文件时兜底）</summary>
    /// <returns>步骤集合</returns>
    public static IReadOnlyList<IDevCommand> CreateAll() =>
    [
        new JumpToPageCommand(),
        new UiSnapshotCommand(),
        new SessionListCommand(),
        new OpenSessionCommand(),
        new MemoryStatsCommand(),
        new FontDiagnosticsCommand(),
    ];

    /// <summary>取当前显示的那一页（不是会话页时为 null）</summary>
    internal static ConversationPageDataBase? CurrentConversationPage() =>
        App.ViewModel.Content as ConversationPageDataBase;

    /// <summary>取当前显示的会话页；不是会话页就报错——脚本写错了该当场知道</summary>
    internal static ConversationPageDataBase RequireConversationPage() =>
        CurrentConversationPage() ?? throw new InvalidOperationException(
            "current page is not a conversation page; run 'page.jump' with agent or chat first");

    /// <param name="args">脚本参数</param>
    /// <param name="name">键名</param>
    /// <returns>字符串值</returns>
    internal static string RequireString(JsonElement args, string name)
    {
        if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty(name, out JsonElement value))
        {
            throw new ArgumentException($"missing arg '{name}'");
        }

        return value.GetString() ?? throw new ArgumentException($"arg '{name}' is not a string");
    }
}

/// <summary>跳到某一页。<c>args.page</c> 取 agent / chat / character / model / log</summary>
internal sealed class JumpToPageCommand : IDevCommand
{
    public string Name => "page.jump";

    public object? Execute(JsonElement args)
    {
        string page = DevCommandRegistry.RequireString(args, "page");
        // 对话页已合并：agent/chat 都进同一个页面，再经切换器拨到对应类型区
        MenuPages? target = page switch
        {
            "agent" => MenuPages.MenuConversationKey,
            "chat" => MenuPages.MenuConversationKey,
            "character" => MenuPages.MenuCharacterKey,
            "model" => MenuPages.MenuModelKey,
            "log" => MenuPages.MenuLogKey,
            _ => null,
        };
        if (target == null) throw new ArgumentException($"unknown page '{page}'");

        App.ViewModel.JumpToPage(target.Value);
        if (page is "agent" or "chat" && App.ViewModel.Content is ConversationPageData conversation)
        {
            conversation.CurrentType = page == "agent"
                ? EConversationType.Agent
                : EConversationType.Chat;
        }

        return new { page };
    }
}

/// <summary>
/// 当前界面长什么样。这是脚本的<b>主要产出</b>——条目数与忙碌态正是长会话那些问题的观测量。
/// </summary>
internal sealed class UiSnapshotCommand : IDevCommand
{
    public string Name => "ui.snapshot";

    public object? Execute(JsonElement args)
    {
        if (DevCommandRegistry.CurrentConversationPage() is not { } page)
        {
            return new { page = App.ViewModel.Content?.GetType().Name };
        }

        var conversation = page.Conversation;
        return new
        {
            page = page.GetType().Name,
            sessionId = conversation.CurrentMeta?.SessionId,
            title = conversation.Title,
            items = conversation.Items.Count,
            hasEarlierMessages = conversation.HasEarlierMessages,
            isGenerating = conversation.IsGenerating,
            isSessionLoading = conversation.IsSessionLoading,
            sessionsInList = page.SessionList.Sessions.Count,
        };
    }
}

/// <summary>列出本页会话（只给脚本挑 id 用，字段刻意少）</summary>
internal sealed class SessionListCommand : IDevCommand
{
    public string Name => "session.list";

    public object? Execute(JsonElement args)
    {
        ConversationPageDataBase page = DevCommandRegistry.RequireConversationPage();
        return page.SessionList.Sessions
            .Select(x => new { id = x.SessionId, name = x.Name })
            .ToList();
    }
}

/// <summary>
/// 打开某个会话。<b>走的是选中项那条路</b>（<c>SessionList.SelectedSession</c>），
/// 与用户在左栏点一下完全同一条，不另开入口
/// </summary>
internal sealed class OpenSessionCommand : IDevCommand
{
    public string Name => "session.open";

    public object? Execute(JsonElement args)
    {
        string sessionId = DevCommandRegistry.RequireString(args, "id");
        ConversationPageDataBase page = DevCommandRegistry.RequireConversationPage();
        SessionListItem item = page.SessionList.Find(sessionId)
                               ?? throw new ArgumentException($"session '{sessionId}' is not in this page's list");

        page.SessionList.SelectedSession = item;
        return new { opened = sessionId };
    }
}

/// <summary>
/// 内存与驻留：托管堆、加载了几个会话本体、其中几份历史还在内存里。
/// 「跑久了涨到一个多 G」那件事就靠这三个数说话
/// </summary>
internal sealed class MemoryStatsCommand : IDevCommand
{
    public string Name => "diag.memory";

    public object? Execute(JsonElement args)
    {
        bool collect = args.ValueKind == JsonValueKind.Object &&
                       args.TryGetProperty("collect", out JsonElement value) &&
                       value.ValueKind == JsonValueKind.True;

        if (collect) GC.Collect(2, GCCollectionMode.Forced, blocking: true);

        // 活对象、已提交、碎片三个数要一起看:用户看到的那个「占用多少」是已提交那一档,
        // 而它减去活对象就是 IdleMemoryReclaimer 该还掉的部分
        GCMemoryInfo info = GC.GetGCMemoryInfo();
        return new
        {
            managedHeapMb = Math.Round(GC.GetTotalMemory(collect) / 1048576.0, 1),
            committedMb = Math.Round(info.TotalCommittedBytes / 1048576.0, 1),
            fragmentedMb = Math.Round(info.FragmentedBytes / 1048576.0, 1),
            workingSetMb = Math.Round(Environment.WorkingSet / 1048576.0, 1),
            loadedSessions = SessionManager.Instance.LoadedSessionCount,
            residentHistories = SessionManager.Instance.ResidentHistoryCount,
            gen2Collections = GC.CollectionCount(2),
        };
    }
}

/// <summary>
/// 字体诊断：给一段样本文字，报出每一段字形<b>实际</b>解析到的字体、字重与是否合成。
///
/// 为什么必须在真实进程里问：无头测试默认用的是假的字体管理器
/// （<c>UseHeadlessDrawing = true</c>，系统字体一律解析成 <c>BareMinimum</c>），
/// 在那一层得出的「族名对不对、加粗有没有生效」全都不作数。
///
/// 参数：<c>text</c>（样本，默认中英混排）、<c>family</c>（字体族，默认 <c>MainFont</c>）。
/// 中英混排是关键——中文走不走字符回退，正是「同一个 **XXX** 一半粗一半不粗」的分水岭。
/// </summary>
internal sealed class FontDiagnosticsCommand : IDevCommand
{
    public string Name => "diag.font";

    public object? Execute(JsonElement args)
    {
        string text = Arg(args, "text") ?? "修了什么 Bold 123";
        string? familyArg = Arg(args, "family");
        FontFamily family = familyArg != null
            ? new FontFamily(familyArg)
            : Application.Current?.FindResource("MainFont") as FontFamily ?? FontFamily.Default;

        return new
        {
            family = family.ToString(),
            normal = Describe(text, family, FontWeight.Normal),
            bold = Describe(text, family, FontWeight.Bold),
        };
    }

    private static string? Arg(JsonElement args, string name) =>
        args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out JsonElement value)
            ? value.GetString()
            : null;

    private static object Describe(string text, FontFamily family, FontWeight weight)
    {
        TextBlock block = new() { Text = text, FontFamily = family, FontWeight = weight, FontSize = 20 };
        block.Measure(new Size(2000, 2000));

        List<object> runs = [];
        foreach (TextLine line in block.TextLayout.TextLines)
        {
            foreach (TextRun run in line.TextRuns)
            {
                if (run is not ShapedTextRun shaped) continue;
                runs.Add(new
                {
                    text = shaped.Text.Span.ToString(),
                    typeface = shaped.GlyphRun.GlyphTypeface.FamilyName,
                    weight = (int)shaped.GlyphRun.GlyphTypeface.Weight,
                    simulations = shaped.GlyphRun.GlyphTypeface.FontSimulations.ToString(),
                });
            }
        }

        return new { width = Math.Round(block.DesiredSize.Width, 1), runs };
    }
}
