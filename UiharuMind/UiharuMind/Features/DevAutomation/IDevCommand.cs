/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Text.Json;

namespace UiharuMind.Features.DevAutomation;

/// <summary>
/// 开发脚本里的一步。
///
/// <b>一步一个类</b>：加一步不必动执行器，也不必读懂脚本文件那一层。
/// 每一步都在 UI 线程上执行（由 <see cref="DevScriptRunner"/> 保证），
/// 因此实现里可以像界面代码一样直接读写视图模型。
///
/// ⚠️ 只许走<b>公开的视图模型面</b>——读属性、设属性、调 RelayCommand，
/// 也就是用户点一下会走的那条路。为自动化单开一条特权入口，测出来的就不是用户那条路了，
/// 而且那条入口会一直留在出货代码里。
/// </summary>
public interface IDevCommand
{
    /// <summary>步骤名（脚本里的 <c>op</c>）</summary>
    string Name { get; }

    /// <summary>
    /// 执行一步
    /// </summary>
    /// <param name="args">脚本里的 <c>args</c>；没带时为 <c>undefined</c></param>
    /// <returns>写进报告的结果，可为 null</returns>
    object? Execute(JsonElement args);
}
