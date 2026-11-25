using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Ursa.Controls;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.Configs;
using UiharuMind.Core.Configs.RemoteAI;
using UiharuMind.Features.Settings;
using UiharuMind.Shared.Utils;

namespace UiharuMind.App.Tests.Headless;

/// <summary>
/// 子代理模型的多选（ADR 0111）：<see cref="SubAgentModelSlot"/> + 真的 <see cref="SubAgentModelPicker"/>。
///
/// 要钉的是<b>绑定方向</b>这条容易踩空的约定：<c>SelectedItems</c> 是<b>单向</b>绑过去的——
/// 控件拿住 ViewModel 的那个集合实例，自己往里增删，而不是推一个新集合回来。
/// 所以槽里只能清空重填、不能换实例；写反了（或有人「顺手」改成 TwoWay）的表现是
/// 用户勾了模型、界面也变了，配置里却永远只有第一次的那几个。
///
/// 控件而不是裸 ComboBox：页面里那三行只有 <c>Slot="{Binding …}"</c>，绑定由控件内部的
/// <c>RelativeSource AncestorType</c> 完成——那一层错了页面就是空白，而这里量得到。
/// </summary>
[Collection(HeadlessCollection.Name)]
public class SubAgentModelMultiSelectTests
{
    /// <summary>勾选/取消的先后就是轮换顺序：界面集合的顺序必须与配置里的一致</summary>
    [Fact]
    public void Selected_IsTheSameCollectionTheControlMutates()
    {
        HeadlessUi.Run(() =>
        {
            Fixture fixture = new(["model-a", "model-b"]);
            (Window window, SubAgentModelPicker picker) = fixture.Show();
            try
            {
                MultiComboBox combo = Combo(picker);
                Assert.Same(fixture.Slot.Selected, combo.SelectedItems);

                fixture.Slot.Selected.Add(fixture.Candidates[0]);
                Pump(window);

                // 选中项画在框里（那枚标签），而不是只动了集合
                Assert.Contains(picker.GetVisualDescendants().OfType<TextBlock>(), x => x.Text == "model-a");
                Assert.DoesNotContain(":selection-empty", combo.Classes);
            }
            finally
            {
                window.Close();
            }
        });
    }

    /// <summary>
    /// 从界面上取消勾选（框里那枚标签的叉，走的就是 <c>MultiComboBox.Remove</c>）：
    /// 集合收缩、配置跟着重写。这是「用户改了就落盘」那条链在控件这一端的样子
    /// </summary>
    [Fact]
    public void RemoveFromControl_WritesBackToConfig()
    {
        HeadlessUi.Run(() =>
        {
            Fixture fixture = new(["model-a", "model-b"]);
            (Window window, SubAgentModelPicker picker) = fixture.Show();
            try
            {
                fixture.Slot.Selected.Add(fixture.Candidates[0]);
                fixture.Slot.Selected.Add(fixture.Candidates[1]);
                Pump(window);
                Assert.Equal(new[] { "model-a", "model-b" }, fixture.Config.GeneralSubAgentModelNames);

                // 标签上的叉就是拿自己（DataContext = 那一项）调这个方法的
                Combo(picker).Remove(new Border { DataContext = fixture.Candidates[0] });
                Pump(window);

                Assert.Equal(new[] { "model-b" }, fixture.Slot.Selected.Select(x => x.ModelName));
                Assert.Equal(new[] { "model-b" }, fixture.Config.GeneralSubAgentModelNames);
            }
            finally
            {
                window.Close();
            }
        });
    }

    /// <summary>
    /// 回填与候选重建都不算用户输入：整条链上<b>一次都不该落盘</b>——
    /// 打开设置页就重写一遍配置是同类 bug 里最难发现的那种（看起来什么都没变）。
    /// </summary>
    [Fact]
    public void LoadAndRefresh_NeverPersist()
    {
        Fixture fixture = new(["model-a", "model-b"]);
        fixture.Config.GeneralSubAgentModelNames = ["model-a"];

        fixture.Slot.Load();
        fixture.Slot.Refresh();

        Assert.Equal(0, fixture.Saves);
        Assert.Equal(new[] { "model-a" }, fixture.Slot.Selected.Select(x => x.ModelName));
    }

