/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using Avalonia.Controls;
using UiharuMind.App.Tests.Headless;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Features.Conversation.SidePanels;
using Xunit;

namespace UiharuMind.App.Tests.Headless;

/// <summary>
/// 上限那一行是一根滑杆，不是两段拼的。
///
/// 这条是补一个真实的错：原先左边画了一段独立的灰条表示「低于下限不可调」，
/// 而滑杆自己会给拇指留半个拇指宽的边距，两者永远接不上——屏幕上看到的就是一根断掉的进度条。
/// 现在下限只由 <c>Minimum</c> 挡住，轨道从行首起、一直铺到行尾。
/// </summary>
[Collection(HeadlessCollection.Name)]
public class ContextBudgetLayoutTests
{
    [Fact]
    public void Slider_SpansTheWholeRow()
    {
        HeadlessUi.Run(() =>
        {
            ChatSession session = new("t", new CharacterData { CharacterId = "t" }) { IsTransient = true };
            SessionContextBudgetViewData vm = new(() => session, () => 1_000_000, () => 176_000, () => { });
            vm.Refresh();

            SessionContextBudgetView view = new() { DataContext = vm };
            Window window = new() { Width = 400, Height = 200, Content = view };
            window.Show();
            window.UpdateLayout();

            Slider slider = view.FindControl<Slider>("BudgetSlider")!;

            Assert.True(vm.HasRoom);
            // 从行首起、铺满整行：左边不留空档（留了就是「没对齐」），右边不留缺口
            Assert.InRange(slider.Bounds.X, 0, 0.5);
            Assert.InRange(slider.Bounds.Width, view.Bounds.Width - 0.5, view.Bounds.Width + 0.5);
            // 下限仍在：拖不过去，只是不再另画一段灰条
            Assert.Equal(vm.Minimum, slider.Minimum);
        });
    }

    /// <summary>没有占用时下限为 0，滑杆铺满整行；占用顶到窗口时整条不可用，改说原因</summary>
    [Fact]
    public void Slider_CollapsesWhenThereIsNoRoom()
    {
        HeadlessUi.Run(() =>
        {
            ChatSession session = new("t", new CharacterData { CharacterId = "t" }) { IsTransient = true };
            SessionContextBudgetViewData vm = new(() => session, () => 1_000_000, () => 2_000_000, () => { });
            vm.Refresh();

            SessionContextBudgetView view = new() { DataContext = vm };
            Window window = new() { Width = 400, Height = 200, Content = view };
            window.Show();
            window.UpdateLayout();

            Slider slider = view.FindControl<Slider>("BudgetSlider")!;

            Assert.Equal(vm.Maximum, vm.Minimum);
            Assert.False(vm.HasRoom);
            Assert.False(slider.IsVisible);
        });
    }
}
