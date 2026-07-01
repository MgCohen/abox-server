namespace ProbeC;

// The two states and the one gate between them.
//
//   Live  : lower the recipe into a throwaway PREVIEW dir, in place, every time the recipe changes.
//           The preview is never the owned artifact; it is regenerated wholesale each run.
//   Emit  : an explicit action. Lower the recipe into the CONFIGURED target — real files, correct
//           folder, proper namespace. Once written, those files are DETACHED: live never touches
//           the target dir, only the preview dir. Re-emit overwrites the target (clobbers manual
//           edits — accepted, no reconciliation).
static class Engine
{
    // LIVE: regenerate the preview in place. Writes ONLY to the preview dir, never the target.
    public static IReadOnlyList<string> Live(Recipe recipe, EmitTarget target, string previewDir)
    {
        // Live preview shows what an emit WOULD produce, so it lowers against the same target
        // config (same namespace). It just lands in the preview dir, not the owned target.
        Directory.CreateDirectory(previewDir);
        var written = new List<string>();
        foreach (var artifact in Lowering.Lower(recipe, target))
        {
            var path = Path.Combine(previewDir, artifact.RelativePath);
            File.WriteAllText(path, artifact.Text);
            written.Add(path);
        }
        return written;
    }

    // EMIT: the explicit gate. Materialize the real, owned files at the configured target, creating
    // folders as needed, overwriting any existing target files (re-emit override).
    public static IReadOnlyList<string> Emit(Recipe recipe, EmitTarget target)
    {
        Directory.CreateDirectory(target.Folder);
        var written = new List<string>();
        foreach (var artifact in Lowering.Lower(recipe, target))
        {
            var path = Path.Combine(target.Folder, artifact.RelativePath);
            File.WriteAllText(path, artifact.Text);   // overwrites: re-emit clobbers, by design
            written.Add(path);
        }
        return written;
    }
}
