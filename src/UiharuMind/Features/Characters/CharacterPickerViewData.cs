/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.AI.Character;
using UiharuMind.Shared.Utils;

namespace UiharuMind.Features.Characters;

/// <summary>
/// 角色选择器的数据。<b>单选、选完即回调</b>，供内联 Flyout 使用——
/// 原先各处都开 <see cref="CharacterSelectWindow"/> 那个整页尺寸的模态窗，
/// 而"挑一个角色"这种高频动作不该每次开关一个窗口。
///
/// 候选集由调用方给谓词决定（agent 页只要 agent 档、挂载列表只要提示词型角色），
/// 排除集用来去掉已选中的与自己。
/// </summary>
public partial class CharacterPickerViewData : ObservableObject
{
    private readonly Func<CharacterData, bool>? _filter;
    private readonly Action<CharacterData> _onPicked;
    private readonly HashSet<string> _excludedIds;

    [ObservableProperty] private string _searchText = string.Empty;

    /// <summary>当前可选的角色（已过滤、已排除、已按搜索词筛过）</summary>
    public ObservableCollection<CharacterPickerItem> Items { get; } = new();

    /// <summary>候选为空（界面据此显示空态文案）</summary>
    public bool IsEmpty => Items.Count == 0;

    /// <summary>
    /// 构造选择器
    /// </summary>
    /// <param name="onPicked">选中回调（点击即触发，选择器自身不持有"当前选中项"）</param>
    /// <param name="filter">候选谓词；为空表示不限</param>
    /// <param name="excludedIds">要排除的角色标识（已挂载的、自己）</param>
    public CharacterPickerViewData(Action<CharacterData> onPicked,
        Func<CharacterData, bool>? filter = null, IEnumerable<string>? excludedIds = null)
    {
        _onPicked = onPicked;
        _filter = filter;
        _excludedIds = new HashSet<string>(excludedIds ?? [], StringComparer.Ordinal);
        Refresh();
    }

    /// <summary>
    /// 重建候选列表。每次展开都该调一次：角色可能刚被新建/删除/改名，排除集也可能变了。
    /// </summary>
    public void Refresh()
    {
        Items.Clear();
        IEnumerable<CharacterData> candidates = CharacterManager.Instance.CharacterDataDictionary.Values
            .Where(x => !x.IsInternal)
            .Where(x => !_excludedIds.Contains(x.CharacterId))
            .Where(x => _filter == null || _filter(x));

        string keyword = SearchText.Trim();
        if (keyword.Length > 0)
        {
            candidates = candidates.Where(x =>
                x.CharacterName.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                x.Description.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }

        foreach (CharacterData character in candidates.OrderBy(x => x.CharacterName, StringComparer.CurrentCulture))
        {
            Items.Add(new CharacterPickerItem(character, new RelayCommand(() => _onPicked(character))));
        }

        OnPropertyChanged(nameof(IsEmpty));
    }

    /// <summary>把某个角色加入排除集（挂载场景：刚选中的下次不该再出现）</summary>
    /// <param name="characterId">角色标识</param>
    public void Exclude(string characterId)
    {
        if (_excludedIds.Add(characterId)) Refresh();
    }

    partial void OnSearchTextChanged(string value)
    {
        Refresh();
    }
}

/// <summary>选择器里的一行</summary>
/// <param name="Character">角色本体</param>
/// <param name="PickCommand">点击即选中</param>
public sealed record CharacterPickerItem(CharacterData Character, IRelayCommand PickCommand)
{
    /// <summary>显示名；为空时回落为标识</summary>
    public string Name => string.IsNullOrEmpty(Character.CharacterName)
        ? Character.CharacterId
        : Character.CharacterName;

    /// <summary>副行描述</summary>
    public string Description => Character.Description;

    /// <summary>头像</summary>
    public Bitmap? Icon => IconUtils.GetCharacterBitmapOrDefault(Character);
}
