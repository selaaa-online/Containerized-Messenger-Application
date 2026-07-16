using Xunit;

// Loopback socket tests, console redirection and env-var configuration tests touch
// process-global state, so run this assembly's tests serially for determinism.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
