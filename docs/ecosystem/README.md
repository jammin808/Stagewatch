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

## Where the work stands (2026-11-15)

The owner paused the work on 2026-11-12 and lifted the pause on 2026-11-14 ("Continue development"). Every repository's work
is on the session's designated working branch (the same name in all five repositories); each landed unit is pushed as it
lands, so a working tree holds at most the one unit in progress. AGMessage stays with its own session.

### Stagewatch — 2.22 closed (`4ff9064`); 2.23 waits on Patterns Round 98

- 2.20 + 2.21 are cut on `d311de8` and 2.22 on `4ff9064` (the maintainer's PC: `tools\release.ps1`; nothing of 2.23 is in it).
- 2.22 (Train 5), as landed: SW22.1a–c (`/ws` speaks LINK: the offer, the verdict, S on side calls, live revocation, final,
  receipts), SW22.2 (windows in the context), SW22.3 and SW22.4 (grants in Core, and only on the app's own key-verified link),
  SW22.4p (the lane at the siblings' pushed heads), SW22.5–SW22.10 (window lights and grants on the desk and the rig, the
  Companion module's window actions, the Companion and OSC doors, the copilot and MCP proposing to other apps), SW22.11m (a
  mends unit: the rig's `--state` file owner-only, a Companion press pushed out of the awaiting table settled as unheard, sleeps
  on the playout, window and grant paths replaced by waits on the effect), SW22.11 (the lane at Patterns' Round 96: the proved
  `/ws` pair, a window on Patterns, the stuck rig, MCP to PlayMap), SW22.11p (the lane at PlayMap's fixed wire, Mapper `031c9a3`:
  two rows hold the line-wire link to no `malformed` flag) and SW22.12 (the papers).
- The paused head's check at the close: every default lane and the interop lane green (unit 6,155; LAN 39 of 39, the laptop
  5 of 5, offline 3 of 3).
- Next: SW23.1 (PlayoutLink speaks LINK to Patterns and PlayMap) waits on Patterns Round 98's papers (the plan's P97.6); then
  SW23.2 (2.23's papers), then SW24.1 per `master-plan.md`.
- Open: the browser page-frame checks of the e2e lane (`/countdown`, `/audience`, `/holding`) fail now and then when another
  tree's suite runs beside them and pass on a rerun; the cause is not established beyond the load, and wants a mends unit.
- The lane's rows assume a host no other build shares: PlayMap's own preflight reads red above 95 % CPU and closes the doors.
- Template: `workflows/stagewatch-chain.js` (its unit list is given at launch; briefs from `unit-brief.py`; refresh the
  first sentence of its shared text, which says what is landed, before a launch).
- Stagewatch's CI runs on main, tags and pull requests only, so nothing runs on the working branch.

### Patterns — Round 98 in progress (98.2 at `b49a0e8`)

- Landed and pushed: Rounds 89–97. Round 96 is the plan's P95 (PLAN §114, last commit `fb56b4e`). Round 97 is the plan's P96
  (PLAN §115): 97.0 the opening (`7d0907c`), 97.1 a mends unit (`c265352`: OPEN L110, a test canary no hex name can hold, and
  the suites' other canaries of its kind), 97.2 Show Bus node devices (`68737e1`: PlayMap known, paired by proof at level 0,
  driven, drawn in the NETWORK band), 97.3 the papers (`f102d61`).
- CI: runs 303–305, on 97.0–97.2, were red at the build: the runner image's newer .NET SDK reports CA1860 on one test line
  that this bench's SDK does not. 97.3 mends the line; the tree pins no SDK (no `global.json`), which is OPEN L114 and the
  owner's to decide (a pin also binds the SDK the maintainer's PC builds with). **Read CI after every push**: a build that is
  clean here can still fail there.
- Round 98, the plan's P97 (PLAN §116), as landed and pushed: 98.0 the opening (`b42cc32`), 98.1 TLS on 9697 told from
  plain by the first byte (`0dbff3e`; the wire writes nothing before the client's first byte), 98.6 the SDK pin (`9e2333d`:
  `global.json` names 10.0.115 with `latestPatch`, CI installs from it; inserted on the owner's answer, so the papers are
  98.7), 98.2 LINK and LINK PROOF on 9697 (`b49a0e8`). CI green on 98.6 (run 309) and 98.2 (run 310).
- CI run 308, on 98.1, was red at one App test (`PlayAppTests.TheHubRunsARoomForThePhonesTheHostAndTheWall`: the audience
  listener never came up in its 15 s); it passed here and on the next run. Read from the code, not measured: the control
  start threw on a port it could not bind, which closes all three listeners and retries the same numbers; the test's own port
  picks can collide and its probe sees loopback only. The cause on the runner is not proved. Round 98.8, inserted, makes the
  start failure name its listener and port and gives the App tests distinct ports probed as the desk binds.
- In flight, in this order: 98.8, 98.3 app keys, 98.4 the acceptor's list, 98.5 receipts and node devices speaking LINK, 98.7
  the papers (the tag table's row for round 97 = `f102d61`). Then Round 99 (the plan's P98, the Eye sees the room).
