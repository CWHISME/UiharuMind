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
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Shell;
using UiharuMind.Shared.Utils.Tools;
using UiharuMind.Shared.Windows;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Resources.Lang;

namespace UiharuMind.Features.Conversation.Items;

/// <summary>
/// 通用会话条目基类:仅含展示与动作契约,不引用任何具体会话/角色类型,
/// 供 Agent 工作区与(后续)角色聊天共用。
/// </summary>
public abstract partial class ConversationItemBase : ObservableObject
{
    [ObservableProperty] private string _senderName = string.Empty;
    [ObservableProperty] private string _timestamp = string.Empty;

    // 头像:小而长寿,且 IconUtils 可能返回全进程共用的那几张默认图,
    // 释放它会把整个进程的头像一起清空。进程级缓存,不 Dispose
    [ObservableProperty] private Bitmap? _icon;
    [ObservableProperty] private IBrush _senderColor = Brushes.Gray;
    [ObservableProperty] private string _message = string.Empty;
    [ObservableProperty] private bool _isDone = true;

    /// <summary>正文被重新赋值时调用（含流式逐拍赋值）；子类需要从 <see cref="Message"/> 派生展示视图时重写</summary>
    partial void OnMessageChanged(string value) => OnMessageUpdated();

    /// <summary>正文变化钩子。重算成本按子类自担：高频的流式赋值里应当先判断自己是否真的依赖正文</summary>
    protected virtual void OnMessageUpdated()
    {
    }

    // 四个回调决定四个按钮显不显示,而气泡是先上屏、一轮结束后才由 WireItemActions 接上它们的。
    // 因此必须是可观察的:普通属性赋值不抛通知,悬停菜单一旦在接线之前实例化过,
    // 就会一直停在"只有复制按钮",要切走会话重建条目才恢复

    /// <summary>编辑完成回调(为空则隐藏编辑按钮)</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanEdit))]
    private Action<ConversationItemBase>? _editedCallback;

    /// <summary>删除回调(为空则隐藏删除按钮)。异步:删除前要弹确认</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanDelete))]
    private Func<ConversationItemBase, Task>? _deleteCallback;

    /// <summary>重试回调(为空则隐藏重试按钮)</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanRetry))]
    private Action<ConversationItemBase>? _retryCallback;

    /// <summary>
    /// 分叉回调：从本条消息处复制出一个新对话（为空则隐藏分叉按钮）。
    /// 聊天页原有能力，统一到本条目体系时必须保留，否则是功能回退。
    /// </summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanBranch))]
    private Action<ConversationItemBase>? _branchCallback;

    /// <summary>
    /// 条目被永久丢弃时释放它持有的大位图（切会话、删条目、重跑截断历史）。
    ///
    /// <b>必须在条目已经从集合里摘掉之后调用</b>：还挂在界面上的位图一释放，下一帧渲染就撞上去。
    /// <see cref="Icon"/> 刻意不在此释放——头像可能是 <c>IconUtils</c> 那几张进程级共用的默认图。
    /// </summary>
    public virtual void ReleaseImages()
    {
    }

    /// <summary>是否用户侧条目(右对齐)</summary>
    public virtual bool IsUser => false;

    /// <summary>是否系统条目(居中、无头像)</summary>
    public virtual bool IsSystem => false;

    /// <summary>
    /// 是否旁白条目(居中、无头像、无发送者名)。目前只有开场白是——
    /// 它是角色的台词但不是"角色在跟你说话"，所以不画成气泡对话的一方。
    /// </summary>
    public virtual bool IsNarration => false;

    /// <summary>内容水平对齐</summary>
    public HorizontalAlignment Alignment =>
        IsSystem || IsNarration ? HorizontalAlignment.Center :
        IsUser ? HorizontalAlignment.Right : HorizontalAlignment.Left;

    /// <summary>头像所在列(布局 Grid 为 Auto,*,Auto)</summary>
    public int AvatarColumn => IsUser ? 2 : 0;

    /// <summary>是否显示头像</summary>
    public bool ShowAvatar => !IsSystem && !IsNarration && Icon != null;

