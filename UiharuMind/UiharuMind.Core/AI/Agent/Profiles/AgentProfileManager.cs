/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using UiharuMind.Core.Core;
using UiharuMind.Core.Core.Singletons;

namespace UiharuMind.Core.AI.Agent.Profiles;

/// <summary>
/// 子 agent 档案管理器:持久化于单个 JSON 文件,内置识图档案不可删除
/// </summary>
public class AgentProfileManager : Singleton<AgentProfileManager>, IInitialize
{
    /// <summary>预置识图子 agent 档案标识</summary>
    public const string VisionProfileId = "vision";

    private const string SaveFileName = "AgentProfiles.json";

    private List<AgentProfile> _profiles = new();

    public void OnInitialize()
    {
        _profiles = SaveUtility.LoadRootFile<List<AgentProfile>>(SaveFileName) ?? new List<AgentProfile>();
        EnsureBuiltinProfiles();
    }

    /// <summary>
    /// 获取全部档案
    /// </summary>
    /// <returns>档案列表</returns>
    public List<AgentProfile> GetProfiles()
    {
        return new List<AgentProfile>(_profiles);
    }

    /// <summary>
    /// 新增或更新档案并持久化
    /// </summary>
    /// <param name="profile">档案</param>
    public void SaveProfile(AgentProfile profile)
    {
        int index = _profiles.FindIndex(x => x.ProfileId == profile.ProfileId);
        if (index >= 0) _profiles[index] = profile;
        else _profiles.Add(profile);
        Save();
    }

    /// <summary>
    /// 删除档案(内置档案不可删除)
    /// </summary>
    /// <param name="profileId">档案标识</param>
    /// <returns>是否删除成功</returns>
    public bool DeleteProfile(string profileId)
    {
        if (profileId is VisionProfileId) return false;
        int removed = _profiles.RemoveAll(x => x.ProfileId == profileId);
        if (removed > 0) Save();
        return removed > 0;
    }

    private void Save()
    {
        SaveUtility.SaveRootFile(SaveFileName, _profiles);
    }

    private void EnsureBuiltinProfiles()
    {
        if (_profiles.All(x => x.ProfileId != VisionProfileId))
        {
            _profiles.Add(new AgentProfile
            {
                ProfileId = VisionProfileId,
                DisplayName = "VisionAssistant",
                Description = "Answers questions about images. Delegate when the main model cannot see images.",
                SystemPrompt = "You are a vision assistant. Describe and answer questions about the given images " +
                               "accurately and concisely.",
                ExposeAsTool = true,
            });
            Save();
        }
    }
}
