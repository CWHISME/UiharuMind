using System.Text;
using UiharuMind.Core.Core;

namespace UiharuMind.Core.AI.Audio;

/// <summary>
/// 音色库里的一个音色
/// </summary>
/// <param name="Name">音色名，也是请求里的 voice 与 wav 的文件名</param>
/// <param name="Path">参考音频</param>
/// <param name="Transcript">参考音频念的文字</param>
public sealed record VoiceSample(string Name, string Path, string Transcript);

/// <summary>
/// 音色库：一个目录里放参考音频 <c>名字.wav</c>，外加一个 <c>prompt_text</c> 每行 <c>名字|文字</c>。
/// 形状就是 audio.cpp 的 voice_dir，会克隆的模型请求时把 voice 写成名字即可，服务每次请求现读，增删不用重启
/// </summary>
public sealed class VoiceLibrary
{
    /// <summary>
    /// 文字映射文件名
    /// </summary>
    public const string TranscriptFileName = "prompt_text";

    private static readonly Lazy<VoiceLibrary> SharedInstance = new(() => new VoiceLibrary(AppPaths.Data.Voices));
    private static readonly char[] InvalidNameChars =
        [.. Path.GetInvalidFileNameChars(), '|', '/', '\\', ':', '*', '?', '"', '<', '>'];

    private readonly Lock _gate = new();

    /// <param name="directory">音色目录</param>
    public VoiceLibrary(string directory)
    {
        Directory = directory;
    }

    /// <summary>
    /// 走应用档案的那一个
    /// </summary>
    public static VoiceLibrary Shared => SharedInstance.Value;

    /// <summary>
    /// 音色目录
    /// </summary>
    public string Directory { get; }

    /// <summary>
    /// 增删了音色
    /// </summary>
    public event Action? Changed;

    /// <summary>
    /// 把用户起的名字整理成能当文件名的；整理完是空的返回 null
    /// </summary>
    /// <param name="name">原名</param>
    /// <returns>音色名</returns>
    public static string? NormalizeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        StringBuilder builder = new(name.Length);
        foreach (char c in name.Trim()) builder.Append(char.IsControl(c) || InvalidNameChars.Contains(c) ? '_' : c);
        string result = builder.ToString().Trim().TrimEnd('.');
        return result.Length == 0 || result == "." || result == ".." ? null : result;
    }

    /// <summary>
    /// 库里的音色，按名字排
    /// </summary>
    /// <returns>音色</returns>
    public IReadOnlyList<VoiceSample> List()
    {
        if (!System.IO.Directory.Exists(Directory)) return [];
        lock (_gate)
        {
            Dictionary<string, string> transcripts = ReadTranscripts();
            return System.IO.Directory.EnumerateFiles(Directory, "*.wav")
                .Select(path => Path.GetFileNameWithoutExtension(path))
                .Order(StringComparer.CurrentCultureIgnoreCase)
                .Select(name => new VoiceSample(name, WavPath(name), transcripts.GetValueOrDefault(name, "")))
                .ToList();
        }
    }

    /// <summary>
    /// 有没有这个音色
    /// </summary>
    /// <param name="name">音色名</param>
    public bool Contains(string name) => File.Exists(WavPath(name));

    /// <summary>
    /// 不重名的音色名：重了就在后面加序号
    /// </summary>
    /// <param name="name">想用的名字（已整理）</param>
    /// <returns>可用的名字</returns>
    public string MakeUnique(string name)
    {
        if (!Contains(name)) return name;
        for (int i = 2; ; i++)
        {
            string candidate = $"{name} {i}";
            if (!Contains(candidate)) return candidate;
        }
    }

    /// <summary>
    /// 存一个音色，同名覆盖
    /// </summary>
    /// <param name="name">音色名（已整理）</param>
    /// <param name="wav">参考音频（WAV）</param>
    /// <param name="transcript">参考音频念的文字</param>
    /// <returns>存好的音色</returns>
    public VoiceSample Save(string name, byte[] wav, string transcript)
    {
        if (NormalizeName(name) != name) throw new ArgumentException($"Invalid voice name '{name}'.", nameof(name));
        string text = string.Join(' ', transcript.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)).Trim();
        lock (_gate)
        {
            System.IO.Directory.CreateDirectory(Directory);
            File.WriteAllBytes(WavPath(name), wav);
            Dictionary<string, string> transcripts = ReadTranscripts();
            transcripts[name] = text;
            WriteTranscripts(transcripts);
        }

        Changed?.Invoke();
        return new VoiceSample(name, WavPath(name), text);
    }

    /// <summary>
    /// 删掉一个音色
    /// </summary>
    /// <param name="name">音色名</param>
    public void Delete(string name)
    {
        lock (_gate)
        {
            if (File.Exists(WavPath(name))) File.Delete(WavPath(name));
            Dictionary<string, string> transcripts = ReadTranscripts();
            if (transcripts.Remove(name)) WriteTranscripts(transcripts);
        }

        Changed?.Invoke();
    }

    private string WavPath(string name) => Path.Combine(Directory, name + ".wav");

    private Dictionary<string, string> ReadTranscripts()
    {
        Dictionary<string, string> result = new(StringComparer.Ordinal);
        string path = Path.Combine(Directory, TranscriptFileName);
        if (!File.Exists(path)) return result;
        foreach (string line in File.ReadLines(path))
        {
            int separator = line.IndexOf('|');
            if (separator <= 0) continue;
            result[line[..separator].TrimEnd()] = line[(separator + 1)..];
        }

        return result;
    }

    // 先写临时文件再换上：服务随时可能在读
    private void WriteTranscripts(Dictionary<string, string> transcripts)
    {
        string path = Path.Combine(Directory, TranscriptFileName);
        string temp = path + ".tmp";
        File.WriteAllLines(temp, transcripts.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => $"{x.Key}|{x.Value}"),
            new UTF8Encoding(false));
        File.Move(temp, path, true);
    }
}
