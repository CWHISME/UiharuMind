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

using CommunityToolkit.Mvvm.ComponentModel;

namespace UiharuMind.ViewModels;

/// <summary>
/// model 始终会存在
/// data 则会根据需要重新创建
/// </summary>
public class ViewModelBase : ObservableObject
{
    protected ViewModelBase()
    {
        // IsActive = true;
    }

    public virtual void OnEnable()
    {
        // IsActive = true;
    }

    public virtual void OnDisable()
    {
        // IsActive = false;
    }

    // protected virtual bool AutoActive => true;
}