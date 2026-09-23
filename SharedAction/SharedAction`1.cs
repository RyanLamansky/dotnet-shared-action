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
/// Cancellation is the one exception, because it describes the caller that ran the action rather than the action itself.
/// In that case the action is handed to the next waiter.
/// </para>
/// </remarks>
public class SharedAction<TValue> : IDisposable
{
    private Workspace<TValue>? workspace;

    /// <summary>
    /// Provides a <see cref="Task{T}"/> of type <typeparamref name="TValue"/> that contains the result of processing.
    /// The outcome of a single call to <paramref name="valueFactory"/>, whether a result or an exception, is shared with all concurrent requestors.
    /// </summary>
    /// <param name="valueFactory">The function used to generate a value.</param>
    /// <returns>A task that, upon completion, provides the result of processing.</returns>
    public async Task<TValue> RunAsync(Func<Task<TValue>> valueFactory)
    {
        ArgumentNullException.ThrowIfNull(valueFactory);

        var workspace = GetOrCreateWorkspace();

        await workspace.WaitAsync().ConfigureAwait(false);

        if (!workspace.HasOutcome)
        {
            TValue result;

            try
            {
                result = await valueFactory().ConfigureAwait(false);
            }
            catch (Exception failure)
            {
                workspace.SetFailure(failure);
                Complete(workspace, outcomeStored: true);
                throw;
            }

            workspace.SetResult(result);

            Complete(workspace, outcomeStored: true);
        }

        return workspace.GetOutcome();
    }

    /// <summary>
    /// Provides a <see cref="Task{T}"/> of type <typeparamref name="TValue"/> that contains the result of processing.
    /// The outcome of a single call to <paramref name="valueFactory"/>, whether a result or an exception, is shared with all concurrent requestors.
    /// </summary>
    /// <param name="valueFactory">The function used to generate a value.</param>
    /// <param name="cancellationToken">
    /// If provided, can be used to trigger cancellation of the operation.
    /// It is used for both the internal semaphore and <paramref name="valueFactory"/>.
    /// </param>
    /// <returns>A task that, upon completion, provides the result of processing.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was triggered before completion.</exception>
    public async Task<TValue> RunAsync(Func<TValue> valueFactory, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(valueFactory);

        cancellationToken.ThrowIfCancellationRequested();

        var workspace = GetOrCreateWorkspace();

        await workspace.WaitAsync(cancellationToken).ConfigureAwait(false);

        if (!workspace.HasOutcome)
        {
            TValue result;

            try
            {
                result = valueFactory();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // This caller going away says nothing about the operation, so hand the work to the next waiter.
                Complete(workspace, outcomeStored: false);
                throw;
            }
            catch (Exception failure)
            {
                workspace.SetFailure(failure);
                Complete(workspace, outcomeStored: true);
                throw;
            }

            workspace.SetResult(result);

            Complete(workspace, outcomeStored: true);
        }

        return workspace.GetOutcome();
    }

    /// <summary>
    /// Provides a <see cref="Task{T}"/> of type <typeparamref name="TValue"/> that contains the result of processing.
    /// The outcome of a single call to <paramref name="valueFactory"/>, whether a result or an exception, is shared with all concurrent requestors.
    /// </summary>
    /// <param name="valueFactory">The function used to generate a value.</param>
    /// <param name="cancellationToken">
    /// If provided, can be used to trigger cancellation of the operation.
    /// It is used for both the internal semaphore and <paramref name="valueFactory"/>.
    /// </param>
    /// <returns>A task that, upon completion, provides the result of processing.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was triggered before completion.</exception>
    public async Task<TValue> RunAsync(Func<CancellationToken, Task<TValue>> valueFactory, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(valueFactory);

        cancellationToken.ThrowIfCancellationRequested();

        var workspace = GetOrCreateWorkspace();

        await workspace.WaitAsync(cancellationToken).ConfigureAwait(false);

        if (!workspace.HasOutcome)
        {
            TValue result;

            try
            {
                result = await valueFactory(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // This caller going away says nothing about the operation, so hand the work to the next waiter.
                Complete(workspace, outcomeStored: false);
                throw;
            }
            catch (Exception failure)
            {
                workspace.SetFailure(failure);
                Complete(workspace, outcomeStored: true);
                throw;
            }

            workspace.SetResult(result);

            Complete(workspace, outcomeStored: true);
        }

        return workspace.GetOutcome();
    }

    /// <summary>
    /// Provides a <see cref="Task{T}"/> of type <typeparamref name="TValue"/> that contains the result of processing.
    /// The outcome of a single call to <paramref name="valueFactory"/>, whether a result or an exception, is shared with all concurrent requestors.
    /// </summary>
    /// <param name="valueFactory">The function used to generate a value.</param>
    /// <param name="timeout">
    /// The amount of time to wait for the action to complete.
    /// If the time span is 0 or less, the wait time is unlimited.
    /// This is used to create a <see cref="CancellationToken"/> that is passed to <paramref name="valueFactory"/>.
    /// </param>
    /// <returns>A task that, upon completion, provides the result of processing.</returns>
    /// <exception cref="OperationCanceledException">The time limit from <paramref name="timeout"/> was reached before completion.</exception>
    public async Task<TValue> RunAsync(Func<CancellationToken, Task<TValue>> valueFactory, TimeSpan timeout)
    {
        using var timeToken = new CancellationTokenSource(timeout);

        return await RunAsync(valueFactory, timeToken.Token).ConfigureAwait(false);
    }

    /// <summary>
    /// Provides a <see cref="Task{T}"/> of type <typeparamref name="TValue"/> that contains the result of processing.
    /// The outcome of a single call to <paramref name="valueFactory"/>, whether a result or an exception, is shared with all concurrent requestors.
    /// </summary>
    /// <param name="valueFactory">The function used to generate a value.</param>
    /// <returns>A task that, upon completion, provides the result of processing.</returns>
    public TValue Run(Func<TValue> valueFactory) => Run(valueFactory, default);

    /// <summary>
    /// Provides a <see cref="Task{T}"/> of type <typeparamref name="TValue"/> that contains the result of processing.
    /// The outcome of a single call to <paramref name="valueFactory"/>, whether a result or an exception, is shared with all concurrent requestors.
    /// </summary>
    /// <param name="valueFactory">The function used to generate a value.</param>
    /// <param name="timeout">
    /// The amount of time to wait before entering the semaphore.
    /// If the time span is 0 (the default) or less, the wait time is unlimited.
    /// </param>
    /// <returns>A task that, upon completion, provides the result of processing.</returns>
    /// <exception cref="TimeoutException">The time limit indicated by <paramref name="timeout"/> has been exceeded.</exception>
    public TValue Run(Func<TValue> valueFactory, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(valueFactory);

        var workspace = GetOrCreateWorkspace();

        if (timeout.Ticks <= 0)
            timeout = TimeSpan.FromMilliseconds(-1);

        if (!workspace.Wait(timeout))
            throw new TimeoutException();

        if (!workspace.HasOutcome)
        {
            TValue result;

            try
            {
                result = valueFactory();
            }
            catch (Exception failure)
            {
                workspace.SetFailure(failure);
                Complete(workspace, outcomeStored: true);
                throw;
            }

            workspace.SetResult(result);

            Complete(workspace, outcomeStored: true);
        }

        return workspace.GetOutcome();
    }

    /// <summary>
    /// Gets the workspace shared by all current callers, creating it if there isn't one.
    /// </summary>
    private Workspace<TValue> GetOrCreateWorkspace()
    {
        var existing = Volatile.Read(ref workspace);

        if (existing is not null)
            return existing;

        var created = new Workspace<TValue>();

        existing = Interlocked.CompareExchange(ref workspace, created, null);

        if (existing is null)
            return created;

        created.Dispose();

        return existing;
    }

    /// <summary>
    /// Clears the shared workspace and wakes the callers waiting on it.
    /// </summary>
    /// <param name="workspace">The workspace to complete.</param>
    /// <param name="outcomeStored">
    /// When true, the workspace holds a result or a failure and every waiter is released at once.
    /// When false, exactly one waiter is released to run the action.
    /// Releasing more would exceed the semaphore's maximum count.
    /// </param>
    private void Complete(Workspace<TValue> workspace, bool outcomeStored)
    {
        // Clear only this workspace: a retrying waiter must not discard a newer one.
        _ = Interlocked.CompareExchange(ref this.workspace, null, workspace);

        _ = workspace.Release(outcomeStored ? int.MaxValue : 1);
    }

    /// <summary>
    /// Releases the unmanaged resources used by this instance, and optionally releases the managed resources.
    /// </summary>
    /// <param name="disposing">
    /// true to release both managed and unmanaged resources;
    /// false to release only unmanaged resources.
    /// </param>
    /// <remarks>
    /// The effects of disposal are limited--pending actions are cancelled but new ones can still be initiated.
    /// </remarks>
    protected virtual void Dispose(bool disposing)
    {
        if (!disposing)
            return;

        var workspace = Interlocked.Exchange(ref this.workspace, null);

        if (workspace is null)
            return;

        workspace.Dispose();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}
