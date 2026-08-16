using System;
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
    /// <b>返回值的所有权是混合的</b>：角色自带头像时是现解出来的一张新位图，
    /// 回落时是上面那几张进程级共用的默认图。调用方分不出来是哪一种，
    /// 所以<b>一律不得 Dispose 本方法的返回值</b>——释放到默认图上会清空整个进程的头像。
    /// 头像本来就小而长寿，代价只是每次回落多留一张图给 GC。
    /// </summary>
    /// <param name="characterData">角色</param>
    /// <returns>头像位图；加载失败为 null。不得释放</returns>
    public static Bitmap? GetCharacterBitmapOrDefault(CharacterData characterData)
    {
        if (string.IsNullOrEmpty(characterData.CharacterIcon))
        {
            return characterData.Kind switch
            {
                ECharacterKind.UserCard => DefaultUserIcon,
                ECharacterKind.Roleplay => DefaultCharIcon,
                _ => DefaultToolCharIcon,
            };
        }

        var icon = characterData.CharacterIcon.Base64ToBitmap();
        if (icon == null) return DefaultCharIcon;
        return icon;
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