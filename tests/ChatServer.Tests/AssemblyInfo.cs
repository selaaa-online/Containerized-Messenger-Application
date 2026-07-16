using Xunit;

// Loopback socket tests and env-var configuration tests touch process-global state,
// so run this assembly's tests serially to keep them deterministic.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
