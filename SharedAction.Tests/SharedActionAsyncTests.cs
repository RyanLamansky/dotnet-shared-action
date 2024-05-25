using System.Collections.Concurrent;
using System.Diagnostics;

namespace SharedHelpers;

public static class SharedActionAsyncTests
{
    private async static Task<T> DelayAsync<T>(int millisecondsDelay, T value, ConcurrentBag<T> results)
    {
        using (var delay = Task.Delay(millisecondsDelay))
        {
            await delay;
        }

        results.Add(value);
        return value;
    }

    [Fact]
    public static async Task TwoAwaitsOneRunAsync()
    {
        var shared = new SharedAction<int, int>();

        var started = Stopwatch.GetTimestamp();
        var results = new ConcurrentBag<int>();

        await Task.WhenAll(
            shared.RunAsync(0, _ => DelayAsync(1000, 1, results)),
            shared.RunAsync(0, _ => DelayAsync(1000, 2, results))
            );

        var elapsed = Stopwatch.GetElapsedTime(started);

        Assert.InRange(elapsed, TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(1.5));
        Assert.StrictEqual(1, results.Count);
    }
}