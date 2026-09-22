
namespace SharedHelpers;

/// <summary>
/// Covers what happens when the shared action does not complete normally.
/// The value factory throws, or the caller that happens to lead the operation goes away.
/// </summary>
public static class SharedActionFailureTests
{
    /// <summary>
    /// Blocks the first caller inside the value factory so later callers are queued before the leader finishes.
    /// </summary>
    private sealed class Gate
    {
        private readonly TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Entered => entered.Task;

        public void SignalEntered() => entered.TrySetResult();

        public Task Wait() => release.Task;

        public void Release() => release.TrySetResult();
    }

    private static void RecordPeak(ref int peak, int value)
    {
        int seen;

        do
        {
            seen = Volatile.Read(ref peak);

            if (value <= seen)
                return;
        }
        while (Interlocked.CompareExchange(ref peak, value, seen) != seen);
    }

    [Fact]
    public static async Task FailureIsSharedWithAllWaitersAsync()
    {
        using var shared = new SharedAction<int, int>();

        var gate = new Gate();
        var calls = 0;

        async Task<int> Factory(int input, CancellationToken cancellationToken)
        {
            _ = Interlocked.Increment(ref calls);

            gate.SignalEntered();

            await gate.Wait().ConfigureAwait(false);

            throw new InvalidOperationException("backend down");
        }

        var leader = shared.RunAsync(0, Factory);

        await gate.Entered;

        var followers = Enumerable.Range(0, 4).Select(_ => shared.RunAsync(0, Factory)).ToArray();

        await Task.Delay(250); // Let the followers queue on the workspace.

        gate.Release();

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => leader);

        // Everyone asking for the same thing at the same time gets the same outcome.
        foreach (var follower in followers)
            Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => follower));

        Assert.StrictEqual(1, calls);
    }

    [Fact]
    public static async Task CancellationIsNotSharedAsync()
    {
        using var shared = new SharedAction<int, int>();

        var gate = new Gate();
        var calls = 0;
        var active = 0;
        var peak = 0;

        async Task<int> Factory(int input, CancellationToken cancellationToken)
        {
            RecordPeak(ref peak, Interlocked.Increment(ref active));

            try
            {
                if (Interlocked.Increment(ref calls) == 1)
                {
                    gate.SignalEntered();

                    // The leader waits until its own caller goes away.
                    await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
                }

                return 42;
            }
            finally
            {
                _ = Interlocked.Decrement(ref active);
            }
        }

        // Only the leader goes away, simulating a disconnected HTTP client.
        using var leaderCancellation = new CancellationTokenSource();

        var leader = shared.RunAsync(0, Factory, leaderCancellation.Token);

        await gate.Entered;

        var followers = Enumerable.Range(0, 3).Select(_ => shared.RunAsync(0, Factory)).ToArray();

        await Task.Delay(250); // Let the followers queue on the workspace.

        await leaderCancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => leader);

        // A caller that cancelled nothing must still get a real answer.
        // One caller's cancellation describes that caller's request, not the outcome of the shared operation.
        foreach (var follower in followers)
            Assert.StrictEqual(42, await follower);

        // Exactly one waiter picked the work back up, and never alongside another.
        Assert.StrictEqual(2, calls);
        Assert.StrictEqual(1, peak);
    }

    [Fact]
    public static async Task ValueOnlyActionNeverRunsFactoryConcurrentlyAsync()
    {
        const int Rounds = 500;
        const int Callers = 4;

        var peak = 0;

        for (var round = 0; round < Rounds; round++)
        {
            using var shared = new SharedAction<int>();

            var active = 0;

            async Task<int> Factory()
            {
                RecordPeak(ref peak, Interlocked.Increment(ref active));

                try
                {
                    await Task.Delay(1).ConfigureAwait(false);

                    return 1;
                }
                finally
                {
                    _ = Interlocked.Decrement(ref active);
                }
            }

            using var barrier = new Barrier(Callers);

            var callers = Enumerable.Range(0, Callers).Select(_ => Task.Run(async () =>
            {
                barrier.SignalAndWait();

                _ = await shared.RunAsync(Factory).ConfigureAwait(false);
            })).ToArray();

            await Task.WhenAll(callers);
        }

        // A caller arriving after the action finished starts a new one, which is correct rather than a duplicate.
        // What must never happen is two callers running the action at the same time.
        Assert.StrictEqual(1, peak);
    }
}
