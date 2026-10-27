using System.IO;
using UiharuMind.Core.Configs.RemoteAI;
using UiharuMind.Core.RemoteOpenAI;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// 思考字段观测的落盘闭包（<see cref="RemoteModelManager.CreateThinkingFieldObserver"/>）：
/// 流式每帧都可能触发，值不变必须零写入；Save 失败不能打断响应读取；
/// 观测值按 ModelId 分开记，切模型不能串台。
/// </summary>
public class ThinkingFieldObserverTests
{
    private static RemoteModelConfig NewConfig(string modelId = "m1") => new() { ModelId = modelId };

    [Fact]
    public void FirstObservation_WritesConfigAndSavesOnce()
    {
        RemoteModelConfig config = NewConfig();
        int saves = 0;
        Action<string> observer = RemoteModelManager.CreateThinkingFieldObserver(config, () => saves++);

        observer("reasoning");

        Assert.Equal("reasoning", config.ObservedThinkingFields["m1"]);
        Assert.Equal(1, saves);
    }

    [Fact]
    public void SameValue_IsSkipped_NoWriteNoSave()
    {
        RemoteModelConfig config = NewConfig();
        config.ObservedThinkingFields["m1"] = "reasoning";
        int saves = 0;
        Action<string> observer = RemoteModelManager.CreateThinkingFieldObserver(config, () => saves++);

        observer("reasoning");
        observer("reasoning");
        observer("reasoning");

        Assert.Equal("reasoning", config.ObservedThinkingFields["m1"]);
        Assert.Equal(0, saves); // 值未变,一次都不落盘
    }

    [Fact]
    public void NewValueAfterOld_IsWritten()
    {
        RemoteModelConfig config = NewConfig();
        config.ObservedThinkingFields["m1"] = "reasoning";
        int saves = 0;
        Action<string> observer = RemoteModelManager.CreateThinkingFieldObserver(config, () => saves++);

        observer("reasoning_text");

        Assert.Equal("reasoning_text", config.ObservedThinkingFields["m1"]);
        Assert.Equal(1, saves);
    }

    /// <summary>观测值按 ModelId 分开记：在 m2 上观测不能覆盖 m1 的记录</summary>
    [Fact]
    public void Observation_IsScopedToModelId()
    {
        RemoteModelConfig config = NewConfig();
        Action<string> observer = RemoteModelManager.CreateThinkingFieldObserver(config, () => { });

        observer("reasoning");
        config.ModelId = "m2";
        observer("reasoning_text");
        config.ModelId = "m1";

        Assert.Equal("reasoning", config.ObservedThinkingFields["m1"]);
        Assert.Equal("reasoning_text", config.ObservedThinkingFields["m2"]);
        Assert.Equal("reasoning", config.EffectiveThinkingField);
    }

    [Fact]
    public void SaveFailure_DoesNotPropagate_AndKeepsInMemoryValue()
    {
        RemoteModelConfig config = NewConfig();
        Action<string> observer = RemoteModelManager.CreateThinkingFieldObserver(config,
            () => throw new IOException("disk full"));

        observer("reasoning_text"); // 不应抛出

        Assert.Equal("reasoning_text", config.ObservedThinkingFields["m1"]); // 内存值保留,重启前仍然有效
    }

    [Fact]
    public void RepeatedFailure_DoesNotRetrySaveEveryFrame()
    {
        RemoteModelConfig config = NewConfig();
        int saves = 0;
        Action<string> observer = RemoteModelManager.CreateThinkingFieldObserver(config,
            () =>
            {
                saves++;
                throw new IOException("disk full");
            });

        observer("reasoning");
        observer("reasoning");
        observer("reasoning");

        Assert.Equal(1, saves); // 值已在第一次写入,后续同值帧连 Save 都不触发
    }
}
