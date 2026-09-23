using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Text;

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

        if (!Debugger.IsAttached) // The debugger could slow things down too much.
            Assert.InRange(Stopwatch.GetElapsedTime(started), TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(1.5));

        Assert.StrictEqual(1, results.Count);
    }

    private sealed class ComparableMethod(MethodInfo info) : IEquatable<ComparableMethod>
    {
        public readonly MethodInfo Info = info;

        public override string ToString()
        {
            var builder = new StringBuilder();

            builder.Append(Info.ReturnType.Name).Append(' ').Append(Info.Name).Append('(');

            foreach (var parameter in Info.GetParameters())
                builder.Append(parameter.ParameterType.Name).Append(' ').Append(parameter.Name).Append(',').Append(' ');

            if (builder[^1] == ' ')
                builder.Length -= 2;

            builder.Append(')');

            return builder.ToString();
        }

        public bool Equals(ComparableMethod? other)
        {
            ArgumentNullException.ThrowIfNull(other);

            return this.ToString().Equals(other.ToString());
        }

        public override bool Equals(object? obj) => Equals(obj as ComparableMethod);

        public override int GetHashCode() => ToString().GetHashCode();
    }

    [Fact]
    public static void ApiConsistency()
    {
        var instanceMembers = typeof(SharedAction<,>)
            .GetMethods()
            .Where(m => m.Name.StartsWith("Run"))
            .Select(m => new ComparableMethod(m))
            .ToHashSet();

        var sharedMembers = typeof(SharedAction)
            .GetMethods()
            .Where(m => m.Name.StartsWith("Run"))
            .Select(m => new ComparableMethod(m))
            .ToHashSet();

        var missingMembers = instanceMembers.Except(sharedMembers).ToHashSet();

        Assert.Empty(missingMembers);
    }

    [Fact]
    public static async Task SyncCallerJoinsAsyncActionAsync()
    {
        using var shared = new SharedAction<int, int>();

        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;

        var leader = shared.RunAsync(0, async (int _, CancellationToken cancellationToken) =>
        {
            _ = Interlocked.Increment(ref calls);

            started.TrySetResult();

            await Task.Delay(200, cancellationToken).ConfigureAwait(false);

            return 7;
        });

        await started.Task;

        // A synchronous caller blocks on the action that an asynchronous caller started.
        var joined = await Task.Run(() => shared.Run(0, _ =>
        {
            _ = Interlocked.Increment(ref calls);

            return 99;
        })).WaitAsync(TimeSpan.FromSeconds(30));

        Assert.StrictEqual(7, joined);
        Assert.StrictEqual(7, await leader);
        Assert.StrictEqual(1, calls);
    }

    [Fact]
    public static void SyncCallerSeesTheOriginalFailure()
    {
        using var shared = new SharedAction<int, int>();

        // Blocking on a task surfaces failures wrapped, so the original must be unwrapped for the caller.
        _ = Assert.Throws<InvalidOperationException>(
            () => shared.Run(0, _ => throw new InvalidOperationException("backend down")));
    }
}
