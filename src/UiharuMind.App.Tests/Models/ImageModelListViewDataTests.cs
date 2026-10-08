using UiharuMind.App.Tests.TestDoubles;
using UiharuMind.Core.AI.ImageGeneration;
using UiharuMind.Features.Models.ImageModels;

namespace UiharuMind.App.Tests.Models;

/// <summary>
/// 生图模型列表：列表顺序即回退顺序，每次改动立刻落进配置；取消编辑、拒绝删除都不动配置
/// </summary>
public class ImageModelListViewDataTests : IDisposable
{
    private readonly List<ImageModelInfo> _original = ImageModelSettingConfig.Current.Models;
    private readonly RecordingMessageService _messages = new();
    private ImageModelInfo? _editorResult; //编辑框确认后返回的模型；null 视为取消
    private IReadOnlyCollection<string>? _takenNames; //最近一次打开编辑框时传入的「已占用名字」

    public ImageModelListViewDataTests()
    {
        ImageModelSettingConfig.Current.ReplaceModels([Model("a"), Model("b"), Model("c")]);
    }

    public void Dispose() => ImageModelSettingConfig.Current.ReplaceModels(_original);

    private static ImageModelInfo Model(string name) => new() { Name = name, Endpoint = "https://x", ModelId = name };

    private static List<string> ConfiguredNames() => ImageModelSettingConfig.Current.Models.Select(m => m.Name).ToList();

    private ImageModelListViewData Create() => new(_messages, (_, taken) =>
    {
        _takenNames = taken;
        return Task.FromResult(_editorResult);
    });

    [Fact]
    public void Items_FollowFallbackOrder_AndEdgesCannotMoveFurther()
    {
        ImageModelListViewData list = Create();

        Assert.Equal([1, 2, 3], list.Items.Select(i => i.Order));
        Assert.False(list.Items[0].CanMoveUp);
        Assert.True(list.Items[0].CanMoveDown);
        Assert.False(list.Items[2].CanMoveDown);
    }

    [Fact]
    public async Task Add_AppendsToTheEnd_AndCancelLeavesConfigUntouched()
    {
        ImageModelListViewData list = Create();

        await list.AddCommand.ExecuteAsync(null);
        Assert.Equal(["a", "b", "c"], ConfiguredNames());

        _editorResult = Model("d");
        await list.AddCommand.ExecuteAsync(null);

        Assert.Equal(["a", "b", "c", "d"], ConfiguredNames());
        Assert.Equal("d", list.Items[3].Name);
        Assert.Equal(["a", "b", "c"], _takenNames);
    }

    [Fact]
    public async Task Edit_ReplacesInPlace_AndItsOwnNameIsNotTaken()
    {
        ImageModelListViewData list = Create();
        _editorResult = Model("b2");

        await list.EditCommand.ExecuteAsync(list.Items[1]);

        Assert.Equal(["a", "b2", "c"], ConfiguredNames());
        Assert.Equal(["a", "c"], _takenNames);
    }

    [Fact]
    public async Task Delete_OnlyAfterConfirm()
    {
        ImageModelListViewData list = Create();

        _messages.ConfirmResult = false;
        await list.DeleteCommand.ExecuteAsync(list.Items[0]);
        Assert.Equal(["a", "b", "c"], ConfiguredNames());

        _messages.ConfirmResult = true;
        await list.DeleteCommand.ExecuteAsync(list.Items[0]);
        Assert.Equal(["b", "c"], ConfiguredNames());
        Assert.Equal(2, _messages.ConfirmCount);
    }

    [Fact]
    public void Move_SwapsNeighbours_AndIgnoresTheEdge()
    {
        ImageModelListViewData list = Create();

        list.MoveDownCommand.Execute(list.Items[0]);
        Assert.Equal(["b", "a", "c"], ConfiguredNames());

        list.MoveUpCommand.Execute(list.Items[2]);
        Assert.Equal(["b", "c", "a"], ConfiguredNames());

        list.MoveUpCommand.Execute(list.Items[0]);
        Assert.Equal(["b", "c", "a"], ConfiguredNames());
    }

    [Fact]
    public async Task LocalEntry_EditsInLocalEditor_AndDeleteRemovesFilesFirst()
    {
        ImageModelInfo local = new() { Name = "local", Dialect = EImageDialect.Local, Local = new LocalImageModel() };
        ImageModelSettingConfig.Current.ReplaceModels([Model("a"), local]);
        List<string> calls = [];
        ImageModelListActions actions = ImageModelListActions.None with
        {
            OpenLocalEditor = (model, taken) =>
            {
                calls.Add($"edit {model.Name} [{string.Join(",", taken)}]");
                return Task.FromResult<ImageModelInfo?>(new ImageModelInfo
                    { Name = "local2", Dialect = EImageDialect.Local, Local = model.Local });
            },
            DeleteLocalFiles = model =>
            {
                calls.Add($"delete {model.Name} listed={ConfiguredNames().Contains(model.Name)}");
                return Task.FromResult<IReadOnlyList<string>>([]);
            }
        };
        ImageModelListViewData list = new(_messages, (_, _) => throw new InvalidOperationException("远程编辑框不该打开"), actions);

        await list.EditCommand.ExecuteAsync(list.Items[1]);
        Assert.Equal(["a", "local2"], ConfiguredNames());

        _messages.ConfirmResult = true;
        await list.DeleteCommand.ExecuteAsync(list.Items[1]);

        Assert.Equal(["a"], ConfiguredNames());
        Assert.Equal(["edit local [a]", "delete local2 listed=True"], calls);
    }

    [Fact]
    public void Append_AddsToTheEnd()
    {
        ImageModelListViewData list = Create();

        list.Append(Model("d"));

        Assert.Equal(["a", "b", "c", "d"], ConfiguredNames());
        Assert.Equal("d", list.Items[3].Name);
    }
}
