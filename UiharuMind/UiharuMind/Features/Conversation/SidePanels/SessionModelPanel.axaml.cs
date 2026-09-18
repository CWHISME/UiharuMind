using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using UiharuMind.Core.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Core;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Conversation.SidePanels;

public partial class SessionModelPanel : UserControl
{
    public SessionModelPanel()
    {
        InitializeComponent();
    }
}

/// <summary>
/// 本会话模型：空即跟随全局，选过即钉选。选项复用全局模型列表，写回走会话头。
/// 条目实例稳定复用——选中项引用不变，ComboBox 不会因列表重建丢显示。
/// 只经窄依赖与宿主会话对话，不持有任何页面或视图模型。
/// </summary>
public partial class SessionModelViewData : ObservableObject, IDisposable
{
    private readonly Func<ChatSessionMeta?> _metaSource;
    private readonly Func<bool> _isLoadingSource;
    private readonly Action _onChanged;
    private bool _syncing; //回填期抑制写回
    private string? _draftName; //空态草稿：尚无会话时的预选，建会话时带入
    private readonly Dictionary<string, SessionModelOption> _byName = new(StringComparer.Ordinal);
    private readonly SessionModelOption _defaultOption = new() { IsDefault = true };
    private readonly SessionModelOption _missingOption = new() { IsUnavailable = true };

    [ObservableProperty] private ObservableCollection<SessionModelOption> _options = new();
    [ObservableProperty] private SessionModelOption? _selectedOption;
    [ObservableProperty] private bool _hasSession; //宿主会话存在（空态显示草稿，也为真）
    [ObservableProperty] private bool _hasOverride; //钉选徽章显隐
    [ObservableProperty] private string _effectiveHint = string.Empty; //实际生效模型

    /// <summary>设计期预览用，不订阅不刷新</summary>
    public SessionModelViewData() : this(() => null, () => true, () => { })
    {
    }

    /// <param name="metaSource">当前会话元数据</param>
    /// <param name="isLoadingSource">会话装载中（读，不是用户改动）</param>
    /// <param name="onChanged">覆写变化，宿主据此刷模型名与用量</param>
    public SessionModelViewData(Func<ChatSessionMeta?> metaSource, Func<bool> isLoadingSource, Action onChanged)
    {
        _metaSource = metaSource;
        _isLoadingSource = isLoadingSource;
        _onChanged = onChanged;
        if (App.ModelService?.ModelSources is { } sources)
            sources.CollectionChanged += OnModelSourcesChanged;
        LlmManager.Instance.OnCurrentModelChanged += OnGlobalModelChanged;
        LocalizationManager.Instance.LanguageChanged += OnLanguageChanged;
    }

    public void Dispose()
    {
        if (App.ModelService?.ModelSources is { } sources)
            sources.CollectionChanged -= OnModelSourcesChanged;
        LlmManager.Instance.OnCurrentModelChanged -= OnGlobalModelChanged;
        LocalizationManager.Instance.LanguageChanged -= OnLanguageChanged;
    }

    /// <summary>取走空态草稿的模型名（建会话时带入），取后清空</summary>
    /// <returns>草稿名；无预选为 null</returns>
    public string? TakeDraft()
    {
        string? draft = _draftName;
        _draftName = null;
        return draft;
    }

