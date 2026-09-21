using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
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
            // 中心圆压一层柔光阴影，托出层次（同贴图预览窗的阴影留白思路）
            CenterDot.BoxShadow = new BoxShadows(new BoxShadow
            {
                Color = Color.FromArgb(0xA0, 0, 0, 0),
                Blur = 18,
                OffsetX = 0,
                OffsetY = 5
            });
            // 整轮罩一层投影：阴影跟着轮盘整体剪影走。不能逐扇形加，扇形之间会互相叠脏
            RadialMenuControl.Effect = new DropShadowEffect
            {
                Color = Color.FromArgb(0x90, 0, 0, 0),
                BlurRadius = 24,
                OffsetX = 0,
                OffsetY = 0
            };
        }

        public override void Awake()
        {
            base.Awake();
            // 轮盘要压住贴图并盖上菜单栏：base 给的是 BorderOnly（含 titled mask），
            // titled 窗会被 AppKit 框在标题栏可够到的范围，setFrame 顶到菜单栏下就不再往上——
            // 与贴图预览窗、截图遮罩同方案，必须 borderless；只动装饰这一项，不碰背景与透明配置
            WindowDecorations = WindowDecorations.None;
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
            CenterOnMouseAllowOverflow();
            // 入场淡入：跟随鼠标的瞬时浮窗，120ms 淡入去硬切感（复用仓内现成过渡）
            RadialMenuControl.Opacity = 0;
            UiAnimationUtils.PlayAlphaTransitionAnimation(RadialMenuControl, true);
        }

        /// <summary>
        /// 以鼠标为中心落位，允许超出屏幕（屏边打开也不往回挤）。
        /// 落位走贴图同款 setFrame 原子提交：托管 <c>Position</c> 在 macOS 上会被系统按可见区域钳制，
        /// 越界场景下落不到鼠标位置；提交失败回退托管老路。
        /// </summary>
        private void CenterOnMouseAllowOverflow()
        {
            // 纯 Wayland 下拿不到可信鼠标位置，老路回屏幕中央（那一路自带钳制，中央落点本就不需要越界）
            if (!App.ScreensService.IsMousePositionReliable)
            {
                this.SetScreenCenterPosition();
                return;
            }

            Size size = Bounds is { Width: > 0, Height: > 0 } ? Bounds.Size : new Size(Width, Height);
            PixelPoint mouse = App.ScreensService.MousePosition;
            double scaling = App.ScreensService.Scaling;
            var topLeft = new PixelPoint(
                (int)(mouse.X - size.Width / 2 * scaling),
                (int)(mouse.Y - size.Height / 2 * scaling));
            if (!this.TrySetWindowFrame(topLeft, size)) Position = topLeft;
            Width = size.Width;
            Height = size.Height;
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