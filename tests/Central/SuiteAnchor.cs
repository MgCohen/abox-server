namespace ABox.Tests.Central;

// The product suite's public handle, so the harness's own tests (a separate assembly) can reflect over the
// assembly they validate without binding to an arbitrary test class — the test-system analogue of how Arch
// loads src by assembly.
public sealed class SuiteAnchor;
