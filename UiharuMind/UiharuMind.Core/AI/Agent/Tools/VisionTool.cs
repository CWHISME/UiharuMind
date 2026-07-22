/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.ComponentModel;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Core;

namespace UiharuMind.Core.AI.Agent;

/// <summary>
/// 识图工具:主模型不支持多模态时,把图片问题转交给已配置的视觉模型回答。
/// 只读能力,无需审批。
/// </summary>
public static class VisionTool
{
    private const int ModelReadyTimeoutSeconds = 60;

    /// <summary>
    /// 创建识图 AIFunction
    /// </summary>
    /// <returns>工具实例</returns>
    public static AITool Create()
    {
        return AIFunctionFactory.Create(AskVisionAsync, "ask_vision",
            "Answer a question about an image file by delegating to a vision-capable model. " +
            "Use this when you cannot see images yourself.");
    }

    private static async Task<string> AskVisionAsync(
        [Description("Absolute or workspace-relative path of the image file.")]
        string imagePath,
        [Description("The question to answer about the image.")]
        string question,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(imagePath)) return $"Image file not found: {imagePath}";

        IChatClient? client = await ResolveVisionClientAsync(cancellationToken).ConfigureAwait(false);
        if (client == null) return "No vision-capable model is available.";

        ChatMessage message = new(ChatRole.User, new List<AIContent>
        {
            new TextContent(question),
            new DataContent(await File.ReadAllBytesAsync(imagePath, cancellationToken).ConfigureAwait(false),
                GetMediaType(imagePath)),
        });

        ChatResponse response = await client.GetResponseAsync([message], cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return string.IsNullOrEmpty(response.Text) ? "(vision model returned no answer)" : response.Text;
    }

    private static async Task<IChatClient?> ResolveVisionClientAsync(CancellationToken cancellationToken)
    {
        ModelRunningData? model = null;
        if (!LlmManager.Instance.TryCheckModelRunning(true, ref model) || model == null) return null;

        // 远程模型的 ChatClient 可能仍在启动中,限时等待就绪
        DateTimeOffset deadline = DateTimeOffset.Now.AddSeconds(ModelReadyTimeoutSeconds);
        while (model.ChatClient == null && DateTimeOffset.Now < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(200, cancellationToken).ConfigureAwait(false);
        }

        return model.ChatClient;
    }

    private static string GetMediaType(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            _ => "image/jpeg",
        };
    }
}
