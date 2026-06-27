using System.Runtime.CompilerServices;

namespace Spike;

// The snippet catalog. Each method is REAL, compiling, type-checked C# — the body is the
// template. Holes are encoded so the method still compiles and the generator can find them:
//   - value hole : a by-value parameter        (filled by a rendered child expression)
//   - name hole  : an `@`-prefixed identifier   (filled by a name string from the recipe)
//                  (existing variables use a `ref` param so the body compiles in isolation)
//   - body hole  : `Slot.Of<Block>()`           (filled by rendered child statements)
//
// Spike simplification: int-specialized (the design's generic <T> is a backlog refinement),
// and inline-only (no Call mode yet). These methods are never invoked — they exist to be
// authored/validated by the compiler and parsed by the generator.
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
            Slot.Of<Block>();
        }
    }

    [Snippet("return")]
    public static int Return(int value)
    {
        return value;
    }

    // The generator reads these snippets from THIS source file. The CallerFilePath capture
    // must originate inside this file, so it's an initializer here (not a method the generator
    // calls). Editing a snippet (e.g. renaming `@i` to `@idx`) flows through on the next run.
    public static string SourcePath { get; } = Capture();

    static string Capture([CallerFilePath] string path = "") => path;
}
