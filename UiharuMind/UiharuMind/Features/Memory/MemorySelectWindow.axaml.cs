using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using UiharuMind.Resources.Lang;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Shell;
using UiharuMind.Core.AI.Memory;

namespace UiharuMind.Features.Memory;

public partial class MemorySelectWindow : Window
{
    private bool _closeAfterCancellation;

    public MemorySelectWindow()
    {
        InitializeComponent();
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        if (!_closeAfterCancellation &&
            DataContext is MemorySelectWindowModel { HasBackgroundWork: true } model)
        {
            e.Cancel = true;
            await model.CancelAndWaitAsync();
            _closeAfterCancellation = true;
            Close();
            return;
        }

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        (DataContext as MemorySelectWindowModel)?.Dispose();
        base.OnClosed(e);
    }
}

public partial class MemorySelectWindowModel : ObservableObject, IDisposable
{
    private readonly IMessageService _messageService;
    private readonly Action<MemoryData>? _onSelectMemory;
    private readonly Action? _closeWindow;
    private MemoryData? _attachedMemory;

    public ObservableCollection<MemoryLibraryItemViewData> Memories { get; } = [];
    public ObservableCollection<MemoryLibraryItemViewData> FilteredMemories { get; } = [];
    public MemoryIndexUpdateController IndexUpdater { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyCanExecuteChangedFor(nameof(RenameMemoryCommand))]
    private MemoryLibraryItemViewData? _selectedItem;

    [ObservableProperty] private string _searchText = "";
    public bool HasSelection => SelectedItem != null;
    public bool HasBackgroundWork => IndexUpdater.HasBackgroundWork;

    public MemorySelectWindowModel()
    {
        _messageService = App.Services.GetRequiredService<IMessageService>();
        IndexUpdater = new MemoryIndexUpdateController(new MemoryData(), _messageService);
    }

    public MemorySelectWindowModel(
        MemoryData? selectedMemory,
        Action<MemoryData>? onSelectMemory,
        Action? closeWindow = null)
        : this(selectedMemory, onSelectMemory,
            App.Services.GetRequiredService<IMessageService>(), closeWindow)
    {
    }

