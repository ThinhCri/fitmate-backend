using Xunit;

// All WebApplicationFactory fixtures in this assembly intentionally share one
// isolated PostgreSQL database. Serial execution prevents cross-class startup
// races and makes database-wide invariant checks deterministic.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
