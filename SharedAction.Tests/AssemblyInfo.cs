using Xunit;

// Several tests assert on wall clock time or on concurrency behavior.
// Running collections in parallel starves them on a small CI runner and produces failures that say nothing about the code.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
