using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat;

namespace UiharuMind.Core.Tests.Chat;

/// <summary>
/// <see cref="SessionManager.GetLoaded"/>：界面壳高频取自己那一份本体用的只读入口。
/// 与 <see cref="SessionManager.Load"/> 的区别只在副作用——不读盘、不触发冷历史卸载（那一步可能同步写盘）
/// </summary>
public class SessionManagerGetLoadedTests
{
    [Fact]
    public void ReturnsTheSameInstanceAsLoad()
    {
        ChatSession session = SessionManager.Instance.StartNewSession(new CharacterData { CharacterName = "G" });
        try
        {
            Assert.Same(session, SessionManager.Instance.GetLoaded(session.SessionId));
            Assert.Same(SessionManager.Instance.Load(session.SessionId), SessionManager.Instance.GetLoaded(session.SessionId));
        }
        finally
        {
            SessionManager.Instance.Delete(session.SessionId);
        }
    }

    [Fact]
    public void ReturnsNullOnceDeleted()
    {
        ChatSession session = SessionManager.Instance.StartNewSession(new CharacterData { CharacterName = "G" });
        SessionManager.Instance.Delete(session.SessionId);

        Assert.Null(SessionManager.Instance.GetLoaded(session.SessionId));
    }

    [Fact]
    public void DoesNotLoadFromDisk()
    {
        Assert.Null(SessionManager.Instance.GetLoaded(Guid.NewGuid().ToString("N")));
        Assert.Null(SessionManager.Instance.GetLoaded(""));
    }
}
