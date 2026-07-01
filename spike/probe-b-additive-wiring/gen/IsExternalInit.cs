// netstandard2.0 (the analyzer TFM) has no IsExternalInit, which `record`/`init`
// require. This shim is the standard, compiler-recognised stand-in.
namespace System.Runtime.CompilerServices;

internal static class IsExternalInit { }
