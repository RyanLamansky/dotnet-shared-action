using System.Diagnostics.CodeAnalysis;

namespace SharedHelpers;

/// <summary>
/// Shares the result of a single action among one or more concurrent requests.
/// The result is discarded when all concurrent waiters have received it.
/// </summary>
/// <typeparam name="TValue">The type of the value returned by the action.</typeparam>
/// <remarks>
/// Unlike <see cref="SharedAction{TKey, TValue}"/>, input variance is not handled internally.
/// Intended as a private member of an instance that can be shared by multiple threads, wrapped by a public API.
/// <para>
/// When the action fails, the exception is shared with every waiter and the action is not retried.
/// Concurrent requests get the same outcome, which is the point of the type.
/// </para>
/// <para>
/// A caller that cancels abandons only its own wait.
/// The action carries on for everyone still waiting, and is cancelled once the last of them has gone.
/// </para>
/// </remarks>
public class SharedAction<TValue> : IDisposable
{
    /// <summary>
    /// The action currently running, if there is one.
    /// </summary>
    private Attempt<TValue>? attempt;

    /// <summary>
    /// Provides a <see cref="Task{T}"/> of type <typeparamref name="TValue"/> that contains the result of processing.
    /// The outcome of a single call to <paramref name="valueFactory"/>, whether a result or an exception, is shared with all concurrent requestors.
    /// </summary>
    /// <param name="valueFactory">The function used to generate a value.</param>
    /// <returns>A task that, upon completion, provides the result of processing.</returns>
    public Task<TValue> RunAsync(Func<Task<TValue>> valueFactory)
    {
        ArgumentNullException.ThrowIfNull(valueFactory);

        if (TryStart(out var running))
            _ = LeadAsync(_ => valueFactory(), running);

        return AwaitAsync(running, default);
    }

    /// <summary>
    /// Provides a <see cref="Task{T}"/> of type <typeparamref name="TValue"/> that contains the result of processing.
    /// The outcome of a single call to <paramref name="valueFactory"/>, whether a result or an exception, is shared with all concurrent requestors.
    /// </summary>
    /// <param name="valueFactory">The function used to generate a value.</param>
    /// <param name="cancellationToken">
    /// If provided, abandons this call's wait for the action.
    /// The action itself continues for any other caller still waiting.
    /// </param>
    /// <returns>A task that, upon completion, provides the result of processing.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was triggered before completion.</exception>
    public Task<TValue> RunAsync(Func<TValue> valueFactory, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(valueFactory);

        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled<TValue>(cancellationToken);

        if (TryStart(out var running))
            Lead(valueFactory, running);

        return AwaitAsync(running, cancellationToken);
    }

    /// <summary>
    /// Provides a <see cref="Task{T}"/> of type <typeparamref name="TValue"/> that contains the result of processing.
    /// The outcome of a single call to <paramref name="valueFactory"/>, whether a result or an exception, is shared with all concurrent requestors.
    /// </summary>
    /// <param name="valueFactory">The function used to generate a value.</param>
    /// <param name="cancellationToken">
    /// If provided, abandons this call's wait for the action.
    /// The action itself continues for any other caller still waiting.
    /// </param>
    /// <returns>A task that, upon completion, provides the result of processing.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was triggered before completion.</exception>
    public Task<TValue> RunAsync(Func<CancellationToken, Task<TValue>> valueFactory, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(valueFactory);

        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled<TValue>(cancellationToken);

        if (TryStart(out var running))
            _ = LeadAsync(valueFactory, running);

        return AwaitAsync(running, cancellationToken);
    }

    /// <summary>
    /// Provides a <see cref="Task{T}"/> of type <typeparamref name="TValue"/> that contains the result of processing.
    /// The outcome of a single call to <paramref name="valueFactory"/>, whether a result or an exception, is shared with all concurrent requestors.
    /// </summary>
    /// <param name="valueFactory">The function used to generate a value.</param>
    /// <param name="timeout">
    /// The amount of time this call waits for the action to complete.
    /// If the time span is 0 or less, the wait time is unlimited.
    /// The action itself continues for any other caller still waiting.
    /// </param>
    /// <returns>A task that, upon completion, provides the result of processing.</returns>
    /// <exception cref="OperationCanceledException">The time limit from <paramref name="timeout"/> was reached before completion.</exception>
    public async Task<TValue> RunAsync(Func<CancellationToken, Task<TValue>> valueFactory, TimeSpan timeout)
    {
        using var expiry = new CancellationTokenSource(timeout.Ticks <= 0 ? Timeout.InfiniteTimeSpan : timeout);

        return await RunAsync(valueFactory, expiry.Token).ConfigureAwait(false);
    }

    /// <summary>
    /// Provides the result of processing.
    /// The outcome of a single call to <paramref name="valueFactory"/>, whether a result or an exception, is shared with all concurrent requestors.
    /// </summary>
    /// <param name="valueFactory">The function used to generate a value.</param>
    /// <returns>The result of processing.</returns>
    public TValue Run(Func<TValue> valueFactory) => Run(valueFactory, default);

