# S2.1b — GitHub-side stacked-PR choreography (verified against MgCohen/abox-server)

Throwaway validation of the Leg-A (clean merge-commit + retarget) happy path from
research/stacked-prs.md §8, run live with spike/-prefixed branches only. main,
claude/box-feature-substrate-tasks-4r033c, and PR #62 untouched. Date 2026-06-17. Actor: bot ABox-Agent.

## 1. Verified call sequence (tool → key params → key result)
| # | Tool | Params | Result |
|---|------|--------|--------|
| 1 | create_branch | branch=spike/box-x, from=main | ref at main 4ee3b39 |
| 2a | create_branch | branch=spike/phase-1, from=spike/box-x | at 4ee3b39 |
| 2b | create_or_update_file | spike/phase-1, spike-a.txt, "[SPIKE] phase 1" | commit e916501 |
| 3a | create_branch | branch=spike/phase-2, from=spike/phase-1 | at e916501 |
| 3b | create_or_update_file | spike/phase-2, spike-b.txt, "[SPIKE] phase 2" | commit c4f65a0 |
| 4a | create_pull_request | head=spike/phase-1, base=spike/box-x | PR #63 |
| 4b | create_pull_request | head=spike/phase-2, base=spike/phase-1 | PR #64 |
| 5 | pull_request_read get_diff #64 | — | only spike-b.txt |
| 6 | merge_pull_request #63 | merge_method=merge | {merged:true, sha:8f093c5} merge commit on box-x |
| 7 | update_pull_request #64 | base=spike/box-x | retarget OK; base now box-x@8f093c5 |
| 8 | pull_request_read get_diff/get/get_status #64 | — | diff still only spike-b.txt; changed_files:1 |

Create-onto-non-main-base works (base=spike/box-x and base=spike/phase-1 both accepted) — confirms
§2. Merge API returns {sha, merged, message}; sha is the merge commit and becomes the box-branch tip.
Retarget is a pure base-pointer PATCH (update_pull_request base=...) — no rebase, no commit rewrite.

## 2. PR-2 (#64) diff: before vs after retarget — the core proof
Before (base=spike/phase-1): new file spike-b.txt only.
After (base=spike/box-x@8f093c5): byte-for-byte identical — still ONLY spike-b.txt, changed_files:1,
commits:1. No phantom diff. Because #63 landed via a merge commit (not squash/rebase), phase-1's
e916501 is now an ancestor of spike/box-x (through merge commit 8f093c5); GitHub recomputes the diff
against the new merge-base and finds phase-1's content already present. Validates §1 and §8 Leg-A on
the real API.

mergeable_state after retarget = unstable. get_status=pending; get_check_runs: policy-guard success,
two build-test jobs in_progress. So unstable = mergeable, checks pending — NOT a conflict, NOT a dirty
merge-base. No branch protection/required-review on spike branches, so no approval gate exercised.

## 3. Auto-retarget / auto-close on base-branch delete — NOT TESTED (blocked)
Step 9 could not run. Branch deletion is impossible from this environment:
- The authenticated origin proxy returns HTTP 403 on every delete refspec (git push origin --delete,
  git push origin :refs/heads/<b>, single & batched, leaf & non-leaf). Proxy allows create/push, blocks
  ref deletion.
- The GitHub MCP server exposes no branch/ref delete tool (create_branch exists; no delete counterpart;
  delete_file only removes file contents).
- No gh CLI available.
Captured adjacent fact: auto-delete-head-on-merge is OFF for this repo — after merging #63, spike/phase-1
(e916501) still existed. Since step 7 retargeted #64 before any delete, #64 no longer referenced phase-1
as base — exactly the order §6 prescribes. The "PR still pointing at a deleted base" question (§6/§7
auto-retarget-vs-auto-close) remains verify-don't-rely and UNVERIFIED here.

## 4. Approval-dismissal-on-push — NOT TESTED
No branch protection/required-approval on spike branches, so force-push/merge-base approval dismissal is
not observable. Record as not-tested.

## 5. What could NOT be done (and why)
- Branch deletion (step 9 + mandatory cleanup): proxy 403; no MCP delete tool; no gh. Three spike/*
  branches remain on the remote (spike/box-x@8f093c5, spike/phase-1@e916501, spike/phase-2@c4f65a0).
  Parent/owner must delete these (GitHub UI Delete branch, or authenticated API with delete rights).
  PR #63 merged+closed, #64 closed — only the refs linger.
- Auto-retarget/auto-close-on-base-delete and approval-dismissal: not testable here.

## 6. Corrections / confirmations to research/stacked-prs.md
- §1/§2 happy path — CONFIRMED on real API. Merge-commit preserves parent commits as ancestors; retarget
  is pointer-only PATCH; descendant diff stays clean, no rebase. No correction.
- §2 create-PR-onto-non-main-base — CONFIRMED.
- §6 retarget-before-delete order — CONFIRMED correct and the only safe path here.
- §6/§7 auto-retarget/auto-close on base delete + native auto-cascade — STILL UNVERIFIED (couldn't delete
  branches). Keep flagged low-confidence; orchestrator must explicitly retarget, not depend on auto-behavior.
- NEW: mergeable_state:unstable = pending checks (distinct from dirty=conflict). Add to S2.2/S2.3 notes.
- NEW: auto-delete-head-on-merge OFF in this repo; don't assume merged heads disappear.

## Final state
- PRs: #63 merged+closed, #64 closed. #62 untouched.
- Remote branches lingering (deletion blocked from this env): spike/box-x, spike/phase-1, spike/phase-2 — owner cleanup owed.
- main, claude/box-feature-substrate-tasks-4r033c: untouched. Local working tree: untouched (by-refspec/API only).
