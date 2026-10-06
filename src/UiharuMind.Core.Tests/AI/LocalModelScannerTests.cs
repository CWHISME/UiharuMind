using UiharuMind.Core.AI.Embedding;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.AI.Runtime.Backends;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// 对话与嵌入模型同住一个目录（ADR 0067），分流只靠文件头。
/// 判错的后果：嵌入模型出现在对话选择器里，或知识库找不到嵌入模型。
/// </summary>
public class LocalModelScannerTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"uiharu-scan-{Guid.NewGuid():N}");
    private readonly string _originalPath = ModelSettingConfig.Current.LocalModelPath;

    public LocalModelScannerTests()
    {
        Directory.CreateDirectory(Path.Combine(_directory, "owner", "repo"));
        ModelSettingConfig.Current.LocalModelPath = _directory;

        new GGufBuilder().String("general.architecture", "qwen3").UInt32("qwen3.context_length", 4096)
            .WriteTo(Path.Combine(_directory, "chat-a.gguf"));
        new GGufBuilder().String("general.architecture", "qwen3").UInt32("qwen3.context_length", 4096)
            .UInt32("qwen3.pooling_type", 3)
            .WriteTo(Path.Combine(_directory, "owner", "repo", "embed-b.gguf"));
        new GGufBuilder().String("general.architecture", "xlm-roberta").UInt32("xlm-roberta.pooling_type", 4)
            .WriteTo(Path.Combine(_directory, "rerank-c.gguf"));
        new GGufBuilder().String("general.architecture", "clip")
            .WriteTo(Path.Combine(_directory, "projector-d.gguf"));
        new GGufBuilder().String("general.architecture", "clip")
            .WriteTo(Path.Combine(_directory, "mmproj-e.gguf"));
        File.WriteAllText(Path.Combine(_directory, "broken-f.gguf"), "not a gguf");

        Directory.CreateDirectory(Path.Combine(_directory, "owner", "vision"));
        new GGufBuilder().String("general.architecture", "gemma3").UInt32("gemma3.context_length", 4096)
            .WriteTo(Path.Combine(_directory, "owner", "vision", "vision-g.gguf"));
        new GGufBuilder().String("general.architecture", "clip")
            .WriteTo(Path.Combine(_directory, "owner", "vision", "mmproj-F16.gguf"));

        string big = Path.Combine(_directory, "owner", "big");
        Directory.CreateDirectory(big);
        new GGufBuilder().String("general.architecture", "qwen3").UInt32("qwen3.context_length", 4096)
            .Tensor("a", 10, 10).WriteTo(Path.Combine(big, "big-Q4_K_M-00001-of-00002.gguf"));
        new GGufBuilder().Tensor("b", 5).WriteTo(Path.Combine(big, "big-Q4_K_M-00002-of-00002.gguf"));

        string multi = Path.Combine(_directory, "owner", "multi");
        Directory.CreateDirectory(multi);
        new GGufBuilder().String("general.architecture", "gemma3").UInt32("gemma3.context_length", 4096)
            .WriteTo(Path.Combine(multi, "multi-Q8_0.gguf"));
        new GGufBuilder().String("general.architecture", "clip").WriteTo(Path.Combine(multi, "mmproj-F16.gguf"));
        new GGufBuilder().String("general.architecture", "clip").WriteTo(Path.Combine(multi, "mmproj-F32.gguf"));
        new ModelManifest
        {
            Source = "huggingface",
            Repository = "owner/multi",
            Models = [new ManifestModel { Files = [new ManifestFile { Path = "multi-Q8_0.gguf" }], Projector = "mmproj-F32.gguf" }]
        }.Save(multi);
    }

    public void Dispose()
    {
        ModelSettingConfig.Current.LocalModelPath = _originalPath;
        Directory.Delete(_directory, true);
    }

    [Fact]
    public void SplitsByKind_AndNeverListsProjectors()
    {
        IReadOnlyList<LocalModelEntry> all = LocalModelScanner.Scan(force: true);

        Assert.DoesNotContain(all, x => x.Info.ModelName is "projector-d" or "mmproj-e");
        Assert.Equal(ELocalModelKind.Embedding, all.Single(x => x.Info.ModelName == "embed-b").Info.Kind);
        Assert.Equal(ELocalModelKind.Reranker, all.Single(x => x.Info.ModelName == "rerank-c").Info.Kind);
    }

    [Fact]
    public void ChatList_KeepsUnreadableFiles_ExcludesEmbeddingAndReranker()
    {
        string[] chat = LocalModelScanner.Scan(ELocalModelKind.Chat, force: true)
            .Select(x => x.Info.ModelName).Order().ToArray();

        Assert.Equal(["big-Q4_K_M", "broken-f", "chat-a", "multi-Q8_0", "vision-g"], chat);
    }

    [Fact]
    public void EmbeddingCandidates_ComeFromTheSharedModelFolder()
    {
        LocalModelScanner.Scan(force: true);

        EmbeddingModelCandidate candidate = Assert.Single(EmbeddingModelResolver.GetManagedCandidates());

        Assert.Equal("embed-b.gguf", candidate.Name);
        Assert.Equal(EmbeddingModelCandidateSource.Application, candidate.Source);
    }

    [Fact]
    public void SingleProjectorInModelSubfolder_PairsAsVision()
    {
        GGufModelInfo vision = LocalModelScanner.Scan(force: true).Single(x => x.Info.ModelName == "vision-g").Info;

        Assert.EndsWith("mmproj-F16.gguf", vision.ModelProjPath);
        Assert.True(vision.IsVision);
    }

    [Fact]
    public void ProjectorInRootWithSeveralModels_IsNotGuessed()
    {
        GGufModelInfo chat = LocalModelScanner.Scan(force: true).Single(x => x.Info.ModelName == "chat-a").Info;

        Assert.False(chat.IsVision); //根下有 chat-a、broken-f 两个对话模型，不知道 mmproj 是谁的
    }

    [Fact]
    public void ShardedModel_ListsOnce_WithTotalSizeAndParameters()
    {
        GGufModelInfo big = LocalModelScanner.Scan(force: true).Single(x => x.Info.ModelName == "big-Q4_K_M").Info;
        string[] files = Directory.GetFiles(Path.Combine(_directory, "owner", "big"));

        Assert.EndsWith("-00001-of-00002.gguf", big.ModelPath);
        Assert.Equal((ulong)files.Sum(x => new FileInfo(x).Length), big.FileSizeBytes);
        Assert.Equal(10ul * 10 + 5, big.ParameterCount);
    }

    [Fact]
    public void ManifestProjector_WinsWhereGuessingCannot()
    {
        GGufModelInfo multi = LocalModelScanner.Scan(force: true).Single(x => x.Info.ModelName == "multi-Q8_0").Info;

        Assert.EndsWith("mmproj-F32.gguf", multi.ModelProjPath); //目录里两个 mmproj，靠猜配不上
    }

    [Theory]
    [InlineData("Qwen3-235B-Q4_K_M-00002-of-00005", true, "Qwen3-235B-Q4_K_M", 2, 5)]
    [InlineData("model-00001-of-00001", false, "model-00001-of-00001", 0, 0)] //单片不算分片
    [InlineData("model-Q4_K_M", false, "model-Q4_K_M", 0, 0)]
    public void SplitName(string name, bool isSplit, string baseName, int index, int count)
    {
        Assert.Equal(isSplit, GGufSplitName.TryParse(name, out string parsedBase, out int parsedIndex, out int parsedCount));
        Assert.Equal((baseName, index, count), (parsedBase, parsedIndex, parsedCount));
    }
}
