using System;
using System.Collections.Generic;
using System.Linq;
using UiharuMind.Core.AI;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.AI.Engines;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Models.ChatModels;

/// <summary>
/// 对话模型页签顶上的引擎状态条：每个本地模型各起一个 llama-server，这里把它们合成一台「本地引擎」
/// </summary>
public static class ChatEngineStatus
{
    private static readonly EngineStatusTexts Texts = new(LangKey.ChatEngineStatusStarting,
        LangKey.ChatEngineStatusStopped, LangKey.ChatEngineStatusFailed);

    /// <summary>
    /// 跟着本机对话模型进程的状态条：运行中那句说加载了几个模型，释放即全部卸载
    /// </summary>
    /// <param name="llm">模型管理</param>
    /// <param name="openSettings">去引擎设置</param>
    /// <returns>状态条</returns>
    public static EngineStatusData Create(LlmManager llm, Action openSettings)
    {
        LlamaModelsRuntime runtime = new(llm);
        return new EngineStatusData(runtime, Texts,
            () => Loc.Text(LangKey.ChatEngineStatusRunning, runtime.Active().Count(x => x.IsRunning)),
            openSettings: openSettings);
    }

    // 起停事件只有当前模型的，其余模型的变化靠状态条看得见时的定时刷新补上
    private sealed class LlamaModelsRuntime : IEngineRuntime
    {
        private readonly LlmManager _llm;

        public LlamaModelsRuntime(LlmManager llm)
        {
            _llm = llm;
            _llm.OnCurrentModelStartLoading += () => StateChanged?.Invoke();
            _llm.OnCurrentModelLoading += _ => LoadProgressChanged?.Invoke();
            _llm.OnCurrentModelLoaded += () =>
            {
                LastError = null;
                StateChanged?.Invoke();
            };
            _llm.OnCurrentModelChanged += _ => StateChanged?.Invoke();
            _llm.OnModelRunFailed += (_, error) =>
            {
                LastError = error as LocalServerException ?? new LocalServerException(error.Message);
                StateChanged?.Invoke();
            };
        }

        public event Action? StateChanged;
        public event Action? LoadProgressChanged;

        public LocalServerException? LastError { get; private set; }

        public EEngineRunState State
        {
            get
            {
                List<ModelRunningData> active = Active().ToList();
                if (active.Any(x => x.IsLoading)) return EEngineRunState.Starting;
                if (active.Count > 0) return EEngineRunState.Running;
                return LastError != null ? EEngineRunState.Failed : EEngineRunState.Stopped;
            }
        }

        public float LoadProgress => Active().Where(x => x.IsLoading).Select(x => x.LoadingPercent).DefaultIfEmpty(0).Max();

        public IEnumerable<ModelRunningData> Active() =>
            _llm.CacheModelDictionary.Values.Where(x => !x.IsRemoteModel && (x.IsRunning || x.IsLoading));

        public EngineUsage? SampleUsage()
        {
            List<EngineUsage> usages = Active().Where(x => x.IsRunning)
                .Select(x => _llm.SampleUsage(x.ModelName)).OfType<EngineUsage>().ToList();
            if (usages.Count == 0) return null;
            return new EngineUsage(usages.Sum(x => x.MemoryBytes),
                usages.All(x => x.CpuPercent == null) ? null : usages.Sum(x => x.CpuPercent ?? 0),
                usages.All(x => x.GpuMemoryBytes == null) ? null : usages.Sum(x => x.GpuMemoryBytes ?? 0));
        }

        public void Stop()
        {
            foreach (string name in Active().Select(x => x.ModelName).ToList()) _llm.UnloadModel(name);
            LastError = null;
            StateChanged?.Invoke();
        }
    }
}
