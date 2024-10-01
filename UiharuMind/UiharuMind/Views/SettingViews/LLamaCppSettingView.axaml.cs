using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.ViewModels.SettingViewData;

namespace UiharuMind.Views.SettingViews;

public partial class LLamaCppSettingView : UserControl
{
    public LLamaCppSettingView()
    {
        InitializeComponent();

        DataContext = App.ViewModel.GetViewModel<LLamaCppSettingModel>();

        this.GetObservable(IsVisibleProperty).Subscribe(new VisibilityObserver(this));
    }

    private class VisibilityObserver : IObserver<bool>
    {
        private readonly UserControl _control;

        public VisibilityObserver(UserControl control)
        {
            _control = control;
        }

        public void OnNext(bool value)
        {
            if (value)
            {
                // 当 UserControl 变为可见时执行的代码
                Log.Debug("UserControl is now visible.");
            }
            else
            {
                // 当 UserControl 变为不可见时执行的代码
                Log.Debug("UserControl is no longer visible.");
            }
        }

        public void OnError(Exception error)
        {
            Log.Error($"An error occurred: {error.Message}");
        }

        public void OnCompleted()
        {
            Log.Debug("Observation completed.");
        }
    }
}