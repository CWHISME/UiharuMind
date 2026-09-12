/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using Microsoft.Extensions.AI;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Execution;

/// <summary>
/// 一个会话的实时内容分岔口：跑这一轮的 <see cref="TurnDriver"/> 把内容交给它，
/// 它转发给本轮的渲染落点<b>以及所有挂在这个会话上的观察者</b>。
///
/// 存在的理由：内容流只有一条（框架产出的那条），但看它的人可以不止一个。
/// 从前子代理那一轮的落点只攒报告，于是打开着的子会话窗口拿不到任何实时内容，
/// 只能等按服务调用粒度落盘的历史——工具结果因此要晚<b>整整一次模型调用</b>才出现
/// （它是下一次调用的请求消息，随那次落盘，见 <c>SessionChatHistoryProvider</c>）。
///
/// <b>它不是第二份真相</b>：转发的就是那一条流本身，观察者与驱动者看到的是同一串内容。
/// </summary>
public sealed class LiveTurnStream
{
    /// <param name="Sink">落点</param>
    /// <param name="Identity">去重身份：与本轮驱动落点同一个对象时不再转发（否则同一个界面渲染两遍）</param>
    private readonly record struct Observer(ITurnSink Sink, object Identity);

    private readonly object _gate = new();
    private readonly List<Observer> _observers = new(); //随窗口来去,比一轮长命
    private readonly List<AIContent> _pending = new(); //本轮已产出、尚未落盘的那一段
    private ITurnSink? _primary; //本轮的驱动落点(没有观察者时它就是全部)
    private bool _turnRunning;

    /// <summary>这个会话此刻有没有一轮正在往外流内容</summary>
    public bool IsTurnRunning
    {
        get
        {
            lock (_gate) return _turnRunning;
        }
    }

    /// <summary>
    /// 挂一个观察者上来。<b>中途挂上来也补得齐</b>：本轮已产出、尚未落盘的那一段会当场补发给它
    /// ——已落盘的那部分观察者自己从历史读得到，补发会重复。
    /// </summary>
    /// <param name="sink">观察者的渲染落点（跨线程的 marshal 由它自己负责）</param>
    /// <param name="identity">
    /// 去重身份。同一个界面既可能是本轮的驱动者、又挂着观察，两者若是同一份渲染落点
    /// （通常是包了一层线程 marshal 的同一个转录器），传它的内核对象，转发时会跳过。
    /// 省略则以 <paramref name="sink"/> 自身为身份。
    /// </param>
    /// <returns>摘钩子</returns>
    public IDisposable Observe(ITurnSink sink, object? identity = null)
    {
        Observer observer = new(sink, identity ?? sink);
        List<AIContent> backlog;
        lock (_gate)
        {
            _observers.Add(observer);
            // 在锁内取快照:同一时刻的 Apply 要么已经进了这份 backlog、要么会看到这个观察者,
            // 两条路各自都不会漏也不会重(见 Apply 的取快照顺序)
            backlog = _turnRunning && !IsSelf(observer) ? new List<AIContent>(_pending) : new List<AIContent>();
        }

        foreach (AIContent content in backlog)
        {
            SafeApply(observer.Sink, content);
        }

        return new Subscription(this, observer);
    }

    /// <summary>
    /// 开一轮。驱动者在轮首调用，<see cref="Scope"/> 释放时收尾。
    /// </summary>
    /// <param name="primary">本轮的驱动落点；无头执行没有渲染落点，传 null</param>
    /// <returns>本轮作用域，其 <see cref="Scope.Sink"/> 即驱动者该用的落点</returns>
    public Scope BeginTurn(ITurnSink? primary)
    {
        lock (_gate)
        {
            _primary = primary;
            _pending.Clear();
            _turnRunning = true;
        }

        return new Scope(this);
    }

    /// <summary>
    /// 历史已落盘到此为止。<b>待补发的那一段就此清空</b>——它的定义正是「已产出但历史里还没有」，
    /// 落了盘的部分中途挂上来的观察者从历史读得到，留着只会让它渲染两遍。
    /// 顺带把本轮缓冲的内存钉在一次服务调用的量级上。
    /// </summary>
    public void NoteHistoryPersisted()
    {
        lock (_gate) _pending.Clear();
    }

    private bool IsSelf(Observer observer) => _primary != null && ReferenceEquals(_primary, observer.Identity);

    /// <summary>取本轮落点的快照：驱动者，加上除它自己之外的观察者</summary>
    private (ITurnSink? Primary, Observer[] Observers) Snapshot()
    {
        lock (_gate)
        {
            return (_primary, _observers.Where(x => !IsSelf(x)).ToArray());
        }
    }

    private static void SafeApply(ITurnSink sink, AIContent content) => Safe(() => sink.Apply(content));

    /// <summary>观察者炸了不能带倒这一轮：它只是个看客</summary>
    private static void Safe(Action action)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            Log.Warning($"Live turn observer failed: {e.Message}");
        }
    }

    private void Remove(Observer observer)
    {
        lock (_gate) _observers.Remove(observer);
    }

    /// <summary>本轮的转发落点：先喂驱动者，再喂观察者</summary>
    private sealed class Fanout : ITurnSink
    {
        private readonly LiveTurnStream _owner;

        public Fanout(LiveTurnStream owner) => _owner = owner;

        public void Apply(AIContent content)
        {
            //先入缓冲再取快照:此刻挂上来的观察者要么收到补发、要么落进这份快照,不会既漏又重
            lock (_owner._gate) _owner._pending.Add(content);

            (ITurnSink? primary, Observer[] observers) = _owner.Snapshot();
            primary?.Apply(content);
            foreach (Observer observer in observers)
            {
                SafeApply(observer.Sink, content);
            }
        }

        public void CloseSegment() => ForEach(x => x.CloseSegment());

        public void StopRunningToolCalls(string note) => ForEach(x => x.StopRunningToolCalls(note));

        /// <summary>
        /// 落库用的正文只认<b>驱动者</b>那一份：观察者的条目不进任何人的历史。
        /// 仍要对观察者走一遍，因为它同时是「收尾当前流段」。
        /// </summary>
        public string? TakeStreamingText()
        {
            (ITurnSink? primary, Observer[] observers) = _owner.Snapshot();
            foreach (Observer observer in observers)
            {
                Safe(() => observer.Sink.TakeStreamingText());
            }

            return primary?.TakeStreamingText();
        }

        private void ForEach(Action<ITurnSink> action)
        {
            (ITurnSink? primary, Observer[] observers) = _owner.Snapshot();
            if (primary != null) action(primary);
            foreach (Observer observer in observers)
            {
                Safe(() => action(observer.Sink));
            }
        }
    }

    /// <summary>一轮的作用域：<see cref="Sink"/> 是驱动者该用的落点，释放即轮末收尾</summary>
    public sealed class Scope : IDisposable
    {
        private readonly LiveTurnStream _owner;

        internal Scope(LiveTurnStream owner)
        {
            _owner = owner;
            Sink = new Fanout(owner);
        }

        /// <summary>本轮的落点（驱动者的落点 + 全部观察者）</summary>
        public ITurnSink Sink { get; }

        public void Dispose()
        {
            lock (_owner._gate)
            {
                _owner._turnRunning = false;
                _owner._primary = null;
                _owner._pending.Clear();
            }
        }
    }

    private sealed class Subscription : IDisposable
    {
        private readonly LiveTurnStream _owner;
        private readonly Observer _observer;
        private bool _disposed;

        public Subscription(LiveTurnStream owner, Observer observer)
        {
            _owner = owner;
            _observer = observer;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _owner.Remove(_observer);
        }
    }
}
