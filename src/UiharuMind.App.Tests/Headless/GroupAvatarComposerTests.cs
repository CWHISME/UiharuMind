using Avalonia.Media.Imaging;
using UiharuMind.Core.AI.Character;
using UiharuMind.Shared.Utils;

namespace UiharuMind.App.Tests.Headless;

/// <summary>群头像拼图：一位直接用他的头像，多位拼成一张方图并按成员缓存</summary>
[Collection(HeadlessCollection.Name)]
public class GroupAvatarComposerTests
{
    private static CharacterData Card(string id, string avatar) =>
        new() { CharacterId = id, CharacterIcon = $"avares://UiharuMind/Assets/Avatars/{avatar}.png" };

    private static readonly CharacterData[] Trio =
    [
        Card("test-kongo", "KongoMitsuko"), Card("test-uiharu", "UiharuKazari"), Card("test-accelerator", "Accelerator"),
    ];

    [Fact]
    public void SingleMember_UsesOwnAvatar() => HeadlessUi.Run(() =>
    {
        Assert.Same(IconUtils.GetCharacterBitmapOrDefault(Trio[0]), GroupAvatarComposer.Compose([Trio[0]]));
    });

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void SeveralMembers_ComposeSquareAndCache(int count) => HeadlessUi.Run(() =>
    {
        Bitmap? first = GroupAvatarComposer.Compose(Trio.Take(count).ToList());

        Assert.NotNull(first);
        Assert.Equal(first.PixelSize.Width, first.PixelSize.Height);
        Assert.NotSame(IconUtils.GetCharacterBitmapOrDefault(Trio[0]), first);
        Assert.Same(first, GroupAvatarComposer.Compose(Trio.Take(count).ToList()));
    });
}
