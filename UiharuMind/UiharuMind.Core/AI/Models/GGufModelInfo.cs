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

using System.Text.Json.Serialization;
using UiharuMind.Core.AI.Models;

namespace UiharuMind.Core.AI.Models;

public class GGufModelInfo : ILlmModel
{
    public string ModelName { get; set; }

    //质量降级的：GENERATION QUALITY WILL BE DEGRADED! CONSIDER REGENERATING THE MODEL
    public bool IsDegraded { get; set; }

    [JsonIgnore] public string ModelPath { get; set; }

    //投影路径
    [JsonIgnore] public string? ModelProjPath { get; set; }
    public bool IsVision => !string.IsNullOrEmpty(ModelProjPath);
    public string ModelDescription { get; }
    public string ModelId { get; }
    public int Port { get; set; }
    public string ApiKey { get; }
    public bool IsFavorite { get; set; }
    public string Architecture { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string SizeLabel { get; set; } = "";
    public int ContextLength { get; set; }
    public int EmbeddingLength { get; set; }
    public int LayerCount { get; set; }
    public int AttentionHeadCount { get; set; }
    public int AttentionHeadCountKv { get; set; }
    public ulong ParameterCount { get; set; }
    public ulong FileSizeBytes { get; set; }

    [JsonInclude] private Dictionary<string, string> Infos { get; set; } = new Dictionary<string, string>(10);

    /// <summary>
    /// 是否已扫描到信息
    /// </summary>
    public bool IsReady => Infos.Count > 0 || ContextLength > 0;

    public string MetadataSummary
    {
        get
        {
            List<string> parts = [];
            if (!string.IsNullOrWhiteSpace(Architecture)) parts.Add(Architecture);
            if (!string.IsNullOrWhiteSpace(SizeLabel)) parts.Add(SizeLabel);
            if (ContextLength > 0) parts.Add($"{ContextLength:N0} ctx");
            if (LayerCount > 0) parts.Add($"{LayerCount} layers");
            return parts.Count == 0 ? "" : string.Join(" · ", parts);
        }
    }

    public void ApplyMetadata(GGufMetadataInfo? metadata)
    {
        if (metadata == null) return;

        Architecture = metadata.Architecture;
        DisplayName = metadata.DisplayName;
        SizeLabel = metadata.SizeLabel;
        ContextLength = metadata.ContextLength;
        EmbeddingLength = metadata.EmbeddingLength;
        LayerCount = metadata.LayerCount;
        AttentionHeadCount = metadata.AttentionHeadCount;
        AttentionHeadCountKv = metadata.AttentionHeadCountKv;
        ParameterCount = metadata.ParameterCount;
        FileSizeBytes = metadata.FileSizeBytes;
        foreach ((string key, string value) in metadata.RawMetadata)
            Infos[key] = value;
    }

    public void UpdateValue(string lineInfo)
    {
        //check DEGRADED
        if (lineInfo.Contains("DEGRADED", StringComparison.Ordinal)) IsDegraded = true;

        var input = lineInfo.AsSpan();

        int colonIndex = input.LastIndexOf(':');
        if (colonIndex >= 0)
        {
            colonIndex++;
            while (colonIndex < input.Length && char.IsWhiteSpace(input[colonIndex]))
            {
                colonIndex++;
            }

            // 查找等号的位置
            int eqIndex = input.Slice(colonIndex).IndexOf('=');
            if (eqIndex >= 0)
            {
                // 计算键和值的起始和结束位置
                int keyStartIndex = colonIndex;
                int keyEndIndex = colonIndex + eqIndex;
                int valueEndIndex = colonIndex + eqIndex + 1;

                // 跳过等号后面的空格
                while (valueEndIndex < input.Length && char.IsWhiteSpace(input[valueEndIndex]))
                {
                    valueEndIndex++;
                }

                // 提取键和值
                ReadOnlySpan<char> keySpan = input.Slice(keyStartIndex, keyEndIndex - keyStartIndex).Trim();
                ReadOnlySpan<char> valueSpan = input.Slice(valueEndIndex).Trim();

                Infos[keySpan.ToString()] = valueSpan.ToString();
            }
        }
    }
}
