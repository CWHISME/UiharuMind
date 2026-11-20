using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Threading;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.Configs;
using UiharuMind.Core.Configs.RemoteAI;
using UiharuMind.Core.Core;
using UiharuMind.Features.Settings;
using UiharuMind.Shared.Utils;

namespace UiharuMind.App.Tests.Headless;

/// <summary>
/// 设置页模型下拉的候选重建：<b>清空候选列表那一下，绑定着的 ComboBox 会把 SelectedItem
/// 推成 null 并写回 ViewModel</b>。设置页的属性 setter 是「变更即落盘」，于是重建候选
/// 就被当成了「用户选了空」——配置被写成空串，而紧接着的回填又把界面恢复原样，
/// 看上去一切正常，只有重启后才发现设置没了。
///
/// 这里不建真的设置页（那要 LlmManager 的模型列表），只把同一套结构搬进来：
/// ViewModel 属性 + TwoWay 绑定的真 ComboBox + <see cref="SettingsWriteBack"/>。
/// 钉住的是「重建必须包在闸门里」这条约定，不是某一个页面的实现。
/// </summary>
[Collection(HeadlessCollection.Name)]
public class SettingsModelListRebuildTests
{
    /// <summary>
    /// 根因：不包闸门时，Clear() 会把选中项推空并触发一次落盘。
    /// 这条测试就是当初那个 bug 的最小复现，改上层写法别把它删了。
    /// </summary>
    [Fact]
    public void ClearWithoutGuard_PushesNullIntoViewModel_AndSaves()
    {
        HeadlessUi.Run(() =>
        {
            RebuildProbeVm vm = new();
            (Window window, ComboBox combo) = ShowCombo(vm);
            try
            {
                vm.Items.Add("a");
                vm.Items.Add("b");
                Pump(window);

                combo.SelectedItem = vm.Items[0];
                Pump(window);
                Assert.Same(vm.Items[0], vm.Selected);

                vm.Saves = 0; // 上面那次是用户选择，不算重建

                vm.Items.Clear();
                Pump(window);

                Assert.Null(vm.Selected);
                Assert.Equal(1, vm.Saves);
            }
            finally
            {
                window.Close();
            }
        });
    }

    /// <summary>
    /// 修好之后：重建候选整段包在闸门里，控件推回来的 null 不落盘，
    /// 回填按名字找回原选中项。
    /// </summary>
    [Fact]
    public void RebuildInsideGuard_KeepsSelection_AndSavesNothing()
    {
        HeadlessUi.Run(() =>
        {
            RebuildProbeVm vm = new();
            (Window window, ComboBox combo) = ShowCombo(vm);
            try
            {
                vm.Items.Add("a");
                vm.Items.Add("b");
                Pump(window);

                combo.SelectedItem = vm.Items[0];
                Pump(window);
                string? name = vm.Selected as string;

                vm.Saves = 0;
                vm.Rebuild(["a", "b", "c"], name);
                Pump(window);

                Assert.Equal(0, vm.Saves);
                Assert.Equal(name, vm.Selected);
                Assert.Equal(name, combo.SelectedItem);
            }
            finally
            {
                window.Close();
            }
        });
    }

    /// <summary>
    /// 端到端：真的快捷工具设置页。模型列表刷新后，那个模型不在候选里（被删/被停用/列表还没拉完），
    /// <b>界面回落到「跟随顶栏」是正常的，但配置里那个名字不该被抹掉</b>——
    /// 内存与盘上都要还在，模型重新出现时才能自动生改。
    /// </summary>
    [Fact]
    public void QuickToolRefresh_WhenModelMissing_KeepsConfiguredName()
    {
        HeadlessUi.Run(() =>
        {
            string original = QuickToolSetting.Current.DefaultModelName;
            try
            {
                QuickToolSetting.Current.DefaultModelName = "probe-model";
                QuickToolSetting.Current.Save();

                QuickToolSettingViewData vm = new();
                ComboBox combo = new() { ItemsSource = vm.AvailableModels, Width = 200 };
                combo.Bind(SelectingItemsControl.SelectedItemProperty,
                    new Binding(nameof(QuickToolSettingViewData.DefaultModel))
                        { Source = vm, Mode = BindingMode.TwoWay });
                Window window = new() { Width = 300, Height = 200, Content = combo };
                window.Show();
                Pump(window);
                try
                {
                    // 候选里现在有一个模型，用户选中它
                    ModelRunningData probe = new(new RemoteModelInfo
                    {
                        Config = new RemoteModelConfig { ModelName = "probe-model" },
                    });
                    vm.AvailableModels.Add(probe);
                    Pump(window);
                    combo.SelectedItem = probe;
                    Pump(window);
                    Assert.Equal("probe-model", QuickToolSetting.Current.DefaultModelName);

                    // 模型列表刷新：LlmManager 里没有这个模型，候选重建把它清掉
                    vm.RefreshAvailableModels();
                    Pump(window);

                    Assert.Null(vm.DefaultModel); // 界面回落到跟随顶栏
                    Assert.Equal("probe-model", QuickToolSetting.Current.DefaultModelName);
                    QuickToolSetting? onDisk = SaveUtility.Load<QuickToolSetting>(
                        AppPaths.Config.ForType(nameof(QuickToolSetting)));
                    Assert.Equal("probe-model", onDisk?.DefaultModelName);
                }
                finally
                {
                    window.Close();
                }
            }
            finally
            {
                QuickToolSetting.Current.DefaultModelName = original;
                QuickToolSetting.Current.Save();
            }
        });
    }

    private static (Window, ComboBox) ShowCombo(RebuildProbeVm vm)
    {
        ComboBox combo = new()
        {
            ItemsSource = vm.Items,
            Width = 200,
        };
        combo.Bind(SelectingItemsControl.SelectedItemProperty,
            new Binding(nameof(RebuildProbeVm.Selected)) { Source = vm, Mode = BindingMode.TwoWay });

        Window window = new() { Width = 300, Height = 200, Content = combo };
        window.Show();
        Pump(window);
        return (window, combo);
    }

    private static void Pump(Window window)
    {
        for (int i = 0; i < 5; i++)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
        }
    }

    /// <summary>
    /// 与设置页同一套结构：属性 setter 先写「配置」再落盘；重建候选是另一步。
    /// </summary>
    private sealed class RebuildProbeVm : INotifyPropertyChanged
    {
        private readonly SettingsWriteBack _writeBack; //写回闸门
        private object? _selected;

        public RebuildProbeVm()
        {
            _writeBack = new SettingsWriteBack(() => Saves++);
        }

        public ObservableCollection<object> Items { get; } = new();

        /// <summary>落盘次数，代替真的配置写入</summary>
        public int Saves { get; set; }

        public object? Selected
        {
            get => _selected;
            set
            {
                if (ReferenceEquals(_selected, value)) return;
                _selected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Selected)));
                _writeBack.Save();
            }
        }

        /// <summary>重建候选并按名回填——设置页 refresh 路径的等价物</summary>
        public void Rebuild(IEnumerable<string> names, string? selectedName)
        {
            using (_writeBack.BeginLoad())
            {
                Items.Clear();
                foreach (string name in names) Items.Add(name);
                Selected = selectedName == null ? null : Items.FirstOrDefault(x => (string)x == selectedName);
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
