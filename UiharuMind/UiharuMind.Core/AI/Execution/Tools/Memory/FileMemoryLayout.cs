/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Text;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Execution.Tools.Memory;

/// <summary>
/// 文件记忆(框架 <c>FileMemoryProvider</c>)的磁盘布局:<b>一个角色一个目录，跨会话共享</b>，
/// 目录名是 <c>{角色名}_{角色id}</c>。
///
/// 框架默认给每个新会话开一个 <c>{timestamp}_{guid}</c> 目录——那是草稿纸而不是记忆，
/// 所以目录名由本类决定，并在挂接时覆写会话状态里的 <c>WorkingFolder</c>
/// (见 <see cref="HarnessCharacterRunner"/>)。
///
/// 目录名带角色名是为了让用户在文件管理器里认得出来，代价是改名要搬目录；
/// id 全写不截断，见 <see cref="GetFolderName(string,string)"/>。
/// 搬迁不靠"改名事件"(角色名只是个透传 setter，没有变更通知)，而是每次挂接做一次
/// <see cref="Reconcile(CharacterData)"/> 对账：比的是目标状态与磁盘现状，
/// 因此外部导入角色卡、手改 json、旧会话恢复全都能自愈。
/// </summary>
public static class FileMemoryLayout
{
    private const int MaxNameLength = 32; //目录名里角色名部分的长度上限
    private const int WorkspaceHashLength = 8; //工作区键里哈希部分的十六进制位数

    /// <summary>
    /// 框架索引里最多列多少条（<c>FileMemoryProvider.MaxIndexEntries</c>）。
    ///
    /// <b>这是抄来的一份</b>——那边是 private const，拿不到也钉不住。抄错的后果只是界面上的
    /// 条数提示早报或晚报，不影响任何行为，所以不值得为它引入反射。
    /// 越过这个数之后，框架按<b>文件名字母序</b>截断（不是按时间），靠后的记忆不再进索引，
    /// 模型也就再想不起去读——这是已知代价，见 ADR 0002 修正案。
    /// </summary>
    public const int IndexEntryLimit = 50;

    private const string DescriptionSuffix = "_description.md"; //框架的描述侧车后缀,数条数时要排掉
    private const string IndexFileName = "memories.md"; //框架的索引文件名,同上

    /// <summary>
    /// 框架状态包里存 <c>FileMemoryState</c> 的键。等于框架 <c>FileMemoryProvider.StateKeys</c>
    /// 的唯一元素，但那是实例属性、装配侧拿不到实例，故在此复制一份并由不变量测试钉住。
    /// </summary>
    public const string StateKey = "FileMemoryProvider";

    /// <summary>所有角色的文件记忆目录的父目录</summary>
    public static string RootPath => AppPaths.Data.AgentFileMemory;

    /// <summary>
    /// 角色的文件记忆目录名(相对 <see cref="RootPath"/>)
    /// </summary>
    /// <param name="character">角色</param>
    /// <returns>目录名</returns>
    public static string GetFolderName(CharacterData character)
    {
        return GetFolderName(character.CharacterName, character.CharacterId);
    }

    /// <summary>
    /// 角色的文件记忆目录名(显式入参，可单测)
    /// </summary>
    /// <param name="characterName">角色显示名</param>
    /// <param name="characterId">角色标识</param>
    /// <returns>目录名，形如 <c>名字_角色id</c>；名字里没有可用字符时只剩 id</returns>
    public static string GetFolderName(string characterName, string characterId)
    {
        // id 不截断:内置角色的 CharacterId 是枚举名(见 DefaultCharacterManager),
        // 而 Assistant / AssistantExplain / AssistantExpert 这类共前缀的名字一截就撞,
        // 撞了就会把别的角色的笔记目录当成自己改名前的目录搬走
        string suffix = Sanitize(characterId, int.MaxValue);
        string name = Sanitize(characterName, MaxNameLength);
        return name.Length == 0 ? suffix : $"{name}_{suffix}";
    }

