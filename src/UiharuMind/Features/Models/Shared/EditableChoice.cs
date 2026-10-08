using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace UiharuMind.Features.Models.Shared;

/// <summary>
/// 可编辑下拉框的数据：有候选可挑，也能手输。下拉框的 <c>Text</c> 与 <c>SelectedItem</c> 是两条双向绑定，
/// 两边一旦不一致，控件回写就会把手输的文本冲掉——这里始终让选中项跟着文本走
/// </summary>
public partial class EditableChoice : ObservableObject
{
    private bool _syncing; //文本与选中项互相对齐期间

    [ObservableProperty] private string _text = string.Empty;
    [ObservableProperty] private string? _selectedOption;

    /// <summary>
    /// 候选。集合实例不换，换候选时就地重建
    /// </summary>
    public ObservableCollection<string> Options { get; } = [];

    /// <summary>
    /// 有候选（没有时界面退回普通输入框）
    /// </summary>
    public bool HasOptions => Options.Count > 0;

    /// <summary>
    /// 文本变了（含选候选带来的）
    /// </summary>
    public event Action<string>? Changed;

    /// <summary>
    /// 换一组候选，保留当前文本。Clear 会让可编辑下拉框把 Text 置空、经双向绑定回写过来（文本模型窗口踩过）：先留底，重建完补回
    /// </summary>
    /// <param name="options">候选</param>
    public void SetOptions(IEnumerable<string> options)
    {
        string keep = Text;
        _syncing = true;
        Options.Clear();
        foreach (string option in options) Options.Add(option);
        Text = keep;
        SelectedOption = Options.FirstOrDefault(x => x == keep);
        _syncing = false;
        OnPropertyChanged(nameof(HasOptions));
    }

    partial void OnSelectedOptionChanged(string? value)
    {
        if (value == null || _syncing) return;
        Text = value;
    }

    partial void OnTextChanged(string value)
    {
        if (_syncing) return;
        _syncing = true;
        SelectedOption = Options.FirstOrDefault(x => x == value);
        _syncing = false;
        Changed?.Invoke(value);
    }
}
