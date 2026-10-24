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
using UiharuMind.Core.AI.Execution.Tools.Desktop;

namespace UiharuMind.Core.AI.Execution.Extensions;

/// <summary>
/// 一条扩展命令：背后就是原来那把工具，描述与参数 schema 原样拿来渲染帮助、绑定参数
/// </summary>
/// <param name="Name">命令名</param>
/// <param name="Function">执行它的工具</param>
public sealed record ExtensionCommand(string Name, AIFunction Function);

/// <summary>
/// 一个内置扩展（ADR 0079）。<b>每个扩展一个类</b>：名字、命令对照、风险分级、
/// 挂载场合、一句话说明、装配工厂全部内聚在类里——加扩展 = 新类 + 在
/// <see cref="BuiltinExtensions.Catalog"/> 登记一行，不再去中央 if-chain 各处插段。
///
/// 两类实例：
/// <list type="bullet">
/// <item><b>规格</b>（各扩展类的 <c>Spec</c> 静态单例）：无资源依赖，命令名解析
/// <see cref="CanonicalCommand"/>、风险 <see cref="Classify"/>、场合 <see cref="MountsIn"/>
/// 都可在不装配的情况下回答——审批卡、名单、界面只认它</item>
/// <item><b>装配体</b>（<see cref="Assemble"/> 的产物）：每会话一份，<see cref="Commands"/>
/// 绑上了实际工具（浏览器标签页、会话登记等状态在工具里，不在扩展上）</item>
/// </list>
/// </summary>
public abstract class BuiltinExtension
{
    private string? _unavailable;

    /// <summary>扩展名（browser / desktop / image / audio / worktree / goal）</summary>
    public abstract string Name { get; }

    /// <summary>帮助的开头（相当于 MCP 的 server 自述）</summary>
    public abstract string Guide { get; }

    /// <summary>系统提示名单里的一句话说明</summary>
    public abstract string Summary { get; }

    /// <summary>命令名 → 原工具名。审批与执行都从这份解析，因此不会出现「审批按没这条命令判、执行却落到别处」</summary>
    public abstract IReadOnlyList<(string Command, string ToolName)> CommandNames { get; }

    /// <summary>隐藏扩展：不列在广告与帮助里，但可调用（目标模式专用，模型经注入消息得知）</summary>
    public virtual bool IsHidden => false;

    /// <summary>缺前提时的原因：照样列出，但调不了；能用为 null</summary>
    public virtual string? Unavailable => _unavailable;

    /// <summary>装配后的命令（命令名 → 工具）；规格上为空</summary>
    public IReadOnlyList<ExtensionCommand> Commands { get; private set; } = [];

    /// <summary>
    /// 命令的规范写法：认命令名，也认原工具名（模型照着旧习惯写 BrowserAct 时）
    /// </summary>
    /// <param name="command">模型写的命令名</param>
    /// <returns>命令名；没有这条命令为 null</returns>
    public string? CanonicalCommand(string command)
    {
        string wanted = command.Trim();
        foreach ((string name, string toolName) in CommandNames)
        {
            if (string.Equals(name, wanted, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(toolName, wanted, StringComparison.OrdinalIgnoreCase)) return name;
        }

        return null;
    }

    /// <summary>命令名背后的原工具名（帮助渲染、审批拆开调用用）</summary>
    /// <param name="command">命令名（规范写法）</param>
    /// <returns>原工具名；不是本扩展的命令为 null</returns>
    public string? ToolNameOf(string command) =>
        CommandNames.FirstOrDefault(x => x.Command == command).ToolName;

    /// <summary>
    /// 这次调用要不要问用户。默认：没这条命令（执行只会报错）按只读放过去，否则一般级；
    /// 各扩展覆写自己的规则（只读命令、硬问的本机/接管、按应用记的桌面…）
    /// </summary>
    /// <param name="call">扩展调用（已确认是本扩展）</param>
    /// <returns>风险</returns>
    public virtual EExtensionRisk Classify(FunctionCallContent call)
    {
        if (!ExtensionCalls.TryRead(call, out ExtensionInvocation? invocation)) return EExtensionRisk.ReadOnly;
        return CanonicalCommand(invocation.Command) == null ? EExtensionRisk.ReadOnly : EExtensionRisk.Standard;
    }

    /// <summary>
    /// 「本会话允许」记在哪一格：多数扩展整个记；桌面按应用记（见 <see cref="DesktopExtension"/>）
    /// </summary>
    /// <param name="invocation">拆开的扩展调用</param>
    /// <param name="call">原调用</param>
    /// <param name="desktop">桌面宿主（按窗口编号认应用）；缺省用全应用那一个</param>
    /// <returns>放行名单里的一格</returns>
    public virtual string ApprovalKey(ExtensionInvocation invocation, FunctionCallContent call,
        DesktopHost? desktop = null) => Name;

    /// <summary>
    /// 这个场合挂不挂。默认全场合；各扩展覆写（目标模式只挂主代理、worktree 挂能自己动手的场合）
    /// </summary>
    /// <param name="scope">场合</param>
    /// <returns>挂为 true</returns>
    public virtual bool MountsIn(EAgentToolScope scope) => true;

    /// <summary>
    /// 装配一份这次会话可用的扩展（每会话一份）。子类在 <see cref="CreateAssembled"/> 里
    /// 造工具、按 <see cref="CommandNames"/> 绑好（<see cref="Bind"/>），缺前提时置 Unavailable
    /// </summary>
    /// <param name="resources">装配资源（宿主、会话、路径口径等）</param>
    /// <param name="imageMode">当前模型看图的形态（Read 附图 / 指识图 / 不看）</param>
    /// <param name="mountFiles">是否挂了文件工具（影响截图能不能经 Read 看）</param>
    /// <returns>装配体</returns>
    internal BuiltinExtension Assemble(AgentToolResources resources, EReadImageMode imageMode, bool mountFiles) =>
        CreateAssembled(resources, imageMode, mountFiles);

    /// <summary>子类造装配体</summary>
    protected abstract BuiltinExtension CreateAssembled(AgentToolResources resources,
        EReadImageMode imageMode, bool mountFiles);

    /// <summary>装配体记录命令（子类工厂调）</summary>
    /// <param name="commands">已绑定的命令</param>
    protected void SetCommands(IReadOnlyList<ExtensionCommand> commands) => Commands = commands;

    /// <summary>缺前提（子类工厂置）</summary>
    /// <param name="reason">给模型看的原因</param>
    protected void SetUnavailable(string reason) => _unavailable = reason;

    /// <summary>
    /// 把工具按 <see cref="CommandNames"/> 绑成命令。工具改名而声明没跟上时当场炸，
    /// 而不是悄悄少一条命令——与旧 <c>BuiltinExtensions.Bind</c> 同一不变量。
    /// 声明了但没造的工具（如没角色时的 synthesize）不绑，执行侧按「没这条命令」报错
    /// </summary>
    /// <param name="tools">这次装配造出的工具</param>
    /// <returns>绑好的命令</returns>
    protected IReadOnlyList<ExtensionCommand> Bind(IEnumerable<AITool> tools)
    {
        List<ExtensionCommand> bound = new();
        foreach (AITool tool in tools)
        {
            string toolName = ((AIFunction)tool).Name;
            (string command, string declared) = CommandNames.SingleOrDefault(x => x.ToolName == toolName);
            if (declared == null)
            {
                throw new InvalidOperationException(
                    $"扩展 '{Name}' 造出的工具 '{toolName}' 没有在 CommandNames 里声明。");
            }

            bound.Add(new ExtensionCommand(command, (AIFunction)tool));
        }

        return bound;
    }
}