    /// <summary>
    /// 对账并返回该角色本次应使用的目录：磁盘上存在同一 id 后缀但名字不同的目录，就把它搬到目标名；
    /// 角色选了<see cref="EFileMemoryScope.Workspace"/> 且本次绑了工作区时，再往下多一段工作区。
    /// </summary>
    /// <param name="character">角色</param>
    /// <param name="workspacePath">本次会话绑定的工作区绝对路径；未绑定为 null 或空串</param>
    /// <returns>目录名(相对 <see cref="RootPath"/>)，项目级时形如 <c>角色_id/工作区_哈希</c></returns>
    public static string Reconcile(CharacterData character, string? workspacePath = null)
    {
        return Reconcile(RootPath, character, workspacePath);
    }

    /// <inheritdoc cref="Reconcile(CharacterData,string)"/>
    /// <param name="rootPath">父目录（显式入参，可单测）</param>
    /// <param name="character">角色</param>
    /// <param name="workspacePath">本次会话绑定的工作区绝对路径；未绑定为 null 或空串</param>
    public static string Reconcile(string rootPath, CharacterData character, string? workspacePath = null)
    {
        // 改名对账只认外层的角色目录:工作区那一段挂在它下面,跟着一起搬,故这里一行不用改
        string characterFolder = Reconcile(rootPath, character.CharacterName, character.CharacterId);
        return AppendWorkspace(characterFolder, character.Tools.FileMemoryScope, workspacePath);
    }

    /// <summary>
    /// 按范围决定要不要在角色目录下再加一段工作区。
    /// </summary>
    /// <param name="characterFolder">角色目录名</param>
    /// <param name="scope">归属范围</param>
    /// <param name="workspacePath">工作区绝对路径；未绑定为 null 或空串</param>
    /// <returns>最终目录名</returns>
    private static string AppendWorkspace(string characterFolder, EFileMemoryScope scope, string? workspacePath)
    {
        // 选了项目级却没绑工作区:回落角色级。落进 Scratch 是"记了但会没",比不记更坏;
        // 直接关掉文件记忆则会让用户看到"开关开着但模型说没有记忆工具"
        if (scope != EFileMemoryScope.Workspace || string.IsNullOrWhiteSpace(workspacePath))
            return characterFolder;

        return $"{characterFolder}/{GetWorkspaceSegment(workspacePath)}";
    }

