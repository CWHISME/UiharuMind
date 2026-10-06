using UiharuMind.App.Tests.TestDoubles;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.AI.Runtime.Backends;
using UiharuMind.Features.Models;

namespace UiharuMind.App.Tests.Models;

/// <summary>
/// 模型列表的「删除模型文件」：内置与远程不给删；确认框点取消一个文件都不动
/// </summary>
public class ModelDeleteTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"uiharu-model-del-{Guid.NewGuid():N}");

    public ModelDeleteTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, true);

    private static ModelRunningData Local(string path) => new(new GGufModelInfo { ModelName = Path.GetFileNameWithoutExtension(path), ModelPath = path });

    [Fact]
    public void BuiltInAndRemote_CannotBeDeleted()
    {
        string builtIn = Path.Combine(ModelSettingConfig.Current.DefaultLocalModelPath, "embed.gguf");

        Assert.False(ModelPageData.CanDeleteLocalModel(Local(builtIn)));
        Assert.False(ModelPageData.CanDeleteLocalModel(new ModelRunningData(new RemoteModelInfo())));
        Assert.True(ModelPageData.CanDeleteLocalModel(Local(Path.Combine(_directory, "m.gguf"))));
    }

    [Fact]
    public async Task DecliningConfirm_LeavesFilesAlone()
    {
        string path = Path.Combine(_directory, "m.gguf");
        await File.WriteAllTextAsync(path, "x", TestContext.Current.CancellationToken);
        RecordingMessageService messages = new() { ConfirmResult = false };

        await new ModelPageData(messages).DeleteLocalModelCommand.ExecuteAsync(Local(path));

        Assert.Single(messages.Confirms);
        Assert.True(File.Exists(path));
    }
}
