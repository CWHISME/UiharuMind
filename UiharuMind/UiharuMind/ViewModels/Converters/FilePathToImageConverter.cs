using System;
using System.Globalization;
using System.IO;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace UiharuMind.ViewModels.Converters;

/// <summary>
/// 系统图片全文件路径转 Bitmap
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
            return new Bitmap(stream);
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