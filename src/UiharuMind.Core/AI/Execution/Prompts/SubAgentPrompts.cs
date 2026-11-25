/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using UiharuMind.Core.AI.Execution.Tools;

namespace UiharuMind.Core.AI.Execution.Prompts;

/// <summary>
/// 子代理系统提示里「子代理专属」的那些句子，唯一出处。
///
/// 与主代理侧的 <see cref="AgentToolPrompts"/> 对应：这里只收身份与协作口径；
/// 工具纪律（文件、命令行、Python、草稿目录、工作目录）两档<b>共用主代理那份</b>，
/// 由段落清单统一编排，不在这里重复定义。
///
/// <b>从前的「# 做法」段已拆掉</b>：那一段把「你能用哪些工具」与「你该怎么协作」
/// 混成一摊平级 bullet，于是 shell 纪律和「回复要聚焦」并排出现，
/// 而主代理侧同样的内容是有 <c>## 命令行</c> 标题的——同一件事两种层级。
/// </summary>
public static class SubAgentPrompts
{
    /// <summary>
    /// 身份段首句。<b>点名的子智能体不发这一句</b>：它自己的角色卡已经说过它是谁，
    /// 再跟一句"你是 UiharuMind 的一个代理"就是跟人格抢身份（与 ADR 0005 同一道理）。
    /// </summary>
    public const string Role = "你是 UiharuMind 的一个代理，当前受主代理委托做事，请根据任务要求工作。";

    /// <summary>
    /// 派活时给的一句话主题
    /// </summary>
    /// <returns></returns>
    public static string RoleAssignment(string topic) =>
        $"本次委派的主题：{topic}。围绕这个主题做事。";

    /// <summary>可变更档的边界。能力由工具集决定、规则由任务书指明，这里不重复（与「档位提示」同源，那一份已删）</summary>
    public const string BoundaryCanMutate =
        "";

    /// <summary>只读档的边界。同上</summary>
    public const string BoundaryReadOnly =
        "";

    /// <summary>
    /// 脑（ADR 0082）的身份纪律。与 BoundaryReadOnly 分开——脑不只是"只读"：
    /// 它的产出形状（一段话总结、给结论）与可追问语义是它区别于委派的身份。
    /// </summary>
    public const string BrainDiscipline =
        "你是主代理临时雇的整理人：只读，只能使用 `Read` 与 `Grep` 两把工具。\n" +
        "任务结束用一段话总结发现，先给结论；派活者想追问细节时会把同一会话编号传回来，你基于这份材料继续深挖。";

    /// <summary>
    /// 评审档（ADR 0112）的身份纪律。工具集与通用档相同（能不能改由权限档定），区别全在这里：
    /// </summary>
    public const string ReviewDiscipline = "你是来评审的：看清现状，给出判断——你的活是评，不是改，发现问题写进结论里。";
    
    /// <summary>
    /// 协作段正文
    /// </summary>
    public const string Collaboration = MessagingLine + CollaborationBody;

    /// <summary>脑的协作段：不含「你可以给主代理发消息」——脑是被雇来整理的，可被追问、不主动开口（ADR 0082）</summary>
    public const string BrainCollaboration = CollaborationBody;

    /// <summary>协作段共用的主体（不含主动联系那条）</summary>
    private const string CollaborationBody =
        $"""
            - 给你发消息的人会就这件事继续追问、纠偏、和你讨论。这不是一次性交办：
              哪一轮交终版结论、哪一轮只是往下谈，你自己判断。
              用户也可能直接与你对话，主代理追问消息会以 {SubAgentTool.ParentInterjectionPrefix} 开头，用户指令没有这个前缀，认前缀不要猜；
            - 凡是以 {SubAgentTool.ParentInterjectionPrefix} 开头的消息都是等你回答的问题：必须直接回应，不能只收下不答。
            - 事情的前提、目标或方案本身有问题时，直接指出并问回去，不要只顺着做。
            - 事情里没给的信息不要猜着补。缺什么就说缺什么——可以先做能做的部分，也可以直接停下来问回去。
            - 交终版结论时先给结论、再给依据（路径、链接、原文）；讨论轮里不必套这个格式。
            - 每一轮都要以一段文本收尾，这段文本会原样回送给主代理，不要让工具调用做最后一个动作，但是不要跟收尾的 SendMessage 信息重复(不然 {SubAgentTool.ParentInterjectionPrefix} 会看到双份同样内容)。
        """;

    /// <summary>「你可以主动联系主代理」那条，只进通用档（脑不反问）</summary>
    private const string MessagingLine =
        $"- 你可以用 `{ParentMessageTool.ToolName}` 给主代理发消息：要信息、请他拍板，或中途汇报都行。\n" +
        $"  他的回复会以 {SubAgentTool.ParentInterjectionPrefix} 开头送到你这边；发出后先做能做的部分，不用干等。\n";

    /// <summary>代码兜底追加总结轮时的提问</summary>
    public const string SummaryPrompt = "请用一段话总结这一轮的发现和结论，作为本轮结论。";
}