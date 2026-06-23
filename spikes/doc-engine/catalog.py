#!/usr/bin/env python3
"""Decision matrices — the `short` one-liners a selector reads to choose.

Two levels, mirroring each other: pick a doc type, then pick its blocks.

    python3 catalog.py                # doc-type matrix + all blocks
    python3 catalog.py feature-plan    # the blocks available to one doc type
"""
import sys, os, glob
import yaml
from validate import load_blocks, load_doctype

ROOT = os.path.dirname(os.path.abspath(__file__))


def all_doctypes():
    return [yaml.safe_load(open(f))
            for f in sorted(glob.glob(os.path.join(ROOT, "doctypes", "*.yaml")))]


def row(name, short, w):
    return f"  {name.ljust(w)}  — {short or ''}"


def main():
    defs = load_blocks()
    if len(sys.argv) > 1:
        dt = load_doctype(sys.argv[1])
        names = dt.get("blocks") or []
        w = max(len(n) for n in names)
        print(f"{dt['docType']} blocks — pick what carries substance:")
        for n in names:
            print(row(n, defs[n].get("short"), w))
        return

    dts = all_doctypes()
    w = max(len(d["docType"]) for d in dts)
    print("Doc types — pick one:")
    for d in dts:
        print(row(d["docType"], d.get("short"), w))

    w2 = max(len(t) for t in defs)
    print("\nBlocks — pick what carries substance:")
    for t in sorted(defs):
        print(row(t, defs[t].get("short"), w2))


if __name__ == "__main__":
    main()
