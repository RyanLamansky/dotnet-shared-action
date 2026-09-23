using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace SharedHelpers;

/// <summary>
/// Shares the result of a single action among one or more concurrent requests.
/// The result is discarded when all concurrent waiters have received it.
/// </summary>
/// <typeparam name="TKey">The type that describes the input to the action.</typeparam>
/// <typeparam name="TValue">The type of the value returned by the action.</typeparam>
/// <param name="comparer">If provided, enables customized comparison of <typeparamref name="TKey"/> values.</param>
/// <remarks>
/// In general, instances of this type should be reused as long as shared action duplicate inputs are possible.
/// For example, web applications using this to share API results should store the instance in a static readonly field.
/// <para>
/// When the action fails, the exception is shared with every waiter and the action is not retried.
/// Concurrent requests get the same outcome, which is the point of the type.
/// </para>
/// <para>
/// A caller that cancels abandons only its own wait.
/// The action carries on for everyone still waiting, and is cancelled once the last of them has gone.
/// </para>
/// </remarks>
public class SharedAction<TKey, TValue>(IEqualityComparer<TKey>? comparer = null)
    : IDisposable
    where TKey : notnull
{
    /// <summary>
    /// Tracks actions that are currently running. When null, the object is disposed.
    /// </summary>
    private ConcurrentDictionary<TKey, Attempt<TValue>>? attempts = new(comparer);

    /// <summary>
    /// Provides a <see cref="Task{T}"/> of type <typeparamref name="TValue"/> that contains the result of processing the input.
    /// The outcome of a single call to <paramref name="valueFactory"/>, whether a result or an exception, is shared with all concurrent requestors with the same input.
    /// </summary>
    /// <param name="input">The input to the processing logic.</param>
    /// <param name="valueFactory">The function used to generate a value for the input.</param>
    /// <returns>A task that, upon completion, provides the result of processing.</returns>
    public Task<TValue> RunAsync(TKey input, Func<TKey, Task<TValue>> valueFactory)
    {
        ArgumentNullException.ThrowIfNull(valueFactory);

        if (TryStart(input, out var attempt))
            _ = LeadAsync(input, (key, _) => valueFactory(key), attempt);

        return AwaitAsync(attempt, default);
    }

    /// <summary>
    /// Provides a <see cref="Task{T}"/> of type <typeparamref name="TValue"/> that contains the result of processing the input.
    /// The outcome of a single call to <paramref name="valueFactory"/>, whether a result or an exception, is shared with all concurrent requestors with the same input.
    /// </summary>
    /// <param name="input">The input to the processing logic.</param>
    /// <param name="valueFactory">The function used to generate a value for the input.</param>
    /// <param name="cancellationToken">
    /// If provided, abandons this call's wait for the action.
    /// The action itself continues for any other caller still waiting.
    /// </param>
    /// <returns>A task that, upon completion, provides the result of processing.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was triggered before completion.</exception>
    public Task<TValue> RunAsync(TKey input, Func<TKey, TValue> valueFactory, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(valueFactory);

        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled<TValue>(cancellationToken);

        if (TryStart(input, out var attempt))
            Lead(input, valueFactory, attempt);

        return AwaitAsync(attempt, cancellationToken);
    }

    /// <summary>
    /// Provides a <see cref="Task{T}"/> of type <typeparamref name="TValue"/> that contains the result of processing the input.
    /// The outcome of a single call to <paramref name="valueFactory"/>, whether a result or an exception, is shared with all concurrent requestors with the same input.
    /// </summary>
    /// <param name="input">The input to the processing logic.</param>
    /// <param name="valueFactory">The function used to generate a value for the input.</param>
    /// <param name="cancellationToken">
    /// If provided, abandons this call's wait for the action.
    /// The action itself continues for any other caller still waiting.
    /// </param>
    /// <returns>A task that, upon completion, provides the result of processing.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was triggered before completion.</exception>
    public Task<TValue> RunAsync(TKey input, Func<TKey, CancellationToken, Task<TValue>> valueFactory, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(valueFactory);

        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled<TValue>(cancellationToken);

        if (TryStart(input, out var attempt))
            _ = LeadAsync(input, valueFactory, attempt);

        return AwaitAsync(attempt, cancellationToken);
    }

    /// <summary>
    /// Provides a <see cref="Task{T}"/> of type <typeparamref name="TValue"/> that contains the result of processing the input.
    /// The outcome of a single call to <paramref name="valueFactory"/>, whether a result or an exception, is shared with all concurrent requestors with the same input.
    /// </summary>
    /// <param name="input">The input to the processing logic.</param>
    /// <param name="valueFactory">The function used to generate a value for the input.</param>
    /// <param name="timeout">
    /// The amount of time this call waits for the action to complete.
    /// If the time span is 0 or less, the wait time is unlimited.
    /// The action itself continues for any other caller still waiting.
    /// </param>
    /// <returns>A task that, upon completion, provides the result of processing.</returns>
    /// <exception cref="OperationCanceledException">The time limit from <paramref name="timeout"/> was reached before completion.</exception>
    public async Task<TValue> RunAsync(TKey input, Func<TKey, CancellationToken, Task<TValue>> valueFactory, TimeSpan timeout)
    {
        using var expiry = new CancellationTokenSource(timeout.Ticks <= 0 ? Timeout.InfiniteTimeSpan : timeout);

        return await RunAsync(input, valueFactory, expiry.Token).ConfigureAwait(false);
    }

    /// <summary>
    /// Provides the result of processing the input.
    /// The outcome of a single call to <paramref name="valueFactory"/>, whether a result or an exception, is shared with all concurrent requestors with the same input.
    /// </summary>
    /// <param name="input">The input to the processing logic.</param>
    /// <param name="valueFactory">The function used to generate a value for the input.</param>
    /// <returns>The result of processing.</returns>
    public TValue Run(TKey input, Func<TKey, TValue> valueFactory) => Run(input, valueFactory, default);

    /// <summary>
    /// Provides the result of processing the input.
    /// The outcome of a single call to <paramref name="valueFactory"/>, whether a result or an exception, is shared with all concurrent requestors with the same input.
    /// </summary>
    /// <param name="input">The input to the processing logic.</param>
    /// <param name="valueFactory">The function used to generate a value for the input.</param>
    /// <param name="timeout">
    /// The amount of time this call waits for the action to complete.
    /// If the time span is 0 (the default) or less, the wait time is unlimited.
    /// </param>
    /// <returns>The result of processing.</returns>
    /// <exception cref="TimeoutException">The time limit indicated by <paramref name="timeout"/> has been exceeded.</exception>
    /// <remarks>
    /// A caller that joins an action started by an asynchronous caller blocks its thread until that action completes.
    /// </remarks>
    public TValue Run(TKey input, Func<TKey, TValue> valueFactory, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(valueFactory);

        if (TryStart(input, out var attempt))
            Lead(input, valueFactory, attempt);

        try
        {
            try
            {
                if (!attempt.Task.Wait(timeout.Ticks <= 0 ? Timeout.InfiniteTimeSpan : timeout))
                    throw new TimeoutException();
            }
            catch (AggregateException)
            {
                // The action failed, and its original exception is rethrown unwrapped below.
            }

            return attempt.Task.GetAwaiter().GetResult();
        }
        finally
        {
            attempt.Leave();
        }
    }

    /// <summary>
    /// Joins the action already running for this input, or prepares a new one.
    /// </summary>
    /// <param name="input">The input to the processing logic.</param>
    /// <param name="attempt">The attempt this call waits on.</param>
    /// <returns>True when this caller must run the action itself.</returns>
    private bool TryStart(TKey input, out Attempt<TValue> attempt)
    {
        var attempts = GetAttemptsOrThrowDisposedException();

        while (true)
        {
            // Joining an action that is already running costs nothing but the lookup.
            if (attempts.TryGetValue(input, out var running))
            {
                if (running.TryJoin())
                {
                    attempt = running;
                    return false;
                }

                // Every caller gave up on that action, so retire it and look again.
                Remove(input, running);
                continue;
            }

            var starting = new Attempt<TValue>();
            var current = attempts.GetOrAdd(input, starting);

            if (ReferenceEquals(current, starting))
            {
                attempt = starting;
                return true;
            }

            starting.Dispose();

            if (current.TryJoin())
            {
                attempt = current;
                return false;
            }

            Remove(input, current);
        }
    }

    /// <summary>
    /// Waits for the shared outcome, leaving the attempt however this call departs.
    /// </summary>
    private static async Task<TValue> AwaitAsync(Attempt<TValue> attempt, CancellationToken cancellationToken)
    {
        try
        {
            return await (cancellationToken.CanBeCanceled ? attempt.Task.WaitAsync(cancellationToken) : attempt.Task).ConfigureAwait(false);
        }
        finally
        {
            attempt.Leave();
        }
    }

    /// <summary>
    /// Runs the action and publishes its outcome to every caller waiting on this attempt.
    /// </summary>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "The leader shares whatever the action produces, including any failure, with every caller waiting on it.")]
    private async Task LeadAsync(TKey input, Func<TKey, CancellationToken, Task<TValue>> valueFactory, Attempt<TValue> attempt)
    {
        TValue result;

        try
        {
            result = await valueFactory(input, attempt.CancellationToken).ConfigureAwait(false);
        }
        catch (Exception failure)
        {
            Publish(input, attempt, failure);
            return;
        }

        Publish(input, attempt, result);
    }

    /// <inheritdoc cref="LeadAsync" />
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "The leader shares whatever the action produces, including any failure, with every caller waiting on it.")]
    private void Lead(TKey input, Func<TKey, TValue> valueFactory, Attempt<TValue> attempt)
    {
        TValue result;

        try
        {
            result = valueFactory(input);
        }
        catch (Exception failure)
        {
            Publish(input, attempt, failure);
            return;
        }

        Publish(input, attempt, result);
    }

    private void Publish(TKey input, Attempt<TValue> attempt, TValue result)
    {
        Remove(input, attempt);
        attempt.SetResult(result);
        attempt.ReleaseOwner();
    }

    private void Publish(TKey input, Attempt<TValue> attempt, Exception failure)
    {
        Remove(input, attempt);
        attempt.SetFailure(failure);
        attempt.ReleaseOwner();
    }

    private void Remove(TKey input, Attempt<TValue> attempt)
    {
        // Remove only this attempt, and only before its outcome is published.
        // A later caller then starts a new action instead of joining a finished one.
        var attempts = this.attempts;

        if (attempts is not null)
            _ = attempts.TryRemove(new KeyValuePair<TKey, Attempt<TValue>>(input, attempt));
    }

    private ConcurrentDictionary<TKey, Attempt<TValue>> GetAttemptsOrThrowDisposedException()
        => attempts ?? throw new ObjectDisposedException(nameof(SharedAction<TKey, TValue>));

    /// <summary>
    /// Releases the unmanaged resources used by this instance, and optionally releases the managed resources.
    /// </summary>
    /// <param name="disposing">
    /// true to release both managed and unmanaged resources;
    /// false to release only unmanaged resources.
    /// </param>
    /// <remarks>
    /// This cancels any action that is still running.
    /// </remarks>
    protected virtual void Dispose(bool disposing)
    {
        if (!disposing)
            return;

        var attempts = Interlocked.Exchange(ref this.attempts, null);

        if (attempts is null)
            return;

        foreach (var attempt in attempts.Values)
            attempt.Abort();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}
