using System.Collections.Generic;
using System.Threading.Tasks;

namespace UiharuMind.Shared.Services.Permissions;

/// <summary>
/// 无需额外授权的平台（Windows）：清单为空，钩子直接放行。
/// </summary>
public sealed class GrantedPermissionProvider : IPlatformPermissionProvider
{
    public IReadOnlyList<PermissionItem> Items { get; } = new List<PermissionItem>();

    public Task RefreshAsync() => Task.CompletedTask;

    public bool IsInputHookAllowed => true;
}
