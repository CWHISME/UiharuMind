using UiharuMind.Core.AI.Models;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// 删除模型文件：分片全删；下载来的仓库删空了连投影与清单一起清；手放的只删自己
/// </summary>
public class LocalModelDeleterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"uiharu-delete-{Guid.NewGuid():N}");

    public LocalModelDeleterTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private string Touch(string relative)
    {
        string path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "x");
        return path;
    }

    private static ManifestModel Entry(params string[] files) =>
        new() { Files = files.Select(x => new ManifestFile { Path = x }).ToList(), Projector = "mmproj-F16.gguf" };

    private void WriteManifest(string directory, params ManifestModel[] models)
    {
        new ModelManifest
        {
            Source = "huggingface",
            Repository = "owner/repo",
            Models = models.ToList(),
            Projectors = [new ManifestFile { Path = "mmproj-F16.gguf" }]
        }.Save(Path.Combine(_root, directory));
    }

    [Fact]
    public void LastModelInRepo_TakesProjectorManifestAndDirectory()
    {
        string first = Touch("owner/repo/m-Q4-00001-of-00002.gguf");
        Touch("owner/repo/m-Q4-00002-of-00002.gguf");
        Touch("owner/repo/mmproj-F16.gguf");
        WriteManifest("owner/repo", Entry("m-Q4-00001-of-00002.gguf", "m-Q4-00002-of-00002.gguf"));

        IReadOnlyList<string> deleted = LocalModelDeleter.Delete(first);

        Assert.Equal(4, deleted.Count);
        Assert.False(Directory.Exists(Path.Combine(_root, "owner", "repo")));
    }

    [Fact]
    public void OtherQuantRemains_KeepsProjector_AndDropsOnlyThisEntry()
    {
        string q4 = Touch("owner/repo/m-Q4.gguf");
        Touch("owner/repo/m-Q8.gguf");
        Touch("owner/repo/mmproj-F16.gguf");
        WriteManifest("owner/repo", Entry("m-Q4.gguf"), Entry("m-Q8.gguf"));

        LocalModelDeleter.Delete(q4);

        string directory = Path.Combine(_root, "owner", "repo");
        Assert.True(File.Exists(Path.Combine(directory, "mmproj-F16.gguf")));
        ModelManifest manifest = ModelManifest.TryLoad(directory)!;
        Assert.Equal("m-Q8.gguf", Assert.Single(manifest.Models).Files[0].Path);
    }

    [Fact]
    public void HandPlaced_DeletesOnlyItsOwnFiles()
    {
        string model = Touch("loose-Q4.gguf");
        string projector = Touch("mmproj-loose.gguf");
        string other = Touch("other-split-00001-of-00002.gguf");

        IReadOnlyList<string> deleted = LocalModelDeleter.Delete(model);

        Assert.Equal([model], deleted);
        Assert.True(File.Exists(projector));
        Assert.True(File.Exists(other));
    }
}
