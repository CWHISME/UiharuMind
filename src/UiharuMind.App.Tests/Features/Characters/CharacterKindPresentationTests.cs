using Avalonia;
using UiharuMind.Core.AI.Character;
using UiharuMind.Features.Characters;

namespace UiharuMind.App.Tests.Features.Characters;

/// <summary>
/// 类别徽章底色是静态共用的，必须是不可变画刷：<c>SolidColorBrush</c> 是 <see cref="AvaloniaObject"/>，
/// 归第一次建它的线程，静态实例一旦先在别的线程建出来，界面渲染它就抛跨线程访问
/// （<c>WorkspaceTint</c> 的项目色踩过同一个坑）。
/// </summary>
public class CharacterKindPresentationTests
{
    [Fact]
    public void KindBrushes_AreThreadFree()
    {
        CharacterData[] kinds =
        [
            new() { CharacterName = "chat" },
            new() { CharacterName = "agent", IsAgent = true },
            new() { CharacterName = "user", IsUserCard = true },
        ];

        foreach (CharacterData kind in kinds)
        {
            Assert.IsNotAssignableFrom<AvaloniaObject>(CharacterKindPresentation.BrushOf(kind));
        }
    }
}
