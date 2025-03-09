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

using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.KernelMemory;
using Microsoft.KernelMemory.AI.Ollama;
using Microsoft.SemanticKernel;
using UiharuMind.Core.AI.Interfaces;
using UiharuMind.Core.AI.LocalAI.LLamaCpp.Configs;
using UiharuMind.Core.AI.Net;
using UiharuMind.Core.Core.LLM;
using UiharuMind.Core.Core.Process;
using UiharuMind.Core.Core.ServerKernal;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.LLamaCpp.Data;
using UiharuMind.Core.LLamaCpp.Versions;

namespace UiharuMind.Core.LLamaCpp;

public class LLamaCppServerKernal : ServerKernalBase<LLamaCppServerKernal, LLamaCppSettingConfig>, ILlmRuntimeEngine
{
    private LLamaCppVersionManager _llamaCppVersionManager = new LLamaCppVersionManager();
    private Dictionary<string, GGufModelInfo> _modelInfos = new Dictionary<string, GGufModelInfo>();

    private readonly string EmbedeHttpServerUrl;
    private readonly string EmbedeHttpProxyServerUrl;
    private readonly Uri EmbedeHttpServerUri;

    enum EmbededServerStatus
    {
        Stop,
        Loading,
        Running,
    }

    private GGufModelInfo _embededModelInfo = new GGufModelInfo();
    private EmbededServerStatus _embededServerStatus = EmbededServerStatus.Stop;
    private readonly Queue<Action<OpenAIConfig>> _pendingEmbeddedServerActions = new Queue<Action<OpenAIConfig>>();

    private OpenAIConfig? _embeddedServerConfig;

    // private HttpProxyServer? _httpProxyServer;
    private const int EmbedingModelMaxToken = 8191;
    private const int EmbedingModelMaxTokenServer = EmbedingModelMaxToken + 1;


    public GGufModelInfo EmbededModelInfo
    {
        get
        {
            string? filePath = null;
            if (Directory.Exists(Config.ExternalEmbededModelPath))
            {
                filePath = Directory.GetFiles(Config.ExternalEmbededModelPath, "*.gguf", SearchOption.TopDirectoryOnly)
                    .FirstOrDefault();
            }

            filePath ??= Directory.GetFiles(Config.DefaultEmbededModelPath, "*.gguf", SearchOption.TopDirectoryOnly)
                .FirstOrDefault();
            if (filePath == null) return _embededModelInfo;

            _embededModelInfo = new GGufModelInfo()
                { ModelName = Path.GetFileNameWithoutExtension(filePath), ModelPath = filePath };
            return _embededModelInfo;
        }
    }

    private OpenAIConfig EmbeddedServerConfig
    {
        get
        {
            if (_embeddedServerConfig == null)
            {
                _embeddedServerConfig = new OpenAIConfig
                {
                    Endpoint = EmbedeHttpServerUrl,
                    EmbeddingModel = "None",
                    EmbeddingModelMaxTokenTotal = EmbedingModelMaxToken,
                    APIKey = "None"
                };
            }

            return _embeddedServerConfig;
        }
    }

    public LLamaCppServerKernal()
    {
        EmbedeHttpServerUrl = $"http://localhost:{Config.DefaultEmbededPort}/";
        EmbedeHttpProxyServerUrl = $"http://localhost:{Config.DefaultEmbededProxyPort}/";
        EmbedeHttpServerUri = new Uri(EmbedeHttpServerUrl);
    }

    public async Task StartServer(string executablePath, string modelFilePath, string projFilePath, int port,
        Action<string>? onMessageUpdate = null, string extraParams = "", CancellationToken token = default)
    {
        // var exePath = Config.GetExeServerPath(executablePath);
        // if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
        // {
        //     Log.Error($"Can't find server executable {exePath}");
        //     return;
        // }
        if (string.IsNullOrEmpty(modelFilePath))
        {
            Log.Error($"Can't run server without model file path");
            return;
        }

        // config ??= Config.ServerConfig;
        // {(string.IsNullOrEmpty(projFilePath) ? "" : $"--mmproj {projFilePath}")}
        var totalParams =
            $"-m \"{modelFilePath}\" --no-webui --alias {Path.GetFileNameWithoutExtension(modelFilePath)} --port {port} {Config
                .GetExeParams()} {extraParams}";
        Log.Debug("Start sever:" + totalParams);
        await ProcessHelper.StartProcess(Config.GetExeServerPath(executablePath), totalParams, onMessageUpdate, token)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 尝试确保嵌入式服务启动
    /// </summary>
    public void TryEnsureEmbededServer(VersionInfo versionInfo, Action<OpenAIConfig>? onLoadOver = null)
    {
        if (_embededServerStatus == EmbededServerStatus.Running)
        {
            onLoadOver?.Invoke(EmbeddedServerConfig);
            return;
        }

        // //文本嵌入代理处理
        // _httpProxyServer ??= new HttpProxyServer(EmbedeHttpProxyServerUrl, x => EmbedeHttpServerUri,
        //     (x) =>
        //     {
        //         char[] chars = new char[x.Length - 2];
        //         x.CopyTo(1, chars, 0, chars.Length);
        //         // JsonNode rootNode = JsonNode.Parse(new string(chars))!;
        //         // rootNode["data"] = "";
        //         // rootNode["model"] = "";
        //         // rootNode["usage"] = "";
        //         // return rootNode.ToJsonString();
        //         return new string(chars);
        //     });
        // _httpProxyServer.Start();
        Task.Run(async () => { await StartEmbededServer(versionInfo, onLoadOver); }).ConfigureAwait(false);
    }

