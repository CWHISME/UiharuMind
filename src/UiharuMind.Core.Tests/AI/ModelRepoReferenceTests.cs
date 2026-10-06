using UiharuMind.Core.AI.Models.Sources;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// 搜索框输入：仓库名、各站链接直接开仓库，其余当关键词
/// </summary>
public class ModelRepoReferenceTests
{
    [Theory]
    [InlineData("Qwen/Qwen3-8B-GGUF", "Qwen/Qwen3-8B-GGUF")]
    [InlineData("  unsloth/gemma-3-4b-it-GGUF ", "unsloth/gemma-3-4b-it-GGUF")]
    [InlineData("https://huggingface.co/Qwen/Qwen3-8B-GGUF", "Qwen/Qwen3-8B-GGUF")]
    [InlineData("https://huggingface.co/Qwen/Qwen3-8B-GGUF/tree/main", "Qwen/Qwen3-8B-GGUF")]
    [InlineData("https://hf-mirror.com/Qwen/Qwen3-8B-GGUF/blob/main/x.gguf", "Qwen/Qwen3-8B-GGUF")]
    [InlineData("https://modelscope.cn/models/Qwen/Qwen3-8B-GGUF/files", "Qwen/Qwen3-8B-GGUF")]
    public void RecognizesRepositories(string input, string expected)
    {
        Assert.True(ModelRepoReference.TryParse(input, out string repository));
        Assert.Equal(expected, repository);
    }

    [Theory]
    [InlineData("")]
    [InlineData("qwen")]
    [InlineData("qwen 8b")]
    [InlineData("a/b/c")]
    [InlineData("https://huggingface.co/datasets/foo/bar")]
    [InlineData("https://huggingface.co/Qwen")]
    public void KeywordsStayKeywords(string input)
    {
        Assert.False(ModelRepoReference.TryParse(input, out _));
    }
}
