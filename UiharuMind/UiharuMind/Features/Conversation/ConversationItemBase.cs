/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Shared.Shell;
using UiharuMind.Shared.Utils.Tools;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Features.Conversation;

/// <summary>
/// 通用会话条目基类:仅含展示与动作契约,不引用任何具体会话/角色类型,
/// 供 Agent 工作区与(后续)角色聊天共用。
/// </summary>
public abstract partial class ConversationItemBase : ObservableObject
{
    [ObservableProperty] private string _senderName = string.Empty;
    [ObservableProperty] private string _timestamp = string.Empty;
    [ObservableProperty] private Bitmap? _icon;
    [ObservableProperty] private IBrush _senderColor = Brushes.Gray;
    [ObservableProperty] private string _message = string.Empty;
    [ObservableProperty] private bool _isDone = true;

    // 四个回调决定四个按钮显不显示,而气泡是先上屏、一轮结束后才由 WireItemActions 接上它们的。
    // 因此必须是可观察的:普通属性赋值不抛通知,悬停菜单一旦在接线之前实例化过,
    // 就会一直停在"只有复制按钮",要切走会话重建条目才恢复

    /// <summary>编辑完成回调(为空则隐藏编辑按钮)</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanEdit))]
    private Action<ConversationItemBase>? _editedCallback;

    /// <summary>删除回调(为空则隐藏删除按钮)</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanDelete))]
    private Action<ConversationItemBase>? _deleteCallback;

    /// <summary>重试回调(为空则隐藏重试按钮)</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanRetry))]
    private Action<ConversationItemBase>? _retryCallback;

    /// <summary>
    /// 分叉回调：从本条消息处复制出一个新对话（为空则隐藏分叉按钮）。
    /// 聊天页原有能力，统一到本条目体系时必须保留，否则是功能回退。
    /// </summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanBranch))]
    private Action<ConversationItemBase>? _branchCallback;

    /// <summary>是否用户侧条目(右对齐)</summary>
    public virtual bool IsUser => false;

    /// <summary>是否系统条目(居中、无头像)</summary>
    public virtual bool IsSystem => false;

    /// <summary>内容水平对齐</summary>
    public HorizontalAlignment Alignment =>
        IsSystem ? HorizontalAlignment.Center :
        IsUser ? HorizontalAlignment.Right : HorizontalAlignment.Left;

    /// <summary>头像所在列(布局 Grid 为 Auto,*,Auto)</summary>
    public int AvatarColumn => IsUser ? 2 : 0;

    /// <summary>是否显示头像</summary>
    public bool ShowAvatar => !IsSystem && Icon != null;

    /// <summary>
    /// 本条目对应的历史消息。编辑/删除/重试/分叉都需要据此定位到历史里的那一条；
    /// 为空表示该条目不对应单条消息（如流式进行中的占位、框架注入的内容），此时不提供这些操作。
    /// </summary>
    public Microsoft.Extensions.AI.ChatMessage? SourceMessage { get; set; }

    /// <summary>是否可编辑</summary>
    public bool CanEdit => EditedCallback != null;

    /// <summary>是否可删除</summary>
    public bool CanDelete => DeleteCallback != null;

    /// <summary>是否可重试</summary>
    public bool CanRetry => RetryCallback != null;

    /// <summary>是否可分叉</summary>
    public bool CanBranch => BranchCallback != null;

    [RelayCommand]
    private void Copy()
    {
        App.Clipboard.CopyToClipboard(Message, true);
    }

    [RelayCommand]
    private async Task Edit()
    {
        if (EditedCallback == null) return;
        string? result = await UIManager.ShowStringEditWindow(Message);
        if (result == null) return;
        Message = result;
        EditedCallback.Invoke(this);
    }

    [RelayCommand]
    private void Delete()
    {
        DeleteCallback?.Invoke(this);
    }

    [RelayCommand]
    private void Retry()
    {
        RetryCallback?.Invoke(this);
    }

    [RelayCommand]
    private void Branch()
    {
        BranchCallback?.Invoke(this);
    }
}

/// <summary>
/// 标准文本气泡条目(支持流式追加),ConversationView 内置其模板
/// </summary>
public partial class TextConversationItem : ConversationItemBase
{
    private readonly StringBuilder _buffer = new();
    private readonly bool _isUser;

    /// <summary>
    /// UI 侧节流。流式期间每个 token 都把累积全文重设一次,渲染侧要么做全量文本重排、
    /// 要么重解析整篇 markdown,成本随长度二次增长。50ms(~20Hz)肉眼仍是连续的,
    /// 重排次数却降一个数量级。尾巴由 <see cref="Flush"/> 保证不丢。
    /// </summary>
    private readonly ValueUiDelayUpdater<object?> _throttle;

    public override bool IsUser => _isUser;

    /// <summary>随消息一同显示的图片（多模态消息里的 DataContent），一条消息可以带多张</summary>
    public ObservableCollection<Bitmap> MessageImages { get; } = [];

    /// <summary>是否含图片</summary>
    public bool HasImage => MessageImages.Count > 0;

    /// <summary>气泡里缩略图的边长:多图时缩小,免得几张图把气泡撑成一条长龙</summary>
    public double ImageThumbSize => MessageImages.Count > 1 ? 160 : 320;

    /// <summary>
    /// 实际进入模型的正文，与 <see cref="ConversationItemBase.Message"/> 不同时才有值。
    /// 目前只有点名调用会用到：气泡显示 <c>/技能名 参数</c> 那一行，注入的技能正文折在这里。
    /// </summary>
    [ObservableProperty] private string _injectedText = string.Empty;

    /// <summary>注入正文是否展开</summary>
    [ObservableProperty] private bool _isInjectedTextExpanded;

    /// <summary>是否有折叠起来的注入正文</summary>
    public bool HasInjectedText => InjectedText.Length > 0;

    public TextConversationItem(bool isUser)
    {
        _isUser = isUser;
        // 传 null 而非全文:值在真正触发时才从 buffer 取,免得每个 token 都白白拼一次全文
        _throttle = new ValueUiDelayUpdater<object?>(_ => Message = _buffer.ToString(), 50);
    }

    /// <summary>
    /// 追加一段流式增量
    /// </summary>
    /// <param name="delta">增量文本</param>
    public void Append(string delta)
    {
        _buffer.Append(delta);
        _ = _throttle.UpdateValue(null);
    }

    /// <summary>
    /// 立即把缓冲同步到 <see cref="ConversationItemBase.Message"/>(段落收尾时调用)。
    /// 节流器允许最后一次追加晚到 50ms，收尾处必须显式冲刷，否则最后几个字会短暂缺失。
    /// </summary>
    public void Flush()
    {
        Message = _buffer.ToString();
    }

    /// <summary>
    /// 追加一张消息里的图片；解码失败则跳过这一张
    /// </summary>
    /// <param name="bytes">图片字节</param>
    public void AddImage(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.IsEmpty) return;
        try
        {
            using MemoryStream stream = new(bytes.ToArray());
            MessageImages.Add(new Bitmap(stream));
        }
        catch (Exception e)
        {
            Log.Warning($"Load message image failed: {e.Message}");
            return;
        }

        OnPropertyChanged(nameof(HasImage));
        OnPropertyChanged(nameof(ImageThumbSize));
    }

    partial void OnInjectedTextChanged(string value)
    {
        OnPropertyChanged(nameof(HasInjectedText));
    }
}
