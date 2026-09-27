/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using UiharuMind.Core.AI.Execution;

namespace UiharuMind.Features.Conversation;

/// <summary>
/// todo 侧栏显示项
/// </summary>
public class TodoDisplayItem
{
    /// <summary>内容描述</summary>
    public string Content { get; }

    /// <summary>状态图形符号</summary>
    public string StatusGlyph { get; }

    /// <summary>是否已完成(删除线样式)</summary>
    public bool IsCompleted { get; }

    public TodoDisplayItem(TodoSnapshot todo)
    {
        Content = todo.Title;
        IsCompleted = todo.IsComplete;
        StatusGlyph = todo.IsComplete ? "✓" : "○";
    }
}
