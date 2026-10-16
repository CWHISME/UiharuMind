using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Avalonia.Threading;
using UiharuMind.Features.Conversation;
using UiharuMind.Features.Conversation.Items;

namespace UiharuMind.Features.DevAutomation;

/// <summary>
/// 往当前会话流式追加假条目，模拟直播累积（内存快照的 B 路径用：不调模型、不写历史）。
/// 每批条目间夹一段节拍等待，贴近流式到达时 Skia 缓冲反复分配的真实路径——
/// 一次性灌入再多条目只是「更多条目的打开」，量不到反复分配，见快照执行单。
/// 条目形态贴近真实一次回复（用户消息 / 思考段 / 工具卡 / 助手正文混合）。
/// args：count（总条目数）、batch（每批条数，默认 8）、gapMs（批间等待毫秒，默认 300）
/// </summary>
internal sealed class SessionInjectCommand : IAsyncDevCommand
{
    public string Name => "session.inject";

    public string Usage => "往 session.new 盯住的会话流式追加假条目（不调模型、不写历史），模拟直播累积。" +
                           "count、batch（每批条数，默认 8）、gapMs（批间等待毫秒，默认 300）";

    public async Task<JsonNode?> ExecuteAsync(JsonElement args)
    {
        int count = GroupDevCommands.IntOr(args, "count", 0);
        if (count <= 0) throw new ArgumentException("session.inject needs count > 0");

        int batch = GroupDevCommands.IntOr(args, "batch", 8);
        if (batch <= 0) batch = 8;

        int gapMs = GroupDevCommands.IntOr(args, "gapMs", 300);

        ConversationViewModel conversation = SessionDevCommands.Active;
        int injected = 0;
        while (injected < count)
        {
            int take = Math.Min(batch, count - injected);
            // Items 是界面绑定集合，追加必须在界面线程。Avalonia 的调度上下文在 await 后保持回流，
            // 这里显式包一层双保险（节拍后的续体若丢了上下文，追加不会落到别的线程）
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                for (int i = 0; i < take; i++) conversation.Items.Add(MakeItem(injected + i));
            });

            injected += take;
            if (injected < count && gapMs > 0) await Task.Delay(gapMs);
        }

        return new JsonObject { ["injected"] = injected };
    }

    /// <summary>贴近真实一次回复的条目形态：用户消息 / 思考段 / 工具卡 / 助手正文</summary>
    private static ConversationItemBase MakeItem(int index)
    {
        return (index % 4) switch
        {
            0 => ConversationItemFactory.CreateUser($"注入的第 {index} 步"),
            1 => new ThinkingItem { Message = $"想一想 {index}", IsDone = true },
            2 => new ToolCallItem
            {
                CallId = $"inject-{index}",
                ToolName = "Read",
                ArgumentSummary = $"src/file{index}.cs",
                ResultText = string.Join('\n', Enumerable.Range(0, 20).Select(x => $"line {x}")),
                IsRunning = false,
            },
            _ => new TextConversationItem(false) { SenderName = "助手", Message = $"第 {index} 步完成。" },
        };
    }
}
