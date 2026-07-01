namespace ProbeC;

// The CONFIGURED target. This is the "where + how" of an emitted artifact: which folder the real
// files land in and which namespace they declare. In a real system this is project config (a recipe
// setting / .editorconfig-style knob); here it is a checked-in record so the proof is visible and a
// single place answers "what does 'configured target' look like".
sealed record EmitTarget(string Folder, string Namespace)
{
    // The committed sample target, relative to the probe root, so the emitted output is visible in
    // the repo as proof.
    public static EmitTarget Sample(string probeRoot) =>
        new(Path.Combine(probeRoot, "emitted-sample", "Customers"), "Acme.Customers");
}
