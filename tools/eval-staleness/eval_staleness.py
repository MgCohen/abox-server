#!/usr/bin/env python3
"""Report which features need re-evaluating, by comparing a content hash of
each feature's files against the hash recorded the last time it was eval'd.

Content, not dates: git mtimes reset to clone-time in ephemeral containers, so
we hash the working-tree bytes of each file (git ls-files + git hash-object).
A feature is STALE iff its bytes changed since the bookmark — reverts and
whitespace-only recommits that restore prior content read as FRESH.
"""

import argparse
import hashlib
import json
import subprocess
import sys
from datetime import datetime, timezone
from pathlib import Path

REPO_ROOT = Path(
    subprocess.run(
        ["git", "rev-parse", "--show-toplevel"],
        check=True,
        capture_output=True,
        text=True,
    ).stdout.strip()
)

NEW, FRESH, STALE = "NEW", "FRESH", "STALE"


def git(*args: str) -> str:
    result = subprocess.run(
        ["git", *args], cwd=REPO_ROOT, capture_output=True, text=True
    )
    if result.returncode != 0:
        raise RuntimeError(f"git {' '.join(args)} failed: {result.stderr.strip()}")
    return result.stdout


def files_for(patterns: list[str]) -> list[str]:
    out = git("ls-files", "-z", "--", *patterns)
    return sorted(p for p in out.split("\0") if p)


def feature_hash(patterns: list[str]) -> str:
    """One hash over the feature's whole fileset: each tracked file's path plus
    its git blob hash (working-tree content), folded together in path order."""
    files = files_for(patterns)
    if not files:
        return ""
    digest = hashlib.sha256()
    for path in files:
        blob = git("hash-object", path).strip()
        digest.update(f"{path}\0{blob}\0".encode())
    return digest.hexdigest()[:16]


def load(path: Path) -> dict:
    if not path.exists():
        return {}
    return json.loads(path.read_text())


def evaluate(features: dict, bookmarks: dict) -> list[dict]:
    rows = []
    for fid, spec in features.items():
        patterns = spec["paths"] if isinstance(spec, dict) else spec
        current = feature_hash(patterns)
        mark = bookmarks.get(fid)
        if mark is None:
            state = NEW
        elif mark.get("hash") == current:
            state = FRESH
        else:
            state = STALE
        rows.append(
            {
                "feature": fid,
                "state": state,
                "hash": current,
                "files": len(files_for(patterns)),
                "last_eval": (mark or {}).get("evaluated_at", ""),
            }
        )
    return rows


def cmd_status(args) -> int:
    features = load(args.features)
    bookmarks = load(args.bookmarks)
    rows = evaluate(features, bookmarks)
    if args.json:
        print(json.dumps(rows, indent=2))
        return 0
    width = max((len(r["feature"]) for r in rows), default=7)
    for r in rows:
        flag = {NEW: "+", FRESH: " ", STALE: "*"}[r["state"]]
        last = r["last_eval"] or "never"
        print(
            f"{flag} {r['feature']:<{width}}  {r['state']:<5}  "
            f"{r['files']:>3} files  last: {last}"
        )
    stale = [r["feature"] for r in rows if r["state"] != FRESH]
    print(f"\n{len(stale)} of {len(rows)} need eval: {' '.join(stale) or '(none)'}")
    return 0


def cmd_stale(args) -> int:
    features = load(args.features)
    bookmarks = load(args.bookmarks)
    for r in evaluate(features, bookmarks):
        if r["state"] != FRESH:
            print(r["feature"])
    return 0


def cmd_bookmark(args) -> int:
    features = load(args.features)
    bookmarks = load(args.bookmarks)
    targets = (
        list(features)
        if args.all
        else [f for f in args.feature if f in features]
    )
    unknown = [] if args.all else [f for f in args.feature if f not in features]
    if unknown:
        print(f"unknown feature(s): {' '.join(unknown)}", file=sys.stderr)
        return 1
    now = datetime.now(timezone.utc).isoformat(timespec="seconds")
    for fid in targets:
        patterns = features[fid]["paths"] if isinstance(features[fid], dict) else features[fid]
        entry = {"hash": feature_hash(patterns), "evaluated_at": now}
        if args.result:
            entry["result"] = args.result
        bookmarks[fid] = entry
    args.bookmarks.write_text(json.dumps(bookmarks, indent=2, sort_keys=True) + "\n")
    print(f"bookmarked {len(targets)}: {' '.join(targets)}")
    return 0


def main() -> int:
    here = Path(__file__).parent
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--features", type=Path, default=here / "features.json")
    parser.add_argument("--bookmarks", type=Path, default=here / "bookmarks.json")
    sub = parser.add_subparsers(dest="cmd", required=True)

    s = sub.add_parser("status", help="table of every feature's state")
    s.add_argument("--json", action="store_true")
    s.set_defaults(func=cmd_status)

    st = sub.add_parser("stale", help="print ids needing eval, one per line")
    st.set_defaults(func=cmd_stale)

    b = sub.add_parser("bookmark", help="record current hash after evaluating")
    b.add_argument("feature", nargs="*")
    b.add_argument("--all", action="store_true")
    b.add_argument("--result", help="optional note, e.g. score or PASS/FAIL")
    b.set_defaults(func=cmd_bookmark)

    args = parser.parse_args()
    return args.func(args)


if __name__ == "__main__":
    sys.exit(main())
