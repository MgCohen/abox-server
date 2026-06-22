# Claude Design prompt — Core operational loop (Home · Inbox · Flow Control · Flow Builder)

> Paste everything below the line into Claude Design. Derived from
> `PLANS/product-ui-spec.md` §8. Covers the 4 screens of the core loop.

---

Design a desktop-first web app called **Orchestrator** — a control panel for running and supervising AI coding agents. Generate **four connected screens** that share one app shell.

## Product context
Orchestrator is mission control for a team of AI software engineers. A single developer launches **flows** (multi-agent workflows) that run Claude and Codex agents against local code projects — the agents write code, review each other, run evaluations, and move work through a software-development lifecycle. The user watches flows run live and steps in to **answer questions or approve actions** whenever an agent needs a human. It's used mostly at a desk, sometimes from a phone over VPN.

## Design language
- Modern developer-tool aesthetic — in the spirit of Linear, Vercel, Railway, and GitHub. Calm, precise, information-dense but never cluttered.
- **Dark theme primary** (near-black backgrounds, subtly elevated surfaces, hairline borders). A light variant is welcome but optimize the dark one.
- Typography: a clean grotesk sans (e.g. Inter) for UI; a **monospace** (e.g. JetBrains Mono) for code, terminal output, IDs, hashes, and metrics.
- One restrained accent color (electric indigo/violet) for primary actions and active nav.
- **Consistent status system used everywhere** — color + a small dot/pill:
  - Running = blue (pulsing dot) · Blocked / Needs-human = amber · Success / Done = green · Failed = red · Idle / Queued = gray.
- Components to lean on: cards with soft radius, status pills, compact tables, vertical timelines, split panes, slide-over panels, inline-editable fields, code/diff blocks, and a terminal surface.
- Use **realistic sample content**, never lorem ipsum.

## Shared app shell (on every screen)
- **Left sidebar nav:** Home · Flows · Agents · Projects · Evaluation · Schedules · Insights · Settings. Active item marked with the accent color. Collapsible to an icon rail.
- **Top bar:** a project context switcher (current: **“Card Framework”**), a global search box, a provider/usage pill (**“Claude · 62% of weekly limit”**), a **“+ Quick Chat”** button, and an **Attention badge** showing a count (**“3”**) that glows amber when items are waiting. The Attention badge is the single most important always-visible signal.

---

## Screen 1 — Home / Command Center
The landing screen. Answers at a glance: *what needs me · what’s running · what next.* Top to bottom:

1. **Needs Attention** strip (amber-tinted container, top priority) — 3 rows, each with an inline control:
   - “Implementer · Card Framework — **Permission:** run `git push origin feature/deck-shuffle`?” → [Approve] [Deny] · 2m ago
   - “Reviewer (Codex) · Scaffold — **Question:** Empty-state — skeleton or spinner?” → [Answer…] · 8m ago
   - “SDLC Flow · Gear-Engine — **Signoff:** approve feature brief *Inventory slots*?” → [Review] · 1h ago
2. **Active Runs** — a grid of run cards:
   - “Build Deck Shuffler · Card Framework — **running** · phase 2/4 Implementation · 4m · $0.42” (blue pulsing dot; faint last line: *writing CardShuffler.cs…*)
   - “Nightly Quality Loop · Scaffold — **running** · scanning · 12m”
   - “Review PR #128 · Gear-Engine — **blocked** (awaiting human)” (amber)
3. **Quick Start** — three large buttons: New Quick Chat · Run a Flow · Add Project.
4. **Recent Activity** — compact reverse-chron feed: “✓ Feature *Card hover* completed — eval 92/100 · 20m” · “↻ Quality loop reverted change to Deck.cs (no improvement) · 1h” · “⎇ PR #127 merged · 2h”.
5. **Glance KPIs** — small stat tiles: Runs today **14** · Success rate **86%** · Awaiting me **3** · Usage **62%** of weekly cap.

Show an empty state (“Nothing’s waiting on you ✅”) variant for the Attention strip.

