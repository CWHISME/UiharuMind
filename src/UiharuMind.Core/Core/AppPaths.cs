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
/// <para>分树依据、命名规则与历史包袱见 <c>docs/adr/0013</c>；根选址见 <c>docs/adr/0027</c>。</para>
/// </summary>
public static class AppPaths
{
    /// <summary>
    /// 应用数据根目录：<c>~/.uiharu</c>，三平台统一。环境变量 <c>UIHARU_HOME</c> 可覆盖。
    /// 旧址（各平台应用数据目录下的 <c>UiharuMind/</c>）不迁移不删除，见 ADR 0027。
    /// </summary>
    public static readonly string Root = ResolveRoot();

    private static string ResolveRoot()
    {
        string? home = Environment.GetEnvironmentVariable("UIHARU_HOME");
        if (!string.IsNullOrWhiteSpace(home)) return home;
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".uiharu");
    }

    /// <summary>
    /// 建根目录。Windows 下点号前缀不隐藏，默认根补 Hidden 属性；
    /// <c>UIHARU_HOME</c> 指向用户自选位置时只建目录、不动属性。
    /// </summary>
    public static void EnsureRoot()
    {
        try
        {
            if (!Directory.Exists(Root)) Directory.CreateDirectory(Root);
            if (!OperatingSystem.IsWindows()) return;
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("UIHARU_HOME"))) return;
            FileAttributes attributes = File.GetAttributes(Root);
            if ((attributes & FileAttributes.Hidden) == 0)
                File.SetAttributes(Root, attributes | FileAttributes.Hidden);
        }
        catch
        {
            // 最佳努力：隐藏失败不影响启动
        }
    }

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

        /// <summary>
        /// 剪贴板历史记录。
        /// <para>
        /// ⚠️ 旧版的 <c>ClipboardHistory.json</c> 仍可能留在同一目录下，<b>刻意不迁移也不删除</b>——
        /// 它是全量重写的 JSON，撑不住无上限的历史，详见 <c>docs/adr/0024</c>。
        /// </para>
        /// </summary>
        public static readonly string ClipboardHistory = Path.Combine(ClipboardRoot, "ClipboardHistory.db");

        /// <summary>剪贴板历史图片</summary>
        public static readonly string ClipboardImages = Path.Combine(ClipboardRoot, "Images");

        private static readonly string AgentRoot = Path.Combine(Root, "Agent");

        /// <summary>定时任务</summary>
        public static readonly string ScheduledAgentTasks = Path.Combine(AgentRoot, "ScheduledAgentTasks.json");

        /// <summary>对话附件</summary>
        public static readonly string AgentAttachments = Path.Combine(AgentRoot, "Attachments");

        /// <summary>
        /// agent 产物（图、数据、临时脚本、下载物）的根目录：<b>一个工作区一个家</b>，
        /// 家里按会话分房间（ADR 0026）。
        ///
        /// <b>归 Data 而非 Cache</b>:对话正文里以 <c>file://</c> 链接引用它们,
        /// 清掉就等于历史里留下一堆坏图。它们也不可重建——重跑一次是另一次推理。
        ///
        /// ⚠️ 与 <see cref="AgentAttachments"/> 分工:那边是<b>用户</b>发进来的,这边是 agent 产出的。
        /// </summary>
        public static readonly string AgentWorkspaces = Path.Combine(AgentRoot, "Workspaces");

        /// <summary>
        /// [已废弃] 旧布局的 agent 产出目录(<c>Data/Agent/Outputs</c>，ADR 0026 起新产物进
        /// <see cref="AgentWorkspaces"/>)。<b>不迁移</b>:历史 <c>file://</c> 链接还指着这里。
        /// 仅剩两条路径在使用:删除会话时的残留清理与启动时的空目录清扫
        /// （见 <see cref="AI.Execution.AgentOutputLayout"/>）。
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

        /// <summary>WebFetch 截断时落盘的网页全文:可再生(重新抓一次即可)、用户可随手删</summary>
        public static readonly string FetchedPages = Path.Combine(Root, "FetchedPages");

        /// <summary>WebFetch 遇到非文本内容时下载的文件:可再生(重新下载即可)、用户可随手删</summary>
        public static readonly string Downloads = Path.Combine(Root, "Downloads");

        /// <summary>
        /// Write 工具覆盖已有文件前的自动备份:按源文件分桶,只留最近若干份。
        /// 归 Cache 而非 Data:它是防手滑的安全网、可随手删,不是用户资产;
        /// 放全局而不放工作区内,避免污染 Glob/Grep 与 git 状态。
        /// </summary>
        public static readonly string FileBackups = Path.Combine(Root, "FileBackups");

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
