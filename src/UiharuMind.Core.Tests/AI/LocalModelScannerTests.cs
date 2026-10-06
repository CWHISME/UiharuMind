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

        Assert.Equal(["broken-f", "chat-a", "vision-g"], chat);
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
}
