/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.AI;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Shell;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.AI;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Features.Conversation.Composer;

/// <summary>
/// 输入框上方的附件盘：待发附件的集合与增删预览，以及「这些附件怎么变成一条用户消息」。
///
/// 会话以 <see cref="Func{TResult}"/> 取而不是构造时吃一个 <see cref="ChatSession"/>：
/// 首轮发送时会话还不存在（懒建的会话要到 RunTurnAsync 内部才建），而附件解析发生在那之前。
/// 详见 <see cref="FlushOwnedFiles"/>。
/// </summary>
public partial class AttachmentTrayViewData : ObservableObject
{
    private readonly Func<ChatSession?> _session;
    private readonly List<string> _pendingOwnedFiles = new();

    /// <summary>附件集合(文件路径或内存字节),由输入框上方区域展示</summary>
    public ObservableCollection<ConversationAttachment> Attachments { get; } = new();

    /// <param name="session">取当前会话；尚未创建时为 null</param>
    public AttachmentTrayViewData(Func<ChatSession?> session)
    {
        _session = session;
    }

    /// <summary>添加文件附件</summary>
    public void AddAttachmentPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return;
        Attachments.Add(new ConversationAttachment
        {
            FilePath = path,
            FileName = Path.GetFileName(path),
            MediaType = GetMediaType(path),
        });
    }

    /// <summary>添加内存字节附件(如粘贴图片)</summary>
    public void AddAttachmentBytes(byte[] bytes, string mediaType = "image/png", string? fileName = null)
    {
        if (bytes == null || bytes.Length == 0) return;
        Attachments.Add(new ConversationAttachment
        {
            Bytes = bytes,
            FileName = fileName ?? $"pasted_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.png",
            MediaType = mediaType,
        });
    }

    /// <summary>
    /// 取走并清空待发附件。发送那一刻调用——之后输入区就该是空的，
    /// 而取走的这一批还要用于构造消息与气泡里的图片
    /// </summary>
    /// <returns>待发附件；没有附件时为 null</returns>
    public List<ConversationAttachment>? TakePending()
    {
        if (Attachments.Count == 0) return null;
        List<ConversationAttachment> taken = Attachments.ToList();
        Attachments.Clear();
        return taken;
    }

    [RelayCommand]
    private void RemoveAttachment(ConversationAttachment item)
    {
        Attachments.Remove(item);
    }

    [RelayCommand]
    private async Task AddAttachment()
    {
        var file = await App.FilesService.OpenFileAsync(UIManager.GetFocusWindow());
        string? path = file?.TryGetLocalPath();
        if (!string.IsNullOrEmpty(path)) AddAttachmentPath(path);
    }

    [RelayCommand]
    private void PreviewAttachment(ConversationAttachment item)
    {
        // 非图片文件:打开其所在目录
        if (!item.IsImage)
        {
            if (!string.IsNullOrEmpty(item.FilePath))
                App.FilesService.OpenFolder(Path.GetDirectoryName(item.FilePath) ?? item.FilePath);
            return;
        }

        Bitmap? bitmap = null;
        try
        {
            if (item.Bytes != null)
            {
                using var stream = new MemoryStream(item.Bytes);
                bitmap = new Bitmap(stream);
            }
            else if (!string.IsNullOrEmpty(item.FilePath) && File.Exists(item.FilePath))
            {
                bitmap = new Bitmap(item.FilePath);
            }
        }
        catch (Exception e)
        {
            Log.Warning($"Preview attachment failed '{item.FileName}': {e.Message}");
            return;
        }

        if (bitmap != null) UIManager.ShowPreviewImageCopyWindowAtMousePosition(bitmap);
    }

    /// <summary>
    /// 把用户输入与这一批附件组装成一条用户消息。
    /// 视觉模型下图片内联为字节（缩放重编码之后的那一份），否则一律降级为路径文本引用。
    /// </summary>
    /// <param name="text">用户输入的正文</param>
    /// <param name="attachments">本轮附件；为空时等价于一条纯文本消息</param>
    /// <returns>用户消息</returns>
    public ChatMessage BuildUserMessage(string text, List<ConversationAttachment>? attachments)
    {
        if (attachments == null || attachments.Count == 0) return new ChatMessage(ChatRole.User, text);

        bool isVision = ResolveVisionModel(attachments);
        List<AIContent>? contents = isVision ? new() { new TextContent(text) } : null;
        List<string> fileReferences = new();

        foreach (ConversationAttachment attachment in attachments)
        {
            // 仅图片且为视觉模型时内联字节;其余文件一律以路径文本引用
            if (isVision && attachment.IsImage)
            {
                try
                {
                    byte[] data = attachment.Bytes ?? File.ReadAllBytes(attachment.FilePath!);
                    // 只缩发出去的那一份:磁盘附件与界面预览仍是原图,ask_vision 拿到的也还是原文件
                    (byte[] inlineBytes, string inlineType) =
                        ConversationImageDownscaler.Downscale(data, attachment.MediaType);
                    contents!.Add(new DataContent(inlineBytes, inlineType));
                }
                catch (Exception e)
                {
                    Log.Warning($"Attachment load failed '{attachment.FileName}': {e.Message}");
                    fileReferences.Add(ReferenceOf(attachment));
                }
            }
            else
            {
                fileReferences.Add(ReferenceOf(attachment));
            }
        }

        if (isVision && (contents!.Count > 1 || fileReferences.Count == 0))
        {
            if (fileReferences.Count > 0)
                contents.Add(new TextContent(string.Join('\n', fileReferences.Select(x => $"[Attached file: {x}]"))));
            return new ChatMessage(ChatRole.User, contents);
        }

        string reference = string.Join('\n', fileReferences.Select(x => $"[Attached file: {x}]"));
        return new ChatMessage(ChatRole.User, $"{text}\n{reference}");
    }

    /// <summary>
    /// 本轮图片该内联还是降级成路径引用，取决于生效的模型能否自己看图。
    ///
    /// <b>已经选了模型就一律尊重它</b>：选了个看不了图的模型，图片就走路径引用，
    /// 由 <c>ask_vision</c> 去委托视觉模型答（装配那侧正是「当前模型看不了图才挂 ask_vision」）。
    /// 替用户把模型换掉会让「行为突然变了」无法归因，而且他选那个模型往往是有原因的。
    ///
    /// 只有<b>一个模型都还没选</b>时才主动解析一次：那种情况下发送链路下游的
    /// LazyChatClient 会按 <c>isVision=false</c> 兜底，挑中的很可能是不支持识图的偏好模型，
    /// 而此刻并没有任何用户选择会被覆盖。解析不到视觉模型则维持原状，同样走路径引用降级。
    /// </summary>
    /// <param name="attachments">本轮附件</param>
    /// <returns>最终生效的模型是否支持识图</returns>
    private bool ResolveVisionModel(List<ConversationAttachment> attachments)
    {
        // 口径与 SessionModelLabel / LazyChatClient 一致:会话绑定的专属模型优先于全局当前模型
        ModelRunningData? effectiveModel = _session()?.ChatModelRunningData
                                           ?? LlmManager.Instance.CurrentRunningModel;
        if (effectiveModel != null) return effectiveModel.IsVisionModel;
        if (!attachments.Any(x => x.IsImage)) return false;

        LlmManager.Instance.TryCheckModelRunning(true);
        ModelRunningData? resolved = LlmManager.Instance.CurrentRunningModel;
        if (resolved?.IsVisionModel != true) return false;

        // 自动挑人这件事要留痕:后续"为什么用的是这个模型"只能靠它回答
        Log.Warning($"Resolved vision model '{resolved.ModelName}' to send an image (none was selected).");
        return true;
    }

    /// <summary>
    /// 附件的文本引用。粘贴来的图片会先落盘再引用其路径——否则模型只会收到一个
    /// 自动生成的文件名，既没有内容也没有可读取的位置，识图工具也用不了它。
    /// </summary>
    private string ReferenceOf(ConversationAttachment attachment)
    {
        string? path = attachment.ResolveFilePath();
        if (path == null) return attachment.FileName;

        // 只有应用自己落盘的文件才登记为会话所有物;用户从磁盘选的原始文件不能跟着会话被删
        if (attachment.IsInMemory) _pendingOwnedFiles.Add(path);
        return path;
    }

    /// <summary>
    /// 把本轮落盘的附件登记到会话上。首轮发送时会话还不存在
    /// (EnsureSessionAsync 在 RunTurnAsync 内部才建会话)，所以先攒着，会话就绪后再写入。
    /// 这正是本类取会话要用委托而非构造时吃一个实例的原因。
    /// </summary>
    public void FlushOwnedFiles()
    {
        if (_pendingOwnedFiles.Count == 0) return;

        ChatSession? session = _session();
        if (session != null)
        {
            session.OwnedAttachmentFiles.AddRange(_pendingOwnedFiles);
            session.SaveMeta(); //附件清单是头字段
        }

        _pendingOwnedFiles.Clear();
    }

    /// <summary>
    /// 读出附件字节，供气泡显示。非视觉模型下附件没有内联进消息，
    /// 但用户附了图就该在界面上看到，所以气泡回落到附件本身
    /// </summary>
    /// <param name="attachment">附件</param>
    /// <returns>字节；读取失败为空</returns>
    internal static ReadOnlyMemory<byte> ReadAttachmentBytes(ConversationAttachment attachment)
    {
        if (attachment.Bytes != null) return attachment.Bytes;
        try
        {
            return string.IsNullOrEmpty(attachment.FilePath)
                ? ReadOnlyMemory<byte>.Empty
                : File.ReadAllBytes(attachment.FilePath);
        }
        catch (Exception e)
        {
            Log.Warning($"Read attachment failed '{attachment.FileName}': {e.Message}");
            return ReadOnlyMemory<byte>.Empty;
        }
    }

    /// <summary>根据路径推断 MIME 类型;非图片返回通用二进制类型</summary>
    private static string GetMediaType(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            ".jpg" or ".jpeg" => "image/jpeg",
            _ => "application/octet-stream",
        };
    }
}
