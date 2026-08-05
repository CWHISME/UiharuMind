/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using UiharuMind.Core.AI.Character;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Execution;

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

    /// <summary>
    /// 框架状态包里存 <c>FileMemoryState</c> 的键。等于框架 <c>FileMemoryProvider.StateKeys</c>
    /// 的唯一元素，但那是实例属性、装配侧拿不到实例，故在此复制一份并由不变量测试钉住。
    /// </summary>
    public const string StateKey = "FileMemoryProvider";

    /// <summary>所有角色的文件记忆目录的父目录</summary>
    public static string RootPath => Path.Combine(SettingConfig.SaveAgentDataPath, "FileMemory");

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
    /// 对账并返回该角色应使用的目录名：磁盘上存在同一 id 后缀但名字不同的目录，就把它搬到目标名。
    /// </summary>
    /// <param name="character">角色</param>
    /// <returns>目录名(相对 <see cref="RootPath"/>)</returns>
    public static string Reconcile(CharacterData character)
    {
        return Reconcile(RootPath, character.CharacterName, character.CharacterId);
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
