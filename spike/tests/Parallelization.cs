// Runtime.CompileAndRun (in-process Roslyn compile + Assembly.Load) is not reentrant; run serially.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
