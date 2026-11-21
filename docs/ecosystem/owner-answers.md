# Owner answers (given 2026-10-25 by the maintainer, the owner of all four repos)

Every owner question in master-plan.md is answered with the plan's RECOMMENDED answer. In particular:

- X-branches: each repo's work commits to the working branch (Stagewatch, Patterns, Mapper). AGMessage's AG-1 is
  handed to the session that owns AGMessage's 0.5.0 branch (a note was given to the user); later AGMessage
  units land after 0.5.0 on a branch named then.
- P-numbers: Patterns rounds 89-99, PLAN §107-§117, as numbered in master-plan.md.
- P-ADR016: scope A (STATE, EYE, CONTEXT and SUBSCRIBE pair first; NAV, MENU, CUE LIST, ARCADE and ASSISTANT stay open and named).
- P-twin-secrets: yes, in Round 89 (SendSecrets off by default, refused on a plain twin link to another machine, words on both desks, a §22 walk row).
- P-OSC12h: yes (the 12-hour OSC carry-over ends; OSC ALLOW and loopback only on a paired desk; an ADR).
- P-legacy: keep labelled amber fallbacks for OTHER apps' older versions only, each in OPEN, removed at 1.4's default flip.
- P-scopes: follow / post / drive / operate as recommended (no OUTPUTS, TAKE/CUT, SCREEN or settings below operate).
- S-217: release 2.17 first if the maintainer runs release.ps1 before 2.18; otherwise 2.18 carries 2.17's section.
- S-releases: one release per train for 2.18 and 2.19; from 2.20 two adjacent trains may merge.
- S-sections: §13 PeerLink, §14 numbers.
- S-D0: yes — a window grant's page address rides exactly two carriers: a browser's address bar and the one verified
  PeerLink (or key-verified node link) to the grant's own app.
- S-exemptions: keep ?pin= in browser addresses and Companion TCP 9923's door as named §8 exemptions in 1.3; schedule
  9923's authentication with 1.4's default flip.
