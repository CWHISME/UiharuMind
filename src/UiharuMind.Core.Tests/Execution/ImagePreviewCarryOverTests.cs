using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 分叉会话时预览图要跟着历史走。
///
/// 草稿目录按会话分房间，而历史里写的是 <c>$DRAFT/images/previews/&lt;hash&gt;.png</c>：
/// 不把文件复制进新房间，投影就找不到它，那条消息会静默降级成
/// <c>[Image no longer available]</c>——父会话是图、分叉是占位，前缀从那里起永久断
/// （实测分叉会话的缓存停在第一张图之前，16384 tokens）。
/// </summary>
public class ImagePreviewCarryOverTests
{
    [Fact]
    public void Previews_AreCopiedIntoTheNewRoom()
    {
        using TempRooms rooms = new();
        string preview = Path.Combine(rooms.From, "images", "previews", "abc.png");
        Directory.CreateDirectory(Path.GetDirectoryName(preview)!);
        File.WriteAllBytes(preview, [1, 2, 3]);

        int copied = ImagePreview.CarryOverPreviews(History("$DRAFT/images/previews/abc.png"),
            rooms.From, rooms.To);

        Assert.Equal(1, copied);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(Path.Combine(rooms.To, "images", "previews", "abc.png")));
    }

    /// <summary>同一间房间（子会话、群成员都落在壳会话那一间）不必复制，也不能复制</summary>
    [Fact]
    public void SameRoom_IsLeftAlone()
    {
        using TempRooms rooms = new();

        Assert.Equal(0, ImagePreview.CarryOverPreviews(History("$DRAFT/images/previews/abc.png"),
            rooms.From, rooms.From));
    }

    /// <summary>文件已经不在了就跳过：复制不出图，也不该因此失败</summary>
    [Fact]
    public void MissingSource_IsSkipped()
    {
        using TempRooms rooms = new();

        Assert.Equal(0, ImagePreview.CarryOverPreviews(History("$DRAFT/images/previews/gone.png"),
            rooms.From, rooms.To));
        Assert.False(Directory.Exists(Path.Combine(rooms.To, "images")));
    }

    /// <summary>绝对路径落在原房间里也认（老结果可能写成绝对路径）</summary>
    [Fact]
    public void AbsolutePathInsideTheOldRoom_IsCopied()
    {
        using TempRooms rooms = new();
        string preview = Path.Combine(rooms.From, "images", "previews", "abs.png");
        Directory.CreateDirectory(Path.GetDirectoryName(preview)!);
        File.WriteAllBytes(preview, [9]);

        int copied = ImagePreview.CarryOverPreviews(History(preview), rooms.From, rooms.To);

        Assert.Equal(1, copied);
        Assert.True(File.Exists(Path.Combine(rooms.To, "images", "previews", "abs.png")));
    }

    private static List<ChatMessage> History(string previewPath) =>
    [
        new(ChatRole.Tool, [new FunctionResultContent("call_1",
            $"Attached: a.png\nPreview: {previewPath}\nPreview size: 8x8\nThe image follows in the next message.")]),
    ];

    /// <summary>两个临时的"会话房间"</summary>
    private sealed class TempRooms : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "uiharu-rooms-" + Guid.NewGuid().ToString("N"));

        public string From => Path.Combine(_root, "from");

        public string To => Path.Combine(_root, "to");

        public TempRooms()
        {
            Directory.CreateDirectory(From);
            Directory.CreateDirectory(To);
        }

        public void Dispose()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
    }
}
