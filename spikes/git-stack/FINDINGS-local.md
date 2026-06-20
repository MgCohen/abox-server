# S2.1a — Local git-stack mechanics: FINDINGS (local, no network/GitHub)

> Throwaway spike. Proves the pure-git choreography behind stacked PRs in a
> temp repo with a bare repo as `origin`. No GitHub, no network, no `dotnet`,
> no touching the real repo's tree. git version **2.43.0**.
>
> Setup: `git init --bare remote.git` as `origin`; `clone1` is the
> orchestrator's working copy; `clone2` simulates a concurrent actor racing the
> remote. Initial commit on `box`; `phase-1` off `box` adds `a.txt`; `phase-2`
> off `phase-1` adds `b.txt`.

## Verdicts (one line each)

| # | Item | Verdict |
|---|------|---------|
| 1 | Merge-commit (`--no-ff`) preserves descendant ancestry → descendant retargets clean, no rebase | **PROVEN** |
| 2 | Rebuild cascade: `git rebase --onto <new> <old> phase-2` replays cleanly onto rebuilt parent | **PROVEN** |
| 3 | `--force-with-lease` rejects a stale clobber; plain `--force` overwrites; `--force-if-includes` adds background-fetch defense | **PROVEN** (incl. the lease-defeated-by-fetch case) |
| 4 | Phantom diff from stale base; three-dot vs two-dot; "clean" detection | **PROVEN** |

---

## Item 1 — Merge commit preserves ancestry (happy path)

Merged `phase-1` into `box` with a true merge commit:

```
$ git checkout box
$ git merge --no-ff -m "Merge phase-1 into box" phase-1
$ git log --oneline --graph box
*   871df27 Merge phase-1 into box
|\
| * 5b92dab phase-1: add a.txt
|/
* 3a8fa0d initial commit
```

**(a) phase-1's tip is now an ancestor of box** — no rebase needed to retarget descendants:

```
$ git merge-base --is-ancestor 5b92dab box ; echo $?
0          # 0 = IS an ancestor
```

**(b) retargeting phase-2 onto box needs no content rebase** — the three-dot
diff contains ONLY `b.txt` (phase-1's `a.txt` is already on box, so no
phantom):

```
$ git diff --name-only box...phase-2
b.txt
$ git diff --stat box...phase-2
 b.txt | 1 +
 1 file changed, 1 insertion(+)
```

And merging phase-2 into box is clean:

```
$ git checkout box && git merge --no-ff -m "Merge phase-2 into box" phase-2   # exit 0, clean
$ ls *.txt
a.txt  b.txt  root.txt
```

**Takeaway:** with a merge commit at Level-1, the merged parent's commits stay
ancestors of the box branch, so a clean descendant only needs its *base pointer*
moved (a PR `PATCH base`), never a `git rebase`. Confirms research §1 + §8 Leg A.

---

## Item 2 — Rebuild cascade (reject path; SHAs change)

Rebuilt `phase-1` (amended its commit → new SHA, new `a.txt` content), then
replayed `phase-2` onto the rebuilt parent with `rebase --onto`:

```
$ git checkout phase-1
$ echo "a contents v2 (rebuilt)" > a.txt && git add a.txt
$ git commit --amend -m "phase-1: add a.txt (rebuilt)"
# P1_OLD=5b92dab...  ->  P1_NEW=7ed2b24...   (SHA changed, confirmed)

$ git rebase --onto 7ed2b24 5b92dab phase-2
Successfully rebased and updated refs/heads/phase-2.

$ git log --oneline --graph phase-2
* 58ad254 phase-2: add b.txt
* 7ed2b24 phase-1: add a.txt (rebuilt)     # <- sits on the rebuilt parent
* 3a8fa0d initial commit

$ git diff --name-only phase-1...phase-2
b.txt                                       # still just b.txt
$ git rev-parse phase-2^                     # phase-2's parent ==
7ed2b24...                                   # == P1_NEW
```

phase-2 replays cleanly and its diff is still only `b.txt`. Confirms research
§3 + §8 Leg B. The `--onto` form is exact: `<new-base> <old-parent-tip>
<branch>` — the `<old-parent-tip>` is the *exclusive* lower bound, so only
phase-2's unique commits replay.

