# Probe E — forward references (does a generated type need a name-based `TypeRef`?)

> Settles the residual slice of doubt **#17** in `../README.md` §8 and the "cross-recipe
> forward refs" limit flagged in `../PROBES.md` (probe A's honest limits). Reuses probe A's
> `IIncrementalGenerator` + `Microsoft.CodeAnalysis.CSharp 4.11.0`, renamed to the
> `CreateModel(...)` marker. Self-contained net10.0; references no repo project; local bin/obj
> only (`Directory.Build.props` sets `UseArtifactsOutput=false`); no leak to repo `artifacts/`.

## Idea

A recipe site declares a type as a **value** — `CreateModel("Foo", ("Id", typeof(Guid)), ("Name", typeof(string)))`.
A source generator scans the whole compilation for those markers and back-fills
`public record Foo(...)`. The probe asks: when another recipe **references** `Foo`, does it
need a name-based `TypeRef` (the value-level representation §8 #17 posits), or is the
generator-minted `Foo` just a real type usable as `<T>` / `Foo` / `Repo<Foo>`?

## Why

README §8 #17 claims a type one recipe *generates* "lives in another compilation, so it's
named by `TypeRef` (value-level), not `<T>`," making `TypeRef` the *fundamental* representation
and `<T>` mere sugar. The owner's counter-hypothesis: that's exactly what source generators
already do — marker → type minted → usable as a real type, same compilation, like an attribute
spawning a partial. This probe resolves it **with code**: it finds the precise line where a
real `<T>`/named type stops working and a value-level `TypeRef` becomes the *only* thing that
compiles.

## What was proved (three scenarios, real build+run)

### Scenario 1 — same compilation, cross-recipe, order-independent ✅
Two recipe sites in **separate files** in one project:
- `ZZ_SiteX_Mints.cs` holds `CreateModel("Foo", …)` (the mint).
- `00_SiteY_Uses.cs` references `Foo` directly — a `Foo?` field, `Repo<Foo>`, `List<Foo>`.

The use site is named `00_*` and the mint site `ZZ_*` **on purpose**: the use file sorts and
compiles textually *before* the mint file. Generation is **compilation-wide**, not line-by-line,
so order is irrelevant. Built and ran:
```
Current      -> Foo { Id = …, Name = beta }
Repo+List ct -> 4
Scenario 1: same-compilation, cross-file, use-before-mint => COMPILED & RAN
```
Inspected generated output (`scenario1-same-compilation/generated/.../Minted.Foo.g.cs`):
`public record Foo(global::System.Guid Id, string Name);`.
**Verdict: no `TypeRef` needed.** The minted type is a first-class `<T>`-usable type across the
whole compilation, in any file order. (Confirms probe A across two sites/files.)

### Scenario 2 — cross-project, with reference ✅
`projectA` mints `Foo` (the generated record is `public`, so it is baked into A's **emitted
assembly**). `projectB` references A and uses `Foo` — `new Foo(...)`, `Repo<Foo>`, `List<Foo>`
— and also round-trips A's public API returning the minted type. **B does not run the
generator**; it binds against the public `Foo` in A's DLL, the ordinary "referenced assembly
exposes a public type" path. The type's provenance (generator vs hand-written) is invisible to
B. Built and ran:
```
Foo (in B)   -> Foo { Id = …, Name = from-B }
List<Foo> ct -> 2
Repo<Foo> ct -> 1
A.MakeFoo    -> Foo { Id = …, Name = via-A-API }
Scenario 2: cross-project, B references A => COMPILED & RAN
```
**Verdict: no `TypeRef` needed** — as long as B references A. A generator-minted type is a
normal public type in the emitted assembly.

### Scenario 3 — the genuine corner case ✅ (break captured + boundary articulated)
`scenario3-corner-case` wants `Foo` but **neither runs the generator nor references the
producing assembly**. `Foo` is then an unresolved *name*. The default build (`Break.cs`) fails:
```
Break.cs(19,12): error CS0246: The type or namespace name 'Foo' could not be found …
Break.cs(20,17): error CS0246: The type or namespace name 'Foo' could not be found …
Build FAILED.
```
A `<T>`/named reference *requires a symbol*; here no symbol exists in or visible to this
compilation, so it cannot compile. The **value-level `TypeRef`** (`Works.cs`, built via
`-p:Mode=typeref`) carries the type's identity as data and compiles trivially:
```
TypeRef      -> Foo
List<TypeRef>-> 2
Scenario 3: no producer in/visible to this compilation => <T> impossible; value-level TypeRef COMPILES & RAN
```
**The same shape is the circular-producer case:** if A's mint needs a type B mints *and* B's
mint needs a type A mints, neither assembly can be built first, so within each compilation the
other's type is an unresolved name — identical CS0246, identical conclusion.

**Verdict: `TypeRef` is required here — and only here.**

## Verdict table

| Situation | Producer present in/visible to the compilation? | `<T>` / named `Foo` works? | `TypeRef` needed? |
|---|---|---|---|
| **Same compilation, cross-recipe** (Sc.1) | yes (generator runs here) | ✅ yes, order-independent | **No** |
| **Cross-project, B references A** (Sc.2) | yes (A's assembly referenced) | ✅ yes | **No** |
| **Producer not referenced / not yet built / circular** (Sc.3) | **no** | ❌ `CS0246` | **Yes — only here** |

> **TypeRef needed? — same-compilation: NO. cross-project-referenced: NO.
> Only required when: the producing compilation/assembly is not present in or referenced by the
> consuming compilation at the moment it must bind — i.e. an unbuilt/unreferenced producer or a
> circular dependency between two producers. In every case where the producer is referenced
> (same compilation or a referenced assembly), the minted type is a real `<T>`-usable type and
> `TypeRef` buys nothing.**

This **narrows** §8 #17. The claim "a generated type lives in another compilation, so it's
`TypeRef` not `<T>`" is **not generally true**: generated types are part of the producer's
*emitted assembly*, so a referenced producer exposes them as ordinary `<T>` types. `TypeRef` is
the *fundamental* representation **only at the dependency-graph boundary** the compile gate
can't yet close (unreferenced/unbuilt/circular producers). Everywhere else, `<T>` is not "sugar
for the known subset" — it is the real, type-checked reference, and the owner's
counter-hypothesis holds.

## How to run

```bash
# Scenario 1 — same compilation, cross-file, use-before-mint
cd scenario1-same-compilation && dotnet run

# Scenario 2 — cross-project; B references A
cd scenario2-cross-project/projectB && dotnet run

# Scenario 3a — the break: name-based <T> with no producer present (EXPECT CS0246)
cd scenario3-corner-case && dotnet build

# Scenario 3b — the workaround: value-level TypeRef in that same corner (compiles + runs)
cd scenario3-corner-case && dotnet run -p:Mode=typeref
```

Captured output for all three is in `evidence/scenario{1,2,3}.txt`. Minted records are
inspectable under each project's `generated/.../Minted.Foo.g.cs`.

## Honest limitations

- **The corner is a dependency-graph property, not a generator limitation.** Scenario 3 fails
  because the producer isn't on B's reference graph — the same as any unresolved type. The probe
  shows `TypeRef` is the *only representation that compiles* there, not that the generator
  *could* have minted across that gap (it cannot — a generator only emits into its own
  compilation).
- **Circularity is argued by isomorphism, not built as a 2-project cycle.** A real A↔B mint
  cycle would need each generator to consume the other's not-yet-emitted type; the CS0246 shape
  is identical to Scenario 3's unresolved name, so it is covered by the same evidence rather than
  a separate failing build. (`ProjectReference` cycles are themselves rejected by MSBuild, which
  is the same boundary one layer up.)
- **`TypeRef` here is a minimal `record struct {string Name}`** — enough to show it compiles
  where `<T>` can't and defers resolution to a later compile gate. A production `TypeRef` would
  carry assembly/namespace/arity and a resolver; that resolution + cross-use safety is exactly
  the "defers to the compile gate, or a recipe-level validator" tail §8 #17 already names.
- **Generated type is emitted top-level (global namespace).** Real output will be namespaced
  (§8 #15); a namespaced minted type changes nothing about the verdict — it only changes the
  `using`/qualified name B writes, still a real `<T>` once A is referenced.
- **IDE IntelliSense lag** on a freshly-typed marker (inherited from probe A) — a non-issue for
  an agent's write→build loop, a rough edge for a human.
