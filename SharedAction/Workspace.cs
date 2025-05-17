namespace SharedHelpers;

internal sealed class Workspace<TValue> : SemaphoreSlim
{
    internal Workspace() : base(1, int.MaxValue)
    {
    }

    private readonly struct Container(TValue result)
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
