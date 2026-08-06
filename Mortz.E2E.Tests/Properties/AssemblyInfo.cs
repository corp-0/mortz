using Xunit;

// One E2E scenario at a time: two servers would compete for the machine.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