    public async Task Run(VersionInfo info, ILlmModel model, Action<float>? onLoading = null,
        Action<Kernel>? onLoadOver = null, int? port = null, string extraParams = "",
        CancellationToken token = default)
    {
        //检测是否正常扫描过信息
        string projFilePath = "";
        if (model is GGufModelInfo modelInfo)
        {
            if (modelInfo.ModelProjPath != null) projFilePath = modelInfo.ModelProjPath;
            if (!modelInfo.IsReady)
            {
                // await GetModelStateInfo(info.ExecutablePath, model.ModelPath).ConfigureAwait(false);
                //TODO:扫描本地模型，更新缓存信息，以便进行运行时参数设置
            }
        }
        //================================

        int loadingCount = 0;
        bool loadOver = false;

        const float loadingMaxCount = 128f;
        const int loadingMinCount = 16;

        void OnMessageUpdate(string msg)
        {
            // if (Config.DebugConfig.LogRunningInfo) Log.Debug(msg);
            if (loadOver) return;

            if (msg.StartsWith("error"))
            {
                onLoadOver?.Invoke(CreateKernel());
                return;
            }

            if (!msg.StartsWith("main: server is listening"))
            {
                loadingCount++;

                // 增加间隔，避免频繁调用
                if (loadingCount % loadingMinCount == 0)
                {
                    float loadingPercent = Math.Min(1, loadingCount / loadingMaxCount);
                    onLoading?.Invoke(loadingPercent);
                }

                // 如果 loadingCount 达到上限，直接当做完成加载
                if (loadingCount >= loadingMaxCount)
                {
                    Log.Debug($"Loading over cout {loadingCount}");
                    // loadOver = true;
                    // onLoadOver?.Invoke(CreateKernel());
                }
            }
            else
            {
                Log.Debug($"Loading over {loadingCount}");
                loadOver = true;
                onLoadOver?.Invoke(CreateKernel());
            }
        }

        await StartServer(info.ExecutablePath, model.ModelPath, projFilePath, port ?? Config.DefautPort,
                OnMessageUpdate, extraParams,
                token)
            .ConfigureAwait(false);
    }

    private async Task StartEmbededServer(VersionInfo versionInfo, Action<OpenAIConfig>? onLoadOver = null)
    {
        try
        {
            if (_embededServerStatus == EmbededServerStatus.Loading)
            {
                if (onLoadOver != null) _pendingEmbeddedServerActions.Enqueue(onLoadOver);
                return;
            }

            _embededServerStatus = EmbededServerStatus.Loading;
            await Run(versionInfo, EmbededModelInfo, onLoadOver: (x) =>
                {
                    _embededServerStatus = EmbededServerStatus.Running;
                    foreach (var action in _pendingEmbeddedServerActions)
                    {
                        action(EmbeddedServerConfig);
                    }

                    _pendingEmbeddedServerActions.Clear();
                    onLoadOver?.Invoke(EmbeddedServerConfig);
                },
                port: Config.DefaultEmbededPort, extraParams: $"--embedding -ub {EmbedingModelMaxTokenServer}");
        }
        catch (Exception e)
        {
            Log.Error("Start embeded server failed：" + e.Message);
        }
        finally
        {
            _embededServerStatus = EmbededServerStatus.Stop;
        }
    }

    private Kernel CreateKernel()
    {
        var kernelBuilder = Kernel.CreateBuilder()
            .AddOpenAIChatCompletion("UiharuMind", "None",
                httpClient: new HttpClient(new SKernelHttpDelegatingHandler(port: Config.DefautPort)));
        return kernelBuilder.Build();
    }

    // public async Task Run(ILlmModel model, Action<CancellationTokenSource> onStartLoad, Action<float>? onLoading = null,
    //     Action? onLoadOver = null)
    // {
    //     int loadingCount = 0;
    //
    //     void OnMessageUpdate(string msg)
    //     {
    //         Log.Debug(msg);
    //         if (loadingCount < 128 && !msg.StartsWith("main: server is listening"))
    //         {
    //             loadingCount++;
    //             var loadingPercent = Math.Min(1, loadingCount / 128f);
    //             onLoading?.Invoke(loadingPercent);
    //         }
    //         else onLoadOver?.Invoke();
    //     }
    //
    //     await StartServer(model.ModelPath, Config.DefautPort, onStartLoad, OnMessageUpdate);
    // }

    public async Task<IReadOnlyDictionary<string, GGufModelInfo>> GetModelList(VersionInfo? versionInfo)
    {
        return await ScanAllLocalModels(versionInfo).ConfigureAwait(false);
    }

