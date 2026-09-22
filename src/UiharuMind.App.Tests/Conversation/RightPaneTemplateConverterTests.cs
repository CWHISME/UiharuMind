using Avalonia.Controls;
using Avalonia.Controls.Templates;
using UiharuMind.Features.Conversation.Pages;

namespace UiharuMind.App.Tests.Conversation;

/// <summary>
/// 右栏模板选择：状态与模板一一对应，未知值回落到空态（先保证有东西可点）
/// </summary>
public class RightPaneTemplateConverterTests
{
    private sealed class StubTemplate : IDataTemplate
    {
        public Control? Build(object? param) => new Border();

        public bool Match(object? data) => true;
    }

    private static (RightPaneTemplateConverter Converter, StubTemplate Chat, StubTemplate Agent,
            StubTemplate NewSession)
        Create()
    {
        StubTemplate chat = new();
        StubTemplate agent = new();
        StubTemplate session = new();
        RightPaneTemplateConverter converter = new() { Chat = chat, Agent = agent, NewSession = session };
        return (converter, chat, agent, session);
    }

    [Fact]
    public void Chat_SelectsChatTemplate()
    {
        (RightPaneTemplateConverter converter, StubTemplate chat, _, _) = Create();

        Assert.Same(chat, converter.Convert(ERightPaneKind.Chat, typeof(IDataTemplate), null,
            System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Agent_SelectsAgentTemplate()
    {
        (RightPaneTemplateConverter converter, _, StubTemplate agent, _) = Create();

        Assert.Same(agent, converter.Convert(ERightPaneKind.Agent, typeof(IDataTemplate), null,
            System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void NewSession_SelectsNewSessionTemplate()
    {
        (RightPaneTemplateConverter converter, _, _, StubTemplate session) = Create();

        Assert.Same(session, converter.Convert(ERightPaneKind.NewSession, typeof(IDataTemplate), null,
            System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void UnknownValue_FallsBackToNewSessionTemplate()
    {
        (RightPaneTemplateConverter converter, _, _, StubTemplate session) = Create();

        Assert.Same(session, converter.Convert(null, typeof(IDataTemplate), null,
            System.Globalization.CultureInfo.InvariantCulture));
        Assert.Same(session, converter.Convert(999, typeof(IDataTemplate), null,
            System.Globalization.CultureInfo.InvariantCulture));
    }
}
