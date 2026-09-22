using System.Runtime.ExceptionServices;

namespace SharedHelpers;

internal sealed class Workspace<TValue> : SemaphoreSlim
{
    internal Workspace() : base(1, int.MaxValue)
    {
    }

    private readonly struct Container(TValue result, ExceptionDispatchInfo? failure)
    {
        public readonly bool HasOutcome = true; // False if the constructor doesn't run.
        public readonly TValue Result = result; // Default of TValue if the constructor doesn't run.
        public readonly ExceptionDispatchInfo? Failure = failure; // Null if the constructor doesn't run.
    }

    private Container container; // Constructor isn't (initially) run, so .HasOutcome is false.

    internal bool HasOutcome => container.HasOutcome;

    internal void SetResult(TValue result) => container = new(result, null);

    /// <summary>
    /// Stores a failure so every waiter observes the original exception instead of running the value factory again.
    /// </summary>
    internal void SetFailure(Exception failure) => container = new(default!, ExceptionDispatchInfo.Capture(failure));

    internal TValue GetOutcome()
    {
        container.Failure?.Throw();

        return container.Result;
    }
}
