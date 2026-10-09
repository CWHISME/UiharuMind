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
using UiharuMind.Core.Core.LLM;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Runtime.Backends;

/// <summary>
/// 起对话用的 llama-server（一个模型一个进程，启动时定）；版本与安装归 <see cref="LLamaCppEngine"/>
/// </summary>
internal sealed class LLamaCppRuntimeService
{
    private readonly ConcurrentDictionary<string, LocalServerProcess> _servers = new(); //按模型名，就绪后到退出前

    /// <summary>
    /// 取一次某模型服务进程的实际占用
    /// </summary>
    /// <param name="modelName">模型名</param>
    /// <returns>占用；没在跑为 null</returns>
    public EngineUsage? SampleUsage(string modelName) =>
        _servers.TryGetValue(modelName, out LocalServerProcess? server) ? server.SampleUsage() : null;

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
        LocalServerProcess server = await LocalServerProcess.StartAsync(
                LLamaCppServerArgs.Launch(serverPath, LLamaCppServerArgs.Build(model, parameters, config.Server),
                    TimeSpan.FromSeconds(Math.Max(30, config.Server.LoadTimeoutSeconds)),
                    LLamaCppServerArgs.ParseEnvironment(config.Server.EnvironmentVariables)),
                onLoading, token)
            .ConfigureAwait(false);
        _servers[model.ModelName] = server;
        try
        {
            onLoadOver?.Invoke(OpenAICompatibleChatClient.Create(
                new OpenAICompatibleHttpHandler(port: server.Port), model, "UiharuMind", "None"));
            await server.Completion.ConfigureAwait(false);
        }
        finally
        {
            _servers.TryRemove(new KeyValuePair<string, LocalServerProcess>(model.ModelName, server));
        }

        if (!token.IsCancellationRequested)
            throw new LocalServerException(
                $"llama-server exited unexpectedly (exit code {server.ExitCode}).", server.LogTail);
    }
}
