/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using UiharuMind.Shared.Utils;

namespace UiharuMind.App.Tests.Shared;

/// <summary>
/// 设置写回闸门。落盘动作在这里是个计数器，所以整组测试不碰配置、不碰单例、不落一个字节到盘上。
///
/// 值得钉住的是「回填期间静默」这条：坏掉的表现是打开设置页就把配置原样重写一遍，
/// 看起来什么都没变，实际上每次进页面都在写盘——安静得没人会发现。
/// </summary>
public class SettingsWriteBackTests
{
    /// <summary>不在回填里：改一次存一次</summary>
    [Fact]
    public void Save_OutsideLoad_PersistsEveryTime()
    {
        int saves = 0;
        SettingsWriteBack writeBack = new(() => saves++);

        writeBack.Save();
        writeBack.Save();

        Assert.Equal(2, saves);
        Assert.False(writeBack.IsLoading);
    }

    /// <summary>回填期间：一次都不落盘</summary>
    [Fact]
    public void Save_DuringLoad_IsSilent()
    {
        int saves = 0;
        SettingsWriteBack writeBack = new(() => saves++);

        using (writeBack.BeginLoad())
        {
            Assert.True(writeBack.IsLoading);
            writeBack.Save();
            writeBack.Save();
        }

        Assert.Equal(0, saves);
        Assert.False(writeBack.IsLoading);
    }

    /// <summary>回填结束后恢复：这一次该存，而且只存一次</summary>
    [Fact]
    public void Save_AfterLoadEnds_PersistsOnce()
    {
        int saves = 0;
        SettingsWriteBack writeBack = new(() => saves++);

        using (writeBack.BeginLoad())
        {
            writeBack.Save();
        }

        writeBack.Save();

        Assert.Equal(1, saves);
    }

    /// <summary>嵌套回填：内层退出还不算完，得等最外层退出</summary>
    [Fact]
    public void NestedLoad_OnlyOutermostExitRestoresSaving()
    {
        int saves = 0;
        SettingsWriteBack writeBack = new(() => saves++);

        using (writeBack.BeginLoad())
        {
            using (writeBack.BeginLoad())
            {
                writeBack.Save();
            }

            Assert.True(writeBack.IsLoading); //内层退出了，外层还在
            writeBack.Save();
        }

        Assert.Equal(0, saves);

        writeBack.Save();
        Assert.Equal(1, saves);
    }

    /// <summary>同一个凭据释放两次只算一次，别把深度扣穿成负数</summary>
    [Fact]
    public void DisposingTwice_DoesNotUnbalanceDepth()
    {
        int saves = 0;
        SettingsWriteBack writeBack = new(() => saves++);

        IDisposable outer = writeBack.BeginLoad();
        IDisposable inner = writeBack.BeginLoad();
        inner.Dispose();
        inner.Dispose();

        Assert.True(writeBack.IsLoading); //外层还在，重复释放没把它一起带走
        writeBack.Save();
        Assert.Equal(0, saves);

        outer.Dispose();
        writeBack.Save();
        Assert.Equal(1, saves);
    }

    /// <summary>落盘动作是延迟调用的：只建闸门不该碰到任何配置</summary>
    [Fact]
    public void Constructing_DoesNotInvokeSaveAction()
    {
        int saves = 0;
        _ = new SettingsWriteBack(() => saves++);

        Assert.Equal(0, saves);
    }
}
