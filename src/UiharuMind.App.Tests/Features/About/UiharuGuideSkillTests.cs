using UiharuMind.Core.Core;
using UiharuMind.Features.About;
using UiharuMind.Features.DevAutomation;

namespace UiharuMind.App.Tests.Features.About;

/// <summary>
/// 内置技能 uiharu-guide（ADR 0061）：正文是帮助页那份文档，前面补此刻的事实；开发者模式开着时指向 uiharu-dev。
/// 只读当下状态不断言开关（切开关会动真实标记文件），分支存在即算覆盖。
/// 帮助文档不断言全文（那是拿实现证明自己），只钉稳定的标记行
/// </summary>
public class UiharuGuideSkillTests
{
    [Fact]
    public void Body_IsHelpDocumentPlusCurrentFacts()
    {
        string body = UiharuGuideSkill.BuildBody();

        Assert.Contains($"`{AppPaths.Root}`", body);
        Assert.Contains($"`{AppPaths.Data.Skills}`", body);
        Assert.Contains("# UiharuMind 帮助", body);
        Assert.Contains("## 快速开始", body);
        if (DeveloperMode.IsEnabled)
            Assert.Contains($"`{UiharuDevSkill.Name}`", body);
        else
            Assert.Contains("开发者模式关着", body);
    }
}