- S-interop-gate: yes (release waits for the interop job at the siblings' pins; the nightly heads run reports drift only).
- S-interop-ci: yes (a read-only SIBLINGS_READ_TOKEN, contents:read on the three repos; relax the userns AppArmor
  restriction on the ephemeral runner; forks skip with words). The maintainer creates the token.
- S-shared-keys: refuse the second app with words, a one-press "Make a key for <app>" on its Show network row, and the
  upgrade note in 2.18.
- S-brief: the maintainer writes the two CLAUDE.md lines when SW19.11L lands (or accepts them drafted for review).
- X-declines: all six declined for now (G46, G49 'Link all', G19's proposal picker, G33's WEB OPEN/TYPE, G34's
  assistant-proposable link, G21's crew-key ACK policy), recorded in the papers named in the plan.
- M-PMX: yes — PM-1 to PM-8 land now as named round-2 fix batches with CARRYOVER entries, spine lines through §5.7,
  merged between wave merges; WIRE, README and ROADMAP stay the integrator's through papers notes.
- M-content-before-W3: yes, before W3 starts; otherwise at the integration pass.
- M-wp4-peerlink: yes, first after the integration pass.
- M-trusted-addresses: yes (legacy Lines devices only, amber "trusted by address — not a proof").
- M-eye-spine: yes.
- M-decision7: yes, after W4, with CollectApproved=false.
- M-decision17: yes, after Round 96.
- M-G36: defer until someone asks; keep the design.
- M-windows-package: yes, beside or after wp4-peerlink.
- A-branch / A-hold-050 / A-direct-path: AG-1 goes to the 0.5.0 session (hold the tag until it lands); the direct path
  is lost until scoped keys exist.
- A-display-names: publish the admin-set name (60 characters at most) with a per-display "keep private" switch.
- A-versions: 0.5.1 and 0.6.0 as recommended.
- M-PMX extension (2026-10-25): the leave for fix-showbus-keys (PM-2) is extended to src/PlayMap.Net/Wire/WireServer.cs and
  src/PlayMap.Net/Http/HttpHost.cs (one nullable option each). fix-media-page-import's (PM-3) ask for the new
  src/PlayMap.Core/Media/PrivateAddress.cs is extended too (same day).

- M-PMX extension (2026-10-26): the leave for fix-showbus-node-link (PM-5) is extended to src/PlayMap.Engine/Host/NetworkHost.cs
  (the change already made) and to src/PlayMap.App/ViewModels/MainViewModel.cs's LinkLight arm only (held draws red; a live link
  that is not paired draws amber, as preflight and the context do).

- M-PMX extension (2026-10-26, later): the leave for fix-showbus-1.3 (PM-4) is extended to src/PlayMap.Core/Bus/ShowBus.cs,
  src/PlayMap.Net/Bus/PeerTable.cs and src/PlayMap.Engine/Host/NetworkHost.cs (Peers()'s conflict arm and OnPeer's Changed).
- S-brief (2026-10-26): the maintainer accepts all four drafted Stagewatch CLAUDE.md lines, with 2-D0 (scratchpad/brief-lines-draft.md).

- M-PMX (2026-10-27): a new round-2 fix batch, fix-proposal-sink-deadline, is allowed with the leave
  src/PlayMap.Engine/Assistant/StagewatchProposalSink.cs and tests/PlayMap.Engine.Tests/StagewatchSinkTests.cs: the sink's 10 s
  real-time HttpClient limit becomes a deadline on an injectable clock (a running node behaves as today), and a late or missing
  answer is said as "no answer within 10 s; the desk may hold it", never "did not take".

- M-PMX extension (2026-10-27): the leave for fix-stagewatch-timer-air (PM-7) is extended to src/PlayMap.Engine/Host/NetworkHost.cs
  (the timer JSON by its writer; the held blackout's hold and words), tests/PlayMap.Render.Tests/PlayMap.Render.Tests.csproj (the
  item group compiling TimerView.cs and SourceLeases.cs) and the new tests/PlayMap.Engine.Tests/Host/StagewatchTimerAirTests.cs.

- M-PMX (2026-10-27): a new round-2 fix batch, fix-desk-unconfirmed, is allowed: the reply carries its outcome
  (src/PlayMap.Core/Wire/Endpoints.cs, WireReply) and the desk draws an unconfirmed command amber with "it may have run — look at
  its state", never a refusal (EngineLinkClient and its readers, DeskSession's toast, the GO guard's Replied), with their tests and a
  CARRYOVER entry; the desk's 30 s reply wait stays. Desk files are the wave's desk package's: the leave covers the lines this needs.

- M-PMX extension (2026-11-11): the leave for fix-context-peer-rows (PM-11) is extended to src/PlayMap.Engine/Seams.cs (LinkView
  gains the init-only PeerId, through the spine lane) and src/PlayMap.Engine/Host/NetworkHost.cs (HeardId, the one rule that says
  which hello a link's far end is, and its two call sites).

- M-PMX (2026-11-14): the early STATE counted malformed (the finding in Stagewatch's docs/WHATS_NEXT.md, "PlayMap's early STATE
  counted malformed") is PlayMap's to change: "PlayMap changes: hold the STATE until the proof." A new round-2 fix batch,
  fix-wire-state-after-proof, is allowed with the leave src/PlayMap.Net/Wire/WireSession.cs (an unpaired TCP line connection's
  greeting STATE waits for "OK paired", as the web port's line channel already does; a connection paired at connect hears it
  after HELLO as today), the tests of the wire that hold it (tests/PlayMap.Net.Tests), the papers that word a connection's life
  (docs/WIRE.md and the documents that quote it) and a CARRYOVER entry. Stagewatch's core and SHOWBUS §13.14 stay as written.

- M-PMX (2026-11-14, second answer): fix-wire-state-after-proof (Mapper cb9be004) holds the greeting on a connection that pairs by
  proof; a connection PlayMap already trusts at HELLO (loopback when the settings trust it, a listed address, a node with no token)
  still hears it right after HELLO's OK, so a dialler still in its handshake there (Stagewatch's PlayoutLink on one laptop) may
  still count it. Asked "should PlayMap hold the greeting there too?" the owner answered "Yes". A new round-2 fix batch,
  fix-wire-greeting-after-handshake, is allowed with the same leave as fix-wire-state-after-proof (src/PlayMap.Net/Wire/
  WireSession.cs, the wire's tests under tests/PlayMap.Net.Tests, the papers that word a connection's life, a CARRYOVER entry),
  and every reader of the paired-at-HELLO order still hears its STATE. Asked whether the three comments that describe the old
  order may be reworded (src/PlayMap.Net/Wire/WireServer.cs's class comment, src/PlayMap.Net/Links/EngineLinkClient.cs's class
  comment, integrations/companion-module-playmap/src/link/session.ts's hello comment), the owner answered "Yes": comment lines
  only, in the same batch.

- M-PMX (2026-11-14, third answer): CI's run 473 on Mapper cb9be004 was red on the Windows job at one Core test no batch
  touched, GpuDeviceLockTests.ASharerWaitsForTheHolderAndABoundedTakeGivesUpWithinItsBound (its second taker, started on the
  thread pool, did not begin to wait within the test's ten seconds; green unchanged on run 474). Asked whether the test may be
  mended so that taker runs on its own thread, the owner answered "Yes". A new round-2 fix batch, fix-devicelock-test-thread, is
  allowed with the leave tests/PlayMap.Core.Tests/Media/GpuDeviceLockTests.cs and a CARRYOVER entry: test-only, no product line.

- M-PMX (2026-11-15): fix-devicelock-test-thread (Mapper f6181596) found the same dependency in four tests of
  tests/PlayMap.Core.Tests/Wire/ShownLineTests.cs (each waits 30 s on work it starts on the thread pool). Asked whether they may
  be mended the same way, the owner answered "Yes, mend the four ShownLineTests". A new round-2 fix batch,
  fix-shownline-test-threads, is allowed with the leave tests/PlayMap.Core.Tests/Wire/ShownLineTests.cs and a CARRYOVER entry:
  test-only, no product line.

- M-PMX (2026-11-15, second answer): fix-shownline-test-threads (Mapper 46129380) swept the other suites and found ten tests in
  nine files of the Engine, Media and Net suites with the same dependency (its CARRYOVER entry's Open list names each). Asked
  whether they may be mended too, as one batch with a small shared helper in tests/Shared, the owner answered "mend those ten
  too". A new round-2 fix batch, fix-pool-bound-tests, is allowed with the leave: one new helper file under tests/Shared, the
  nine test files that entry names (tests/PlayMap.Engine.Tests: Fleet/MachineSamplerTests.cs, Optimise/OptimiseWorkerEndTests.cs,
  Rendering/FactsSignalTests.cs, Rendering/EngineSourcesTests.cs, Phone/FolderCameraTests.cs, Control/DmxPersonalityTests.cs;
  tests/PlayMap.Media.Tests/Video/NativeBuildTests.cs; tests/PlayMap.Net.Tests: Phone/ToolProbeTests.cs,
  Companion/LineWebSocketTests.cs), a test project's csproj only where the shared file must be named to compile, and a CARRYOVER
  entry: test-only, no product line.

- Three calls after PlayMap's batches (2026-11-17): put to the owner as three questions with a recommendation each; the
  owner chose the recommended answer to each. What that allows, and no more:
  1. The approval desk sink's refusal words ("Mend it"): a new round-2 fix batch, fix-sink-refusal-words, a product line.
     An offer's refusal is kept apart from the pull's own problem: a pull that asked the desk nothing changes neither, and
     a pull the desk answers does NOT clear an offer's refusal (it clears when the desk takes an offer); `Problem` is the
     pull's if any, else the offer's; both written under the sink's gate. The design is fix-small-test-items' CARRYOVER
     entry. The leave: src/PlayMap.Engine/Assistant/StagewatchProposalSink.cs, its tests
     (tests/PlayMap.Engine.Tests/StagewatchSinkTests.cs: the 403 case kept as it is and now deterministic, and the new
     cases the design names), any paper that says when `Problem` clears, and a CARRYOVER entry.
  2. The manual Folder camera's refusal ("Keep as landed"): it stands in the scan's status for the whole re-wait, in place
     of the cue's "…, then SCAN NEXT", as fix-scan-cue-words (158d0b32) landed it. Nothing to do.
  3. Extra CI runs ("Up to three runs"): the orchestrator may dispatch the normal build on Mapper's working branch up to
     three times, stopping at the first run whose Windows Engine host stalls, to read a stall with the new measurements.
     No more than three without asking again; a push's own run is not one of the three.

- PlayMap's Windows job and what it showed (2026-11-16, second answer): five items were put to the owner with a
  recommendation each (all yes); the owner answered "Carry on with your recommendations". What that allows, and no more:
  1. fix-ci-windows-bounds (items 4 and 5): in `.github/workflows/build.yml`, a 60-minute limit on the windows job; in its
     test step a deadline per project that, when it runs out, prints the process tree, stops that tree and goes on to the
     next project; a proof, run on demand, of whether the hang guard's dump setting (`VSTEST_DUMP_FORCENETDUMP=1`) ends a
     stuck test host on Windows, or the setting's removal if the proof says it does not; and the measurement: a test-only
     probe in each test host (a file under tests/Shared, on only where the CI job turns it on) recording thread wake
     overshoot, pool dispatch latency and pool counts to a file both the Windows and the Linux job upload. The leave: the
     workflow file, a script or probe project under tools/ where the workflow needs one, tests/Shared, a test project's
     csproj only where a shared file must be named to compile, the papers that describe the job, and a CARRYOVER entry.
  2. fix-foldercamera-cue-words (item 1): the simulated person in `FolderCameraTests` waits for its own cue's words; a
     refusal ends the wait at once with its own message. The leave: tests/PlayMap.Engine.Tests/Phone/FolderCameraTests.cs,
     Phone/PhoneScanKit.cs, another test of the same shape only where the code shows it (DmxPersonalityTests' console
     start is to be read), and a CARRYOVER entry that also corrects fix-pool-bound-tests' line about this test. Test-only.
  3. fix-small-test-items (item 2): `StagewatchIdentifyTests`' fake Stagewatch speaks first (a beat), and
     `OptimiseWorkerEndTests`' hold is seen on a thread of its own; `StagewatchSinkTests`' 403 case is READ first, and is
     mended in this batch only if its defect is the test's and the mend is test-only: a product defect is written down
     with its path and design and put to the owner. The leave: tests/PlayMap.Engine.Tests/Host/StagewatchIdentifyTests.cs,
     Optimise/OptimiseWorkerEndTests.cs, the StagewatchSinkTests file, tests/Shared, and a CARRYOVER entry.
  4. fix-scan-cue-words (item 3), a product line: the scan's status never names a wait that has ended (between the last
     locate frame and the first pattern it still said the previous cue's "then SCAN NEXT"). The leave:
     src/PlayMap.Engine/Scanning/CuedCapture.cs (FolderCamera.cs or ScanRunner.cs only where the words are made), their
     tests, every reader's fixture that the words move, the papers that quote them, and a CARRYOVER entry.
  The owner did not object to the two CI actions taken on run 479 (a cancel and one re-run); a stuck run is cancelled and
  re-run once, bounded, and said.

- The six open calls (2026-11-16): put to the owner with a recommendation each, the owner answered "Yes to all". What that
  allows, and no more:
  1. M-PMX, PlayMap's first ping: the link sends no ping before its first message is read (CI run 476 on Mapper 46129380;
     `StagewatchLinkTests.AKeylessLinkAskedForTheViewingCodeIsRefusedInThoseWordsAndHeld`). A new round-2 fix batch,
     fix-link-first-ping, is allowed with the leave src/PlayMap.Net/Links/StagewatchLink.cs, tests/PlayMap.Net.Tests/
     StagewatchLinkTests.cs with the fakes it uses, a paper only where it words the beat's order, and a CARRYOVER entry: a
     product line, low severity, with a test that fails without it on every platform.
  2. M-PMX, the remaining test-only cases in one batch: `BackupMirrorLinkTests`' holds (CI run 477 on Mapper 2ae7ac7b),
     `HelloTests.AHelloIsCountedHeardOnlyOnceThePeerTableHasTakenItIn` (the same shape) and `VerbRowTests`' one test
     fix-pool-bound-tests' Open list names. A new round-2 fix batch, fix-hold-tests, is allowed with the leave
     tests/PlayMap.Engine.Tests/Fleet/BackupMirrorLinkTests.cs, tests/PlayMap.Net.Tests/HelloTests.cs,
     tests/PlayMap.Engine.Tests/VerbRowTests.cs, tests/Shared (the existing helper or one small file beside it) and a CARRYOVER
     entry: test-only, no product line.
  3. Patterns' SDK band: the pin moves from 10.0.115 (the 10.0.1xx band) to 10.0.404 (the 10.0.4xx band, the one a current
     Visual Studio carries), `rollForward` `latestPatch` as before. It lands as a unit of its own in Patterns; what the newer
     band's analysis reports is mended, never silenced. This bench installs the 10.0.404 SDK beside its own at the start of a
     session (docs/ecosystem/README.md says how).
  4. The contract's word for an operator's CLOSE on a line wire (Patterns OPEN L115): yes, as part of Show Bus 1.4 (a
     `disconnect` class on the line carrier, held `peerlink.hold.disconnect.ms`); 1.4 is not opened for this alone, and 1.3
     stays final until then.
  5. SW24.1's amber: confirmed. An accepted link at level 0 is amber by 13.16.5, so Stagewatch's own context is amber while
     PlayMap or AGMessage is linked at level 0; the headline stays the show's own. The owner may look at it on a real show.
  6. The preflight's stuck words (decision 16) stay as they are, also while the show thread is resuming.

- Patterns' SDK (2026-11-14): asked whether Patterns pins its .NET SDK (OPEN L114: CI's runner image built with a newer SDK whose
  analysis failed the build on every unit of Round 97), with patch updates only, at the cost that the maintainer's PC must have
  that SDK's feature band installed, the owner answered "Yes". It lands as a unit Round 98 inserts (a global.json with
  rollForward latestPatch, and CI installing the SDK it names).

- Models (2026-11-14): the light stages (a round's opening, the papers' check-and-commit) stay on the cheaper model; the work
  stages stay as set on 2026-11-12.

- The pause of 2026-11-12 is lifted (2026-11-14): "Continue development."

- PlayMap's Windows stall, after run 490's three legs (2026-11-18; asked with the legs' readings: output on = about four
  processors and a test of 2 min 36 s, output off = near zero and 9 s). Four answers, each the recommended one:
  1. NodeHostTests' child: "Mend it". Test-only: the test starts its child with the default project's one output not
     enabled, asserts from STATE that none is enabled and, on Windows, that the child reads High; the test host's own
     class is recorded each run; the papers say what CI stops seeing (a saturating 1080p draw under HIGH; the dispatched
     `nodehostab` job keeps measuring it). No second real-child test with a small output was asked for.
  2. `PreflightMachineTests`' leak of HIGH into the shared Engine test host: "Fix it after the mend". Test-only, and
     strictly after the NodeHost mend has landed and run green; watched over the next Windows runs.
  3. `tests/PlayMap.Devices.Tests/Simulators/FakeTcpServer.cs`' accept race (run 489's one red test): "Mend it".
     Test-only, with a test that fails without the mend; it rides in the NodeHost mend's batch.
  4. The product questions (a node's default priority class; whether a headless node draws outputs nobody sees; whether
     a render loop that overruns yields): "No change; measure first". Nothing in the product changes; a dispatched CI
     measurement of the hazard on its own subject is built (what a saturating HIGH process costs a normal-priority
     process doing real work, at HIGH and at NORMAL); the owner reads it, then decides.

