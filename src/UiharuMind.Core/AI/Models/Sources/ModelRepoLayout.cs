using System.Text.RegularExpressions;
using UiharuMind.Core.AI.Runtime;

namespace UiharuMind.Core.AI.Models.Sources;

/// <summary>
/// 仓库里的一个可下载模型（一个量化；分片的算一个）
/// </summary>
/// <param name="Name">模型名（分片去掉后缀）</param>
/// <param name="Quantization">量化标签，如 Q4_K_M；认不出为空</param>
/// <param name="Files">组成文件，分片按片序</param>
public sealed record ModelRepoQuant(string Name, string Quantization, IReadOnlyList<ModelRepoFile> Files)
{
    /// <summary>
    /// 所有文件大小之和
    /// </summary>
    public long TotalSize => Files.Sum(x => x.Size);
}

/// <summary>
/// 把仓库文件列表理成「能下的量化 + 视觉投影」，并挑默认项
/// </summary>
public sealed partial class ModelRepoLayout
{
    private static readonly string[] PreferredQuantizations = ["Q4_K_M", "Q5_K_M", "Q4_K_S", "Q8_0"];
    private static readonly string[] PreferredProjectors = ["F16", "BF16", "F32", "Q8_0"];

    private ModelRepoLayout(IReadOnlyList<ModelRepoQuant> quants, IReadOnlyList<ModelRepoQuant> projectors)
    {
        Quants = quants;
        Projectors = projectors;
    }

    /// <summary>
    /// 主模型的各个量化
    /// </summary>
    public IReadOnlyList<ModelRepoQuant> Quants { get; }

    /// <summary>
    /// 视觉投影（mmproj）的各个精度
    /// </summary>
    public IReadOnlyList<ModelRepoQuant> Projectors { get; }

    /// <summary>
    /// 整理仓库文件：只要 gguf，分片合成一项，缺片的不列
    /// </summary>
    /// <param name="files">仓库文件</param>
    /// <returns>整理结果</returns>
    public static ModelRepoLayout From(IEnumerable<ModelRepoFile> files)
    {
        List<ModelRepoQuant> quants = [];
        List<ModelRepoQuant> projectors = [];
        foreach (IGrouping<string, (ModelRepoFile File, string BaseName, int Index, int Count)> group in files
                     .Where(x => x.Path.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase))
                     .Select(Describe)
                     .GroupBy(x => x.BaseName, StringComparer.Ordinal))
        {
            List<(ModelRepoFile File, string BaseName, int Index, int Count)> parts = group.OrderBy(x => x.Index).ToList();
            int count = parts[0].Count;
            if (count > 1 && (parts.Count != count || parts.Any(x => x.Count != count))) continue;

            string name = Path.GetFileName(group.Key);
            ModelRepoQuant quant = new(name, ParseQuantization(name), parts.Select(x => x.File).ToList());
            (name.Contains("mmproj", StringComparison.OrdinalIgnoreCase) ? projectors : quants).Add(quant);
        }

        return new ModelRepoLayout(quants.OrderBy(x => x.TotalSize).ToList(),
            projectors.OrderBy(x => x.TotalSize).ToList());
    }

    /// <summary>
    /// 从文件名认量化标签（取最后一个），如 Qwen3-8B-UD-Q4_K_XL → UD-Q4_K_XL
    /// </summary>
    /// <param name="name">不带扩展名的文件名</param>
    /// <returns>量化标签；认不出为空</returns>
    public static string ParseQuantization(string name)
    {
        MatchCollection matches = QuantizationPattern().Matches(name);
        return matches.Count == 0 ? "" : matches[^1].Value.ToUpperInvariant();
    }

    /// <summary>
    /// 默认量化：偏好表里能跑（绿）的 → 能跑里最大的 → 偏好表里第一个有的 → 最小的
    /// </summary>
    /// <param name="fit">估某个量化能不能跑</param>
    /// <returns>默认项；仓库里没有可下的为 null</returns>
    public ModelRepoQuant? PickDefaultQuant(Func<ModelRepoQuant, RuntimeLoadRiskLevel> fit)
    {
        List<ModelRepoQuant> preferred = PreferredQuantizations
            .Select(label => Quants.FirstOrDefault(x => x.Quantization == label))
            .OfType<ModelRepoQuant>()
            .ToList();
        return preferred.FirstOrDefault(x => fit(x) == RuntimeLoadRiskLevel.Low)
               ?? Quants.LastOrDefault(x => fit(x) == RuntimeLoadRiskLevel.Low)
               ?? preferred.FirstOrDefault()
               ?? Quants.FirstOrDefault();
    }

    /// <summary>
    /// 默认视觉投影：F16 优先（体积和精度的折中），没有就依次退
    /// </summary>
    /// <returns>默认项；仓库没有视觉投影为 null</returns>
    public ModelRepoQuant? PickDefaultProjector()
    {
        return PreferredProjectors
                   .Select(label => Projectors.FirstOrDefault(x => x.Quantization == label))
                   .OfType<ModelRepoQuant>()
                   .FirstOrDefault()
               ?? Projectors.FirstOrDefault();
    }

    private static (ModelRepoFile File, string BaseName, int Index, int Count) Describe(ModelRepoFile file)
    {
        string directory = Path.GetDirectoryName(file.Path)?.Replace('\\', '/') ?? "";
        string name = Path.GetFileNameWithoutExtension(file.Path);
        GGufSplitName.TryParse(name, out string baseName, out int index, out int count);
        string key = directory.Length == 0 ? baseName : $"{directory}/{baseName}";
        return (file, key, Math.Max(index, 1), Math.Max(count, 1));
    }

    // 量化标签：Q4_K_M、Q8_0、IQ4_XS、TQ1_0、BF16、F16、F32、MXFP4_MOE，可带 unsloth 的 UD- 前缀
    [GeneratedRegex(@"(?<![A-Za-z0-9])(?:UD-)?(?:I?Q\d+(?:_[A-Z0-9]+)*|TQ\d_\d|BF16|F16|F32|MXFP4(?:_MOE)?)(?![A-Za-z0-9])",
        RegexOptions.IgnoreCase)]
    private static partial Regex QuantizationPattern();
}
