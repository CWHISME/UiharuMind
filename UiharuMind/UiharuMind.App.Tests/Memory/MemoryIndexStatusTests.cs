/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UiharuMind.Core.AI.Memory;
using UiharuMind.Features.Memory;
using UiharuMind.Shared.Services;

namespace UiharuMind.App.Tests.Memory;

/// <summary>
/// 索引状态阶梯与其文案。
///
/// 这套判定原先在编辑窗连写三遍、选择窗又写一遍且顺序不同，于是同一个记忆库
/// 在两个窗口里状态不一样——没人会因此收到报错，只会看到自相矛盾的界面。
/// 现在阶梯只有一份，本文件把「哪个条件赢」和「每档说什么」钉住。
/// </summary>
public class MemoryIndexStatusTests
{
    /// <summary>axaml 只认这四种 Tag（Assets/Themes/CustomStatusStyle.axaml），多出一个就是没颜色的圆点</summary>
    private static readonly string[] ThemeStatusKeys = ["Progress", "Error", "Dirty", "Ready"];

    /// <summary>正在更新压过一切：此时说「出错」或「需要更新」都是在描述上一轮的旧状态</summary>
    [Fact]
    public void Updating_WinsOverEveryOtherCondition()
    {
        var state = new MemoryIndexState(true, "boom", true, null);

        Assert.Equal(EMemoryIndexStatus.Updating, MemoryIndexUiText.ResolveStatus(state));
    }

    /// <summary>更新失败会同时把索引标脏，出错必须压过「需要更新」，否则用户看不到失败</summary>
    [Fact]
    public void Error_WinsOverDirtyAndNeverBuilt()
    {
        var state = new MemoryIndexState(false, "Memory name not set", true, null);

        Assert.Equal(EMemoryIndexStatus.Error, MemoryIndexUiText.ResolveStatus(state));
    }

    /// <summary>
    /// 从未建立压过已过期——这就是两窗之前的分歧点。
    /// 新建记忆库加完来源时两个条件同时成立，说「需要更新」会让人以为还有旧索引可用，
    /// 实际上一条都检索不到，所以以选择窗那个顺序为准。
    /// </summary>
    [Fact]
    public void NeverBuilt_WinsOverDirty()
    {
        var state = new MemoryIndexState(false, "", true, null);

        Assert.Equal(EMemoryIndexStatus.NeverBuilt, MemoryIndexUiText.ResolveStatus(state));
    }

    /// <summary>建过索引又改了来源：已过期</summary>
    [Fact]
    public void Dirty_WhenIndexedThenSourcesChanged()
    {
        var state = new MemoryIndexState(false, "", true, DateTime.UtcNow);

        Assert.Equal(EMemoryIndexStatus.Dirty, MemoryIndexUiText.ResolveStatus(state));
    }

    /// <summary>建过索引且没再改动：就绪</summary>
    [Fact]
    public void Ready_WhenIndexedAndClean()
    {
        var state = new MemoryIndexState(false, "", false, DateTime.UtcNow);

        Assert.Equal(EMemoryIndexStatus.Ready, MemoryIndexUiText.ResolveStatus(state));
    }

    /// <summary>空白错误串不算出错，否则清空错误时写了个空格就会一直显示红</summary>
    [Fact]
    public void WhitespaceError_IsNotTreatedAsError()
    {
        var state = new MemoryIndexState(false, "   ", false, DateTime.UtcNow);

        Assert.Equal(EMemoryIndexStatus.Ready, MemoryIndexUiText.ResolveStatus(state));
    }

    /// <summary>快照取自记忆数据的字段，映射错一个就是两个窗口同时错</summary>
    [Fact]
    public void From_MapsMemoryFields()
    {
        DateTime indexedAt = new(2026, 8, 12, 3, 4, 5, DateTimeKind.Utc);
        var memory = new MemoryData { IndexDirty = true, LastIndexError = "boom", LastIndexedAt = indexedAt };

        MemoryIndexState state = MemoryIndexState.From(memory, isUpdating: true);

        Assert.Equal(new MemoryIndexState(true, "boom", true, indexedAt), state);
    }

