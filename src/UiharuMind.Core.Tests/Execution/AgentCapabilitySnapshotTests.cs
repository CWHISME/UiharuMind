/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Execution;
using Xunit;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 纯提示词档（角色扮演/工具人）的固定开销快照。它让普通对话在<b>没有会话</b>
/// 的空态也能报出「这段对话固定占多少」——能力面板对智能体外的档位不再是一片空白。
/// </summary>
public class AgentCapabilitySnapshotTests
{
    private static CharacterData RoleplayCharacter()
    {
        DefaultCharacterManager.Instance.OnInitialize();
        return DefaultCharacterManager.Instance.All["Assistant"];
    }

    [Fact]
    public void FromRoleplay_ReportsOnlyCharacterSegment()
    {
        AgentCapabilitySnapshot snapshot = AgentCapabilitySnapshot.FromRoleplay(RoleplayCharacter());

        // 只有角色段——工具/工作区/技能/MCP 那些档本就与角色扮演无关
        Assert.Single(snapshot.PromptSegments);
        AgentPromptSegment segment = snapshot.PromptSegments[0];
        Assert.Equal(EPromptSection.Character, segment.Section);
        Assert.False(string.IsNullOrWhiteSpace(segment.Text));
    }

    [Fact]
    public void FromRoleplay_CharacterSegmentCountsTowardTotal()
    {
        AgentCapabilitySnapshot snapshot = AgentCapabilitySnapshot.FromRoleplay(RoleplayCharacter());

        // 角色段每轮完整重发，是纯提示词档唯一的固定开销，必须计入合计
        int characterTokens = snapshot.PromptTokensOf(EPromptSection.Character);
        Assert.True(characterTokens > 0, "角色提示词应能估出 token 数");
        Assert.Equal(snapshot.EstimatedTokens, characterTokens);
    }
}