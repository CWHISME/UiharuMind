using Avalonia.Layout;
using Avalonia.Threading;
using SharpHook.Data;
using UiharuMind.Controls;
using UiharuMind.Core.Input;
using UiharuMind.Services;
using UiharuMind.Utils;
using UiharuMind.Views.Common;

namespace UiharuMind.Views.Windows
{
    public partial class RadialMenuWindow : QuickWindowBase
    {
        public override bool IsCacheWindow => true;
        public override bool ContributesToMacRegularMode => false;

        public RadialMenuWindow()
        {
            InitializeComponent();
            DataContext = App.ViewModel.GetViewModel<RadialMenuModel>();
        }

        protected override void OnPreShow()
        {
            base.OnPreShow();
            InputManager.Instance.EventOnKeyUp += OnGlobalKeyUp;
        }

        protected override void OnPostShow()
        {
            base.OnPostShow();
            this.SetWindowToMousePosition(HorizontalAlignment.Center, VerticalAlignment.Center);
        }

        protected override void OnPreClose()
        {
            base.OnPreClose();
            InputManager.Instance.EventOnKeyUp -= OnGlobalKeyUp;
        }

        private void OnGlobalKeyUp(KeyCode keyCode)
        {
            Dispatcher.UIThread.Post(() =>
            {
                var hoveredItem = RadialMenuControl.GetHoveredItem();
                if (hoveredItem != null)
                {
                    hoveredItem.Execute();
                }

                SafeClose();
            });
        }
    }
}