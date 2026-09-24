/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.ComponentModel;
using System.Text;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Character.PromptActions;

namespace UiharuMind.Core.AI.Execution;

/// <summary>
/// 识图工具:主模型不支持多模态时,把图片问题转交给视觉模型回答。
/// 内部复用 Vision 角色的技能链路(ImageVisionPromptAction)——提示词、视觉模型解析与
/// 会话级模型绑定只存在一份,不再与快捷识图各写一套。只读能力,无需审批。
/// 支持一次多张(<c>imagePaths</c> 数组),多张时视觉模型同框看,对比类问题才答得准。
/// </summary>
public static class VisionTool
{
    /// <summary>工具名。提示词里提到本工具时一律引用这个常量,写死字面量迟早对不上</summary>
    public const string ToolName = "AnalyzeImage";

    /// <summary>单次调用最多几张图。一张图在视觉模型上下文里几千 token,塞太多会炸</summary>
    public const int MaxImages = 8;

    /// <summary>
    /// 创建识图 AIFunction
    /// </summary>
    /// <param name="workspaceRoot">工作目录根,相对路径的解析基准(与文件/shell 工具同一规则)</param>
    /// <returns>工具实例</returns>
    public static AITool Create(string workspaceRoot)
    {
        return AIFunctionFactory.Create(
            async ([Description("Absolute or workspace-relative paths of the image files.")] string[] imagePaths,
                    [Description("The question to answer about the image(s).")] string question,
                    CancellationToken cancellationToken = default) =>
                await AskVisionAsync(workspaceRoot, imagePaths, question, cancellationToken).ConfigureAwait(false),
            ToolName,
            "Analyze one or more image files by delegating to a vision-capable model: identify their content " +
            "and answer questions about them. " +
            "Pass multiple paths to compare images side by side.");
    }

    private static async Task<string> AskVisionAsync(string workspaceRoot, string[] imagePaths, string question,
        CancellationToken cancellationToken)
    {
        if (imagePaths is null or { Length: 0 }) return "Error: imagePaths must contain at least one path.";
        if (imagePaths.Length > MaxImages) return $"Error: at most {MaxImages} images per call.";

        List<ImageInput> images = new();
        List<string> missing = new();
        foreach (string imagePath in imagePaths)
        {
            string full = ResolvePath(workspaceRoot, imagePath);
            if (!File.Exists(full))
            {
                missing.Add(imagePath);
                continue;
            }

            byte[] imageBytes = await File.ReadAllBytesAsync(full, cancellationToken).ConfigureAwait(false);
            images.Add(new ImageInput(imageBytes, InferMediaType(full)));
        }

        // 任一缺失就整体报错并列出全部缺失路径,让模型修正后重试;
        // 不搞「缺的跳过、剩的分析」——那会让模型误以为全部分析了
        if (missing.Count > 0) return $"Error: image file not found: {string.Join(", ", missing)}";

        ImageVisionPromptAction skill = new(images);

        StringBuilder result = new();
        await foreach (string delta in skill.RunAsync(question, cancellationToken).ConfigureAwait(false))
        {
            result.Append(delta);
        }

        return result.Length == 0 ? "(vision model returned no answer)" : result.ToString();
    }

    /// <summary>按扩展名推 MIME 类型;推不出默认 jpeg(视觉问答链路的老默认)</summary>
    private static string InferMediaType(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            _ => "image/jpeg", //含 .jpg/.jpeg 与一切未知扩展
        };
    }

    // 相对路径解析到工作区根,绝对路径直接访问(与 PermissiveFileAccessTools.ResolvePath 同规则)
    private static string ResolvePath(string workspaceRoot, string path)
    {
        if (Path.IsPathRooted(path)) return Path.GetFullPath(path);
        return Path.GetFullPath(Path.Combine(workspaceRoot, path));
    }
}
