using System;
using Avalonia.Controls;
using Avalonia.Input;

namespace UiharuMind.Features.Audio;

/// <summary>
/// 听写去向：一个文本框。文字插到光标处（有选中就替换选中），获得焦点时登记成按住说话的去向，卸载时注销
/// </summary>
public sealed class TextBoxDictationTarget : IDictationTarget, IDisposable
{
    private readonly TextBox _textBox;
    private readonly IDictationService _service;

    /// <param name="textBox">文本框</param>
    /// <param name="service">听写服务</param>
    public TextBoxDictationTarget(TextBox textBox, IDictationService service)
    {
        _textBox = textBox;
        _service = service;
        textBox.GotFocus += OnGotFocus;
        if (textBox.IsFocused) service.Focus(this);
    }

    public bool IsAvailable => _textBox.IsEffectivelyVisible && _textBox.IsEffectivelyEnabled &&
                               TopLevel.GetTopLevel(_textBox) is Window { IsActive: true };

    public void Insert(string text)
    {
        string current = _textBox.Text ?? "";
        int start = Math.Clamp(Math.Min(_textBox.SelectionStart, _textBox.SelectionEnd), 0, current.Length);
        int end = Math.Clamp(Math.Max(_textBox.SelectionStart, _textBox.SelectionEnd), start, current.Length);
        string inserted = NeedsSpace(current, start, text) ? " " + text : text;
        _textBox.Text = current[..start] + inserted + current[end..];
        _textBox.SelectionStart = _textBox.SelectionEnd = _textBox.CaretIndex = start + inserted.Length;
        _textBox.Focus();
    }

    public void Dispose()
    {
        _textBox.GotFocus -= OnGotFocus;
        _service.Forget(this);
    }

    /// <summary>
    /// 英文接在英文后面要隔个空格；中文不用
    /// </summary>
    /// <param name="current">原文</param>
    /// <param name="position">插入点</param>
    /// <param name="text">要插的文字</param>
    /// <returns>要加空格为 true</returns>
    public static bool NeedsSpace(string current, int position, string text) =>
        position > 0 && text.Length > 0 && char.IsAsciiLetterOrDigit(current[position - 1]) &&
        char.IsAsciiLetterOrDigit(text[0]);

    private void OnGotFocus(object? sender, FocusChangedEventArgs e) => _service.Focus(this);
}
