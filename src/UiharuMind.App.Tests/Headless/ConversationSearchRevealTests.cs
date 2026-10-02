using Avalonia.Controls;
using Avalonia.Threading;
using Microsoft.Extensions.AI;
using UiharuMind.App.Tests.TestDoubles;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Features.Conversation;
using UiharuMind.Features.Conversation.Search;

namespace UiharuMind.App.Tests.Headless;

/// <summary>
/// 跨会话搜索点进来的那条跳转（ADR 0057）：视图模型只记下，视图落位之后取走、打开会话内搜索跳过去。
/// 三条路——装载中、已在显示、没等到视图就被切走——各自只能跳一次，切走的那条不能留到下回
/// </summary>
[Collection(HeadlessCollection.Name)]
public class ConversationSearchRevealTests
{
    private const int Target = 10;

    [Fact]
    public void RequestedWhileLoading_JumpsOnceTheViewSettles() => HeadlessUi.RunAsync(async () =>
    {
        ChatSession session = CreateSession();
        try
        {
            using ConversationViewModel vm = new(new RecordingMessageService()) { IsDisplayed = true };
            (Window window, ConversationView view) = Show();
            Task load = vm.LoadSessionAsync(SessionManager.Instance.GetMeta(session.SessionId));
            vm.RequestSearchReveal(new ConversationSearchReveal("目标", Target));
            view.DataContext = vm;
            await load;

            await WaitUntil(window, () => vm.SearchNavigator.Anchor != null);

            AssertJumpedToTarget(vm);
            window.Close();
        }
        finally
        {
            SessionManager.Instance.Delete(session.SessionId);
        }
    });

    [Fact]
    public void RequestedWhileDisplayed_JumpsRightAway() => HeadlessUi.RunAsync(async () =>
    {
        ChatSession session = CreateSession();
        try
        {
            using ConversationViewModel vm = new(new RecordingMessageService()) { IsDisplayed = true };
            (Window window, ConversationView view) = Show();
            Task load = vm.LoadSessionAsync(SessionManager.Instance.GetMeta(session.SessionId));
            view.DataContext = vm;
            await load;
            await WaitUntil(window, () => vm.Items.Count > 0);

            vm.RequestSearchReveal(new ConversationSearchReveal("目标", Target));
            await WaitUntil(window, () => vm.SearchNavigator.Anchor != null);

            AssertJumpedToTarget(vm);
            Assert.False(vm.HasPendingSearchReveal);
            window.Close();
        }
        finally
        {
            SessionManager.Instance.Delete(session.SessionId);
        }
    });

    /// <summary>没等到视图就被切走（装载中途放下）：这条作废，下回从列表正常点开不该弹搜索栏</summary>
    [Fact]
    public void SwitchedAwayBeforeTheViewTookIt_IsForgotten() => HeadlessUi.Run(() =>
    {
        using ConversationViewModel vm = new(new RecordingMessageService()) { IsDisplayed = true };

        vm.RequestSearchReveal(new ConversationSearchReveal("目标", Target));
        vm.IsDisplayed = false;

        Assert.False(vm.HasPendingSearchReveal);
    });

    private static ChatSession CreateSession()
    {
        ChatSession session = SessionManager.Instance.StartNewSession(new CharacterData { CharacterName = "reveal" });
        for (int i = 0; i < 60; i++)
        {
            string text = i is Target or 40 ? $"第 {i} 条提到了目标" : $"第 {i} 条无关";
            session.History.Add(new ChatMessage(i % 2 == 0 ? ChatRole.User : ChatRole.Assistant, text));
        }

        SessionManager.Instance.Save(session);
        return session;
    }

    private static (Window, ConversationView) Show()
    {
        ConversationView view = new();
        Window window = new() { Width = 900, Height = 700, Content = view };
        window.Show();
        return (window, view);
    }

    private static void AssertJumpedToTarget(ConversationViewModel vm)
    {
        Assert.True(vm.Search.IsOpen);
        Assert.Equal(Target, vm.Search.Hits[vm.Search.SelectedIndex].Hit.MessageIndex);
        Assert.Equal(Target, vm.SearchNavigator.Anchor!.MessageIndex);
    }

    private static async Task WaitUntil(Window window, Func<bool> condition)
    {
        for (int i = 0; i < 200 && !condition(); i++)
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            await Task.Delay(10);
        }

        Assert.True(condition(), "等到超时");
    }
}
