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

        // 本趟轮盘只执行一次：Alt+Shift+主键逐个松开会各触发一次 EventOnKeyUp，加上鼠标点击，
        // 一次选择可能连发多个 DoCheck，不拦住就会把同一项执行多遍（TextFileWindow 多开就是三扇窗）
        private bool _executedThisShow;

        public RadialMenuWindow()
        {
            InitializeComponent();
            DataContext = App.ViewModel.GetViewModel<RadialMenuModel>();
        }

        protected override void OnPreShow()
        {
            base.OnPreShow();
            _executedThisShow = false;
            // 隐藏不触发 PointerExited，旧悬停态会残留到下一趟；复用路径上 DoCheck 可能抢在
            // 窗口真正显示前执行上一趟悬停的项，重开前先清空
            RadialMenuControl.ResetHoverStates();
            InputManager.Instance.EventOnKeyUp += OnGlobalKeyUp;
            InputManager.Instance.EventOnMouseClicked += OnGlobalMouseClick;
        }

        protected override void OnPostShow()
        {
            base.OnPostShow();
            // 轮盘也要能压到贴图与菜单栏上面去：只抬层级，不换 Space 归属（同贴图预览窗）
            OverlayWindowService.ApplyNativeWindowLevel(this, EOverlayWindowLevel.RadialMenu);
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
                if (_executedThisShow) return;
                _executedThisShow = true;

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