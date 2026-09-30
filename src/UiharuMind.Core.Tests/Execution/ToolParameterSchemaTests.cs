using System.Text.Json;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution.Files;

namespace UiharuMind.Core.Tests.Agent;

/// <summary>
/// 钉死宽容转换器不吞 schema：参数类型挂了自定义转换器时，schema 生成器推不出结构，
/// 只会给一个无类型的空 schema。实机表现是 Edit 的 edits 丢了 array/items，
/// 模型不知道 newString 这个名字，还把整个 edits 当字符串发来。
/// </summary>
public class ToolParameterSchemaTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("uiharu-schema-").FullName;
    private readonly IReadOnlyList<AITool> _tools;

    public ToolParameterSchemaTests()
    {
        _tools = new PermissiveFileAccessTools(_dir).Create();
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch
        {
            // 临时目录清理失败不影响断言
        }
    }

    [Fact]
    public void EditTool_EditsParameter_IsArrayOfOldNewStringObjects()
    {
        JsonElement edits = Parameter(FileToolNames.Edit, "edits");

        Assert.True(HasType(edits, "array"), edits.GetRawText());
        JsonElement itemProperties = edits.GetProperty("items").GetProperty("properties");
        Assert.Equal("string", itemProperties.GetProperty("oldString").GetProperty("type").GetString());
        Assert.Equal("string", itemProperties.GetProperty("newString").GetProperty("type").GetString());
        Assert.True(itemProperties.GetProperty("newString").TryGetProperty("description", out _));
        Assert.True(edits.TryGetProperty("description", out _), "参数自己的说明不能在替换 schema 时丢掉");
    }

    [Fact]
    public void GrepTool_FileGlobsParameter_IsArrayOfStrings()
    {
        JsonElement fileGlobs = Parameter(FileToolNames.Grep, "fileGlobs");

        Assert.True(HasType(fileGlobs, "array"), fileGlobs.GetRawText());
        Assert.True(HasType(fileGlobs.GetProperty("items"), "string"), fileGlobs.GetRawText());
        Assert.True(fileGlobs.TryGetProperty("description", out _));
    }

    [Fact]
    public void GrepTool_FileGlobsParameter_AdmitsNullLikeItsDefault()
    {
        JsonElement fileGlobs = Parameter(FileToolNames.Grep, "fileGlobs");

        Assert.True(HasType(fileGlobs, "null"), fileGlobs.GetRawText());
        Assert.Equal(JsonValueKind.Null, fileGlobs.GetProperty("default").ValueKind);
    }

    private JsonElement Parameter(string toolName, string parameterName)
    {
        AIFunction function = _tools.OfType<AIFunction>().Single(t => t.Name == toolName);
        return function.JsonSchema.GetProperty("properties").GetProperty(parameterName);
    }

    private static bool HasType(JsonElement schema, string type)
    {
        if (!schema.TryGetProperty("type", out JsonElement value)) return false;
        return value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Any(v => v.GetString() == type)
            : value.GetString() == type;
    }
}
