namespace BdoGrindTracker.App.Tests;

// These tests coordinate real background workers with bounded deadlines.
// Keep unrelated CPU-heavy test collections from starving those workers.
[CollectionDefinition("Timing-sensitive integration", DisableParallelization = true)]
public sealed class TimingSensitiveCollection;
