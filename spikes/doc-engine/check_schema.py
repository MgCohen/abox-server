#!/usr/bin/env python3
"""Floor enforcement for the DEFINITIONS themselves.

Every kind (`kinds/*.yaml`) declares the fields its definitions must carry and
any cross-field constraints. One meta-schema, `_schema/kind.schema.yaml`, says
what a kind file looks like — and is itself a kind, so it conforms to itself and
the regress stops. This file knows no kind names: add a kind by adding data.

    kind.schema.yaml  →  kinds/*.yaml  →  blocks/*.yaml, doctypes/*.yaml

    python3 check_schema.py
"""
import sys, os, glob
import yaml

ROOT = os.path.dirname(os.path.abspath(__file__))
TYPES = {"markdown", "string", "enum"}


def load(path):
    return yaml.safe_load(open(path))


def rel(path):
    return os.path.relpath(path, ROOT)


def is_typespec(v):
    if isinstance(v, str):
        return v in TYPES
    if isinstance(v, dict):
        return (v.get("type") or ("enum" if "enum" in v else None)) in TYPES
    return False


def is_fieldspec(v):
    return isinstance(v, dict) and isinstance(v.get("kind"), str)


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
        return [f"{name}.{k}: expected a string rule"
                for k, v in value.items() if not isinstance(v, str)]
    if kind == "fieldmap":
        if not isinstance(value, dict):
            return [f"{name}: expected map (field: spec)"]
        return [f"{name}.{fn}: each field spec needs a `kind`"
                for fn, fv in value.items() if not is_fieldspec(fv)]
    return []


def check(defn, fields):
    errs = []
    for fn, spec in fields.items():
        if spec.get("required") and fn not in defn:
            errs.append(f"missing required field '{fn}'")
        if fn in defn:
            errs += check_field(fn, spec, defn[fn])
    for fn in defn:
        if fn not in fields:
            errs.append(f"unknown field '{fn}'")
    return errs


CONSTRAINTS = {
    "requires_when": lambda d, c:
        [f"`{c['when']}` is set but `{c['then']}` is missing"]
        if d.get(c["when"]) and not d.get(c["then"]) else [],
    "subset": lambda d, c:
        [f"`{c['of']}` is not a subset of `{c['in']}`: "
         f"{sorted(set(d.get(c['of']) or []) - set(d.get(c['in']) or []))}"]
        if set(d.get(c["of"]) or []) - set(d.get(c["in"]) or []) else [],
}


def run_constraints(defn, constraints):
    errs = []
    for c in constraints or []:
        fn = CONSTRAINTS.get(c.get("rule"))
        if fn is None:
            errs.append(f"unknown constraint rule '{c.get('rule')}'")
        else:
            errs += fn(defn, c)
    return errs


def report(name, errs):
    for e in errs:
        print(f"  x {name}: {e}")
    return len(errs)


def conform(defn, kind, path):
    return report(rel(path), check(defn, kind["fields"]) + run_constraints(defn, kind.get("constraints")))


def main():
    floor_path = os.path.join(ROOT, "_schema", "kind.schema.yaml")
    floor = load(floor_path)
    total = conform(floor, floor, floor_path)
    for kf in sorted(glob.glob(os.path.join(ROOT, floor["defs"]))):
        kind = load(kf)
        total += conform(kind, floor, kf)
        for df in sorted(glob.glob(os.path.join(ROOT, kind["defs"]))):
            total += conform(load(df), kind, df)
    if total:
        print(f"\nFAIL — {total} definition violation(s).")
        sys.exit(1)
    print("PASS — meta-schema, kinds, and every definition conform.")


if __name__ == "__main__":
    main()
