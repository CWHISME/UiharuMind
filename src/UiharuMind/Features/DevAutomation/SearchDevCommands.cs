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
using UiharuMind.Features.Conversation.Search;

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
