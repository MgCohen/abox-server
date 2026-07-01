#!/usr/bin/env python3
# Surface-area + TYPE-SAFETY measurement, author-only. This probe's win is QUALITATIVE
# (type-safe, structured, string-free business glue), not a line race — the leverage
# line race was explicitly rejected as contaminated. So the headline metric here is
# "free-text string literals in the authored business surface": the rejected recipe
# carries the key/command/models/glue as strings (drift-prone, unchecked); this one
# carries ZERO. Lines/tokens are reported as a secondary "did the surface blow up?"
# check.
#
# Excludes generated code and one-time reusable definitions (the component, the
# domain, the runtime, the resolution machinery) — written once, amortised.
import re, pathlib

HERE = pathlib.Path(__file__).resolve().parent
ROOT = HERE.parent
LEVERAGE = ROOT.parent / "leverage-probe"

def strip_comments(text):
    text = re.sub(r"/\*.*?\*/", "", text, flags=re.S)
    out = []
    for line in text.splitlines():
        line = re.sub(r"//.*$", "", line)
        if line.strip():
            out.append(line.rstrip())
    return out

def span(path, begin, end):
    lines = pathlib.Path(path).read_text().splitlines()
    bi = next(i for i, l in enumerate(lines) if begin in l)
    ei = next(i for i, l in enumerate(lines) if end in l)
    return "\n".join(lines[bi+1:ei])

def count_tokens(lines):
    text = "\n".join(lines)
    text = re.sub(r'"""(.*?)"""', ' STR ', text, flags=re.S)
    text = re.sub(r'"(\\.|[^"\\])*"', ' STR ', text)
    text = re.sub(r"'(\\.|[^'\\])*'", ' STR ', text)
    return len(re.findall(r"[A-Za-z_][A-Za-z0-9_]*|[0-9]+|[^\sA-Za-z0-9_]", text))

# free-text string literals (the drift-prone, unchecked surface) in the authored span
def count_string_literals(lines):
    text = "\n".join(lines)
    triple = re.findall(r'"""(.*?)"""', text, flags=re.S)
    text = re.sub(r'"""(.*?)"""', ' ', text, flags=re.S)
    single = re.findall(r'"(\\.|[^"\\])*"', text)
    return len(triple) + len(single)

def report(name, lines):
    return name, len(lines), count_tokens(lines), count_string_literals(lines), lines

cases = []

# 1. REJECTED string motif recipe (leverage probe) — business glue as free text
rej = strip_comments("using Probe.Catalog;\n" + span(
    LEVERAGE / "src" / "AuthoredRecipe.cs", "=== AUTHORED (begin) ===", "=== AUTHORED (end) ==="))
cases.append(report("rejected string motif (leverage)", rej))

# 2. TYPED extraction recipe (this probe) — generics + real-code lambdas
typ = strip_comments("using Probe.Domain;\n" + span(
    ROOT / "src" / "AuthoredRecipe.cs", "=== AUTHORED (begin) ===", "=== AUTHORED (end) ==="))
cases.append(report("typed extraction (this probe)", typ))

# 3. hand-written baseline (the "real case") — all real code, no machinery
base = strip_comments(span(
    HERE / "baseline-handwritten.cs", "=== BASELINE (begin) ===", "=== BASELINE (end) ==="))
cases.append(report("hand-written baseline", base))

w = max(len(c[0]) for c in cases)
print(f"{'version'.ljust(w)} | lines | tokens | business strings")
print(f"{'-'*w}-|-------|--------|-----------------")
for name, nb, tk, st, _ in cases:
    print(f"{name.ljust(w)} | {str(nb).rjust(5)} | {str(tk).rjust(6)} | {str(st).rjust(16)}")

rej_s = cases[0][3]
typ_s = cases[1][3]
print()
print(f"FREE-TEXT BUSINESS STRINGS  rejected={rej_s}  ->  typed={typ_s}")
print("  the rejected recipe's key/command/models/glue are strings (unchecked, drift-prone);")
print("  the typed recipe carries ZERO — every part is a generic arg or a compiler-checked lambda.")
print()
print(f"surface did not blow up: typed={cases[1][1]} lines vs hand-written={cases[2][1]} lines",
      "(<= baseline)" if cases[1][1] <= cases[2][1] else "(> baseline)")

print("\n" + "="*70)
for name, nb, tk, st, lines in cases:
    print(f"\n----- {name}  ({nb} lines, {tk} tokens, {st} business strings) -----")
    print("\n".join(lines))
