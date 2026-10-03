using System.Text;
using UiharuMind.Core.AI.Execution.Skills;
using UiharuMind.Core.Core;
using UiharuMind.Features.DevAutomation;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.About;

/// <summary>
/// 内置技能 uiharu-guide（ADR 0061）：这个应用怎么用。正文就是帮助页那份文档（按当前语言），前面补几条此刻的事实；
/// 开发者模式开着时指向 uiharu-dev
/// </summary>
internal static class UiharuGuideSkill
{
    public const string Name = "uiharu-guide";

    /// <summary>
    /// 建这个技能。一直在
    /// </summary>
    /// <returns>内置技能</returns>
    public static BuiltInSkill Create() => new(Name,
        "How to use the UiharuMind app itself: Use when the user asks how to do something in this app or where a setting lives.",
        BuildBody);

    /// <summary>
    /// 生成正文：帮助页那份文档，前面补此刻的事实（与 <see cref="UiharuMind.Features.DevAutomation.UiharuDevSkill"/> 同口径，供测试直接调）
    /// </summary>
    /// <returns>正文（markdown）</returns>
    internal static string BuildBody()
    {
        StringBuilder text = new();
        text.AppendLine("# UiharuMind 使用说明");
        text.AppendLine();
        text.AppendLine("## 此刻的事实");
        text.AppendLine();
        text.AppendLine($"- 档案目录：`{AppPaths.Root}`（会话、配置、日志都在这里）");
        text.AppendLine($"- 技能目录：`{AppPaths.Data.Skills}`（放一个同名技能可以顶掉内置的这份）");
        text.AppendLine(DeveloperMode.IsEnabled
            ? $"- 开发者模式开着：要从命令行驱动这个应用（建群、发言、离席、导出流水），用技能 `{UiharuDevSkill.Name}`。"
            : "- 开发者模式关着。");
        text.AppendLine();
        text.Append(HelpPageData.ReadHelpDocument(LocalizationManager.Instance.LanguageCode));
        return text.ToString();
    }
}
