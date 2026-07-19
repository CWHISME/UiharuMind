using System;
using System.IO;

namespace UiharuMind.ViewModels.Conversation;

/// <summary>
/// 统一附件模型:支持文件路径与内存字节两种形态,供所有会话类型(聊天/Agent)共用。
/// </summary>
public class ConversationAttachment
{
    /// <summary>文件附件的本地路径;内存附件时为空</summary>
    public string? FilePath { get; init; }

    /// <summary>内存图片字节(粘贴图片时);文件附件时为空</summary>
    public byte[]? Bytes { get; init; }

    /// <summary>展示名称</summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>MIME 类型,如 image/png;非图片文件为 application/octet-stream 等</summary>
    public string MediaType { get; init; } = "image/png";

    /// <summary>是否为图片(可内联为视觉内容)</summary>
    public bool IsImage => MediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);

    /// <summary>是否仅存在于内存(未落盘)</summary>
    public bool IsInMemory => Bytes != null;
}
