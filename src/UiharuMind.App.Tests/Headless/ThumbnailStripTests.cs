using Microsoft.Extensions.AI;
using SkiaSharp;
using UiharuMind.Features.Conversation.Items;

namespace UiharuMind.App.Tests.Headless;

/// <summary>
/// 条目上的缩略图：后台只解到缩略图大小，原图点开时从来源现解；条目被裁掉后晚到的结果不再挂上
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class ThumbnailStripTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "uiharu-thumbs-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static byte[] Png(int width, int height)
    {
        using SKBitmap bitmap = new(width, height);
        using SKData data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    [Fact]
    public void DecodesBytesAndFiles_ToThumbnailWidth_KeepingTheOriginalForPreview() => HeadlessUi.RunAsync(async () =>
    {
        Directory.CreateDirectory(_root);
        string file = Path.Combine(_root, "a.png");
        await File.WriteAllBytesAsync(file, Png(1200, 600));
        ThumbnailStrip strip = new();

        await strip.LoadAsync([ImageThumbnailSource.FromBytes(Png(1600, 800)), ImageThumbnailSource.FromFile(file)], 400);

        Assert.Equal([400, 400], strip.Items.Select(t => t.Bitmap.PixelSize.Width));
        Assert.Equal(file, strip.Items[1].FilePath);
        using Avalonia.Media.Imaging.Bitmap full = strip.Items[0].DecodeFull();
        Assert.Equal(1600, full.PixelSize.Width);
    });

    [Fact]
    public void ReleasedBeforeDecodeFinishes_DropsTheLateThumbnails() => HeadlessUi.RunAsync(async () =>
    {
        ThumbnailStrip strip = new();

        Task loading = strip.LoadAsync([ImageThumbnailSource.FromBytes(Png(800, 800))], 400);
        strip.Release();
        await loading;

        Assert.Empty(strip.Items);
    });

    [Fact]
    public void UnreadableSources_AreSkipped() => HeadlessUi.RunAsync(async () =>
    {
        ThumbnailStrip strip = new();

        await strip.LoadAsync(
        [
            ImageThumbnailSource.FromBytes("not an image"u8.ToArray()),
            ImageThumbnailSource.FromFile(Path.Combine(_root, "gone.png")),
            ImageThumbnailSource.FromBytes(Png(10, 10)),
        ], 400);

        Assert.Single(strip.Items);
    });

    /// <summary>用户气泡：「有没有图」按来源同步给出（布局不等解码），驻留的是缩略图而不是原图</summary>
    [Fact]
    public void UserBubble_KnowsItHasImagesAtOnce_AndHoldsOnlyThumbnails() => HeadlessUi.RunAsync(async () =>
    {
        ChatMessage source = new(ChatRole.User,
            [new DataContent(Png(1568, 1000), "image/png"), new DataContent(Png(1568, 1000), "image/png"), new TextContent("看")]);

        TextConversationItem bubble = ConversationItemFactory.CreateUser("看", source);

        Assert.True(bubble.HasImage);
        Assert.Equal(160, bubble.ImageThumbSize);
        for (int i = 0; i < 500 && bubble.MessageImages.Count < 2; i++) await Task.Delay(10);
        Assert.All(bubble.MessageImages, t => Assert.True(t.Bitmap.PixelSize.Width <= 640));
        Assert.Equal(2, bubble.MessageImages.Count);

        bubble.ReleaseImages();
        Assert.False(bubble.HasImage);
    });
}
