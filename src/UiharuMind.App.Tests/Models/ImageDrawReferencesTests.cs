using System;
using System.IO;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using UiharuMind.App.Tests.Headless;
using UiharuMind.Features.Models.ImageModels;
using Xunit;

namespace UiharuMind.App.Tests.Models;

/// <summary>
/// 画图窗的粘贴参考图：编码 PNG 落盘后追加到末尾（与拖放一致），
/// 临时文件在移除参考图或窗口关闭（Dispose）时删除；拖放进来的文件不碰。
/// 位图编码/解码依赖渲染平台，故走无头界面线程。
/// </summary>
[Collection(HeadlessCollection.Name)]
public class ImageDrawReferencesTests
{
    [Fact]
    public void AddPastedBitmap_PersistsPngAndAddsReference() => HeadlessUi.RunAsync(async () =>
    {
        string dir = NewTestDir();
        try
        {
            ImageDrawReferences references = new();
            using Bitmap bitmap = CreateBitmap(32, 32);

            bool added = await references.AddPastedBitmapAsync(bitmap, dir);

            Assert.True(added);
            Assert.Single(references.Items);
            Assert.True(references.HasItems);
            Assert.StartsWith("pasted_", references.Items[0].FileName);
            Assert.EndsWith(".png", references.Items[0].Path);
            Assert.True(references.Items[0].IsPasted);
            Assert.True(File.Exists(references.Items[0].Path));

            var (inputs, error) = await references.LoadAsync();
            Assert.Null(error);
            Assert.Single(inputs);
            references.Dispose();
        }
        finally
        {
            DeleteDir(dir);
        }
    });

    [Fact]
    public void AddPastedBitmap_AppendsToEnd_LikeDragDrop() => HeadlessUi.RunAsync(async () =>
    {
        string dir = NewTestDir();
        try
        {
            Directory.CreateDirectory(dir);
            string first = Path.Combine(dir, "first.png");
            File.WriteAllBytes(first, TinyPng());
            ImageDrawReferences references = new();
            await references.AddAsync([first]);

            using Bitmap bitmap = CreateBitmap(16, 16);
            await references.AddPastedBitmapAsync(bitmap, dir);

            Assert.Equal(2, references.Items.Count);
            Assert.Equal("first.png", references.Items[0].FileName); // 原参考图还在前
            Assert.StartsWith("pasted_", references.Items[1].FileName); // 粘贴的追加到末尾
            references.Dispose();
        }
        finally
        {
            DeleteDir(dir);
        }
    });

    [Fact]
    public void Remove_DeletesPastedFile() => HeadlessUi.RunAsync(async () =>
    {
        string dir = NewTestDir();
        try
        {
            ImageDrawReferences references = new();
            using Bitmap bitmap = CreateBitmap(16, 16);
            await references.AddPastedBitmapAsync(bitmap, dir);
            string path = references.Items[0].Path;
            Assert.True(File.Exists(path));

            references.RemoveCommand.Execute(references.Items[0]);

            Assert.Empty(references.Items);
            Assert.False(File.Exists(path));
            references.Dispose();
        }
        finally
        {
            DeleteDir(dir);
        }
    });

    [Fact]
    public void Dispose_DeletesRemainingPastedFiles() => HeadlessUi.RunAsync(async () =>
    {
        string dir = NewTestDir();
        try
        {
            ImageDrawReferences references = new();
            using Bitmap bitmap = CreateBitmap(16, 16);
            await references.AddPastedBitmapAsync(bitmap, dir);
            string path = references.Items[0].Path;

            references.Dispose(); // 窗口关闭走这里

            Assert.False(File.Exists(path));
        }
        finally
        {
            DeleteDir(dir);
        }
    });

    [Fact]
    public void Remove_KeepsUserDroppedFile() => HeadlessUi.RunAsync(async () =>
    {
        string dir = NewTestDir();
        try
        {
            Directory.CreateDirectory(dir);
            string dropped = Path.Combine(dir, "dropped.png");
            File.WriteAllBytes(dropped, TinyPng());
            ImageDrawReferences references = new();
            await references.AddAsync([dropped]);
            Assert.False(references.Items[0].IsPasted);

            references.RemoveCommand.Execute(references.Items[0]);

            Assert.Empty(references.Items);
            Assert.True(File.Exists(dropped)); // 拖放进来的文件属于用户，不删
            references.Dispose();
        }
        finally
        {
            DeleteDir(dir);
        }
    });

    private static string NewTestDir() =>
        Path.Combine(Path.GetTempPath(), "uiharu-ref-test", Guid.NewGuid().ToString("N"));

    private static void DeleteDir(string dir)
    {
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }

    private static Bitmap CreateBitmap(int width, int height) =>
        new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);

    private static byte[] TinyPng()
    {
        using WriteableBitmap bitmap = new(new PixelSize(2, 2), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using MemoryStream stream = new();
        bitmap.Save(stream);
        return stream.ToArray();
    }
}
