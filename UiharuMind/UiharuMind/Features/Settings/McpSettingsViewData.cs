/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.AI.Execution.Mcp;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Shell;

namespace UiharuMind.Features.Settings;

/// <summary>
/// MCP server 的配置面板数据。自成一块（与 <see cref="WebSearchSettingsViewData"/> 同例）：
/// 它有自己的列表、选中项、编辑缓冲与连接状态，混在 <see cref="AgentSettingViewData"/> 里
/// 只会让那个类同时管四件不相干的事。
///
/// 编辑的是<b>连接层</b>配置。哪个智能体能用某个 server，在角色编辑页配（见 ADR 0008）。
/// </summary>
public partial class McpSettingsViewData : ViewModelBase
{
    public ObservableCollection<McpServerConfig> Servers { get; } = new();

    [ObservableProperty] private McpServerConfig? _selectedServer;
    [ObservableProperty] private int _transportIndex;
    [ObservableProperty] private string _statusText = string.Empty;

    /// <summary>连接失败的原因原文；成功或未连接时为空串</summary>
    [ObservableProperty] private string _errorText = string.Empty;

    /// 下面三项是「多行文本 ↔ 结构」的编辑缓冲。逐行编辑是为了让含空格的参数
    /// 与带等号的取值不必再靠分隔符猜边界——那正是旧的空格分隔字符串栽过的地方
    [ObservableProperty] private string _argsText = string.Empty;
    [ObservableProperty] private string _envText = string.Empty;
    [ObservableProperty] private string _headersText = string.Empty;

    /// <summary>一个 server 都没配（界面据此显示空态引导，而不是留一整片空白）</summary>
    [ObservableProperty] private bool _isEmpty = true;

    /// <summary>选中的是 stdio（命令与参数那几行据此显隐）</summary>
    public bool IsStdio => TransportIndex == 0;

    /// <summary>选中的是 HTTP（地址与请求头据此显隐）</summary>
    public bool IsHttp => TransportIndex != 0;

    /// <summary>标准配置文件路径（可直接编辑或整段替换）</summary>
    public string ConfigFilePath => McpManager.ConfigFilePath;

    public McpSettingsViewData()
    {
        RefreshServers();
    }

    partial void OnSelectedServerChanged(McpServerConfig? value)
    {
        TransportIndex = value == null ? 0 : (int)value.TransportType;
        ArgsText = value == null ? string.Empty : string.Join('\n', value.Args);
        EnvText = FormatPairs(value?.EnvironmentVariables);
        HeadersText = FormatPairs(value?.Headers);
        RefreshStatus();
    }

    partial void OnTransportIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsStdio));
        OnPropertyChanged(nameof(IsHttp));
    }

    [RelayCommand]
    private void NewServer()
    {
        McpServerConfig server = new()
        {
            Name = $"server-{DateTime.Now:HHmmss}",
            IsEnabled = false,
        };
        McpManager.Instance.SaveServer(server);
        RefreshServers(server.Name);
    }

    [RelayCommand]
    private void SaveServer()
    {
        if (!TryCommitEdits()) return;
        RefreshServers(SelectedServer!.Name);
        RefreshStatus();
    }

    [RelayCommand]
    private void DeleteServer()
    {
        if (SelectedServer == null) return;
        McpManager.Instance.DeleteServer(SelectedServer.Name);
        RefreshServers();
    }

    /// <summary>
    /// 测试选中的这一个。<b>先存后测</b>，且不看它托管与否——
    /// 用户填完就点测试是最自然的顺序，而新建的 server 默认不托管，
    /// 若跟着"刷新全部已托管"走，这里会一声不响什么都不做。
    /// </summary>
    [RelayCommand]
    private async Task TestServer()
    {
        if (!TryCommitEdits()) return;

        StatusText = LocalizationManager.Instance.GetString("AgentMcpStateConnecting");
        ErrorText = string.Empty;
        await McpManager.Instance.TestServerAsync(SelectedServer!.Name);
        RefreshStatus();
    }

    /// <summary>用户直接改了配置文件之后，从磁盘重新读入</summary>
    [RelayCommand]
    private void ReloadServers()
    {
        McpManager.Instance.Reload();
        RefreshServers(SelectedServer?.Name);
    }

    /// <summary>打开配置文件所在目录（整段替换成别处那份配置时用）</summary>
    [RelayCommand]
    private void OpenConfigFolder()
    {
        App.FilesService.OpenFolder(Path.GetDirectoryName(ConfigFilePath) ?? string.Empty);
    }

    /// 把三个编辑缓冲写回选中的配置并落盘。无选中项时返回 false
    private bool TryCommitEdits()
    {
        if (SelectedServer == null) return false;

        SelectedServer.TransportType = (EMcpTransportType)Math.Clamp(TransportIndex, 0, 1);
        SelectedServer.Args = ParseLines(ArgsText);
        SelectedServer.EnvironmentVariables = ParsePairs(EnvText);
        SelectedServer.Headers = ParsePairs(HeadersText);
        McpManager.Instance.SaveServer(SelectedServer);
        return true;
    }

    private void RefreshServers(string? keepSelectedName = null)
    {
        Servers.Clear();
        foreach (McpServerConfig server in McpManager.Instance.GetServers())
        {
            Servers.Add(server);
        }

        IsEmpty = Servers.Count == 0;
        SelectedServer = Servers.FirstOrDefault(x => x.Name == keepSelectedName) ?? Servers.FirstOrDefault();
    }

    private void RefreshStatus()
    {
        if (SelectedServer == null)
        {
            StatusText = string.Empty;
            ErrorText = string.Empty;
            return;
        }

        McpServerStatus status = McpManager.Instance.GetServerStatus(SelectedServer.Name);
        string stateText = LocalizationManager.Instance.GetString($"AgentMcpState{status.State}");
        StatusText = status.ToolCount > 0 ? $"{stateText} · {status.ToolCount} tools" : stateText;
        // 失败原因原样摆出来:"为什么没工具"必须能当场看见,而不是只躺在日志里
        ErrorText = status.Error ?? string.Empty;
    }

    /// 逐行取值,空行与首尾空白丢弃
    private static List<string> ParseLines(string text)
    {
        return text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .ToList();
    }

    /// 每行一条 key=value。只在<b>第一个</b>等号处切分——取值里带等号(如 base64 凭据)很常见
    private static Dictionary<string, string> ParsePairs(string text)
    {
        Dictionary<string, string> pairs = new();
        foreach (string line in ParseLines(text))
        {
            int index = line.IndexOf('=');
            if (index <= 0) continue;
            pairs[line[..index].Trim()] = line[(index + 1)..].Trim();
        }

        return pairs;
    }

    private static string FormatPairs(Dictionary<string, string>? pairs)
    {
        return pairs == null ? string.Empty : string.Join('\n', pairs.Select(x => $"{x.Key}={x.Value}"));
    }
}
