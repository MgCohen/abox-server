#!/usr/bin/env python3
"""Spike validator: enforce a block-structured doc against its doc-type catalog.

Reads the block schemas (blocks/*.yaml) and one doc-type catalog
(doctypes/<docType>.yaml), parses a :::block instance file, and reports every
structural violation. This is the throwaway proof of "structure over prose,
enforced" — the rules in the YAML are law, not suggestions.

    python3 validate.py out/git-feature.plan.md
"""
import sys, re, glob, os
import yaml

ROOT = os.path.dirname(os.path.abspath(__file__))
OPEN_RE = re.compile(r'^:::([\w-]+)\s*(?:\{(.*)\})?\s*$')
ATTR_RE = re.compile(r'(\w[\w-]*)=(?:"([^"]*)"|(\S+))')


def load_blocks():
    out = {}
    for f in glob.glob(os.path.join(ROOT, "blocks", "*.yaml")):
        d = yaml.safe_load(open(f))
        out[d["type"]] = d
    return out


def load_doctype(name):
    return yaml.safe_load(open(os.path.join(ROOT, "doctypes", name + ".yaml")))


def doctype_of(md_path, lines):
    for ln in lines[:5]:
        m = re.search(r'docType:\s*([\w-]+)', ln)
        if m:
            return m.group(1)
    raise SystemExit("no `docType:` marker in first lines of " + md_path)


def parse(lines):
    blocks, cur = [], None
    for raw in lines:
        line = raw.rstrip("\n")
        s = line.strip()
        if s == ":::" and cur is not None:
            cur["body"] = "\n".join(cur.pop("lines")).strip()
            blocks.append(cur); cur = None; continue
        m = OPEN_RE.match(s)
        if m and cur is None and s != ":::":
            attrs = {}
            if m.group(2):
                for a in ATTR_RE.finditer(m.group(2)):
                    attrs[a.group(1)] = a.group(2) if a.group(2) is not None else a.group(3)
            cur = {"type": m.group(1), "attrs": attrs, "lines": []}
            continue
        if cur is not None:
            cur["lines"].append(line)
    if cur is not None:
        raise SystemExit("unterminated block ::: " + cur["type"])
    return blocks


def validate(defs, dt, parsed):
    errs = []
    counts, order = {}, []
    for i, b in enumerate(parsed):
        t = b["type"]; order.append(t); counts[t] = counts.get(t, 0) + 1
        spec = defs.get(t)
        if not spec:
            errs.append(f"block #{i+1}: unknown block type '{t}'"); continue
        attrs = spec.get("attrs") or {}
        for an, asp in attrs.items():
            if asp.get("required") and an not in b["attrs"]:
                errs.append(f"{t} #{i+1}: missing required attr '{an}'")
            if an in b["attrs"] and asp.get("type") == "enum" and b["attrs"][an] not in asp["values"]:
                errs.append(f"{t} #{i+1}: attr {an}='{b['attrs'][an]}' not in {asp['values']}")
        for an in b["attrs"]:
            if an not in attrs:
                errs.append(f"{t} #{i+1}: unknown attr '{an}'")
        body = spec.get("body")
        if body and body.get("required") and not b["body"]:
            errs.append(f"{t} #{i+1}: required body is empty")
        if not spec.get("repeatable") and counts[t] > 1:
            errs.append(f"{t} #{i+1}: block is not repeatable but appears {counts[t]}×")

    rules = dt.get("blocks") or {}
    for t in counts:
        if t not in rules:
            errs.append(f"doctype: block '{t}' not allowed in '{dt['docType']}'")
    for t, r in rules.items():
        c = counts.get(t, 0)
        if "min" in r and c < r["min"]:
            errs.append(f"doctype: '{t}' appears {c}× (min {r['min']})")
        if "max" in r and c > r["max"]:
            errs.append(f"doctype: '{t}' appears {c}× (max {r['max']})")
        if r.get("position") == "first" and c and order[0] != t:
            errs.append(f"doctype: '{t}' must be the first block")
        if r.get("position") == "last" and c and order[-1] != t:
            errs.append(f"doctype: '{t}' must be the last block")
    return errs, counts


def main():
    path = sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, "out", "git-feature.plan.md")
    lines = open(path).readlines()
    defs = load_blocks()
    dt = load_doctype(doctype_of(path, lines))
    parsed = parse(lines)
    errs, counts = validate(defs, dt, parsed)
    print(f"doc: {os.path.relpath(path, ROOT)}   docType: {dt['docType']}")
    print(f"blocks: {len(parsed)}  " + ", ".join(f"{k}×{v}" for k, v in counts.items()))
    if errs:
        print(f"\nFAIL — {len(errs)} violation(s):")
        for e in errs:
            print("  ✗ " + e)
        sys.exit(1)
    print("\nPASS — conforms to the catalog.")


if __name__ == "__main__":
    main()
