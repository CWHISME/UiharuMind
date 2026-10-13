using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;

namespace UiharuMind.Core.AI.Execution.Tools;

/// <summary>
/// A1 尖刺验证入口（public 壳，内部用 internal 的 <see cref="AotToolFunction"/>）：
/// 在真实 AOT 产物里创建手写 AotToolFunction 并实际调用一次，证明零反射可用。
/// </summary>
public static class AotSpike
{
    public static async Task<object?> VerifyAsync(string filePath)
    {
        JsonNode schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["filePath"] = new JsonObject { ["type"] = "string", ["description"] = "文件路径" },
            },
            ["required"] = new JsonArray(JsonValue.Create("filePath")),
        };

        AotToolFunction tool = new("aot_spike_read", "读取文件（A1 尖刺）", AotToolFunction.SchemaFromNode(schema),
            // 完全同步读：调用侧在 UI 线程 GetResult 等。任何 await（含 Task.Run 后 await 回 UI）都会死锁。
            (args, ct) => Task.FromResult<object?>(
                File.ReadAllText(AotToolFunction.Marshaller.String(args, "filePath")!)));

        AIFunctionArguments callArgs = new() { ["filePath"] = filePath };
        object? result = await tool.InvokeAsync(callArgs, CancellationToken.None);
        // 显式转 string 返回：绕开 dev-report 写盘的 object 反射序列化（那才是 AOT 下 EmptyJsonTypeInfoResolver 的源头）
        return result?.ToString() ?? "";
    }
}