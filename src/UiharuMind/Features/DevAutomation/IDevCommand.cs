/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

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

    /// <summary>一行用法：做什么、带哪些参数。控制通道的 <c>app.ops</c> 与内置技能 uiharu-dev 都从这里取，不另写一份</summary>
    string Usage { get; }

    /// <summary>
    /// 执行一步
    /// </summary>
    /// <param name="args">脚本里的 <c>args</c>；没带时为 <c>undefined</c></param>
    /// <returns>写进报告的结果，可为 null</returns>
    JsonNode? Execute(JsonElement args);
}

/// <summary>
/// 要等一阵才有结果的一步（等群跑完、等会话装载）。同样在 UI 线程上执行，执行器会等它的任务结束
/// </summary>
public interface IAsyncDevCommand : IDevCommand
{
    JsonNode? IDevCommand.Execute(JsonElement args) => throw new NotSupportedException($"'{Name}' is async");

    /// <summary>
    /// 执行一步
    /// </summary>
    /// <param name="args">脚本里的 <c>args</c>；没带时为 <c>undefined</c></param>
    /// <returns>写进报告的结果，可为 null</returns>
    Task<JsonNode?> ExecuteAsync(JsonElement args);
}

/// <summary>一步的用法：步骤名与一行说明（含参数）</summary>
/// <param name="Op">步骤名</param>
/// <param name="Usage">做什么、带哪些参数</param>
public sealed record DevStepUsage(string Op, string Usage);

/// <summary>步骤结果的拼装：结果直接是 JSON 节点，报告与控制通道原样写出，不经序列化（AOT 下匿名对象写不出来）</summary>
internal static class DevJson
{
    /// <summary>
    /// 把一组条目逐个转成节点
    /// </summary>
    /// <param name="items">条目</param>
    /// <param name="select">单条怎么写</param>
    /// <typeparam name="T">条目类型</typeparam>
    /// <returns>JSON 数组</returns>
    public static JsonArray Array<T>(IEnumerable<T> items, Func<T, JsonNode?> select)
    {
        JsonArray array = new();
        foreach (T item in items) array.Add(select(item));
        return array;
    }

    /// <summary>
    /// 把一张计数表写成对象
    /// </summary>
    /// <param name="counts">键与计数</param>
    /// <returns>JSON 对象</returns>
    public static JsonObject Counts(IEnumerable<KeyValuePair<string, int>> counts)
    {
        JsonObject result = new();
        foreach ((string key, int count) in counts) result[key] = count;
        return result;
    }
}