    /// <summary>会话切换后按新元数据回填选中项</summary>
    public void Refresh()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(Refresh);
            return;
        }

        SyncAll();
    }

    partial void OnSelectedOptionChanged(SessionModelOption? value)
    {
        if (_syncing || value == null) return;
        if (_isLoadingSource()) return;
        ChatSessionMeta? meta = _metaSource();
        string? name = value.IsDefault ? null : value.ModelName;

        // 空态无会话可落盘，记成草稿等首轮建会话时带入
        if (meta == null)
        {
            if (string.Equals(_draftName ?? "", name ?? "", StringComparison.Ordinal)) return;
            _draftName = name;
            WarmupRemote(name);
            SyncSelection();
            return;
        }

        if (string.Equals(meta.SessionModelName ?? "", name ?? "", StringComparison.Ordinal)) return;
        meta.SessionModelName = name;
        ConversationSessionBinder.PersistSettings(meta);
        WarmupRemote(name);
        _onChanged();
        SyncSelection(); //只动选中与徽章，条目实例不动
    }

    // 远程模型顺手预热：选中即拉起，首轮发送时多半已就绪；
    // 本地走不了自动加载，发送时按规则回落或报错
    private static void WarmupRemote(string? modelName)
    {
        if (string.IsNullOrEmpty(modelName)) return;
        ModelRunningData? running = App.ModelService?.ModelSources
            .FirstOrDefault(m => m.ModelName == modelName);
        if (running is { IsRemoteModel: true }) LlmManager.Instance.EnsureModelStarted(running);
    }

    private void OnModelSourcesChanged(object? sender, NotifyCollectionChangedEventArgs e) => Refresh();

    private void OnGlobalModelChanged(ModelRunningData? model) => Refresh();

    private void OnLanguageChanged() => Refresh();

    private void SyncAll()
    {
        SyncOptions();
        SyncSelection();
    }

    // 增量同步条目：只增删，不重建——选中项引用全程稳定
    private void SyncOptions()
    {
        // 默认项报出它此刻实际指向谁。光写"默认（跟随全局）"看不出跟的是哪个,
        // 而这正是用户要复制模型名时最先看的那一行
        string? followed = ResolveEffectiveName(null);
        _defaultOption.DisplayName = string.IsNullOrEmpty(followed)
            ? Loc.Text(LangKey.SessionModelDefault)
            : string.Format(Loc.Text(LangKey.SessionModelDefaultFormat), followed);
        if (!Options.Contains(_defaultOption)) Options.Insert(0, _defaultOption);

        HashSet<string> seen = new(StringComparer.Ordinal);
        if (App.ModelService?.ModelSources is { } sources)
        {
            foreach (ModelRunningData model in sources)
            {
                seen.Add(model.ModelName);
                if (!_byName.TryGetValue(model.ModelName, out SessionModelOption? option))
                {
                    option = new SessionModelOption
                    {
                        ModelName = model.ModelName,
                        DisplayName = model.ModelName,
                        IsRemoteModel = model.IsRemoteModel,
                        IsVisionModel = model.IsVisionModel,
                    };
                    _byName[model.ModelName] = option;
                    Options.Add(option);
                }
            }
        }

        for (int i = Options.Count - 1; i >= 0; i--)
        {
            SessionModelOption option = Options[i];
            if (option.IsDefault || option.IsUnavailable) continue;
            if (seen.Contains(option.ModelName ?? "")) continue;
            Options.RemoveAt(i);
            _byName.Remove(option.ModelName ?? "");
        }
    }

    // 实际会问谁：命中且可用（远程未起也会被拉起）走钉选，否则走全局，
    // 与 LazyChatClient.ResolveAsync 同口径，提示行据此不说谎
    private static string? ResolveEffectiveName(string? pinned)
    {
        if (!string.IsNullOrEmpty(pinned) &&
            App.ModelService?.ModelSources.FirstOrDefault(m => m.ModelName == pinned)
                is { } candidate &&
            (candidate.IsRunning || candidate.IsRemoteModel))
            return pinned;
        return LlmManager.Instance.CurrentRunningModel?.ModelName
            ?? LlmManager.Instance.GetPreferredModelName(false);
    }

    private void SyncSelection()
    {
        _syncing = true;
        try
        {
            ChatSessionMeta? meta = _metaSource();
            HasSession = true;
            string? metaName = string.IsNullOrEmpty(meta?.SessionModelName) ? null : meta!.SessionModelName;
            string? pinned = metaName ?? (meta == null ? _draftName : null);

            SessionModelSelectionKind kind =
                SessionModelOption.ResolveSelection(pinned, _byName.Keys);
            SessionModelOption target = kind switch
            {
                SessionModelSelectionKind.Model => _byName[pinned!],
                SessionModelSelectionKind.Missing => _missingOption,
                _ => _defaultOption,
            };

            // 钉选名已不在列表里：保留条目并回落全局，下次模型回来自动恢复
            if (kind == SessionModelSelectionKind.Missing)
            {
                _missingOption.ModelName = pinned;
                _missingOption.DisplayName = $"{pinned}（{Loc.Text(LangKey.SessionModelUnavailableSuffix)}）";
                if (!Options.Contains(_missingOption)) Options.Add(_missingOption);
            }
            else if (Options.Contains(_missingOption))
            {
                Options.Remove(_missingOption);
            }

            HasOverride = pinned != null;
            EffectiveHint = string.IsNullOrEmpty(ResolveEffectiveName(pinned))
                ? string.Empty
                : string.Format(Loc.Text(LangKey.SessionModelEffectiveFormat), ResolveEffectiveName(pinned));
            if (!ReferenceEquals(SelectedOption, target)) SelectedOption = target;
        }
        finally
        {
            _syncing = false;
        }
    }
}
