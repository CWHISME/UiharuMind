using System.Dynamic;
using AngleSharp;
using AngleSharp.Dom;
using UiharuMind.Core.AI.LocalAI.LLamaCpp.Configs;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.LLamaCpp.Versions;

namespace UiharuMind.Core.LLamaCpp;

public class LLamaCppVersionManager
{
    private readonly VersionManager _versionManager = new VersionManager();

    /// <summary>
    /// 获取目录中本地引擎版本信息
    /// </summary>
    /// <param name="path"></param>
    /// <returns></returns>
    public async Task<VersionManager> GetLocalVersions(string path)
    {
        await Task.Run(() =>
        {
            foreach (var item in _versionManager.VersionsList)
            {
                item.IsDownloaded = false;
            }

            foreach (string versionDir in Directory.EnumerateDirectories(path))
            {
                string versionName = Path.GetFileName(versionDir);
                var version = _versionManager.GetOrCreateVersion(versionName);
                version.AddBackendType(new LLamaCppRuntimeEngine(versionName, versionDir));
                //判断 versionDir 下是否有可执行文件
                foreach (var file in Directory.EnumerateFiles(versionDir, LLamaCppSettingConfig.ServerExeName + "*",
                             SearchOption.AllDirectories))
                {
                    version.ExecutablePath = Path.GetDirectoryName(file)!;
                    break;
                }

                if (!string.IsNullOrEmpty(version.ExecutablePath)) version.IsDownloaded = true;
            }

            _versionManager.RemoveAllNotLoadedVersions();
            _versionManager.Sort();
        });

        return _versionManager;
    }

    /// <summary>
    /// 拉取最新版本列表(注：会包含本地已有的版本)
    /// </summary>
    /// <returns></returns>
    public async Task<VersionManager> GetLatestVersion(string path)
    {
        var config = Configuration.Default.WithDefaultLoader();
        var context = BrowsingContext.New(config);
        var document = await context.OpenAsync("https://github.com/ggerganov/llama.cpp/releases/latest");

        // string versionName = Path.GetFileName(document.Location.PathName);
        var descDocumant = document.QuerySelector(".Box-body");
        if (descDocumant == null) return _versionManager;

        string? versionName = descDocumant.QuerySelector("h1")?.TextContent;
        if (versionName == null) return _versionManager;

        string? releaseDate = descDocumant.QuerySelector("relative-time")?.GetAttribute("datetime");
        string? updateDesc = descDocumant.QuerySelector("pre")?.TextContent;
        // string assetCount = document.QuerySelector(".Box-footer .Counter").TextContent;
        // _versionManager.GetOrCreateVersion(versionName);

        if (releaseDate != null)
        {
            try
            {
                DateTimeOffset dateTimeOffset = DateTimeOffset.Parse(releaseDate);

                // 转换为本地时间
                DateTime dateTime = dateTimeOffset.LocalDateTime;

                // 格式化日期
                releaseDate = dateTime.ToString("yyyy-MM-dd HH:mm:ss");
            }
            catch (Exception e)
            {
                Log.Error(e.Message);
            }
        }

        _versionManager.ReleaseDate = $"Release Date: {releaseDate} \n\n{updateDesc}";

        string assetPath = "https://github.com/ggerganov/llama.cpp/releases/expanded_assets/" + versionName;
        var assetsDocument = await context.OpenAsync(assetPath);
        var assetList = assetsDocument.QuerySelectorAll("li");
        foreach (var detail in assetList)
        {
            var detailList = detail.QuerySelectorAll("div");
            var link = detailList[0].QuerySelector("a");
            var name = link?.TextContent.Trim();
            if (name == null || !name.StartsWith("llama-")) continue;
            var linkHref = "https://github.com" + link?.GetAttribute("href");
            // var size = detailList[1].QuerySelector("span")?.TextContent;
            // Log.Debug($"{name} {size} {linkHref}");
            var version = _versionManager.GetOrCreateVersion(Path.GetFileNameWithoutExtension(name));
            if (version.IsDownloaded) continue;
            // version.DownloadSize = size;
            version.DownloadUrl = linkHref;
            version.ExecutablePath = Path.Combine(path, version.VersionNumber);
        }

        _versionManager.Sort();
        return _versionManager;
    }
}