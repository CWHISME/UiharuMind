/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using UiharuMind.Core.AI.Execution.Assembly;
using UiharuMind.Core.AI.Execution.Files;
using UiharuMind.Core.AI.Execution.Tools;

namespace UiharuMind.Core.AI.Execution.Extensions;

/// <summary>
/// 生图扩展
/// </summary>
public sealed class ImageExtension : BuiltinExtension
{
    /// <summary>规格单例</summary>
    public static readonly ImageExtension Spec = new();

    public override string Name => BuiltinExtensions.Image;

    public override string Guide => "根据提示生成图像，或传入路径编辑已有图像。";

    public override string Summary => "生成或修改图片";

    public override IReadOnlyList<(string Command, string ToolName)> CommandNames =>
        [(BuiltinExtensions.ImageGenerate, ImageGenerationTool.ToolName)];

    protected override BuiltinExtension CreateAssembled(AgentToolResources resources,
        EReadImageMode imageMode, bool mountFiles)
    {
        ImageExtension assembled = new();
        assembled.SetCommands(Bind([ImageGenerationTool.Create(resources.Paths, resources.ImageProgress)]));
        if (!resources.ImageModelsConfigured)
        {
            assembled.SetUnavailable("未配置图像生成模型。告诉用户在「设置 → 图像生成」里添加一个。");
        }

        return assembled;
    }
}
