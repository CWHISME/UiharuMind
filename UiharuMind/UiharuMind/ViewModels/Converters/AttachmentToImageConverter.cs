using System;
using System.Globalization;
using System.IO;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using UiharuMind.ViewModels.Conversation;

namespace UiharuMind.ViewModels.Converters;

/// <summary>
/// ConversationAttachment 转 Bitmap,供输入框上方缩略图显示。
/// 文件附件按路径读取;内存附件按字节流读取。
/// </summary>
public class AttachmentToImageConverter : IValueConverter
{
    public static readonly AttachmentToImageConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not ConversationAttachment attachment) return null;

        try
        {
            if (attachment.Bytes != null)
            {
                using var stream = new MemoryStream(attachment.Bytes);
                return new Bitmap(stream);
            }

            if (!string.IsNullOrEmpty(attachment.FilePath) && File.Exists(attachment.FilePath))
            {
                using var stream = File.OpenRead(attachment.FilePath);
                return new Bitmap(stream);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load attachment image: {ex.Message}");
        }

        return null;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
