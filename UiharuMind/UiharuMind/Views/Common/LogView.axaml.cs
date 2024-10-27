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

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.ViewModels.Converters;
using UiharuMind.ViewModels.UIHolder;
using UiharuMind.ViewModels.ViewData;

namespace UiharuMind.Views.Common;

public partial class LogView : UserControl
{
    private readonly ScrollViewerAutoScrollHolder _scrollHolder;

    private bool _isDragging;
    private Border? _selectedControl;
    private LogLevelToColorConverter _logLevelToColorConverter = new LogLevelToColorConverter();

    public LogView()
    {
        InitializeComponent();

        DataContext = App.ViewModel.GetViewModel<LogViewModel>();
        _scrollHolder = new ScrollViewerAutoScrollHolder(Viewer);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        _isDragging = true;
        CheckSelectedLogItem(e);
        // Log.Warning(
        //     "sdfnisnfsin\nshdisudfissfnsifnsinfnshdisudfissfnsifnsinfsjfnskfdnshdisudfissfnsifnsinfsjfnskfdnshdisudfissfnsifnsinfsjfnskfdnshdisudfissfnsifnsinfsjfnskfdnshdisudfissfnsifnsinfsjfnskfdnshdisudfissfnsifnsinfsjfnskfdnshdisudfissfnsifnsinfsjfnskfdnshdisudfissfnsifnsinfsjfnskfdnshdisudfissfnsifnsinfsjfnskfdnshdisudfissfnsifnsinfsjfnskfdnshdisudfissfnsifnsinfsjfnskfdnshdisudfissfnsifnsinfsjfnskfdnshdisudfissfnsifnsinfsjfnskfdnshdisudfissfnsifnsinfsjfnskfdsjfnskfd\ndsfsefsfnsijdndsj?你个 三十四分十分士大夫");
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        // Log.Debug("Jmove1111" + _isDragging);
        CheckSelectedLogItem(e);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _isDragging = false;
    }

    private void CheckSelectedLogItem(PointerEventArgs e)
    {
        if (_isDragging)
        {
            var point = e.GetPosition(LogList);
            var hitTestResult = LogList.InputHitTest(point);
            // Log.Debug("Jmove2222" + _isDragging + "  " + hitTestResult);
            // if (hitTestResult is ContentPresenter contentPresenter && contentPresenter.Parent is ItemsControl item)
            if (hitTestResult is Border control && control != _selectedControl &&
                control.DataContext is LogItem logItem)
            {
                //Log.Debug("Jmove3333"+_isDragging+"  "+item.Content);
                //LogList.SelectedItem = item.Content;
                if (_selectedControl != null) _selectedControl.Background = new SolidColorBrush(Colors.Transparent);
                _selectedControl = control;
                // _selectedControl.BorderThickness = new Thickness();
                _selectedControl.Background = new SolidColorBrush(new Color(
                    (byte)(Application.Current?.ActualThemeVariant == ThemeVariant.Dark ? 118 : 40), 118, 118, 118));

                DetailText.Foreground =
                    (IBrush)_logLevelToColorConverter.Convert(logItem.LogType, typeof(LogItem), null,
                        CultureInfo.CurrentCulture);
                DetailText.Text = logItem.LogString;
            }
        }
    }
    // protected override void OnPointerPressed(PointerPressedEventArgs e)
    // {
    //     base.OnPointerPressed(e);
    //     _isDragging = true;
    // }

    // private void OnListBoxItemPointerPressed(object? sender, PointerPressedEventArgs e)
    // {
    //     _isDragging = true;
    //     Log.Debug("1111"+_isDragging);
    //
    // }
    //
    // private void OnListBoxItemPointerMoved(object? sender, PointerEventArgs e)
    // {
    //     Log.Debug("Jmove2222"+_isDragging);
    //     if (_isDragging && sender is Control control)
    //     {
    //         Log.Debug("Jmove4444"+_isDragging+"  "+ (control.Parent as ListBoxItem).DataContext);
    //         if (control.Parent is ListBoxItem listBoxItem&&LogList.SelectedItem != listBoxItem.DataContext)
    //         {
    //             LogList.SelectedItem = listBoxItem.DataContext;
    //         }
    //     }
    // }

    // protected override void OnPointerMoved(PointerEventArgs e)
    // {
    //     base.OnPointerMoved(e);
    //     Log.Debug("Jmove1111"+_isDragging);
    // }

    // protected override void OnPointerReleased(PointerReleasedEventArgs e)
    // {
    //     base.OnPointerReleased(e);
    //     _isDragging = false;
    //     Log.Debug("Jmove3333"+_isDragging);
    // }

    private void ListBox_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _isDragging = true;
        // Log.Debug("``````" + _isDragging);
    }
}