# eval-staleness

A cache key for "have I already eval'd this feature?" — so you only re-run a
quality eval on features whose code actually changed.

## Why content hashes, not dates

`grep` emits no timestamps, and filesystem mtimes are useless here: the repo is
cloned fresh into an ephemeral container, so every file's mtime is the clone
time, not its real edit time. Git commit *dates* survive clones but still move
on reverts, rebases, and whitespace-only recommits.

So we key on **content**: for each feature, `git ls-files` enumerates its tracked
files and `git hash-object` hashes each file's working-tree bytes; the per-file
hashes fold into one feature hash. A feature is STALE iff that hash differs from
the one recorded at its last eval — a revert that restores prior bytes reads as
FRESH.

## Map features → paths

Edit `features.json`. Keys are your feature ids; `paths` are files, directories,
or git pathspec globs (`*Claude*`, `**/Git*.cs`):

```json
{
  "C1-claude-agent": { "paths": ["src/RemoteAgents/Agents/*Claude*"] }
}
```

## Use

```bash
# What needs eval? ( * = changed since bookmark, + = never bookmarked )
python3 tools/eval-staleness/eval_staleness.py status

# Just the ids, for scripting an eval loop:
for f in $(python3 tools/eval-staleness/eval_staleness.py stale); do
  run_quality_eval "$f"
  python3 tools/eval-staleness/eval_staleness.py bookmark "$f" --result PASS
done

# Or bookmark everything after a full eval pass:
python3 tools/eval-staleness/eval_staleness.py bookmark --all
```

`bookmarks.json` is the persisted state — **commit it** so the cache survives
the next clone. `--result` stores an optional note (score, PASS/FAIL) alongside
the hash and timestamp.
