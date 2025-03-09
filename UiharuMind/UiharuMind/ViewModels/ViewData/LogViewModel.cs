/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 *
 * Latest Update: 2024.10.07
 ****************************************************************************/

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using UiharuMind.Core.Configs;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Utils.Tools;

namespace UiharuMind.ViewModels.ViewData;

public class LogViewModel : ViewModelBase
{
    public ObservableCollection<LogItem> Items { get; } = new();

    // private readonly ValueUiDelayUpdater<LogItem> _delayUpdater;

    public LogViewModel()
    {
        // _delayUpdater = new ValueUiDelayUpdater<LogItem>((_) => { OnPropertyChanged(nameof(Items)); }, 1000);
        InitializeAsync();
    }

    private async void InitializeAsync()
    {
        await Task.Run(() =>
        {
            foreach (var item in LogManager.Instance.LogItems)
            {
                OnLogChange(item);
            }

            return Task.CompletedTask;
        });
        LogManager.Instance.OnLogChange += OnLogChange;
        // await _delayUpdater.UpdateValue(null);
    }

    private void OnLogChange(LogItem obj)
    {
        if (ConfigManager.Instance.DebugSetting.LogTypeInfo <= obj.LogType)
            Items.Add(obj);
        // _delayUpdater.UpdateValue(obj).ConfigureAwait(false);
    }
}