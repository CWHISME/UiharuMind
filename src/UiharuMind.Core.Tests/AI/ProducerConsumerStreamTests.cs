using UiharuMind.Core.AI.Audio;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// 流式合成的边收边读：消费者缓冲区比块小时，剩下的字节必须先于后到的块读出，否则播出来是花的
/// </summary>
public class ProducerConsumerStreamTests
{
    [Fact]
    public async Task SmallReadBuffer_KeepsByteOrderAcrossChunks()
    {
        ProducerConsumerStream stream = new();
        await stream.WriteAsync(new byte[] { 1, 2, 3, 4, 5 });
        await stream.WriteAsync(new byte[] { 6, 7 });
        stream.Complete();

        List<byte> read = [];
        byte[] buffer = new byte[2];
        int count;
        while ((count = await stream.ReadAsync(buffer)) > 0) read.AddRange(buffer.Take(count));

        Assert.Equal([1, 2, 3, 4, 5, 6, 7], read);
    }

    [Fact]
    public async Task Fail_SurfacesAfterBufferedBytes()
    {
        ProducerConsumerStream stream = new();
        await stream.WriteAsync(new byte[] { 1 });
        stream.Fail(new InvalidOperationException("boom"));

        byte[] buffer = new byte[4];
        Assert.Equal(1, await stream.ReadAsync(buffer));
        await Assert.ThrowsAsync<InvalidOperationException>(() => stream.ReadAsync(buffer).AsTask());
    }

    [Fact]
    public void Dispose_NotifiesProducer()
    {
        bool stopped = false;
        new ProducerConsumerStream(() => stopped = true).Dispose();

        Assert.True(stopped);
    }
}
