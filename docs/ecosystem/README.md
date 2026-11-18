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

## Where the work stands (2026-11-17)

The owner paused the work on 2026-11-12 and lifted the pause on 2026-11-14 ("Continue development"). Every repository's work
is on the session's designated working branch (the same name in all five repositories); each landed unit is pushed as it
lands, so a working tree holds at most the one unit in progress. AGMessage stays with its own session.

### Stagewatch — 2.24 closed (`fe8224e`); eight units after its papers landed (head `15d6e81` before this guide's commit: the owner's answers of 2026-11-17)

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
- 2.24 (Train 7), as landed and pushed: SW24.1 (`61e300e`: the context by Show Bus §6 P1–P8: one item and one row per
  playout link to the node proved or named at its other end, accepted `/ws` links as `link:ws:<id>` items, a window
  serves its holder only on a grant over the holder's own verified link, `clock:<node id>` per measured clock, the `vt`
  item, no message words in `recent`; unit 6236, e2e 93 of 93, LAN 41 of 41, laptop 5 of 5, offline 3 of 3) and SW24.2
  (`fe8224e`: the papers). It waits for its release on the maintainer's PC (`-Version 2.24.0 -ModuleVersion 2.24.0`).
- **Confirmed by the owner (2026-11-16, SW24.1):** an accepted link at level 0 is amber by 13.16.5, so Stagewatch's own
  context is amber while PlayMap or AGMessage (level 0 at these pins) is connected; lane rows S1.5 and S1c.3 accept that
  amber only when every amber item is a level-0 `link:ws:` link, and name the ids in their facts. The preflight's stuck
  words stay as decision 16 wrote them.
- After 2.24's papers, landed and pushed (each with its own row and section in `docs/WHATS_NEXT.md`):
  - SW24.1p (`bc41663`): the lane at Patterns' Round 99 (`52e176b`), LAN 44 of 44, laptop 5 of 5, offline 3 of 3: one
    card per app on Patterns' Eye (S9.1), the rig's 2.24 rows read (S9.2; the unplaced count compared at zero), the granted
    window drawn and withdrawn (S5.4; no `serves` edge for a held grant), the stuck rig read from both apps (S6.2b). The
    lane proved a defect of Stagewatch's own, mended in the same commit: a stuck show thread's context was served stale.
  - SW24.1q (`ec28135`), SW24.1r (`6931ddc`): two rounds of adversarial review of that mend, each of code a fold-in had
    written unread. As it stands: one served-stuck rule for every asked door (`GET /api/context`, `GET /api/state` with
    `"stuck":true` appended last, the MCP tools, the preflight, the Companion pong and its answered `SNAPSHOT`); no
    pre-stall context or frame served live after a stall; the context and the preflight built by time (1 s) on the hub's
    clock and served live up to the hub's 3 s plus that second, so a slow but live show thread is never served stuck; the
    Companion module draws no frame while stuck. The plan's "every link green" for a stuck rig cannot hold (13.16.1 makes
    a stuck show's link amber): the lane asserts the contract.
  - SW24.1s (`cf1817a`): the three items SW24.1r left open rather than build unread: no frame carries a verdict judged
    before a stall, every stuck rule ages on the hub's one clock (a wall-clock step moves no door), and the
    flags-before-pair order has a check. Open in `docs/WHATS_NEXT.md`: the preflight's stuck words (the owner's decision
    16, unchanged, now also served while resuming); for one rig tick after a stall `/api/state` is live while its
    `network` says stuck; the WPF desk walk-through and the UI smoke run (PC work).
  - SW24.1t (`b59c654`), a lane unit with no product change: the Patterns pin at Round 100's close (`a794633`); LAN 45
    of 45, laptop 5 of 5, offline 3 of 3. Rows S4.6, S4.7 and S9.1 read `told`; S6.2b reads every asked door while the
    rig's show thread is stalled; S9.3 (new) holds that no app at these pins posts a context to Patterns, so CONTEXT
    POST with real apps stays an open row.
  - SW24.1u (`30e6370`), a lane unit with no product change: pins at Patterns `d7ef79a` (built with the SDK 10.0.404 its
    `global.json` names) and PlayMap `cf576ffc`. Whole at the new pins before any row moved (LAN 45 of 45, laptop 5 of 5,
    offline 3 of 3); with its rows, e2e 95 of 95, LAN 48 of 48, laptop 5 of 5, offline 3 of 3. New rows: S2.1d (PlayMap's
    link to the rig stays one connection for 16 s, past PlayMap's 12 s drop bound: what fix-link-first-ping relies on,
    with the real apps; it does not tell the new pin from the old), S3.8 (Patterns' speaker message ends not-sent in both
    of 101.2's words and never reaches the rig), S3.9 (a message the rig took and never answered stays unconfirmed);
    S2.1b tightened to one socket. The rig's `nodes` control line ends each row with `connectedAt` (a tool, not the
    product). Open, each in `docs/WHATS_NEXT.md`: the order of PlayMap's first ping against the rig's first message (TLS,
    recorded by neither app); a keyless PlayMap link the viewing-code door shuts (no lane rig runs with `--view`); a rig
    whose web server never answers.
  - SW24.1v (`1f4e017`), a lane unit with no product change: the Mapper pin at `158d0b32` (PlayMap's five batches of
    2026-11-17); whole and green, nothing moved (unit 6256, e2e 95 of 95, LAN 48 of 48, laptop 5 of 5, offline 3 of 3;
    both siblings built with the SDK 10.0.404). No lane row reads STATE's scan block, so the lane proves nothing of
    fix-scan-cue-words beyond PlayMap building and every other row holding; a scan driver is written as open with its
    design in `docs/WHATS_NEXT.md`. `docs/ECOSYSTEM.md`'s lane line now keeps 2.23's 49 rows as 2.23's and gives 2.24's 56.
