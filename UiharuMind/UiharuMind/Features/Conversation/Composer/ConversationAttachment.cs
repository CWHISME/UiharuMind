using System;
using System.IO;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Features.Conversation.Composer;

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

    /// <summary>
    /// 取得一个真实存在的文件路径；粘贴来的内存附件会先落盘。
    ///
    /// 必要性：非视觉模型下附件不会被内联为 DataContent，而是以路径文本引用的形式送给模型。
    /// 粘贴的图片原本没有路径，只有一个自动生成的文件名，模型收到的是一个既无内容也无路径的
    /// 名字，完全无用；而识图工具 ask_vision 要的正是一个 File.Exists 通得过的路径。
    /// 落盘之后这两件事同时成立。
    /// </summary>
    /// <returns>文件路径；落盘失败返回 null</returns>
    public string? ResolveFilePath()
    {
        if (!string.IsNullOrEmpty(FilePath)) return FilePath;
        if (Bytes == null || Bytes.Length == 0) return null;

        try
        {
            string dir = Path.Combine(SettingConfig.SaveAgentDataPath, "Attachments");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            string name = string.IsNullOrEmpty(FileName)
                ? $"pasted_{DateTime.Now:yyyyMMdd_HHmmssfff}.png"
                : FileName;
            string path = Path.Combine(dir, name);
            if (!File.Exists(path)) File.WriteAllBytes(path, Bytes);
            return path;
        }
        catch (Exception e)
        {
            Log.Warning($"Persist pasted attachment failed '{FileName}': {e.Message}");
            return null;
        }
    }
}
