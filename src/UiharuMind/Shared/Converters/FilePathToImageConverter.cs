using System;
using System.Globalization;
using System.IO;
using Avalonia.Data.Converters;
using UiharuMind.Shared.Utils;

namespace UiharuMind.Shared.Converters;

/// <summary>
/// 系统图片全文件路径转 Bitmap。
/// <c>ConverterParameter</c> 给目标高度（缩略图必须给，否则一张 4K 截图会整张解码）；不给则按原尺寸解。
/// </summary>
public class FilePathToImageConverter : IValueConverter
{
    public static readonly FilePathToImageConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string filePath || string.IsNullOrEmpty(filePath))
            return null;

        try
        {
            if (!File.Exists(filePath))
                return null;

            using var stream = File.OpenRead(filePath);
            return UiUtils.DecodeBitmap(stream, UiUtils.ParseTargetHeight(parameter));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load image from path: {filePath}, Error: {ex.Message}");
            return null;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
