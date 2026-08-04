using System.Text.Json;
using UiharuMind.Core.AI.Runtime.Backends;
using UiharuMind.Core.Core;

namespace UiharuMind.Core.Tests.AI;

public class ModelSettingConfigTests
{
    [Fact]
    public void ToggleFavorite_AddsAndRemoves()
    {
        var config = new ModelSettingConfig();

        config.ToggleFavorite("model-a");
        config.ToggleFavorite("model-b");

        Assert.True(config.IsFavorite("model-a"));
        Assert.True(config.IsFavorite("model-b"));
        Assert.Equal(2, config.FavoriteModels.Count);

        config.ToggleFavorite("model-a");
        Assert.False(config.IsFavorite("model-a"));
        Assert.Single(config.FavoriteModels);
    }

    [Fact]
    public void FavoriteModels_SerializeAsList_NoLegacyField()
    {
        var config = new ModelSettingConfig();
        config.ToggleFavorite("model-a");

        string json = JsonSerializer.Serialize(config, SaveUtility.JsonOptions);

        Assert.Contains("\"FavoriteModels\"", json);
        Assert.Contains("model-a", json);
        Assert.DoesNotContain("\"FavoriteModel\"", json);
    }
}
