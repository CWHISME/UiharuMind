using UiharuMind.Core.Core.Configs;

namespace UiharuMind.Core.Core.ServerKernal;

/// <summary>
/// 代表提供服务的一个基类，并不是指服务器
/// </summary>
public abstract class ServerKernalBase<T, TC> where TC : ConfigBase, new()
{
    public TC Config { get; } = SaveUtility.Load<TC>(typeof(TC));

    public void SaveConfig()
    {
        Config.Save();
    }
}