    /// <summary>每档都要落在主题认得的 Tag 上；「从未建立」与「已过期」共用待处理色</summary>
    [Fact]
    public void StatusKeys_StayWithinThemeVocabulary()
    {
        foreach (EMemoryIndexStatus status in Enum.GetValues<EMemoryIndexStatus>())
            Assert.Contains(MemoryIndexUiText.GetStatusKey(status), ThemeStatusKeys);

        Assert.Equal("Dirty", MemoryIndexUiText.GetStatusKey(EMemoryIndexStatus.NeverBuilt));
        Assert.Equal("Dirty", MemoryIndexUiText.GetStatusKey(EMemoryIndexStatus.Dirty));
        Assert.Equal("Progress", MemoryIndexUiText.GetStatusKey(EMemoryIndexStatus.Updating));
        Assert.Equal("Error", MemoryIndexUiText.GetStatusKey(EMemoryIndexStatus.Error));
        Assert.Equal("Ready", MemoryIndexUiText.GetStatusKey(EMemoryIndexStatus.Ready));
    }

    /// <summary>每档的短文案与详述都得有话说，加了新档位而漏配文案时这里当场失败</summary>
    [Theory]
    [InlineData("zh-Hans")]
    [InlineData("en")]
    public void EveryStatus_HasCopy(string culture)
    {
        LocalizationManager.Instance.ApplyLanguage(culture, save: false);

        foreach (EMemoryIndexStatus status in Enum.GetValues<EMemoryIndexStatus>())
        {
            AssertHasCopy(MemoryIndexUiText.GetStatusTextKey(status), culture);
            AssertHasCopy(MemoryIndexUiText.GetStatusDetailTextKey(status), culture);
        }
    }

    /// <summary>出错档的详述来自后端错误，不是固定文案</summary>
    [Fact]
    public void ErrorStatus_DetailComesFromBackendError()
    {
        MemoryIndexStatusView view = MemoryIndexUiText.ResolveStatusView(
            new MemoryIndexState(false, "Memory name not set", true, null));

        Assert.Equal(EMemoryIndexStatus.Error, view.Status);
        Assert.Equal(MemoryIndexUiText.GetIndexErrorText("Memory name not set"), view.DetailText);
    }

    /// <summary>
    /// 上次索引时间只给纯值，不带「上次更新：」这类标签前缀——
    /// 前缀留给版面，两窗才可能显示成一致的东西（原先编辑窗带、选择窗不带）。
    /// </summary>
    [Fact]
    public void LastIndexedText_IsBareValueWithoutLabel()
    {
        string text = MemoryIndexUiText.GetLastIndexedText(new DateTime(2026, 8, 12, 3, 4, 0, DateTimeKind.Utc));

        Assert.Matches(new Regex(@"^\d{4}/\d{2}/\d{2} \d{2}:\d{2}$"), text);
    }

    /// <summary>从未索引时给专门的文案，而不是空串或占位时间</summary>
    [Theory]
    [InlineData("zh-Hans")]
    [InlineData("en")]
    public void LastIndexedText_NeverIndexed_HasCopy(string culture)
    {
        LocalizationManager.Instance.ApplyLanguage(culture, save: false);

        string text = MemoryIndexUiText.GetLastIndexedText(null);

        Assert.False(string.IsNullOrWhiteSpace(text));
        Assert.NotEqual("MemoryIndexNeverUpdated", text); //回退成键名 = 这个语言里没有这条
    }

