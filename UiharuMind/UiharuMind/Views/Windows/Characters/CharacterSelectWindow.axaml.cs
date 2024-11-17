using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using UiharuMind.ViewModels.ViewData;

namespace UiharuMind.Views.Windows.Characters;

public partial class CharacterSelectWindow : Window
{
    public static async Task<CharacterInfoViewData?> Show(Window parent)
    {
        var win = new CharacterSelectWindow();
        return await win.ShowDialog<CharacterInfoViewData?>(parent);
    }

    public static async Task<List<CharacterInfoViewData>?> Show(Window parent,
        HashSet<string>? alreadySelectedList)
    {
        var win = new CharacterSelectWindow
        {
            _multiSelect = true
        };
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

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        CharacterListView.AddButton.IsVisible = false;
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