using UiharuMind.Core.Input.Linux;
using Xunit;

namespace UiharuMind.Core.Tests.Input;

public class LinuxDesktopMetricsTests
{
    [Fact]
    public void Update_IgnoresNonPositiveValues()
    {
        LinuxDesktopMetrics.Update(2560, 1440);
        LinuxDesktopMetrics.Update(0, -1);

        Assert.Equal(2560, LinuxDesktopMetrics.Width);
        Assert.Equal(1440, LinuxDesktopMetrics.Height);
    }
}
