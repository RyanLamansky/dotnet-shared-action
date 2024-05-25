using System.Collections.Concurrent;
using System.Diagnostics;

namespace SharedHelpers;

public static class SharedActionTests
{
    private static Func<TKey, TValue> Delay<TKey, TValue>(int millisecondsDelay, TValue value, ConcurrentBag<TValue> results) => new(_ =>
    {
        var thread = new Thread(() =>
        {
            Thread.Sleep(millisecondsDelay);
            results.Add(value);

        });

        thread.Start();

        thread.Join();

        return value;
    });

    [Fact]
    public static void TwoThreadsOneRun()
    {
        var shared = new SharedAction<int, int>();

        var started = Stopwatch.GetTimestamp();
        var results = new ConcurrentBag<int>();

        var thread1 = new Thread(() => shared.Run(0, Delay<int, int>(1000, 1, results)));
        var thread2 = new Thread(() => shared.Run(0, Delay<int, int>(1000, 2, results)));

        thread1.Start();
        thread2.Start();

        thread1.Join();
        thread2.Join();

        var elapsed = Stopwatch.GetElapsedTime(started);

        Assert.InRange(elapsed, TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(1.5));
        Assert.StrictEqual(1, results.Count);
    }
}