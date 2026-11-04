using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using UiharuMind.App.Tests.TestDoubles;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution.Extensions;
using UiharuMind.Features.Conversation;
using UiharuMind.Shared.Controls;

namespace UiharuMind.App.Tests.Headless;

/// <summary>
/// 输入框工具行那几个「状态灯」必须跟着状态<b>实时</b>变色。
///
/// 曾经的写法把颜色设在按钮里头的 <see cref="ThemedSvgIcon"/> 上：图标会把自己的颜色写成
/// <b>局部值</b>（跟随主题、置灰都靠它），而局部值优先级高于样式——样式于是只在图标建出来那一刻
/// 生效一次。表现出来就是：勾上「扩展不再询问」不变红、红着换到没勾的会话也不褪红，
/// 只有重启（图标按 Silent 重建）才对。顶着这个坑的不只扩展那一颗，权限档、模式、记忆库同理。
/// 现在颜色一律设在 <see cref="ThemedIconButton.CurrentColor"/> 上，由按钮转给图标。
/// </summary>
[Collection(HeadlessCollection.Name)]
public class ComposerStateLightTests
{
    private const string ExtensionState = "extension-state";
    private const string PermissionState = "permission-state";

    /// <summary>勾上「本会话扩展不再询问」立刻变红，取消立刻褪回主题色</summary>
    [Fact]
    public void ExtensionLight_FollowsToggle() => HeadlessUi.Run(() =>
    {
        ChatSession session = SessionManager.Instance.StartNewSession(AgentCharacter());
        try
        {
            using ConversationViewModel vm = new(new RecordingMessageService());
            Window window = Open(vm);
            vm.OnSessionCreated(session);
            Pump(window);

            Assert.NotEqual(Colors.OrangeRed, StateColor(window, ExtensionState));

            vm.ExtensionsApproved = true;
            Pump(window);
            Assert.Equal(Colors.OrangeRed, StateColor(window, ExtensionState));

            vm.ExtensionsApproved = false;
            Pump(window);
            Assert.NotEqual(Colors.OrangeRed, StateColor(window, ExtensionState));
        }
        finally
        {
            SessionManager.Instance.Delete(session.SessionId);
        }
    });

    /// <summary>换会话就换那一份名单：勾过的红、没勾的褪回主题色</summary>
    [Fact]
    public void ExtensionLight_FollowsSessionSwitch() => HeadlessUi.Run(() =>
    {
        ChatSession approved = SessionManager.Instance.StartNewSession(AgentCharacter());
        ChatSession plain = SessionManager.Instance.StartNewSession(AgentCharacter());
        try
        {
            approved.SetSessionApprovedExtension(ExtensionCalls.AllExtensions, true);

            using ConversationViewModel vm = new(new RecordingMessageService());
            Window window = Open(vm);

            vm.OnSessionCreated(approved);
            Pump(window);
            Assert.Equal(Colors.OrangeRed, StateColor(window, ExtensionState));

            vm.OnSessionCreated(plain);
            Pump(window);
            Assert.NotEqual(Colors.OrangeRed, StateColor(window, ExtensionState));

            vm.OnSessionCreated(approved);
            Pump(window);
            Assert.Equal(Colors.OrangeRed, StateColor(window, ExtensionState));
        }
        finally
        {
            SessionManager.Instance.Delete(approved.SessionId);
            SessionManager.Instance.Delete(plain.SessionId);
        }
    });

    /// <summary>权限档那颗共用同一条上色通道，档位一换颜色就跟着换</summary>
    [Fact]
    public void PermissionLight_FollowsPermissionMode() => HeadlessUi.Run(() =>
    {
        using ConversationViewModel vm = new(new RecordingMessageService());
        Window window = Open(vm);

        (int Index, Color Expected)[] cases =
        [
            (0, Colors.Gray),
            (2, Colors.OrangeRed),
            (1, Colors.Orange),
        ];

        foreach ((int index, Color expected) in cases)
        {
            vm.PermissionModeIndex = index;
            Pump(window);
            Assert.Equal(expected, StateColor(window, PermissionState));
        }
    });

    private static CharacterData AgentCharacter() => new() { CharacterName = "state-light", IsAgent = true };

    private static Window Open(ConversationViewModel vm)
    {
        Window window = new() { Width = 900, Height = 640, Content = new ConversationView { DataContext = vm } };
        window.Show();
        Pump(window);
        return window;
    }

    private static void Pump(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    // 读的是图标最终上色：状态灯到底变没变，只有它说了算
    private static Color StateColor(Window window, string stateClass)
    {
        ThemedIconButton button = window.GetVisualDescendants().OfType<ThemedIconButton>()
            .First(x => x.Classes.Contains(stateClass));
        ThemedSvgIcon icon = (ThemedSvgIcon)button.Icon!;
        return (Color)icon.GetValue(Avalonia.Svg.Skia.Svg.CurrentColorProperty)!;
    }
}
