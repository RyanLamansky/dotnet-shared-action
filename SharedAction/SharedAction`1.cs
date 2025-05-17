namespace SharedHelpers;

/// <summary>
/// Shares the result of a single action among one or more concurrent requests.
/// The result is discarded when all concurrent waiters have received it.
/// </summary>
/// <typeparam name="TValue">The type of the value returned by the action.</typeparam>
/// <remarks>
/// Unlike <see cref="SharedAction{TKey, TValue}"/>, input variance is not handled internally.
/// Intended as a private member of an instance that can be shared by multiple threads, wrapped by a public API.
/// </remarks>
public class SharedAction<TValue> : IDisposable
{
    private Workspace<TValue>? workspace;

    /// <summary>
    /// Provides a <see cref="Task{T}"/> of type <typeparamref name="TValue"/> that contains the result of processing.
    /// The results of the first successful call to <paramref name="valueFactory"/> are shared with all concurrent requestors.
    /// </summary>
    /// <param name="valueFactory">The function used to generate a value.</param>
    /// <returns>A task that, upon completion, provides the result of processing.</returns>
    public async Task<TValue> RunAsync(Func<Task<TValue>> valueFactory)
    {
#if NET7_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(valueFactory);
#else
        if (valueFactory is null)
            throw new ArgumentNullException(nameof(valueFactory));
#endif

        var workspace = this.workspace ??= new();

        await workspace.WaitAsync().ConfigureAwait(false);

        if (!workspace.HasResult)
        {
            try
            {
                workspace.Result = await valueFactory().ConfigureAwait(false);
            }
            finally
            {
                this.workspace = null;
                workspace.Release(int.MaxValue);
            }
        }

        return workspace.Result;
    }

    /// <summary>
    /// Provides a <see cref="Task{T}"/> of type <typeparamref name="TValue"/> that contains the result of processing.
    /// The results of the first successful call to <paramref name="valueFactory"/> are shared with all concurrent requestors.
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
#if NET7_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(valueFactory);
#else
        if (valueFactory is null)
            throw new ArgumentNullException(nameof(valueFactory));
#endif

        cancellationToken.ThrowIfCancellationRequested();

        var workspace = this.workspace ??= new();

        await workspace.WaitAsync(cancellationToken).ConfigureAwait(false);

        if (!workspace.HasResult)
        {
            try
            {
                workspace.Result = valueFactory();
            }
            finally
            {
                this.workspace = null;
                workspace.Release(int.MaxValue);
            }
        }

        return workspace.Result;
    }

    /// <summary>
    /// Provides a <see cref="Task{T}"/> of type <typeparamref name="TValue"/> that contains the result of processing.
    /// The results of the first successful call to <paramref name="valueFactory"/> are shared with all concurrent requestors.
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
#if NET7_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(valueFactory);
#else
        if (valueFactory is null)
            throw new ArgumentNullException(nameof(valueFactory));
#endif

        cancellationToken.ThrowIfCancellationRequested();

        var workspace = this.workspace ??= new();

        await workspace.WaitAsync(cancellationToken).ConfigureAwait(false);

        if (!workspace.HasResult)
        {
            try
            {
                workspace.Result = await valueFactory(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                this.workspace = null;
                workspace.Release(int.MaxValue);
            }
        }

        return workspace.Result;
    }

    /// <summary>
    /// Provides a <see cref="Task{T}"/> of type <typeparamref name="TValue"/> that contains the result of processing.
    /// The results of the first successful call to <paramref name="valueFactory"/> are shared with all concurrent requestors.
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
    /// The results of the first successful call to <paramref name="valueFactory"/> are shared with all concurrent requestors.
    /// </summary>
    /// <param name="valueFactory">The function used to generate a value.</param>
    /// <returns>A task that, upon completion, provides the result of processing.</returns>
    public TValue Run(Func<TValue> valueFactory) => Run(valueFactory, default);

    /// <summary>
    /// Provides a <see cref="Task{T}"/> of type <typeparamref name="TValue"/> that contains the result of processing.
    /// The results of the first successful call to <paramref name="valueFactory"/> are shared with all concurrent requestors.
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
#if NET7_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(valueFactory);
#else
        if (valueFactory is null)
            throw new ArgumentNullException(nameof(valueFactory));
#endif

        var workspace = this.workspace ??= new();

        if (timeout.Ticks <= 0)
            timeout = TimeSpan.FromMilliseconds(-1);

        if (!workspace.Wait(timeout))
            throw new TimeoutException();

        if (!workspace.HasResult)
        {
            try
            {
                workspace.Result = valueFactory();
            }
            finally
            {
                this.workspace = null;
                workspace.Release(int.MaxValue);
            }
        }

        return workspace.Result;
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

        var workspace = this.workspace;

        if (workspace is null)
            return;

        workspace.Dispose();
        this.workspace = null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}
