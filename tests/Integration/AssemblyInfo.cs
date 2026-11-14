using Xunit;

// Integration tests share one Docker daemon and fixed local networking
// resources. Running collections in parallel makes hosted outcomes depend on
// runner capacity and can terminate testhost before it writes the TRX report.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
