/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Text.Json;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Assembly;
using UiharuMind.Core.AI.Execution.Files;
using UiharuMind.Core.AI.Execution.Mcp;
using UiharuMind.Core.AI.Execution.Prompts;
using UiharuMind.Core.AI.Execution.Tools;
using UiharuMind.Core.Core;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 按需送达（ADR 0051）的配置层不变量：元工具恒定、名单只含配置信息、
/// 送达方式怎么落盘、查找的分支、以及超限结果怎么落盘。都不碰单例与网络。
/// </summary>
public class McpOnDemandTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"mcp-ondemand-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private static McpOnDemandServer OnDemand(string name, string description = "") =>
        new() { Name = name, Description = description };

    private static IReadOnlyList<AIFunction> MetaTools() =>
        McpMetaTools.Create(new McpBridge(new NullHost(), null, [], Path.GetTempPath()));

    //================= 元工具恒定 + 名单只含配置 =================

    [Fact]
    public void OnDemand_MountsBothMetaToolsAndNothingElse()
    {
        McpToolSet set = McpToolSetBuilder.Build([], [OnDemand("github")], MetaTools());

        Assert.Equal([McpMetaTools.HelpName, McpMetaTools.CallName], set.Tools.Select(x => x.Name));
    }

    /// <summary>缓存前缀稳定的根：工具定义与 server 是否连上、有多少工具毫无关系</summary>
    [Fact]
    public void MetaToolDefinitions_AreIdenticalAcrossBuilds()
    {
        string first = Describe(MetaTools());
        string second = Describe(MetaTools());

        Assert.Equal(first, second);

        static string Describe(IReadOnlyList<AIFunction> tools) => string.Join('\n',
            tools.Select(x => $"{x.Name}|{x.Description}|{x.JsonSchema.GetRawText()}"));
    }

    [Fact]
    public void Roster_HasNamesAndDescriptionsButNoToolsOrCounts()
    {
        McpToolSet set = McpToolSetBuilder.Build([], [OnDemand("github", "GitHub 仓库与 issue"), OnDemand("unity")],
            MetaTools());

        Assert.Contains("- github：GitHub 仓库与 issue", set.Instructions);
        Assert.Contains("- unity", set.Instructions);
        Assert.Contains(McpMetaTools.HelpName, set.Instructions);
        Assert.Contains(McpMetaTools.CallName, set.Instructions);
    }

    /// <summary>防的是实机那次：模型信了 server 自带"列出工具"里被关掉的名字。以 McpHelp 为准要写进提示</summary>
    [Fact]
    public void Roster_TellsTheModelMcpHelpIsTheAuthorityOnCallableTools()
    {
        McpToolSet set = McpToolSetBuilder.Build([], [OnDemand("unity")], MetaTools());

        Assert.Contains("以 `McpHelp` 列出的为准", set.Instructions);
    }

    [Fact]
    public void Roster_IsIndependentOfWhichToolsExist()
    {
        // 同一份按需名单，直挂部分怎么变都不影响名单那一段
        string bare = McpToolSetBuilder.Build([], [OnDemand("github", "desc")], MetaTools()).Instructions;
        McpToolSet mixed = McpToolSetBuilder.Build(
            [new ResolvedMcpServer(new McpServerConfig { Name = "unity", MountMode = EMcpMountMode.Direct },
                [AIFunctionFactory.Create(() => "ok", "read_scene", "d")], "")],
            [OnDemand("github", "desc")], MetaTools());

        Assert.EndsWith(bare, mixed.Instructions);
    }

    [Fact]
    public void OnDemand_GroupsCarryNoToolsAndAreMarked()
    {
        McpToolSet set = McpToolSetBuilder.Build([], [OnDemand("github")], MetaTools());

        McpServerToolGroup group = Assert.Single(set.Groups);
        Assert.Equal(EMcpMountMode.OnDemand, group.MountMode);
        Assert.Empty(group.Tools);
        Assert.Equal(0, group.EstimatedTokens);
    }

    /// <summary>按需省的是工具定义，但两个元工具加名单是恒定开销，账上必须看得见</summary>
    [Fact]
    public void OnDemand_CountsFixedOverheadInTotal()
    {
        McpToolSet set = McpToolSetBuilder.Build([], [OnDemand("github", "desc")], MetaTools());

        Assert.True(set.EstimatedTokens > 0);
    }

    [Fact]
    public void NoOnDemandServers_LeavesToolSetUntouched()
    {
        McpToolSet set = McpToolSetBuilder.Build([], [], MetaTools());

        Assert.Same(McpToolSet.Empty, set);
    }

    //================= 参数宽容 =================

    [Fact]
    public void ReadObject_AcceptsJsonObjectAndJsonString()
    {
        JsonElement obj = JsonSerializer.SerializeToElement(new { q = "a", n = 2 });
        AIFunctionArguments asObject = new() { ["arguments"] = obj };
        AIFunctionArguments asString = new() { ["arguments"] = "{\"q\":\"a\",\"n\":2}" };
        AIFunctionArguments doubleEncoded = new() { ["arguments"] = JsonSerializer.SerializeToElement("{\"q\":\"a\"}") };

        Assert.Equal("a", McpMetaTools.ReadObject(asObject, "arguments")!["q"].GetString());
        Assert.Equal(2, McpMetaTools.ReadObject(asString, "arguments")!["n"].GetInt32());
        Assert.Equal("a", McpMetaTools.ReadObject(doubleEncoded, "arguments")!["q"].GetString());
    }

    [Fact]
    public void ReadObject_GarbageOrMissing_IsNullNotAnException()
    {
        Assert.Null(McpMetaTools.ReadObject(new AIFunctionArguments { ["arguments"] = "not json" }, "arguments"));
        Assert.Null(McpMetaTools.ReadObject(new AIFunctionArguments(), "arguments"));
        Assert.Null(McpMetaTools.ReadObject(new AIFunctionArguments { ["arguments"] = 42 }, "arguments"));
    }

    [Fact]
    public void ReadString_AcceptsElementAndPlainString()
    {
        AIFunctionArguments args = new()
        {
            ["a"] = "plain",
            ["b"] = JsonSerializer.SerializeToElement("element"),
            ["c"] = JsonSerializer.SerializeToElement((string?)null),
        };

        Assert.Equal("plain", McpMetaTools.ReadString(args, "a"));
        Assert.Equal("element", McpMetaTools.ReadString(args, "b"));
        Assert.Null(McpMetaTools.ReadString(args, "c"));
        Assert.Null(McpMetaTools.ReadString(args, "missing"));
    }

    //================= 查找分支 =================

    private static McpServerConfig Server(string name, EMcpMountMode mode = EMcpMountMode.OnDemand,
        bool enabled = true) => new() { Name = name, MountMode = mode, IsEnabled = enabled };

    private static McpServerLookup Find(string name, IReadOnlyList<McpServerConfig> servers,
        string[]? disabled = null, Func<McpServerConfig, bool>? trusted = null) =>
        McpServerFinder.Find(servers, McpManager.DisabledSet(disabled), trusted ?? (_ => true), name);

    [Fact]
    public void Find_UsableOnDemandServer_IsFoundIgnoringCase()
    {
        McpServerLookup lookup = Find(" GITHUB ", [Server("github")]);

        Assert.Equal(EMcpLookupStatus.Found, lookup.Status);
        Assert.Equal("github", lookup.Server!.Name);
    }

    [Fact]
    public void Find_ReportsTheFirstReasonItCannotBeUsed()
    {
        Assert.Equal(EMcpLookupStatus.NotFound, Find("nope", [Server("github")]).Status);
        Assert.Equal(EMcpLookupStatus.HostingOff, Find("github", [Server("github", enabled: false)]).Status);
        Assert.Equal(EMcpLookupStatus.DisabledByCharacter, Find("github", [Server("github")], ["github"]).Status);
        Assert.Equal(EMcpLookupStatus.NeedsApproval, Find("github", [Server("github")], trusted: _ => false).Status);
        Assert.Equal(EMcpLookupStatus.MountedDirectly,
            Find("github", [Server("github", EMcpMountMode.Direct)]).Status);
    }

    [Fact]
    public void Find_AvailableListsOnlyUsableOnDemandServers()
    {
        McpServerLookup lookup = Find("nope", [
            Server("a"), Server("direct", EMcpMountMode.Direct), Server("off", enabled: false),
            Server("blocked"), Server("untrusted"),
        ], ["blocked"], x => x.Name != "untrusted");

        Assert.Equal(["a"], lookup.Available);
    }

    //================= 送达方式的落盘 =================

    [Fact]
    public void LocalState_DefaultsToOnDemand_SoOldConfigsFlipWithoutMigration()
    {
        McpServersFile file = new() { McpServers = { ["github"] = new McpServerEntry { Command = "npx" } } };

        McpServerConfig config = Assert.Single(file.ToConfigs(new Dictionary<string, McpServerLocalState>()));

        Assert.Equal(EMcpMountMode.OnDemand, config.MountMode);
        Assert.Equal(string.Empty, config.Description);
    }

    [Fact]
    public void ModeAndDescription_RoundTripThroughLocalStateNotTheStandardFile()
    {
        McpServerConfig config = new()
        {
            Name = "unity", Command = "uvx", MountMode = EMcpMountMode.Direct, Description = "Unity 编辑器",
        };

        var (standard, states) = McpServersFile.FromConfigs([config]);
        McpServerConfig restored = Assert.Single(standard.ToConfigs(states));

        Assert.Equal(EMcpMountMode.Direct, restored.MountMode);
        Assert.Equal("Unity 编辑器", restored.Description);
        // 标准文件保持生态标准形状，不被本项目特有字段污染
        string json = JsonSerializer.Serialize(standard);
        Assert.DoesNotContain("Direct", json);
        Assert.DoesNotContain("Unity 编辑器", json);
    }

    [Fact]
    public void Fingerprint_IgnoresMountModeAndDescription()
    {
        McpServerConfig a = new() { Name = "x", Command = "npx" };
        McpServerConfig b = a.Clone();
        b.MountMode = EMcpMountMode.Direct;
        b.Description = "changed";

        Assert.Equal(McpServerFingerprint.Of(a), McpServerFingerprint.Of(b));
    }

    //================= 项目级送达方式账本 =================

    [Fact]
    public void WorkspaceMountStore_OnlyRemembersNonDefaultAndSurvivesReload()
    {
        string file = Path.Combine(_directory, "mounts.json");
        string workspace = Path.Combine(_directory, "proj");
        McpWorkspaceMountStore store = new(file);

        Assert.Equal(EMcpMountMode.OnDemand, store.Get(workspace, "unity"));
        Assert.True(store.Set(workspace, "unity", EMcpMountMode.Direct));
        Assert.False(store.Set(workspace, "unity", EMcpMountMode.Direct)); //没变不落盘

        McpWorkspaceMountStore reloaded = new(file);
        reloaded.Reload();
        Assert.Equal(EMcpMountMode.Direct, reloaded.Get(workspace, "UNITY"));
        Assert.Equal(EMcpMountMode.OnDemand, reloaded.Get(Path.Combine(_directory, "other"), "unity"));

        Assert.True(reloaded.Set(workspace, "unity", EMcpMountMode.OnDemand)); //改回默认即删除记录
        McpWorkspaceMountStore again = new(file);
        again.Reload();
        Assert.Equal(EMcpMountMode.OnDemand, again.Get(workspace, "unity"));
    }

    [Fact]
    public void WorkspaceMountStore_UnreadableFile_IsTreatedAsEmpty()
    {
        string file = Path.Combine(_directory, "broken.json");
        Directory.CreateDirectory(_directory);
        File.WriteAllText(file, "{ not json");
        McpWorkspaceMountStore store = new(file);

        store.Reload();

        Assert.Equal(EMcpMountMode.OnDemand, store.Get(_directory, "x"));
    }

    //================= 工具清单缓存 =================

    private static McpToolCatalog Catalog(string fingerprint) => new()
    {
        Fingerprint = fingerprint,
        Instructions = "notes",
        CapturedUtc = DateTime.UtcNow,
        Tools =
        [
            new McpToolDescriptor
            {
                Name = "search",
                Description = "d",
                InputSchema = JsonSerializer.SerializeToElement(new { type = "object" }),
            },
        ],
    };

    [Fact]
    public void ToolCatalogStore_RoundTripsAndRejectsStaleFingerprint()
    {
        McpToolCatalogStore store = new(_directory);
        McpServerKey key = new(null, "github");

        store.Save(key, Catalog("fp1"));

        McpToolCatalog? hit = store.Load(key, "fp1");
        Assert.NotNull(hit);
        Assert.Equal("search", Assert.Single(hit.Tools).Name);
        Assert.Equal("object", hit.Tools[0].InputSchema.GetProperty("type").GetString());
        Assert.Null(store.Load(key, "fp2")); //命令改过，缓存属于另一条 server
    }

    [Fact]
    public void ToolCatalogStore_SeparatesSameNameAcrossWorkspaces()
    {
        McpToolCatalogStore store = new(_directory);
        McpServerKey a = new("/proj/a", "unity");
        McpServerKey b = new("/proj/b", "unity");

        store.Save(a, Catalog("fp"));

        Assert.NotEqual(store.PathFor(a), store.PathFor(b));
        Assert.Null(store.Load(b, "fp"));
    }

    [Fact]
    public void ToolCatalogStore_MissingOrCorruptFile_IsNullNotAnException()
    {
        McpToolCatalogStore store = new(_directory);
        McpServerKey key = new(null, "github");
        Assert.Null(store.Load(key, "fp"));

        Directory.CreateDirectory(_directory);
        File.WriteAllText(store.PathFor(key), "garbage");
        Assert.Null(store.Load(key, "fp"));
    }

    //================= 超限落盘 =================

    [Fact]
    public void Spill_UnderBudget_ReturnsTextUntouchedAndWritesNothing()
    {
        string result = ToolResultSpill.Limit("short", _directory, "stem");

        Assert.Equal("short", result);
        Assert.False(Directory.Exists(_directory));
    }

    [Fact]
    public void Spill_OverBudget_SavesFullTextAndReturnsSkeleton()
    {
        string big = string.Join('\n', Enumerable.Range(0, 30000).Select(i => $"line {i}"));

        string result = ToolResultSpill.Limit(big, _directory, "McpCall_x");

        string saved = Assert.Single(Directory.GetFiles(_directory));
        Assert.Contains(saved, result);
        Assert.Equal(big, File.ReadAllText(saved));
        Assert.True(result.Length < big.Length);
    }

    /// <summary>
    /// MCP 结果比网页更紧：实机一条 61745 字节的日志结果在 64KB 的网页预算下根本没触发截断，
    /// 整条进了历史、之后每一轮都重发
    /// </summary>
    [Fact]
    public void McpBudget_IsTighterThanWebFetch_AndKeepsAGenerousTail()
    {
        string logs = string.Join('\n', Enumerable.Range(0, 800).Select(i => $"[{i:D4}] log line {new string('x', 60)}"));
        Assert.InRange(System.Text.Encoding.UTF8.GetByteCount(logs), 50 * 1024, 64 * 1024);

        string result = ToolResultSpill.Limit(logs, _directory, "McpCall_unity_console");

        Assert.True(System.Text.Encoding.UTF8.GetByteCount(result) < 12 * 1024, "约 8KB 加一段说明");
        Assert.Contains("[0000] log line", result);
        Assert.Contains("[0799] log line", result); //最新的日志在末尾，必须留住
        Assert.Contains("full content saved to", result);
        Assert.Equal(logs, File.ReadAllText(Assert.Single(Directory.GetFiles(_directory))));
    }

    /// <summary>
    /// 实机的形状：日志结果是一整行紧凑 JSON。按行取头尾时单行只能返头，尾部（最新的日志）整个丢掉，
    /// 落盘文件也只有一行、没法按行号续读。超限的 JSON 必须先缩进成多行
    /// </summary>
    [Fact]
    public void OversizedSingleLineJson_KeepsTheTail_AndSavesALineAddressableFile()
    {
        string json = "{\"result\":[" + string.Join(",", Enumerable.Range(0, 400)
            .Select(i => $"{{\"t\":\"12:{i:D4}\",\"Message\":\"log entry {i} {new string('x', 60)}\"}}")) + "]}";
        Assert.DoesNotContain('\n', json);

        string result = ToolResultSpill.Limit(json, _directory, "McpCall_unity_console");

        Assert.Contains("12:0000", result);
        Assert.Contains("12:0399", result); //最新的日志在末尾，必须留住
        Assert.DoesNotContain("tail omitted", result);
        string saved = File.ReadAllText(Assert.Single(Directory.GetFiles(_directory)));
        Assert.True(saved.Count(c => c == '\n') > 400, "落盘文件要能按行号续读");
        // 缩进只是排版，内容与原 JSON 等价
        Assert.Equal(400, JsonDocument.Parse(saved).RootElement.GetProperty("result").GetArrayLength());
    }

    [Fact]
    public void OversizedNonJson_IsUntouchedByIndenting()
    {
        string text = string.Join('\n', Enumerable.Range(0, 2000).Select(i => $"line {i} {new string('y', 20)}"));

        string result = ToolResultSpill.Limit(text, _directory, "s");

        Assert.Equal(text, File.ReadAllText(Assert.Single(Directory.GetFiles(_directory))));
        Assert.Contains("line 1999", result);
    }

    [Fact]
    public void PageBudget_StaysGenerousForWebPages()
    {
        Assert.Equal(64 * 1024, ToolOutputBudget.Page.MaxBytes);
        Assert.True(ToolOutputBudget.Compact.MaxBytes < ToolOutputBudget.Page.MaxBytes / 2);
        Assert.Equal(ToolOutputBudget.Compact.MaxBytes,
            ToolOutputBudget.Compact.HeadBytes + ToolOutputBudget.Compact.TailBytes);
    }

    [Fact]
    public void Spill_SameContentSameFile_DifferentContentDifferentFile()
    {
        Assert.Equal(ToolResultSpill.FileNameFor("s", "a"), ToolResultSpill.FileNameFor("s", "a"));
        Assert.NotEqual(ToolResultSpill.FileNameFor("s", "a"), ToolResultSpill.FileNameFor("s", "b"));
    }

    [Fact]
    public void Spill_UnwritableDirectory_StillReturnsTruncatedText()
    {
        string blocker = Path.Combine(_directory, "file");
        Directory.CreateDirectory(_directory);
        File.WriteAllText(blocker, "x");
        string big = new('a', ToolOutputTruncation.MaxBytes + 10);

        string result = ToolResultSpill.Limit(big, Path.Combine(blocker, "sub"), "stem");

        Assert.Contains("could not be saved", result);
    }

    [Fact]
    public void NormalizeResult_MediaFromEveryRawShapeEndsUpAsAPath()
    {
        DataContent image = new(new byte[] { 1, 2, 3 }, "image/png");
        DataContent audio = new(new byte[] { 4, 5 }, "audio/wav");

        string mixed = Assert.IsType<string>(McpCallResult.Normalize(
            new AIContent[] { new TextContent("hello"), image, audio }, _directory, "stem"));
        string alone = Assert.IsType<string>(McpCallResult.Normalize(image, _directory, "stem"));

        Assert.StartsWith("hello\n", mixed);
        Assert.Equal(2, Directory.GetFiles(Path.Combine(_directory, AgentOutputLayout.ImagesFolder)).Length); //同一张图只落一份
        Assert.Contains(".wav", mixed);
        Assert.DoesNotContain("hello", alone);
        Assert.Contains(".png", alone);
    }

    //================= CallToolResult 解包 =================

    private static JsonElement Result(string json) => JsonDocument.Parse(json).RootElement.Clone();

    /// <summary>钉住实机踩到的那个：同一份数据在 content 与 structuredContent 各一遍，且文本里满是转义</summary>
    [Fact]
    public void CallToolResult_UsesContentTextOnce_NotTheDuplicatedStructuredCopy()
    {
        JsonElement raw = Result("""
            {"content":[{"type":"text","text":"{\"result\":[{\"name\":\"a\"}]}"}],
             "structuredContent":{"result":[{"name":"a"}]},"isError":false}
            """);

        object result = McpCallResult.Normalize(raw, _directory, "s");

        Assert.Equal("""{"result":[{"name":"a"}]}""", result);
    }

    [Fact]
    public void CallToolResult_FallsBackToStructuredContentOnlyWhenThereIsNoText()
    {
        JsonElement raw = Result("""{"content":[],"structuredContent":{"n":1}}""");

        Assert.Equal("""{"n":1}""", McpCallResult.Normalize(raw, _directory, "s"));
    }

    [Fact]
    public void CallToolResult_ErrorIsMarkedSoItDoesNotReadAsAResult()
    {
        JsonElement raw = Result("""{"content":[{"type":"text","text":"file not found"}],"isError":true}""");

        Assert.Equal("Error: file not found", McpCallResult.Normalize(raw, _directory, "s"));
        Assert.StartsWith("Error:", Assert.IsType<string>(
            McpCallResult.Normalize(Result("""{"content":[],"isError":true}"""), _directory, "s")));
    }

    /// <summary>tool 消息只收文本：图片留在结果里会被整个序列化成 base64 塞给模型，所以落盘只给路径</summary>
    [Fact]
    public void CallToolResult_ImageBlocksAreSavedAndOnlyThePathIsReturned()
    {
        byte[] bytes = [1, 2, 3];
        string base64 = Convert.ToBase64String(bytes);
        JsonElement raw = Result($$"""
            {"content":[{"type":"text","text":"shot"},
                        {"type":"image","data":"{{base64}}","mimeType":"image/png"}]}
            """);

        string result = Assert.IsType<string>(McpCallResult.Normalize(raw, _directory, "s"));

        Assert.StartsWith("shot\n", result);
        Assert.DoesNotContain(base64, result);
        string saved = Assert.Single(Directory.GetFiles(Path.Combine(_directory, AgentOutputLayout.ImagesFolder)));
        Assert.EndsWith(".png", saved);
        Assert.Contains(saved, result);
        Assert.Equal(bytes, File.ReadAllBytes(saved));
    }

    /// <summary>落进会话房间时写成草稿目录简写，与 GenerateImage 同口径，模型拿它直接喂看图、改图</summary>
    [Fact]
    public void MediaSavedInTheRoom_IsReportedWithDraftShorthand()
    {
        DataContent image = new(new byte[] { 1, 2, 3 }, "image/png");

        string result = Assert.IsType<string>(
            McpCallResult.Normalize(image, _directory, "s", new AgentPathResolver(null, _directory)));

        Assert.Contains($"${AgentPathResolver.DraftVariable}/{AgentOutputLayout.ImagesFolder}/", result);
        Assert.DoesNotContain(_directory, result);
    }

    [Fact]
    public void ImageBlockWithUnreadableData_IsNotPassedThroughAsText()
    {
        const string junk = "!!!not-base64!!!";
        JsonElement raw = Result($$"""{"content":[{"type":"image","data":"{{junk}}","mimeType":"image/png"}]}""");

        string result = Assert.IsType<string>(McpCallResult.Normalize(raw, _directory, "s"));

        Assert.DoesNotContain(junk, result);
        Assert.Contains("image", result);
    }

    /// <summary>实机那条 61743 字符的日志结果：转义来自 server 自己的编码器，语义等价，整理掉纯省 token</summary>
    [Fact]
    public void JsonTextWithUnicodeEscapes_IsReserializedWithoutThem()
    {
        string escaped = """{"result":[{"Message":"\u003ECGet { \u0022rooms\u0022: 1 } \u4E2D\u6587"}]}""";

        string tidy = Assert.IsType<string>(McpCallResult.Normalize(new TextContent(escaped), _directory, "s"));

        Assert.Equal("""{"result":[{"Message":">CGet { \"rooms\": 1 } 中文"}]}""", tidy);
        Assert.True(tidy.Length < escaped.Length);
        // 语义完全等价
        Assert.Equal(JsonDocument.Parse(escaped).RootElement.GetProperty("result")[0].GetProperty("Message").GetString(),
            JsonDocument.Parse(tidy).RootElement.GetProperty("result")[0].GetProperty("Message").GetString());
    }

    [Fact]
    public void TidyJsonText_LeavesEverythingElseAlone()
    {
        Assert.Equal("""{"a":"plain"}""", McpCallResult.TidyJsonText("""{"a":"plain"}""")); //无转义
        Assert.Equal("""not json \u0022 at all""", McpCallResult.TidyJsonText("""not json \u0022 at all""")); //不是 JSON
        Assert.Equal("""{ broken \u0022""", McpCallResult.TidyJsonText("""{ broken \u0022""")); //解析失败
        Assert.Equal("""plain text with \u0022""", McpCallResult.TidyJsonText("""plain text with \u0022""")); //不以 { [ 开头
    }

    [Fact]
    public void CallToolResult_UnrecognizedShape_KeepsTheRawText()
    {
        Assert.Equal("""{"weird":true}""", McpCallResult.Normalize(Result("""{"weird":true}"""), _directory, "s"));
    }

    [Fact]
    public async Task DirectTools_GetTheSameResultShaping()
    {
        AIFunction raw = new RawJsonTool("""{"content":[{"type":"text","text":"ok"}],"structuredContent":{"x":1}}""");

        McpToolSet set = McpToolSetBuilder.Build(
            [new ResolvedMcpServer(new McpServerConfig { Name = "s", MountMode = EMcpMountMode.Direct }, [raw], "")],
            spillDirectory: _directory);

        AIFunction mounted = (AIFunction)Assert.Single(set.Tools);
        Assert.Equal("raw", mounted.Name);
        Assert.Equal("ok", await mounted.InvokeAsync(new AIFunctionArguments(), TestContext.Current.CancellationToken));
    }

    private sealed class RawJsonTool : AIFunction
    {
        private static readonly JsonElement Schema = JsonDocument.Parse("""{"type":"object"}""").RootElement.Clone();
        private readonly JsonElement _result;

        public RawJsonTool(string json) => _result = Result(json);

        public override string Name => "raw";
        public override string Description => "raw";
        public override JsonElement JsonSchema => Schema;

        protected override ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments,
            CancellationToken cancellationToken) => ValueTask.FromResult<object?>(_result);
    }

    [Fact]
    public void NormalizeResult_HandlesTheRawShapesTheFrameworkProduces()
    {
        Assert.Equal("(empty result)", McpCallResult.Normalize(null, _directory, "s"));
        Assert.Equal("t", McpCallResult.Normalize(new TextContent("t"), _directory, "s"));
        Assert.Equal("a\nb", McpCallResult.Normalize(new AIContent[] { new TextContent("a"), new TextContent("b") },
            _directory, "s"));
        JsonElement unknown = JsonSerializer.SerializeToElement(new { isError = true });
        Assert.Contains("isError", Assert.IsType<string>(McpCallResult.Normalize(unknown, _directory, "s")));
    }

    //================= 装配事实 =================

    [Fact]
    public void Facts_OnDemandRosterChangeTriggersRebuild_ButOnlyForAgents()
    {
        CharacterData agent = new() { CharacterName = "a", IsAgent = true };

        AgentAssemblyFacts one = Capture(agent, "github\tdesc");
        AgentAssemblyFacts two = Capture(agent, "github\tdesc\nunity\t");

        Assert.NotEqual(one, two);
        Assert.Equal(one, Capture(agent, "github\tdesc"));
        CharacterData chat = new() { CharacterName = "c" };
        Assert.Equal(Capture(chat, "github\tdesc"), Capture(chat, "unity\t")); //非 agent 归零，不连累重建
    }

    [Fact]
    public void Facts_DirectRevision_IsIndependentOfOnDemandSignature()
    {
        CharacterData agent = new() { CharacterName = "a", IsAgent = true };

        Assert.NotEqual(Capture(agent, "", revision: 1), Capture(agent, "", revision: 2));
    }

    private static AgentAssemblyFacts Capture(CharacterData character, string onDemand, int revision = 0) =>
        AgentAssemblyFacts.Capture(character,
            new AgentAssemblyInputs
            {
                Instructions = "prompt", WorkspacePath = "/ws", Permission = EAgentPermissionMode.ReadOnly,
                McpRevision = revision, McpOnDemand = onDemand,
            });

    /// 不会被调用的宿主：这里只看元工具的定义，不执行
    private sealed class NullHost : IMcpServerHost
    {
        public McpServerLookup Find(string? workspacePath, IEnumerable<string>? disabledServers, string serverName) =>
            new(EMcpLookupStatus.NotFound, null, []);

        public Task<McpServerConnection> ConnectAsync(McpServerConfig server, TimeSpan timeout,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<McpServerConnection> RefreshToolsAsync(McpServerConfig server, TimeSpan timeout,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public McpToolCatalog? LoadCatalog(McpServerConfig server) => null;
    }
}
