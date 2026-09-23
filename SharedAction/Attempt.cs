namespace SharedHelpers;

/// <summary>
/// One run of a shared action, together with the callers currently waiting for its outcome.
/// </summary>
internal sealed class Attempt<TValue> : IDisposable
{
    private readonly TaskCompletionSource<TValue> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly CancellationTokenSource cancellation = new();

    /// <summary>
    /// The callers still interested in this attempt, starting at one for the caller that created it.
    /// Reaching zero is permanent, so a later caller starts a fresh attempt instead of joining a doomed one.
    /// </summary>
    private int waiters = 1;

    /// <summary>
    /// The work itself and the waiting callers as a group, each of which must finish before the cancellation source can be released.
    /// </summary>
    private int owners = 2;

    internal Task<TValue> Task => completion.Task;

    /// <summary>
    /// Cancelled once every caller has given up, so the action is not paid for after nobody wants it.
    /// </summary>
    internal CancellationToken CancellationToken => cancellation.Token;

    /// <summary>
    /// Adds a caller to this attempt, unless everyone has already given up on it.
    /// </summary>
    internal bool TryJoin()
    {
        var current = Volatile.Read(ref waiters);

        while (current > 0)
        {
            var seen = Interlocked.CompareExchange(ref waiters, current + 1, current);

            if (seen == current)
                return true;

            current = seen;
        }

        return false;
    }

    /// <summary>
    /// Removes a caller from this attempt, cancelling the action when the last one leaves.
    /// </summary>
    internal void Leave()
    {
        if (Interlocked.Decrement(ref waiters) != 0)
            return;

        cancellation.Cancel();

        ReleaseOwner();
    }

    /// <summary>
    /// Publishes a result to every caller still waiting.
    /// </summary>
    internal void SetResult(TValue result) => completion.TrySetResult(result);

    /// <summary>
    /// Publishes a failure to every caller still waiting, so that one run produces one outcome.
    /// </summary>
    internal void SetFailure(Exception failure)
    {
        if (failure is OperationCanceledException canceled)
            _ = completion.TrySetCanceled(canceled.CancellationToken);
        else
            _ = completion.TrySetException(failure);

        // The last caller may already have left, and an unawaited failure would surface as unobserved.
        _ = completion.Task.Exception;
    }

    /// <summary>
    /// Cancels the action without waiting for its callers, used when the owning instance is disposed.
    /// </summary>
    internal void Abort()
    {
        try
        {
            cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The attempt already finished and released its cancellation source.
        }
    }

    /// <summary>
    /// Releases one of the two owners, disposing the cancellation source once neither can reach it.
    /// </summary>
    internal void ReleaseOwner()
    {
        if (Interlocked.Decrement(ref owners) == 0)
            cancellation.Dispose();
    }

    /// <summary>
    /// Discards an attempt that was built speculatively and never shared.
    /// </summary>
    public void Dispose() => cancellation.Dispose();
}
