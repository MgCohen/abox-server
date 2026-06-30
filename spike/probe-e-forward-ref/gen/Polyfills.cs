// netstandard2.0 lacks IsExternalInit, which C# records (init accessors) require.
// This shim lets the generator itself be authored with records.
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}
