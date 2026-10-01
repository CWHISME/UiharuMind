using UiharuMind.Core.AI.Execution.Assembly;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 按服务端报的数校准估算：只往上调、带死区、没量过不算、换模型归位。
/// 每一条都对着一种会把压缩提早或推迟的失效
/// </summary>
public class TurnInputEstimateTests
{
    private static TurnInputEstimate Measured(long history) => new() { LastHistory = history };

    [Fact]
    public void Calibrate_TakesTheServerToOursRatio()
    {
        TurnInputEstimate estimate = Measured(1000);

        estimate.Calibrate("m", 1500);

        Assert.Equal(1.5, estimate.Calibration, 3);
    }

    /// <summary>少报的服务端（GLM 不计工具定义）不能把估算往下拖：晚压的代价远大于早压</summary>
    [Fact]
    public void Calibrate_NeverScalesDown()
    {
        TurnInputEstimate estimate = Measured(1000);

        estimate.Calibrate("m", 480);

        Assert.Equal(1, estimate.Calibration);
    }

    /// <summary>偏离不到死区不动：系数每发微调的话，台阶里要腾的量跟着动，前缀就不稳了</summary>
    [Fact]
    public void Calibrate_IgnoresSmallDrift_ButFollowsARealShift()
    {
        TurnInputEstimate estimate = Measured(1000);
        estimate.Calibrate("m", 1500);

        estimate.Calibrate("m", 1530);
        Assert.Equal(1.5, estimate.Calibration, 3);

        estimate.Calibrate("m", 1200);
        Assert.Equal(1.2, estimate.Calibration, 3);
    }

    /// <summary>历史估算为 0（框架跳过了压缩判定）的那一发只估了固定开销，服务端却连交接文档一起算，比值虚高</summary>
    [Fact]
    public void Calibrate_SkipsRequestsWhoseHistoryWasNotMeasured()
    {
        TurnInputEstimate estimate = Measured(0);

        estimate.Calibrate("m", 5000);

        Assert.Equal(1, estimate.Calibration);
    }

    [Fact]
    public void Calibrate_IsCappedAgainstAbsurdRatios()
    {
        TurnInputEstimate estimate = Measured(1000);

        estimate.Calibrate("m", 100_000);

        Assert.Equal(4, estimate.Calibration);
    }

    /// <summary>系数属于那一个模型：agent 不随换模型重建，轮次开头就得归位</summary>
    [Fact]
    public void UseModel_ResetsTheCalibrationOnlyWhenTheModelChanges()
    {
        TurnInputEstimate estimate = Measured(1000);
        estimate.Calibrate("deepseek", 1500);

        estimate.UseModel("deepseek");
        Assert.Equal(1.5, estimate.Calibration, 3);

        estimate.UseModel("glm");
        Assert.Equal(1, estimate.Calibration);
    }
}
