using System.Text.Json;
using System.Text.Json.Nodes;
using CliFx;
using CliFx.Binding;
using CliFx.Infrastructure;
using UiharuMind.Core.Core.DevControl;

namespace UiharuMind.CLI.Commands.App;

[Command("app run", Description = "Run a dev script (JSONL, same format as --dev-script) through the control channel.")]
public partial class AppRunCommand : ICommand
{
    private static readonly JsonSerializerOptions ReportOptions = new(DevControlServer.ReplyOptions) { WriteIndented = true };

    [CommandParameter(0, Description = "Script path; one {\"op\":…,\"args\":…} per line. {\"op\":\"quit\"} quits the app.")]
    public required string Script { get; set; }

    [CommandOption("report", Description = "Report path; defaults to <script>.report.json. Written after every step.")]
    public string? Report { get; set; }

    [CommandOption("home", Description = "Profile directory; defaults to UIHARU_HOME or ~/.uiharu.")]
    public string? Home { get; set; }

    public async ValueTask ExecuteAsync(IConsole console)
    {
        string reportPath = Report ?? Path.ChangeExtension(Script, ".report.json");
        string[] lines = await File.ReadAllLinesAsync(Script);
        JsonArray report = new();
        bool failed = false;
        await using var client = await AppControl.ConnectAsync(AppControl.EndpointOf(Home), CancellationToken.None);

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal)) continue;

            JsonObject entry = new() { ["step"] = i + 1 };
            string op = "<unparsed>";
            try
            {
                JsonNode step = JsonNode.Parse(line) ?? throw new JsonException("empty step");
                op = step["op"]?.GetValue<string>() ?? throw new JsonException("missing 'op'");
                JsonObject reply = await client.CallAsync(op == "quit" ? "app.quit" : op, step["args"]);
                entry["op"] = op;
                foreach ((string key, JsonNode? value) in reply)
                {
                    if (key != "id") entry[key] = value?.DeepClone();
                }
            }
            catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException)
            {
                // 这一行写坏了（不是 JSON、op 不是字符串……）：记下，接着跑下一行
                entry["op"] = op;
                entry["ok"] = false;
                entry["error"] = $"{e.GetType().Name}: {e.Message}";
            }
            catch (IOException e)
            {
                // 应用退了或崩了：这一步记下来再收手，后面的发不出去
                entry["op"] = op;
                entry["ok"] = false;
                entry["error"] = $"lost the app: {e.Message}";
                report.Add(entry);
                await File.WriteAllTextAsync(reportPath, report.ToJsonString(ReportOptions));
                throw new CommandException($"[{i + 1}] {op}: lost the app ({e.Message}); see {reportPath}", 2);
            }

            bool ok = entry["ok"]?.GetValue<bool>() == true;
            failed |= !ok;
            report.Add(entry);
            await File.WriteAllTextAsync(reportPath, report.ToJsonString(ReportOptions));
            await console.Output.WriteLineAsync(
                $"[{i + 1}] {op} {(ok ? "ok" : "FAILED")} {entry["elapsedMs"]}ms{(ok ? "" : $": {entry["error"]}")}");
            if (op == "quit") break;
        }

        if (failed) throw new CommandException($"some steps failed; see {reportPath}", 1);
    }
}
