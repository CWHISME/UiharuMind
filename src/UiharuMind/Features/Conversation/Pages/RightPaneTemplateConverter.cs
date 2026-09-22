using System;
using System.Globalization;
using Avalonia.Controls.Templates;
using Avalonia.Data.Converters;

namespace UiharuMind.Features.Conversation.Pages;

/// <summary>
/// 按 <see cref="ERightPaneKind"/> 选右栏模板。模板的 <c>DataContext</c> 仍是
/// <see cref="ConversationPageData"/> 本体（<c>ContentControl.Content</c> 直接绑页面），
/// 这里只换模板不换数据——面板内部的绑定与
/// <c>$parent…((ConversationPageData)DataContext)</c> 强转都原样有效
/// </summary>
public class RightPaneTemplateConverter : IValueConverter
{
    /// <summary>普通对话面板模板</summary>
    public IDataTemplate? Chat { get; set; }

    /// <summary>智能体面板模板</summary>
    public IDataTemplate? Agent { get; set; }

    /// <summary>空态新建卡模板</summary>
    public IDataTemplate? NewSession { get; set; }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is ERightPaneKind kind
            ? kind switch
            {
                ERightPaneKind.Chat => Chat,
                ERightPaneKind.Agent => Agent,
                _ => NewSession,
            }
            : NewSession;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
