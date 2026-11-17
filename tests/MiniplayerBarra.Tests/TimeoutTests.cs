using MiniplayerBarra.Media;
using Xunit;

namespace MiniplayerBarra.Tests;

public class TimeoutTests
{
    [Fact]
    public async Task Returns_result_when_task_completes()
    {
        Assert.Equal(5, await CallLimit.Run(Task.FromResult(5), TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task Throws_when_task_never_completes()
    {
        var never = new TaskCompletionSource<int>().Task;
        await Assert.ThrowsAsync<TimeoutException>(() => CallLimit.Run(never, TimeSpan.FromMilliseconds(50)));
    }
}
