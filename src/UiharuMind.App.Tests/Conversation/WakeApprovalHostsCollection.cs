namespace UiharuMind.App.Tests.Conversation;

/// <summary>
/// 会改 <see cref="WakeApprovalHosts"/> 的进程级登记（谁接唤醒轮的审批、无人值守策略）的测试类挂这里、
/// 彼此串行：那是全局状态，两个类同时写就会互相顶掉，断言随机红
/// </summary>
[CollectionDefinition(Name)]
public sealed class WakeApprovalHostsCollection
{
    public const string Name = "WakeApprovalHosts";
}
