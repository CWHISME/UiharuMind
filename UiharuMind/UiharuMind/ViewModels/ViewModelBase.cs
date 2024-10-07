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

public class ViewModelBase : ObservableRecipient
{
    protected ViewModelBase()
    {
        IsActive = true;
    }

    public virtual void OnEnable()
    {
        IsActive = true;
    }

    public virtual void OnDisable()
    {
        IsActive = false;
    }

    // protected virtual bool AutoActive => true;
}