using System.Dynamic;
using AngleSharp;
using AngleSharp.Dom;

namespace UiharuMind.Core.LLamaCpp;

public class LLamaCppVersion
{
    // private LLamaCppVersionItem
    //
    // public List<LLamaCppVersionItem> GetVersions()
    // {
    //     var versions = new List<LLamaCppVersionItem>();
    //     var version = new LLamaCppVersionItem();
    //     
    // }

    public async Task<LLamaCppVersionItem> GetLatestVersion()
    {
        var config = Configuration.Default.WithDefaultLoader();
        var context = BrowsingContext.New(config);
        var document = await context.OpenAsync("https://github.com/ggerganov/llama.cpp/releases");
        LLamaCppVersionItem item = new LLamaCppVersionItem();
        var titleElement = document.QuerySelector("title");
        if (titleElement != null)
        {
            Console.WriteLine($"Page Title: {titleElement.TextContent}");
        }

        return item;
    }
}

public class LLamaCppVersionItem
{
}