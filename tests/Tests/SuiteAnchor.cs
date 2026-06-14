namespace ABox.Tests;

// The product suite's public handle, so the Meta self-suite (a separate assembly) can reflect over the
// assembly it validates without binding to an arbitrary test class — the test-system analogue of how Arch
// loads src by assembly.
public sealed class SuiteAnchor;
