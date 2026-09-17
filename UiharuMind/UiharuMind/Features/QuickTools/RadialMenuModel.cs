using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Shell;
using UiharuMind.Shared.Windows;
using UiharuMind.Features.Conversation;
using UiharuMind.Features.Conversation.QuickChat;

namespace UiharuMind.Features.QuickTools
{
    public class RadialMenuModel : ViewModelBase
    {
        private readonly ObservableCollection<MenuItemModel> _menuItems;

        public RadialMenuModel()
        {
            _menuItems = new ObservableCollection<MenuItemModel>();
            LocalizationManager.Instance.LanguageChanged += RefreshLocalizedTexts;
            InitializeMenuItems();
        }

        public ObservableCollection<MenuItemModel> MenuItems => _menuItems;

        private void InitializeMenuItems()
        {
            _menuItems.Clear();
            AddMenuItem("search", "RadialMenuFileSearch", () => UIManager.ShowWindow<FileSearchWindow>());
            AddMenuItem("message-circle-more", "Ask", () => QuickStartChatWindow.Show());
            // AddMenuItem("house", "RadialMenuHome", () => App.DummyWindow.LaunchMainWindow());
            // 无可见窗时恢复缓存的隐藏窗（不清内容），有可见窗或无实例时才开空文档
            AddMenuItem("file-text", "RadialMenuTextEditor", TextFileWindow.ShowLastOrEmpty);
        }

        private void AddMenuItem(string icon, string textKey, Action action)
        {
            var item = new MenuItemModel { Icon = icon, TextKey = textKey, Action = action };
            item.RefreshText();
            _menuItems.Add(item);
        }

        /// <summary>语言切换后按当前语言重取轮盘文案</summary>
        private void RefreshLocalizedTexts()
        {
            foreach (var item in _menuItems) item.RefreshText();
        }

        public void ExecuteAction(MenuItemModel menuItem)
        {
            menuItem?.Action?.Invoke();
        }
    }

    public class MenuItemModel : ViewModelBase
    {
        // Icon/TextKey/Action 全部在对象初始化器里赋值（见 InitializeMenuItems），非空但默认置 null
        public string Icon { get; set; } = null!;
        public string TextKey { get; set; } = null!;
        public Action Action { get; set; } = null!;

        private string _text = null!;

        /// <summary>轮盘扇区文案；语言切换时由 <see cref="RefreshText"/> 按当前语言重取</summary>
        public string Text
        {
            get => _text;
            private set => SetProperty(ref _text, value);
        }

        public ICommand ActionCommand => new RelayCommand(() => Action?.Invoke());

        /// <summary>按当前语言重取文案（构造与语言切换时调用）</summary>
        public void RefreshText() => Text = Loc.Text(TextKey);
    }
}