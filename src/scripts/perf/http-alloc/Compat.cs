using System.Text;
using System.Text.Json.Nodes;
using HttpAllocBench.Proto;
using UiharuMind.Core.AI;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.AI.Net;

namespace HttpAllocBench;

/// <summary>
/// 差分核对：P2 原型与现状实现逐例比。请求改写按 JSON 语义比，请求体日志按字面比。
/// 已知差异单独标出、不算失败——它们是现状的毛病，原型是对的
/// </summary>
internal static class Compat
{
    /// <returns>进程退出码：0 全部一致（已知差异除外），1 有不一致</returns>
    public static int Run()
    {
        int failures = RewriterCases() + LogCases();
        Console.WriteLine(failures == 0 ? "差分核对：全部一致（已知差异除外）" : $"差分核对：{failures} 处不一致（见上）");
        return failures == 0 ? 0 : 1;
    }

    private static int RewriterCases()
    {
        string[] bodies =
        [
            """{"model":"m","messages":[]}""",
            """{"model":"m","max_tokens":5,"messages":[]}""",
            """{"messages":[],"tool_choice":"auto","temperature":0.5,"top_p":1,"model":"m"}""",
            """{"model":"m","tools":[{"type":"function","function":{"name":"t","parameters":{"type":"object","properties":{"max_tokens":{"type":"integer"},"temperature":{"type":"number"}}}}}],"messages":[]}""",
            """{"model":"m","messages":[{"role":"assistant","content":null,"tool_calls":[{"id":"c1","type":"function","function":{"name":"f","arguments":"null"}}]},{"role":"tool","tool_call_id":"c1","content":"ok"}]}""",
            """{"model":"m","messages":[{"role":"assistant","reasoning_content":null,"tool_calls":[{"id":"c1","type":"function","function":{"name":"f","arguments":"{}"}}]}]}""",
            """{"model":"m","messages":[{"role":"assistant","reasoning_content":"已有","tool_calls":[{"id":"c1","type":"function","function":{"name":"f","arguments":"{}"}}]}]}""",
            """{"model":"m","messages":[{"role":"assistant","tool_calls":[{"id":"x","type":"function","function":{"name":"f","arguments":"{}"}},{"id":"c2","type":"function","function":{"name":"g","arguments": "null"}}]}]}""",
            """{"model":"m","messages":[{"role":"user","content":[{"type":"text","text":"说 \"arguments\":\"null\" 不该改"}]}]}""",
            """{}""",
            """[1,2,3]""",
        ];
        Dictionary<string, string> reasoning = new() { ["c1"] = "思考<一>&\"引号\"", ["c2"] = "第二段" };
        (string Name, bool DeepSeek, bool OmitSampling)[] modes =
        [
            ("max_tokens + thinking", false, false),
            ("DeepSeek 回填思考", true, false),
            ("Kimi 删采样参数", false, true),
        ];

        int failures = 0;
        foreach (string body in bodies)
        {
            foreach (var (name, deepSeek, omitSampling) in modes)
            {
                foreach (bool forbid in new[] { false, true })
                {
                    var model = new CompatModel(deepSeek, omitSampling);
                    string current = Task.Run(() =>
                    {
                        // 请求上下文是 AsyncLocal：放进单独的异步流里设，不漏到别处
                        LlmRequestContext.ForbidToolCalls = forbid;
                        LlmRequestContext.PendingReasoningByCallId = reasoning;
                        try
                        {
                            return OpenAICompatibleRequestRewriter.Rewrite(body, model);
                        }
                        catch (Exception e)
                        {
                            return "!" + e.GetType().Name;
                        }
                    }).GetAwaiter().GetResult();
                    byte[]? spliced = Utf8RequestRewriter.Rewrite(Encoding.UTF8.GetBytes(body), model.GetExtraParams(),
                        forbid, deepSeek ? reasoning : null, omitSampling);
                    string proto = spliced == null ? body : Encoding.UTF8.GetString(spliced);
                    if (current == proto || SameJson(current, proto)) continue;
                    if (current == "!InvalidOperationException" && body.StartsWith('['))
                    {
                        Console.WriteLine($"  [请求改写] 已知差异：根不是对象时现状 AsObject() 直接抛，原型原样返回（{name}，禁工具 {forbid}）");
                        continue;
                    }

                    failures++;
                    Console.WriteLine($"  [请求改写] {name}，禁工具 {forbid} 不一致\n    输入 {body}\n    现状 {current}\n    原型 {proto}");
                }
            }
        }

        return failures;
    }

    private static int LogCases()
    {
        (string Body, bool KnownBug)[] cases =
        [
            ("""{"a":"第一行\n第二行","b":[1,2.50,-3e10,true,false,null],"c":{},"d":[],"e":"\u4e2d\u6587","f":"<&>'+"}""", false),
            ("{\"img\":\"data:image/png;base64," + new string('A', 2000) + "\",\"raw\":\"" + new string('Q', 600) +
             "\",\"short\":\"QUJD\"}", false),
            ("not json at all", false),
            ("""{"path":"C:\\new\\table"}""", true), //现状把转义的 \\n 也当换行
        ];

        int failures = 0;
        var buffer = new PooledByteWriter(1024);
        foreach (var (body, knownBug) in cases)
        {
            string current = LlmBodyLogFormat.ForLog(body);
            buffer.Clear();
            int length = Utf8BodyLogFormat.Format(Encoding.UTF8.GetBytes(body), buffer);
            string proto = length < 0 ? body : Encoding.UTF8.GetString(buffer.WrittenSpan[..length]);
            if (current == proto) continue;
            if (knownBug)
            {
                Console.WriteLine($"  [请求体日志] 已知差异：{body} 现状把 \\\\n 显示成换行，原型保持原样");
                continue;
            }

            failures++;
            Console.WriteLine($"  [请求体日志] 不一致\n    输入 {body[..Math.Min(body.Length, 120)]}\n    现状 {current[..Math.Min(current.Length, 300)]}\n    原型 {proto[..Math.Min(proto.Length, 300)]}");
        }

        return failures;
    }

    private static bool SameJson(string a, string b)
    {
        try
        {
            return JsonNode.DeepEquals(JsonNode.Parse(a), JsonNode.Parse(b));
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
    }

    private sealed class CompatModel(bool deepSeek, bool omitSampling) : ILlmModel
    {
        public string ModelName => "m";
        public string ModelPath => "http://localhost";
        public bool IsVision => false;
        public string ModelDescription => "";
        public string ModelId => "m";
        public int Port => 0;
        public bool RequiresReasoningContentRoundtrip => deepSeek;
        public bool OmitSamplingParams => omitSampling;

        public IReadOnlyList<KeyValuePair<string, JsonNode?>>? GetExtraParams() =>
            [new("max_tokens", JsonValue.Create(65535)), new("thinking", new JsonObject { ["type"] = "enabled" })];
    }
}
