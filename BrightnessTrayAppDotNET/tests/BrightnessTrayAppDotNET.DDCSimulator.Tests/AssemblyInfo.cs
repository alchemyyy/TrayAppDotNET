using Xunit;

// The simulation clock decides when virtual time may advance by watching process-wide thread pool activity,
// so two simulations running at once would stall each other
[assembly: CollectionBehavior(DisableTestParallelization = true)]
