using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.ViewModels;
using UiharuMind.Views;
using UiharuMind.Views.Windows;

namespace UiharuMind.Services
{
    public class RadialMenuModel : ViewModelBase
    {
        private readonly ObservableCollection<MenuItemModel> _menuItems;

        public RadialMenuModel()
        {
            _menuItems = new ObservableCollection<MenuItemModel>();
            InitializeMenuItems();
        }

        public ObservableCollection<MenuItemModel> MenuItems => _menuItems;

        private void InitializeMenuItems()
        {
            _menuItems.Add(new MenuItemModel
            {
                Icon = "search",
                Text = "文件搜索",
                Action = () => UIManager.ShowWindow<FileSearchWindow>()
            });

            _menuItems.Add(new MenuItemModel
            {
                Icon = "book-search",
                Text = "笔记",
            });
        }

        public void ExecuteAction(MenuItemModel menuItem)
        {
            menuItem?.Action?.Invoke();
        }
    }

    public class MenuItemModel
    {
        public string Icon { get; set; }
        public string Text { get; set; }
        public Action Action { get; set; }

        public ICommand ActionCommand => new RelayCommand(() => Action?.Invoke());
    }
}
