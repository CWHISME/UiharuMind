using System;
using System.Text.Json;
using UiharuMind.Core.AI.Execution.Tools;

namespace UiharuMind.Features.DevAutomation;

/// <summary>
/// A1 尖刺验证命令：在真实 AOT 产物里创建手写 AotToolFunction（Read 代表）并实际调用一次，
/// 验证零反射可用。绕过装配链（其它工具族仍是 AIFunctionFactory 反射，A2 全量前会被挡）。
/// </summary>
internal sealed class AotSpikeCommand : IDevCommand
{
    public string Name => "aot.spike";

    public string Usage => "A1 尖刺：创建手写 AotToolFunction 并调用（零反射验证）。path（要读的文件）";

    public object? Execute(JsonElement args)
    {
        string path = args.ValueKind == JsonValueKind.Object && args.TryGetProperty("path", out JsonElement p)
            ? p.GetString() ?? ""
            : "/tmp/uiharu-aot-sess/Logs/Log.txt";

        object? result = AotSpike.VerifyAsync(path)
            .GetAwaiter().GetResult();
        string? text = result?.ToString();
        return new { ok = result != null, length = text?.Length ?? 0, head = text?[..Math.Min(120, text.Length)] };
    }
}