using System.Collections.Generic;
using System.Threading.Tasks;

namespace UiharuMind.Shared.Services.Permissions;

/// <summary>
/// 各平台的权限清单与探测方式。
/// 抽掉这层是为了让权限引导界面只管渲染列表，不再在界面代码里堆平台分支。
/// </summary>
public interface IPlatformPermissionProvider
{
    /// <summary>本平台需要向用户交代的权限项，顺序即显示顺序</summary>
    IReadOnlyList<PermissionItem> Items { get; }

    /// <summary>
    /// 重新探测所有权限项的状态
    /// </summary>
    Task RefreshAsync();

    /// <summary>
    /// 当前是否允许启动全局输入钩子。
    /// macOS 未授予辅助功能时启动会被系统静默拦下，必须先拦一道。
    /// </summary>
    bool IsInputHookAllowed { get; }
}
