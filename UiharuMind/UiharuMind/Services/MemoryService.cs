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
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.AI;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.AI.Memery;
using UiharuMind.Core.LLamaCpp.Data;

namespace UiharuMind.Services;

/// <summary>
/// 记忆管理，表现层必须从这里调用
/// </summary>
public partial class MemoryService : ObservableObject
{
    public ObservableCollection<MemoryData> MemorySources { get; set; } =
        new ObservableCollection<MemoryData>();

    public MemoryService()
    {
        foreach (var item in MemoryManager.Instance.GetOrderedItems())
        {
            MemorySources.Add(item);
        }

        MemoryManager.Instance.OnItemAdded += OnItemAdded;
        MemoryManager.Instance.OnItemRemoved += OnItemRemoved;
    }

    private void OnItemAdded(MemoryData obj)
    {
        MemorySources.Insert(0, obj);
    }

    private void OnItemRemoved(MemoryData obj)
    {
        MemorySources.Remove(obj);
    }
}