- After PlayMap's runs 491 to 494 and the priority measurement's check (2026-11-21): the owner answered "Continue with your
  recommendations", read as approval of each recommendation in the report of 2026-11-18, all on PlayMap and none touching
  its product:
  1. libvmx's compiler on CI: prefer Visual Studio's own clang (the one its standard library is built for), print its
     version, and fail with words if the component is absent; no fallback to the runner's LLVM on the PATH.
  2. The priority probe: its reader rebuilds every frame slot (no stall that straddles the window's start is dropped),
     reports the burning lead before each window, marks pool waits still pending at the window's end as lower bounds,
     stamps each process's processor time at the window's bounds and records the runner's scheduler settings; its README
     and CARRYOVER text say what the data supports; the probe and its reader are built and checked on every push. One
     more dispatch of `prioritycost` follows once the reader is fixed, with the legs repeated and in another order (the
     orchestrator's recommendation, made in this answer's reading: one run per leg is n=1).
  3. The test-only items found on the way: the six other test fakes with the same accept race; `PlatformCallsTests`
     putting its test host back as found; `PreflightMachineTests` in a serial collection (the run order it changes no
     longer decides the host's class); and the race in `HostNetworkTests`' fake desk if a deterministic reading confirms it.
  Not in it: the product's default priority class and the other product calls stay as they are (2026-11-18, answer 4).

- After run 498 (the second priority measurement) and its check (2026-11-21, the day's second answer): "Continue with your
  recommendations", read as approval of the three recommendations in that report:
  1. The node's default priority class stays HIGH until a third measurement reports what HIGH buys the node beside what it
     costs the rest of the machine; then the owner decides with both halves. No product change to the class, the pools, the
     supervisor's or the desk's class.
  2. The race in PlayMap's link to Patterns is mended (a product change, a leave of its own: fix-patterns-link-answer-order):
     the link no longer takes its asked STATUS answer after a newer pushed STATE; the mend is in
     `src/PlayMap.Net/Links/PatternsLink.cs`, fenced by a test that fails without it on every platform; the Net suite's fake
     desk says its lines in one lane, as the Engine suite's already does.
  3. A third `prioritycost` dispatch (tools and CI only, a leave of its own: fix-priority-probe-third): a warm-up before the
     first leg and the "HIGH, all but one" leg early and late; the build's servers stopped before the legs and the sampler
     listing every process with its base priority; a deadline loop on the busy side, at HIGH and at NORMAL beside a competing
     Normal load (what HIGH buys); a leg keeping a whole core free; the labels say logical processors where they count them.
     It is dispatched once the batch is green on its push.

- After PlayMap's run 499 (2026-11-21; red on the Windows job at `NodeLogTests.EveryFileTheLogStartsBeginsWithTheBuild`, "line 3
  was not moved aside: the log has dropped 1", the second time after run 480): "go with your recommendation". A product change
  with a leave of its own (fix-nodelog-roll): NodeLog's roll on Windows replaces with POSIX semantics as `DurableFile` does, a
  failed roll is tried again without waiting for the next line, and it is counted on its own, never as a dropped line; the
  Windows test designed in PlayMap's CARRYOVER is added; the three replaces of the same shape (`SupervisorLog.cs`,
  `MediaTransfer.cs`, `PhoneKeys.cs`) are mended alongside.

# Commit identity (all repos, from 2026-10-25)
The committer is always the session's configured committer identity (each repo's git config says so; never override it with -c user.name/
user.email). The AUTHOR follows the repo: Stagewatch --author="jammin808 <b.rohleder@gmail.com>"; Patterns
--author="Patterns <b.rohleder@gmail.com>"; Mapper the default (Claude); AGMessage as its branch does.