## Screen 2 — Attention Inbox
The one place every “blocked on a human” item lands, across all flows. Full-page queue.
- **Left filter rail:** by project, by flow, by type (Questions · Permissions · Signoffs), by urgency. A sort toggle “Blocking most runs”.
- **Main list:** grouped, expandable rows. Each shows source (flow · agent · project), the ask, age, and the correct inline control:
  - **Permission** item — the exact tool call in a mono block (`Bash › git push origin feature/deck-shuffle`) with [Approve] [Deny] [Always allow this].
  - **Question** item — the agent’s question with an answer textarea, or multiple-choice chips if it’s a structured question.
  - **Signoff** item — an artifact summary with [Review & approve] that opens a slide-over containing the diff/brief.
- A **“Weekly ADR triage”** batch group with [Approve all] and [Review each].
- Every row has a **“Jump to run →”** link.
- Empty state: a calm “Nothing’s waiting on you ✅”.

## Screen 3 — Flow Control Center (single run)
Everything about one running flow: status, live output, interaction, control. Run shown: **“Build Deck Shuffler” · Card Framework**.
- **Header bar:** flow name · project · status pill **Running** (blue) · elapsed 4m 12s · cost $0.42 · buttons [Pause] [Cancel] [Take control].
- **Left column — Phase timeline** (vertical): Phase 1 Spec ✓ · **Phase 2 Implementation ● (active)** · Phase 3 Review ○ · Phase 4 Eval ○. Under the active phase, sub-tasks with checkmarks and a per-phase commit hash (`a1b2c3d`).
- **Center — Live activity stream** (chronological, collapsible tool results): “Implementer → Edit `CardShuffler.cs` (+42 −3)” · “Ran tests: 18 passed” · “Implementer → thinking: handling empty-deck edge case”.
- **Right column (tabbed):**
  - **Terminal** — a dark monospace surface streaming raw output, with a **Take control** affordance to type into it.
  - **Artifacts** — diffs produced, files touched, eval results, any bot-PR.
- **Blocked banner** (show this state): an amber bar — “Implementer needs permission: `git push…`” with inline [Approve] [Deny].
- A small **agent chip** showing who’s acting: “Implementer · Claude Opus · scope: `src/`, `tests/unit/`”.
- Render the states: running, blocked (prominent amber), completed (green summary card with links to history/eval/diff), failed (red, error + [Retry]).

## Screen 4 — Flow Builder (guided form)
Author a flow without touching raw config. Two-pane: **left = form (stepper sections)**, **right = live summary preview** of the flow being built.
- **1 · Basics:** name, description, target project(s) (or “Scratch / no project”).
- **2 · Template:** choose a starting shape as selectable cards — *Single agent* · *Agent + Reviewer* · *Full SDLC loop* · *Feature wizard*.
- **3 · Steps:** an ordered, drag-reorderable list of agent steps. Each step: pick an agent from a dropdown (Implementer, Reviewer, Spec-author, …), set its inputs, and an optional per-run policy override (Ask / Auto / Bypass).
- **4 · Inputs:** define the prompt fields the user fills at launch (e.g. “Feature description”, “Target file”).
- **5 · Gates:** toggles for signoff gates — Feature brief · Fixture list · Pull request · ADR — shown as inherited from the active profile (**solo-dev**).
- **6 · Evals:** attach evaluators as inline steps.
- **Right preview:** a clean vertical summary of the steps with their agents + active gates, plus [Validate] and a primary [Save & Run].
- States: empty (template picker prominent), editing, inline validation errors, saved.

---

## Cross-cutting requirements
- Apply the **same status colors and pills** consistently across all four screens.
- Provide **empty, loading (skeletons), and blocked** states for each screen.
- **Responsive:** must remain usable on a phone — when narrow, prioritize the Needs-Attention and Active-Runs content; collapse side columns into tabs.
- Make the four screens feel like one product: shared shell, spacing scale, and component vocabulary.
