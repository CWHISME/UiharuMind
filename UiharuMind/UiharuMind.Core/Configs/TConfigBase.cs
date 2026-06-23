using UiharuMind.Core.Core;
using UiharuMind.Core.Core.Configs;

namespace UiharuMind.Core.Configs;

public class TConfigBase<T> : ConfigBase where T : class, new()
{
    public static T Current { get; set; } = SaveUtility.LoadOrNew<T>(typeof(T));
}