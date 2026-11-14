// The tests share one interactive desktop: two applications at once could cover each other's windows and dialogs.
[assembly: Xunit.v3.Parallelization(Mode = Xunit.Sdk.ParallelMode.None)]
