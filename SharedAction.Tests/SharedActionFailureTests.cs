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

    /// <summary>
    /// Fails the test rather than hanging the whole run when a shared action never settles.
    /// </summary>
    private static Task OrTimeout(Task task) => task.WaitAsync(TimeSpan.FromSeconds(30));

    /// <inheritdoc cref="OrTimeout(Task)" />
    private static Task<T> OrTimeout<T>(Task<T> task) => task.WaitAsync(TimeSpan.FromSeconds(30));

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

        await OrTimeout(gate.Entered);

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
    public static async Task AbandonedWaiterDoesNotDisturbTheActionAsync()
    {
        using var shared = new SharedAction<int, int>();

        var gate = new Gate();
        var calls = 0;

        async Task<int> Factory(int input, CancellationToken cancellationToken)
        {
            _ = Interlocked.Increment(ref calls);

            gate.SignalEntered();

            await gate.Wait().ConfigureAwait(false);

            return 42;
        }

        // The caller that starts the action is the one that has waited longest,
        // so it is also the one most likely to give up first.
        using var leaverCancellation = new CancellationTokenSource();

        var leaver = shared.RunAsync(0, Factory, leaverCancellation.Token);

        await OrTimeout(gate.Entered);

        var stayers = Enumerable.Range(0, 3).Select(_ => shared.RunAsync(0, Factory)).ToArray();

        await Task.Delay(250); // Let the other callers join the action.

        await leaverCancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => OrTimeout(leaver));

        gate.Release();

        // The action carries on for the callers that are still waiting, and is never restarted.
        foreach (var stayer in stayers)
            Assert.StrictEqual(42, await OrTimeout(stayer));

        Assert.StrictEqual(1, calls);
    }

    [Fact]
    public static async Task ActionIsCancelledWhenEveryWaiterAbandonsAsync()
    {
        using var shared = new SharedAction<int, int>();

        var gate = new Gate();
        var cancelledInsideFactory = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<int> Factory(int input, CancellationToken cancellationToken)
        {
            gate.SignalEntered();

            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                cancelledInsideFactory.TrySetResult();
                throw;
            }

            return 42;
        }

        var sources = Enumerable.Range(0, 3).Select(_ => new CancellationTokenSource()).ToArray();

        try
        {
            var first = shared.RunAsync(0, Factory, sources[0].Token);

            await OrTimeout(gate.Entered);

            var rest = sources.Skip(1).Select(s => shared.RunAsync(0, Factory, s.Token)).ToArray();

            await Task.Delay(250); // Let the other callers join the action.

            foreach (var source in sources)
                await source.CancelAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => OrTimeout(first));

            foreach (var caller in rest)
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => OrTimeout(caller));

            // Nobody is left to want the result, so the work itself is shed rather than run to completion.
            await cancelledInsideFactory.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            foreach (var source in sources)
                source.Dispose();
        }
    }

    [Fact]
    public static async Task LoneCallerCancellingStopsTheActionAsync()
    {
        using var shared = new SharedAction<int, int>();

        var gate = new Gate();
        var cancelledInsideFactory = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<int> Factory(int input, CancellationToken cancellationToken)
        {
            gate.SignalEntered();

            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                cancelledInsideFactory.TrySetResult();
                throw;
            }

            return 42;
        }

        using var only = new CancellationTokenSource();

        var caller = shared.RunAsync(0, Factory, only.Token);

        await OrTimeout(gate.Entered);

        await only.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => OrTimeout(caller));

        // Under light load most inputs have a single interested caller, so this is the ordinary case.
        // One caller leaving is the last one leaving, and the action must stop with it.
        await OrTimeout(cancelledInsideFactory.Task);
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