    /// <summary>
    /// 工作区路径 → 目录名一段：<c>目录名_短哈希</c>。
    ///
    /// 两半都不可省。光用目录名会撞（两个项目都叫 <c>client</c>，撞了就是记忆互相污染）；
    /// 光用哈希用户在文件管理器里认不出是哪个项目——与 ADR 0002「目录名里带角色名」同一个取舍。
    ///
    /// 哈希取 SHA256 而非 <c>string.GetHashCode</c>：后者每进程随机化，
    /// 重启一次就换一个目录，记忆当场"丢"。
    /// </summary>
    /// <param name="workspacePath">工作区路径</param>
    /// <returns>目录名一段</returns>
    public static string GetWorkspaceSegment(string workspacePath)
    {
        // 先归一再算:同一个工作区经不同写法(相对路径、大小写、尾斜杠)进来必须落到同一段,
        // 否则同一个项目会分裂成几份记忆
        string full = Path.GetFullPath(workspacePath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string canonical = OperatingSystem.IsLinux() ? full : full.ToLowerInvariant();

        byte[] hash = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        string suffix = Convert.ToHexString(hash)[..WorkspaceHashLength].ToLowerInvariant();

        string name = Sanitize(Path.GetFileName(full), MaxNameLength);
        return name.Length == 0 ? suffix : $"{name}_{suffix}";
    }

    /// <summary>
    /// 数这个角色（在本次范围下）已经记了多少条记忆，供界面提示用。
    ///
    /// <b>刻意不做对账</b>：<see cref="Reconcile(CharacterData,string)"/> 会搬目录，
    /// 而这是个纯查询，界面打开一次就搬一次目录是意外的副作用。代价是刚改名、还没挂接过的角色
    /// 这里读的是新名字的空目录 —— 显示 0 条，下一次挂接后自动正确。
    ///
    /// 排掉框架的两类内部文件：描述侧车与索引本身。不排的话 50 条记忆在磁盘上是 101 个文件，
    /// 提示直接错一倍。
    /// </summary>
    /// <param name="character">角色</param>
    /// <param name="workspacePath">工作区绝对路径；未绑定为 null 或空串</param>
    /// <returns>记忆条数；目录不存在时为 0</returns>
    public static int CountMemories(CharacterData character, string? workspacePath = null)
    {
        return CountMemories(RootPath, character, workspacePath);
    }

    /// <inheritdoc cref="CountMemories(CharacterData,string)"/>
    /// <param name="rootPath">父目录（显式入参，可单测）</param>
    /// <param name="character">角色</param>
    /// <param name="workspacePath">工作区绝对路径；未绑定为 null 或空串</param>
    public static int CountMemories(string rootPath, CharacterData character, string? workspacePath = null)
    {
        string folder = AppendWorkspace(GetFolderName(character), character.Tools.FileMemoryScope, workspacePath);
        string path = Path.Combine(rootPath, folder.Replace('/', Path.DirectorySeparatorChar));
        if (!Directory.Exists(path)) return 0;

        try
        {
            return Directory.EnumerateFiles(path)
                .Select(Path.GetFileName)
                .Count(name => name != null && !IsInternalFile(name));
        }
        catch (Exception e)
        {
            // 数不出来就当没有:这只喂一句界面提示,不该让编辑页构造失败
            Log.Warning($"File memory: count failed for '{folder}': {e.Message}");
            return 0;
        }
    }

    /// <summary>是否框架的内部文件（描述侧车或索引本身），与框架 <c>IsInternalFile</c> 同口径</summary>
    private static bool IsInternalFile(string fileName)
    {
        return fileName.EndsWith(DescriptionSuffix, StringComparison.OrdinalIgnoreCase)
               || fileName.Equals(IndexFileName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 对账并返回该角色应使用的目录名(显式入参，可单测)。
    /// <b>宁可不搬也不覆盖笔记</b>：命中多于一个旧目录、或目标目录已存在时都只告警，直接用目标名。
    /// </summary>
    /// <param name="rootPath">父目录</param>
    /// <param name="characterName">角色显示名</param>
    /// <param name="characterId">角色标识</param>
    /// <returns>目录名(相对 <paramref name="rootPath"/>)</returns>
    public static string Reconcile(string rootPath, string characterName, string characterId)
    {
        string target = GetFolderName(characterName, characterId);
        if (!Directory.Exists(rootPath)) return target;

        string suffix = Sanitize(characterId, int.MaxValue);
        List<string> owned = Directory.EnumerateDirectories(rootPath)
            .Where(path => IsOwnedBy(Path.GetFileName(path), suffix))
            .Where(path => !string.Equals(Path.GetFileName(path), target, StringComparison.Ordinal))
            .ToList();

        if (owned.Count == 0) return target;
        if (owned.Count > 1)
        {
            Log.Warning($"File memory: {owned.Count} stale folders match '{suffix}', none moved. " +
                        $"Using '{target}'.");
            return target;
        }

        string source = owned[0];
        string destination = Path.Combine(rootPath, target);
        if (Directory.Exists(destination))
        {
            Log.Warning($"File memory: both '{Path.GetFileName(source)}' and '{target}' exist, " +
                        "not merging. Using the latter.");
            return target;
        }

        try
        {
            Directory.Move(source, destination);
            Log.Debug($"File memory: moved '{Path.GetFileName(source)}' -> '{target}' (character renamed).");
        }
        catch (Exception e)
        {
            // 搬不动就等于这个角色从空目录重新开始记，笔记还在旧目录里，故必须留下痕迹
            Log.Warning($"File memory: move '{Path.GetFileName(source)}' -> '{target}' failed, " +
                        $"starting empty: {e.Message}");
        }

        return target;
    }

    /// <summary>目录名是否属于该角色(名字部分可以是任意值，认的是 id 后缀)</summary>
    private static bool IsOwnedBy(string folderName, string idSuffix)
    {
        return string.Equals(folderName, idSuffix, StringComparison.Ordinal)
               || folderName.EndsWith($"_{idSuffix}", StringComparison.Ordinal);
    }

    /// <summary>
    /// 目录名安全化：只留字母与数字(中文属 Letter，会保留)，再截到长度上限。
    /// 名字部分截断后可能与另一个角色相同，但 id 后缀负责区分，不会撞车。
    /// </summary>
    private static string Sanitize(string text, int maxLength)
    {
        string kept = new(text.Where(char.IsLetterOrDigit).ToArray());
        return kept.Length <= maxLength ? kept : kept[..maxLength];
    }
}
