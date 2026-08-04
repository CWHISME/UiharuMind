using Avalonia.Layout;
using Avalonia.Threading;
using SharpHook.Data;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Utils;
using UiharuMind.Shared.Windows;
using UiharuMind.Core.Input;

namespace UiharuMind.Features.QuickTools
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
            InputManager.Instance.EventOnMouseClicked += OnGlobalMouseClick;
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
            InputManager.Instance.EventOnMouseClicked -= OnGlobalMouseClick;
        }

        private void OnGlobalKeyUp(KeyCode keyCode)
        {
            DoCheck();
        }

        private void OnGlobalMouseClick(MouseEventData obj)
        {
            DoCheck();
        }

        private void DoCheck()
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