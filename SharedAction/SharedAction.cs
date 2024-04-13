namespace SharedAction;

using System.Collections.Concurrent;

/// <summary>
/// Shares the result of a single action among one or more concurrent requests.
/// The result is discarded once all concurrent waiters have received it.
/// </summary>
/// <typeparam name="TKey">The type that describes the input to the action.</typeparam>
/// <typeparam name="TValue">The type of the value returned by the action.</typeparam>
/// <param name="comparer">If provided, enables customized comparison of <typeparamref name="TKey"/> values.</param>
/// <remarks>
/// In general, instances of this type should be reused as long as shared action duplicate inputs are possible.
/// As an example, web applications using this to share API results should store the instance in a static readonly field.
/// </remarks>
public class SharedAction<TKey, TValue>(IEqualityComparer<TKey>? comparer = null)
    : IDisposable
    where TKey : notnull
{
    private sealed class Workspace : SemaphoreSlim
    {
        internal Workspace() : base(1, 1)
        {
        }

        internal readonly struct Container(TValue result)
        {
            public readonly bool HasResult = true; // False if the constructor doesn't run.
            public readonly TValue Result = result; // Default of TValue if the constructor doesn't run.
        }

        private Container container; // Constructor isn't (initially) run, so .HasResult is false and .Result is the default for TValue.

        internal bool HasResult => container.HasResult;

        internal TValue Result
        {
            get => container.Result;
            set => container = new(value);
        }
    }

    /// <summary>
    /// Tracks active activities. When null, the object is disposed.
    /// </summary>
    private ConcurrentDictionary<TKey, Workspace>? workspaces = new(comparer);

    /// <summary>
    /// Provides a <see cref="Task{T}"/> of type <typeparamref name="TValue"/> that contains the result of processing the input.
    /// The results of the first successful call to <paramref name="valueFactory"/> are shared with all concurrent requestors with the same input.
    /// </summary>
    /// <param name="input">The input to the processing logic.</param>
    /// <param name="valueFactory">The function used to generate a value for the input.</param>
    /// <returns>A task that, upon completion, provides the result of processing.</returns>
    public async Task<TValue> RunAsync(TKey input, Func<TKey, Task<TValue>> valueFactory)
    {
        var workspace = GetWorkspacesOrThrowDisposedException().GetOrAdd(input, static _ => new());

        await workspace.WaitAsync().ConfigureAwait(false);

        try
        {
            if (!workspace.HasResult)
            {
                try
                {
                    workspace.Result = await valueFactory(input).ConfigureAwait(false);
                }
                finally
                {
                    GetWorkspacesOrThrowDisposedException().TryRemove(input, out _);
                }
            }

            return workspace.Result;
        }
        finally
        {
            if (workspace.Release() == 1)
                workspace.Dispose();
        }
    }

    /// <summary>
    /// Provides a <see cref="Task{T}"/> of type <typeparamref name="TValue"/> that contains the result of processing the input.
    /// The results of the first successful call to <paramref name="valueFactory"/> are shared with all concurrent requestors with the same input.
    /// </summary>
    /// <param name="input">The input to the processing logic.</param>
    /// <param name="valueFactory">The function used to generate a value for the input.</param>
    /// <param name="cancellationToken">
    /// If provided, can be used to trigger cancellation of the operation.
    /// It is used for both the internal semaphore and <paramref name="valueFactory"/>.
    /// </param>
    /// <returns>A task that, upon completion, provides the result of processing.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was triggered before completion.</exception>
    public async Task<TValue> RunAsync(TKey input, Func<TKey, CancellationToken, Task<TValue>> valueFactory, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var workspace = GetWorkspacesOrThrowDisposedException().GetOrAdd(input, static _ => new());

        await workspace.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (!workspace.HasResult)
            {
                try
                {
                    workspace.Result =await valueFactory(input, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    GetWorkspacesOrThrowDisposedException().TryRemove(input, out _);
                }
            }

            return workspace.Result;
        }
        finally
        {
            if (workspace.Release() == 1)
                workspace.Dispose();
        }
    }

    /// <summary>
    /// Provides a <see cref="Task{T}"/> of type <typeparamref name="TValue"/> that contains the result of processing the input.
    /// The results of the first successful call to <paramref name="valueFactory"/> are shared with all concurrent requestors with the same input.
    /// </summary>
    /// <param name="input">The input to the processing logic.</param>
    /// <param name="valueFactory">The function used to generate a value for the input.</param>
    /// <returns>A task that, upon completion, provides the result of processing.</returns>
    public TValue Run(TKey input, Func<TKey, TValue> valueFactory) => Run(input, valueFactory, default);

    /// <summary>
    /// Provides a <see cref="Task{T}"/> of type <typeparamref name="TValue"/> that contains the result of processing the input.
    /// The results of the first successful call to <paramref name="valueFactory"/> are shared with all concurrent requestors with the same input.
    /// </summary>
    /// <param name="input">The input to the processing logic.</param>
    /// <param name="valueFactory">The function used to generate a value for the input.</param>
    /// <param name="timeout">
    /// The amount of time to wait before entering the semaphore.
    /// If the time span is 0 (the default) or less, the wait time is unlimited.
    /// </param>
    /// <returns>A task that, upon completion, provides the result of processing.</returns>
    /// <exception cref="TimeoutException">The time limit indicated by <paramref name="timeout"/> has been exceeded.</exception>
    public TValue Run(TKey input, Func<TKey, TValue> valueFactory, TimeSpan timeout)
    {
        var workspace = GetWorkspacesOrThrowDisposedException().GetOrAdd(input, static _ => new());

        if (timeout.Ticks <= 0)
            timeout = TimeSpan.FromMilliseconds(-1);

        if (!workspace.Wait(timeout))
            throw new TimeoutException();

        try
        {
            if (!workspace.HasResult)
            {
                try
                {
                    workspace.Result = valueFactory(input);
                }
                finally
                {
                    GetWorkspacesOrThrowDisposedException().TryRemove(input, out _);
                }
            }

            return workspace.Result;
        }
        finally
        {
            if (workspace.Release() == 1)
                workspace.Dispose();
        }
    }

    private ConcurrentDictionary<TKey, Workspace> GetWorkspacesOrThrowDisposedException()
        => workspaces ?? throw new ObjectDisposedException(nameof(SharedAction));

    /// <summary>
    /// Releases the unmanaged resources used by this instance, and optionally releases the managed resources.
    /// </summary>
    /// <param name="disposing">
    /// true to release both managed and unmanaged resources;
    /// false to release only unmanaged resources.
    /// </param>
    /// <remarks>
    /// This will break any waiting actions.
    /// The base <see cref="SharedAction"/> doesn't have any unmanaged resources, but derived types may.
    /// </remarks>
    protected virtual void Dispose(bool disposing)
    {
        if (!disposing)
            return;

        var workspaces = this.workspaces;

        if (workspaces is null)
            return;

        this.workspaces = null;

        foreach (var workspace in workspaces.Values)
            workspace.Dispose();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}
