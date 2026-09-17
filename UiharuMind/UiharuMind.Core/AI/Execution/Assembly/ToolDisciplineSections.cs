/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using UiharuMind.Core.AI.Execution.Prompts;

namespace UiharuMind.Core.AI.Execution.Assembly;

/// <summary>
/// 工具纪律段的<b>唯一一张清单</b>：哪些段、什么顺序、什么条件出现。主代理与子代理共用。
///
/// 两档从前各写一套，差异不是设计而是漂移：子代理拿着 `Edit`/`Write` 却从没收到过
/// 文件修改纪律，同时又收到一段指名了 `Edit` 的 shell 纪律；主代理挂了 WebSearch/WebFetch
/// 却零指示；工作目录在一边排最前、在另一边排最后。共用一张清单之后，
/// 两档的差别只剩 <see cref="ToolDisciplineFacts"/> 里那几个开关。
/// </summary>
internal static class ToolDisciplineSections
{
    /// <summary>
    /// 一次装配的事实。<b>全部取自"装配结果"而非"配置意图"</b>——
    /// 提示语指名的工具必须真的在同一份工具集里，有不变量测试按这条钉着。
    /// </summary>
    internal sealed record ToolDisciplineFacts
    {
        /// <summary>读文件的工具（Glob/Grep/Read）是否已装配</summary>
        public bool FileRead { get; init; }

        /// <summary>写文件的工具（Edit/Write）是否已装配。只读装配下为 false，而 <see cref="FileRead"/> 仍可为 true</summary>
        public bool FileWrite { get; init; }

        /// <summary>命令行工具是否已装配</summary>
        public bool Shell { get; init; }

        /// <summary>受管 Python 环境是否就绪。它不对应任何工具，Python 由 shell 跑（ADR 0019）</summary>
        public bool Python { get; init; }

        /// <summary>联网工具（WebSearch/WebFetch）是否已装配</summary>
        public bool WebAccess { get; init; }

        /// <summary>识图工具是否已装配</summary>
        public bool Vision { get; init; }

        /// <summary>知识库检索工具是否已装配</summary>
        public bool KnowledgeBase { get; init; }

        /// <summary>委派工具是否已装配。<b>子代理恒为 false</b>：它不能再派子代理（防无限递归）</summary>
        public bool Delegation { get; init; }

        /// <summary>工作目录绝对路径；空串则不写该段</summary>
        public string WorkingDirectory { get; init; } = string.Empty;

        /// <summary>草稿目录绝对路径；空串则不写该段</summary>
        public string OutputRoom { get; init; } = string.Empty;

        /// <summary>记忆目录绝对路径；空串则不写该段。子代理没有记忆段——它拿的是一份任务书</summary>
        public string Memory { get; init; } = string.Empty;

        /// <summary>实际解析出来的 shell 可执行路径；空串则不写那一句</summary>
        public string ShellBinary { get; init; } = string.Empty;

        /// <summary>是否给子代理装配（只影响草稿目录段的措辞，见 <see cref="AgentToolPrompts.BuildOutputRoom"/>）</summary>
        public bool ForSubAgent { get; init; }
    }

    /// <summary>
    /// 按清单拼出 <c># 工具</c> 整段（含父标题与两句护栏）。
    ///
    /// 护栏句紧跟父标题：整段是中文、每轮都发、体量压过用户那几句话，
    /// 不钉一句"别照着这段的语言回复"，小模型的输出语言就会被拽向中文。
    /// 挂在这里而不是工作循环段，是因为那段会落进用户存档、用户删得掉。
    /// </summary>
    /// <param name="facts">这次装配的事实</param>
    /// <param name="includeGuards">
    /// 是否把两句护栏挂在父标题之后。<b>主代理传 true</b>：它的中文体量几乎全在这一段，
    /// 护栏与漂移源同生共死，没有纪律段就不白花 token。<b>子代理传 false</b>：
    /// 它整份提示词都是中文（身份段、协作段都算），护栏得挂在更靠前、且一定存在的地方
    /// </param>
    /// <returns><c># 工具</c> 整段；一项纪律都没有时为空串（光挂一个空父标题是纯噪声）</returns>
    internal static string Build(ToolDisciplineFacts facts, bool includeGuards = true)
    {
        PromptSectionList list = new();

        // 路径事实排在最前:后面每一段纪律都以"路径怎么写"为前提。
        // 这条原则从前只在主代理侧成立,子代理把工作目录排在整段 shell 纪律之后
        list.Section(facts.WorkingDirectory.Length > 0,
            AgentPromptHeadings.WorkingDirectory("##"),
            () => AgentToolPrompts.BuildWorkingDirectory(facts.WorkingDirectory));

        // 草稿目录只在真有地方可写时出现:没有写工具又没有 shell 时,
        // 说了也只是指一个写不进去的目录
        list.Section(facts.OutputRoom.Length > 0 && (facts.FileWrite || facts.Shell),
            AgentPromptHeadings.OutputRoom("##"),
            () => AgentToolPrompts.BuildOutputRoom(facts.OutputRoom, facts.ForSubAgent));

        // 记忆靠 Read/Write/Edit/Glob 读写,没有专门的记忆工具(ADR 0028)
        list.Section(facts.Memory.Length > 0 && facts.FileRead,
            AgentPromptHeadings.Memory("##"),
            () => AgentToolPrompts.BuildMemory(facts.Memory, facts.Shell));

        list.Section(facts.FileRead, AgentPromptHeadings.FileOperations, AgentToolPrompts.FileReadDefault);
        list.Section(facts.FileWrite, AgentPromptHeadings.FileModifications, AgentToolPrompts.FileWriteDefault);

        // shell 曾是唯一挂了工具却零指示的能力,缺口的表现是模型拿 Write 重写全文去做一次 mv。
        // 「文件系统操作用 Shell」那句刻意不写进文件纪律段:shell 可以被关掉,
        // 而那一段在 shell 关掉时照样发出去,于是会给模型指一个不存在的工具
        list.Section(facts.Shell, AgentPromptHeadings.Shell,
            () => AgentToolPrompts.BuildShell(facts.FileWrite, facts.ShellBinary));

        // Python 是 shell 的一个分项而非独立能力(ADR 0019),故要求 shell 也在场
        list.Section(facts.Shell && facts.Python, AgentPromptHeadings.Python,
            () => AgentToolPrompts.BuildPython(facts.FileWrite));

        list.Section(facts.WebAccess, AgentPromptHeadings.WebAccess, AgentToolPrompts.WebAccessDefault);
        list.Section(facts.Vision, AgentPromptHeadings.Images, AgentToolPrompts.VisionToolDefault);
        list.Section(facts.KnowledgeBase, AgentPromptHeadings.KnowledgeBase, AgentToolPrompts.KnowledgeSearchDefault);
        list.Section(facts.Delegation, AgentPromptHeadings.Delegation, AgentToolPrompts.SubAgentDefault);

        if (list.IsEmpty) return string.Empty;

        string guards = includeGuards
            ? $"\n{AgentToolPrompts.LanguageNeutrality}\n{AgentToolPrompts.ConcurrentCalls}\n"
            : string.Empty;
        return $"{AgentPromptHeadings.Tools}\n{guards}\n{list}";
    }
}
