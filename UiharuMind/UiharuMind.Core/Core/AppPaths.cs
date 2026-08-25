/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

namespace UiharuMind.Core.Core;

/// <summary>
/// 全应用的磁盘布局。嵌套类与磁盘目录层级同构——改这里就等于改磁盘。
/// <para>
/// 只管目录与全局唯一的固定文件；带变量的文件名(<c>{sessionId}.meta</c>、角色的
/// <c>{guid}.json</c>)属于各模块的命名方案,不进这里。
/// </para>
/// <para>分树依据、命名规则与历史包袱见 <c>docs/adr/0013</c>。</para>
/// </summary>
public static class AppPaths
{
    /// <summary>应用数据根目录</summary>
    public static readonly string Root =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UiharuMind");

    /// <summary>日志</summary>
    public static readonly string Logs = Path.Combine(Root, "Logs");

    /// <summary>配置:丢了重配即可</summary>
    public static class Config
    {
        public static readonly string Root = Path.Combine(AppPaths.Root, "Config");

        private static readonly string McpRoot = Path.Combine(Root, "Mcp");

        /// <summary>MCP 服务器定义,对齐 .mcp.json 格式</summary>
        public static readonly string McpServers = Path.Combine(McpRoot, "McpServers.json");

        /// <summary>MCP 逐条授权指纹</summary>
        public static readonly string McpTrust = Path.Combine(McpRoot, "McpTrust.json");

        /// <summary>MCP 服务器的本地启停状态</summary>
        public static readonly string McpServerStates = Path.Combine(McpRoot, "McpServerStates.json");

        /// <summary>
        /// 配置类的落盘位置,文件名即类名。加一个配置类不需要在这里登记。
        /// </summary>
        /// <param name="configTypeName">配置类名</param>
        /// <returns>完整文件路径</returns>
        public static string ForType(string configTypeName) => Path.Combine(Root, configTypeName + ".json");
    }

    /// <summary>用户数据:删了就没了</summary>
    public static class Data
    {
        public static readonly string Root = Path.Combine(AppPaths.Root, "Data");

        /// <summary>角色卡</summary>
        public static readonly string Characters = Path.Combine(Root, "Characters");

        /// <summary>内置角色的用户覆盖文件(内置角色本体在程序集资源里)</summary>
        public static readonly string CharacterOverrides = Path.Combine(Characters, "Overrides");

        /// <summary>提示词片段</summary>
        public static readonly string PromptSnippets = Path.Combine(Characters, "PromptSnippets.json");

        /// <summary>会话存档:角色对话与 agent 对话共用</summary>
        public static readonly string Sessions = Path.Combine(Root, "Sessions");

        /// <summary>知识库定义</summary>
        public static readonly string Memory = Path.Combine(Root, "Memory");

        /// <summary>知识库向量库。归 Data 而非 Cache:重建依赖当初那个 embedding 模型还在</summary>
        public static readonly string MemoryEmbeddings = Path.Combine(Memory, "Embeddings");

        /// <summary>技能包</summary>
        public static readonly string Skills = Path.Combine(Root, "Skills");

        /// <summary>连点器脚本</summary>
        public static readonly string AutoClick = Path.Combine(Root, "AutoClick");

        private static readonly string ClipboardRoot = Path.Combine(Root, "Clipboard");

        /// <summary>剪贴板历史记录</summary>
        public static readonly string ClipboardHistory = Path.Combine(ClipboardRoot, "ClipboardHistory.json");

        /// <summary>剪贴板历史图片</summary>
        public static readonly string ClipboardImages = Path.Combine(ClipboardRoot, "Images");

        private static readonly string AgentRoot = Path.Combine(Root, "Agent");

        /// <summary>定时任务</summary>
        public static readonly string ScheduledAgentTasks = Path.Combine(AgentRoot, "ScheduledAgentTasks.json");

        /// <summary>agent 的文件记忆</summary>
        public static readonly string AgentFileMemory = Path.Combine(AgentRoot, "FileMemory");

        /// <summary>对话附件</summary>
        public static readonly string AgentAttachments = Path.Combine(AgentRoot, "Attachments");

        /// <summary>
        /// agent 想让用户看到的产出(跑 Python 画的图、导出的数据)。
        ///
        /// <b>归 Data 而非 Cache</b>:对话正文里以 <c>file://</c> 链接引用它们,
        /// 清掉就等于历史里留下一堆坏图。它们也不可重建——重跑一次是另一次推理。
        ///
        /// ⚠️ 与 <see cref="AgentAttachments"/> 分工:那边是<b>用户</b>发进来的,这边是 agent 产出的。
        /// </summary>
        public static readonly string AgentOutputs = Path.Combine(AgentRoot, "Outputs");

        /// <summary>快捷工具面板的搜索历史</summary>
        public static readonly string QuickSearchHistory =
            Path.Combine(Root, "QuickTools", "QuickSearchHistory.json");
    }

    /// <summary>可再生缓存:大且用户可随手删</summary>
    public static class Cache
    {
        public static readonly string Root = Path.Combine(AppPaths.Root, "Cache");

        /// <summary>agent 的临时工作区</summary>
        public static readonly string Scratch = Path.Combine(Root, "Scratch");

        /// <summary>下载的应用安装包</summary>
        public static readonly string Updates = Path.Combine(Root, "Updates");

    }

    /// <summary>用户自管的大件:模型权重与后端引擎,应用只读不生成</summary>
    public static class External
    {
        public static readonly string Root = Path.Combine(AppPaths.Root, "External");

        /// <summary>本地 GGUF 模型的默认目录(用户可在设置里改)</summary>
        public static readonly string Models = Path.Combine(Root, "Models");

        /// <summary>
        /// embedding 模型的默认目录(用户可在设置里改)。
        /// <b>必须与 <see cref="Models"/> 平级,不能嵌进去</b>——对话模型的扫描是
        /// <c>SearchOption.AllDirectories</c>,嵌进去会让 embedding 模型混进对话模型列表。
        /// </summary>
        public static readonly string EmbeddedModels = Path.Combine(Root, "EmbeddedModels");

        /// <summary>本地服务引擎(llama.cpp 等)</summary>
        public static readonly string Engine = Path.Combine(Root, "Engine");

        /// <summary>
        /// 受管 Python 虚拟环境。解释器由用户提供,这个目录由我们建、由 agent 往里装包。
        /// 归 External 是因为它与引擎同性质:体量大(装完科学栈可达数百 MB)、可重建但重建很贵。
        /// </summary>
        public static readonly string PythonEnv = Path.Combine(Root, "PythonEnv");
    }
}
