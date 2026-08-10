using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Singletons;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.AI.Character;

/// <summary>
/// 可插入提示词框的一段现成文本。<b>只在编辑期用</b>——插进去之后就是角色自己的文本，
/// 运行期没有任何机制引用它，因此它不需要角色卡那二十多个字段、存档目录、档位与图标。
/// </summary>
public class PromptSnippet
{
    /// <summary>片段名(插入菜单里显示的那行)</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>片段正文</summary>
    public string Text { get; set; } = string.Empty;
}

/// <summary>
/// 提示词片段库。整库一个 json，首次运行用内置预设(角色扮演第一/第三人称脚手架)播种，
/// 播下去之后就是普通数据——用户能改能删，删光了不会再长回来。
///
/// 刻意<b>不是角色</b>：曾用两个 <c>IsInternal</c> 的内置角色充当脚手架文本来源，
/// 那让角色再次兼职当文本片段，而"谁是角色、谁是片段"正是角色域上一轮花力气拆开的东西。
/// </summary>
public class PromptSnippetManager : Singleton<PromptSnippetManager>, IInitialize
{
    private const string FileName = "PromptSnippets.json";

    /// <summary>内置预设的嵌入资源名(首次运行播种用)</summary>
    private const string SeedResourceName = "PromptSnippets.json";

    /// <summary>智能体工作循环那条内置片段的名字</summary>
    public const string WorkLoopSnippetName = "智能体工作循环";

    private List<PromptSnippet> _snippets = new();

    /// <summary>当前片段库(按加入顺序，界面直接展示)</summary>
    public IReadOnlyList<PromptSnippet> Snippets => _snippets;

    private static string SavePath => Path.Combine(SettingConfig.SaveDataPath, FileName);

    public void OnInitialize()
    {
        if (File.Exists(SavePath))
        {
            _snippets = SaveUtility.Load<List<PromptSnippet>>(SavePath) ?? new List<PromptSnippet>();
            return;
        }

        // 首次运行:把内置预设落盘一份,此后它与用户自建的片段没有区别
        try
        {
            _snippets = EmbeddedResourcesUtils.ReadFromJson<List<PromptSnippet>>(SeedResourceName);
            // 工作循环那条不写在资源里:它的正文由 AgentToolPrompts.AgentWorkLoop 定,
            // 新建智能体时也用同一份预填,抄两份迟早走样
            _snippets.Add(new PromptSnippet
            {
                Name = WorkLoopSnippetName,
                Text = AgentToolPrompts.AgentWorkLoop,
            });
            Save();
        }
        catch (Exception e)
        {
            Log.Warning($"Seed prompt snippets failed: {e.Message}");
            _snippets = new List<PromptSnippet>();
        }
    }

    /// <summary>
    /// 新增一个片段。同名视为覆盖——「存为片段」用同一个名字再存一次就是更新它。
    /// </summary>
    /// <param name="name">片段名；为空则忽略</param>
    /// <param name="text">片段正文；为空则忽略</param>
    public void AddOrUpdate(string? name, string? text)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(text)) return;

        string trimmed = name.Trim();
        PromptSnippet? existing = _snippets.Find(x => string.Equals(x.Name, trimmed, StringComparison.Ordinal));
        if (existing != null) existing.Text = text;
        else _snippets.Add(new PromptSnippet { Name = trimmed, Text = text });

        Save();
    }

    /// <summary>
    /// 删除一个片段
    /// </summary>
    /// <param name="snippet">片段</param>
    public void Remove(PromptSnippet snippet)
    {
        if (_snippets.Remove(snippet)) Save();
    }

    private void Save()
    {
        SaveUtility.Save(SavePath, _snippets);
    }
}
