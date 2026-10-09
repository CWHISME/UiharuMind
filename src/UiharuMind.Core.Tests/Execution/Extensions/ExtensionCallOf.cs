using System.Text.Json;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution.Extensions;

namespace UiharuMind.Core.Tests.Execution.Extensions;

/// <summary>测试里造一次 ExtensionCall：参数对象与模型发来的一样是 JsonElement</summary>
internal static class ExtensionCallOf
{
    public static FunctionCallContent Make(string extension, string command, object? arguments = null,
        string callId = "c1")
    {
        Dictionary<string, object?> args = new()
        {
            ["extension"] = JsonSerializer.SerializeToElement(extension),
            ["command"] = JsonSerializer.SerializeToElement(command),
        };
        if (arguments != null) args["arguments"] = JsonSerializer.SerializeToElement(arguments);
        return new FunctionCallContent(callId, ExtensionCalls.CallName, args);
    }
}
