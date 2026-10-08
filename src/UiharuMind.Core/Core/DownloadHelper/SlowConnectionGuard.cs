using System.Collections.Concurrent;
using System.Diagnostics;

namespace UiharuMind.Core.Core.DownloadHelper;

/// <summary>
/// 当前连接被判为慢连接，断开从已下位置重连
/// </summary>
internal sealed class SlowConnectionException() : Exception("Connection is much slower than this host has been.");

/// <summary>
/// 慢连接判定的参数
/// </summary>
/// <param name="Window">每隔多久量一次这条连接的速度</param>
/// <param name="Ratio">低于这个站点最快速度的多少算慢</param>
/// <param name="MinPeakBytesPerSecond">站点最快都没到这个数时不判（本来就慢的网络不折腾）</param>
/// <param name="MaxReconnects">同一块最多因慢重连几次</param>
internal sealed record SlowConnectionPolicy(
    TimeSpan Window,
    double Ratio,
    long MinPeakBytesPerSecond,
    int MaxReconnects)
{
    public static SlowConnectionPolicy Default { get; } = new(TimeSpan.FromSeconds(10), 0.25, 512 * 1024, 50);
}

/// <summary>
/// 长连接中途掉速的兜底：走代理或 CDN 时，同一时刻新开的连接常常比卡住的那条快得多（实测 0.8MB/s 对 5MB/s）。
/// 每条连接按窗口量速度，比这个站点见过的最快速度慢得多就重连；换了连接仍一样慢，说明是网络整体变慢，降基准、不再重连
/// </summary>
internal sealed class SlowConnectionGuard(SlowConnectionPolicy policy)
{
    private readonly ConcurrentDictionary<string, HostSpeed> _hosts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 给一条新连接开一个测速器
    /// </summary>
    /// <param name="host">站点</param>
    /// <param name="afterSlowReconnect">这条连接是不是因为慢才重连出来的</param>
    public ConnectionWatch Watch(string host, bool afterSlowReconnect)
    {
        return new ConnectionWatch(policy, _hosts.GetOrAdd(host, _ => new HostSpeed()), afterSlowReconnect);
    }

    public int MaxReconnects => policy.MaxReconnects;

    internal sealed class ConnectionWatch(SlowConnectionPolicy policy, HostSpeed host, bool afterSlowReconnect)
    {
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private long _windowBytes;
        private bool _isFirstWindow = true;

        /// <summary>
        /// 记一次收到的字节；窗口满了量一次速度，判为慢连接时抛 <see cref="SlowConnectionException"/>
        /// </summary>
        public void Add(int bytes)
        {
            _windowBytes += bytes;
            if (_clock.Elapsed < policy.Window) return;

            double speed = _windowBytes / _clock.Elapsed.TotalSeconds;
            bool slow = host.Judge(speed, policy, afterSlowReconnect && _isFirstWindow);
            _windowBytes = 0;
            _isFirstWindow = false;
            _clock.Restart();
            if (slow) throw new SlowConnectionException();
        }
    }

    internal sealed class HostSpeed
    {
        private readonly Lock _gate = new();
        private double _peak;

        // freshAfterSlow：重连后的第一个窗口仍然慢，就把它认作新的基准
        public bool Judge(double speed, SlowConnectionPolicy policy, bool freshAfterSlow)
        {
            lock (_gate)
            {
                if (speed >= _peak)
                {
                    _peak = speed;
                    return false;
                }

                if (_peak < policy.MinPeakBytesPerSecond || speed >= _peak * policy.Ratio) return false;
                if (!freshAfterSlow) return true;
                _peak = speed;
                return false;
            }
        }
    }
}
