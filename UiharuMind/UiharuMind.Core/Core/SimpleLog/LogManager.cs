using System.Text.Json;

namespace UiharuMind.Core.Core.SimpleLog;

public class LogManager
{
    private static LogManager? _instance;
    private static readonly object Locker = new object();
    private static SpinLock _spinLocker = new SpinLock();

    public static LogManager Instance
    {
        get
        {
            if (_instance == null)
                lock (Locker)
                {
                    if (_instance == null) _instance = new LogManager();
                    return _instance;
                }

            return _instance;
        }
    }

    private List<LogItem> _logItems = new List<LogItem>(30);

    public List<LogItem> LogItems
    {
        get { return _logItems; }
    }

    /// <summary>
    /// 日志改变事件，bool 表示是否是错误信息，可以进行后续处理如是否弹窗提示
    /// </summary>
    public event Action<LogItem>? OnLogChange;

    public ILogger? Logger;

    public void Log(string str)
    {
        Logger?.Debug(str, AddLog(ELogType.Log, str));
    }

    public void LogWarning(string str)
    {
        Logger?.Warning(str, AddLog(ELogType.Warning, str));
    }

    public void LogError(string str)
    {
        Logger?.Error(str, AddLog(ELogType.Error, str));
    }

    public void SaveLog(string path)
    {
        if (!Directory.Exists(path)) Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "Log.txt"),
            JsonSerializer.Serialize(LogItems, new JsonSerializerOptions() { WriteIndented = true }));
    }

    public void ClearLog()
    {
        bool islock = false;
        _spinLocker.Enter(ref islock);
        _logItems.Clear();
        OnLogChange?.Invoke(new LogItem(ELogType.Log, "Clear Log!"));
        _spinLocker.Exit();
    }

    private LogItem AddLog(ELogType type, string str)
    {
        bool islock = false;
        _spinLocker.Enter(ref islock);
        LogItem item = new LogItem(type, str);
        _logItems.Add(item);
        OnLogChange?.Invoke(item);
        _spinLocker.Exit();
        // Console.WriteLine(item.ToString());
        return item;
    }
}