    /// <summary>是否显示发送者名字</summary>
    public bool ShowSenderName => !IsNarration;

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
        App.Clipboard.CopyToClipboard(Message, true, true);
    }

    [RelayCommand]
    private async Task Edit()
    {
        if (EditedCallback == null) return;
        string? result = await UIManager.ShowStringEditWindow(Message, title: Lang.EditMessageTitle);
        if (result == null) return;
        Message = result;
        EditedCallback.Invoke(this);
    }

    [RelayCommand]
    private async Task Delete()
    {
        if (DeleteCallback != null) await DeleteCallback(this);
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
public partial class TextConversationItem : ConversationItemBase, IStreamFlushTarget
{
    private readonly StringBuilder _buffer = new();
    private readonly object _bufferGate = new(); //追加来自推理线程,冲刷在 UI 线程,两边都要过锁
    private readonly bool _isUser;
    private readonly bool _isNarration;

    /// <summary>
    /// UI 侧上屏间隔。流式期间每个 token 都把累积全文重设一次,渲染侧要么做全量文本重排、
    /// 要么重解析整篇 markdown,成本随长度二次增长。50ms(~20Hz)肉眼仍是连续的,
    /// 重排次数却降一个数量级。节拍由 <see cref="StreamFlushPump"/> 统一给,
    /// 尾巴由 <see cref="Flush"/> 保证不丢。
    /// </summary>
    public int FlushIntervalMs => 50;

    public override bool IsUser => _isUser;

    /// <inheritdoc />
    public override bool IsNarration => _isNarration;

    // 旁白类(开场白/子代理后续报告)的截断视图。只对旁白启用:它是静态的"扫一眼"内容,
    // 与工具结果同一语义;用户/助手消息是流式的、正在被阅读,截断会破坏阅读体验。
    // 开场白通常很短不会触发,长的是子代理报告——超长时显示头部 + 提示行 + 查看全文
    private ToolResultView _view = ToolResultTruncation.Empty;

    /// <summary>气泡真正渲染的正文:旁白超长时是截断后的头部,其余透传原文</summary>
    public string DisplayMessage => IsNarration ? _view.DisplayText : Message;

    /// <summary>正文是否被截断(仅旁白类可能为真;决定「查看全文」入口显隐)</summary>
    public bool IsTruncated => IsNarration && _view.IsTruncated;

    /// <summary>截断提示行文案(未截断时为空串,提示行不显示)</summary>
    public string TruncationHint => IsTruncated ? ToolResultTruncation.FormatTruncationHint(_view) : string.Empty;

    /// <summary>打开全文窗(卡片上的「查看全文」按钮)。全文一律去独立窗口,理由同工具卡——
    /// 会话流没有虚拟化,几十万字内联进气泡会当场冻住界面</summary>
    [RelayCommand]
    private void ShowFullText()
    {
        // 与工具卡同一支笔:窗口标题就是「查看全文」,不另立资源
        FullTextWindow.Show(Loc.Text(LangKey.ToolViewFullText), Message);
    }

    /// <inheritdoc />
    protected override void OnMessageUpdated()
    {
        // 非旁白不截断:DisplayMessage 直接透传原文,不跑 Build。
        // 但<b>绑定的是 DisplayMessage 而不是 Message</b>,Message 变化必须转告它,
        // 否则流式气泡收不到更新、一直停在空白(切走切回整段重建才恢复)
        if (!IsNarration)
        {
            OnPropertyChanged(nameof(DisplayMessage));
            return;
        }

        _view = ToolResultTruncation.Build(Message);
        OnPropertyChanged(nameof(DisplayMessage));
        OnPropertyChanged(nameof(IsTruncated));
        OnPropertyChanged(nameof(TruncationHint));
    }

    /// <summary>
    /// 随消息一同显示的图片（多模态消息里的 DataContent），一条消息可以带多张。
    /// 解码后按原始像素驻留（缩放后仍可达上千像素边长），<b>由本条目独占并在
    /// <see cref="ReleaseImages"/> 里释放</b>——条目被整体丢弃时没有别人会去释放它们。
    /// </summary>
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

    /// <param name="isUser">是否用户侧气泡</param>
    /// <param name="isNarration">是否旁白（开场白）；旁白居中、无头像无名字</param>
    public TextConversationItem(bool isUser, bool isNarration = false)
    {
        _isUser = isUser;
        _isNarration = isNarration;
    }

    /// <summary>
    /// 追加一段流式增量
    /// </summary>
    /// <param name="delta">增量文本</param>
    public void Append(string delta)
    {
        lock (_bufferGate) _buffer.Append(delta);
        StreamFlushPump.Request(this);
    }

    /// <summary>
    /// 立即把缓冲同步到 <see cref="ConversationItemBase.Message"/>(段落收尾时调用)。
    /// 节拍器允许最后一次追加晚到一拍，收尾处必须显式冲刷，否则最后几个字会短暂缺失。
    ///
    /// <b>缓冲为空即不是流式条目</b>，此时什么都不做：旁白、后续报告这些是直接赋
    /// <see cref="ConversationItemBase.Message"/> 造出来的，一个字都没进过缓冲，
    /// 照冲不误就是把正文抹成空——表现为气泡当场变空白(后续报告原地替换时踩到过)。
    /// 流式条目在这里恒为非空:它的正文本来就只有缓冲这一个来源。
    /// </summary>
    public void Flush()
    {
        string text;
        // 取快照再赋值:赋值会引发绑定与布局,不该攥着锁做
        lock (_bufferGate) text = _buffer.ToString();
        if (text.Length == 0) return;
        Message = text;
    }

    /// <inheritdoc />
    void IStreamFlushTarget.FlushForDisplay() => Flush();

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

    /// <inheritdoc />
    public override void ReleaseImages()
    {
        // 先摘绑定再释放:集合清空会让 ItemsSource 收到通知、摘掉那些 Image,
        // 顺序反了就是把还在界面上的位图放掉
        Bitmap[] stale = MessageImages.ToArray();
        MessageImages.Clear();
        foreach (Bitmap image in stale) image.Dispose();

        OnPropertyChanged(nameof(HasImage));
        OnPropertyChanged(nameof(ImageThumbSize));
    }

    partial void OnInjectedTextChanged(string value)
    {
        OnPropertyChanged(nameof(HasInjectedText));
    }
}