using System.Text.Json.Nodes;
using CliFx;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.DevControl;

namespace UiharuMind.CLI.Commands.App;

/// <summary>
/// <c>app</c> 一组命令共用的连接与收发（ADR 0059）
/// </summary>
internal static class AppControl
{
    /// <summary>
    /// 档案目录对应的通道
    /// </summary>
    /// <param name="home">档案目录；为空时与应用同一套解析（<c>UIHARU_HOME</c> 优先）</param>
    /// <returns>通道位置</returns>
    public static DevControlEndpoint EndpointOf(string? home) =>
        string.IsNullOrWhiteSpace(home) ? DevControlEndpoint.Default : new DevControlEndpoint(Path.GetFullPath(home));

    /// <summary>
    /// 连上通道；没开时给出能照着做的提示
    /// </summary>
    /// <param name="endpoint">通道位置</param>
    /// <param name="token">取消</param>
    /// <returns>客户端</returns>
    public static async Task<DevControlClient> ConnectAsync(DevControlEndpoint endpoint, CancellationToken token)
    {
        try
        {
            return await DevControlClient.ConnectAsync(endpoint, token);
        }
        catch (InvalidOperationException e)
        {
            throw new CommandException($"{e.Message}. Start it with 'app start', or turn on developer mode in the running app.", 2);
        }
    }

    /// <summary>
    /// 应用是否在这个档案上开着通道
    /// </summary>
    /// <param name="endpoint">通道位置</param>
    /// <returns>应用进程号；没开为 null</returns>
    public static async Task<int?> PingAsync(DevControlEndpoint endpoint)
    {
        try
        {
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(3));
            await using DevControlClient client = await DevControlClient.ConnectAsync(endpoint, timeout.Token);
            JsonObject reply = await client.CallAsync("app.ping", token: timeout.Token);
            return reply["result"]?["pid"]?.GetValue<int>();
        }
        catch (Exception e) when (e is InvalidOperationException or IOException or OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>
    /// 解析 <c>--args</c>
    /// </summary>
    /// <param name="json">JSON 对象文本；为空时无参数</param>
    /// <returns>参数</returns>
    public static JsonNode? ParseArgs(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonNode.Parse(json);
        }
        catch (System.Text.Json.JsonException e)
        {
            throw new CommandException($"--args is not valid JSON: {e.Message}", 2);
        }
    }

    /// <summary>给 <c>--timeout</c> 秒数建取消；0 为不限</summary>
    public static CancellationTokenSource TimeoutOf(int seconds) =>
        seconds > 0 ? new CancellationTokenSource(TimeSpan.FromSeconds(seconds)) : new CancellationTokenSource();
}
