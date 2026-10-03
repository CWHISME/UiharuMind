/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using UiharuMind.Features.Conversation;
using UiharuMind.Features.Conversation.Items;
using UiharuMind.Features.Conversation.Pages;
using UiharuMind.Features.Conversation.Search;
using UiharuMind.Features.Conversation.SessionList;

namespace UiharuMind.Features.DevAutomation;

/// <summary>
/// 会话内搜索：打开搜索栏、填词、按「更早一条」若干次，报告落点。与按 Ctrl/⌘+F 再敲回车同一条路，
/// 不调模型、不改数据。作用于当前显示的会话，先 <c>session.open</c>。
/// <c>args</c>：query（关键词）、steps（按几次「更早一条」，默认 1；负数按「更新一条」，从最新绕到最早只要 -1）、
/// thinking / tools（范围开关）
/// </summary>
internal sealed class SessionSearchCommand : IAsyncDevCommand
{
    private static readonly TimeSpan SearchSettle = TimeSpan.FromMilliseconds(600); //防抖 200ms 加后台搜一遍
    private static readonly TimeSpan JumpSettle = TimeSpan.FromSeconds(2); //分批续窗加滚动落地

    public string Name => "session.search";

    public string Usage => "在当前会话里搜索并跳转。query、steps、thinking、tools";

    public async Task<object?> ExecuteAsync(JsonElement args)
    {
        ConversationViewModel conversation = DevCommandRegistry.RequireConversationPage().Conversation;
        ConversationSearchViewData search = conversation.Search;
        search.OpenCommand.Execute(null);
        if (GroupDevCommands.BoolOr(args, "thinking", false) != search.IncludeThinking) search.ToggleThinkingCommand.Execute(null);
        if (GroupDevCommands.BoolOr(args, "tools", false) != search.IncludeTools) search.ToggleToolsCommand.Execute(null);
        search.Query = DevCommandRegistry.RequireString(args, "query");
        await Task.Delay(SearchSettle);

        int steps = GroupDevCommands.IntOr(args, "steps", 1);
        for (int i = 0; i < Math.Abs(steps); i++)
        {
            (steps > 0 ? search.OlderCommand : search.NewerCommand).Execute(null);
            await Task.Delay(JumpSettle);
        }

        ConversationSearchHitRow? selected = search.SelectedIndex >= 0 ? search.Hits[search.SelectedIndex] : null;
        ConversationItemBase? target = selected == null ? null : conversation.SearchNavigator.Find(selected.Hit);
        return new
        {
            hits = search.Hits.Count,
            status = search.StatusText,
            selectedIndex = selected?.Hit.MessageIndex,
            selectedKind = selected?.Hit.Kind.ToString(),
            snippet = selected?.Snippet,
            targetDrawn = target != null,
            targetItem = target?.GetType().Name,
            items = conversation.Items.Count,
            hasEarlierMessages = conversation.HasEarlierMessages,
            labels = search.Hits.Take(5).Select(x => $"{x.Label}: {x.Snippet}").ToList(),
        };
    }
}

/// <summary>
/// 跨会话搜索：在会话列表搜索框填词、点「在消息里搜索」，等「消息里提到的」扫完，点第 result 个会话的第 hit 条摘要，报告切过去之后的落点。
/// 与在侧栏打字再点摘要同一条路，不调模型、不改数据。作用于当前类型那一侧（先 <c>page.jump</c>）。
/// <c>args</c>：query（关键词）、start（点不点「在消息里搜索」，默认 true）、result（第几个会话，默认 0；负数只搜不点）、
/// hit（第几条摘要，默认 0）
/// </summary>
internal sealed class SessionsSearchCommand : IAsyncDevCommand
{
    private static readonly TimeSpan ScanTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan OpenSettle = TimeSpan.FromSeconds(3); //装载、落位、会话内重搜再跳

    public string Name => "sessions.search";

    public string Usage => "跨会话搜索。query、start、result、hit";

    public async Task<object?> ExecuteAsync(JsonElement args)
    {
        ConversationPageDataBase page = DevCommandRegistry.RequireConversationPage();
        SessionContentSearchViewData content = page.SessionList.ContentSearch;
        page.SessionList.SearchText = DevCommandRegistry.RequireString(args, "query");
        // 与点「在消息里搜索」那一行同一条路;已开扫时是空操作。start=false 只填词(看标题过滤与那一行本身)
        if (GroupDevCommands.BoolOr(args, "start", true)) content.StartCommand.Execute(null);

        long began = Environment.TickCount64;
        await Task.WhenAny(content.Pending, Task.Delay(ScanTimeout)); //没开扫时它本来就是完成的
        long scanMs = Environment.TickCount64 - began;

        int result = GroupDevCommands.IntOr(args, "result", 0);
        int hit = GroupDevCommands.IntOr(args, "hit", 0);
        SessionContentHitRow? clicked = result >= 0 && result < content.Results.Count &&
                                        hit < content.Results[result].Hits.Count
            ? content.Results[result].Hits[hit]
            : null;
        if (clicked != null)
        {
            content.OpenCommand.Execute(clicked);
            await Task.Delay(OpenSettle);
        }

        ConversationViewModel conversation = page.Conversation;
        ConversationSearchViewData search = conversation.Search;
        ConversationSearchHitRow? selected = search.SelectedIndex >= 0 ? search.Hits[search.SelectedIndex] : null;
        return new
        {
            header = content.HeaderText,
            sessions = content.Results.Count,
            scanMs,
            top = content.Results.Take(5).Select(x => $"{x.Session.Name} ({x.CountText})").ToList(),
            clicked = clicked == null ? null : new { clicked.SessionId, clicked.Reveal.MessageIndex, clicked.Snippet },
            openedSession = conversation.CurrentMeta?.SessionId,
            listSelected = page.SessionList.SelectedSession?.SessionId,
            searchOpen = search.IsOpen,
            searchStatus = search.StatusText,
            selectedIndex = selected?.Hit.MessageIndex,
            targetDrawn = selected != null && conversation.SearchNavigator.Find(selected.Hit) != null,
            items = conversation.Items.Count,
        };
    }
}
