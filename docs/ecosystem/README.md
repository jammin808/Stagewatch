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
| `workflows/` | The scripts each train ran (implement → adversarial review → fold-in and one commit per unit); the two chain templates in use now (`stagewatch-chain.js`, `patterns-chain.js`); `unit-brief.py`, which builds a unit's brief from this folder; and `patterns-app-parts.sh`, which runs Patterns' App suite as foreground parts. They name this folder by its path in a cloud checkout (`/home/user/Stagewatch/docs/ecosystem`); edit `S` if the trees live elsewhere |

## Where the work stands (2026-11-12): paused

**Paused at the owner's word (2026-11-12): "Pause at the end of the two chains running now."** The reason is the owner's
weekly usage limit, which resets on Saturday morning. The two chains ended: Stagewatch's Train 5 through SW22.10 and Patterns'
Round 96 through its papers. Every tree is clean and pushed; nothing is in flight. Start nothing until the owner says so; on
their word, the next units are the ones named under each tree below.

Every repository's work is on the session's designated working branch (the same name in all five repositories); each landed
unit was pushed as it landed. AGMessage stays with its own session.

### Stagewatch — 2.20 and 2.21 closed; Train 5 (2.22) landed through SW22.10 (`53023da`)

- Landed: 2.17 (controlled windows), 2.18 (Train 1's harms), 2.19 (Show Bus 1.3 final, the PeerLink kit, hello 1.3, the
  interop lane `tools/test.sh interop` with CI's interop job), the brief's four lines, the temp-folder fence, 2.20 SW20.1–SW20.13
  (playout links on the PeerLink core; the VT rows against the real apps), 2.21 SW21.1–SW21.9 (one desk for every app, one
  report, one preflight), SW21.9m (the suite's timing races, and two product races they showed) and SW21.10, the two releases'
  combined papers, at `d311de8`: the head the 2.20 + 2.21 release is cut on (the maintainer's PC: `tools\release.ps1`), with
  nothing of 2.22 in it. `main` was merged in at `b6f454e`.
- Train 5 (2.22), landed in this order: SW22.1a, SW22.1b (`/ws` speaks LINK: the offer, the verdict, S on side calls, live
  revocation), SW22.3 (grants in Core), SW22.4 (grants reach only the app's own key-verified link), SW22.1c (final, receipts,
  lateMs, 429 words), SW22.2 (windows in the context), SW22.4p (the interop lane at the siblings' pushed heads: Patterns
  `d707925`, where a person's Trust is pressed through the test bench's `trust <hex>` line, and Mapper `783319e`), SW22.5
  (window lights and words, grants made on the desk), SW22.6 (the rig grants as the app does; `grants.e2e.cjs`), SW22.7 (the
  Companion module's window actions), SW22.8 (the Companion and OSC doors through the gate), SW22.9 (the copilot plans steps
  for other apps and a person decides), SW22.10 (MCP proposes to other apps and a person decides).
- The paused head's check: `tools/test.sh` (build, unit, module, holo, e2e) all green on `53023da`.
- Next on resume, in order: SW22.11 (interop rows: windows on Patterns, proved `/ws` pairs, the MCP rows; its wait, Patterns
  Round 96's papers, is over: move the lane's Patterns pin to Round 96's head first), then SW22.12 (2.22's papers), then
  Train 6 per `master-plan.md`.
- The lane's rows assume a host no other build shares: PlayMap's own preflight reads red above 95 % CPU and closes the doors.
- Template: `workflows/stagewatch-chain.js` (its unit list is given at launch; briefs from `unit-brief.py`; refresh the
  first sentence of its shared text, which says what is landed, before a launch).
- Stagewatch's CI runs on main, tags and pull requests only, so nothing runs on the working branch.

### Patterns — Round 96 closed (`fb56b4e`); Round 97 is next

- Landed and pushed: Rounds 89–96. Round 94 is the plan's P93 (papers `fa5d76b`). Round 95 is the plan's P94 (the Eye's NETWORK
  band, cost and marks; 95.3, a mends unit: the Core allocation fence CI showed red once, root-caused to the runtime's
  per-thread accounting and never loosened, and the test bench's `trust <hex>` line; papers `d34e67d`, PLAN §113). Round 96
  is the plan's P95 (PLAN §114): 96.0 the opening (`d707925`), 96.1 the Stagewatch link proves itself with LINK (`31d5056`),
  96.2 a Stagewatch window granted to this desk (`12cdffd`), 96.3 the twin's WINDOWS word (`b2dbb5b`), 96.4 the papers
  (`61cc822`, and `fb56b4e`, one line of them corrected). 3,459 tests, the module at 3.22.0. The round is unwalked.
- First on resume: read CI's run 302, on `fb56b4e` (it carries `61cc822` too and was still running at the pause); a red job
  is ours to root-cause. Run 301, on `b2dbb5b`, is green. One red is already read and written down: the run on `31d5056` failed on a test's canary alone (OPEN L110: a four-hex-digit canary that
  a random temporary folder name can hold, about one run in 1,700); its mend, a canary no hex can hold, is not made yet and
  belongs to Round 97's first unit.
- Next: Round 97, the plan's P96 (97.0 the opening, the plan's P96.0; 97.1 PlayMap as a Show Bus node device, P96.1; 97.2 the
  papers, P96.2, with the tag table's row for round 96 = `fb56b4e`).
- Template: `workflows/patterns-chain.js` (units carry both the round's and the plan's numbers; `args.notes` adds a line to
  one stage of one unit without touching the rest). The App suite takes about 21 minutes, longer than one command may run, and
  stages that started it in the background ended without it three times; it now runs only by six foreground parts through
  `workflows/patterns-app-parts.sh` (two to six minutes a part on this bench). Run one test run at a time: two App test runs
  on one machine hear each other's mDNS answers and beacons and fail each other's tests.

### PlayMap (repo Mapper) — waiting on round 3's integration pass (`783319e1`)

- Landed and pushed, each a round-2 fix batch with its entry in `docs/round2/CARRYOVER.md`: PM-1 fix-showbus-identify, PM-2
  fix-showbus-keys, PM-3 fix-media-page-import, PM-5 fix-showbus-node-link (and its status-light arm), PM-4 fix-showbus-1.3,
  fix-proposal-sink-deadline, PM-6 fix-peerlink-core, PM-7 fix-stagewatch-timer-air, PM-8 fix-stagewatch-context-size,
  fix-desk-unconfirmed and PM-11 fix-context-peer-rows (the context names its peers and the timer it draws; `cddf83b6`, its leave
  extension `783319e1`). Every leave extension is in `owner-answers.md` and in a "Papers: the owner extends …" commit.
- CI's run 472, on `783319e1`, is green.
- Nothing can start now: PM-9a–c and PM-10 (wp4-peerlink) are "first after round 3's integration pass", which has not happened
  in Mapper, and PM-12…PM-20 follow them per `master-plan.md`.
- Open items each batch left are in its CARRYOVER entry's Open list.
- Template: `workflows/train3-playmap.js` (several batches) or `workflows/playmap-desk-unconfirmed.js` (one batch).

### Open findings carried into the next units

- **The owner's to decide:** PlayMap's link to Stagewatch carries the `malformed` flag from its first moment (a STATE frame
  arrives after HELLO and before the proof); which side changes is not decided. Whether a PIN-only Stagewatch may be an open
  acceptor. Two stray files at the filesystem root of the cloud container (`/WebServer.cs.bak`, `/WebServer.cs.keep`, a
  stage's mutation backups) could not be removed from the session; they are in no repository.
- **Stagewatch, for SW22.11 or a mends unit:** the rig's `--state` file is not owner-only; the Companion module's shared
  64-slot awaiting table can drop a playout press silently; some checks sleep before reading a command's effect; `stuck` is
  not offered until measured; the 429 bounds are chosen, not measured.
- **The interop lane does not prove yet:** Patterns at level 1 in the lane (SW22.11), PlayMap's timer row, AGMessage 0.6.0.
- **Patterns:** OPEN L108–L110 (Round 96 added them); the App suite's tests share the machine's mDNS and beacon ports.

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
   - a run longer than one command may take is never started in the background and left: a stage that ends while a run is
     still going has reported without it. Split it into foreground parts that together cover it (Patterns' App suite:
     `patterns-app-parts.sh`), and run one test run at a time on the machine.
   - a chain is resumed, not relaunched, after a stop: the same script and unit list replay every finished stage from the
     run's record, so a change for one stage goes in as a note for that stage (`args.notes`), never into the shared text.
5. Tests make temporary paths only through each tree's fence (Patterns `tests/Shared/TempRun.cs`, Stagewatch
   `tests/Stagewatch.Tests/Scratch.cs`); give a run its own TMPDIR under the tree's ignored output folder.

## The maintainer's PC (not doable from the cloud)

`release.ps1` for Stagewatch 2.18 and 2.19 (then 2.20 + 2.21 together, cut on `d311de8`); `smoke.ps1` walks of the WPF desk
work (compiled only here: 2.20's links and cue editor, 2.21's one desk, 2.22's window lights and grants); the six
Windows-only checks the suite names and never counts here; Companion loading the module's new definitions; LB-1's two Windows
runs of the carrier probe; `SIBLINGS_READ_TOKEN` and the runner's userns sysctl for CI's interop job; Patterns' unwalked
QUALIFICATION rows (Round 96: WebView2 showing a grant, the twin's WINDOWS word).