    /// <summary>
    /// 后端错误按英文前缀匹配，上游一改措辞就会静默失配、把原始英文丢给用户看。
    /// 这里逐条钉住现状（不改匹配机制本身），改动措辞时至少有一条测试会红。
    /// </summary>
    [Theory]
    [InlineData("Embedding request failed: 500", "MemoryIndexEmbeddingRequestFailed")]
    [InlineData("embedding request failed: 500", "MemoryIndexEmbeddingRequestFailed")] //前缀匹配忽略大小写
    [InlineData("LLamaSharp embedding request failed: out of memory", "MemoryIndexEmbeddingRequestFailed")]
    [InlineData("Response status code does not indicate success: 401 (Unauthorized).",
        "MemoryIndexEmbeddingRequestFailed")]
    [InlineData("Embedding model startup failed.", "MemoryIndexEmbeddingServerUnavailable")]
    [InlineData("Failed to load LLamaSharp embedding model: /models/e5.gguf",
        "MemoryIndexEmbeddingServerUnavailable")]
    [InlineData("Remote embedding backend is not implemented yet.", "MemoryIndexEmbeddingServerUnavailable")]
    [InlineData("Memory vector store failed to open", "MemoryIndexStorageFailed")] //含子串即命中
    [InlineData("SQLite Error 8: 'attempt to write a readonly database'", "MemoryIndexStorageFailed")]
    [InlineData("Embedding server is unavailable.", "MemoryIndexEmbeddingServerUnavailable")]
    [InlineData("Embedding model is unavailable.", "MemoryIndexEmbeddingServerUnavailable")]
    [InlineData("Embedding server startup timed out.", "MemoryIndexEmbeddingServerTimeout")]
    [InlineData("Memory name not set", "MemoryIndexMemoryNameMissing")]
    [InlineData("Memory source validation failed", "MemorySourceValidationFailed")]
    [InlineData("Memory vector dimension mismatch", "MemoryIndexDimensionMismatch")]
    [InlineData("Embedding input is too large", "MemoryIndexEmbeddingInputTooLarge")]
    public void KnownBackendError_MapsToCopyKey(string error, string expectedKey)
    {
        Assert.Equal(expectedKey, MemoryIndexUiText.GetIndexErrorTextKey(error));
    }

    /// <summary>
    /// 认不出来的错误原样返回后端英文——这是现状而非期望。
    /// 「Memory index update failed」是 Core 自己写进 LastIndexError 的兜底串，
    /// 竟然也在这张表之外，一旦走到就是把英文摆给用户。
    /// </summary>
    [Theory]
    [InlineData("Memory index update failed")]
    [InlineData("Memory vector store unavailable")]
    [InlineData("Something nobody mapped yet")]
    public void UnknownBackendError_FallsBackToRawEnglish(string error)
    {
        Assert.Null(MemoryIndexUiText.GetIndexErrorTextKey(error));
        Assert.Equal(error, MemoryIndexUiText.GetIndexErrorText(error));
    }

    /// <summary>能被识别的后端错误，其文案键在两种语言里都要真的有译文</summary>
    [Theory]
    [InlineData("zh-Hans")]
    [InlineData("en")]
    public void EveryMappedBackendError_HasCopy(string culture)
    {
        LocalizationManager.Instance.ApplyLanguage(culture, save: false);
        string[] errors =
        [
            "Embedding request failed: 500",
            "Embedding model startup failed.",
            "Memory vector store failed to open",
            "Embedding server startup timed out.",
            "Memory name not set",
            "Memory source validation failed",
            "Memory vector dimension mismatch",
            "Embedding input is too large"
        ];

        IEnumerable<string> keys = errors
            .Select(MemoryIndexUiText.GetIndexErrorTextKey)
            .Select(key => key!)
            .Distinct();

        foreach (string key in keys) AssertHasCopy(key, culture);
    }

    private static void AssertHasCopy(string key, string culture)
    {
        string text = LocalizationManager.Instance.GetString(key);

        Assert.NotEqual(key, text); //回退成键名 = 这个语言里没有这条
        Assert.False(string.IsNullOrWhiteSpace(text), $"{culture} 的 {key} 文案是空的");
    }
}
