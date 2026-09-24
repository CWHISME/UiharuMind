using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Shared.Utils;

public class IconUtils
{
    private const string AvaresScheme = "avares://";

    // 图标尺寸、最多四张、全进程共用一份。进程级缓存,不 Dispose——
    // 谁把它们释放了,整个进程的头像与托盘图标一起变空白
    private static Bitmap? _defaultIcon;

    private static readonly Dictionary<string, CharacterIconEntry> CharacterIcons = new(); //角色自带头像,按角色缓存

    //缓存项连来源一起存:角色改了头像,来源串跟着变,据此失效
    private readonly record struct CharacterIconEntry(string Source, Bitmap Bitmap);

    /// <summary>应用图标。进程级缓存，调用方不得释放</summary>
    public static Bitmap? DefaultAppIcon => _defaultIcon ??= LoadDefaultBitmap("Icon.png");

    /// <summary>默认角色头像 = 应用图标（花的五瓣图，任何主题下都不违和）。进程级缓存，调用方不得释放</summary>
    public static Bitmap? DefaultCharIcon => DefaultAppIcon;

    /// <summary>默认工具人(智能体)头像 = 应用图标，与默认角色头像统一（旧的花环少女图与初春撞脸，不再使用）。进程级缓存，调用方不得释放</summary>
    public static Bitmap? DefaultToolCharIcon => DefaultAppIcon;

    /// <summary>默认用户头像。进程级缓存，调用方不得释放</summary>
    public static Bitmap? DefaultUserIcon => DefaultAppIcon;

    /// <summary>
    /// 取角色头像。三种来源：空串回落默认头像；<c>avares://</c> 走内置头像资源；
    /// 其余按 base64 图片数据解。
    ///
    /// 返回值<b>一律归进程级缓存所有，谁都不得 Dispose</b>。
    /// </summary>
    /// <remarks>
    /// 内置头像刻意只有一套、不随主题切：切主题时已在界面上的条目（聊天气泡、会话列表）
    /// 不会重取头像，曾经的两套图会新旧混用。一套图在任何主题下都一致，从根上消掉刷新问题；
    /// 存量的 <c>Avatars/Dark/</c> 目录只是后备资源，代码不再引用。
    /// </remarks>
    /// <param name="characterData">角色</param>
    /// <returns>头像位图；加载失败为 null。不得释放</returns>
    public static Bitmap? GetCharacterBitmapOrDefault(CharacterData characterData)
    {
        var source = characterData.CharacterIcon;
        if (string.IsNullOrEmpty(source))
        {
            // ADR 0043 合并之后只剩三类：用户卡 / 智能体 / 普通角色。
            // 存量的工具人卡归普通角色，头像从「工具」换成「角色」——它们本来就和扮演角色走同一条装配
            if (characterData.IsUserCard) return DefaultUserIcon;
            return characterData.IsAgent ? DefaultToolCharIcon : DefaultCharIcon;
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

    //来源的两种形态:avares 路径走资源,其余是 base64 用户数据
    private static Bitmap? DecodeIcon(string source)
    {
        if (source.StartsWith(AvaresScheme, StringComparison.Ordinal))
        {
            try
            {
                using Stream stream = AssetLoader.Open(new Uri(source));
                return new Bitmap(stream);
            }
            catch (Exception e)
            {
                Log.Error($"Load character avatar asset failed: {e.Message}");
                return null;
            }
        }

        //base64 是用户数据,坏串只该回落到默认头像,不该顺着绑定抛上去
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

    /// <summary>Assets 下资源的统一 URI（前缀只在这里拼）</summary>
    public static Uri AssetUri(string fileName) => new("avares://UiharuMind/Assets/" + fileName);

    /// <summary>加载 Assets 下的图片作为窗口图标（托盘用）。进程级不缓存：托盘图标小而长寿，由调用方持有</summary>
    /// <param name="fileName">Assets 下的文件名，如 <c>TrayColorBase.png</c></param>
    /// <returns>窗口图标；加载失败为 null</returns>
    public static WindowIcon? LoadWindowIconFromAsset(string fileName)
    {
        try
        {
            using Stream stream = AssetLoader.Open(AssetUri(fileName));
            return new WindowIcon(stream);
        }
        catch (Exception e)
        {
            Log.Error($"Load window icon failed: {e.Message}");
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
        try
        {
            using Stream stream = AssetLoader.Open(AssetUri(path));
            return new Bitmap(stream);
        }
        catch (Exception e)
        {
            Log.Error(e.Message);
            return null;
        }
    }
}
