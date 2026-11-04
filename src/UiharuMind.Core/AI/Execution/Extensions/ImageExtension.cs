/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using Microsoft.Extensions.AI;
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

    public override string Guide => "根据提示生成图像，或传入路径编辑已有图像；也能列出配了哪些生图模型。";

    public override string Summary => "生成或修改图片";

    public override IReadOnlyList<(string Command, string ToolName)> CommandNames =>
    [
        (BuiltinExtensions.ImageGenerate, ImageGenerationTool.ToolName),
        (BuiltinExtensions.ImageModels, ImageModelsTool.ToolName),
    ];

    /// <summary>查清单只读、不花钱，三档都不问；出图会外呼并可能计费，与基类一致按一般级问一次</summary>
    public override EExtensionRisk Classify(FunctionCallContent call, ExtensionRiskContext? context = null)
    {
        if (!ExtensionCalls.TryRead(call, out ExtensionInvocation? invocation)) return EExtensionRisk.ReadOnly;
        // 没这条命令的走基类：按只读放过去，到工具里只会报错
        return invocation.Command == BuiltinExtensions.ImageModels ? EExtensionRisk.ReadOnly : base.Classify(call);
    }

    protected override BuiltinExtension CreateAssembled(AgentToolResources resources,
        EReadImageMode imageMode, bool mountFiles)
    {
        ImageExtension assembled = new();
        assembled.SetCommands(Bind([
            ImageGenerationTool.Create(resources.Paths, resources.ImageProgress),
            ImageModelsTool.Create(),
        ]));
        if (!resources.ImageModelsConfigured)
        {
            assembled.SetUnavailable("未配置图像生成模型。告诉用户在「设置 → 图像生成」里添加一个。");
        }

        return assembled;
    }
}
