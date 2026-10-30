/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 *
 * Latest Update: 2024.10.07
 ****************************************************************************/

using System.Collections.Concurrent;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Engines;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.AI.Net;
using UiharuMind.Core.Configs;
using UiharuMind.Core.Core;
using UiharuMind.Core.AI.Net.Transport;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Runtime.Backends;

/// <summary>
/// 起对话用的 llama-server（一个模型一个进程，启动时定）；版本与安装归 <see cref="LLamaCppEngine"/>
/// </summary>
internal sealed class LLamaCppRuntimeService
{
    private readonly ConcurrentDictionary<string, RunningServer> _servers = new(); //按模型名，就绪后到退出前

    /// <summary>
    /// 取一次某模型服务进程的实际占用；独立显卡上的显存按加载日志算（macOS 统一内存已在进程占用里）
    /// </summary>
    /// <param name="modelName">模型名</param>
    /// <returns>占用；没在跑为 null</returns>
    public EngineUsage? SampleUsage(string modelName)
    {
        if (!_servers.TryGetValue(modelName, out RunningServer? running) ||
            running.Process.SampleUsage() is not { } usage) return null;
        return OperatingSystem.IsMacOS() ? usage : usage with { GpuMemoryBytes = running.Buffers.DeviceBytes };
    }

    /// <summary>
    /// 起一个对话用的 llama-server，就绪后回调对话客户端，然后陪跑到进程结束
    /// </summary>
    /// <exception cref="LocalServerException">起不来，或没被要求停却退出了</exception>
    public async Task Run(
        VersionInfo version,
        ILlmModel model,
        RuntimeResolvedParameters parameters,
        Action<float>? onLoading = null,
        Action<IChatClient>? onLoadOver = null,
        CancellationToken token = default)
    {
        if (string.IsNullOrEmpty(model.ModelPath))
            throw new LocalServerException($"Model '{model.ModelName}' has no file path.");

        LLamaCppSettingConfig config = LLamaCppSettingConfig.Current;
        string serverPath = version.GetExecutable(LLamaCppSettingConfig.ServerExeName);
        LLamaCppBufferTally buffers = new();
        LocalServerProcess server = await LocalServerProcess.StartAsync(
                LLamaCppServerArgs.Launch(serverPath, LLamaCppServerArgs.Build(model, parameters, config.Server),
                    TimeSpan.FromSeconds(Math.Max(30, config.Server.LoadTimeoutSeconds)),
                    LLamaCppServerArgs.ParseEnvironment(config.Server.EnvironmentVariables), buffers.Observe),
                onLoading, token)
            .ConfigureAwait(false);
        RunningServer running = new(server, buffers);
        _servers[model.ModelName] = running;
        try
        {
            onLoadOver?.Invoke(ChatClientFactory.Create(
                new WireHttpHandler(port: server.Port), model, "UiharuMind", "None"));
            await server.Completion.ConfigureAwait(false);
        }
        finally
        {
            _servers.TryRemove(new KeyValuePair<string, RunningServer>(model.ModelName, running));
        }

        if (!token.IsCancellationRequested)
            throw new LocalServerException(
                $"llama-server exited unexpectedly (exit code {server.ExitCode}).", server.LogTail);
    }

    private sealed record RunningServer(LocalServerProcess Process, LLamaCppBufferTally Buffers);
}
