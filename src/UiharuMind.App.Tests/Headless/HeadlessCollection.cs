// 整个程序集不并行。光靠下面的集合还不够：会话每次 Dispatch 都会重置 Dispatcher.UIThread，重置后谁先碰它谁就是界面线程，
// 别的集合里并行跑的测试一 Post 就可能把它抢走。串行全量只慢几秒（实测约 41 秒到 44 秒）；
// 串行后剩下的是前面测试遗留的后台续体，由 HeadlessUi 重试建应用那一步兜住
[assembly: Xunit.v3.Parallelization(Mode = Xunit.Sdk.ParallelMode.None)]

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
