using Microsoft.Agents.AI;
using UiharuMind.Core.AI.Execution;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 文件记忆的磁盘布局：一个角色一个目录，改名跟着搬。
/// 框架默认是每个新会话一个 <c>{timestamp}_{guid}</c> 目录——那样它就只是草稿纸，
/// 「记忆」这个词名不副实，所以目录归属由我们决定，本组测试钉住这套规则。
/// </summary>
public class FileMemoryLayoutTests : IDisposable
{
    private const string CharacterId = "1a2b3c4d5e6f7788990011223344aabb";

    private readonly string _root = Path.Combine(Path.GetTempPath(),
        "UiharuMindFileMemoryTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    /// <summary>
    /// 状态包里存 FileMemoryState 的键必须与框架一致。装配侧拿不到 provider 实例
    /// （<c>StateKeys</c> 是实例属性），只能复制一份常量——这条测试就是那份复制品的保险。
    /// 键一旦对不上，覆写会写进一个没人读的槽位，而框架照旧用它自己的会话级目录：
    /// 症状是「记忆又变成每会话一份」，且不报任何错。
    /// </summary>
    [Fact]
    public void StateKey_MatchesTheFrameworkProvider()
    {
        FileMemoryProvider provider = new(new FileSystemAgentFileStore(_root));

        Assert.Contains(FileMemoryLayout.StateKey, provider.StateKeys);
    }

    [Fact]
    public void FolderName_CombinesNameAndIdPrefix()
    {
        Assert.Equal("Kuroko_1a2b3c4d", FileMemoryLayout.GetFolderName("Kuroko", CharacterId));
    }

    /// <summary>显示名允许重复，所以名字部分不足以定位；id 后缀才是身份。</summary>
    [Fact]
    public void FolderName_KeepsIdSuffixWhenNamesCollide()
    {
        string first = FileMemoryLayout.GetFolderName("Kuroko", CharacterId);
        string second = FileMemoryLayout.GetFolderName("Kuroko", "ffffffff00000000");

        Assert.NotEqual(first, second);
    }

    /// <summary>名字里没有一个字母数字（纯标点、空串）时只剩 id 后缀，而不是一个以下划线开头的怪目录。</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!!! ???")]
    public void FolderName_FallsBackToIdSuffixWhenNameHasNothingUsable(string name)
    {
        Assert.Equal("1a2b3c4d", FileMemoryLayout.GetFolderName(name, CharacterId));
    }

    [Fact]
    public void FolderName_TruncatesLongNames()
    {
        string folder = FileMemoryLayout.GetFolderName(new string('a', 100), CharacterId);

        Assert.Equal($"{new string('a', 32)}_1a2b3c4d", folder);
    }

    /// <summary>中文名不该被过滤成空：char.IsLetterOrDigit 认中文，目录名要留得住它。</summary>
    [Fact]
    public void FolderName_KeepsCjkCharacters()
    {
        Assert.Equal("御坂美琴_1a2b3c4d", FileMemoryLayout.GetFolderName("御坂 美琴", CharacterId));
    }

    /// <summary>
    /// 改名的核心用例：旧目录里的笔记必须跟到新目录去。
    /// 对账不依赖任何"改名事件"（角色名只是个透传 setter），比的是磁盘现状与目标名。
    /// </summary>
    [Fact]
    public void Reconcile_MovesTheFolderWhenTheCharacterWasRenamed()
    {
        string old = CreateFolder("Kuroko_1a2b3c4d", "note.md");

        string folder = FileMemoryLayout.Reconcile(_root, "Misaka", CharacterId);

        Assert.Equal("Misaka_1a2b3c4d", folder);
        Assert.False(Directory.Exists(old));
        Assert.True(File.Exists(Path.Combine(_root, folder, "note.md")));
    }

    /// <summary>名字从"没有可用字符"变成有名字，也是改名（旧目录名就是裸 id 后缀）。</summary>
    [Fact]
    public void Reconcile_MovesTheIdOnlyFolderToo()
    {
        CreateFolder("1a2b3c4d", "note.md");

        string folder = FileMemoryLayout.Reconcile(_root, "Misaka", CharacterId);

        Assert.Equal("Misaka_1a2b3c4d", folder);
        Assert.True(File.Exists(Path.Combine(_root, folder, "note.md")));
    }

    [Fact]
    public void Reconcile_LeavesOtherCharactersAlone()
    {
        string other = CreateFolder("Kuroko_ffffffff", "note.md");

        FileMemoryLayout.Reconcile(_root, "Misaka", CharacterId);

        Assert.True(Directory.Exists(other));
    }

    /// <summary>框架默认留下的 {timestamp}_{guid} 目录不属于任何角色，一律不动。</summary>
    [Fact]
    public void Reconcile_IgnoresFrameworkDefaultFolders()
    {
        string legacy = CreateFolder("20260805_081538_d60d3dbe-5700-4547-9727-ab6d75881545", "note.md");

        string folder = FileMemoryLayout.Reconcile(_root, "Misaka", CharacterId);

        Assert.Equal("Misaka_1a2b3c4d", folder);
        Assert.True(Directory.Exists(legacy));
    }

    /// <summary>宁可不搬也不覆盖：目标目录已经有笔记时，旧目录留在原地等人处理。</summary>
    [Fact]
    public void Reconcile_DoesNotMergeIntoAnExistingTarget()
    {
        string old = CreateFolder("Kuroko_1a2b3c4d", "old.md");
        CreateFolder("Misaka_1a2b3c4d", "current.md");

        string folder = FileMemoryLayout.Reconcile(_root, "Misaka", CharacterId);

        Assert.Equal("Misaka_1a2b3c4d", folder);
        Assert.True(Directory.Exists(old));
        Assert.True(File.Exists(Path.Combine(_root, folder, "current.md")));
        Assert.False(File.Exists(Path.Combine(_root, folder, "old.md")));
    }

    /// <summary>同一 id 后缀命中多个旧目录时无法判断该搬哪个，一个都不搬。</summary>
    [Fact]
    public void Reconcile_MovesNothingWhenSeveralStaleFoldersMatch()
    {
        string first = CreateFolder("Kuroko_1a2b3c4d", "a.md");
        string second = CreateFolder("Saten_1a2b3c4d", "b.md");

        string folder = FileMemoryLayout.Reconcile(_root, "Misaka", CharacterId);

        Assert.Equal("Misaka_1a2b3c4d", folder);
        Assert.True(Directory.Exists(first));
        Assert.True(Directory.Exists(second));
    }

    /// <summary>没改名（甚至父目录还不存在）时对账必须是无操作。</summary>
    [Fact]
    public void Reconcile_IsANoOpWhenNothingChanged()
    {
        Assert.Equal("Misaka_1a2b3c4d", FileMemoryLayout.Reconcile(_root, "Misaka", CharacterId));

        CreateFolder("Misaka_1a2b3c4d", "note.md");

        Assert.Equal("Misaka_1a2b3c4d", FileMemoryLayout.Reconcile(_root, "Misaka", CharacterId));
        Assert.Single(Directory.EnumerateDirectories(_root));
    }

    private string CreateFolder(string folderName, string fileName)
    {
        string path = Path.Combine(_root, folderName);
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, fileName), "content");
        return path;
    }
}
