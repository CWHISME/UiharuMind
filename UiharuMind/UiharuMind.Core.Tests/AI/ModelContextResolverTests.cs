using UiharuMind.Core.AI.Models;
using UiharuMind.Core.Configs.RemoteAI;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// 钉死上下文窗口的解析顺序：用户填的 → 预设表 → 兜底。
/// 用户值必须永远优先——模型标称 1M 却实测拒收时，模型编辑窗那个数是唯一的压制手段，
/// 运行期补齐一旦覆盖它，用户就再没有别的办法。
/// 远程与本地的兜底也必须分开：本地拿到远程那份会每轮必然溢出。
/// </summary>
public class ModelContextResolverTests
{
    private static readonly IReadOnlyDictionary<string, RemoteModelIdVariant> Variants =
        new Dictionary<string, RemoteModelIdVariant>
        {
            ["glm-4.7-flash"] = new(ContextLength: 128000),
            ["no-context"] = new(ContextLength: 0),
        };

    [Fact]
    public void ConfiguredValue_WinsOverPresetTable()
    {
        int resolved = ModelContextResolver.ResolveRemote(32000, "glm-4.7-flash", Variants);

        Assert.Equal(32000, resolved);
    }

    [Fact]
    public void UnsetContext_FallsBackToPresetTable()
    {
        int resolved = ModelContextResolver.ResolveRemote(0, "glm-4.7-flash", Variants);

        Assert.Equal(128000, resolved);
    }

    [Theory]
    [InlineData("unknown-model")]
    [InlineData("no-context")] //表里有这个 id 但预设本身是 0,同样不算数
    [InlineData("")]
    [InlineData(null)]
    public void UnknownModel_FallsBackToRemoteConstant(string? modelId)
    {
        int resolved = ModelContextResolver.ResolveRemote(0, modelId, Variants);

        Assert.Equal(ModelContextResolver.RemoteFallback, resolved);
    }

    [Fact]
    public void MissingVariantTable_FallsBackToRemoteConstant()
    {
        int resolved = ModelContextResolver.ResolveRemote(0, "glm-4.7-flash", null);

        Assert.Equal(ModelContextResolver.RemoteFallback, resolved);
    }

    [Fact]
    public void Local_UsesRuntimeContextSize()
    {
        Assert.Equal(4096, ModelContextResolver.ResolveLocal(4096));
    }

    [Fact]
    public void Local_FallsBackToItsOwnConstant()
    {
        Assert.Equal(ModelContextResolver.LocalFallback, ModelContextResolver.ResolveLocal(0));
        Assert.NotEqual(ModelContextResolver.RemoteFallback, ModelContextResolver.LocalFallback);
    }
}
