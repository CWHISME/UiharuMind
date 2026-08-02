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
using UiharuMind.Core.AI.Character.Skills;

namespace UiharuMind.Core.AI.Agent;

/// <summary>
/// 识图工具:主模型不支持多模态时,把图片问题转交给视觉模型回答。
/// 内部复用 Vision 角色的技能链路(ImageVisionSkill)——提示词、视觉模型解析与
/// 会话级模型绑定只存在一份,不再与快捷识图各写一套。只读能力,无需审批。
/// </summary>
public static class VisionTool
{
    /// <summary>
    /// 创建识图 AIFunction
    /// </summary>
    /// <param name="workspaceRoot">工作目录根,相对路径的解析基准(与文件/shell 工具同一规则)</param>
    /// <returns>工具实例</returns>
    public static AITool Create(string workspaceRoot)
    {
        return AIFunctionFactory.Create(
            async ([Description("Absolute or workspace-relative path of the image file.")] string imagePath,
                    [Description("The question to answer about the image.")] string question,
                    CancellationToken cancellationToken = default) =>
                await AskVisionAsync(workspaceRoot, imagePath, question, cancellationToken).ConfigureAwait(false),
            "ask_vision",
            "Answer a question about an image file by delegating to a vision-capable model. " +
            "Use this when you cannot see images yourself.");
    }

    private static async Task<string> AskVisionAsync(string workspaceRoot, string imagePath, string question,
        CancellationToken cancellationToken)
    {
        string full = ResolvePath(workspaceRoot, imagePath);
        if (!File.Exists(full)) return $"Image file not found: {imagePath}";

        byte[] imageBytes = await File.ReadAllBytesAsync(full, cancellationToken).ConfigureAwait(false);
        ImageVisionSkill skill = new(imageBytes);

        StringBuilder result = new();
        await foreach (string delta in skill.DoSkill(question, cancellationToken).ConfigureAwait(false))
        {
            result.Append(delta);
        }

        return result.Length == 0 ? "(vision model returned no answer)" : result.ToString();
    }

    // 相对路径解析到工作区根,绝对路径直接访问(与 PermissiveFileAccessTools.ResolvePath 同规则)
    private static string ResolvePath(string workspaceRoot, string path)
    {
        if (Path.IsPathRooted(path)) return Path.GetFullPath(path);
        return Path.GetFullPath(Path.Combine(workspaceRoot, path));
    }
}
