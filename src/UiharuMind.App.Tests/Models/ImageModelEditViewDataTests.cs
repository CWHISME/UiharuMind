using UiharuMind.Core.AI.ImageGeneration;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.Configs.RemoteAI;
using UiharuMind.Features.Models.ImageModels;

namespace UiharuMind.App.Tests.Models;

/// <summary>
/// 生图模型编辑表单：预设只在新建时回填、密钥只从同一服务地址的对话模型沿用、确认产出新实例
/// </summary>
public class ImageModelEditViewDataTests
{
    private static RemoteModelInfo ChatModel(string name, string path, string apiKey) => new()
    {
        Config = new RemoteModelConfig { ModelName = name, ModelPath = path },
        ApiKey = apiKey,
    };

    private static ImageModelEditViewData Create(IReadOnlyCollection<string>? taken = null,
        IReadOnlyList<RemoteModelInfo>? chatModels = null) =>
        new(null, taken ?? [], chatModels ?? []);

    private static ImageModelPresetItem Preset(ImageModelEditViewData form, string key) =>
        form.Presets.Single(p => p.Preset.Key == key);

    [Fact]
    public void NewForm_StartsFromFirstPreset()
    {
        ImageModelEditViewData form = Create();

        Assert.Equal(EImageDialect.SenseNova, form.Dialect);
        Assert.Equal("https://token.sensenova.cn/v1", form.Endpoint);
        Assert.Equal("sensenova-u1.5-lite", form.ModelId);
        Assert.Equal("sensenova-u1.5-lite", form.Name);
        Assert.True(form.CanConfirm);
    }

    [Fact]
    public void SwitchingPreset_RefillsAndNameFollowsModelIdUntilEdited()
    {
        ImageModelEditViewData form = Create();

        form.SelectedPreset = Preset(form, "agnes");
        Assert.Equal(EImageDialect.Agnes, form.Dialect);
        Assert.Equal("agnes-image-2.5-flash", form.Name);

        form.Name = "我的 Agnes";
        form.ModelId = "agnes-image-3";
        Assert.Equal("我的 Agnes", form.Name); //用户改过名就不再跟着模型 id 走
    }

    [Fact]
    public void ModelIdOptions_FollowPresetAndPickingOneSetsTheId()
    {
        ImageModelEditViewData form = Create();

        Assert.Equal(["sensenova-u1.5-lite", "sensenova-u1.5-fast"], form.ModelIdOptions);
        Assert.Equal("sensenova-u1.5-lite", form.SelectedModelIdOption);

        form.SelectedModelIdOption = "sensenova-u1.5-fast";
        Assert.Equal("sensenova-u1.5-fast", form.ModelId);

        form.SelectedPreset = Preset(form, ImageModelPreset.CustomKey);
        Assert.False(form.HasModelIdOptions); //自定义没有候选,退回普通输入框
    }

    [Fact]
    public void TypedCustomId_ClearsSelectionButKeepsTheText()
    {
        ImageModelEditViewData form = Create();

        form.ModelId = "sensenova-u2-preview";

        Assert.Null(form.SelectedModelIdOption);
        Assert.Equal("sensenova-u2-preview", form.ModelId);
    }

    [Fact]
    public void CopySources_OnlyChatModelsOnTheSameHostWithAKey()
    {
        ImageModelEditViewData form = Create(chatModels:
        [
            ChatModel("chat-a", "https://token.sensenova.cn/v1/chat/completions", "sk-a"),
            ChatModel("no-key", "https://token.sensenova.cn/v1/chat/completions", ""),
            ChatModel("other", "https://api.agnes-ai.cn/v1/chat/completions", "sk-b"),
        ]);

        RemoteModelInfo source = Assert.Single(form.CopySources);
        form.CopyKeyFromCommand.Execute(source);

        Assert.Equal("sk-a", form.ApiKey);
    }

    [Fact]
    public void Validation_BlocksDuplicateNameBadEndpointTimeoutAndExtraBody()
    {
        ImageModelEditViewData form = Create(taken: ["Taken"]);

        form.Name = "taken";
        Assert.True(form.HasNameError);
        form.Name = "fresh";

        form.Endpoint = "ftp://example.com";
        Assert.True(form.HasEndpointError);
        form.Endpoint = "https://example.com/v1";

        form.TimeoutText = "5";
        Assert.True(form.HasTimeoutError);
        form.TimeoutText = "120";

        form.ExtraBody = "[1, 2]";
        Assert.True(form.HasExtraBodyError);
        Assert.False(form.CanConfirm);

        form.ExtraBody = "{\"watermark\": false}";
        Assert.True(form.CanConfirm);
    }

    [Fact]
    public void EditMode_KeepsStoredFieldsAndBuildsANewInstance()
    {
        ImageModelInfo stored = new()
        {
            Name = "painter", Dialect = EImageDialect.Agnes, Endpoint = "https://api.agnes-ai.cn/v1",
            ModelId = "agnes-image-2.5-flash", ApiKey = "sk-x", Resolution = EImageResolution.Res4K,
            SupportsEditing = false, TimeoutSeconds = 90, ExtraBody = "{\"seed\": 1}",
        };

        ImageModelEditViewData form = new(stored, [], []);
        ImageModelInfo built = form.BuildResult();

        Assert.True(form.IsEditMode);
        Assert.True(form.HasWebsite); //编辑时仍借预设的官网
        Assert.Equal("agnes-image-2.5-flash", form.SelectedModelIdOption); //候选也借,并定位到当前 id
        Assert.NotSame(stored, built);
        Assert.Equal((stored.Name, stored.Dialect, stored.ApiKey, stored.Resolution, stored.SupportsEditing,
                stored.TimeoutSeconds, stored.ExtraBody),
            (built.Name, built.Dialect, built.ApiKey, built.Resolution, built.SupportsEditing,
                built.TimeoutSeconds, built.ExtraBody));
    }
}
