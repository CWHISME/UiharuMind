using System.Text.Json;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Audio;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Tools;
using UiharuMind.Core.AI.Execution.Tools.Skills;
using UiharuMind.Core.AI.Execution.Tools.WebTools;

namespace UiharuMind.Core.Tests.Agent;

/// <summary>
/// A2 全量扩展的 schema 形状护栏（普拉娜规矩：没形状断言不算扩展完成）。
/// 每个手写工具声明一组形状断言，钉死「手写 schema 与反射推断等价」——漂移有测试兜。
/// </summary>
public class AotToolSchemaShapeTests
{
    private static JsonElement Schema(AITool tool) => ((AIFunction)tool).JsonSchema;

    private static JsonElement Parameter(AITool tool, string name) =>
        Schema(tool).GetProperty("properties").GetProperty(name);

    private static bool HasType(JsonElement schema, string type)
    {
        if (!schema.TryGetProperty("type", out JsonElement value)) return false;
        return value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Any(v => v.GetString() == type)
            : value.GetString() == type;
    }

    [Fact]
    public void GroupPostTool_Schema_RequiresContentString()
    {
        var tool = GroupPostTool.Create("s1");
        JsonElement content = Parameter(tool, "content");
        Assert.True(HasType(content, "string"), content.GetRawText());
        Assert.True(content.TryGetProperty("description", out _));
        Assert.Contains("content", Schema(tool).GetProperty("required").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public void EndAwayTool_Schema_RequiresReasonSummary_AdmitsSay()
    {
        var tool = EndAwayTool.Create();
        Assert.True(HasType(Parameter(tool, "reason"), "string"));
        Assert.True(HasType(Parameter(tool, "summary"), "string"));
        Assert.True(HasType(Parameter(tool, "say"), "string") || HasType(Parameter(tool, "say"), "null"));
        var required = Schema(tool).GetProperty("required").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains("reason", required);
        Assert.Contains("summary", required);
        Assert.DoesNotContain("say", required);
    }

    [Fact]
    public void SpeakTool_Schema_RequiresText_AdmitsTone()
    {
        var tool = SpeakTool.Create(_ => Task.FromResult(true));
        Assert.True(HasType(Parameter(tool, "text"), "string"));
        Assert.True(HasType(Parameter(tool, "tone"), "string") || HasType(Parameter(tool, "tone"), "null"));
        Assert.Contains("text", Schema(tool).GetProperty("required").EnumerateArray().Select(e => e.GetString()));
        Assert.DoesNotContain("tone", Schema(tool).GetProperty("required").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public void KnowledgeTool_Schema_RequiresQueryString()
    {
        var tool = KnowledgeTool.Create(null);
        Assert.True(HasType(Parameter(tool, "query"), "string"));
        Assert.Contains("query", Schema(tool).GetProperty("required").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public void SynthesizeSpeechTool_Schema_RequiresText_AdmitsVoiceToneSpeed()
    {
        var paths = new UiharuMind.Core.AI.Execution.Files.AgentPathResolver(Path.GetTempPath());
        var tool = SynthesizeSpeechTool.Create(paths, new UiharuMind.Core.AI.Character.CharacterData(),
            SpeechFileSynthesizer.Shared, Enumerable.Empty<CharacterData>);
        Assert.True(HasType(Parameter(tool, "text"), "string"));
        Assert.True(HasType(Parameter(tool, "voice"), "string") || HasType(Parameter(tool, "voice"), "null"));
        Assert.True(HasType(Parameter(tool, "speed"), "number") || HasType(Parameter(tool, "speed"), "null"));
        Assert.Contains("text", Schema(tool).GetProperty("required").EnumerateArray().Select(e => e.GetString()));
        Assert.DoesNotContain("voice", Schema(tool).GetProperty("required").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public void TranscribeAudioTool_Schema_RequiresPath_AdmitsSubtitles()
    {
        var paths = new UiharuMind.Core.AI.Execution.Files.AgentPathResolver(Path.GetTempPath());
        var tool = TranscribeAudioTool.Create(paths);
        Assert.True(HasType(Parameter(tool, "path"), "string"));
        Assert.True(HasType(Parameter(tool, "subtitles"), "boolean"));
        Assert.Contains("path", Schema(tool).GetProperty("required").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public void WebSearchTool_Schema_RequiresQuery_AdmitsPage()
    {
        var tool = WebSearchTool.Create();
        Assert.True(HasType(Parameter(tool, "query"), "string"));
        Assert.True(HasType(Parameter(tool, "page"), "integer"));
        Assert.Contains("query", Schema(tool).GetProperty("required").EnumerateArray().Select(e => e.GetString()));
        Assert.DoesNotContain("page", Schema(tool).GetProperty("required").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public void WebFetchTool_Schema_RequiresUrlString()
    {
        var tool = WebFetchTool.Create();
        Assert.True(HasType(Parameter(tool, "url"), "string"));
        Assert.Contains("url", Schema(tool).GetProperty("required").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public void LoadSkillTool_Schema_RequiresSkillNameString()
    {
        // source 只在调用时用，构造可传 null（AgentSkillsSource 是 internal 抽象，测试不便实例化）
        var tool = LoadSkillTool.Create(null!, false, false);
        Assert.True(HasType(Parameter(tool, "skillName"), "string"));
        Assert.Contains("skillName", Schema(tool).GetProperty("required").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public void VisionTool_Schema_RequiresImagePathsAndQuestion()
    {
        var tool = VisionTool.Create(null!); // paths 只在调用时用
        Assert.True(HasType(Parameter(tool, "imagePaths"), "array"));
        Assert.True(HasType(Parameter(tool, "question"), "string"));
        var required = Schema(tool).GetProperty("required").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains("imagePaths", required);
        Assert.Contains("question", required);
    }

    [Fact]
    public void ImageGenerationTool_Schema_RequiresPrompt_AdmitsImagesAndAspect()
    {
        var tool = ImageGenerationTool.Create(null!, null!); // paths/service 只在调用时用
        Assert.True(HasType(Parameter(tool, "prompt"), "string"));
        Assert.True(HasType(Parameter(tool, "images"), "array") || HasType(Parameter(tool, "images"), "null"));
        Assert.True(HasType(Parameter(tool, "aspectRatio"), "string") || HasType(Parameter(tool, "aspectRatio"), "null"));
        Assert.Contains("prompt", Schema(tool).GetProperty("required").EnumerateArray().Select(e => e.GetString()));
        Assert.DoesNotContain("images", Schema(tool).GetProperty("required").EnumerateArray().Select(e => e.GetString()));
    }
}