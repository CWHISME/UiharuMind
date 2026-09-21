using System;
using System.Collections.Generic;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Shared.Utils;

public class IconUtils
{
    // 图标尺寸、最多四张、全进程共用一份。进程级缓存,不 Dispose——
    // 谁把它们释放了,整个进程的头像与托盘图标一起变空白
    private static Bitmap? _defaultIcon;
    private static Bitmap? _defaultCharIcon;
    private static Bitmap? _defaultToolCharIcon;
    private static Bitmap? _defaultUserIcon;

    private static readonly Dictionary<string, CharacterIconEntry> CharacterIcons = new(); //角色自带头像,按角色缓存

    //缓存项连来源 base64 一起存:角色改了头像,来源串跟着变,据此失效
    private readonly record struct CharacterIconEntry(string Source, Bitmap Bitmap);

    /// <summary>应用图标。进程级缓存，调用方不得释放</summary>
    public static Bitmap? DefaultAppIcon => _defaultIcon ??= LoadDefaultBitmap("Icon.png");

    /// <summary>默认角色头像。进程级缓存，调用方不得释放</summary>
    public static Bitmap? DefaultCharIcon => _defaultCharIcon ??= LoadDefaultBitmap("DefaultCharIcon.png");

    /// <summary>默认工具人头像。进程级缓存，调用方不得释放</summary>
    public static Bitmap? DefaultToolCharIcon => _defaultToolCharIcon ??= LoadDefaultBitmap("DefaultToolCharIcon.png");

    /// <summary>默认用户头像。进程级缓存，调用方不得释放</summary>
    public static Bitmap? DefaultUserIcon => _defaultUserIcon ??= LoadDefaultBitmap("Icon.png");

    /// <summary>
    /// 取角色头像（<c>CharacterIcon</c> 是 base64 编码的图片），没有则回落到默认头像。
    ///
    /// 返回值<b>一律归进程级缓存所有，谁都不得 Dispose</b>：回落时是上面那几张共用的默认图，
    /// 角色自带头像时是本类按角色缓存的那一张。头像属于规则 2-3 的「小而长寿」一档。
    /// 角色改了头像下一次调用自动重解，被顶掉的旧图不释放、交给 GC。
    /// </summary>
    /// <param name="characterData">角色</param>
    /// <returns>头像位图；加载失败为 null。不得释放</returns>
    public static Bitmap? GetCharacterBitmapOrDefault(CharacterData characterData)
    {
        var source = characterData.CharacterIcon;
        if (string.IsNullOrEmpty(source))
        {
            return characterData.Kind switch
            {
                ECharacterKind.UserCard => DefaultUserIcon,
                ECharacterKind.Roleplay => DefaultCharIcon,
                _ => DefaultToolCharIcon,
            };
        }

        return GetOrDecodeCharacterIcon(characterData.CharacterId, source) ?? DefaultCharIcon;
    }

    private static Bitmap? GetOrDecodeCharacterIcon(string characterId, string source)
    {
        if (string.IsNullOrEmpty(characterId)) return DecodeIcon(source); //没有稳定标识就没法缓存

        lock (CharacterIcons)
        {
            // 先比引用:同一个 CharacterData 反复读取时这一步就命中,不必逐字符比几十 KB
            if (CharacterIcons.TryGetValue(characterId, out var cached) &&
                (ReferenceEquals(cached.Source, source) || cached.Source == source))
            {
                return cached.Bitmap;
            }

            var bitmap = DecodeIcon(source);
            if (bitmap != null) CharacterIcons[characterId] = new CharacterIconEntry(source, bitmap);
            return bitmap;
        }
    }

    //base64 是用户数据,坏串只该回落到默认头像,不该顺着绑定抛上去
    private static Bitmap? DecodeIcon(string source)
    {
        try
        {
            return source.Base64ToBitmap();
        }
        catch (Exception e)
        {
            Log.Error($"Decode character icon failed: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// Assets 下的图片路径
    /// </summary>
    /// <param name="path"></param>
    /// <returns></returns>
    public static Bitmap? LoadDefaultBitmap(string path)
    {
        var uri = new Uri("avares://UiharuMind/Assets/" + path);
        var stream = AssetLoader.Open(uri);
        try
        {
            var bitmap = new Bitmap(stream);
            return bitmap;
        }
        catch (Exception e)
        {
            Log.Error(e.Message);
            return null;
        }
    }
}