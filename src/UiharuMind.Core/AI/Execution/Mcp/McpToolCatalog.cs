/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Execution.Mcp;

/// <summary>
/// 一个工具的定义（名字、描述、参数 schema），脱离连接之后用于展示与离线查阅。
/// </summary>
public sealed class McpToolDescriptor
{
    /// <summary>工具名（server 原名）</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>工具描述</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>参数 schema（JSON Schema 原文）</summary>
    public JsonElement InputSchema { get; set; }

    /// <summary>
    /// 从活的工具取一份定义。
    /// </summary>
    /// <param name="function">已连接 server 的工具</param>
    /// <returns>定义快照</returns>
    public static McpToolDescriptor From(AIFunction function) => new()
    {
        Name = function.Name,
        Description = function.Description,
        InputSchema = function.JsonSchema.Clone(),
    };
}

/// <summary>
/// 一个 server 的工具清单快照：上次连上时取回的定义与自述。
/// 按需模式的 <c>McpHelp</c> 在 server 离线时读它，让模型至少看得见"有什么"，
/// 再决定要不要等、要不要换路。
/// </summary>
public sealed class McpToolCatalog
{
    /// <summary>取回时那条配置的可执行面指纹；与当前配置对不上即视为过期，不使用</summary>
    public string Fingerprint { get; set; } = string.Empty;

    /// <summary>server 自述</summary>
    public string Instructions { get; set; } = string.Empty;

    /// <summary>工具定义</summary>
    public List<McpToolDescriptor> Tools { get; set; } = new();

    /// <summary>取回时刻（UTC）</summary>
    public DateTime CapturedUtc { get; set; }
}

/// <summary>
/// <see cref="McpToolCatalog"/> 的磁盘缓存。落 <see cref="AppPaths.Cache.McpToolCatalogs"/>：
/// 可再生（重连即重写）、用户可随手删，读不到只是少一份离线线索，从不致命。
///
/// 目录可注入，便于单测；读写失败一律吞掉并记日志——缓存丢了不该让任何一次调用失败。
/// </summary>
internal sealed class McpToolCatalogStore
{
    private readonly string _directory;

    /// <param name="directory">缓存目录；缺省用应用的缓存位置</param>
    public McpToolCatalogStore(string? directory = null)
    {
        _directory = directory ?? AppPaths.Cache.McpToolCatalogs;
    }

    /// <summary>
    /// 写一份清单。
    /// </summary>
    /// <param name="key">server 的索引键</param>
    /// <param name="catalog">清单</param>
    public void Save(McpServerKey key, McpToolCatalog catalog)
    {
        try
        {
            Directory.CreateDirectory(_directory);
            SaveUtility.Save(PathFor(key), catalog);
        }
        catch (Exception e)
        {
            Log.Warning($"MCP tool catalog for '{key}' not cached: {e.Message}");
        }
    }

    /// <summary>
    /// 读一份清单。
    /// </summary>
    /// <param name="key">server 的索引键</param>
    /// <param name="fingerprint">当前配置的指纹；对不上说明命令改过，缓存属于另一条 server，不用</param>
    /// <returns>清单；没有、读坏或过期为 null</returns>
    public McpToolCatalog? Load(McpServerKey key, string fingerprint)
    {
        try
        {
            string path = PathFor(key);
            if (!File.Exists(path)) return null;
            McpToolCatalog? catalog = SaveUtility.Load<McpToolCatalog>(path);
            return catalog != null && string.Equals(catalog.Fingerprint, fingerprint, StringComparison.Ordinal)
                ? catalog
                : null;
        }
        catch (Exception e)
        {
            Log.Warning($"MCP tool catalog for '{key}' unreadable: {e.Message}");
            return null;
        }
    }

    /// 可读前缀 + 索引键哈希：名字含非法字符也安全，同名不同工作区不会撞
    internal string PathFor(McpServerKey key)
    {
        string prefix = new string(key.Name.Select(c => char.IsLetterOrDigit(c) ? c : '_').Take(40).ToArray())
            .Trim('_');
        if (prefix.Length == 0) prefix = "server";
        string hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key.ToString())))[..8];
        return Path.Combine(_directory, $"{prefix}_{hash}.json");
    }
}
