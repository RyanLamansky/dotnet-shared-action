using System.Collections.Concurrent;

namespace SharedHelpers; // Needed something here, but you should use your own choice when copying into your project.

/// <summary>
/// Shares the result of a single action among one or more concurrent requests.
/// The result is discarded when all concurrent waiters have received it.
/// </summary>
/// <remarks>
/// Internally uses globally shared instances of <see cref="SharedAction{TKey, TValue}"/> with the default comparer.
/// The unique key for the instance is the key/value type combination.
/// The internal instances are never disposed and are only released by the application shutting down.
/// </remarks>
public static class SharedAction
{
    private static class Shared<TKey, TValue>
        where TKey : notnull
    {
        public static readonly SharedAction<TKey, TValue> Instance = new();
    }

    /// <summary>
    /// Provides a <see cref="Task{T}"/> of type <typeparamref name="TValue"/> that contains the result of processing the input.
    /// The results of the first successful call to <paramref name="valueFactory"/> are shared with all concurrent requestors with the same input.
    /// </summary>
    /// <param name="input">The input to the processing logic.</param>
    /// <param name="valueFactory">The function used to generate a value for the input.</param>
    /// <returns>A task that, upon completion, provides the result of processing.</returns>
    public static Task<TValue> RunAsync<TKey, TValue>(TKey input, Func<TKey, Task<TValue>> valueFactory)
        where TKey : notnull
        => Shared<TKey, TValue>.Instance.RunAsync(input, valueFactory);

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
    public static Task<TValue> RunAsync<TKey, TValue>(TKey input, Func<TKey, CancellationToken, Task<TValue>> valueFactory, CancellationToken cancellationToken = default)
        where TKey : notnull
        => Shared<TKey, TValue>.Instance.RunAsync(input, valueFactory, cancellationToken);

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
    public static Task<TValue> RunAsync<TKey, TValue>(TKey input, Func<TKey, TValue> valueFactory, CancellationToken cancellationToken = default)
        where TKey : notnull
        => Shared<TKey, TValue>.Instance.RunAsync(input, valueFactory, cancellationToken);

    /// <summary>
    /// Provides a <see cref="Task{T}"/> of type <typeparamref name="TValue"/> that contains the result of processing the input.
    /// The results of the first successful call to <paramref name="valueFactory"/> are shared with all concurrent requestors with the same input.
    /// </summary>
    /// <param name="input">The input to the processing logic.</param>
    /// <param name="valueFactory">The function used to generate a value for the input.</param>
    /// <param name="timeout">
    /// The amount of time to wait for the action to complete.
    /// If the time span is 0 or less, the wait time is unlimited.
    /// This is used to create a <see cref="CancellationToken"/> that is passed to <paramref name="valueFactory"/>.
    /// </param>
    /// <returns>A task that, upon completion, provides the result of processing.</returns>
    /// <exception cref="OperationCanceledException">The time limit from <paramref name="timeout"/> was reached before completion.</exception>
    public static Task<TValue> RunAsync<TKey, TValue>(TKey input, Func<TKey, CancellationToken, Task<TValue>> valueFactory, TimeSpan timeout)
        where TKey : notnull
        => Shared<TKey, TValue>.Instance.RunAsync(input, valueFactory, timeout);

    /// <summary>
    /// Provides a <see cref="Task{T}"/> of type <typeparamref name="TValue"/> that contains the result of processing the input.
    /// The results of the first successful call to <paramref name="valueFactory"/> are shared with all concurrent requestors with the same input.
    /// </summary>
    /// <param name="input">The input to the processing logic.</param>
    /// <param name="valueFactory">The function used to generate a value for the input.</param>
    /// <returns>A task that, upon completion, provides the result of processing.</returns>
    public static TValue Run<TKey, TValue>(TKey input, Func<TKey, TValue> valueFactory)
        where TKey : notnull
        => Shared<TKey, TValue>.Instance.Run(input, valueFactory);

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
    public static TValue Run<TKey, TValue>(TKey input, Func<TKey, TValue> valueFactory, TimeSpan timeout)
        where TKey : notnull
        => Shared<TKey, TValue>.Instance.Run(input, valueFactory, timeout);
}