- The pin's band is the owner's open call: 10.0.115 is this bench's SDK (Ubuntu's package, the 10.0.1xx band); before the pin
  CI built on 10.0.404, the band a current Visual Studio carries. Kept at 1xx, the maintainer's PC installs a 10.0.1xx SDK at
  10.0.115 or above beside its own; re-pinned to 4xx, this bench installs 10.0.404 at the start of each session (the download
  is reachable). Either way Stagewatch's interop jobs must install the SDK Patterns' `global.json` names before
  `siblings.json` moves its Patterns pin past `9e2333d`, and the lane's wire client must speak first (98.1's door).
- Template: `workflows/patterns-chain.js` (units carry both the round's and the plan's numbers; a unit with no plan is one
  the round inserted; `args.notes` adds a line to one stage of one unit without touching the rest). The App suite runs only by
  six foreground parts through `workflows/patterns-app-parts.sh` (two to six minutes a part on this bench). Run one test run at
  a time: two App test runs on one machine hear each other's mDNS answers and beacons (OPEN L111).

### PlayMap (repo Mapper) — two wire fixes and two test mends landed (`46129380`); the rest waits on round 3's integration pass

- Landed and pushed, each a round-2 fix batch with its entry in `docs/round2/CARRYOVER.md`: PM-1 fix-showbus-identify, PM-2
  fix-showbus-keys, PM-3 fix-media-page-import, PM-5 fix-showbus-node-link (and its status-light arm), PM-4 fix-showbus-1.3,
  fix-proposal-sink-deadline, PM-6 fix-peerlink-core, PM-7 fix-stagewatch-timer-air, PM-8 fix-stagewatch-context-size,
  fix-desk-unconfirmed, PM-11 fix-context-peer-rows (`cddf83b6`, its leave extension `783319e1`), fix-wire-state-after-proof
  (`cb9be004`: a connection not paired at HELLO hears its greeting STATE only after "OK paired") and
  fix-wire-greeting-after-handshake (`031c9a3d`: a connection paired at HELLO hears it only once a dialler's handshake behind
  its HELLO is over; three comments reworded). Every leave and extension is in `owner-answers.md`.
- Test-only mends on the owner's answers of 2026-11-14 and 2026-11-15: fix-devicelock-test-thread (`f6181596`: CI run 473's
  red test; its second taker waited on a pool worker, reproduced under a starved pool) and fix-shownline-test-threads
  (`46129380`: four tests of the same shape). fix-pool-bound-tests (ten more in the Engine, Media and Net suites, one shared
  helper under `tests/Shared`) is in flight.
- CI: run 475, on `f6181596`, green. Run 476, on `46129380`, red on the Windows job at one Net test no batch touched
  (`StagewatchLinkTests.AKeylessLinkAskedForTheViewingCodeIsRefusedInThoseWordsAndHeld`: two sockets and two connections
  where the held refusal allows one). Read from the code by two readers, not measured: the link's first ping is written
  before its first read (`StagewatchLink.BeatAsync`); a Stagewatch that sends one refusal and closes without reading answers
  the ping with a reset, Windows drops the unread refusal, no hold is recorded and the link redials on its backoff until a
  refusal is read. A product defect of low severity (bounded by the backoff; nothing on the air), latent since the link's
  beat was written. The mend (no ping before the first message is read; a test that fails without it on every platform) is in
  `src/PlayMap.Net/Links/StagewatchLink.cs`, outside every leave: it waits on the owner's word.
- Open in the last batch's CARRYOVER entry, for the owner: a dialler whose next line is not in when the node next reads after
  its HELLO is still greeted inside its handshake (a race, unmeasured); Patterns' node device on a trusted address counts one
  discarded STATE there and says why in its own words.
- Nothing more can start now: PM-9a–c and PM-10 (wp4-peerlink) are "first after round 3's integration pass", which has not
  happened in Mapper, and PM-12…PM-20 follow them per `master-plan.md`.
- Template: `workflows/train3-playmap.js` (several batches) or `workflows/playmap-desk-unconfirmed.js` (one batch).

### Open findings carried into the next units

- **The owner's to decide:** whether a PIN-only Stagewatch may be an open acceptor; the band of Patterns' SDK pin (1xx or
  4xx); the mend of PlayMap's first ping before its first read. Three stray files at the filesystem root of the cloud container
  (`/WebServer.cs.bak`, `/WebServer.cs.keep`, `/module.log`: stages' scratch copies, no secret in them) could not be removed
  from the session; they are in no repository.
- **Stagewatch:** `stuck` is not offered until measured; the 429 bounds are chosen, not measured; the sleeps SW22.11m left
  (its list is in `docs/WHATS_NEXT.md`); the e2e page-frame checks under load.
- **The interop lane does not prove yet:** PlayMap's timer row, AGMessage 0.6.0, the one-laptop race of PlayMap's greeting.
- **Patterns:** OPEN L108–L114.

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
