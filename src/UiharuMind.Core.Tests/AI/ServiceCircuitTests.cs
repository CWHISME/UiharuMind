using System.Net;
using UiharuMind.Core.AI.Net;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// 熔断记账。用例之间靠<b>各用各的服务名</b>隔离——记账是进程级静态的,
/// 清空它会踩到并行跑的其它用例。
/// </summary>
public class ServiceCircuitTests
{
    [Fact]
    public void ConsecutiveFailures_TripTheCircuit()
    {
        const string service = "test-trip";

        for (int i = 0; i < ServiceCircuit.FailureThreshold - 1; i++)
        {
            ServiceCircuit.RecordFailure(service);
            Assert.False(ServiceCircuit.IsTripped(service, out _));
        }

        ServiceCircuit.RecordFailure(service);

        Assert.True(ServiceCircuit.IsTripped(service, out TimeSpan remaining));
        Assert.InRange(remaining, TimeSpan.Zero, ServiceCircuit.OpenDuration);
    }

    [Fact]
    public void Success_ResetsFailureStreak()
    {
        const string service = "test-reset";

        for (int i = 0; i < ServiceCircuit.FailureThreshold - 1; i++) ServiceCircuit.RecordFailure(service);
        ServiceCircuit.RecordSuccess(service);
        ServiceCircuit.RecordFailure(service);

        Assert.False(ServiceCircuit.IsTripped(service, out _));
    }

    [Fact]
    public void Success_ClosesAnOpenCircuit()
    {
        const string service = "test-recover";

        for (int i = 0; i < ServiceCircuit.FailureThreshold; i++) ServiceCircuit.RecordFailure(service);
        Assert.True(ServiceCircuit.IsTripped(service, out _));

        ServiceCircuit.RecordSuccess(service);

        Assert.False(ServiceCircuit.IsTripped(service, out _));
    }

    [Fact]
    public void UnknownService_IsNeverTripped()
    {
        Assert.False(ServiceCircuit.IsTripped("test-never-touched", out TimeSpan remaining));
        Assert.Equal(TimeSpan.Zero, remaining);
    }

    /// <summary>限流/鉴权/5xx/超时是服务级故障,换个 URL 也一样,该熔断</summary>
    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    public void ServiceLevelFailures_AreCounted(HttpStatusCode code)
    {
        Assert.True(ServiceCircuit.IsServiceLevelFailure(new HttpRequestException("x", null, code)));
    }

    [Fact]
    public void TimeoutAndConnectFailures_AreCounted()
    {
        Assert.True(ServiceCircuit.IsServiceLevelFailure(new TaskCanceledException()));
        Assert.True(ServiceCircuit.IsServiceLevelFailure(new HttpRequestException("dns failed")));
    }

    /// <summary>
    /// 单个 URL 读不了是这个 URL 的事。要是也算进熔断,三个坏链接就能把整条通路停掉五分钟。
    /// </summary>
    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Gone)]
    [InlineData(HttpStatusCode.BadRequest)]
    public void PerUrlFailures_AreNotCounted(HttpStatusCode code)
    {
        Assert.False(ServiceCircuit.IsServiceLevelFailure(new HttpRequestException("x", null, code)));
    }
}
