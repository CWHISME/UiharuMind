using System.Text.Json;
using UiharuMind.Core.AI.Engines;

namespace UiharuMind.Core.AI.Audio;

/// <summary>
/// server.json 里的一个模型条目
/// </summary>
/// <param name="Id">请求里用的模型 id</param>
/// <param name="Family">模型家族（GGUF 内嵌 spec 的 family，如 pocket_tts）</param>
/// <param name="Path">模型文件</param>
/// <param name="Task">tts / asr</param>
/// <param name="Mode">offline / streaming</param>
/// <param name="LoadOptions">加载选项</param>
/// <param name="SessionOptions">会话选项</param>
/// <param name="DefaultVoice">默认音色</param>
/// <param name="ModelFile">扫描到的 gguf 文件（<paramref name="Path"/> 可能是它所在目录）；不写进 server.json</param>
public sealed record AudioCppModelEntry(
    string Id,
    string Family,
    string Path,
    string Task,
    string Mode = "offline",
    IReadOnlyDictionary<string, string>? LoadOptions = null,
    IReadOnlyDictionary<string, string>? SessionOptions = null,
    string? DefaultVoice = null,
    string? ModelFile = null);

/// <summary>
/// server.json 的顶层选项
/// </summary>
/// <param name="Backend">metal / vulkan / cuda / cpu，由引擎包变体决定（设置可强制 cpu）</param>
/// <param name="Threads">CPU 线程数</param>
/// <param name="MaxLoadedModels">同时驻留的模型数，超出按最久未用淘汰</param>
/// <param name="IdleUnloadMs">模型闲置多久卸载</param>
/// <param name="MinFreeMemoryMb">加载后主机与 GPU 各至少剩多少内存，不够拒绝加载（503）；0 不检查</param>
public sealed record AudioCppServerOptions(
    string Backend,
    int Threads = 4,
    int MaxLoadedModels = 2,
    int IdleUnloadMs = 300000,
    int MinFreeMemoryMb = 0);

/// <summary>
/// audiocpp_server 由配置文件驱动（不是命令行参数）：端口分到后再写 server.json，形状照 hub 的 writeServerJSON 与实测配置
/// </summary>
public static class AudioCppServerConfig
{
    /// <summary>
    /// 日志与报错里的服务名
    /// </summary>
    public const string ServerName = AudioCppVersionManager.ServerExeName;

    /// <summary>
    /// 起 audiocpp_server 的方式
    /// </summary>
    /// <param name="executablePath">可执行文件</param>
    /// <param name="configPath">server.json 写到哪（每次启动覆盖）</param>
    /// <param name="models">模型条目</param>
    /// <param name="options">顶层选项</param>
    /// <param name="readyTimeout">就绪超时</param>
    /// <returns>启动描述</returns>
    public static LocalServerLaunch Launch(string executablePath, string configPath,
        IReadOnlyList<AudioCppModelEntry> models, AudioCppServerOptions options, TimeSpan readyTimeout)
    {
        return new LocalServerLaunch(ServerName, executablePath, port =>
        {
            Write(configPath, port, models, options);
            return ["--config", configPath, "--no-ui"];
        }, readyTimeout);
    }

    /// <summary>
    /// 写 server.json
    /// </summary>
    /// <param name="path">文件路径</param>
    /// <param name="port">监听端口</param>
    /// <param name="models">模型条目</param>
    /// <param name="options">顶层选项</param>
    public static void Write(string path, int port, IReadOnlyList<AudioCppModelEntry> models,
        AudioCppServerOptions options)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        using FileStream stream = File.Create(path);
        using Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Indented = true });
        writer.WriteStartObject();
        writer.WriteString("host", "127.0.0.1");
        writer.WriteNumber("port", port);
        writer.WriteString("backend", options.Backend);
        writer.WriteNumber("threads", options.Threads);
        writer.WriteBoolean("lazy_load", true);
        writer.WriteNumber("max_loaded_models", options.MaxLoadedModels);
        writer.WriteNumber("idle_unload_ms", options.IdleUnloadMs);
        writer.WriteNumber("min_free_memory_mb", options.MinFreeMemoryMb);
        writer.WriteStartArray("models");
        foreach (AudioCppModelEntry model in models) WriteModel(writer, model);
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteModel(Utf8JsonWriter writer, AudioCppModelEntry model)
    {
        writer.WriteStartObject();
        writer.WriteString("id", model.Id);
        writer.WriteString("family", model.Family);
        writer.WriteString("path", model.Path);
        writer.WriteString("task", model.Task);
        writer.WriteString("mode", model.Mode);
        WriteOptions(writer, "load_options", model.LoadOptions);
        WriteOptions(writer, "session_options", model.SessionOptions);
        if (!string.IsNullOrWhiteSpace(model.DefaultVoice))
        {
            writer.WriteStartObject("default_voice_preset");
            writer.WriteString("voice_id", model.DefaultVoice);
            writer.WriteEndObject();
        }

        writer.WriteEndObject();
    }

    private static void WriteOptions(Utf8JsonWriter writer, string name, IReadOnlyDictionary<string, string>? options)
    {
        if (options is not { Count: > 0 }) return;
        writer.WriteStartObject(name);
        foreach ((string key, string value) in options) writer.WriteString(key, value);
        writer.WriteEndObject();
    }
}
