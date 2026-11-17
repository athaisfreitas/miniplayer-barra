namespace MiniplayerBarra.Tests;

public class SingleInstanceTests
{
    [Fact]
    public void TryAcquire_Twice_SecondIsNull()
    {
        var name = "mb-test-" + Guid.NewGuid().ToString("N");
        using var first = SingleInstance.TryAcquire(name);
        Assert.NotNull(first);
        var second = Task.Run(() => SingleInstance.TryAcquire(name)).Result; // outra thread, como outro processo
        Assert.Null(second);
    }

    [Fact]
    public void TryAcquire_AfterDispose_Succeeds()
    {
        var name = "mb-test-" + Guid.NewGuid().ToString("N");
        SingleInstance.TryAcquire(name)!.Dispose();
        using var again = SingleInstance.TryAcquire(name);
        Assert.NotNull(again);
    }
}
