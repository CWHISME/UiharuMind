using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using UiharuMind.Core.AI.Memery;
using UiharuMind.Resources.Lang;
using UiharuMind.Services;

namespace UiharuMind.Views.Windows.Common;

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
        UIManager.ShowMemoryEditorWindow(UIManager.GetFoucusWindow(), SelectedItem.Memory,
            SelectedItem.Refresh);
    }

    [RelayCommand]
    private async Task DeleteMemory()
    {
        if (SelectedItem == null) return;
        MemoryLibraryItemViewData removing = SelectedItem;
        if (!await _messageService.ConfirmAsync(L("MemoryDeleteConfirm"))) return;
        removing.Memory.DeleteStoredIndex();
        MemoryManager.Instance.Delete(removing.Memory);
        removing.Dispose();
        Memories.Remove(removing);
        ApplyFilter();
        SelectedItem = FilteredMemories.FirstOrDefault();
    }

    [RelayCommand]
    private async Task CreateMemory()
    {
        MemoryCreateRequest? request =
            await UIManager.ShowMemoryCreateWindow(UIManager.GetFoucusWindow());
        if (request == null) return;

        MemoryData memory = MemoryManager.Instance.AddNewItem(request.Name);
        memory.Description = request.Description;
        memory.SaveMetadata();

        var item = new MemoryLibraryItemViewData(memory, false);
        Memories.Insert(0, item);
        ApplyFilter();
        SelectedItem = item;
        UIManager.ShowMemoryEditorWindow(UIManager.GetFoucusWindow(), memory, item.Refresh);
    }

    private void ApplyFilter()
    {
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

        if (SelectedItem != null && !FilteredMemories.Contains(SelectedItem))
            SelectedItem = FilteredMemories.FirstOrDefault();
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
        if (e.PropertyName == nameof(MemoryIndexUpdateController.IsUpdating) && SelectedItem != null)
        {
            SelectedItem.IsUpdating = IndexUpdater.IsUpdating;
            SelectedItem.Refresh();
        }
        OnPropertyChanged(nameof(HasBackgroundWork));
    }

    private void OnIndexUpdateCompleted(MemoryIndexUpdateResult result)
    {
        SelectedItem?.Refresh();
    }

    private static string L(string key) =>
        Lang.ResourceManager.GetString(key, LocalizationManager.Instance.CurrentCulture) ?? key;
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
            ? L("MemoryDescriptionFallback")
            : Memory.Description;
        SourceSummary = string.Format(L("MemoryLibrarySourceSummary"),
            Memory.TextSources.Count, Memory.FilePaths.Count);
        StatusKey = IsUpdating
            ? "Progress"
            : !string.IsNullOrWhiteSpace(Memory.LastIndexError)
                ? "Error"
            : Memory.IndexDirty || Memory.LastIndexedAt == null
                ? "Dirty"
                : "Ready";
        StatusText = StatusKey switch
        {
            "Error" => L("MemoryIndexHasError"),
            "Progress" => L("MemoryIndexUpdating"),
            "Ready" => L("MemoryIndexReady"),
            _ => Memory.LastIndexedAt == null
                ? L("MemoryIndexNotBuiltShort")
                : L("MemoryIndexPendingShort")
        };
        LastIndexedText = Memory.LastIndexedAt == null
            ? L("MemoryIndexNeverUpdated")
            : Memory.LastIndexedAt.Value.ToLocalTime().ToString("yyyy/MM/dd HH:mm");
        OnPropertyChanged(nameof(TextSourceCount));
        OnPropertyChanged(nameof(FileSourceCount));
    }

    private void OnMemoryStateChanged() => Avalonia.Threading.Dispatcher.UIThread.Post(Refresh);

    public void Dispose() => Memory.StateChanged -= OnMemoryStateChanged;

    private static string L(string key) =>
        Lang.ResourceManager.GetString(key, LocalizationManager.Instance.CurrentCulture) ?? key;
}