---

## Item 3 — Force-push safety (the headline finding)

Race setup: clone1 pushes `phase-2` to remote (tracking ref synced). clone2
clones, advances `phase-2` on the remote (adds `c.txt`). clone1 does NOT fetch,
so its `origin/phase-2` tracking ref is now stale.

```
clone1 origin/phase-2 (stale) = 58ad254
clone1 local  phase-2         = 58ad254 -> rebuilt to 48a0dc5
actual remote phase-2         = 8662417   # clone2's new commit
```

**(a) `--force-with-lease` REJECTS the stale clobber:**

```
$ git push --force-with-lease origin phase-2
 ! [rejected]        phase-2 -> phase-2 (stale info)
error: failed to push some refs to '../remote.git'      # exit 1
```

**(b) plain `--force` WOULD overwrite (clone2's commit lost):**

```
$ git push --force origin phase-2
 + 8662417...48a0dc5 phase-2 -> phase-2 (forced update)  # exit 0
# remote phase-2: 8662417 -> 48a0dc5  — clone2's c.txt commit is GONE
```

**(c) `--force-with-lease` ALONE is defeated by a background fetch** — this is
the exact failure `--force-if-includes` exists to catch. clone2 advances the
remote again (`d.txt`); then a *background* `git fetch` in clone1 updates
`origin/phase-2` to match the remote **without** the local branch integrating
that work:

```
clone1 origin/phase-2 (post bg-fetch) = f101d37   # matches remote
clone1 local  phase-2                 = 48a0dc5   # never integrated d.txt

$ git push --force-with-lease origin phase-2
 + f101d37...31e8730 phase-2 -> phase-2 (forced update)   # exit 0 — CLOBBERED d.txt!
```

The lease passed because the lease compares against the (now fetch-updated)
tracking ref, which already matches the remote — so it sees "no surprise" even
though the user never saw or integrated `d.txt`.

**(d) `--force-with-lease --force-if-includes` REJECTS that same case:**

```
clone1 origin/phase-2 (post bg-fetch) = a2c3450   # remote advanced again (e.txt)
clone1 local  phase-2                 = 31e8730   # never integrated e.txt

$ git push --force-with-lease --force-if-includes origin phase-2
 ! [rejected]        phase-2 -> phase-2 (remote ref updated since checkout)
error: failed to push some refs to '../remote.git'      # exit 1
hint: ... the tip of the remote-tracking branch has been updated since the
hint: last checkout. ... use 'git pull' before pushing again.
```

`--force-if-includes` additionally checks that the commits behind the
remote-tracking ref are *reachable from a reflog entry of the local branch*
(i.e. the local branch actually saw/integrated them since the last checkout).
A background fetch updates the tracking ref but leaves no such reflog evidence
on the local branch → rejected. **This is why S2.3 must add `--force-if-includes`
to `Domain/Git/Git.cs` PushOp (currently lease-only, line ~81).**

---

## Item 4 — Phantom diff from a stale base; detecting "clean"

Two distinct phantom flavors, and the three-dot/two-dot distinction matters:

**4a — base MOVED forward (new unrelated commits), descendant not restacked.**
`git diff base..feat` (two-dot) is misleading; `git diff base...feat`
(three-dot, vs the merge-base) still isolates the feature *as long as the base
only fast-forwarded* (no rewrite):

```
# base gained unrelated1.txt, unrelated2.txt while feat's PR was open
$ git diff --name-status demo-base..demo-feat      # TWO-DOT — the phantom view
A  feature.txt
D  unrelated1.txt                                   # base's commits look "removed"
D  unrelated2.txt
$ git diff --name-status demo-base...demo-feat      # THREE-DOT (what a PR shows)
A  feature.txt                                       # clean — diff is vs merge-base
```

**4b — base REBUILT (SHAs/content rewritten), descendant not restacked.** Now
even the three-dot diff leaks a phantom, because the merge-base diverged from
the new base tip:

```
# s-base's shared.txt rewritten v1 -> v2 via amend (new SHA)
$ git diff --name-status s-base...s-feat            # THREE-DOT
A  feat2.txt
A  shared.txt                                        # PHANTOM: feat carries old v1 vs base's v2
# merge-base = the pre-rewrite commit (diverged from s-base tip)
```

**Restack clears it** (`rebase --onto` the new base), and the three-dot diff
returns to just the feature's own files:

```
$ git rebase --onto s-base <old-s-base> s-feat
$ git diff --name-status s-base...s-feat
A  feat2.txt                                         # clean
# git merge-base s-base s-feat  ==  s-base tip
```

**Detection rule for "clean":** a descendant PR (`<base>`/`<head>`) is clean iff
1. `git diff <base>...<head>` (three-dot) lists only its own files, **and**
2. `git merge-base <base> <head> == git rev-parse <base>` (no divergence — base
   is a true ancestor of head; equivalently `git merge-base --is-ancestor <base> <head>` exits 0).

Condition (2) is the robust one: it catches the 4b rewrite case where the
three-dot file list could still look plausible. The merge-commit happy path
(Item 1) satisfies (2) for free, which is exactly why it only needs a retarget.

---

## Gotchas that bit

- **Three-dot diff is NOT automatically "clean."** It is robust against a base
  that merely *fast-forwarded* (4a) but leaks a phantom once the base is
  *rewritten* (4b). Don't treat `git diff base...head` file-count alone as the
  cleanliness oracle — also assert `merge-base == base tip`. (Nuance to add to
  research §6.)
- **`--force-with-lease` is genuinely defeated by a background `git fetch`**,
  reproduced here (Item 3c). If the orchestrator (or any cron/IDE) fetches
  between checkout and push, lease-only silently clobbers. `--force-if-includes`
  is not optional for an automated cascade.
- **Bare-remote HEAD warning** (`remote HEAD refers to nonexistent ref`) appears
  when cloning a bare repo whose `HEAD` points at an unborn default branch
  (`main`) while our branches are `box`/`phase-*`. Cosmetic; harmless. Avoid by
  setting the bare repo's `HEAD` to an existing branch if it ever matters.
- **`rebase --onto` arg order is a foot-gun.** It is
  `--onto <new-base> <upstream/old-parent-tip> <branch>` — the middle arg is the
  *exclusive* boundary (old parent), not the new base. Swapping them silently
  replays the wrong commit range.

---

## Cross-check vs `research/stacked-prs.md`

**§1 (merge method → SHA rewrite):**
- CONFIRMED: a true **merge commit** keeps the merged phase's commits as
  ancestors of the box branch → descendant retargets clean, no content rebase
  (Item 1). The table's "Merge commit → Yes, descendants keep their ancestor" is
  exactly what `merge-base --is-ancestor` returned 0 for.
- CONFIRMED: amend/rebuild produces new SHAs and forces the `rebase --onto`
  cascade (Items 2, 4b).

**§6 (pitfalls):**
- CONFIRMED: "`--force` blindly overwrites concurrent pushes; use
  `--force-with-lease --force-if-includes` (lease alone is defeated by background
  fetches)." Reproduced verbatim — lease alone clobbered after a background
  fetch (3c); `--force-if-includes` rejected the same race (3d). This claim is
  now empirically backed, not just cited.
- CONFIRMED: "Stale base → phantom diff … looks wrong until rebased; keep
  descendants restacked." Shown in Item 4.
- **NUANCE to add (not a contradiction):** §6's phantom-diff bullet should
  distinguish the *fast-forwarded base* case (three-dot already clean) from the
  *rewritten base* case (three-dot leaks). The reliable cleanliness check is
  `merge-base <base> <head> == <base> tip`, not the three-dot file list alone.

**§8 (spike sequence):**
- CONFIRMED for the local legs: Leg A's "phase-1's commits are now ancestors of
  box, diff clean, no phantom" (Item 1) and Leg B's "rebuild → `rebase --onto`
  → clean against rebuilt parent" (Item 2) both reproduce as written. The
  GitHub-API pieces of §8 (PATCH base, PUT merge, head-branch delete) are S2.1b,
  out of scope here.

No claim in §1/§6/§8 was **contradicted**. The only correction is the
three-dot-diff cleanliness nuance above.