- **The fold-in rule (both chain templates, 2026-11-16):** a committing stage makes the changes its review names and
  builds no new mechanism; what needs one is written as open with its path and design, and gets a unit of its own.
  Three units in a row (SW24.1p, SW24.1q, Patterns 100.4) landed fold-in code no second reader had read, and each
  review of it found medium defects.
- The browser page-frame checks of the e2e lane (`/countdown`, `/audience`, `/holding`), which failed now and then beside
  another tree's load, had a race of the check's own (a frame index read once, before the window's reveal); SW23.1p names it
  and mends the check (`docs/WHATS_NEXT.md`). SW23.1 mended an earlier race of the same check; no run has shown these
  were the only causes, so a next sighting is read before anything is changed.
- The lane's rows assume a host no other build shares: PlayMap's own preflight reads red above 95 % CPU and closes the doors.
- Template: `workflows/stagewatch-chain.js` (its unit list is given at launch; briefs from `unit-brief.py`; refresh the
  first sentence of its shared text, which says what is landed, before a launch).
- Stagewatch's CI runs on main, tags and pull requests only, so nothing runs on the working branch.

### Patterns — Round 101 closed (`d7ef79a`); the plan has no further Patterns round

- Landed and pushed: Rounds 89–99. Round 98 is the plan's P97 (PLAN §116): this desk is a LINK acceptor on 9697.
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
- Round 99, the plan's P98 (PLAN §117, the Eye sees the room), closed at `52e176b`: 99.0 the opening (`7555b02`),
  99.1 EyeNetwork (`e990739`: one card per proved id, a link drawn once as its edge, unplaced rows counted), 99.2 one
  projection for lights (`8f62a0b`: precedence, not "worst"; five readers under one fence; a bounded brief; its body names
  three departures from §117.1 for the papers), 99.7 (`ff01e34`, inserted for the defect the lane found; below), 99.3 the
  feed (`04c2990`: one composition of the show network for every reader; the Eye's gather by revision), 99.4 the desk's
  readers (`6f5600f`: `NetworkMenus` shared by the Eye, the Nodes card and the deck; `EYE LENS network`; Accept new id),
  99.5 the wire's readers (`dc3166c`: EYE JSON's and STATE's appended fields, the Super Check's Eye network row, module
  3.24.0) and 99.6 the papers (`52e176b`: OPEN L125 to L130 added). CI green on every unit's push (runs 320 to 326); the
  run on the papers is read as Round 100 opens. One sighting stands unexplained: 99.5's validate saw
  `RemoteTrustTests.AnOpenDeskAndATrustedLoopbackAreGreetedWithStateAsBefore` fail once (REVIEW round 99 holds it).
- 99.7, as landed: a live typed-address Stagewatch link looks again at who is heard at that address and moves once to the
  node whose dial proves the key, by one rule in Core (`StagewatchAt`) shared with the resolver. A keyless or public-only
  link is never moved (13.4.3), a bound key is never sent to an unproved address, a move the node refuses goes back to the
  address at once, and each is one log line and one journal row.
- Round 100, the plan's P99 (PLAN §118), closed at `a794633`: 100.0 the opening (`d1c3df7`), 100.1 CONTEXT POST
  (`2365871`: another app gives its context over a verified PeerLink at scope post; gated ahead of the router; 128
  contexts, 64 KiB each, kept 30 s; a told source on the app's card), six units the round inserted, and the papers:
  - 100.3 (`60bd2ae`; CI runs 327 to 329): a still desk's baseline follows its own first readings; a reading moving
    within one light moves no word the Eye hashes; "errors caught and contained" named for what it counts.
  - 100.4 (`258ca83`; run 327's Pixera test) and 100.6 (`ac06129`; the review of 100.4's fold-in): a device session
    takes each connection's start once and sees its end; a line never sent is never counted; a timed-out Pixera cue
    plays nothing behind a failed receipt; stale items of an old link are dropped. Unwalked (device links).
  - 100.5 (`d4ebc37`; one bench sighting of 100.1's test): a link the desk has told its last word is closing for every
    reader until its connection ends; the grey row stays the observation of the socket's end; module 3.25.0.
  - 100.7 (`7e7eea5`; CI run 331): every still-Eye assertion takes its baseline after the Eye has read what the desk's
    own ticks left; tests only.
  - 100.8 (`3d6700a`; OPEN L128): the Stagewatch link's level-1 rise is published whole (record and key in one read).
  - 100.2 the papers (`a794633`): 3,789 tests; OPEN L128 closed, L131 to L134 added; the tag table's row for round 99.
  CI: 327 to 329 and 331 red (each root-caused and mended above), 330 and 332 to 335 green, 336 (on the papers,
  `a794633`) red on one App test.
- Round 101 (PLAN §119), a repair round outside the plan, closed at `d7ef79a` on 2026-11-16. Each case was read by one
  reader and one adversarial check against the code before its unit:
  - 101.0 the opening (`f2e20a4`).
  - 101.1 (`ebb8f8a`; CI run 336; tests only): a still window pins the Desk tick light red at its baseline, so a real
    desk tick crossing 16 ms inside the window no longer counts as a rebuild (OPEN L134 b). The pin stops catching a
    subject that itself slows the desk tick; OPEN L135 says the row's amber bound is unmeasured against the rig.
  - 101.2 (`a475624`; bench sighting; product): a speaker call whose request never reached a connection is not-sent; a
    call whose deadline runs out before any connection is also not-sent now; written and unanswered stays unconfirmed.
  - 101.3 (`5520584`) and 101.6 (`85761f5`; bench sighting; product): the key's standing and the approval words are one
    value written in one step; a context answer's words are its own, a 2xx takes back only a context answer's words, an
    overtaken answer writes none. Left open (L133): the approval queue's own refusal words have no overtaken check.
  - 101.5 (`bcbb74e`; the owner's answer of 2026-11-16): `global.json` names 10.0.404. The newer band's analysis
    reported nothing in the tree; CI run 341 built every job on 10.0.404, green.
  - 101.4 the papers (`d7ef79a`): tag row for round 100 = `a794633`.
  CI: runs 337 to 343 green, every push of the round (342 on 101.6, 343 on the papers).
- What remains of the plan for Patterns after Round 101 is nothing; PlayMap's Train 8 waits behind round 3's integration pass and the
  owner's gates, AGMessage's Train 9 is its own session's.
- A stopped chain is relaunched with the remaining units (the template takes the list); a unit is inserted by stopping
  the chain as a unit lands, with the tree clean.
- The pin's band (the owner's answer of 2026-11-16): 10.0.404, the 10.0.4xx band a current Visual Studio carries,
  landed as Round 101.5 (`bcbb74e`) and proved on CI (run 341). The maintainer's PC needs a 10.0.4xx SDK at 10.0.404 or
  newer; with only another band dotnet refuses ("A compatible .NET SDK was not found. Requested SDK version: 10.0.404").
  Apt's `dotnet-sdk-10.0` gives this bench only 10.0.115, so at the start of a session the 10.0.404 SDK is added beside
  it, touching no file apt installed: download `dotnet-sdk-10.0.404-linux-x64.tar.gz` from builds.dotnet.microsoft.com,
  check its SHA-512 against the release metadata, and
  `tar -xzf … --skip-old-files -C /usr/lib/dotnet ./sdk/10.0.404 ./packs/Microsoft.NETCore.App.Host.linux-x64`. Both
  SDKs carry the runtime 10.0.15. Without it Patterns does not build here. Stagewatch and Mapper, which pin nothing,
  build here with the newest SDK present (10.0.404 once it is added; what Mapper's CI installs).
- **Read CI after every push** (a build clean here can fail there), and root-cause a red run before the next unit lands
  on top of it. A small agent reads the run list; the failed job's log is downloaded only when a run is red.
- Template: `workflows/patterns-chain.js` (units carry both the round's and the plan's numbers; a unit with no plan is one
  the round inserted; `args.notes` adds a line to one stage of one unit). A unit is inserted by stopping the chain as a unit
  lands and launching it again with the new list. The App suite runs only by six foreground parts through
  `workflows/patterns-app-parts.sh`. Run one App test run at a time on the machine (OPEN L111).

### PlayMap (repo Mapper) — head `15499771`: the stall's mend holds in both test orders (runs 491, 492); the priority cost is measured once (run 493) and checked; the leak's fix is in flight; libvmx's compiler on the new runner image is the owner's call

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
- Two batches on the owner's answers of 2026-11-16 ("The six open calls", items 1 and 2), both pushed:
  fix-link-first-ping (`9b7d1fca`: the Stagewatch link writes no ping before the connection's first message is read; the
  silence rule still runs from the connection's start; CI run 478 green on all three jobs) and fix-hold-tests
  (`cf576ffc`: the holds of `BackupMirrorLinkTests`, `HelloTests` and `VerbRowTests` are seen on a thread of their own;
  tests only).
- CI run 479 on `cf576ffc`: Linux and the installer green. The Windows job's test step was open 85 minutes on the first
  attempt (the 5-minute hang guard did not end it; the job has no time limit) and was cancelled; no log of that attempt
  exists (the job's log is not served and the run's archive lacks it). Read by three readers and an adversarial check,
  nothing run: every wait the batch added has a deadline and the batch is not shown at fault; the best-supported suspect
  is the guard's own hang-dump path on Windows (`VSTEST_DUMP_FORCENETDUMP=1`, set on that job since 2026-10-22 and by
  PlayMap's own notes never yet exercised there). It stays unexplained. Re-run once: the step took its normal 14 minutes
  and failed on one Engine test, `FolderCameraTests.AManualFolderTakesTheNewestPictureOnScanNext` ("the scan never
  ended" at its 2-minute wait); the batch's three test files passed on Windows.
- That failure, read by three readers and an adversarial check with the re-run's log (cause read, not observed): a race
  in the test. Its simulated person takes any "…then SCAN NEXT" as its cue; between the last locate frame and the first
  pattern the previous cue's words still show, so it presses before the camera waits, PlayMap refuses (correctly), the
  refusal is hidden and the scan waits past the test's 2 minutes. Not Windows-specific; made likelier by
  fix-pool-bound-tests (`2ae7ac7b`), which moved that person onto a thread of its own and whose CARRYOVER entry
  misstates the test's shape; `cf576ffc` did not touch the path. It does not explain the 85 minutes. The mend is
  test-only (`FolderCameraTests.cs`: wait for the cue's own words; optionally `PhoneScanKit.cs`: a refusal ends the wait
  with its own message) and waits on the owner. A product point surfaced beside it, the owner's to judge: the scan
  status keeps showing the previous cue's "then SCAN NEXT" through that gap (`src/PlayMap.Engine/Scanning/CuedCapture.cs`).
  PlayMap's Windows job has been red or hung on five of its last six runs, each on a different test.
- The owner answered all five on 2026-11-16 ("Carry on with your recommendations"; `owner-answers.md`, "PlayMap's Windows
  job and what it showed"), as four batches. Landed and pushed so far:
  - fix-ci-windows-bounds (`f8e038a7`): the Windows job ends at 60 minutes; `tools/ci-tests.ps1 -Suites` runs each test
    project under a deadline of its own (30 minutes for Engine, 10 for the rest) that prints the process tree, stops it,
    fails the project as `<name>(deadline)` and goes on; a `hangproof` dispatch input runs only a job that proves the hang
    guard; `tests/Shared/SchedulingProbe.cs` in every test project writes a row every 50 ms (wake overshoot, pool dispatch
    latency, pool counts) where CI sets `PLAYMAP_SCHEDULING_PROBE=1`, uploaded from both jobs.
  - fix-foldercamera-cue-words (`1a6386db`): the folder camera test's person waits for its own cue's words; a person's
    fault ends the wait for the scan at once. Shown by a planted hold in a scratch copy, never observed on the runner.
  - fix-small-test-items (`ab22f621`, tests only): the identify tests' fake Stagewatch speaks first and the theory holds
    its link past the 12 s drop mark on a clock it controls; `OptimiseWorkerEndTests`' hold is seen on a thread of its own.
    `StagewatchSinkTests`' 403 case was read to a PRODUCT defect and not mended (below).
  - fix-ci-stall-measure (`dd4d82f5`, the orchestrator's batch, tests and CI only): the probe writes eighteen columns
    (completed pool items, GC pause, working set, process CPU, its own write time, a second highest-priority beat) under
    a facts line; `NodeHostTests` records the child engine's own start and "is up" stamps beside when the test saw them
    (`*.nodehost.csv`); `tools/ci-tests.ps1 -Suites` runs a one-row-a-second machine sampler and prints Defender's status
    once; `NodeLogTests`' failure says the dropped count and both files' lengths; `tools/probe-read.py` reads the rows.
  - fix-scan-cue-words (`158d0b32`, the product line): a Folder camera's words stand in STATE's scan block, as the stage
    `waiting`, only while it waits for the picture they ask for (nine stale points); a wait given up is refused on SCAN
    NEXT instead of answered "taken"; a manual camera's refusal now stands for the whole re-wait (the owner's call, put).
- The hang guard's proof (run 481, dispatched on `f8e038a7`): on Windows, on an idle runner, blame ended a never-ending
  test at 60 s with `VSTEST_DUMP_FORCENETDUMP=1` and without it; `dotnet test` returned at 63 s; a 4.2 MB dump each;
  `dotnet-dump` named the test. The setting stays. It does not rule the dump path out for a large host, so run 479's 85
  minutes stay unexplained; the deadline and the job's limit now bound a repeat.
- CI run 480 on `f8e038a7`: Linux and the installer green; Windows red in the Test step at two Engine tests (the new
  script ran every project to its end, no deadline hit). Read by five readers and an adversarial check against the
  probe's rows, nothing run:
  - `SettingsWriterTests.ASyncRoleAnEpochTheLinkAndANewTokenSurviveEachOther` was a victim, not a defect of its own:
    from 23:34:15 to 23:35:09 a work item queued to the Windows Engine host's thread pool waited up to 12.7 s to start
    while the probe's own wake overshoot stayed at its floor (the host was not saturated as that thread saw it); the
    probe's dedicated thread itself stopped twice for over 7 s; four tests that take seconds each took 52 to 56 s and
    one's 10 s wall-clock wait ran out. Linux, same commit: no queued item waited longer than 0.25 s. Run 465 has the
    same shape. Both windows begin as `NodeHostTests.TheNodeHostRunsAnEngineThatAnswersPing` starts its child engine and
    end as the child is seen up. What held the workers is not known.
  - `NodeLogTests.EveryFileTheLogStartsBeginsWithTheBuild` ("line 1 was not moved aside") is separate: no stall in its
    window. Likeliest, read not shown: one roll's `File.Move` threw on Windows, was swallowed and counted as a dropped
    line, and is retried only when another line is logged (`src/PlayMap.Engine/Host/NodeLog.cs`).
  - Refuted by the check: that the child's kill ended the stall (recovery is 0.18 s before it); that the pool's thread
    count counts blocked workers; that a higher pool minimum, a parallelism cap or a bigger runner would cure it; that
    the probe caused it. Not to do: widen the tests' waits, set a pool minimum or cap xUnit's threads, exclude anything
    from Defender, or credit runs 473, 477 and 479 to this stall (they had no probe).
- Four more Windows readings, each read with the probe (`tools/probe-read.py` on the Engine host's rows and trx):
  - Run 482 (`1a6386db`), green: the same stall in a passing run. NodeHost took 56 s; queued pool work waited up to
    11.9 s; the probe's thread wrote nothing for 9.3, 12.9 and 7.4 s; the tests beside it had long waits and passed.
  - Run 483 (`ab22f621`), green: no stall; NodeHost 2.7 s.
  - Run 484 (`dd4d82f5`), green: the new instruments' first run, all working (849 sampler rows, no warning; Defender
    never over 3.1 % of a processor; memory never under 9.8 GB); no stall; the child engine up 3.8 s after its start.
    The sampler's own rows gap up to 9.5 s at full processors with no stall in the host: read it beside the probe's beat.
  - Run 485 (`158d0b32`): Linux and the installer green; Windows red on one test only, `StagewatchSinkTests`' 403 case
    (the product defect below), not the scan change; no stall; the child engine up in 9.8 s.
  So across the seven Windows runs with a reading the stall and a slow NodeHost child go together both ways: 465 (45 s),
  480 (54 s, red), 482 (56 s, green) stalled; 466, 467, 483, 484, 485 (2.7 to 9.8 s) did not. None has stalled since the
  new columns landed: the cause waits for the next stall.
- The owner's three answers of 2026-11-17 (`owner-answers.md`) and what came of them:
  - "Mend it": fix-sink-refusal-words (`f8282af0`, the product line `StagewatchProposalSink.cs`): an offer's refusal and
    the pull's own problem are kept apart; an answered poll does not clear a refusal; three new tests. Open in its entry:
    a refusal can outlive its proposal; two offers in flight settle by answer order; a refused collect stands until a
    later pull succeeds.
  - "Keep as landed": a manual Folder camera's refusal stands for the whole re-wait. Nothing to do.
  - "Up to three runs", stopping at the first stall: run 486 (no stall, NodeHost 2.1 s), run 487 (STALLED, NodeHost
    17 s, red on `NodeCredentialStoreTests`, a victim). The third was not dispatched. Run 488, the push of `f8282af0`
    and not one of the three, stalled too (NodeHost 48.6 s, red on `ClockSlewTests`, a victim; the sink's 403 case
    passed).
- What the two instrumented stalls showed (observed): once the child engine of
  `NodeHostTests.TheNodeHostRunsAnEngineThatAnswersPing` is up it holds all four processors (487: about 377 % of one
  processor for 8 s; 488: 390 to 395 % for about 46 s); the test host gets zero processor time and completes zero pool
  items (488: 496 of 529 probe rows), its Highest-priority beat is unrun for over a second at a time, and the sampler's
  own process is held (488: samples of 18, 17 and 11 s); memory, disk and Defender are quiet; it ends as the test kills
  the child.
- The cause as read by five readers and an adversarial check (run 487; read from code, NOT yet recorded on the runner):
  the child claims HIGH priority class by default (`src/PlayMap.Core/Machine/MachineSettings.cs:23`, claimed in
  `MachineParts.Configure` before the "is up" line) and draws the default project's one 1920x1080 60 Hz output offscreen
  through Mesa llvmpipe, which the Windows job copies beside `dotnet.exe`; a frame costs about one period, and when it
  reaches the period `RenderHost.Pace` starts the next at once, so four busy threads at HIGH shut out every Normal-class
  process. On Linux the child claims nothing and never stalls. Most Windows runs escape because the test kills the
  child within 75 to 150 ms of seeing it up. Overturned from run 480's pass: "the child came up late", "the CPU was
  not saturated", "the kill did not end it". Not on a shipped node as such: no Mesa ships, so its GL cannot fall back to
  llvmpipe; the real-node hazard is CPU-bound in-process work under HIGH (Compute, FFmpeg, HAP workers), unmeasured.
  Unknown: the child's class and renderer on the runner, when saturation starts, whether runs 480 and 482 share the
  cause, why run 479 hung 85 minutes.
- fix-ci-nodehost-measure landed (`eccb35a0`, tests and CI only; the orchestrator's batch on "no governor follows
  without a reading"): the nodehost row carries the child's priority class, renderer, processor time, thread count and
  the stamps of connect, PING, STATE and kill; the sampler carries each process's base priority and the processor queue
  length; a `nodehostab` dispatch input runs only a job that runs NodeHostTests' PING test three times with a 5 s hold.
  Its CARRYOVER entry carries runs 487 and 488.
- Run 490 (the `nodehostab` job, dispatched once on `eccb35a0`), recorded on the runner, each leg n=1, A first:
  - A, as the suite runs it: child High (base 13) on llvmpipe, 3.98 processors over the hold; the test host's probe
    silent for 32, 30 and 28 s, its Highest beat for 50 s, queue length 352; the test took 2 min 36 s.
  - B, the child's home at class normal (base 8): 3.80 processors; the probe silent up to 9.6 s, the host's pool held
    for the child's whole life (14 items in 38 s), the beat on time; 45 s.
  - C, default class, the one output not enabled: about 0.03 processors once started; nothing held; 9 s.
  - The test host and `dotnet test` run at base 6 (Below Normal) in that job; where 6 comes from is not known.
- Run 489 (the push of `eccb35a0`): Linux and the installer green; Windows red on one Devices test only,
  `NecDriverTests.EveryCommandIsRefusedUntilTheLinkAllowsUnverifiedCommands` ("Not listening"), read to a race in the
  test's `Simulators/FakeTcpServer.cs` (a test that ends at once stops the listener as the accept loop calls it; that
  exception is not caught). No stall: the child High on llvmpipe, killed 0.1 s after up.
- What two readers and an adversarial check made of it (scratch `wf2/ci490-findings.md`; the reading they tested is
  `wf2/ci490-reading.md`):
  - The demand is the enabled 1080p output drawn through llvmpipe (about 3.6 to 3.9 processors, at High or Normal).
    The class does not change the demand; it decides who loses.
  - The stall needs a second condition: the test host below the child's class. `PreflightMachineTests.OffWindowsState…`
    claims HIGH on the shared Engine test host and never restores it (run 489's sampler shows the host going from base 6
    to 13). In the runs where it ended before NodeHost began (484, 485, 486, 489) nothing stalled; where NodeHost came
    first, four of five stalled (480, 482, 487, 488; 483 escaped). The order varies because xUnit runs collections in
    parallel. For 480 to 488 the host's class is inferred from the order, not recorded.
  - Corrected: "most runs escape by exposure" (the 487 check's guess) is wrong; STATE's `frameMsP95` for a headless
    output is the loop's present interval, not draw cost; the sampler's lists pair by name, never by position.
  - With no enabled output the render loop still runs and paces under the default claims; only the draw is skipped.
- The owner's four answers of 2026-11-18 (`owner-answers.md`): mend the NodeHost test; fix the leak after that mend is
  green; mend the fake TCP server's race; the product unchanged, the hazard measured first.
- fix-ci-nodehost-mend landed (`7ffde5c6`, tests and CI only): NodeHostTests' child starts on the default project with
  its output not enabled (asserted from STATE; on Windows the child must read High); the dispatched legs are A output on,
  B output on at class normal, C as the suite runs it; the nodehost row ends with the test host's class at start and at
  kill; `ci-tests.ps1` prints the step shell's and its parent's class; `FakeTcpServer`'s accept after the stop ends
  quietly, fenced by `FakeTcpServerTests` (fails without the mend). Its CARRYOVER entry carries runs 489 and 490 and the
  check's findings. Open in it: six other test fakes with the same accept shape (two can throw on their first round:
  `FakePatternsDesk` in `HostRig.cs` and in `LinkTests.cs`), not edited; two Engine tests that failed on the bench,
  outside the leave (`ContentHostTests.ARoutedGeneratorDrawsOnItsItemsTimeAndAStingReachesTheFrame`, 3 of 3 in its part,
  also at `eccb35a0`, passes alone, cause not named; `HostNetworkTests.ADesksBlackoutIsHeldWithItsAge…`, once, read to a
  race in the test's fake desk).
- Run 491 (the push of `7ffde5c6`): Linux green; Windows GREEN, 1,369 Engine tests passed, in the order that used to
  stall (NodeHost at 19:16:13, before PreflightMachineTests at 19:17:02): the test host BelowNormal at start and at kill
  (recorded, no longer inferred), the child High on llvmpipe with its output not enabled, NodeHost 4.2 s, no probe gap.
  The step's pwsh and its parent `Runner.Worker.exe` both read BelowNormal, base 6: that is where base 6 comes from.
- Run 491's installer job is RED, not from the mend: its step "libvmx (OMT's codec, beside the exe)" failed with
  `STL1000: Unexpected compiler version, expected Clang 22 or newer`. That job drew runner image `windows-2025-vs2026`
  20261116.315.1 (Visual Studio Enterprise 2026 18.12, MSVC 14.52.37025), whose STL refuses the image's own LLVM 20.1.8
  on the PATH; the Windows job drew the older image 20261103.298.1 and built libvmx. The same break reaches the Windows
  job (its "native test doubles' compiler" step takes the PATH's clang too) once it draws the new image. The image lists
  Visual Studio's own Clang components (`VC.Llvm.Clang`) and the 14.44 toolset. `build.yml` lines 351-369 and 660-673
  prefer the PATH's clang and fall back to Visual Studio's. libvmx.dll ships in the installer, so which compiler builds
  it is put to the owner (recommended: prefer Visual Studio's own clang, print its version, fail with words if absent).
- fix-ci-priority-measure landed (`15499771`, tools and CI only): a `prioritycost` dispatch input; `tools/priority-probe/`
  (project file `.csproj.in`, built from a copy outside the checkout): a burner that claims HIGH or NORMAL through the
  product's own `ClaimProcess` and reads it back, beside a victim set to Normal doing a frame loop (about 10 ms of work
  every 16.7 ms), a 50 ms tick and a pool item; six 20 s legs; a summary table per leg and as ratios to the baseline.
- Run 492 (the push of `15499771`): all three jobs green; every job drew the OLDER runner image (20261103.298.1), which
  is why libvmx built. The other test order this time (PreflightMachineTests at 19:47:37, NodeHost at 19:49:18): the
  nodehost row records the test host HIGH at start and at kill (the leak, recorded), the child High with no output,
  NodeHost 4.7 s. So the mend holds with the host below normal (491) and at High (492).
- Run 493 (the `prioritycost` job, dispatched once on `15499771`; one run per leg, fixed order, 4 processors), as two
  readers and an adversarial check corrected it (scratch `wf2/ci493-findings.md`, the reading `wf2/ci493-reading.md`,
  raw files `ci493/pc/`); frames done of 1200 slots / missed, longest stretch with no frame, longest pool wait:
  - baseline: 1200 / 0; 18 ms; 0.16 ms.
  - HIGH on all 4 processors: 2 / 1198; 17.6 s (ended as the burner stopped); at least 17.6 s. Every victim thread,
    the 50 ms tick included, froze about 6 s from the burner's start.
  - NORMAL on all 4: 1096 / 104 (8.7 %); 73 ms; 31 ms. The runner's own Below Normal processes were held for seconds.
  - HIGH on 3 of 4: 1157 / 43 (3.6 %); 100 ms; 84 ms. Most misses ride a disturbance about every 550 ms, source unknown.
  - NORMAL on 3 of 4: 1195 / 5; 43 ms; 19 ms.
  - HIGH on all 4, the victim left Below Normal: 6 / 1194; 8.3 s.
  The check's corrections: the probe's reader undercounts (its "due" rule drops a stall that straddles the window's
  start: it printed 1055, 41 and 1049 missed); the tick did NOT keep running beside HIGH; pool waits pending at the
  window's end are lower bounds; processors used are from the burner's ready to its stop, not the window; cores minus
  one is each pool's own width (Compute, HAP), not a floor the node holds, and nothing caps their sum; what HIGH buys
  the node itself is not measured by any leg. README and the CARRYOVER entry say three things the data does not support
  (a missed period's meaning, the window "cut to the time its burner was burning", N-1 as the pools' width).
- Next for the measurement (each put to the owner; a second dispatch is the owner's to allow): the reader's and the
  papers' corrections (tools only); a second dispatch; legs in another order and repeated; the runner's scheduler
  settings recorded; a trace for the 6 s release and the 550 ms disturbance; a real workload as the burner.
- In flight: fix-preflight-priority-leak (scratch `wf2/playmap-leak.js`, generator `wf2/gen-playmap-g.py`):
  PreflightMachineTests leaves the shared Engine test host as it found it, with a fence. From it on every Engine test on
  the Windows runner runs at the runner's Below Normal class: read the next Windows runs for a test that only passed
  at High.
- Not to do: land (3) before (1); raise the test host's class as a mend; use class normal as the mend; change the
  PROCESS default, the headless draw, Pace, MMCSS or the pools on this CI evidence; and the earlier list (serialise or
  skip NodeHostTests, `LP_NUM_THREADS` job-wide, stop copying Mesa, lower the child's class from outside, widen waits,
  change pool minimums).
- For the owner, none taken:
  1. The default priority class of a node (HIGH today; NORMAL, or HIGH only under a named condition such as installed,
     armed or a hardware adapter on the air). Round 3 §9 decision 12 is the standing call.
  2. Whether a headless node draws outputs nobody sees (already put in `docs/round2/CARRYOVER.md`, about line 1395).
  3. Whether a render loop that misses its period yields a bounded slice; a processor floor for the rest of the machine.
  4. Whether NodeHostTests may start its child with no enabled output (put to the owner on 2026-11-17 with run 490's
     legs; the claims and the loop stay exercised, the saturating draw leaves the default suite), and the test-only
     mend of `FakeTcpServer`'s accept race (run 489).
  5. `NodeLog`'s roll on Windows (a failed move is counted as a dropped line and retried only on the next line), with
     `SupervisorLog.cs`, `MediaTransfer.cs` and `PhoneKeys.cs` of the same shape; the deterministic Windows test waits on it.
  6. A dump written when a stall is seen; any pool minimum or parallelism cap in test hosts; the runner's size or image;
     Defender's settings: each recommended no.
- The readings and raw files are in the session's scratch (`wf2/ci480-findings.md`, `wf2/ci487-findings.md`, `wf2/ci490-findings.md`,
  `wf2/ci493-findings.md`, `ci480/` to `ci493/`); Mapper's CARRYOVER entries of fix-ci-stall-measure and fix-small-test-items carry runs 480 to 483 and the
  defect; fix-ci-nodehost-measure's entry is to carry runs 487 and 488 and the cause as read.
- Nothing more can start now: PM-9a–c and PM-10 (wp4-peerlink) are "first after round 3's integration pass", which has not
  happened in Mapper, and PM-12…PM-20 follow them per `master-plan.md`.
- Template: `workflows/train3-playmap.js` (several batches) or `workflows/playmap-desk-unconfirmed.js` (one batch);
  `workflows/playmap-ci480-root-cause.js` and `workflows/playmap-ci487-root-cause.js` are the read-only root-cause passes
  (readers by lens, one adversarial check).

### Open findings carried into the next units

- **The owner's to decide:** whether a PIN-only Stagewatch may be an open acceptor. Answered on 2026-11-16
  (`owner-answers.md`, "The six open calls"): PlayMap's first ping and the test-only holds (two batches, launched that
  day: fix-link-first-ping, then fix-hold-tests), the SDK band (10.0.404), the CLOSE word (with Show Bus 1.4, not before).
  Three stray files at the filesystem root of the cloud container (`/WebServer.cs.bak`, `/WebServer.cs.keep`,
  `/module.log`: stages' scratch copies, no secret in them) could not be removed from the session; they are in no repository.
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
