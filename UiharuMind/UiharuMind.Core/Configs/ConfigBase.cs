namespace UiharuMind.Core.Core.Configs;

public class ConfigBase
{
    public void Save()
    {
        SaveUtility.Save(this.GetType().Name, this);
    }

    // public void Load()
    // {
    //     SaveUtility.Load<T>(this.GetType().Name);
    // }
}