    public MemorySelectWindowModel(
        MemoryData? selectedMemory,
        Action<MemoryData>? onSelectMemory,
        IMessageService messageService,
        Action? closeWindow = null)
    {
        _messageService = messageService;
        _attachedMemory = selectedMemory;
        _onSelectMemory = onSelectMemory;
        _closeWindow = closeWindow;
        IndexUpdater = new MemoryIndexUpdateController(
            selectedMemory ?? App.MemoryService.MemorySources.FirstOrDefault() ?? new MemoryData(),
            messageService);
        foreach (MemoryData memory in App.MemoryService.MemorySources)
            Memories.Add(new MemoryLibraryItemViewData(memory, memory == selectedMemory));

        ApplyFilter();
        SelectedItem = FilteredMemories.FirstOrDefault(x => x.Memory == selectedMemory) ??
                       FilteredMemories.FirstOrDefault();
        RebindIndexUpdater(SelectedItem?.Memory);
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnSelectedItemChanged(MemoryLibraryItemViewData? value)
    {
        RebindIndexUpdater(value?.Memory);
    }

    [RelayCommand]
    private void AttachMemory()
    {
        if (SelectedItem == null) return;
        _attachedMemory = SelectedItem.Memory;
        foreach (MemoryLibraryItemViewData item in Memories)
            item.IsAttached = item.Memory == _attachedMemory;

        _onSelectMemory?.Invoke(SelectedItem.Memory);
        _closeWindow?.Invoke();
    }

    [RelayCommand]
    private void EditMemory()
    {
        if (SelectedItem == null) return;
        MemoryWindows.ShowMemoryEditorWindow(UIManager.GetFocusWindow(), SelectedItem.Memory,
            SelectedItem.Refresh);
    }

    [RelayCommand]
    private async Task DeleteMemory()
    {
        if (SelectedItem == null) return;
        MemoryLibraryItemViewData removing = SelectedItem;
        if (!await _messageService.ConfirmAsync(Loc.Text("MemoryDeleteConfirm"))) return;

        // 索引文件的清理归 MemoryManager.Delete 收口,这里不再各自记得多调一步
        MemoryManager.Instance.Delete(removing.Memory);
        removing.Dispose();
        Memories.Remove(removing);
        ApplyFilter();
        SelectedItem = FilteredMemories.FirstOrDefault();
    }

    /// <summary>
    /// 重命名选中的记忆库。索引库要跟着搬,所以一律走 <see cref="MemoryManager.ModifyName"/>,
    /// 不直接改 <see cref="MemoryData.Name"/>——直接改会让索引留在旧名字下,检索静默失效。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRenameMemory))]
    private async Task RenameMemory()
    {
        if (!CanRenameMemory || SelectedItem == null) return;

        MemoryLibraryItemViewData item = SelectedItem;
        string? input = await UIManager.ShowStringEditWindow(item.Memory.Name, title: Lang.RenameTitle);
        if (input == null) return;

        string requested = input.Trim();
        if (requested.Length == 0 || requested is "." or ".." ||
            requested.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            // 库文件名跟名称走,非法字符会被替换成 _,两个不同名称就可能撞上同一个库文件
            await _messageService.ShowWarningAsync(Loc.Text("MemoryNameInvalid"));
            return;
        }

        if (string.Equals(requested, item.Memory.Name, StringComparison.Ordinal)) return;

        try
        {
            string finalName = MemoryManager.Instance.ModifyName(item.Memory, requested);
            if (!string.Equals(finalName, requested, StringComparison.Ordinal))
            {
                _messageService.ShowNotification(
                    string.Format(Loc.Text("MemoryRenamedToUniqueName"), finalName));
            }
        }
        catch (Exception e)
        {
            await _messageService.ShowWarningAsync(string.Format(Loc.Text("MemoryRenameFailed"), e.Message));
        }
        finally
        {
            // 成功要显示新名字,失败也要把回滚后的旧名字刷回来
            item.Refresh();
            ApplyFilter();
        }
    }

    /// <summary>索引正在更新时不许改名:搬库要先关句柄,而那时构建流程正握着它</summary>
    private bool CanRenameMemory => SelectedItem != null && !IndexUpdater.IsUpdating;

    [RelayCommand]
    private async Task CreateMemory()
    {
        MemoryCreateRequest? request =
            await MemoryWindows.ShowMemoryCreateWindow(UIManager.GetFocusWindow());
        if (request == null) return;

        MemoryData memory = MemoryManager.Instance.AddNewItem(request.Name);
        memory.Description = request.Description;
        memory.SaveMetadata();

        var item = new MemoryLibraryItemViewData(memory, false);
        Memories.Insert(0, item);
        ApplyFilter();
        SelectedItem = item;
        MemoryWindows.ShowMemoryEditorWindow(UIManager.GetFocusWindow(), memory, item.Refresh);
    }

    private void ApplyFilter()
    {
        // 先记下选中项:Clear() 会让绑定的列表控件把 SelectedItem 顶成 null 并回写过来,
        // 等填完再判断就已经丢了选中态——改名、搜索框输入都会因此莫名跳选。
        MemoryLibraryItemViewData? previous = SelectedItem;

        string keyword = SearchText.Trim();
        FilteredMemories.Clear();
        foreach (MemoryLibraryItemViewData item in Memories)
        {
            if (keyword.Length == 0 ||
                item.Name.Contains(keyword, StringComparison.CurrentCultureIgnoreCase) ||
                item.Description.Contains(keyword, StringComparison.CurrentCultureIgnoreCase))
            {
                FilteredMemories.Add(item);
            }
        }

        SelectedItem = previous != null && FilteredMemories.Contains(previous)
            ? previous
            : FilteredMemories.FirstOrDefault();
    }

    public void Dispose()
    {
        IndexUpdater.PropertyChanged -= OnIndexUpdaterPropertyChanged;
        IndexUpdater.Completed -= OnIndexUpdateCompleted;
        IndexUpdater.Dispose();
        foreach (MemoryLibraryItemViewData item in Memories) item.Dispose();
    }

    public Task CancelAndWaitAsync() => IndexUpdater.CancelAndWaitAsync();

    private void RebindIndexUpdater(MemoryData? memory)
    {
        IndexUpdater.PropertyChanged -= OnIndexUpdaterPropertyChanged;
        IndexUpdater.Completed -= OnIndexUpdateCompleted;
        if (memory != null && !ReferenceEquals(IndexUpdater.Memory, memory))
            IndexUpdater.ChangeMemory(memory);
        IndexUpdater.PropertyChanged += OnIndexUpdaterPropertyChanged;
        IndexUpdater.Completed += OnIndexUpdateCompleted;
    }

    private void OnIndexUpdaterPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MemoryIndexUpdateController.IsUpdating))
        {
            if (SelectedItem != null)
            {
                SelectedItem.IsUpdating = IndexUpdater.IsUpdating;
                SelectedItem.Refresh();
            }

            RenameMemoryCommand.NotifyCanExecuteChanged(); //更新期间禁用改名,更新完再放开
        }

        OnPropertyChanged(nameof(HasBackgroundWork));
    }

    private void OnIndexUpdateCompleted(MemoryIndexUpdateResult result)
    {
        SelectedItem?.Refresh();
    }
}

public partial class MemoryLibraryItemViewData : ObservableObject, IDisposable
{
    public MemoryData Memory { get; }

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private string _sourceSummary = "";
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _statusKey = "Dirty";
    [ObservableProperty] private string _lastIndexedText = "";
    [ObservableProperty] private bool _isAttached;
    [ObservableProperty] private bool _isUpdating;

    public int TextSourceCount => Memory.TextSources.Count;
    public int FileSourceCount => Memory.FilePaths.Count;

    public MemoryLibraryItemViewData(MemoryData memory, bool isAttached)
    {
        Memory = memory;
        IsAttached = isAttached;
        Memory.StateChanged += OnMemoryStateChanged;
        Refresh();
    }

    public void Refresh()
    {
        Name = Memory.Name;
        Description = string.IsNullOrWhiteSpace(Memory.Description)
            ? Loc.Text("MemoryDescriptionFallback")
            : Memory.Description;
        SourceSummary = string.Format(Loc.Text("MemoryLibrarySourceSummary"),
            Memory.TextSources.Count, Memory.FilePaths.Count);
        MemoryIndexStatusView status = MemoryIndexUiText.ResolveStatusView(
            MemoryIndexState.From(Memory, IsUpdating));
        StatusKey = status.StatusKey;
        StatusText = status.StatusText;
        LastIndexedText = status.LastIndexedText;
        OnPropertyChanged(nameof(TextSourceCount));
        OnPropertyChanged(nameof(FileSourceCount));
    }

    private void OnMemoryStateChanged() => Avalonia.Threading.Dispatcher.UIThread.Post(Refresh);

    public void Dispose() => Memory.StateChanged -= OnMemoryStateChanged;
}
