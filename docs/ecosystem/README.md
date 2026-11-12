# The cross-app plan of record: Show Bus 1.3 and PeerLink across Stagewatch, Patterns, PlayMap and AGMessage

This folder is the plan four repositories are built against, kept here so the work can resume from GitHub alone: the plan, the
owner's answers, the designs behind each unit, the structured data they were drawn from, and the templates every unit was
run with. Stagewatch holds it because Stagewatch holds the contract (`docs/SHOWBUS.md`, Show Bus 1.3 final); the other trees
carry byte-identical copies of the contract, not of this folder.

| File | What it is |
|---|---|
| `master-plan.md` | The plan of record: nine trains of units with their dependencies, rules R1–R7 near the top, each unit's `### <id>` section, and at the end the renumbering note (Patterns' rounds are the plan's P-number + 1 from Train 3 on) |
| `owner-answers.md` | Every owner answer, including each leave extension for PlayMap's fix batches and the commit identity rule |
| `owner-decisions.md`, `owner-questions.txt` | The questions as asked and the decisions as recorded |
| `<area>.md` | The source designs units cite (`spec-1-3`, `peerlink` (the §13 profile), `peerlink-*`, `eye`, `ecosystem-lane`, `cross-app-proposals`, `playout-links`, `windows-for-peers`, `peers-not-screens`, `patterns-playmap-side`, `agmessage`); `plan.md` is an older draft kept for its longer text; `plan-critique.md` the critique the plan answered |
| `agmessage-handoff.md` | What was handed to AGMessage's own session (its AG-1) |
| `data/` | `gaps.json` (G01–G49), `maps-lite.json`, `master-plan.json` (the units as data), `units/*.json` (each design's units) |
| `workflows/` | The scripts each train ran (implement → adversarial review → fold-in and one commit per unit), the `-lean` templates in use now and `unit-brief.py`, which builds a unit's brief from this folder. They name this folder by its path in a cloud checkout (`/home/user/Stagewatch/docs/ecosystem`); edit `S` if the trees live elsewhere |

## Where the work stands (2026-11-11)

Every repository's work is on the session's designated working branch (the same name in all five repositories). The owner
lifted all three pauses on 2026-11-11 ("Resume and continue StageWatch, Patterns, and PlayMap"); each landed unit is pushed as
it lands, so a working tree holds at most the one unit in progress. AGMessage stays with its own session.

### Stagewatch — 2.21's code landed; its papers wait on Patterns Round 94

- Landed: 2.17 (controlled windows), 2.18 (Train 1's harms), 2.19 (Show Bus 1.3 final, the PeerLink kit, hello 1.3, the
  interop lane `tools/test.sh interop` with CI's interop job), the brief's four lines, the temp-folder fence, 2.20 SW20.1–SW20.12
  (playout links on the PeerLink core) and 2.21 SW21.1–SW21.9 (the verb table, the desk's rules, the desk as one service,
  `peer`/`desk` on the wire, the readers, one report, one preflight, the desk's door in WPF, the lane's desk rows), the last at
  `ce8efad`. `main` was merged in at `b6f454e` (its lockout fix: a valid key clears no count); the merge's WHATS_NEXT names
  one interop row not yet built.
- Next, in order: the suite's two timing races named in SW21.3's and SW21.7's commits (a mend unit of its own); SW20.13 (the
  VT interop rows S4.1–S4.4, once Patterns Round 94's papers are pushed); SW20.14 and SW21.10 as one set of papers (the
  owner's S-releases answer: 2.20 and 2.21 ship as one release); then Train 5 (SW22.1a … SW22.12), no unit of it before
  those papers, so the release is cut on a head that holds 2.21 and nothing of 2.22.
- Template: `workflows/train4-stagewatch-lean.js` (set its unit list; drop the units `git log` already names).
- Stagewatch's CI runs on main, tags and pull requests only, so nothing runs on the working branch.

### Patterns — Round 94 under way

- Landed and pushed: Rounds 89–93 (Round 93 is the plan's P92, closed at `b57410d`; the tag table's next row is round 93's),
  Round 94.0 (PLAN §112's opening), 94.1 (one speaker message), 94.2 (a playlist item and a stinger's clip held) and 94.3
  (`VIDEO … ON <key>`), the last at `66fd5c5`.
- Next: Round 94.4, a mends unit this round inserted for what 94.2's and 94.3's whole runs found (a STAGE CLEAR that does not
  reach the Stagewatch a receipt bound; "not sent" said for a call that may have arrived; a test's wait under load; a STATE
  push after the control wire's long-line refusal), then 94.5, the round's papers (the plan's P93.4). Then Round 95 = the
  plan's P94 (the Eye's NETWORK band: 95.0 opening, 95.1, 95.2, 95.3 papers). Round 96 (the plan's P95) waits on Stagewatch's
  SW22.1b and SW22.4.
- Template: `workflows/patterns-rounds-lean.js` (its units carry both plan and round numbers; drop the ones `git log` names).

### PlayMap (repo Mapper) — waiting on round 3's integration pass

- Landed and pushed, each a round-2 fix batch with its entry in `docs/round2/CARRYOVER.md`: PM-1 fix-showbus-identify, PM-2
  fix-showbus-keys, PM-3 fix-media-page-import, PM-5 fix-showbus-node-link (and its status-light arm), PM-4 fix-showbus-1.3,
  fix-proposal-sink-deadline, PM-6 fix-peerlink-core, PM-7 fix-stagewatch-timer-air, PM-8 fix-stagewatch-context-size,
  fix-desk-unconfirmed and PM-11 fix-context-peer-rows (the context names its peers and the timer it draws; `cddf83b6`, its leave
  extension `783319e1`). Every leave extension is in `owner-answers.md` and in a "Papers: the owner extends …" commit.
- First on resume: read CI for the pushes after run 471 (PM-11); a red job is ours to root-cause.
- Nothing can start now: PM-9a–c and PM-10 (wp4-peerlink) are "first after round 3's integration pass", which has not happened
  in Mapper, and PM-12…PM-20 follow them per `master-plan.md`.
- Open items each batch left are in its CARRYOVER entry's Open list.
- Template: `workflows/train3-playmap.js` (several batches) or `workflows/playmap-desk-unconfirmed.js` (one batch).

### AGMessage

Nothing of this plan's is on the working branch, which was never pushed. The other session's work (0.5.0, 0.5.1) is merged
into `main` (`89104d8`); AG-1 was handed over (`agmessage-handoff.md`). On resume, start the working branch from `main`, read
what 0.5.x landed against `agmessage-handoff.md`, then plan Train 9 (AG-2…) on it.

## Resuming in a fresh container

1. Clone the five repositories to `/home/user/<Repo>` and check out the working branch in each; `git log -1` should match the
   remote. Set each repository's committer to the session's identity (`git config user.name/user.email`), as
   `owner-answers.md`'s identity rule asks; authors stay per repository (Stagewatch `jammin808`, Patterns `Patterns`, Mapper the
   default).
2. Bench: `tools/setup-dev.sh` in Stagewatch (the .NET 10 SDK, Node, Chromium's headless shell) and in Mapper (Xvfb and Mesa,
   FFmpeg 9.0 in `/opt/ffmpeg9`, DXC in `/opt/dxc`); Patterns builds with the .NET 10 SDK alone.
3. Read each tree's `CLAUDE.md`, then this folder's `master-plan.md` for the next unit's section and `owner-answers.md`.
4. Run the tree's template (or one unit by hand): implement and validate with the tree's own lanes, an adversarial review
   that does not edit, then fold-in and **one** commit per unit; push after each landed unit; a file outside a PlayMap batch's
   leave is asked of the owner before the push. Since 2026-11-11 units run lean (the owner's choice), which the `-lean`
   templates carry:
   - a brief per unit, built by text extraction only: `python3 docs/ecosystem/workflows/unit-brief.py artifacts/briefs SW21.4 …`
     (Patterns units by the plan's id, `P93.2` for Round 94.2); agents work from the brief and open the plan or a design only
     at a section the brief lists;
   - one full validate per unit, run by the committing stage; the implementer runs the tests of what it touched, the reviewer
     rebuilds nothing and runs only the narrowest `--no-build` test that proves or kills a finding (Patterns' R4 stays: the
     whole Core, Rendering and App suites per code unit, once, at the commit);
   - build and test output goes to a log file; only its summary and the failing tests are read;
   - `artifacts/interop` (the interop lane's sibling clones and builds) is kept between runs;
   - a round's opening is one stage; a round's papers are a writer and one check-and-commit stage; code units keep all three;
   - pushes, CI reads, merges and this guide's updates are done by hand, not as a run.
   - since 2026-11-12 the session that orchestrates and the work stages run on different models (the owner's choice); the
     templates take the work stages' model and effort at launch (`args.work`, `args.light`), never from the file.
5. Tests make temporary paths only through each tree's fence (Patterns `tests/Shared/TempRun.cs`, Stagewatch
   `tests/Stagewatch.Tests/Scratch.cs`); give a run its own TMPDIR under the tree's ignored output folder.

## The maintainer's PC (not doable from the cloud)

`release.ps1` for Stagewatch 2.18 and 2.19 (then 2.20 + 2.21 together); `smoke.ps1` walks of the WPF desk work (compiled only
here); LB-1's two Windows runs of the carrier probe; `SIBLINGS_READ_TOKEN` and the runner's userns sysctl for CI's interop
job; Patterns' unwalked QUALIFICATION rows.
