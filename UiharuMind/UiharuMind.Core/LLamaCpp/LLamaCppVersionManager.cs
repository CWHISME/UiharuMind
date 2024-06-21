using System.Dynamic;
using AngleSharp;
using AngleSharp.Dom;
using UiharuMind.Core.Core;
using UiharuMind.Core.LLamaCpp.Versions;

namespace UiharuMind.Core.LLamaCpp;

public class LLamaCppVersionManager
{
    // private LLamaCppVersionItem
    //
    // public List<LLamaCppVersionItem> GetVersions()
    // {
    //     var versions = new List<LLamaCppVersionItem>();
    //     var version = new LLamaCppVersionItem();
    //     
    // }

    private VersionManager _versionManager;

    public LLamaCppVersionManager()
    {
        _versionManager = new VersionManager();

        var version1 = new VersionInfo("b3096");
        version1.AddBackendType(new BackendData());
        version1.AddBackendType(new BackendData());

        var version2 = new VersionInfo("b3097");
        version2.AddBackendType(new BackendData());
        var version3 = new VersionInfo("r3000");

        _versionManager.AddVersion(version1);
        _versionManager.AddVersion(version2);
        _versionManager.AddVersion(version3);
        _versionManager.Sort();
        // _versionManager.PreviewVersion();
    }

    public async Task<bool> GetLatestVersion()
    {
        var config = Configuration.Default.WithDefaultLoader();
        var context = BrowsingContext.New(config);
        var document = await context.OpenAsync("https://github.com/ggerganov/llama.cpp/releases/latest");
        LLamaCppVersionItem item = new LLamaCppVersionItem();

        // string versionName = Path.GetFileName(document.Location.PathName);
        var descDocumant = document.QuerySelector(".Box-body");
        if (descDocumant == null) return false;

        string? versionName = descDocumant.QuerySelector("h1")?.TextContent;
        if (versionName == null) return false;

        string releaseDate = descDocumant.QuerySelector("relative-time").GetAttribute("datetime");
        string updateDesc = descDocumant.QuerySelector("pre").TextContent;
        string assetCount = document.QuerySelector(".Box-footer .Counter").TextContent;
        _versionManager.GetOrCreateVersion(versionName);
            
        string assetPath = "https://github.com/ggerganov/llama.cpp/releases/expanded_assets/" + versionName;
        var assetsDocument = await context.OpenAsync(assetPath);
        var assetList = assetsDocument.QuerySelectorAll("li");
        foreach (var detail in assetList)
        {
            var detailList = detail.QuerySelectorAll("div");
            var link = detailList[0].QuerySelector("a");
            var name = link.TextContent.Trim();
            if (!name.StartsWith("llama-")) continue;
            var linkHref = "https://github.com" + link.GetAttribute("href");
            var size = detailList[1].QuerySelector("span")?.TextContent;
            Log.Debug($"{name} {size} {linkHref}");
        }

        return true;
    }
}

public class LLamaCppVersionItem
{
}