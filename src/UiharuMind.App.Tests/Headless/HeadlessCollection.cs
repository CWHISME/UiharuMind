namespace UiharuMind.App.Tests.Headless;

/// <summary>
/// 所有无头界面测试共用的 xunit 集合，作用只有一个：<b>它们之间不许并行</b>。
///
/// 会话只有一条调度线程，而 xunit 默认按集合并行。两个测试同时 <c>Dispatch</c> 时，
/// 后一个会在前一个的控件树还活着的时候另起一个应用实例，于是随机撞出
/// 「The calling thread cannot access this object」——只在跑全量时复现，单测一个类永远是绿的。
///
/// 新增无头测试类记得挂 <c>[Collection(HeadlessCollection.Name)]</c>。
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class HeadlessCollection
{
    /// <summary>集合名</summary>
    public const string Name = "headless-ui";
}
