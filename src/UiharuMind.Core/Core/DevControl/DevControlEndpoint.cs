using System.IO;

namespace UiharuMind.Core.Core.DevControl;

/// <summary>
/// 开发控制通道落在档案目录的哪里（ADR 0059）：<c>run/</c> 下一个 Unix socket 与一份令牌。
/// 应用与 CLI 按同一个档案根算出同一处，一个档案只有一个通道
/// </summary>
public sealed class DevControlEndpoint
{
    /// <param name="root">档案根目录</param>
    public DevControlEndpoint(string root)
    {
        RunDirectory = Path.Combine(root, "run");
        SocketPath = Path.Combine(RunDirectory, "control.sock");
        TokenPath = Path.Combine(RunDirectory, "control.token");
    }

    /// <summary>当前档案（<see cref="AppPaths.Root"/>）的通道</summary>
    public static DevControlEndpoint Default => new(AppPaths.Root);

    /// <summary>socket 与令牌所在目录（仅本用户可进）</summary>
    public string RunDirectory { get; }

    /// <summary>Unix socket 路径</summary>
    public string SocketPath { get; }

    /// <summary>本次启动的令牌</summary>
    public string TokenPath { get; }
}
