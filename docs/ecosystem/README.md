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

### Stagewatch — 2.20 and 2.21 closed; Train 5 (2.22) under way

- Landed: 2.17 (controlled windows), 2.18 (Train 1's harms), 2.19 (Show Bus 1.3 final, the PeerLink kit, hello 1.3, the
  interop lane `tools/test.sh interop` with CI's interop job), the brief's four lines, the temp-folder fence, 2.20 SW20.1–SW20.13
  (playout links on the PeerLink core; the VT rows against the real apps), 2.21 SW21.1–SW21.9 (one desk for every app, one
  report, one preflight), SW21.9m (the suite's timing races, and two product races they showed) and SW21.10, the two releases'
  combined papers, at `d311de8`: the head the 2.20 + 2.21 release is cut on (the maintainer's PC: `tools\release.ps1`), with
  nothing of 2.22 in it. `main` was merged in at `b6f454e`.
- The interop lane's LAN rows still run against Patterns at round 91's pin: a newer Patterns holds its node key from a
  Stagewatch on another machine until a person presses Trust, which the lane cannot do yet. The pin moves in a unit of its own
  once Patterns' test bench has a trust line (a mends unit queued in Patterns Round 95) or the rig answers LINK (SW22.1a–c).
  The lane's rows assume a host no other build shares: PlayMap's own preflight reads red above 95 % CPU and closes the doors.
- Next, in order (Train 5): SW22.1a, SW22.1b, SW22.3, SW22.4 (Patterns Round 96 waits on 1b and 4), SW22.1c, SW22.2, SW22.5,
  SW22.6, SW22.7, SW22.8, SW22.9, SW22.10; SW22.11 waits on Patterns Round 96's papers; SW22.12 is 2.22's papers.
- Template: `workflows/stagewatch-chain.js` (its unit list is given at launch; briefs from `unit-brief.py`).
- Stagewatch's CI runs on main, tags and pull requests only, so nothing runs on the working branch.

### Patterns — Round 95 under way

- Landed and pushed: Rounds 89–94 (Round 94 is the plan's P93: one speaker message, held clips, `VIDEO … ON <key>`, a mends
  unit 94.4 the round inserted, papers 94.5 at `fa5d76b`; the tag table's next row is round 94's), Round 95.0 (PLAN §113.1's
  opening) and 95.1 (the Eye's NETWORK band, `04e29e6`).
- Next: 95.2 (the Eye's cost and marks); 95.3, a mends unit this round inserted (the Core allocation fence CI's run on 94.4
  showed red once, root-caused and never loosened; the test bench's trust line for the interop lane); 95.4, the round's papers
  (the plan's P94.3). Round 96 (the plan's P95) waits on Stagewatch's SW22.1b and SW22.4.
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
