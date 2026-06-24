#!/usr/bin/env python3
"""Spike validator: enforce a block-structured doc against its doc-type catalog.

Singleton blocks are top-level headers; collection blocks are grouped:

    ## Summary                     <- singleton block (type from the header)
    <!-- id: 1 -->

    body ...

    ## Decisions                   <- group header for a collection type
    ### Merge-commit for Level-1   <- member block (type inherited from the group)
    <!-- id: 6 -->

    body ...

A block schema with `collection: true` declares its `group:` label; the engine
maps "## <group>" to that type and reads its "### <title>" members. Doc-type
catalogs list allowed block types + (at most) a required set — no min/max.

    python3 validate.py out/git-feature.plan.md
"""
import sys, re, glob, os
import yaml

ROOT = os.path.dirname(os.path.abspath(__file__))
H2_RE = re.compile(r'^##\s+(.+?)\s*$')
H3_RE = re.compile(r'^###\s+(.+?)\s*$')
ID_RE = re.compile(r'^<!--\s*id:\s*(\S+)\s*-->\s*$')
ATTR_RE = re.compile(r'^([\w-]+):\s*(.+?)\s*$')


def slug(label):
    return label.strip().lower().replace(" ", "-")


def norm_field(spec, default_required):
    # One canonical form, no shorthand: a field spec is always a map in block
    # notation (`type: markdown` / `enum: [...]`). Agent-first — uniform
    # structure beats a terser bare-string form.
    if not isinstance(spec, dict):
        raise SystemExit(f"field spec must be a map, got {spec!r} — "
                         "write `type:`/`enum:` explicitly (no bare-string shorthand)")
    d = dict(spec)
    if "enum" in d:
        d["values"] = d.pop("enum")
        d.setdefault("type", "enum")
    d.setdefault("required", default_required)
    return d


def load_blocks():
    out = {}
    for f in glob.glob(os.path.join(ROOT, "blocks", "*.yaml")):
        d = yaml.safe_load(open(f))
        out[d["type"]] = d
    return out


def load_doctype(name):
    return yaml.safe_load(open(os.path.join(ROOT, "doctypes", name + ".yaml")))


def parse_frontmatter(lines):
    # A leading `---` YAML block is the doc's front matter — visible (unlike a
    # comment), and the home for `docType` plus any doc-level attrs.
    if not lines or lines[0].strip() != "---":
        return {}
    buf = []
    for raw in lines[1:]:
        if raw.strip() == "---":
            break
        buf.append(raw)
    return yaml.safe_load("".join(buf)) or {}


def doctype_of(path, lines):
    dt = parse_frontmatter(lines).get("docType")
    if not dt:
        raise SystemExit("no `docType` in the `---` front matter of " + path)
    return dt


def label_maps(defs):
    singleton, group = {}, {}
    for t, d in defs.items():
        if d.get("collection"):
            group[slug(d.get("group", t))] = t
        else:
            singleton[slug(t)] = t
    return singleton, group


def parse(lines, defs):
    singleton, group = label_maps(defs)
    blocks, groups_seen = [], []
    cur, mode, gtype, meta = None, None, None, False

    def new(t, title, grp, unknown=False):
        return {"type": t, "title": title, "group": grp, "id": "",
                "attrs": {}, "unknown": unknown, "lines": []}

    def close():
        nonlocal cur
        if cur is not None:
            cur["body"] = "\n".join(cur.pop("lines")).strip()
            blocks.append(cur); cur = None

    for raw in lines:
        line = raw.rstrip("\n")
        m2 = H2_RE.match(line)
        m3 = None if m2 else H3_RE.match(line)
        if m2:
            close()
            lab = slug(m2.group(1))
            if lab in group:
                mode, gtype = "group", group[lab]
                groups_seen.append(gtype)
            elif lab in singleton:
                mode, gtype = "single", None
                cur = new(singleton[lab], "", None); meta = True
            else:
                mode, gtype = "single", None
                cur = new(lab, "", None, unknown=True); meta = True
            continue
        if m3:
            if mode == "group":
                close()
                cur = new(gtype, m3.group(1), defs[gtype].get("group")); meta = True
                continue
            if cur is not None:
                cur["lines"].append(line)
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
            meta = False
        cur["lines"].append(line)
    close()
    return blocks, groups_seen


def validate(defs, dt, blocks, groups_seen, fm):
    errs = []
    allowed = set(dt.get("blocks") or [])
    required = set(dt.get("required") or [])
    seen_ids, present = set(), set()

    for i, b in enumerate(blocks):
        where = f"#{i+1} (id={b['id'] or '?'})"
        if not b["id"]:
            errs.append(f"{where}: missing `<!-- id: -->`")
        elif b["id"] in seen_ids:
            errs.append(f"{where}: duplicate id '{b['id']}'")
        seen_ids.add(b["id"])

        t = b["type"]; present.add(t)
        if b["unknown"] or t not in defs:
            errs.append(f"{where}: unknown block/section '{t}'"); continue
        if t not in allowed:
            errs.append(f"{where}: '{t}' not in the '{dt['docType']}' catalog")

        spec_attrs = defs[t].get("attrs") or {}
        for an, raw in spec_attrs.items():
            asp = norm_field(raw, False)
            if asp["required"] and an not in b["attrs"]:
                errs.append(f"{where} {t}: missing required attr '{an}'")
            if an in b["attrs"] and asp["type"] == "enum" and b["attrs"][an] not in asp["values"]:
                errs.append(f"{where} {t}: {an}='{b['attrs'][an]}' not in {asp['values']}")
        for an in b["attrs"]:
            if an not in spec_attrs:
                errs.append(f"{where} {t}: unknown attr '{an}'")

        bspec = defs[t].get("body")
        if bspec:
            body = norm_field(bspec, True)
            if body["required"] and not b["body"]:
                errs.append(f"{where} {t}: required body is empty")

    counts = {}
    for b in blocks:
        counts[b["type"]] = counts.get(b["type"], 0) + 1
    for gt in groups_seen:
        if counts.get(gt, 0) == 0:
            errs.append(f"group '{defs[gt]['group']}' has no members")

    for t in sorted(required - present):
        errs.append(f"doctype: required block '{t}' is missing")

    for an, raw in (dt.get("attrs") or {}).items():
        asp = norm_field(raw, False)
        if asp["required"] and an not in fm:
            errs.append(f"doc: missing required front-matter attr '{an}'")
        if an in fm and asp["type"] == "enum" and fm[an] not in asp["values"]:
            errs.append(f"doc: front-matter {an}='{fm[an]}' not in {asp['values']}")
    return errs


def main():
    path = sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, "out", "git-feature.plan.md")
    lines = open(path).readlines()
    defs = load_blocks()
    dt = load_doctype(doctype_of(path, lines))
    blocks, groups_seen = parse(lines, defs)
    fm = parse_frontmatter(lines)
    errs = validate(defs, dt, blocks, groups_seen, fm)
    print(f"doc: {os.path.relpath(path, ROOT)}   docType: {dt['docType']}   blocks: {len(blocks)}")
    if errs:
        print(f"\nFAIL — {len(errs)} violation(s):")
        for e in errs:
            print("  x " + e)
        sys.exit(1)
    print("PASS — conforms to the catalog.")


if __name__ == "__main__":
    main()
