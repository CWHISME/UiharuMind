using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using UiharuMind.ViewModels.ViewData;

namespace UiharuMind.Views.Windows.Characters;

public partial class CharacterSelectWindow : Window
{
    public enum CharacterType
    {
        Tool,
        Roleplay,
        All
    }

    public static async Task<CharacterInfoViewData?> ShowCharacterSelectWindow(Window parent)
    {
        var win = new CharacterSelectWindow();
        win.CharacterListView.NormalListBox.SelectedItem = null;
        win.CharacterListView.PhotoListBox.SelectedItem = null;
        return await win.ShowDialog<CharacterInfoViewData?>(parent);
    }

    public static async Task<List<CharacterInfoViewData>?> ShowCharacterSelectWindow(Window parent,
        HashSet<string>? alreadySelectedList, CharacterType type = CharacterType.All, params string[] excludeArray)
    {
        var win = new CharacterSelectWindow
        {
            _multiSelect = true
        };
        win._listViewData.IsDisplayAllCharacters = true;
        //排除角色
        List<string> excludeList = excludeArray.ToList();
        if (type != CharacterType.All)
            excludeList.AddRange(win._listViewData.Characters.Where(x =>
                    type == CharacterType.Roleplay && !x.IsRole || type == CharacterType.Tool && x.IsRole)
                .Select(x => x.Name));
        win.ExcludeCharacters(excludeList.ToArray());

        IList? alreadySelected = null;
        win.CharacterListView.NormalListBox.SelectionMode = SelectionMode.Multiple;
        win.CharacterListView.PhotoListBox.SelectionMode = SelectionMode.Multiple;

        if (alreadySelectedList != null)
        {
            alreadySelected = new List<CharacterInfoViewData>();
            foreach (var data in win._listViewData.Characters)
            {
                if (alreadySelectedList.Contains(data.Name)) alreadySelected.Add(data);
            }
        }

        win.CharacterListView.NormalListBox.SelectedItems = alreadySelected;
        win.CharacterListView.PhotoListBox.SelectedItems = alreadySelected;

        // win.CharacterListView.NormalListBox.SelectedItems = alreadySelected;
        return await win.ShowDialog<List<CharacterInfoViewData>?>(parent);
    }

    // private Action<CharacterInfoViewData> _onSelectCharacter;

    private bool _multiSelect = false;
    private CharacterListViewData _listViewData;

    public CharacterSelectWindow()
    {
        InitializeComponent();

        _listViewData = new CharacterListViewData();

        DataContext = _listViewData;
    }

    /// <summary>
    /// 排除角色
    /// </summary>
    /// <param name="excludeList"></param>
    public void ExcludeCharacters(params string[] excludeList)
    {
        List<CharacterInfoViewData> excludeListData = new List<CharacterInfoViewData>();
        foreach (var name in excludeList)
        {
            foreach (var data in _listViewData.Characters)
            {
                if (data.Name == name)
                {
                    excludeListData.Add(data);
                    break;
                }
            }
        }

        foreach (var item in excludeListData)
        {
            _listViewData.Characters.Remove(item);
        }
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        CharacterListView.AddPanel.IsVisible = false;
        SureButton.IsVisible = _multiSelect;
        if (!_multiSelect)
        {
            _listViewData.EventOnSelectedCharacterChanged += Close;
        }
    }

    private void SureButton_Click(object? sender, RoutedEventArgs e)
    {
        if (CharacterListView.NormalListBox.SelectedItems == null)
        {
            App.MessageService.ShowErrorMessageBox("请选择角色", this);
            return;
        }

        List<CharacterInfoViewData> selectedList = new List<CharacterInfoViewData>();
        foreach (var item in CharacterListView.NormalListBox.SelectedItems)
        {
            selectedList.Add((CharacterInfoViewData)item);
        }

        Close(selectedList);
    }
}