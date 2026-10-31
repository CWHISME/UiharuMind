using System.Text;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution.Tools;
using UiharuMind.Core.AI.ImageGeneration;

namespace UiharuMind.Core.AI.Execution;

/// <summary>
/// 生图模型清单（<c>image.models</c>）：有哪些家、按什么顺序回退、各自此刻能不能接单。
/// 只读、不花钱，名字就是 <see cref="ImageGenerationTool"/> 的 <c>models</c> 参数唯一认得的写法。
///
/// 清单<b>不进系统提示</b>，也不写进 <c>generate</c> 的描述：名字随用户配置变，写死就等于每改一次生图模型
/// 换一次缓存前缀（同 ADR 0031 理由）。模型需要挑的时候现查，平时不付这份钱。
/// </summary>
public static class ImageModelsTool
{
    /// <summary>工具名</summary>
    public const string ToolName = "ListImageModels";

    /// <summary>
    /// 造工具（读全局生图模型列表）
    /// </summary>
    /// <returns>工具实例</returns>
    public static AITool Create() => Create(ImageGenerationService.Shared);

    /// <summary>
    /// 造工具
    /// </summary>
    /// <param name="service">生图回退链（名字与状态都从它取，出图与清单因此永远同一份）</param>
    /// <returns>工具实例</returns>
    internal static AITool Create(ImageGenerationService service) =>
        ToolJson.CreateFunction(() => Describe(service), ToolName,
            "列出生图模型：名字（给 image.generate 的 models 参数用）、接口格式、分辨率档、能不能编辑、此刻的状态。" +
            "要挑一家或几家来出图时先查这个，别猜名字。");

    /// <summary>
    /// 清单正文
    /// </summary>
    /// <param name="service">生图回退链</param>
    /// <returns>给模型的文本</returns>
    internal static string Describe(ImageGenerationService service)
    {
        IReadOnlyList<ImageModelInfo> models = service.Models;
        if (models.Count == 0) return "还没有登记任何生图模型。告诉用户在「设置 → 图像生成」里添加一个。";

        StringBuilder sb = new($"生图模型 {models.Count} 个，按回退顺序排（靠前的先试）：\n");
        foreach (ImageModelInfo model in models)
        {
            sb.Append("- ").Append(model.Name)
                .Append(" · ").Append(DialectLabel(model.Dialect))
                .Append(" · ").Append(model.Resolution.ToTierLabel())
                .Append(" · ").Append(model.SupportsEditing ? "可编辑" : "只能生成")
                .Append(" · ").Append(StatusLabel(ImageGenerationService.StatusOf(model)))
                .Append('\n');
        }

        sb.Append("generate 不带 models 时按这个顺序自动挑一家、用不了就换下一家；" +
                  "点名了就只试点名的那些（最多 ").Append(ImageGenerationTool.MaxModels)
            .Append(" 个）、各出一张，不再自动换家。");
        return sb.ToString();
    }

    private static string DialectLabel(EImageDialect dialect) => dialect switch
    {
        EImageDialect.OpenAI => "OpenAI 兼容",
        EImageDialect.SenseNova => "商汤",
        EImageDialect.Agnes => "Agnes",
        _ => "本机 sd.cpp",
    };

    // 与设置页那几个词一致，用户说「熔断中那个」时两边指同一件事
    private static string StatusLabel(ImageModelStatus status) => status.State switch
    {
        EImageModelState.NotConfigured => "未配置（地址或模型 id 没填）",
        EImageModelState.MissingFiles => "缺组件文件（被删了或还没下完）",
        EImageModelState.Cooling => $"连续失败，暂停使用：{status.LastError}",
        _ => "就绪",
    };
}
