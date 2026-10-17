using System.Collections.ObjectModel;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.AI.Runtime.Backends;
using UiharuMind.Core.Configs.RemoteAI;
using UiharuMind.Features.Models;

namespace UiharuMind.App.Tests.Models;

/// <summary>
/// 模型列表排序口径：运行/加载置顶 → 远程区在本地区前 → 区内收藏在前 → 名称
/// </summary>
public class ModelSortingTests
{
    private static ModelRunningData Local(string name) =>
        new(new GGufModelInfo { ModelName = name, ModelPath = $"/m/{name}.gguf" });

    private static ModelRunningData Remote(string name) =>
        new(new RemoteModelInfo { Config = new RemoteModelConfig { ModelName = name, ModelId = name } });

    private static string[] Names(ObservableCollection<ModelRunningData> items) =>
        items.Select(x => x.ModelName).ToArray();

    [Fact]
    public void RemoteBlockBeforeLocalBlock()
    {
        ObservableCollection<ModelRunningData> items = [Local("z-local"), Remote("a-remote")];

        ModelSorting.Sort(items);

        Assert.Equal(["a-remote", "z-local"], Names(items));
    }

    [Fact]
    public void WithinBlock_FavoriteFirst_ThenName()
    {
        List<string> backup = [.. ModelSettingConfig.Current.FavoriteModels];
        try
        {
            ObservableCollection<ModelRunningData> items =
                [Local("a-local"), Local("m-local"), Remote("a-remote"), Remote("m-remote")];
            ModelSettingConfig.Current.FavoriteModels.Clear();
            ModelSettingConfig.Current.ToggleFavorite("m-local");
            ModelSettingConfig.Current.ToggleFavorite("m-remote");

            ModelSorting.Sort(items);

            Assert.Equal(["m-remote", "a-remote", "m-local", "a-local"], Names(items));
        }
        finally
        {
            ModelSettingConfig.Current.FavoriteModels.Clear();
            ModelSettingConfig.Current.FavoriteModels.AddRange(backup);
        }
    }

    [Fact]
    public void LoadingModel_PinnedAbovePartition()
    {
        ObservableCollection<ModelRunningData> items = [Local("a-local"), Remote("a-remote"), Local("m-local")];
        items[2].BeginLoading();

        ModelSorting.Sort(items);

        Assert.Equal(["m-local", "a-remote", "a-local"], Names(items));
    }

    [Fact]
    public void AlreadySorted_DoesNotRaiseCollectionChanges()
    {
        ObservableCollection<ModelRunningData> items = [Remote("a-remote"), Local("a-local")];
        int changes = 0;
        items.CollectionChanged += (_, _) => changes++;

        ModelSorting.Sort(items);

        Assert.Equal(0, changes);
    }

    [Fact]
    public void SingleRankChange_EmitsExactlyOneMove()
    {
        List<string> backup = [.. ModelSettingConfig.Current.FavoriteModels];
        try
        {
            ObservableCollection<ModelRunningData> items =
                [Local("a"), Local("b"), Local("c"), Local("d"), Local("e")];
            ModelSettingConfig.Current.FavoriteModels.Clear();
            int moves = 0;
            items.CollectionChanged += (_, e) =>
            {
                if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Move) moves++;
            };

            ModelSettingConfig.Current.ToggleFavorite("e");
            ModelSorting.Sort(items);

            Assert.Equal(["e", "a", "b", "c", "d"], Names(items));
            Assert.Equal(1, moves);
        }
        finally
        {
            ModelSettingConfig.Current.FavoriteModels.Clear();
            ModelSettingConfig.Current.FavoriteModels.AddRange(backup);
        }
    }
}
