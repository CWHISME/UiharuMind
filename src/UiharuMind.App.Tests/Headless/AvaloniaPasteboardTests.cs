using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using UiharuMind.Features.Clipboard;

namespace UiharuMind.App.Tests.Headless;

/// <summary>
/// 剪贴板借用后还原：文字与自定义格式的字节都原样回来；原来是空的就清空
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class AvaloniaPasteboardTests
{
    [Fact]
    public void Restore_BringsBackEveryFormat() => HeadlessUi.Run(() =>
    {
        Window window = new();
        window.Show();
        IClipboard clipboard = window.Clipboard!;
        AvaloniaPasteboard pasteboard = new(clipboard);
        DataFormat<byte[]> custom = DataFormat.CreateBytesApplicationFormat("uiharu-test");
        DataTransferItem original = DataTransferItem.CreateText("原来的");
        original.Set(custom, [1, 2, 3]);
        DataTransfer data = new();
        data.Add(original);
        clipboard.SetDataAsync(data).GetAwaiter().GetResult();

        DataTransfer? saved = pasteboard.SaveAsync().GetAwaiter().GetResult();
        pasteboard.SetTextAsync("借用").GetAwaiter().GetResult();
        Assert.Equal("借用", clipboard.TryGetTextAsync().GetAwaiter().GetResult());
        pasteboard.RestoreAsync(saved).GetAwaiter().GetResult();

        Assert.Equal("原来的", clipboard.TryGetTextAsync().GetAwaiter().GetResult());
        Assert.Equal([1, 2, 3], clipboard.TryGetValueAsync(custom).GetAwaiter().GetResult());

        clipboard.ClearAsync().GetAwaiter().GetResult();
        Assert.Null(pasteboard.SaveAsync().GetAwaiter().GetResult());
        window.Close();
    });
}
