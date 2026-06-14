// Integration tests share process-global state (the OpenTelemetry ActivitySource and
// Testcontainer Postgres instances). Several diagnostics tests assert a single span of
// a given name exists, which only holds when DB-touching tests do not run concurrently
// across collections. Serialise the assembly to keep that invariant.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
