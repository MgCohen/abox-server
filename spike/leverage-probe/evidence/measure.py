#!/usr/bin/env python3
# Surface-area measurement: count ONLY what the author writes for each of the three
# versions of the SAME feature. Excludes generated code and one-time reusable
# definitions (the motif, the catalog, the runtime, the resolution machinery) —
# those are written once and amortised, which is the whole point of leverage.
#
# Two metrics, both author-only:
#   - lines : non-blank, comment-stripped authored lines
#   - tokens: non-trivial tokens (identifiers, literals, operators, punctuation),
#             a length-independent proxy for "stuff the author had to decide".
import re, sys, pathlib

HERE = pathlib.Path(__file__).resolve().parent
ROOT = HERE.parent
SLICE = ROOT.parent / "integration-slice"

def strip_comments(text):
    # drop // line comments and /* */ blocks (none of our spans use block comments,
    # but be safe), then blank lines.
    text = re.sub(r"/\*.*?\*/", "", text, flags=re.S)
    out = []
    for line in text.splitlines():
        line = re.sub(r"//.*$", "", line)
        if line.strip():
            out.append(line.rstrip())
    return out

def span(path, begin, end):
    text = pathlib.Path(path).read_text()
    lines = text.splitlines()
    bi = next(i for i, l in enumerate(lines) if begin in l)
    ei = next(i for i, l in enumerate(lines) if end in l)
    return "\n".join(lines[bi+1:ei])

def line_range(path, start, stop):
    lines = pathlib.Path(path).read_text().splitlines()
    return "\n".join(lines[start-1:stop])

# crude C# tokenizer: words/numbers, string contents collapsed to one token,
# and standalone punctuation/operators.
def count_tokens(lines):
    text = "\n".join(lines)
    # collapse string and char literals to a single STR token each
    text = re.sub(r'"""(.*?)"""', ' STR ', text, flags=re.S)
    text = re.sub(r'"(\\.|[^"\\])*"', ' STR ', text)
    text = re.sub(r"'(\\.|[^'\\])*'", ' STR ', text)
    toks = re.findall(r"[A-Za-z_][A-Za-z0-9_]*|[0-9]+|[^\sA-Za-z0-9_]", text)
    # drop nothing — punctuation is real authoring cost
    return len(toks)

def report(name, lines):
    nb = len(lines)
    tk = count_tokens(lines)
    return name, nb, tk, lines

cases = []

# 1. hand-written baseline
b = strip_comments(span(HERE / "baseline-handwritten.cs",
                        "=== BASELINE (begin) ===", "=== BASELINE (end) ==="))
cases.append(report("hand-written baseline", b))

# 2. integration-slice verbose recipe (the FeatureRecipe call, lines 21-57)
s = strip_comments(line_range(SLICE / "src" / "AuthoredRecipe.cs", 21, 57))
cases.append(report("integration-slice recipe (verbose)", s))

# 3. motif recipe (this probe) — authored span + the one using line the author writes
m_span = span(ROOT / "src" / "AuthoredRecipe.cs",
              "=== AUTHORED (begin) ===", "=== AUTHORED (end) ===")
m = strip_comments("using Probe.Catalog;\n" + m_span)
cases.append(report("motif recipe (this probe)", m))

# emit the table
w = max(len(c[0]) for c in cases)
print(f"{'version'.ljust(w)} | authored lines | author tokens")
print(f"{'-'*w}-|----------------|--------------")
for name, nb, tk, _ in cases:
    print(f"{name.ljust(w)} | {str(nb).rjust(14)} | {str(tk).rjust(13)}")

base = cases[0][1]
slice_ = cases[1][1]
motif = cases[2][1]
print()
print(f"motif vs baseline : {motif} vs {base} lines  ->  {base/motif:.2f}x smaller than hand-written")
print(f"slice vs baseline : {slice_} vs {base} lines  ->  {slice_/base:.2f}x LARGER than hand-written")
print(f"motif vs slice    : {motif} vs {slice_} lines  ->  {slice_/motif:.2f}x smaller than the verbose recipe")
btk, stk, mtk = cases[0][2], cases[1][2], cases[2][2]
print(f"tokens motif/base/slice: {mtk} / {btk} / {stk}  ->  motif is {btk/mtk:.2f}x fewer tokens than baseline")
print()
ordered = motif < base < slice_
print("WIN CONDITION (motif << hand-written << verbose slice):", "MET" if ordered else "NOT MET")

# dump the three authored spans verbatim for the record
print("\n" + "="*70)
for name, nb, tk, lines in cases:
    print(f"\n----- {name}  ({nb} lines, {tk} tokens) -----")
    print("\n".join(lines))
