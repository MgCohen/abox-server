#!/usr/bin/env python3
"""Floor enforcement for the DEFINITIONS themselves.

Every blocks/*.yaml must conform to _schema/block.schema.yaml, and every
doctypes/*.yaml to _schema/doctype.schema.yaml. This is the meta-schema layer:
the schema the schemas follow, so the whole system is structured top to bottom.

    python3 check_schema.py
"""
import sys, os, glob
import yaml

ROOT = os.path.dirname(os.path.abspath(__file__))
TYPES = {"markdown", "string", "enum"}   # the field-type vocabulary in use


def load(path):
    return yaml.safe_load(open(path))


def is_typespec(v):
    if isinstance(v, str):
        return v in TYPES
    if isinstance(v, dict):
        t = v.get("type") or ("enum" if "enum" in v else None)
        return t in TYPES
    return False


def check_field(name, spec, value):
    kind = spec["kind"]
    if kind == "string" and not isinstance(value, str):
        return [f"{name}: expected string"]
    if kind == "bool" and not isinstance(value, bool):
        return [f"{name}: expected bool"]
    if kind == "list":
        if not isinstance(value, list):
            return [f"{name}: expected list"]
        if spec.get("of") == "string" and not all(isinstance(x, str) for x in value):
            return [f"{name}: expected a list of strings"]
    if kind == "typespec" and not is_typespec(value):
        return [f"{name}: type must be one of {sorted(TYPES)}"]
    if kind == "attrs":
        if not isinstance(value, dict):
            return [f"{name}: expected map"]
        return [f"{name}.{an}: type must be one of {sorted(TYPES)}"
                for an, av in value.items() if not is_typespec(av)]
    if kind == "strmap":
        if not isinstance(value, dict):
            return [f"{name}: expected map (id: rule)"]
        return [f"{name}.{k}: expected a string rule" for k, v in value.items()
                if not isinstance(v, str)]
    return []


def check(defn, meta, kind):
    errs, fields = [], meta["fields"]
    for fn, spec in fields.items():
        if spec.get("required") and fn not in defn:
            errs.append(f"missing required field '{fn}'")
        if fn in defn:
            errs += check_field(fn, spec, defn[fn])
    for fn in defn:
        if fn not in fields:
            errs.append(f"unknown field '{fn}'")
    if kind == "block" and defn.get("collection") and not defn.get("group"):
        errs.append("a collection block must declare a `group`")
    if kind == "doctype":
        extra = set(defn.get("required") or []) - set(defn.get("blocks") or [])
        if extra:
            errs.append(f"`required` not in `blocks` catalog: {sorted(extra)}")
    return errs


def main():
    block_meta = load(os.path.join(ROOT, "_schema", "block.schema.yaml"))
    doctype_meta = load(os.path.join(ROOT, "_schema", "doctype.schema.yaml"))
    total = 0
    for f in sorted(glob.glob(os.path.join(ROOT, "blocks", "*.yaml"))):
        total += report(f, check(load(f), block_meta, "block"))
    for f in sorted(glob.glob(os.path.join(ROOT, "doctypes", "*.yaml"))):
        total += report(f, check(load(f), doctype_meta, "doctype"))
    if total:
        print(f"\nFAIL — {total} definition violation(s).")
        sys.exit(1)
    print("PASS — every block and doc-type definition conforms to its meta-schema.")


def report(f, errs):
    for e in errs:
        print(f"  x {os.path.relpath(f, ROOT)}: {e}")
    return len(errs)


if __name__ == "__main__":
    main()