    /// <summary>
    /// 候选里暂时没有的模型名<b>留在配置里</b>：模型被停用/被删时界面显示不出来，
    /// 但那不代表用户要把它删掉——模型回来就该自动再生效（与改动前单值那套的行为一致）
    /// </summary>
    [Fact]
    public void MissingCandidate_KeepsItsNameInConfig()
    {
        Fixture fixture = new(["model-a", "model-b"]);
        fixture.Config.GeneralSubAgentModelNames = ["model-a", "ghost"];
        fixture.Slot.Load();

        fixture.Slot.Selected.Add(fixture.Candidates[1]); // 用户勾上 model-b：这一次真落盘
        Assert.Equal(new[] { "model-a", "model-b", "ghost" }, fixture.Config.GeneralSubAgentModelNames);

        fixture.Slot.Refresh();
        Assert.Equal(new[] { "model-a", "model-b", "ghost" }, fixture.Config.GeneralSubAgentModelNames);
        Assert.Equal(new[] { "model-a", "model-b" }, fixture.Slot.Selected.Select(x => x.ModelName));
    }

    /// <summary>
    /// 只剩「模型库已经没有」的名字时：界面上一个都不显示，但恢复默认按钮必须亮着，
    /// 且点它就是真的把这串看不见的名字清干净——否则用户只能去手改 JSON。
    /// </summary>
    [Fact]
    public void OnlyGhosts_StillResettable()
    {
        Fixture fixture = new(["model-a"]);
        fixture.Config.GeneralSubAgentModelNames = ["ghost"];
        fixture.Slot.Load();

        Assert.Empty(fixture.Slot.Selected);
        Assert.True(fixture.Slot.IsNotDefault); // 配置里还有东西，不是出厂态

        fixture.Slot.ResetCommand.Execute(null);

        Assert.Empty(fixture.Config.GeneralSubAgentModelNames);
        Assert.False(fixture.Slot.IsNotDefault);
        // 真的落盘了：Reset 先改配置、再靠 Selected 的集合变更触发 Save，
        // 而 Selected 此时是空的——这条盯的就是「空集合 Clear 也会发事件」这个前提。
        // 不发的话幽灵名字在盘上原样留着，下次启动又回来
        Assert.Equal(1, fixture.Saves);
    }

    private static MultiComboBox Combo(SubAgentModelPicker picker) =>
        picker.GetVisualDescendants().OfType<MultiComboBox>().Single();

    private static ModelRunningData NewModel(string name) =>
        new(new RemoteModelInfo { Config = new RemoteModelConfig { ModelName = name } });

    private static void Pump(Window window)
    {
        for (int i = 0; i < 5; i++)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
        }
    }

    /// <summary>一档模型槽 + 按设置页同款挂上去的真控件</summary>
    private sealed class Fixture
    {
        private readonly SubAgentModelSlot _slot;

        public Fixture(IEnumerable<string> candidates)
        {
            foreach (string name in candidates) Candidates.Add(NewModel(name));
            _slot = new SubAgentModelSlot(Candidates, new SettingsWriteBack(() => Saves++),
                () => Config,
                config => config.GeneralSubAgentModelNames,
                (config, names) => config.GeneralSubAgentModelNames = names,
                () => { });
        }

        public ObservableCollection<ModelRunningData> Candidates { get; } = new();

        public AgentSettingConfig Config { get; } = new();

        public SubAgentModelSlot Slot => _slot;

        public int Saves { get; set; }

        /// <summary>建窗并显示（要先在无头界面线程上，窗口构造本身就要窗口平台）</summary>
        public (Window, SubAgentModelPicker) Show()
        {
            SubAgentModelPicker picker = new() { Slot = _slot };
            Window window = new() { Width = 420, Height = 320, Content = picker };
            window.Show();
            Pump(window);
            return (window, picker);
        }
    }
}
