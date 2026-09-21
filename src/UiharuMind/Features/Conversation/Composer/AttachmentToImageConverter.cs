using System;
using System.Globalization;
using System.IO;
using Avalonia.Data.Converters;
using UiharuMind.Shared.Utils;

namespace UiharuMind.Features.Conversation.Composer;

/// <summary>
/// ConversationAttachment 转 Bitmap,供输入框上方缩略图显示。
/// 文件附件按路径读取;内存附件按字节流读取。
/// <c>ConverterParameter</c> 给目标高度（缩略图必须给）；不给则按原尺寸解。
/// </summary>
public class AttachmentToImageConverter : IValueConverter
{
    public static readonly AttachmentToImageConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not ConversationAttachment attachment) return null;
        if (!attachment.IsImage) return null;

        int targetHeight = UiUtils.ParseTargetHeight(parameter);

        try
        {
            if (attachment.Bytes != null)
            {
                using var stream = new MemoryStream(attachment.Bytes);
                return UiUtils.DecodeBitmap(stream, targetHeight);
            }

            if (!string.IsNullOrEmpty(attachment.FilePath) && File.Exists(attachment.FilePath))
            {
                using var stream = File.OpenRead(attachment.FilePath);
                return UiUtils.DecodeBitmap(stream, targetHeight);
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
