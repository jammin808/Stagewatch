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

### Stagewatch — 2.23 closed (`45c94c1`); 2.24 in flight

- 2.20 + 2.21 are cut on `d311de8` and 2.22 on `4ff9064` (the maintainer's PC: `tools\release.ps1`; nothing of 2.23 is in it). 2.23 waits for its release on `45c94c1`
  (`tools\release.ps1 -Version 2.23.0 -ModuleVersion 2.23.0`).
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
- 2.23 (Train 6), as landed and pushed: SW23.0 (`a466d83`, inserted: the lane's pins moved to Patterns `2515017` and Mapper
  `2ae7ac7b`, the interop jobs install the SDK each sibling's `global.json` names through `tools/interop/sdk.sh`, the lane's
  wire client speaks first; LAN 39 of 39) and SW23.1 (`f907c4a`: PlayoutLink speaks LINK to Patterns by what its proof
  offers, adopts the key it enrols, reads its scope before a press, `playout <link> floor lower`; PlayMap's seam stays level 0
  until its acceptor lands; lane row S4.6).
- **SW23.1 was committed with the LAN lane at 38 of 40** (S6.1 and S7.4 red), against the rule that a unit commits on green;
  the chain was stopped before the papers. The cause, read by two readers, is a defect in Patterns, not in this unit: on the
  typed-address path Patterns' Stagewatch link names the node only while its hello is live, so a redial that lands between a
  restarted Stagewatch's port opening and its first hello takes the address form, holds back the key bound to the node, and
  stays live at level 0 with nothing looking again (S6.1 restarts the rig beside Patterns' fifth redial). No key went to the
  unproved address. The mend is Patterns' Round 99.7 (below).
- SW23.1p (inserted once 99.7 was pushed: the lane's Patterns pin moved to `ff01e34`, S6.1 and S6.1b hold Patterns
  following the rig as its node by its key, S4.7 the redial with the enrolled key a row; LAN 41 of 41, the laptop 5 of 5,
  offline 3 of 3; `docs/WHATS_NEXT.md` says what it found; `857539f`) and SW23.2 (`45c94c1`: the papers, which say SW23.1's
  commit at 38 of 40 plainly). Open: the one-way-loss GO row (S4.8); S4.7 on a CI runner's kernel (it needs
  `CONFIG_INET_DIAG_DESTROY`).
- In flight, 2.24 (Train 7): SW24.1 (Stagewatch publishes its context by P1–P8) and SW24.2 (the papers). The lane's
  Patterns pin stays `ff01e34` until Patterns' Round 99 closes; a lane row that needs Patterns' feed (99.3 on) is left
  named as open for a lane unit after that.
- The browser page-frame checks of the e2e lane (`/countdown`, `/audience`, `/holding`), which failed now and then beside
  another tree's load, had a race of the check's own (a frame index read once, before the window's reveal); SW23.1p names it
  and mends the check (`docs/WHATS_NEXT.md`). SW23.1 mended an earlier race of the same check; no run has shown these
  were the only causes, so a next sighting is read before anything is changed.
- The lane's rows assume a host no other build shares: PlayMap's own preflight reads red above 95 % CPU and closes the doors.
- Template: `workflows/stagewatch-chain.js` (its unit list is given at launch; briefs from `unit-brief.py`; refresh the
  first sentence of its shared text, which says what is landed, before a launch).
- Stagewatch's CI runs on main, tags and pull requests only, so nothing runs on the working branch.

### Patterns — Round 98 closed (`2515017`); Round 99 in flight

- Landed and pushed: Rounds 89–98. Round 98 is the plan's P97 (PLAN §116): this desk is a LINK acceptor on 9697.
  - The plan's units: 98.0 the opening (`b42cc32`), 98.1 TLS told from plain by the first byte (`0dbff3e`; the wire writes
    nothing before the client's first byte), 98.2 LINK and LINK PROOF (`b49a0e8`), 98.3 app keys (`2d2a9c2`: `ptk_`, scoped
    follow/post/drive/operate, enrolled over TLS, desk-only KEY verbs), 98.4 the acceptor's list (`8c123a4`: APP LINKS on the
    Nodes page, STATE's decks rows, an accepted link as one edge on the Eye; OPEN L115: an operator's CLOSE rides the refused
    class, the line having no disconnect word), 98.5 receipts, CUE GO's final, `lateMs` (`2735001`), 98.7 the papers
    (`2515017`).
  - Units the round inserted: 98.6 the SDK pin (`9e2333d`, the owner's answer), and five from CI's red runs, each read by two
    readers before it was mended: 98.8 (`b326280`, run 308: a failed listener start names its listener and port), 98.9
    (`efc0481`, run 313: a device link raises nothing before Start; the node device's lost HELLO, a product defect since
    97.2), 98.11 (`7c8b39e`, run 314: the shutdown test left a desk listening on the default ports; a closed desk opens
    nothing), 98.12 (`30137dc`, runs 315 and 316: the flood test's bound counted scheduling ahead of the accept; the doors'
    accepts now run off the desk's thread, in arrival order), 98.10 (`da54d9e`: 13.20's conformance table; the hello's
    `peerlink` cap is held back while a row is not kept: the node device's line link, the Stagewatch link's S1 and S14, the
    acceptor's reply-queue cut over a real socket).
  - CI: 309 to 312 green, 313 to 316 red on one App test each, 317 to 319 green. None of the four causes is proved for its
    run; each mend added failure words that name the cause at a next sighting.
- In flight: Round 99, the plan's P98 (PLAN §117, the Eye sees the room). Landed and pushed: 99.0 the opening (`7555b02`),
  99.1 EyeNetwork (`e990739`: one card per proved id, a link drawn once as its edge, unplaced rows counted), 99.2 one
  projection for lights (`8f62a0b`: precedence, not "worst"; five readers under one fence; a bounded brief; its body names
  three departures from §117.1 for the papers), 99.7 (`ff01e34`, inserted for the defect the lane found; below). CI green on
  all four (runs 320 to 323).
- 99.7, as landed: a live typed-address Stagewatch link looks again at who is heard at that address and moves once to the
  node whose dial proves the key, by one rule in Core (`StagewatchAt`) shared with the resolver. A keyless or public-only
  link is never moved (13.4.3), a bound key is never sent to an unproved address, a move the node refuses goes back to the
  address at once, and each is one log line and one journal row.
- The chain now runs 99.3, 99.4, 99.5, 99.6: 99.3 the feed, 99.4 the desk's readers, 99.5 the wire's readers, 99.6 the papers (the tag
  table's row for round 98 = `2515017`). Then Round 100 (the plan's P99, CONTEXT POST).
- The pin's band is the owner's open call: `global.json` names 10.0.115 (this bench's SDK, the 10.0.1xx band); before the
  pin CI built on 10.0.404, the band a current Visual Studio carries. Kept at 1xx, the maintainer's PC installs a 10.0.1xx
  SDK at 10.0.115 or above beside its own; re-pinned to 4xx, this bench installs 10.0.404 at the start of each session.
- **Read CI after every push** (a build clean here can fail there), and root-cause a red run before the next unit lands
  on top of it. A small agent reads the run list; the failed job's log is downloaded only when a run is red.
- Template: `workflows/patterns-chain.js` (units carry both the round's and the plan's numbers; a unit with no plan is one
  the round inserted; `args.notes` adds a line to one stage of one unit). A unit is inserted by stopping the chain as a unit
  lands and launching it again with the new list. The App suite runs only by six foreground parts through
  `workflows/patterns-app-parts.sh`. Run one App test run at a time on the machine (OPEN L111).

### PlayMap (repo Mapper) — two wire fixes and three test mends landed (`2ae7ac7b`); the rest waits on round 3's integration pass

- Landed and pushed, each a round-2 fix batch with its entry in `docs/round2/CARRYOVER.md`: PM-1 fix-showbus-identify, PM-2
  fix-showbus-keys, PM-3 fix-media-page-import, PM-5 fix-showbus-node-link (and its status-light arm), PM-4 fix-showbus-1.3,
  fix-proposal-sink-deadline, PM-6 fix-peerlink-core, PM-7 fix-stagewatch-timer-air, PM-8 fix-stagewatch-context-size,
  fix-desk-unconfirmed, PM-11 fix-context-peer-rows (`cddf83b6`, its leave extension `783319e1`), fix-wire-state-after-proof
  (`cb9be004`: a connection not paired at HELLO hears its greeting STATE only after "OK paired") and
  fix-wire-greeting-after-handshake (`031c9a3d`: a connection paired at HELLO hears it only once a dialler's handshake behind
  its HELLO is over; three comments reworded). Every leave and extension is in `owner-answers.md`.
- Test-only mends on the owner's answers of 2026-11-14 and 2026-11-15: fix-devicelock-test-thread (`f6181596`: CI run 473's
  red test; its second taker waited on a pool worker, reproduced under a starved pool) and fix-shownline-test-threads
  (`46129380`: four tests of the same shape).
- CI: run 475, on `f6181596`, green. Run 476, on `46129380`, red on the Windows job at one Net test no batch touched
  (`StagewatchLinkTests.AKeylessLinkAskedForTheViewingCodeIsRefusedInThoseWordsAndHeld`: two sockets and two connections
  where the held refusal allows one). Read from the code by two readers, not measured: the link's first ping is written
  before its first read (`StagewatchLink.BeatAsync`); a Stagewatch that sends one refusal and closes without reading answers
  the ping with a reset, Windows drops the unread refusal, no hold is recorded and the link redials on its backoff until a
  refusal is read. A product defect of low severity (bounded by the backoff; nothing on the air), latent since the link's
  beat was written. The mend (no ping before the first message is read; a test that fails without it on every platform) is in
  `src/PlayMap.Net/Links/StagewatchLink.cs`, outside every leave: it waits on the owner's word.
- fix-pool-bound-tests landed (`2ae7ac7b`: the ten tests, one helper `tests/Shared/OwnThread.cs`; its CARRYOVER entry names one
  more test of the shape, in `VerbRowTests`, outside the leave). CI run 477 on it: red on the Windows job at one Engine test no
  batch touched (`BackupMirrorLinkTests.AMirrorQuietBeforeItsNewConnectionIsSubscribed…`: the ten-second wait for the greeting's
  hold ran out). Read by two readers and probed outside the tree, not reproduced in the suite: a defect of the test. Each hold
  helper sets a task source whose continuation is queued to the local queue of the pool worker that then blocks in the hold, so
  the test's wake-up runs only if another worker steals it, which a pool that never goes idle does not do in time; the product's
  redial, pairing and greeting were traced and are not at fault. The mend is test-only (the hold signalled from a thread or
  queue that is not the blocked worker's, the waits for a hold a named patience; `HelloTests.AHelloIsCountedHeardOnlyOnce…` has
  the same shape) and waits on the owner's word. Not to do: a fence that floods the pool inside the suite.
- PlayMap's Windows job was red on runs 473, 476 and 477, each on a different intermittent test; Linux and the installer job
  were green on all three.
- Open in the last batch's CARRYOVER entry, for the owner: a dialler whose next line is not in when the node next reads after
  its HELLO is still greeted inside its handshake (a race, unmeasured); Patterns' node device on a trusted address counts one
  discarded STATE there and says why in its own words.
- Nothing more can start now: PM-9a–c and PM-10 (wp4-peerlink) are "first after round 3's integration pass", which has not
  happened in Mapper, and PM-12…PM-20 follow them per `master-plan.md`.
- Template: `workflows/train3-playmap.js` (several batches) or `workflows/playmap-desk-unconfirmed.js` (one batch).

### Open findings carried into the next units

- **The owner's to decide:** whether a PIN-only Stagewatch may be an open acceptor; the band of Patterns' SDK pin (1xx or
  4xx); the mend of PlayMap's first ping before its first read; the
  test-only mends of `BackupMirrorLinkTests`' holds (with `HelloTests`) and of `VerbRowTests`; the contract's word for an
  operator's CLOSE of a link on a line wire (Patterns OPEN L115). Three stray files at the filesystem root of the cloud container
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