    /// <summary>
    /// Provides the result of processing.
    /// The outcome of a single call to <paramref name="valueFactory"/>, whether a result or an exception, is shared with all concurrent requestors.
    /// </summary>
    /// <param name="valueFactory">The function used to generate a value.</param>
    /// <param name="timeout">
    /// The amount of time this call waits for the action to complete.
    /// If the time span is 0 (the default) or less, the wait time is unlimited.
    /// </param>
    /// <returns>The result of processing.</returns>
    /// <exception cref="TimeoutException">The time limit indicated by <paramref name="timeout"/> has been exceeded.</exception>
    /// <remarks>
    /// A caller that joins an action started by an asynchronous caller blocks its thread until that action completes.
    /// </remarks>
    public TValue Run(Func<TValue> valueFactory, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(valueFactory);

        if (TryStart(out var running))
            Lead(valueFactory, running);

        try
        {
            try
            {
                if (!running.Task.Wait(timeout.Ticks <= 0 ? Timeout.InfiniteTimeSpan : timeout))
                    throw new TimeoutException();
            }
            catch (AggregateException)
            {
                // The action failed, and its original exception is rethrown unwrapped below.
            }

            return running.Task.GetAwaiter().GetResult();
        }
        finally
        {
            running.Leave();
        }
    }

    /// <summary>
    /// Joins the action already running, or prepares a new one.
    /// </summary>
    /// <param name="running">The attempt this call waits on.</param>
    /// <returns>True when this caller must run the action itself.</returns>
    private bool TryStart(out Attempt<TValue> running)
    {
        while (true)
        {
            var existing = Volatile.Read(ref attempt);

            if (existing is not null)
            {
                if (existing.TryJoin())
                {
                    running = existing;
                    return false;
                }

                // Every caller gave up on that action, so retire it and look again.
                Remove(existing);
                continue;
            }

            var starting = new Attempt<TValue>();

            existing = Interlocked.CompareExchange(ref attempt, starting, null);

            if (existing is null)
            {
                running = starting;
                return true;
            }

            starting.Dispose();

            if (existing.TryJoin())
            {
                running = existing;
                return false;
            }

            Remove(existing);
        }
    }

    /// <summary>
    /// Waits for the shared outcome, leaving the attempt however this call departs.
    /// </summary>
    private static async Task<TValue> AwaitAsync(Attempt<TValue> running, CancellationToken cancellationToken)
    {
        try
        {
            return await (cancellationToken.CanBeCanceled ? running.Task.WaitAsync(cancellationToken) : running.Task).ConfigureAwait(false);
        }
        finally
        {
            running.Leave();
        }
    }

    /// <summary>
    /// Runs the action and publishes its outcome to every caller waiting on this attempt.
    /// </summary>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "The leader shares whatever the action produces, including any failure, with every caller waiting on it.")]
    private async Task LeadAsync(Func<CancellationToken, Task<TValue>> valueFactory, Attempt<TValue> running)
    {
        TValue result;

        try
        {
            result = await valueFactory(running.CancellationToken).ConfigureAwait(false);
        }
        catch (Exception failure)
        {
            Publish(running, failure);
            return;
        }

        Publish(running, result);
    }

    /// <inheritdoc cref="LeadAsync" />
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "The leader shares whatever the action produces, including any failure, with every caller waiting on it.")]
    private void Lead(Func<TValue> valueFactory, Attempt<TValue> running)
    {
        TValue result;

        try
        {
            result = valueFactory();
        }
        catch (Exception failure)
        {
            Publish(running, failure);
            return;
        }

        Publish(running, result);
    }

    private void Publish(Attempt<TValue> running, TValue result)
    {
        Remove(running);
        running.SetResult(result);
        running.ReleaseOwner();
    }

    private void Publish(Attempt<TValue> running, Exception failure)
    {
        Remove(running);
        running.SetFailure(failure);
        running.ReleaseOwner();
    }

    /// <summary>
    /// Clears this attempt, and only this one, before its outcome is published.
    /// A later caller then starts a new action instead of joining a finished one.
    /// </summary>
    private void Remove(Attempt<TValue> running) => _ = Interlocked.CompareExchange(ref attempt, null, running);

    /// <summary>
    /// Releases the unmanaged resources used by this instance, and optionally releases the managed resources.
    /// </summary>
    /// <param name="disposing">
    /// true to release both managed and unmanaged resources;
    /// false to release only unmanaged resources.
    /// </param>
    /// <remarks>
    /// The effects of disposal are limited--the running action is cancelled but new ones can still be initiated.
    /// </remarks>
    protected virtual void Dispose(bool disposing)
    {
        if (!disposing)
            return;

        var running = Interlocked.Exchange(ref attempt, null);

        running?.Abort();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}
