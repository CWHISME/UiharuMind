/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Collections.Concurrent;
using System.Text.Json;
using PuppeteerSharp;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Execution.Tools.Browser;

/// <summary>
/// 浏览器里的下载：不落 Chrome 默认的 ~/Downloads（工作区外写盘、不经审批，模型也不知道存哪了），
/// 先按 guid 存进中转目录，下完挪进发起它的那个会话的草稿目录，并把路径告诉那个会话。
/// 下载行为是整个浏览器一份，几个会话共用，所以靠事件里的 frameId 认是谁的下载。
/// </summary>
internal sealed class PuppeteerDownloads
{
    private readonly string _staging;
    private readonly Func<string, PuppeteerTab?> _findTab; //frameId → 标签页
    private readonly ConcurrentDictionary<string, Pending> _pending = new(); //guid → 进行中的下载

    private PuppeteerDownloads(string staging, Func<string, PuppeteerTab?> findTab)
    {
        _staging = staging;
        _findTab = findTab;
    }

    /// <summary>
    /// 接管浏览器的下载
    /// </summary>
    /// <param name="browser">浏览器</param>
    /// <param name="staging">中转目录</param>
    /// <param name="findTab">按 frameId 找发起下载的标签页；认不出时给个兜底（最近用过的）或 null</param>
    /// <returns>接管成功的实例；失败为 null（下载照 Chrome 默认走，不影响别的）</returns>
    public static async Task<PuppeteerDownloads?> EnableAsync(IBrowser browser, string staging,
        Func<string, PuppeteerTab?> findTab)
    {
        try
        {
            Directory.CreateDirectory(staging);
            PuppeteerDownloads downloads = new(staging, findTab);
            ICDPSession session = await browser.Target.CreateCDPSessionAsync();
            session.MessageReceived += (_, e) => downloads.OnMessage(e.MessageID, e.MessageData);
            await session.SendAsync("Browser.setDownloadBehavior", new Dictionary<string, object>
            {
                ["behavior"] = "allowAndName",
                ["downloadPath"] = staging,
                ["eventsEnabled"] = true,
            });
            return downloads;
        }
        catch (Exception e) when (e is PuppeteerException or IOException or UnauthorizedAccessException)
        {
            Log.Warning($"[Browser] taking over downloads failed, Chrome's default applies: {e.Message}");
            return null;
        }
    }

    private void OnMessage(string method, JsonElement data)
    {
        switch (method)
        {
            case "Browser.downloadWillBegin":
                string guid = Text(data, "guid");
                PuppeteerTab? tab = _findTab(Text(data, "frameId"));
                string fileName = SafeFileName(Text(data, "suggestedFilename"));
                _pending[guid] = new Pending(tab, fileName);
                tab?.DownloadStarted(guid, fileName);
                break;
            case "Browser.downloadProgress":
                string state = Text(data, "state");
                if (state is "completed" or "canceled" && _pending.TryRemove(Text(data, "guid"), out Pending? done))
                {
                    Finish(Text(data, "guid"), done, state == "completed");
                }

                break;
        }
    }

    private void Finish(string guid, Pending download, bool completed)
    {
        string staged = Path.Combine(_staging, guid);
        string note;
        try
        {
            if (!completed)
            {
                if (File.Exists(staged)) File.Delete(staged);
                note = $"The download of \"{download.FileName}\" was cancelled or failed.";
            }
            else if (download.Tab is not { DownloadDirectory.Length: > 0 } tab)
            {
                Log.Warning($"[Browser] download \"{download.FileName}\" has no session to go to; left at {staged}");
                return;
            }
            else
            {
                Directory.CreateDirectory(tab.DownloadDirectory);
                string path = UniquePath(tab.DownloadDirectory, download.FileName);
                File.Move(staged, path);
                note = $"Downloaded \"{download.FileName}\" to {path}.";
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warning($"[Browser] moving download \"{download.FileName}\" failed: {e.Message}");
            note = $"The download of \"{download.FileName}\" finished but could not be saved: {e.Message}";
        }

        download.Tab?.DownloadEnded(guid, note);
    }

    // 重名不覆盖：同一会话下两次同名文件，后者加序号（与 Chrome 的「(1)」同一习惯）
    private static string UniquePath(string directory, string fileName)
    {
        string path = Path.Combine(directory, fileName);
        string stem = Path.GetFileNameWithoutExtension(fileName);
        string extension = Path.GetExtension(fileName);
        for (int i = 1; File.Exists(path); i++) path = Path.Combine(directory, $"{stem} ({i}){extension}");
        return path;
    }

    // 文件名来自网站：只留最后一段、去掉非法字符，免得带着 ../ 写出目录
    private static string SafeFileName(string suggested)
    {
        string name = Path.GetFileName(suggested.Replace('\\', '/'));
        name = new string(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray()).Trim(' ', '.');
        return name.Length > 0 ? name : "download";
    }

    private static string Text(JsonElement data, string name) =>
        data.ValueKind == JsonValueKind.Object && data.TryGetProperty(name, out JsonElement value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private sealed record Pending(PuppeteerTab? Tab, string FileName);
}
