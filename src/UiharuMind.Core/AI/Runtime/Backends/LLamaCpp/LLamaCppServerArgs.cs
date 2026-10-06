using System.Text;
using UiharuMind.Core.AI.Models;

namespace UiharuMind.Core.AI.Runtime.Backends;

/// <summary>
/// 拼对话用 llama-server 的启动参数与环境变量
/// </summary>
public static class LLamaCppServerArgs
{
    /// <summary>
    /// 对话服务参数（不含 host/port）。等于 llama-server 默认值的项不传
    /// </summary>
    /// <param name="model">模型</param>
    /// <param name="parameters">解析后的通用参数</param>
    /// <param name="options">llama-server 专有选项</param>
    /// <returns>逐项参数</returns>
    public static IReadOnlyList<string> Build(ILlmModel model, RuntimeResolvedParameters parameters,
        LLamaCppServerOptions options)
    {
        List<string> args =
        [
            "-m", model.ModelPath,
            "--no-webui",
            "--alias", Path.GetFileNameWithoutExtension(model.ModelPath),
            "-to", "0",
            "-c", parameters.ContextSize.ToString(),
            "-b", parameters.BatchSize.ToString(),
            "-ub", parameters.UBatchSize.ToString(),
            // 用模型自带的聊天模板渲染，工具调用也靠它
            "--jinja"
        ];

        if (model is GGufModelInfo { ModelProjPath: { Length: > 0 } projPath })
            args.AddRange(["--mmproj", projPath]);
        // 负数是自动：不传，交给 llama-server（默认 auto，配合 --fit 按显存放层）
        if (parameters.GpuLayers >= 0)
            args.AddRange(["-ngl", parameters.GpuLayers.ToString()]);
        if (parameters.Threads > 0)
            args.AddRange(["--threads", parameters.Threads.ToString()]);
        if (parameters.FlashAttention is { } flashAttention)
            args.AddRange(["--flash-attn", flashAttention ? "on" : "off"]);

        if (!options.Fit) args.AddRange(["--fit", "off"]);
        else if (options.FitTargetMiB != 1024) args.AddRange(["-fitt", options.FitTargetMiB.ToString()]);
        if (options.ThreadsBatch > 0) args.AddRange(["-tb", options.ThreadsBatch.ToString()]);
        if (options.Parallel > 0) args.AddRange(["-np", options.Parallel.ToString()]);
        if (!options.ContinuousBatching) args.Add("--no-cont-batching");
        if (options.CacheTypeK != LLamaCppServerOptions.DefaultCacheType) args.AddRange(["-ctk", options.CacheTypeK]);
        if (options.CacheTypeV != LLamaCppServerOptions.DefaultCacheType) args.AddRange(["-ctv", options.CacheTypeV]);
        if (options.LoadMode != LLamaCppServerOptions.DefaultLoadMode) args.AddRange(["--load-mode", options.LoadMode]);
        if (options.NoKvOffload) args.Add("--no-kv-offload");
        if (options.CpuMoeLayers > 0) args.AddRange(["-ncmoe", options.CpuMoeLayers.ToString()]);
        if (options.CacheRamMiB != 8192) args.AddRange(["-cram", options.CacheRamMiB.ToString()]);
        if (options.CacheReuse > 0) args.AddRange(["--cache-reuse", options.CacheReuse.ToString()]);
        if (options.ContextShift) args.Add("--context-shift");
        if (options.SwaFull) args.Add("--swa-full");

        args.AddRange(SplitArguments(options.ExtraArguments));
        return args;
    }

    /// <summary>
    /// 解析环境变量设置：KEY=VALUE，以分号或换行分隔；无效项跳过
    /// </summary>
    /// <param name="text">设置文本</param>
    /// <returns>环境变量</returns>
    public static IReadOnlyDictionary<string, string> ParseEnvironment(string? text)
    {
        Dictionary<string, string> environment = new(StringComparer.Ordinal);
        foreach (string item in (text ?? "").Split([';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
        {
            int index = item.IndexOf('=');
            if (index <= 0) continue;
            string key = item[..index].Trim();
            if (key.Length > 0) environment[key] = item[(index + 1)..].Trim();
        }

        return environment;
    }

    /// <summary>
    /// 按空白拆参数，双引号内的空白不拆
    /// </summary>
    /// <param name="text">参数文本</param>
    /// <returns>逐项参数</returns>
    public static IReadOnlyList<string> SplitArguments(string? text)
    {
        List<string> result = [];
        StringBuilder current = new();
        bool inQuotes = false;
        bool hasToken = false;
        foreach (char c in text ?? "")
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
                hasToken = true;
            }
            else if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (hasToken) result.Add(current.ToString());
                current.Clear();
                hasToken = false;
            }
            else
            {
                current.Append(c);
                hasToken = true;
            }
        }

        if (hasToken) result.Add(current.ToString());
        return result;
    }
}
