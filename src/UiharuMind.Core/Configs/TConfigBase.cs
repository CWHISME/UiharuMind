using UiharuMind.Core.Core;
using UiharuMind.Core.Core.Configs;

namespace UiharuMind.Core.Configs;

/// <summary>
/// 配置基类。落盘位置由类名决定(见 <see cref="AppPaths.Config.ForType"/>),
/// 派生类不需要、也不应该自己声明路径。
/// </summary>
public class TConfigBase<T> : ConfigBase where T : class, new()
{
    public static T Current { get; set; } =
        SaveUtility.Load<T>(AppPaths.Config.ForType(typeof(T).Name)) ?? new T();
}