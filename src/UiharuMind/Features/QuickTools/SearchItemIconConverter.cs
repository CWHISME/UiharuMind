using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace UiharuMind.Features.QuickTools;

/// <summary>
/// 结果条目图标：目录用 folder-open，内容命中用 file-text（带行号），文件名/浏览的文件用 file。
/// 入参是整个 SearchItem（DataTemplate 的 DataContext），由条目自己说它是什么。
/// </summary>
public class SearchItemIconConverter : IValueConverter
{
    public static readonly SearchItemIconConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is SearchItem item)
        {
            if (item.IsDirectory) return "folder-open";
            return item.IsContentSearch ? "file-text" : "file";
        }
        return "file";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
