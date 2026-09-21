using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.AI.Character;

namespace UiharuMind.Features.Characters;

/// <summary>
/// 提示词片段选择器的数据。点一行即把该片段插到提示词开头；
/// 底部可把当前提示词<b>存为片段</b>——写在提示词框里的东西直接攒成自己的常用开头，
/// 不必另开一个片段编辑窗。
/// </summary>
public partial class PromptSnippetPickerViewData : ObservableObject
{
    private readonly Func<string> _currentPrompt;
    private readonly Action<string> _onInsert;

    /// <summary>当前片段库</summary>
    public ObservableCollection<PromptSnippetItem> Items { get; } = new();

    /// <summary>片段库为空(界面据此显示空态文案)</summary>
    public bool IsEmpty => Items.Count == 0;

    /// <summary>「存为片段」用的名字；与已有片段同名即覆盖那一条</summary>
    [ObservableProperty] private string _newSnippetName = string.Empty;

    /// <summary>
    /// 构造选择器
    /// </summary>
    /// <param name="currentPrompt">取当前提示词正文(存为片段时用)</param>
    /// <param name="onInsert">插入回调，参数是片段正文</param>
    public PromptSnippetPickerViewData(Func<string> currentPrompt, Action<string> onInsert)
    {
        _currentPrompt = currentPrompt;
        _onInsert = onInsert;
        Refresh();
    }

    /// <summary>重建列表。每次展开都该调一次：片段可能刚被别处增删</summary>
    public void Refresh()
    {
        Items.Clear();
        foreach (PromptSnippet snippet in PromptSnippetManager.Instance.Snippets)
        {
            Items.Add(new PromptSnippetItem(snippet,
                new RelayCommand(() => _onInsert(snippet.Text)),
                new RelayCommand(() => Remove(snippet))));
        }

        OnPropertyChanged(nameof(IsEmpty));
    }

    /// <summary>把当前提示词存为片段</summary>
    [RelayCommand]
    private void SaveCurrentAsSnippet()
    {
        PromptSnippetManager.Instance.AddOrUpdate(NewSnippetName, _currentPrompt());
        NewSnippetName = string.Empty;
        Refresh();
    }

    private void Remove(PromptSnippet snippet)
    {
        PromptSnippetManager.Instance.Remove(snippet);
        Refresh();
    }
}

/// <summary>片段列表里的一行</summary>
/// <param name="Snippet">片段本体</param>
/// <param name="InsertCommand">插到提示词开头</param>
/// <param name="DeleteCommand">从片段库删除</param>
public sealed record PromptSnippetItem(
    PromptSnippet Snippet, IRelayCommand InsertCommand, IRelayCommand DeleteCommand)
{
    /// <summary>片段名</summary>
    public string Name => Snippet.Name;

    /// <summary>正文预览(悬停提示用)</summary>
    public string Preview => Snippet.Text.Length <= 300 ? Snippet.Text : Snippet.Text[..300] + "…";
}
