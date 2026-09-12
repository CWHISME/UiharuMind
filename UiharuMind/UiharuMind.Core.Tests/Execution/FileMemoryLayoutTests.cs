using Microsoft.Agents.AI;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Tools.Memory;

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
    public void FolderName_CombinesNameAndId()
    {
        Assert.Equal("Kuroko_1a2b3c4d5e6f7788990011223344aabb", FileMemoryLayout.GetFolderName("Kuroko", CharacterId));
    }

    /// <summary>
    /// id 后缀<b>不能截断</b>。内置角色的 CharacterId 是枚举名（见 <c>DefaultCharacterManager</c>），
    /// 而它们大量共享前缀：Assistant / AssistantExplain / AssistantExpert…、
    /// Roleplay_FirstPerson / Roleplay_ThirdPerson、Translator / TranslatorAdvanced。
    /// 一旦后缀只取前几位，这些角色就会互相被认作"改名前的自己"，
    /// 对账会把别人的笔记目录搬到自己名下——这曾经是真的。
    /// </summary>
    [Theory]
    [InlineData("Assistant", "AssistantExplain")]
    [InlineData("Assistant", "AssistantSyntacticAnalysis")]
    [InlineData("Roleplay_FirstPerson", "Roleplay_ThirdPerson")]
    [InlineData("Translator", "TranslatorAdvanced")]
    public void FolderName_KeepsBuiltInIdsApartDespiteSharedPrefixes(string firstId, string secondId)
    {
        Assert.NotEqual(FileMemoryLayout.GetFolderName("Uiharu", firstId),
            FileMemoryLayout.GetFolderName("Uiharu", secondId));
    }

    /// <summary>共前缀的两个内置角色之间，对账也绝不能把对方的目录认成自己的。</summary>
    [Fact]
    public void Reconcile_DoesNotClaimAFolderOfAnIdThatSharesItsPrefix()
    {
        string other = CreateFolder("Uiharu_AssistantExplain", "note.md");

        string folder = FileMemoryLayout.Reconcile(_root, "Uiharu", "Assistant");

        Assert.Equal("Uiharu_Assistant", folder);
        Assert.True(Directory.Exists(other));
        Assert.False(File.Exists(Path.Combine(_root, folder, "note.md")));
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
        Assert.Equal("1a2b3c4d5e6f7788990011223344aabb", FileMemoryLayout.GetFolderName(name, CharacterId));
    }

    [Fact]
    public void FolderName_TruncatesLongNames()
    {
        string folder = FileMemoryLayout.GetFolderName(new string('a', 100), CharacterId);

        Assert.Equal($"{new string('a', 32)}_1a2b3c4d5e6f7788990011223344aabb", folder);
    }

    /// <summary>中文名不该被过滤成空：char.IsLetterOrDigit 认中文，目录名要留得住它。</summary>
    [Fact]
    public void FolderName_KeepsCjkCharacters()
    {
        Assert.Equal("御坂美琴_1a2b3c4d5e6f7788990011223344aabb", FileMemoryLayout.GetFolderName("御坂 美琴", CharacterId));
    }

    /// <summary>
    /// 改名的核心用例：旧目录里的笔记必须跟到新目录去。
    /// 对账不依赖任何"改名事件"（角色名只是个透传 setter），比的是磁盘现状与目标名。
    /// </summary>
    [Fact]
    public void Reconcile_MovesTheFolderWhenTheCharacterWasRenamed()
    {
        string old = CreateFolder("Kuroko_1a2b3c4d5e6f7788990011223344aabb", "note.md");

        string folder = FileMemoryLayout.Reconcile(_root, "Misaka", CharacterId);

        Assert.Equal("Misaka_1a2b3c4d5e6f7788990011223344aabb", folder);
        Assert.False(Directory.Exists(old));
        Assert.True(File.Exists(Path.Combine(_root, folder, "note.md")));
    }

    /// <summary>名字从"没有可用字符"变成有名字，也是改名（旧目录名就是裸 id 后缀）。</summary>
    [Fact]
    public void Reconcile_MovesTheIdOnlyFolderToo()
    {
        CreateFolder("1a2b3c4d5e6f7788990011223344aabb", "note.md");

        string folder = FileMemoryLayout.Reconcile(_root, "Misaka", CharacterId);

        Assert.Equal("Misaka_1a2b3c4d5e6f7788990011223344aabb", folder);
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

        Assert.Equal("Misaka_1a2b3c4d5e6f7788990011223344aabb", folder);
        Assert.True(Directory.Exists(legacy));
    }

    /// <summary>宁可不搬也不覆盖：目标目录已经有笔记时，旧目录留在原地等人处理。</summary>
    [Fact]
    public void Reconcile_DoesNotMergeIntoAnExistingTarget()
    {
        string old = CreateFolder("Kuroko_1a2b3c4d5e6f7788990011223344aabb", "old.md");
        CreateFolder("Misaka_1a2b3c4d5e6f7788990011223344aabb", "current.md");

        string folder = FileMemoryLayout.Reconcile(_root, "Misaka", CharacterId);

        Assert.Equal("Misaka_1a2b3c4d5e6f7788990011223344aabb", folder);
        Assert.True(Directory.Exists(old));
        Assert.True(File.Exists(Path.Combine(_root, folder, "current.md")));
        Assert.False(File.Exists(Path.Combine(_root, folder, "old.md")));
    }

    /// <summary>同一 id 后缀命中多个旧目录时无法判断该搬哪个，一个都不搬。</summary>
    [Fact]
    public void Reconcile_MovesNothingWhenSeveralStaleFoldersMatch()
    {
        string first = CreateFolder("Kuroko_1a2b3c4d5e6f7788990011223344aabb", "a.md");
        string second = CreateFolder("Saten_1a2b3c4d5e6f7788990011223344aabb", "b.md");

        string folder = FileMemoryLayout.Reconcile(_root, "Misaka", CharacterId);

        Assert.Equal("Misaka_1a2b3c4d5e6f7788990011223344aabb", folder);
        Assert.True(Directory.Exists(first));
        Assert.True(Directory.Exists(second));
    }

    /// <summary>没改名（甚至父目录还不存在）时对账必须是无操作。</summary>
    [Fact]
    public void Reconcile_IsANoOpWhenNothingChanged()
    {
        Assert.Equal("Misaka_1a2b3c4d5e6f7788990011223344aabb", FileMemoryLayout.Reconcile(_root, "Misaka", CharacterId));

        CreateFolder("Misaka_1a2b3c4d5e6f7788990011223344aabb", "note.md");

        Assert.Equal("Misaka_1a2b3c4d5e6f7788990011223344aabb", FileMemoryLayout.Reconcile(_root, "Misaka", CharacterId));
        Assert.Single(Directory.EnumerateDirectories(_root));
    }

    /// <summary>
    /// 工作区键必须<b>跨进程稳定</b>。曾经的坑候选是 <c>string.GetHashCode</c>——它每进程随机化，
    /// 重启一次就换一个目录名，表现是「记忆莫名其妙丢了，但文件还在磁盘上」。
    /// </summary>
    [Fact]
    public void WorkspaceSegment_IsStableAndCarriesTheFolderName()
    {
        string segment = FileMemoryLayout.GetWorkspaceSegment("/Users/me/projects/client");

        Assert.Equal(segment, FileMemoryLayout.GetWorkspaceSegment("/Users/me/projects/client"));
        Assert.StartsWith("client_", segment);
    }

    /// <summary>
    /// 同名不同路径的两个项目必须落到不同的键：只用目录名的话「两个都叫 client」会共用一份记忆，
    /// 那就是跨项目污染——而按项目隔离正是这一档存在的理由。
    /// </summary>
    [Fact]
    public void WorkspaceSegment_DistinguishesSameNamedProjects()
    {
        Assert.NotEqual(
            FileMemoryLayout.GetWorkspaceSegment("/Users/me/a/client"),
            FileMemoryLayout.GetWorkspaceSegment("/Users/me/b/client"));
    }

    /// <summary>
    /// 同一个工作区经不同写法进来必须归一到同一个键，否则一个项目会分裂成几份记忆。
    /// Linux 之外的文件系统不区分大小写，故大小写也要归一。
    /// </summary>
    [Fact]
    public void WorkspaceSegment_NormalizesTrailingSeparator()
    {
        Assert.Equal(
            FileMemoryLayout.GetWorkspaceSegment("/Users/me/projects/client"),
            FileMemoryLayout.GetWorkspaceSegment("/Users/me/projects/client/"));
    }

    /// <summary>项目级：在角色目录<b>下面</b>再多一段，角色隔离这条性质不能丢（ADR 0002）</summary>
    [Fact]
    public void Reconcile_NestsWorkspaceUnderTheCharacterFolder()
    {
        CharacterData character = NewCharacter(EFileMemoryScope.Workspace);

        string folder = FileMemoryLayout.Reconcile(_root, character, "/Users/me/projects/client");

        Assert.StartsWith($"Misaka_{CharacterId}/", folder);
        Assert.Contains("client_", folder);
    }

    /// <summary>
    /// 选了项目级却没绑工作区：回落角色级。落进 Scratch 会是「记了但会没」，
    /// 而那是可丢弃的缓存树（ADR 0013）。
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Reconcile_FallsBackToCharacterScopeWithoutWorkspace(string? workspacePath)
    {
        CharacterData character = NewCharacter(EFileMemoryScope.Workspace);

        Assert.Equal($"Misaka_{CharacterId}", FileMemoryLayout.Reconcile(_root, character, workspacePath));
    }

    /// <summary>角色级下即便绑了工作区也不该分目录——那是现存记忆所在的那一层</summary>
    [Fact]
    public void Reconcile_IgnoresWorkspaceInCharacterScope()
    {
        CharacterData character = NewCharacter(EFileMemoryScope.Character);

        Assert.Equal($"Misaka_{CharacterId}",
            FileMemoryLayout.Reconcile(_root, character, "/Users/me/projects/client"));
    }

    /// <summary>
    /// 数条数要排掉框架的两类内部文件（描述侧车与索引本身）。
    /// 不排的话 50 条记忆在磁盘上是 101 个文件，界面提示直接错一倍。
    /// </summary>
    [Fact]
    public void CountMemories_ExcludesFrameworkInternalFiles()
    {
        string folder = CreateFolder($"Misaka_{CharacterId}", "notes.md");
        File.WriteAllText(Path.Combine(folder, "notes_description.md"), "desc");
        File.WriteAllText(Path.Combine(folder, "memories.md"), "# Memory Index");
        File.WriteAllText(Path.Combine(folder, "prefs.md"), "content");

        Assert.Equal(2, FileMemoryLayout.CountMemories(_root, NewCharacter(EFileMemoryScope.Character)));
    }

    /// <summary>目录还不存在时是 0 条，不是异常——编辑页每次打开都会问这个数</summary>
    [Fact]
    public void CountMemories_IsZeroWhenNothingWrittenYet()
    {
        Assert.Equal(0, FileMemoryLayout.CountMemories(_root, NewCharacter(EFileMemoryScope.Character)));
    }

    private static CharacterData NewCharacter(EFileMemoryScope scope)
    {
        CharacterData character = new() { CharacterId = CharacterId, Kind = ECharacterKind.Agent };
        character.CharacterName = "Misaka";
        character.Tools.FileMemoryScope = scope;
        return character;
    }

    private string CreateFolder(string folderName, string fileName)
    {
        string path = Path.Combine(_root, folderName);
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, fileName), "content");
        return path;
    }
}
