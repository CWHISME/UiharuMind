using System;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Utils;

public class IconUtils
{
    private static Bitmap? _defaultIcon;
    private static Bitmap? _defaultCharIcon;
    private static Bitmap? _defaultToolCharIcon;
    private static Bitmap? _defaultUserIcon;

    public static Bitmap? DefaultAppIcon => _defaultIcon ??= LoadDefaultBitmap("Icon.png");
    public static Bitmap? DefaultCharIcon => _defaultCharIcon ??= LoadDefaultBitmap("DefaultCharIcon.png");
    public static Bitmap? DefaultToolCharIcon => _defaultToolCharIcon ??= LoadDefaultBitmap("DefaultToolCharIcon.png");
    public static Bitmap? DefaultUserIcon => _defaultUserIcon ??= LoadDefaultBitmap("Icon.png");

    /// <summary>
    /// character 是 base64 编码的图片
    /// </summary>
    /// <returns></returns>
    public static Bitmap? GetCharacterBitmapOrDefault(CharacterData characterData)
    {
        if (string.IsNullOrEmpty(characterData.CharacterIcon))
        {
            return characterData.IsHide
                ? DefaultUserIcon
                : characterData.IsTool
                    ? DefaultToolCharIcon
                    : DefaultCharIcon;
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