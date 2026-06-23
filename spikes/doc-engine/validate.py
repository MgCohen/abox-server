#!/usr/bin/env python3
"""Spike validator: enforce a block-structured doc against its doc-type catalog.

Block sections are plain markdown headers, type-first, with the (agent-oriented)
id tucked into a comment so it stays out of the human's way:

    ## <type> [- <optional title>]
    <!-- id: 12 -->        # the stable handle, de-emphasised
    key: value             # optional attribute lines, until a blank line

    body markdown ...      # until the next "## " header

The type label is normalised (lower-cased, spaces -> hyphens) to a block slug,
so "Open Question" matches blocks/open-question.yaml. Doc-type catalogs list the
allowed block types and (at most) which are required — no min/max/position.

    python3 validate.py out/git-feature.plan.md
"""
import sys, re, glob, os
import yaml

ROOT = os.path.dirname(os.path.abspath(__file__))
HEADER_RE = re.compile(r'^##\s+(.+?)\s*$')
ID_RE = re.compile(r'^<!--\s*id:\s*(\S+)\s*-->\s*$')
ATTR_RE = re.compile(r'^([\w-]+):\s*(.+?)\s*$')


def slug(label):
    return label.strip().lower().replace(" ", "-")


def load_blocks():
    out = {}
    for f in glob.glob(os.path.join(ROOT, "blocks", "*.yaml")):
        d = yaml.safe_load(open(f))
        out[d["type"]] = d
    return out


def load_doctype(name):
    return yaml.safe_load(open(os.path.join(ROOT, "doctypes", name + ".yaml")))


def doctype_of(path, lines):
    for ln in lines[:5]:
        m = re.search(r'docType:\s*([\w-]+)', ln)
        if m:
            return m.group(1)
    raise SystemExit("no `docType:` marker in first lines of " + path)


def parse(lines):
    blocks, cur, meta = [], None, False
    for raw in lines:
        line = raw.rstrip("\n")
        h = HEADER_RE.match(line)
        if h:
            if cur is not None:
                cur["body"] = "\n".join(cur.pop("lines")).strip()
                blocks.append(cur)
            parts = [p.strip() for p in h.group(1).split(" - ")]
            cur = {
                "type": slug(parts[0]) if parts else "",
                "type_label": parts[0] if parts else "",
                "title": " - ".join(parts[1:]) if len(parts) > 1 else "",
                "id": "", "attrs": {}, "lines": [],
            }
            meta = True
            continue
        if cur is None:
            continue
        if meta:
            if ID_RE.match(line):
                cur["id"] = ID_RE.match(line).group(1); continue
            if line.strip() == "":
                meta = False; continue
            a = ATTR_RE.match(line)
            if a:
                cur["attrs"][a.group(1)] = a.group(2); continue
            meta = False  # first real content line begins the body
        cur["lines"].append(line)
    if cur is not None:
        cur["body"] = "\n".join(cur.pop("lines")).strip()
        blocks.append(cur)
    return blocks


def validate(defs, dt, parsed):
    errs = []
    allowed = set(dt.get("blocks") or [])
    required = set(dt.get("required") or [])
    seen_ids, present = set(), set()

    for i, b in enumerate(parsed):
        where = f"#{i+1} (id={b['id'] or '?'})"
        if not b["id"]:
            errs.append(f"{where}: missing `<!-- id: -->` under the header")
        elif b["id"] in seen_ids:
            errs.append(f"{where}: duplicate id '{b['id']}'")
        seen_ids.add(b["id"])

        t = b["type"]; present.add(t)
        if t not in defs:
            errs.append(f"{where}: unknown block type '{b['type_label']}'"); continue
        if t not in allowed:
            errs.append(f"{where}: '{t}' not in the '{dt['docType']}' catalog")

        spec_attrs = defs[t].get("attrs") or {}
        for an, asp in spec_attrs.items():
            if asp.get("required") and an not in b["attrs"]:
                errs.append(f"{where} {t}: missing required attr '{an}'")
            if an in b["attrs"] and asp.get("type") == "enum" and b["attrs"][an] not in asp["values"]:
                errs.append(f"{where} {t}: {an}='{b['attrs'][an]}' not in {asp['values']}")
        for an in b["attrs"]:
            if an not in spec_attrs:
                errs.append(f"{where} {t}: unknown attr '{an}'")

        body = defs[t].get("body")
        if body and body.get("required") and not b["body"]:
            errs.append(f"{where} {t}: required body is empty")

    for t in sorted(required - present):
        errs.append(f"doctype: required block '{t}' is missing")
    return errs


def main():
    path = sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, "out", "git-feature.plan.md")
    lines = open(path).readlines()
    defs = load_blocks()
    dt = load_doctype(doctype_of(path, lines))
    parsed = parse(lines)
    errs = validate(defs, dt, parsed)
    print(f"doc: {os.path.relpath(path, ROOT)}   docType: {dt['docType']}   blocks: {len(parsed)}")
    if errs:
        print(f"\nFAIL — {len(errs)} violation(s):")
        for e in errs:
            print("  x " + e)
        sys.exit(1)
    print("PASS — conforms to the catalog.")


if __name__ == "__main__":
    main()