    public async Task<Dictionary<string, GGufModelInfo>> ScanAllLocalModels(VersionInfo? versionInfo,
        bool force = false)
    {
        _modelInfos.Clear();
        await ScanLocalModels(versionInfo, Config.DefaultLocalModelPath, _modelInfos);
        return await ScanLocalModels(versionInfo, Config.LocalModelPath, _modelInfos);
    }

    private async Task<Dictionary<string, GGufModelInfo>> ScanLocalModels(VersionInfo? versionInfo, string modelPath,
        Dictionary<string, GGufModelInfo> modelInfos,
        bool force = false)
    {
        string? executablePath = Config.GetExeLookupStatsPath(versionInfo?.ExecutablePath);
        // _modelInfos.Clear();
        if (!Directory.Exists(modelPath)) return modelInfos;
        string[] files = Directory.GetFiles(modelPath, "*.gguf", SearchOption.AllDirectories);
        bool isChanged = false;
        foreach (var file in files)
        {
            string fileName = Path.GetFileNameWithoutExtension(file);
            if (fileName.Contains("mmproj", StringComparison.Ordinal)) continue;

            //check vision
            string projPath = "";
            // var dir = Path.GetDirectoryName(file);
            // if (dir != null)
            // {
            //     var mmproj = Directory.GetFiles(dir, "*.gguf", SearchOption.TopDirectoryOnly)
            //         .FirstOrDefault(x => x.Contains("mmproj", StringComparison.Ordinal));
            //     projPath = mmproj;
            // }

            //缓存扫描结果，避免重复扫描，除非强制标记
            if (!force && Config.ModelInfos.TryGetValue(fileName, out var info))
                // if (Config.ModelInfos.ContainsKey(fileName))
            {
                info.ModelPath = file;
                info.ModelProjPath = projPath;
                modelInfos[fileName] = info;
                continue;
            }

            if (executablePath == null) continue;

            if (!isChanged) isChanged = true;
            info = new GGufModelInfo(); //await GetModelStateInfo(executablePath, file);
            info.ModelName = fileName;
            info.ModelPath = file;
            info.ModelProjPath = projPath;

            modelInfos[fileName] = info;
            Config.ModelInfos[fileName] = info;
            // break;
        }

        if (isChanged) Config.Save();
        //缓存一次时因为可能出现更换目录，配置设置还在但是模型已经不在的情况
        return modelInfos;
        // return null;
    }

    // public async Task ScanLocalModel(string modelFilePath)
    // {
    //     var info = await GetModelStateInfo(Config.ExeLookupStats, modelFilePath);
    //     Config.ModelInfos[Path.GetFileName(modelFilePath)] = info;
    // }

    /// <summary>
    /// 获取本地版本列表
    /// </summary>
    /// <param name="enginePath"></param>
    /// <returns></returns>
    public async Task<VersionManager> GetLocalVersions(string enginePath)
    {
        string path = Path.Combine(enginePath, "LLamaCpp");
        if (!Directory.Exists(path)) Directory.CreateDirectory(path);
        var versionManager = await _llamaCppVersionManager.GetLocalVersions(path).ConfigureAwait(false);

        //合并内部版本
        string internalPath = Path.Combine(Config.DefaultRuntimePath, "LLamaCpp");
        if (!Directory.Exists(internalPath)) return versionManager;
        var internalVersion = await _llamaCppVersionManager.GetLocalVersions(internalPath, true).ConfigureAwait(false);
        foreach (var ver in internalVersion.VersionsList)
        {
            ver.IsInternal = true;
        }

        versionManager.Merge(internalVersion);
        return versionManager;
    }

    /// <summary>
    /// 拉取最新版本列表
    /// </summary>
    /// <returns></returns>
    public async Task<VersionManager> PullLastestVersion(string enginePath)
    {
        string path = Path.Combine(enginePath, "LLamaCpp");
        if (!Directory.Exists(path)) Directory.CreateDirectory(path);
        return await _llamaCppVersionManager.GetLatestVersion(path);
    }

    private async Task<GGufModelInfo> GetModelStateInfo(string exec, string file)
    {
        // Stopwatch stopwatch = new Stopwatch();
        // stopwatch.Start();
        GGufModelInfo info = new GGufModelInfo();
        // await ProcessHelper.StartProcess(lookupExe, $"-m {file}",
        //     async (line, cts) => await ParseModelInfo(line, info, cts));
        CancellationTokenSource cts = new CancellationTokenSource();
        await ProcessHelper.StartProcess(exec, $"-m {file} -v",
            (line) => ParseModelInfo(line, info, cts),
            cts.Token);
        // stopwatch.Stop();
        // Log.Debug($"Scan Model {file} {stopwatch.ElapsedMilliseconds}");
        return info;
    }

    private void ParseModelInfo(string line, GGufModelInfo info, CancellationTokenSource cts)
    {
        if (line.StartsWith("llm_load_tensors", StringComparison.Ordinal)) cts.Cancel();
        info.UpdateValue(line);
    }
}