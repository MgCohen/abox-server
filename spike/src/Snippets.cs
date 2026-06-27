using System.Runtime.CompilerServices;

namespace Spike;

// The snippet catalog. Each method is REAL, compiling, type-checked C# — the body is the
// template. A snippet's fills come in three forms:
//   param  : a by-value parameter        (filled by a rendered child expression)
//   marker : an `@`-prefixed identifier   (filled by a name from the recipe)
//            (existing variables use a `ref` param so the body compiles in isolation)
//   block  : `Block.Of("id")`             (filled by rendered child statements)
//
// Spike simplification: int-specialized and inline-only. These methods are never invoked —
// they exist to be authored/validated by the compiler and parsed by the generator.
static class Snippets
{
    [Snippet("define")]
    public static void Define(int value)
    {
        int @var = value;
    }

    [Snippet("assign")]
    public static void Assign(ref int @target, int value)
    {
        @target = value;
    }

    [Snippet("add")]
    public static int Add(int a, int b) => a + b;

    [Snippet("loop")]
    public static void Loop(int count)
    {
        for (int @i = 0; @i < count; @i++)
        {
            Block.Of("body");
        }
    }

    [Snippet("return")]
    public static int Return(int value)
    {
        return value;
    }

    // The generator reads these snippets from THIS source file (CallerFilePath captured here,
    // inside the file it refers to). Editing a snippet flows through on the next run.
    public static string SourcePath { get; } = Capture();

    static string Capture([CallerFilePath] string path = "") => path;
}
