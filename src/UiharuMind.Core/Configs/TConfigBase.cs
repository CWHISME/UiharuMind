using UiharuMind.Core.Core;
using UiharuMind.Core.Core.Configs;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.Configs;

/// <summary>
/// 配置基类。落盘位置由类名决定(见 <see cref="AppPaths.Config.ForType"/>),
/// 派生类不需要、也不应该自己声明路径。
/// </summary>
public class TConfigBase<T> : ConfigBase where T : ConfigBase, new()
{
    public static T Current { get; set; } = LoadCurrent();

    private static T LoadCurrent()
    {
        T config = SaveUtility.Load<T>(AppPaths.Config.ForType(typeof(T).Name), SaveUtility.SaveJsonOptions) ?? new T();
        // 旧档迁移在这里：此刻抑制已解除、字段全到位，改完只留在内存里，
        // 等用户下一次改设置随那一趟落盘顺带写掉，不在启动路径上多一次写盘
        try
        {
            config.OnLoaded();
        }
        catch (Exception e)
        {
            // 吞掉而不是放出去：这里在类型初始化的锁内，抛一次就是 TypeInitializationException,
            // 之后每次访问 Current 都炸——为一个迁移 bug 废掉整份配置不划算。
            // 迁移失败的最坏结果是配置停在旧形状（下次启动再试），而不是用不了
            Log.Error($"Config post-load hook failed: {typeof(T).Name}: {e}");
        }

        return config;
    }
}
