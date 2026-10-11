using UiharuMind.Core.AI.Execution.Tools;

namespace UiharuMind.Core.AI.Execution.Prompts;

/// <summary>
/// 脑工具（<see cref="BrainTool.ToolName"/>，ADR 0082）的「模型可见」说明书：描述与参数说明，唯一出处。
///
/// 与委派两把工具的措辞分工：脑是<b>同步</b>的——当场返回结果，没有「已派出、尚无结果」那套；
/// 描述里必须把「同步等、当场拿内容、追问带 brainId」讲清，否则模型会以为它是后台委派。
/// </summary>
public static class BrainToolPrompts
{
    /// <summary>
    /// 描述：是什么、什么时候值得开、同步意味着什么、怎么追问。
    ///
    /// 「只读、只能 Read/Grep、不能改任何东西」写在这里而不是纪律段——脑没有纪律段，
    /// 它的全部政策都在这一把工具的 schema 上（对模型来说它就是这么用的）。
    /// </summary>
    public const string Description =
        "把一段内容交给一个小模型助手（脑）整理：总结大文件、给被截断的工具结果做摘要、按关键词查证。\n" +
        "它是同步的：你这一轮会当场等到结果，直接拿到整理好的内容，末尾有一行 [sub-session: 短号] 标识。\n" +
        "它只读，只能用 Read 与 Grep 两把工具，不能改任何东西；适合「现在就想要一份结论」的场景。\n" +
        "想基于同一份材料继续深挖，把返回的标识作为 brainId 再调本工具，它会记得之前的问答。";

    /// <summary>task 参数说明</summary>
    public const string TaskParam =
        "任务或问题，直接写给它：要总结什么、按什么口径、重点是什么。它看不到你的对话，要写完整、自包含。";

    /// <summary>brainId 参数说明</summary>
    public const string BrainIdParam =
        "可选。之前返回的 [sub-session: …] 标识；给了 = 追问那个脑，它记得之前的问答；留空 = 新开一个脑。";
}
