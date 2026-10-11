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
using UiharuMind.Core.AI.Execution.Files;
using UiharuMind.Core.Core.Utils;

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
    /// <param name="paths">路径解析口径(与文件工具同一份)</param>
    /// <returns>工具实例</returns>
    public static AITool Create(AgentPathResolver paths)
    {
        return AIFunctionFactory.Create(
            async ([Description("图片文件的绝对路径或相对工作区的路径。")] string[] imagePaths,
                    [Description("要针对图片回答的问题。")] string question,
                    CancellationToken cancellationToken = default) =>
                await AskVisionAsync(paths, imagePaths, question, cancellationToken).ConfigureAwait(false),
            ToolName,
            "将一张或多张图片交给支持视觉的模型分析：识别其内容并回答相关问题。" +
            "传入多个路径可并排对比图片。" +
            "绝不要仅凭文件名猜测图片内容。");
    }

    private static async Task<string> AskVisionAsync(AgentPathResolver paths, string[] imagePaths, string question,
        CancellationToken cancellationToken)
    {
        if (imagePaths is null or { Length: 0 }) return "Error: imagePaths 至少要包含一个路径。";
        if (imagePaths.Length > MaxImages) return $"Error: 单次最多 {MaxImages} 张图。";

        List<ImageInput> images = new();
        List<string> missing = new();
        foreach (string imagePath in imagePaths)
        {
            if (!paths.TryResolve(imagePath, out string full) || !File.Exists(full))
            {
                missing.Add(imagePath);
                continue;
            }

            byte[] imageBytes = await File.ReadAllBytesAsync(full, cancellationToken).ConfigureAwait(false);
            images.Add(new ImageInput(imageBytes, ImageFormats.MediaTypeFromPath(full, "image/jpeg")));
        }

        // 任一缺失就整体报错并列出全部缺失路径,让模型修正后重试;
        // 不搞「缺的跳过、剩的分析」——那会让模型误以为全部分析了
        if (missing.Count > 0) return $"Error: 找不到图片文件：{string.Join(", ", missing)}";

        ImageVisionPromptAction skill = new(images);

        StringBuilder result = new();
        await foreach (string delta in skill.RunAsync(question, cancellationToken).ConfigureAwait(false))
        {
            result.Append(delta);
        }

        return result.Length == 0 ? "(视觉模型未返回答案)" : result.ToString();
    }
}
