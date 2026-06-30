namespace Probe.Domain;

// STANDS IN FOR THE SOURCE-GEN MINT — not author surface, NOT counted.
//
// Per the north star, creating a new type is the CLOSED part: the author declares
// "a model named CartItem with these fields" and the inline source generator mints
// the record (proven by probe A inline type-gen + probe E forward-ref — the type
// exists before the lines that use it compile). This probe does NOT re-prove that.
//
// These two records are what that mint WOULD produce. They are hand-declared here
// only so the recipe's real-code glue (which references CartItem / AddItemCommand)
// type-checks and the emitted feature compiles. In the real flow the generator emits
// them; the author never writes the records, only the field declaration the mint reads.

public sealed record CartItem(Guid BookId, int Qty, decimal Price, string Label);

public sealed record AddItemCommand(string Email, Guid BookId, int Qty);
