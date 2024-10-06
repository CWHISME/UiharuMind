using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace UiharuMind.Core.Core.Configs;

public class ConfigBase : INotifyPropertyChanged
{
    public void Save()
    {
        SaveUtility.Save(this.GetType().Name, this);
    }

    // public void Load()
    // {
    //     SaveUtility.Load<T>(this.GetType().Name);
    // }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}