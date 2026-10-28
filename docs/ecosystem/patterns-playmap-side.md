# The Patterns and PlayMap side — ranked work packages each repo's process can take (revised after critique)

Area: patterns-playmap-side. Gaps: G03, G04, G09, G12, G13, G21, G25, G31, G33, G34, G35, G36, G47, G48.
Design only: nothing in any repo was edited or built. Paths are repo-relative; line numbers are the trees as read on
2026-10-25 (Patterns at `bcdbf28`, Round 88.1's last commit; Mapper at `f647094`, round 3 with its W1 packages built;
Stagewatch at `855e8b6`).

Requester and evidence class (Patterns' round shape, `Patterns/CLAUDE.md` "How a round opens" item 3): the maintainer's ask
("StageWatch also needs to be able to interact with AGMessage, PlayMap, and Patterns. Consider use of the ShowBus"), answered from
**a handed-in critique** — the verified cross-repo gap analysis, and a critic's review of this design's first draft — and a code
reading of all four trees. No field files arrived: `Patterns/docs/field/` holds only its `README.md`, so each Patterns round's
"what the files said" is "none this round".

This area is the **consolidation** for Patterns and PlayMap. Four sibling designs already name Patterns and Mapper work
(cross-app-proposals, peers-not-screens, windows-for-peers, spec-1-3; playout-links names none for Patterns and one docs unit for
Mapper). This design keeps their content and wire shapes where they own a shape, adds the gaps no sibling covers (G04, G21, G25, G31,
G34, G35, G36 and the safety half of G12), and fixes **one order by show risk**. §9 maps every sibling unit to its place in that
order. §10 answers the critic's review item by item.

---

## 0. What exists (evidence)

### G04 — a stuck Stagewatch is not seen by Patterns
- Stagewatch says it is stuck only with `{"type":"beat","live":false}`, repeated every `HeartbeatEvery` (2 s) while stuck, and sends no
  frame meanwhile (`Stagewatch/src/Timer.Remote/WebServer.cs:1265-1271`, constants `:1295-1297`). On recovery it sends the current
  frame first (`lastSent = null`, `:1269`). Its state frames carry no `live` field (`Stagewatch/src/Timer.Core/SnapshotJson.cs` writes none).
- Patterns' receive loop handles `pong` and `state`/untyped only (`Patterns/src/Patterns.App/Services/StagewatchLinkService.cs:523-538`).
  It refreshes `_lastWordAt` on every message (`:522`). `StagewatchFrame.Parse` reads `live` only inside a state frame
  (`Patterns/src/Patterns.Core/Services/StagewatchFollow.cs:61`) and returns null for a beat (`:43`).
- The link stays Live for a second reason: Patterns pings every 2 s (`StagewatchLinkService.cs:337-346`), and Stagewatch answers the
  ping on the socket's receive loop, off the show thread (`Stagewatch/src/Timer.Remote/WebServer.cs:1349-1355`).
- **Two writers of `_sight`.** The receive loop writes the frame (`StagewatchLinkService.cs:533`) and the beat task writes the context
  (`_sight = _sight with { Context = context }`, `:377`, called from `BeatAsync` at `:350`). Both are read-modify-writes of one
  `volatile` record (`:34`), so whichever lands second can undo the other's change.
- The pure follow logic already draws a frame with `Live=false` as no time: `StageFollow.Time` returns Idle when `!frame.Live`
  (`StagewatchFollow.cs:136`), and `StageFollow.Words` says "it says its show is stuck" (`:167`). The only test feeds a state frame
  with `live:false`, which Stagewatch never sends, though the parse path it covers is real (`Patterns/tests/Patterns.Core.Tests/ShowBusTests.cs:374`).
  `docs/REMOTE.md:645` promises the behaviour.
- Readers of the held frame: the stage pages and STAGE STATUS (`Patterns/src/Patterns.App/Services/StageService.cs:52-57`, `:207-219`),
  through `AppServices.FollowedStagewatch` = the link's `Sight` getter (`Patterns/src/Patterns.App/Services/AppServices.cs:934`);
  STATE `bus.stagewatch` (`Patterns/src/Patterns.App/Services/CommandRouter.cs:754-779`); the twin relay to timer and caller nodes
  (`Patterns/src/Patterns.App/Services/TwinService.Followers.cs:204`), whose node repaints only when `Frame.Seq` or the state moves
  (`:357`); the Eye's show-app light (`Patterns/src/Patterns.Core/Services/Eye.cs:778-783`); the context's peers row and the brief
  (`StagewatchFollow.cs:242-254`, `:269-285`); and the **Nodes page card**, whose hue and line come from the state alone
  (`ShowAppCard.Of`, `StagewatchFollow.cs:299-310`).
- `StageService.Following` is "a frame is held and the state is not Gone" (`StageService.cs:61`); while it is true the desk's own TIMER
  verbs are refused (`:64-66`).
- PlayMap handles the beat (`Mapper/src/PlayMap.Net/Links/StagewatchLink.cs:380-381`, cleared by a frame at `:420`).

### G09 — a window pass published by Patterns
- A page's mount key is `"web:" + the normalised address` (`Patterns/src/Patterns.Core/Media/InputKeys.cs:10-15`). That key is the Eye's
  source id `"source:" + key` and its words `"Key " + key` (`Patterns/src/Patterns.Core/Services/Eye.cs:553-556`). The context's items are
  the Eye's nodes (`Patterns/src/Patterns.Core/Services/ShowContext.cs:178-194`); the context is posted to Stagewatch every 5 s
  (`StagewatchLinkService.cs:389-415`) and answered to any wire peer (`docs/REMOTE.md:36-41`).
- STATE `web.url` is the page's current or configured address, the native player's `Origin` first (`CommandRouter.cs:294-306`, `:299`).
  Residency publishes the key (`CommandRouter.cs:704-706`, `Patterns/src/Patterns.App/Services/ResidencyService.cs:89-92`).
- The browser source logs the full address at open (`Patterns/src/Patterns.App/Services/WebFrameSource.cs:308`) **and on a navigate
  failure** (`:1402`, with `_status` carrying the exception's words at `:1401`), and returns it as `CurrentUrl` (`:749`, set at `:141`,
  `:256`, `:1396`).
- **OSC**: `/patterns/web/open <address> [page]` (`Patterns/src/Patterns.Core/Services/OscMap.cs:63`) keeps the raw line in
  `OscService._lastLine` (`Patterns/src/Patterns.Devices/OscService.cs:267`, `:272`), which its `StatusLine` shows (`:81`) and the Eye's
  OSC node carries (`Patterns/src/Patterns.App/Services/EyeService.cs:587`).
- **The native player** rewrites a YouTube or Vimeo page into the stream yt-dlp found, keeping the page's key and putting the page's
  address in `Origin` (`Patterns/src/Patterns.App/Services/WebVideoService.cs:58-75`, `MediaLocator.cs:126-127`); a failed open logs the
  stream address (`Patterns/src/Patterns.App/Services/VideoEngine.cs:331`).
- The address is stored in the show: `PatternConfig.WebUrl` (`Patterns/src/Patterns.Core/Model/PatternConfig.cs:459`, layer `:754`),
  `Web.SavedUrls` (`Patterns/src/Patterns.Core/Model/ShowState.cs:2821`), saved looks' captured JSON (the id-rewrite precedent at
  `Patterns/src/Patterns.Core/Services/ContentTargets.cs:71-77`). Entry points: the Media page (`ViewModels/MediaPage.cs:160-197`),
  `WEB OPEN` (`Services/WebActions.cs:45-51`), OSC, the cue editor (`ViewModels/CueEditor.cs:928`), the Library (`Services/LibraryItems.cs:100-110`).
- STATE is the greeting and every push to every wire peer, paired or not (`Patterns/src/Patterns.App/Services/ControlService.cs:357-363`,
  `:763-774`) — a documented choice (`docs/REMOTE.md:36-41`, PLAN §103.7), and spec-1-3 §8 lists it as Patterns' exemption.
- **Low-entropy passes ride query words.** Stagewatch's PIN can be a short string (the rig's default is `4321`,
  `Stagewatch/tools/Stagewatch.Rig/Program.cs:24`), the viewing code is free text (`Stagewatch/src/Timer.App/SettingsDialog.xaml.cs:596`),
  and both are accepted as `?pin=` and `?view=` (`Stagewatch/src/Timer.Remote/WebServer.cs:1058-1059`, `:1023-1029`). Any published
  value derived from the URL alone could be brute-forced back to them.
- **The twin and the files.** A standby copies the whole show and takes it over on the main's silence or TAKE OVER
  (`Patterns/src/Patterns.App/Services/TwinService.cs:11-20`); a standby launched on this machine runs with its own `--home`
  (`Patterns/src/Patterns.App/Services/TwinLauncher.cs:18`), so its own `SettingsStore.BaseDirectory`
  (`Patterns/src/Patterns.Core/Services/SettingsStore.cs:44`). The twin carries the show's secrets when `TwinConfig.SendSecrets` is on
  (`Patterns/src/Patterns.App/Services/TwinService.Main.cs:81`, `:89`; `Patterns/src/Patterns.Core/Services/Twin.cs:373-401`), which is
  the default and an open row (`docs/OPEN.md` L36). The support bundle carries `patterns.log` and `patterns.log.old` and masks known
  secret values (`Patterns/src/Patterns.Core/Services/RemoteAdmin.cs:76-87`, `:109-136`); a fence holds that every file-name constant
  beside the settings is in `Files` or in `NeverBundled` with its reason (`:91-98`). `backups/` is not bundled.
- Open ledger: `docs/OPEN.md` L26 asks for "a WebView2 probe in `--verify-runtime` in the same commit as the next web-path change".

### G47 — PlayMap imports a page it cannot draw, and keeps a pass
- `MEDIA IMPORT` of any http(s) URL that is not YouTube adds a Web item and answers `added <name>`
  (`Mapper/src/PlayMap.Engine/EngineCore.Content.cs:390-407`). With no name given, the name is the whole URL (`:393`).
- `MediaBackend.Open` returns null for Web and Embed (`Mapper/src/PlayMap.Engine/Host/MediaBackend.cs:144-145`), yet readiness says
  present and "a URL — read from the network when it plays" (`:381-382`, Decodable only for a Stream), MEDIA LIST says `ready:true`
  (`EngineCore.Content.cs:509`) and state `ready` (`:528`), and MEDIA FETCH says it "is rendered from its address" (`:447`).
- A refused or accepted line is journalled with `VerbCatalog.Redacted(line)` as its line column, and a refusal's own words as its text
  (`Mapper/src/PlayMap.Engine/EngineCore.Control.cs:171-175`). `Redacted` hides only a verb's `SecretFrom` words and refused capture
  URIs (`Mapper/src/PlayMap.Core/Wire/VerbCatalog.cs:609-646`). `MEDIA IMPORT` has no `SecretFrom`, and its catalogue row offers
  `<path|url>` to the assistant and MCP (`VerbCatalog.cs:185`).
- The desk's offline link files any `http…` location as Web too (`Mapper/src/PlayMap.App/Link/FakeEngineLink.cs:697`).

### G12 — PlayMap's timer source drops what its contract promises
- The parser keeps blackout, holding, holdingName, watermark, cue, style, urgency, flash, next, playout and show flags
  (`Mapper/src/PlayMap.Net/Links/StagewatchTimer.cs:87-113`); the watermark "must be shown whenever it is not empty" (`:55`).
- STATE's timer object carries only state, text, seconds, phase, color, title, message, trusted, why
  (`Mapper/src/PlayMap.Engine/Host/NetworkHost.cs:318-341`). `TimerView` draws time, colour, title and message on black
  (`Mapper/src/PlayMap.Engine/Rendering/TimerView.cs:99-117`, `:131-150`). So a Stagewatch **blackout** leaves a PlayMap timer surface
  showing the time, and the Free-mode licence mark (`Stagewatch/src/Timer.Core/FeatureTier.cs:63`, sent at `SnapshotJson.cs:100`) is
  never drawn.
- Readers of that object beyond the engine: the desk's `StateSnapshot.ReadLink` (`Mapper/src/PlayMap.App/State/StateSnapshot.cs:285-292`,
  `TimerState` at `:85`) and the assistant's `StateExplainer` (`Mapper/src/PlayMap.Assistant/StateExplainer.cs:263-275`).
- STATE fixture `tests/fixtures/state/main.json` holds `links.stagewatch` as `{"state":"off"}` only, so appended timer fields change no
  fixture.

### G25 — PlayMap's posted context has no byte budget
- The link posts `_endpoint.Context()` as built (`Mapper/src/PlayMap.Net/Links/StagewatchLink.cs:500-519`); a failure is debug-logged
  only (`:515-519`). `ShowContext.Write` caps nothing but `recent` to 20 (`Mapper/src/PlayMap.Core/Bus/ShowContext.cs:97-170`).
- **The only measured wall is bytes.** Stagewatch drops a declared body over 64 KiB before routing (`HttpRequest.ReadAsync` returns null at
  `Stagewatch/src/Timer.Remote/WebServer.cs:1613`) and the connection closes unanswered (`:320`); `/api/peers/context` refuses 0 or
  > 64 KiB (`:457`). Stagewatch's reader does not refuse on counts: it keeps the first 64 items (state ≤ 160 characters), the last 20
  recent (text ≤ 200), the first 64 commands (verb ≤ 80, risk) and the raw JSON (`Stagewatch/src/Timer.Core/ShowBus.cs:43`, `:60`, `:97-103`).
- The busy golden context is 43,640 bytes with 199 commands (max verb 307 characters, max words 403), 33 items, 25 links
  (`Mapper/tests/PlayMap.Engine.Tests/Fleet/Golden/context-busy.json`, measured). A journal row's text can be a whole redacted line, so
  20 long recent rows can pass 64 KiB. Mapper's `FakeStagewatch` takes any size (`Mapper/tests/PlayMap.Net.Tests/StagewatchLinkTests.cs:53-56`).

### G31 — a typed Stagewatch address is plain, without the desk, and "paired" by liveness
- `StagewatchOptions` borrows a hello only when one is live for the host at that moment (`Mapper/src/PlayMap.Engine/Host/NetworkHost.cs:718-736`).
  The link is built from settings right after the hello service starts (`:265-266`), and `OnPeer` re-picks only for an empty host
  (`:802-806`). So after every restart a typed address dials plain with `Approvals = false`.
- `paired` is `Status == Live` (`NetworkHost.cs:315`). A 401/403 at the upgrade reads "check the Stagewatch key"
  (`StagewatchLink.cs:256-259`), although Stagewatch answers a plain upgrade with 403 under Require HTTPS (`WebServer.cs:325-327`).
  Stagewatch serves TLS and plain on one port, by the first byte (`:300-317`).

### G13 — PlayMap's assistant never uses the desk
- `UseApprovalDeskAsync` sets the sink (`Mapper/src/PlayMap.Engine/Assistant/AssistantService.cs:141-157`); production calls it only with
  null (`:273`). The owner parked it (`Mapper/docs/round2/CARRYOVER.md:99`; `README.md:213`). `docs/UX.md:280` says the opposite.

### G36 — PlayMap's Patterns link is read-only
- `PatternsLink.SendAsync` exists (`Mapper/src/PlayMap.Net/Links/PatternsLink.cs:214-216`) and is used only by its own CONTEXT poll
  (`:279`). The clip clock is anchored on `seq` with a 250 ms re-sync window (`:252-270`) but drives nothing; `TimecodeKind` has no Patterns
  source (`Mapper/src/PlayMap.Core/Model/TimecodeModel.cs:5`).

### G35 — Patterns does not know PlayMap
- `KindWords` has no `playmap` (`Patterns/src/Patterns.Core/Services/ShowBusNetwork.cs:168-175`). Only the linked Stagewatch's context is
  read (`StagewatchFollow.cs:206-226`); a context keeps 64 commands (`ShowContext.cs:99`).
- Patterns' `LinesSession` sends lines as they are, no NONCE or AUTH-PROOF (`Patterns/src/Patterns.Core/Services/DeviceProfiles.cs:167-190`);
  a device's `Secret` is PJLink's (`DeviceProfiles.cs:35-43`). The show's facts (`BLACKOUT 1`, `LIVE 0`, `LOOK …`, the arrival's `OPEN 1`)
  go to every device that hears the show through the same `Encode` (`Patterns/src/Patterns.Devices/DeviceService.cs:769-790`,
  `:428-484`; `HearsShow` at `ShowState.cs:2542`).
- PlayMap's wire: a fact line is untagged and its first word one of `FactLines.Keys` (`BLACKOUT`, `LIVE`, `DUCK`, `FROZEN`, `REVIEW`, `LOOK`,
  `PROGRAM`, `STINGER`, `LOWERTHIRD`, `VOL`, `MUSICVOL`, `LOOKEDITED`, `ARMED`, `HOLD`, `CUE`, `DECK`, `STEP`; `BLACKOUT` only as 0/1); it
  is taken from a paired peer and never answered; a line with a request id is a verb (`Mapper/src/PlayMap.Net/Commands/CommandRunner.cs:247-271`,
  `Mapper/src/PlayMap.Net/Wire/WireSession.cs:221-226`, `Mapper/docs/WIRE.md:101-106`). `HELLO` makes a state listener (STATE at once or
  after `OK paired` on a strict node) and registers the session in the client table (`WireSession.cs:263-284`, `:144-156`, which needs
  `_saidHello`); `SUBSCRIBE OFF` stops every push (`:401-406`). A proof verifies against nonce + the hello name (`:37`, `:337`).
- spec-1-3 §7.2 makes a token node show its context only to a paired link, and its §8 lists PlayMap's open reads as an exemption "ended
  once Patterns' Lines devices pair by proof".
- Mapper's own papers say the opposite of the code: `docs/SHOWBUS-NODE.md:40` (it is Patterns' address on PlayMap's
  `NodeSettings.TrustedAddresses`, `Mapper/src/PlayMap.Core/Bus/Pairing.cs:115`) and `:72-73`. `Mapper/docs/FLEET.md:97-103` records
  PlayMap's three asks of Patterns (P1 kind, P2 read contexts, P3 the Video band).

### G21 — two speaker messages while following
- STAGE MESSAGE/CREW/CLEAR change only this desk's stage (`Patterns/src/Patterns.App/Services/ShowActions.Stage.cs:20-24`, a cue step's
  message included; `StageService.cs:107-134`). `Message` defaults to `flash = true` (`:107`); Patterns' `StageMessage` has text,
  channel, from, sent, ack and flash and **no urgency** (`Patterns/src/Patterns.Core/Model/ShowState.cs:845-868`).
- The pages draw this desk's pending message (`Patterns/src/Patterns.App/Services/ControlService.Stage.cs:81-85`); `pendingSpeaker` means
  "waiting for its ACK" in STATE (`StageService.cs:193`), in `/api/stage` (`:269`) and in the module (`stage_pending` feedback and
  variable, `Patterns/integrations/companion-module-patterns/src/feedbacks.js:227-232`, `src/state.js:308`). `DeckSignature` is keyed on
  the local pending ids (`StageService.cs:201-205`). Stagewatch's message reaches only `follows.message` in `/api/stage` (`:218`).
  `messageId`/`messageAck` are parsed (`StagewatchFollow.cs:59-60`); `messageAckBy` is not.
- Following needs no key since Round 87: the Authorization header is set only when a key is held (`StagewatchLinkService.cs:269`, `:361`).
  A 403 on the context post means a read key (`:408-409`).
- Stagewatch: a node key may run `message` and `clearmessage` (`Stagewatch/src/Timer.Core/ApiKeys.cs:85`, `WebServer.cs:797-808`). The
  message path is `message/<text ≤ 500>/<info|warn|crit>/<off|slow|fast>[/ack]` (`Stagewatch/src/Timer.Core/ShowState.cs:750-756`,
  `:1179-1191`). With `X-Request-Id` the answer is a receipt, 200 for any `ok` and 409 otherwise (`WebServer.cs:838-843`); when the show
  thread does not answer within `ReceiptWait` (3 s, `:851`) the receipt is `{ok:true, stage:"accepted"}` with no refs
  (`Stagewatch/src/Timer.Core/ShowState.cs:1199-1203`). An applied message with an ack names its signal in `refs.messageId`
  (`Stagewatch/src/Timer.App/ViewModels/MainViewModel.cs:709`), and a new message cancels the previous one's signal (`:707`). Frames carry
  `messageId`, `messageAck` and `messageAckBy` in the public frame (`SnapshotJson.cs:132-134`). `ack` is a public verb
  (`ShowState.cs:799-808`) under the crew-key policy (`WebServer.cs:1044-1056`).
- The link's beat task runs ping, context read and post, and the approval queue in one loop, each request on an `HttpClient` with a 4 s
  timeout (`StagewatchLinkService.cs:289`, `:328-355`).
- Stagewatch's rig does not simulate this: its receipts carry no refs (`Stagewatch/tools/Stagewatch.Rig/Program.cs:191`), `ack` falls to
  "the rig doesn't simulate" (`:217`), its frames set no message signal (`:271-290`), and it takes no key option (`:21-44`).

### G34 — the link is set on the Nodes page only
- `StagewatchLinkConfig` (Enabled, Host, ApiKey, FollowStage, Pins) (`ShowState.cs:2339-2370`) is reached from the Nodes page only
  (`Patterns/src/Patterns.App/Views/Sections/NodesSection.axaml:102-120`); `NODES` answers only STATUS/LIST
  (`Patterns/src/Patterns.Core/Services/ControlProtocol.cs:1319-1325`). Approved lines from the desk run with origin
  `(Tcp, "approval desk")` through the full router (`Patterns/src/Patterns.App/Services/AppServices.cs:682-686`).
- The key goes in a header to whatever target the link resolves (`StagewatchLinkService.cs:269`, `:225-243`). `ParseAddress` accepts a bare
  host name (`StagewatchFollow.cs:174-193`), so a heard node id such as `a1b2c3d4` also parses as a host.
- Stagewatch looks a key-shaped value up and refuses an unknown one with 401 (`WebServer.cs:1070-1083`); a value that is not key-shaped is
  a PIN, which passes when no PIN is set (`:867-870`). So a 2xx proves a key only when the value is key-shaped.

### G48 — VIDEO PAUSE/STOP refuse a playlist item and a stinger's clip
- `ownClock` refusals (`Patterns/src/Patterns.App/Services/ShowActions.Content.cs:481-505`; OPEN L48 at `docs/OPEN.md:38`). STATE already
  publishes `role`, `tag`, `key` (`CommandRouter.cs:237-252`). The playlist's item clock is the sequencer's
  (`Patterns/src/Patterns.Core/Services/PlaylistSequencer.cs:21-60`), wound by `EndCurrentIn`/`RestartCurrent`
  (`Patterns/src/Patterns.App/Services/PlaylistService.cs:165-175`). A stinger ends on its decoder's end and guards a stall only while
  playing (`Patterns/src/Patterns.App/Services/StingerService.cs:725-766`); `STINGER STOP` puts the show back (`docs/REMOTE.md:143`).

### G03, G33 — the context's commands
- Patterns lists 14 commands (`ShowContext.cs:157-173`), no `APPLY`, no VIDEO STOP, no staged verbs. Every proposal is `APPLY <id>: …`
  with risk review (`Patterns/src/Patterns.App/ViewModels/AssistantPage.cs:388`), which Stagewatch rates from the target's table
  (`Stagewatch/src/Timer.Core/Approvals.cs:102`, `:225-237`) as high.

### Mapper's round 3 and its files
- §4 says: "Every file has one owner in each wave (§2.2) … A folder not named for a wave keeps no owner in it, **and nothing in that wave
  edits it**" (`Mapper/docs/round3/PLAN.md:2122-2125`). `EngineCore.Content.cs` is round 2's, then desk-map's in W3 and canvas's in W4,
  with hunks "none" (`:2180`). §5.7's spine lane is for "a seam found missing mid-wave" (`:2346-2352`). `PlayMap.App/**` and
  `PlayMap.Assistant` are wave-owned (`:2175`; `Mapper/CLAUDE.md`).
- W1's packages are built (rhi, shaders, platform, desk-ux, installer; `Mapper/docs/round3/README.md`); W3 has not begun.
- The `fix-*` branches merged during round 3 (`fix-win-batch2..4`, `fix-nodehost-win`, `fix-fetchrow-win`, `fix-promote-win`,
  `fix-stagewatch-win`; `git log --merges`) fixed the Windows job's failures on round-2 files — stop and cancel semantics, not new
  behaviour. They show the owner grants branches on round-2 files; they do not license new refusals, STATE fields or a fitter.

---

## 1. Ranked by show risk

| tier | gap | what the room meets today | repo | lands |
|---|---|---|---|---|
| **A — safety, now** | G04 | A speaker on Patterns' `/stage` (and every timer node) reads a time that keeps counting from a stuck Stagewatch's last frame; nothing on the desk turns red | Patterns | Round 89.1, no dependency |
| **A** | G09 | A pasted window address (`/w/<token>`, or a page with `?pin=`/`?view=`) rides STATE (to unpaired wire peers), CONTEXT, the context posted to Stagewatch, the Eye, residency, OSC's status line, the log and the show file | Patterns | Round 89.2–89.3, no dependency |
| **A** | G47 | A pasted window address on PlayMap is kept in the project, the name, the reply and the journal (even when refused), and the item reads ready while the surface plays nothing | Mapper | on the owner's exception (PM-F1, PM-F2), else the integration pass |
| **A** | G12 (blackout, watermark) | Stagewatch blacks out; the PlayMap surface drawing its timer keeps showing the time. A Free-mode Stagewatch's licence mark is missing on PlayMap's outputs | Mapper | on the owner's exception (PM-F3) |
| **B — silent failures, receipts** | G25 | One long journal line pushes PlayMap's context past 64 KiB: Stagewatch closes the socket, PlayMap stays "following Stagewatch", its things go stale on Stagewatch's Show network | Mapper (+ a Stagewatch 413) | on the owner's exception (PM-F4); SW-25 now |
| **B** | G48 | `patterns pause` on a playlist item is refused by Patterns while Stagewatch journals it done (Stagewatch's half: playout-links); Patterns cannot hold a playlist item at all | Patterns | Round 91 |
| **B** | G21 | Stagewatch's message to the speaker never reaches a speaker reading Patterns' `/stage`; Patterns' own message never reaches Stagewatch's displays | Patterns | Round 91 |
| **B** | G31 | After a restart, approved lines for a typed-address PlayMap go undelivered after 60 s; Require HTTPS reads as "check the key" | Mapper | post round 3 (with peers-not-screens PM-1) |
| **C — features, reach** | G35 | PlayMap is a grey dot on Patterns' Eye; Patterns drives it only with no token or a trusted address; PlayMap's 1.3 open-read exemption cannot end until Patterns pairs by proof | Patterns (+ Mapper docs now) | Round 92; Mapper docs fix now |
| **C** | G03, G33 | Every Patterns proposal reads "CHANGES THE ROOM" and is never pre-ticked; VIDEO STOP and staged verbs are not advertised (over-rating: the safe direction) | Patterns | Round 90 (cross-app units) |
| **C** | G34 | The link cannot be set from a deck, the wire, a menu or the assistant | Patterns | Round 91 |
| **D — opportunities** | G13 | PlayMap's assistant keeps its proposals on its own list | Mapper | post round 3, owner's decision 7 |
| **D** | G36 | PlayMap cannot chase Patterns' clip or send it a verb | Mapper | post round 3 |

Tier A goes first because each item is a wrong picture or a published pass, each is one repo's, and none waits on another repo. For
Mapper, "first" means the first thing asked of the owner, not a licence to edit wave-protected files (§2 decision 6).

---

## 2. Decisions

1. **One order by show risk, the safety units first with no dependency.** Patterns Round 89 holds G04 and G09 only. Mapper's tier-A and
   G25 work is put to the owner as an explicit exception request (decision 6); everything else waits for the Stagewatch release it needs or
   for round 3's integration pass. *Why:* the room is unsafe today in four ways that need no contract change. *Alternative rejected:* one
   interop round per repo, which would hold the safety fixes behind Stagewatch 2.18 and round 3.

2. **G04: the stuck beat sets a flag only the receive loop writes; the `Sight` getter folds it into the frame.** `StagewatchLinkService`
   gains `volatile bool _stuck`: the receive loop sets it on `{"type":"beat","live":false}` and clears it on the next state frame; connect
   and `Reconcile` clear it. The context moves out of the sight into its own field, `volatile ShowContext? _context`, written by the beat
   task alone; the getter composes `_sight with { Context = _context }`, and, while `_stuck` and a frame is held, `Frame = Frame with
   { Live = false }` and `Stuck = true`. So each field has one writer, and a context landing beside a stuck beat can no longer restore the
   live frame (or an older frame). A plain beat changes nothing. *Why:* the link is genuinely alive (pongs answered off Stagewatch's show
   thread), so liveness is the wrong signal; the existing pure readers already turn `Live=false` into "no time"; Stagewatch always sends a
   fresh frame before its next plain beat after a stall (`WebServer.cs:1269`). *Alternatives rejected:* writing `Live=false` into
   `_sight` (the beat task's read-modify-write undoes it, `StagewatchLinkService.cs:377`); withholding the last-word refresh (pongs refresh
   it anyway); dropping the link (loses the context, the desk and the clock for a stall Stagewatch reports itself).

3. **G04: a stuck Stagewatch that never sent a frame changes nothing on the stage.** `StagewatchSight` gains `bool Stuck { get; init; }`
   (not positional, so its constructor and every caller stay). With no frame held, the getter sets only `Stuck`; `Following` stays false
   (`StageService.cs:61`), so the desk's own timer runs and its TIMER verbs stay allowed, as they are today before a first frame. The words
   say "Stagewatch Hall A says its show is stuck — this desk's own stage timer runs until its first frame".

4. **G04 reaches every reader in the same unit.** The stage pages and `/api/stage` (through `StageFollow`), STATE `bus.stagewatch`
   (`text` empty, `light` red, `stuck` appended), the twin relay (the node repaints when `Frame.Live` moves), the Eye (the linked Stagewatch
   red, "its show is stuck"), **the Nodes page card** (`ShowAppCard.Of`: hue `#B03030`, line "linked · its show is stuck — no time shown"),
   the context's peers row and the assistant's brief (red, the same words), the Companion module (`stagewatch_stuck` variable, a red
   feedback), and one log line per transition — guaranteed by the single writer of `_stuck`.

5. **G09: the pass is never a key, and the kept id is random.** Adopt windows-for-peers decisions 5 and 6 now, without its grants: a private
   address typed or loaded anywhere becomes `kept://<host[:port]>/<id>`, where `<id>` is 12 base32 characters (60 bits) **drawn from
   `RandomNumberGenerator`** when the address is first kept and stored with it in the book. The same normalised URL kept again reuses its
   book entry by lookup (so two pastes share one mount and one browser, as `InputKeys.cs:10` intends). Nothing published is a function of
   the URL, so a short PIN or viewing code cannot be brute-forced from the id. The real URL lives only in `patterns.webpasses.json` in
   `SettingsStore.BaseDirectory` and in the browser. *Why:* the mount key feeds at least ten readers (Eye ids and words, the context and its
   post to Stagewatch, STATE `web` and residency, OSC's status line, the log, the show file, looks' JSON, the native player); fixing the key
   fixes them all and the next reader too; a fence test builds every published artefact and asserts no pass. *Alternatives rejected:*
   redacting each reader (the next reader leaks); a hash of the URL (brute-forceable for `?pin=4321`); an HMAC under a per-machine secret
   (works, but needs a second secret file and gains nothing over a random id the book already stores).
   **Private** = a `/w/` path plus 26 of Stagewatch's window alphabet; a query or fragment word in `win, pin, token, key, view, code,
   secret, password, pass, apikey, api_key, access_token, auth, sig, signature`; or user information in the authority.

6. **Mapper: the tier-A fixes are an exception request, not the rule's allowance.** §4 says a file a wave does not name "keeps no owner in
   it, and nothing in that wave edits it" (`Mapper/docs/round3/PLAN.md:2124-2125`), and §5.7's lane is for a missing seam (`:2346-2352`),
   not a behaviour change. So PM-F1 to PM-F4 are put to the owner as one request: four `fix-*` branches on round-2 files (`Net/Links/**`,
   `Engine/Host/{NetworkHost,MediaBackend}.cs`, `Engine/Rendering/TimerView.cs`, a new `Core/Media/PrivateAddress.cs`), one change to
   `VerbCatalog.Redacted` outside the blocks, and `EngineCore.Content.cs` before W3 begins (its §4 row has no hunk slot, `:2180`, and its
   G47 change touches four sites — import, FETCH, LIST twice — so it is not a ten-line hunk). The request quotes §4 in full and names what
   each fix protects. If the owner declines, PM-F1 to PM-F4 land with the integration pass, Patterns' own rule (89.2) protects the Patterns
   side, and Mapper's papers say "a pasted page is not drawn; do not paste a window address" until then. *Why:* the critic is right that the
   `fix-*-win` precedent was Windows-job stability, not new refusals and STATE fields.

7. **G09's remainder — STATE to unpaired peers — closes in Round 90 with spec-1-3's "reads follow pairing" unit**, not in 89. *Why:* after 89
   nothing secret rides STATE; closing the open read changes every reader, which already answer NONCE
   (`Patterns/integrations/companion-module-patterns/src/main.js:216-218`, `Stagewatch/src/Timer.Integrations/PatternsLink.cs:311-314`),
   and it belongs with the 1.3 copy of the contract. Its existing tests that read `STATE {` as the first line on a token desk with
   `TrustLoopback=false` (`Patterns/tests/Patterns.App.Tests/RemoteTrustTests.cs:71-78`, `ShowBusAppTests`) change in the same unit.

8. **G09 and the twin: the kept book travels with the show's secrets.** A new twin word `KEPT` (main → standby; appended to `TwinWord`)
   carries the whole book (≤ 64 entries) on welcome and whenever it changes, only when `TwinConfig.SendSecrets` is on — the same switch, and
   the same plain-TCP exposure, as the devices' passwords (L36). The standby merges it into its own book in its own `BaseDirectory`, so a
   same-machine standby with `--home` and another machine's standby both resolve kept pages when they take the show. With `SendSecrets` off,
   the standby takes the show with those pages black and words that say why; that is a new OPEN row and a §22 walk row. *Why:* a takeover
   that blacks out every keyed page is a show fault; the twin already carries secrets by this switch. *Alternative rejected:* "never
   twinned" (the first draft), which broke failover silently.

9. **G47: refuse, never echo, never keep — and scrub what is already kept.** `MEDIA IMPORT` of a page or an embed is refused with the reason
   and a way out; a private address is refused without echoing it; https `.m3u8`/`.mpd` is filed as Stream (its decode over https by the
   shipped LGPL FFmpeg is unproven: readiness stays the existing Stream rule, and the papers say so); existing Web/Embed items read
   `ready:false`, `unplayable`. `Redacted` shows any word that is a private address as `‹private address›`. At engine start a Web/Embed
   item whose location is private is renamed `<host> ‹private address›` and its location cleared, by one journalled edit. *Why:* the
   refusal row itself would otherwise journal the pass (`EngineCore.Control.cs:172-175`); an item already holding a pass publishes it in
   MEDIA LIST, STATE and the context's names, which `Redacted` (lines only) does not reach. The catalogue row's `<path|url>` words, and the
   desk's `FakeEngineLink.cs:697` filing, go to the integration pass as notes (spine block and App files are not PM-F's).

10. **G12 splits by risk.** Now (on the exception): `blackout` and `watermark` appended to STATE's timer object; `TimerView` draws black under
    a blackout and the watermark always when non-empty (in blackout, unknown and stuck too, from the last frame that carried one). Later,
    with native windows (windows-for-peers PM-W2/W3): holding, flash, urgency, next, `show{}`, time of day. The desk's `StateSnapshot` and
    the assistant's `StateExplainer` take `blackout` (and the later fields) at the integration pass, as notes: both are wave-owned files.

11. **G25: fit by bytes only, at the poster.** A pure `ContextFit.ForPost(json, 65_536)` in `Net/Links` leaves a context at or under
    65,536 bytes byte-identical. Over it, in order: (1) each `recent` text cut to 200 characters — what Stagewatch's reader keeps anyway
    (`ShowBus.cs:99`), so nothing it reads is lost; (2) command `words` dropped from the last command back; (3) green, then grey items
    dropped from the last back; if still over, nothing is posted. Anything trimmed adds one grey `context:trimmed` item and words on the
    link. No count limit is imposed: none is measured as a refusal (Stagewatch truncates, `ShowBus.cs:97-103`), so the per-count numbers
    of 1.3 stay the integration pass's, in `ShowContext.ToJson`. Stagewatch answers 413 for an oversize body instead of closing (SW-25).
    *Why:* "measured, then governed" — the 64 KiB refusal is the only measured wall.

12. **G21: while following, the speaker channel goes through Stagewatch when the link can carry it; otherwise it stays on this desk and
    says so.** The route rule, read at the moment of the verb:
    - Following, the link Live, a key held that is not known to be a read key, channel `speaker` → **via Stagewatch**: the message is kept
      on this desk as a `StageMessage` with `Via = stagewatch` and the result is `Requested`.
    - Otherwise (no key, a read key known from a 403, the link stale; or channel `crew`) → **this desk's displays**, as today, `Done`, with
      words that say Stagewatch's displays do not show it and why. A cue step's message therefore always lands somewhere, and the operator
      is never told to turn the follow off.
    The request is `POST /api/message/<text>/info/<slow|off>/ack` — `slow` when the message flashes (`Message`'s `flash`, default true),
    `info` because Patterns' messages carry no urgency (`ShowState.cs:845-868`). The receipt is read with its stage: `applied` with
    `refs.messageId` binds the message to Stagewatch's id; `accepted` (no refs) is journalled "accepted by Stagewatch Hall A, not yet
    applied — its show thread is slow" and binds when a frame first shows a new `messageId` with that text; a 409, 401, 403 or a timeout
    turns the message to this desk's displays and journals why (a 403 marks the key read-only for the link's life). STAGE CLEAR posts
    `clearmessage` only when the frame's `messageId` is one this desk posted; otherwise it clears this desk's messages and says
    "Stagewatch Hall A's own message stays — clear it there". *Why:* one room, one message where it can be had, and never a refusal or a
    false "on screen" when it cannot. *Alternative rejected:* the first draft's refusal (it stopped cue stacks mid-show and offered a way out
    that also dropped the timer).

13. **G21: the pages draw one speaker message, latest wins; `pendingSpeaker` keeps its meaning.** While following, the speaker box shows
    whichever arrived later: Stagewatch's current message (its arrival tracked by the link when `messageId` or the text moves) or a
    message on this desk's displays only. `pendingSpeaker` stays "waiting for its ACK": a via-Stagewatch message is pending until the frame's
    `messageAck` for its id reads `seen` or `done`, then it is marked seen and journalled; Stagewatch's own messages are never this desk's
    pending. STATE `stage` appends `speakerFrom`, `speakerAck`, `speakerAckBy`; `DeckSignature` adds the frame's `MessageId` and
    `MessageAck`; the module gains `stage_speaker_from`, `stage_speaker_ack` and a feedback, so the deck shows the ACK it now has.

14. **G21: the speaker's messages ride their own lane.** A `SpeakerLane` task per connection posts through the link's `HttpClient` (thread
    safe; its handler carries the pin), one request in flight, latest wins per channel (a newer message supersedes an unsent one, which is
    journalled "superseded"); results are posted to the UI thread. *Why:* the beat task runs ping, context and approvals one after another
    with a 4 s timeout each (`StagewatchLinkService.cs:289`, `:328-355`), so a message behind it could wait seconds.

15. **G34: the wire sets the link, never the key, and an unbound key goes nowhere a line chose.** `STAGEWATCH LINK …`, `STAGEWATCH FOLLOW …`,
    `STAGEWATCH ACCEPT <fingerprint prefix>` and the query. `StagewatchLinkConfig.KeyFor` is `node:<id>`, `addr:<host>:<port>`, `none`, or
    `""` (unbound). It is set when the key is typed on the Nodes page, to the target the page shows; a key saved before Round 91 is treated
    as typed for the configured host at load. An unbound key (AUTO, not yet answered) binds at the first `2xx` to `POST /api/peers/context`
    — node scope — and only when the key is key-shaped (`swk_…`; a PIN-shaped value proves nothing, `WebServer.cs:867-870`). A
    `STAGEWATCH LINK` from any line first turns an unbound key to `none`, so the key never goes to a line-chosen target. An `addr:` binding
    re-binds to `node:<id>` only over TLS with that node's pin held and its hello naming the address (a DHCP move then keeps the key);
    on a plain link the Nodes page offers "Bind the key to Hall A" instead. The heard-id form is `id:<id>` (a bare word is a host,
    `StagewatchFollow.cs:192`). ACCEPT's prefix (≥ 8 hex) is a typing check that refuses a stale press once the waiting certificate has
    changed again — not a trust proof; the trust is the operator's who compared fingerprints. Risk: LINK, FOLLOW and ACCEPT **high**
    (FOLLOW OFF changes what the speaker sees). Refused from the approval-desk origin. Not listed in the context's commands.

16. **G34: Patterns' own assistant does not propose these lines.** Its proposals are typed show content (looks, cues, lower thirds,
    patterns, overlays, screens, brand, show plans; `AssistantPage.cs:380-395`), and a line that repoints a key-bearing link or the speaker's
    timer source is a trust decision, as a certificate is. The brief names the verbs in words ("the operator may STAGEWATCH FOLLOW OFF on a
    deck or the Eye's menu") so the assistant can suggest them; the gap's "assistant-proposable line" is declined, with this reason, in the
    papers.

17. **G48: Patterns cures L48.** PAUSE holds a playlist item's clock and a stinger's clip; PLAY releases; STOP on a playlist item restarts its
    clock and holds it at the top; STOP on a stinger's clip stays refused, pointing at `STINGER STOP`. STATE `video` appends `pausable` and
    `stoppable`, which playout-links' Stagewatch link reads in place of its role rule. A schedule entry that falls due during a hold fires at
    the release if it is still inside its window, else is journalled "held past its time".

18. **G03, G33: adopt cross-app-proposals' 2.8 rows; read 256 commands and 512 characters.** The 15 appended rows (APPLY review, VIDEO STOP high,
    the staged verbs review, TAKE/CUT and web actions high), `WEB OPEN` still unlisted. Patterns' context reader keeps 256 commands, drops a
    command whose verb is over 512 characters, and cuts words at 512 (PlayMap's busy golden: verb ≤ 307, words ≤ 403, so all 199 commands and
    their words are kept). The brief's list grows from 12 to 32 (windows-for-peers decision 10).

19. **G35: one proof-paired device link per PlayMap node carries its verbs, its facts and its context.** A new device profile, *Show Bus node*:
    on `NONCE` it says `HELLO Patterns <desk name>` and then `AUTH-PROOF` over nonce + that name (so PlayMap's client table lists the desk),
    then `SUBSCRIBE OFF` (no STATE pushes; a STATE line that still arrives is dropped, counted, never raised) and `SUBSCRIBE CONTEXT`. Commands
    are tagged `@d<n>` for their own receipts. **The show's facts go untagged** through a new `ProfileSession.EncodeFact` (default: `Encode`),
    and this profile sends only facts whose first word is one of PlayMap's fact keys (`CommandRunner.cs:253-257`; `OPEN 1` is not one and is
    not sent), so PlayMap follows Patterns' blackout over this link when `FOLLOW-BLACKOUT` is on and no fact becomes an ERR. A wrong token
    stops the device sending. Kind words `playmap` → `PlayMap`; the pushed contexts are the PlayMap app's context on the Eye, the Nodes page
    and the brief. *Why:* from 1.3 a token node shows its context only to a paired link (spec-1-3 §7.2), and this profile ends PlayMap's §8
    exemption. *Alternatives rejected:* tagging every line (facts become refused verbs, `WIRE.md:105-106`); no HELLO (PlayMap's STATE never
    lists the desk, `WireSession.cs:153`); turning `HearsShow` off (loses blackout following over the one link).

20. **G31 (after round 3, with peers-not-screens PM-1): re-resolve, try TLS first, probe the desk, blame the key only after TLS.**

21. **G13 adopts cross-app-proposals decision 14** on the owner's answer to round 2 decision 7; post round 3.

22. **G36: the smallest honest version** — a `PATTERNS` timecode source accurate to STATE cadence, and `LINK PATTERNS SEND` with Patterns'
    receipt; frame accuracy waits for `video.anchor`. Post round 3; the owner may decline it.

23. **Round numbers are the maintainer's; the order is fixed here.** §9 maps the sibling designs' guessed numbers.

---

## 3. Wire (exact shapes)

### 3.1 Patterns

**Round 89.1 — the stuck beat (G04).** Nothing new is sent. Read from Stagewatch: `{"type":"beat","live":false}` (today's bytes).
STATE `bus.stagewatch` (fields appended at the end of the row, in this order across rounds: `stuck` (89), `key` (90, peers-not-screens),
`follow` and `pending` (91), `windows` (92, windows-for-peers)):
```json
"stagewatch":{"on":true,"state":"live","name":"Hall A","following":true,"title":"Keynote — Ana","text":"","light":"red",
 "headline":"Keynote — 12:30 left","clock":"","words":"following Stagewatch Hall A — it says its show is stuck; the time is not shown","stuck":true}
```
With no frame yet: `"following":false`, `"light":"red"`, `"words":"Stagewatch Hall A says its show is stuck — this desk's own stage timer
runs until its first frame"`, `"stuck":true`. `/api/stage` `follows` appends `"stuck":true`. The twin relay keeps its shape (`frame.live`
already rides). Companion: variable `stagewatch_stuck` = `yes`|`no`; feedback `stagewatch_stuck` (red). Log lines, one per transition:
`Stagewatch link: Hall A says its show is stuck — the stage shows no time.` / `Stagewatch link: Hall A's show runs again.`

**Round 89.2–89.3 — held addresses (G09).** No new verb. Shapes:
```
STATE web:            {"page":"10.0.0.5 (private address, kept)","url":"kept://10.0.0.5:9924/q3x7m2k9p4ab", …}
STATE residency.holds[].key:  "web:kept://10.0.0.5:9924/q3x7m2k9p4ab"
Eye / CONTEXT item id:         "source:web:kept://10.0.0.5:9924/q3x7m2k9p4ab"
WEB OPEN http://10.0.0.5:9924/w/<26> ON lobby
   -> OK "lobby → 10.0.0.5 (private address, kept on this machine) (the pattern keeps its own address; a look recall brings it back)."
OSC status line:      "/patterns/web/open → WEB OPEN http://10.0.0.5:9924 (private address) → OK …"
patterns.webpasses.json (SettingsStore.BaseDirectory, ≤ 64 entries; NeverBundled "private page addresses and their passes"):
   {"v":1,"passes":[{"id":"q3x7m2k9p4ab","url":"http://10.0.0.5:9924/w/…","keptUtc":"2026-10-25T09:12:00Z"}]}
   the id: 12 of base32 (a–z, 2–7) from RandomNumberGenerator; the same URL kept again reuses its entry
twin (main → standby, only with TwinConfig.SendSecrets on):
   KEPT {"v":1,"passes":[…]}      on welcome and on every change; the standby merges into its own book
another machine, or a standby without the book:
                      the page draws black; web.page "10.0.0.5 (private address, kept on another machine)"
load migration:       one journal row "kept 2 private page addresses on this machine (Stagewatch windows and keyed links); older
                      copies in backups/ and the log keep them on this machine — the support bundle hides them"
support bundle:       every text entry passes WindowAddresses.RedactText (a private URL → "http://10.0.0.5:9924 (private address)"),
                      and the kept URLs join the secret values Secrets.Scrub masks
```
Any other `CurrentUrl` a page navigates to is published as `scheme://host[:port]` when private.

**Round 90 (contract 1.3 client side).** As the siblings define them: the node link `/ws?node=<id>&t=patterns` and the `auth` verdict,
STATE `bus.stagewatch.key` (peers-not-screens §2.1, §2.6); on a token desk a non-loopback unpaired wire connection hears `NONCE` first
and no STATE until `OK paired`, and STATE pushes skip unpaired peers (spec-1-3 §7.2 and its Patterns table); the context's 15 appended
rows (cross-app-proposals §2.8). Patterns' context reader keeps ≤ 256 commands (a verb over 512 characters drops its command; words cut
at 512).

**Round 91.1 — the link's verbs (G34).**
```
STAGEWATCH | STAGEWATCH STATUS        -> OK {"on":true,"state":"live","name":"Hall A","address":"10.0.0.6:9924","follow":true,
                                            "tls":"pinned 3F:A2:…","keyFor":"Hall A (a1b2c3d4)","pending":""}
STAGEWATCH LINK AUTO                  -> OK "Stagewatch link on — following the first Stagewatch heard on the show network; the key stays
                                            unbound until the Nodes page binds it."   (an unbound key turns to none first)
STAGEWATCH LINK <host[:port]>         -> OK "Stagewatch link → 10.0.0.9:9924 — dialling without the key (it is bound to Hall A); type one for
                                            10.0.0.9 on the Nodes page."
STAGEWATCH LINK id:<heard id>         -> OK "Stagewatch link → Hall A (a1b2c3d4) at 10.0.0.6:9924 — with its key."
STAGEWATCH LINK ON | OFF              -> OK "Stagewatch link on (…)" | "Stagewatch link off — this desk's own stage timer runs."
STAGEWATCH FOLLOW ON | OFF            -> OK "The stage follows Stagewatch Hall A's timer." | "The stage runs this desk's own timer."
STAGEWATCH ACCEPT <≥ 8 hex of the pending fingerprint>
                                      -> OK "Accepted Stagewatch Hall A's new certificate 3F:A2:… — the link dials again."
```
Refusals: `'10.0.0.9/x' is not an address — host:port, id:<a heard Stagewatch's id>, AUTO, ON or OFF` · `No Stagewatch with id a1b2c3d4
is heard on the show network` · `The link already follows 10.0.0.9:9924` · `The Stagewatch link is this desk's operator's to change — not an
approval desk's` (origin `(Tcp, "approval desk")`) · `No changed certificate is waiting` · `The waiting certificate is 3F:A2:9C:01… — not
11:22:33:44; compare it with Stagewatch's /tls page` · `STAGEWATCH KEY …` → `The Stagewatch key is typed on the Nodes page only — it never
rides a line`. Risk (`ControlProtocol` and the menu): LINK, FOLLOW and ACCEPT high; none is listed in the context's commands. Journal row
on each: kind `stagewatch`, target the address, origin as usual. STATE `bus.stagewatch` appends `follow` (the setting) and `pending` (the
short fingerprint or `""`). Module: actions `stagewatch_link` (auto / on / off / address / id), `stagewatch_follow` (on / off),
`stagewatch_accept` (fingerprint field); variables `stagewatch_follow`, `stagewatch_pending`; `test/lines.txt` gains the seven lines.

**Round 91.2 — the speaker's message through Stagewatch (G21).** Patterns → Stagewatch, on the speaker lane (HTTP on the link's scheme,
host, port and pin; the link's `HttpClient`):
```
POST /api/message/<text as one percent-encoded segment, ≤ 500 chars>/info/<slow|off>/ack
POST /api/clearmessage                                          (only when the frame's messageId is one this desk posted)
POST /api/ack/<messageId>/seen/<this desk's name, ≤ 40>         (a page's ACK on Stagewatch's message)
Authorization: Bearer <the link's key>     X-Request-Id: pt<n>   (1–32 of [A-Za-z0-9_-])
← 200 {"type":"receipt","id":"pt12","ok":true,"stage":"applied","effect":"…","error":null,"refs":{"messageId":41}}
← 200 {"type":"receipt","id":"pt12","ok":true,"stage":"accepted","effect":"","error":null}       (show thread slow: no refs)
← 409 {"type":"receipt","id":"pt12","ok":false,"stage":"applied","effect":"","error":"…"}
← 401/403 {"ok":false,"error":"…"}
```
Patterns' answers to `STAGE MESSAGE Wrap up`:
- via Stagewatch: `OK requested: to Stagewatch Hall A's speaker: 'Wrap up' — its receipt and the speaker's ACK come back here` (Requested);
- this desk only: `OK To the speaker: 'Wrap up' — on this desk's displays only; Stagewatch Hall A's displays do not show it (the link holds
  no node key | the key may read but not send messages | Stagewatch Hall A is quiet)` (Done).
Journal rows: `stage message → Stagewatch Hall A: on the speaker's screen (message 41)` · `stage message → Stagewatch Hall A: accepted, not
yet applied — its show thread is slow` · `stage message → Stagewatch Hall A refused it (<error>) — shown on this desk's displays only` ·
`stage message superseded before it was sent` · `stage message 41 seen by Ana (10.0.0.30) at 10:42:07` · `STAGE CLEAR: Stagewatch Hall A's
own message stays — clear it there`.
STATE `stage` appends `speakerFrom` (`stagewatch`|`desk`|`""`), `speakerAck` (`""|requested|accepted|sent|seen|done|refused`),
`speakerAckBy`. `pendingSpeaker` keeps its meaning (this desk's message waiting for its ACK). `/api/stage` appends `speakerShown`
(`{"id":"sw:41"|"<local id>","text","flash","from":"stagewatch"|"desk"}`, the one the page draws) and `follows` appends `messageId`,
`messageAck`, `messageAckBy`. The pages draw `speakerShown`; their ACK posts its id (`sw:<id>` is forwarded). Module: variables
`stage_speaker_from`, `stage_speaker_ack`; feedback `stage_speaker_ack_is` (`requested|accepted|sent|seen|done|refused`). Context row words:
`STAGE MESSAGE <words>` — "words to the speaker's display (through a linked Stagewatch while the stage follows it and the link holds a node
key), with a receipt".

**Round 91.3 — the playlist and stinger hold (G48).** STATE `video` appends `pausable` and `stoppable` (booleans) after `seq`: playlist item
`true,true`; stinger's clip `true,false`; a live source or an unopened decoder `false,false`. Replies: `VIDEO PAUSE` on a playlist item →
`OK 'opener.mp4' paused at 1:02 — the playlist holds with it; VIDEO PLAY runs both on`; `VIDEO STOP` on a stinger's clip →
`ERR 'sting.mov' is a stinger's clip — STINGER STOP puts the show back`.

**Round 92 — PlayMap as a device and an app (G35).** Device profile `ShowBusNode` (appended to `DeviceProfile`), on its TCP link:
```
← NONCE 3f9a0c…                                   (a token node, from 1.3 also an untrusted address)
→ HELLO Patterns FOH                              ← OK
→ @dp AUTH-PROOF <hex(HMAC-SHA256(normalised Secret, nonce + "Patterns FOH"))>
← OK @dp paired | OK @dp open — this node asks for no token | ERR @dp wrong token — …
← STATE {…}                                       (the greeting: dropped and counted, never raised)
→ @do SUBSCRIBE OFF                               ← OK @do pushes off
→ @dc SUBSCRIBE CONTEXT                           ← OK @dc subscribed context   ← CONTEXT {…}  (on every change)
→ @d17 PLAYLIST GO show #3                        ← OK @d17 … | ERR @d17 …   (each command's own receipt)
→ BLACKOUT 1 | LIVE 0 | LOOK Keynote | CUE 01.020   (the show's facts: untagged, only PlayMap's fact keys; never answered)
```
A wrong token stops the device sending (no retry loop toward PlayMap's five-wrong close) with the words `wrong pairing token — the device's
Secret is the node's pairing token (PlayMap: its Network drawer)`. Kind words `playmap` → `PlayMap`; the context's peers row `t:"playmap"`.

### 3.2 PlayMap

**`fix-stagewatch-timer-air` (G12, air half; on the exception).** STATE `links.stagewatch.timer` appends, after `why`:
`"blackout":false,"watermark":"StageWatch by Ben Rohleder"`. The later half appends, in this order: `next, urgency, flash, flashTimer,
holding, holdingName, holdingFill, timeOfDay, show`.

**`fix-media-page-import` (G47; on the exception).**
```
MEDIA IMPORT https://10.0.0.6:9924/w/k3m9…        -> ERR that address carries a pass (a Stagewatch window's, or a key in its query) — PlayMap
                                                      keeps no such address: draw Stagewatch's timer with SURFACE SOURCE <surface> timer:stagewatch,
                                                      or send the page as NDI from a browser
MEDIA IMPORT https://example.com/menu             -> ERR a web page needs a browser source, which PlayMap has not built (docs/ROADMAP.md L10) —
                                                      send it as NDI, OMT or Spout from a browser and import that
MEDIA IMPORT https://youtu.be/abc                 -> ERR an embedded player needs a browser source, which PlayMap has not built (docs/ROADMAP.md L10) — …
MEDIA IMPORT https://cdn.example.com/live.m3u8    -> OK added live.m3u8   (kind stream; decode over https unproven — the papers say so)
journal / reply / proposal of any line holding a private address: the word shown as ‹private address›
engine start, an existing Web/Embed item with a private location: renamed "10.0.0.6 ‹private address›", location cleared; one journal row
   "2 page items held a pass — renamed and their addresses cleared (PlayMap keeps no such address)"
MEDIA LIST (an existing web item): {"kind":"web","present":true,"ready":false,"words":"a web page — PlayMap has no browser source (ROADMAP L10)","state":"unplayable",…}
MEDIA FETCH <that item>                           -> ERR menu is a web page PlayMap cannot draw — never fetched
```

**`fix-stagewatch-context-size` (G25; on the exception).** No new field. The posted body is at most 65,536 bytes. When anything was
trimmed it carries one item `{"id":"context:trimmed","kind":"health","label":"Context trimmed","light":"grey","state":"recent cut to 200
characters, 120 command words and 9 green items left out to fit Stagewatch's 64 KB"}` and `links.stagewatch.detail` adds ` · context
trimmed for Stagewatch (…)`. When it cannot fit: nothing is posted and the detail reads `this node's context is 71 KB after trimming — over
Stagewatch's 64 KB; not posted`. A 413 from Stagewatch reads `Stagewatch refused the context as too large (413)`.

**Post round 3 — G31 (with peers-not-screens PM-1).** No new verb. Journal rows: `Stagewatch at 10.0.0.6:9924 heard as Hall A (a1b2c3d4):
the link dials it again over TLS, as its approval desk` · `Stagewatch at 10.0.0.6:9924 offers no TLS — the link is plain`. Words:
`Stagewatch at … answered without its pinned certificate — refused` · `refused over plain ws (403): trying wss — Stagewatch's Require HTTPS
answers a plain upgrade with 403` · only then `refused — check the Stagewatch key`. The desk probe: `GET /api/proposals?target=<id>&state=approved`
once a minute while `Approvals` is false and a key is set; a 2xx turns collection on.

**Post round 3 — G13:** cross-app-proposals §2.9 unchanged (`ASSISTANT SET desk auto|off`, STATE `assistant.desk`).

**Post round 3 — G36:**
```
TIMECODE SOURCE ADD <name> PATTERNS     -> a source whose code is the linked Patterns desk's clip on air (positionMs anchored on video.seq);
                                           "accurate to Patterns' pushes (≤ 1 s) and the link's 250 ms re-sync — not frame-accurate"
LINK PATTERNS SEND <line…>              -> OK Patterns FOH: <Patterns' OK words> | ERR Patterns FOH refused <verb>: <words>
                                           | ERR no Patterns desk linked | ERR not paired with Patterns FOH — LINK PATTERNS with its pairing token
```
`SEND` is high risk, refuses a first word of `AUTH`, `AUTH-PROOF`, `NONCE`, `HELLO` or `SUBSCRIBE`, and its journal row is `Redacted`.

### 3.3 Stagewatch (small, this area's)
- **413.** A request whose declared `Content-Length` is over 65,536 on a path that is **not** a deferred upload (`/api/holding/upload`,
  `/api/slides/script` keep their own caps, `WebServer.cs:1526-1529`, `:1603-1608`) → the body is drained up to 1 MiB within 10 s (as
  `SkipDeferredBodyAsync` does for refused uploads, `:1537-1546`, `:955-960`), then `413 {"ok":false,"error":"a request body is at most 64 KB"}`
  and close. A declared body over 1 MiB is closed without draining (out of contract by sixteen times; the reset is accepted). No body is
  logged. There is no chunked transfer: `HttpRequest` reads `Content-Length` only, and that stays.
- **Rig.** A line `stall <ms>` on the rig's standard input stops its ticker marking the producer alive for that long and prints
  `{"rig":"stalled","ms":5000}`, then `{"rig":"running"}`.
- **Rig messages (new unit SW-RM).** A rig message with `ack` makes a signal id (counting from 1): the receipt carries `refs.messageId`; frames
  carry `messageId`, `messageAck` (`sent`) and `messageAckBy`; `ack <id> seen|done <by>` moves it; a new message cancels the old signal;
  `clearmessage` clears it. Node keys come from peers-not-screens' `--node-keys N` (its SW-6).
- Proposed to spec-1-3's owner for §13: `beat.stuck {"type":"beat","live":false}`.

---

## 4. Changes, file by file

### 4.1 Patterns — Round 89 (PLAN §107): a stuck Stagewatch shows no time; a page's pass never leaves the machine

**89.1 (G04)**
- `src/Patterns.Core/Services/StagewatchFollow.cs`
  - `StagewatchFrame.IsStuckBeat(string? json)`: true for an object with `type` `"beat"` and `live` literally `false`.
  - `StagewatchSight.Stuck` (`bool`, init, not positional) and `MessageAtSeconds` reserved for 91.2 (appended there, not here).
  - `StageFollow.Words` (`:163-171`): a case for `Stuck` with no frame ("… says its show is stuck — this desk's own stage timer runs until its
    first frame").
  - `ShowBusView.Apps` (`:206-227`): a linked Stagewatch whose sight is `Stuck` gets `EyeShowApp.Stuck = true`. `Peers` (`:242-254`): light
    `red`, headline "its show is stuck — no time shown". `BriefLines` (`:269-285`): "… — red: its show is stuck; the stage shows no time".
  - `ShowAppCard.Of` (`:299-310`): `Stuck` → hue `#B03030`, line "linked · its show is stuck — no time shown".
- `src/Patterns.Core/Services/Eye.cs`: `EyeShowApp` gains `bool Stuck { get; init; }` (`:142-145`); the app light (`:778-783`) is red and its
  sub "its show is stuck — the time is not shown" when `Stuck`.
- `src/Patterns.App/Services/StagewatchLinkService.cs`
  - `volatile bool _stuck` (written by the receive loop only; cleared in `Reconcile` `:144-148` and at connect `:280`).
  - `volatile ShowContext? _context` written by `FetchContextAsync` only (`:377` becomes `_context = context`); `_sight` keeps no context
    (`:280` stops carrying `_sight.Context`).
  - `Sight` (`:72-83`): composes `_context`, and while `_stuck` sets `Frame = Frame with { Live = false }` when a frame is held, and `Stuck`.
  - `ReceiveAsync` (`:523-538`): `case "beat" when StagewatchFrame.IsStuckBeat(text)` → when `_stuck` was false: set it, `_status`, one
    `Log.Warn`, `UiThread.Post(_changed)`; `case "state"` → when `_stuck` was true: clear it, one `Log.Info` "runs again".
  - `StatusWords` (`:542-547`) names the stall.
- `src/Patterns.App/Services/TwinService.Followers.cs:357`: repaint also when `was.Frame?.Live != relay.Frame?.Live`.
- `src/Patterns.App/Services/CommandRouter.cs` `ShowBusRow` (`:765-777`): `text` empty and `light` red while stuck; `stuck` appended.
- `src/Patterns.App/Services/StageService.cs` `FollowJson` (`:207-219`): `stuck` appended.
- `integrations/companion-module-patterns/src/{state.js,variables.js,feedbacks.js}`: `stagewatch_stuck` and its red feedback; the STATE
  fixture `test/fixtures/state.json` gains `stuck`.

**89.2 (G09, Core)** — windows-for-peers PT-W1's Core files, without grants:
- `src/Patterns.Core/Services/WindowAddresses.cs` (new, pure): `IsStagewatchWindowUrl`, `LooksPrivate` (decision 5's rule), `Kept(host,
  port, id)`, `TryKept`, `PublicForm`, `IsHeld`, `RedactText(string)` (every private URL in a text → its public form + " (private
  address)"), `NewId(Func<int, byte[]> random)` (12 base32 from the bytes handed in; production passes `RandomNumberGenerator.GetBytes`).
- `src/Patterns.Core/Services/WebPassBook.cs` (new, pure, immutable): entries `(id, url, keptUtc)`; `Keep(url, newId) → (book, id)` reusing an
  entry for the same normalised URL; `Resolve(address) → WebPassResolution(Url?, Why)` (`record struct`); `Merge(other)` (the twin);
  `Label(address)`; ≤ 64 entries, oldest unreferenced dropped first.
- `src/Patterns.Core/Services/WebPassMigration.cs` (new, pure): `Rewrite(ShowState, Func<string,string> secure) → int` over every
  `PatternConfig.WebUrl` reachable (`ContentTargets.AllPatterns`' set plus layers), `Web.SavedUrls`, and each saved look's captured JSON
  (string-replace, as `ContentTargets.cs:71-77` does for ids).
- `src/Patterns.Core/Services/Twin.cs`: `TwinWord.Kept` appended (`:10-50`), `"KEPT"` in `Parse`.
- `src/Patterns.Core/Services/RemoteAdmin.cs`: `SupportBundle.NeverBundled` gains `WebPasses.FileName` with its reason (`:94-99`); `Build`
  (`:110-136`) runs `WindowAddresses.RedactText` over every text entry.
- `src/Patterns.Core/Services/WebAddress.cs:16-26`: `ShortName` of a held address is its host.
- `src/Patterns.Rendering/Patterns/MediaAndParticlePatterns.cs:139-151`: a held address with no page draws black, never a card.

**89.3 (G09, App, the fence)**
- `src/Patterns.App/Services/WebPasses.cs` (new): the book (Interlocked swap), `patterns.webpasses.json` in `SettingsStore.BaseDirectory`
  (`AtomicFile`, one writer: this process), `Keep`, `Secure(typed)`, `MigrateShow(state)` (one journal row with the count and the
  backups/log sentence), `Changed`, `MergeFromTwin(json)`.
- `src/Patterns.App/Services/WebFrameSource.cs`: `NavigateCore` (`:1389-1404`) resolves a held address (black `about:blank` with words when
  it cannot); the failure log (`:1402`) and `_status` (`:1401`) name the held address, never the resolved one; `CurrentUrl` (`:749`)
  returns the held address, or `PublicForm` of a private one; the open log (`:308`) and the sound note (`:995`) name the held address only.
- `src/Patterns.App/Services/WebEngine.cs`: a `Resolve` seam beside `SourceFactory`; re-navigate on `WebPasses.Changed`.
- `src/Patterns.App/Services/WebVideoService.cs` (`:58-75`, `:81-93`): a held page is resolved before the tool is asked; the cache is keyed
  by the held address; `VideoEngine.cs:331` names `w.Origin` when it is set, never the stream URL.
- `src/Patterns.Devices/OscService.cs` (`:267`, `:272`): `_lastLine` holds `WindowAddresses.RedactText(line)`; the router still gets the
  line (it secures it at the action).
- `src/Patterns.App/Services/WebActions.cs:45-51`; `ViewModels/MediaPage.cs:160-197`; `ViewModels/CueEditor.cs:928`;
  `Services/LibraryItems.cs:100-110`: every typed address through `Secure`; the status line says when one was kept.
- `src/Patterns.App/Services/TwinService.Main.cs` (`:81-89`): `KEPT` on welcome and on `WebPasses.Changed`, only with `SendSecrets`;
  `TwinService` standby side: `KEPT` → `WebPasses.MergeFromTwin`.
- Show load (`AppServices` load path) runs `MigrateShow`; the support bundle call passes the kept URLs as secret values.
- `src/Patterns.App/Services/CommandRouter.cs` `WebRow` (`:294-316`): `url` held or public; residency (`:706`) the held key.
- `src/Patterns.App/Services/RuntimeCheck.cs`: the WebView2 probe (closes L26, which ties it to this commit).
- `tests/Patterns.App.Tests/PrivateAddressFenceTests.cs` (new; §5).

**89.4 papers** — `docs/PLAN.md` §107 (requester, evidence class, "what the files said: none this round", the design, the proof, what the
round did not do: the open STATE read (Round 90), grants (Round 92), node links (Round 90), backups and logs on the machine keep old
addresses); `docs/REVIEW.md` Round 89 ("Walk: unwalked — WebView2 path and the twin's takeover touched", §22 rows asked); `CHANGELOG.md`
`## Round 89 — <date> — a stuck Stagewatch shows no time; a page's pass never leaves the machine` with evidence "a handed-in critique (the
cross-repo gap analysis)"; `README.md` (web pages: kept addresses; a standby resolves them when the twin carries secrets); `docs/REMOTE.md`
(`bus.stagewatch.stuck`, `web.url` held forms, `:645` now true); `docs/COMPANION.md` (`stagewatch_stuck`); `docs/QUALIFICATION.md` §22 rows
(a pasted Stagewatch window on a screen over WebView2; STATE and the Eye show `kept://`; restart keeps it; a second machine draws black; a
standby takes the show with the window drawn when SendSecrets is on and black with words when off) and a new §28 for round 89; `docs/OPEN.md`:
add and close L57 (the stuck beat ignored, closed by 89.1) and L58 (a page's pass published, closed by 89.3), close L26 (89.3), amend L36 (the
twin also carries kept passes under SendSecrets) and L40 (the kept passes and pre-migration copies in `backups/` and the log join it), add L59
(STATE greeted and pushed to unpaired wire peers, closed by Round 90) and L60 (a standby with SendSecrets off takes the show with kept pages
black); the tag table row for round 88 (`bcdbf2829cea302078acee7887841389e00d5178`). The closing sentence: no new verb; the stall's two log
lines and the migration's journal row are what the rig reads beside the stage page.

### 4.2 Patterns — Round 90 (PLAN §108): the 1.3 contract from Patterns' side
- **90.1** peers-not-screens PT-1 as designed there (`StagewatchFrame.SocketQuery`, `Auth`, the dial at `:276`, the verdict in `ReceiveAsync`,
  `bus.stagewatch.key`, `stagewatch_key`).
- **90.2** spec-1-3's Patterns unit "reads follow pairing": `ControlService.cs:356-363` (NONCE first, no greeting STATE to an unpaired
  non-loopback connection on a token desk; the greeting after `OK paired`), `:763-774` (`Broadcast` skips unpaired peers),
  `ControlProtocol.OpenBeforePairing`. The tests that read `STATE {` first on a token desk with `TrustLoopback=false` change in this unit:
  `RemoteTrustTests.Wire` (`tests/Patterns.App.Tests/RemoteTrustTests.cs:71-78`) pairs first (reads `NONCE`, sends the proof, then expects
  the greeting), and the `ShowBusAppTests` wire helpers likewise. Closes L59 and G09's remainder.
- **90.3** cross-app-proposals 89.1 (the 15 rows of its §2.8 appended to `ShowContext.cs:157-173`) and `ShowContext.Parse` (`:99`) keeps 256
  commands, drops a command whose verb is over 512 characters, cuts words at 512; `StagewatchFollow.cs:281` `Take(32)`.
- **90.4** papers: PLAN §108, REVIEW Round 90, CHANGELOG, REMOTE, COMPANION, `docs/SHOWBUS.md` copy 1.3 (spec-1-3), OPEN (close L59; add and
  close "the context omits VIDEO STOP and the staged verbs"; add "WEB OPEN is not advertised until a window is granted, not typed"),
  QUALIFICATION §26 row (a refused key on a real Stagewatch), the tag row for 89.

### 4.3 Patterns — Round 91 (PLAN §109): the operator's link — set it, speak through it, hold a playlist item
**91.1 (G34)**
- `src/Patterns.Core/Model/ShowState.cs` `StagewatchLinkConfig` (`:2339-2370`): `KeyFor` (string: `node:<id>`|`addr:<host>:<port>`|`none`|`""`;
  set by the Nodes page's key box and by the bind rules, never by a line's value).
- `src/Patterns.Core/Services/StagewatchFollow.cs`: `StagewatchLinkVerb.Parse(arg) → (Kind, Value)` pure (`id:` prefix for a heard id);
  `StageFollow.KeyGoesTo(keyFor, pinKey, peerAddress) → bool` pure (`none` and `""` after a line → false); `StageFollow.BindAfterLine(keyFor)`.
- `src/Patterns.Core/Services/ControlProtocol.cs`: `case "STAGEWATCH"` beside `NODES` (`:1319`): STATUS query, LINK, FOLLOW, ACCEPT, KEY
  (refusal); `ShowActionKind.StagewatchLink`, `StagewatchFollow`, `StagewatchAccept` appended; all three high.
- `src/Patterns.App/Services/StagewatchLinkService.cs`: `ConnectAsync` (`:269`) and `Request` (`:361`) set the header only when `KeyGoesTo`;
  `Resolve` (`:225-243`) resolves `id:`; an unbound key binds at the first 2xx of `PostOwnContextAsync` (`:405-407`) when key-shaped; an
  `addr:` binding re-binds to `node:<id>` when the link is TLS with that node's pin held and the hello names the address.
- `src/Patterns.App/Services/ShowActions*.cs`: the three actions (refused from origin `(Tcp, "approval desk")`), journal rows.
- `src/Patterns.App/Services/CommandRouter.cs`: the STATUS answer; `bus.stagewatch` appends `follow`, `pending`.
- `src/Patterns.Core/Menus/EyeMenus.cs` (`:16-75`): for a Stagewatch show app, "Link to this Stagewatch" (`STAGEWATCH LINK id:<id>`), "Unlink"
  (`STAGEWATCH LINK OFF`), "Follow its timer" (`STAGEWATCH FOLLOW ON|OFF`), "Accept its new certificate…" (when pending); the Nodes page's
  card menu reuses the same entries (desk and menu in step); the Nodes page gains "Bind the key to <name>" on a plain `addr:` link.
- `src/Patterns.Core/Services/StagewatchFollow.cs` `BriefLines`: one line naming the link's verbs as the operator's.
- Module: `src/actions.js` (`stagewatch_link`, `stagewatch_follow`, `stagewatch_accept`), `src/variables.js`/`src/state.js`
  (`stagewatch_follow`, `stagewatch_pending`), `test/lines.txt`.

**91.2 (G21)**
- `src/Patterns.Core/Services/StagewatchFollow.cs`: `StagewatchFrame.MessageAckBy` (appended); `StagewatchSight.MessageAtSeconds` (init);
  `StageMessagePath.For(text, flash) → string` (one percent-encoded segment, ≤ 500, `info/<slow|off>/ack`);
  `StageMessagePath.Receipt(status, json) → (Ok, Stage, MessageId, Error)` pure; `StageSpeakerChoice.Of(local, frame, messageAt) →
  (Id, Text, Flash, From)` pure (latest wins).
- `src/Patterns.Core/Model/ShowState.cs` `StageMessage` (`:845-868`): `Via` (`""`|`stagewatch`), `SwMessageId`, `SwAck`, `SwAckBy`
  appended (air-state fields, not saved in looks).
- `src/Patterns.App/Services/StagewatchLinkService.cs`: the receive loop sets `MessageAtSeconds` when `messageId` or the message text moves;
  `SpeakerLane` (its own task per connection, one request in flight, latest wins per channel) with `SendStageAsync(kind, text, flash,
  requestId)` on the link's `HttpClient`; a 403 sets `_readOnlyKey`; results posted to the UI thread; `CanCarrySpeaker` (Live, key held,
  not read-only).
- `src/Patterns.App/Services/StageService.cs`: `Message` (`:107`) routes by decision 12; `Clear` (`:122`) posts `clearmessage` only for a
  bound `SwMessageId` equal to the frame's `messageId`; `Ack` (`:136`) forwards `sw:<id>`; the receipt and frame handlers mark seen and
  journal; `Block` (`:171-198`) appends `speakerFrom`, `speakerAck`, `speakerAckBy`; `DeckSignature` (`:201-205`) adds the frame's
  `MessageId`/`MessageAck` and the via message's ack; `StatusJson` (`:269`) appends `speakerShown`; `FollowJson` appends `messageId`,
  `messageAck`, `messageAckBy`.
- `src/Patterns.App/Services/ControlService.Stage.cs:81-85`: the page draws `speakerShown` (falls back to `pendingSpeaker` when absent); ACK
  posts its id.
- `src/Patterns.Core/Services/ShowContext.cs:162`: the STAGE MESSAGE row's words.
- `src/Patterns.App/Services/TwinService.Followers.cs:357`: repaint when `MessageId` or `MessageAck` moves.
- Module: `src/state.js`, `src/variables.js` (`stage_speaker_from`, `stage_speaker_ack`), `src/feedbacks.js` (`stage_speaker_ack_is`),
  `test/fixtures/state.json`.

**91.3 (G48)**
- `src/Patterns.Core/Services/PlaylistSequencer.cs`: `Hold(utcNow)`, `Release(utcNow)` (the item's start moved on by the held span, the
  override's end likewise), `Held`; due schedule entries held then fired at release inside their window, else reported missed.
- `src/Patterns.App/Services/PlaylistService.cs:165-175`: `HoldCurrent`, `ReleaseCurrent`, a journal row for a missed schedule.
- `src/Patterns.App/Services/StingerService.cs`: `HoldClip(bool)`; the stall guard (`:753-766`) skips a held clip.
- `src/Patterns.App/Services/ShowActions.Content.cs:481-505`: PAUSE/PLAY/STOP by role as §3.1.
- `src/Patterns.App/Services/CommandRouter.cs:229-262`: `pausable`, `stoppable` after `seq`.

**91.4 papers** — PLAN §109, REVIEW 91, CHANGELOG, README (the link from a deck; the message route and its fallback; holding a playlist
item), REMOTE (the STAGEWATCH verbs, the message route, `video.pausable/stoppable`, `stage.speaker*`, `speakerShown`), COMPANION, OPEN (close
L48; add and close "the link is set on the Nodes page only" and "two speaker messages while following"; add "the assistant does not propose
STAGEWATCH lines (decision 16)" as checked and left), the tag row for 90. Walk: unwalked (no platform path; L50 and L56 stay open).

### 4.4 Patterns — Round 92 (PLAN §110): PlayMap known; Stagewatch windows granted
- **92.1 (G35)**
  - `src/Patterns.Core/Model/ShowState.cs:2404-2413`: `DeviceProfile.ShowBusNode` appended.
  - `src/Patterns.Core/Services/DeviceProfiles.cs`: `ProfileSession.EncodeFact(words, out problem)` virtual (default `Encode`);
    `ShowBusNodeSession(secret, deskName)` (`For`, `:35-43`): NONCE → `HELLO` → proof → `SUBSCRIBE OFF` → `SUBSCRIBE CONTEXT`; `@d<n>` on
    commands with `SentKey`; `OK/ERR @…` as acks and refusals; `CONTEXT {…}` raised as context; `STATE` lines dropped and counted;
    `EncodeFact` untagged and only for PlayMap's fact keys (a copy of `FactLines.Keys`, held by a contract test against
    `Mapper/docs/WIRE.md:101-106`); `Label` "Show Bus node (PlayMap, Patterns)", `Words`, a preset.
  - `src/Patterns.Devices/DeviceService.cs`: `SendFeedback` (`:769-790`) and the `OPEN 1` announce write through `EncodeFact`; on paired,
    contexts kept per device (≤ 512 KB, latest wins).
  - `src/Patterns.Core/Services/ShowBusNetwork.cs:168-175`: `"playmap" => "PlayMap"` (also in spec-1-3's Patterns table — one hunk).
  - `src/Patterns.Core/Services/StagewatchFollow.cs` `ShowBusView.Apps`: a heard PlayMap whose address and wire port a ShowBusNode device
    names takes that device's context and `Linked` (paired); the peers row `t:"playmap"`.
  - `src/Patterns.Core/Services/Eye.cs:773-820`: a `ShowAppItem` of kind output, projector, surface, playlist or media sits on the Video
    plane (FLEET.md's P3); the device→app edge is `Controls`.
  - Nodes page and the Eye's menu: "Add as a device…" for a heard PlayMap (opens Devices, prefilled; the Secret box focused).
- **92.2–92.4** windows-for-peers PT-W2 and PT-W3 as designed there (grants, `SCREEN n WINDOW`, the Eye chain), on top of 89.2's held
  addresses (only the grant half is new).
- **92.5 papers** — PLAN §110, REVIEW 92, CHANGELOG, README, REMOTE, COMPANION, SHOWBUS copy, OPEN (add and close "PlayMap unknown to
  Patterns; device links do not pair by proof"), QUALIFICATION §22 (a PlayMap node driven and drawn from a Patterns desk on one machine,
  its blackout following over the device link), the tag row for 91.

### 4.5 Mapper — the exception request (PM-X), then the fixes it would allow
**PM-X, the request (papers only, to the owner).** It quotes §4 in full ("Every file has one owner in each wave (§2.2). A package touching
another's file does so only through a hunk named here, landing in §5.3's order. … A folder not named for a wave keeps no owner in it, and
nothing in that wave edits it", `Mapper/docs/round3/PLAN.md:2122-2125`), says the `fix-*-win` branches were stability fixes, and asks leave
for exactly: PM-F1 (`Core/Media/PrivateAddress.cs` new, `VerbCatalog.Redacted` outside the blocks), PM-F2 (`Engine/Host/MediaBackend.cs`,
`EngineCore.Content.cs` before W3, the engine-start scrub), PM-F3 (`Engine/Host/NetworkHost.cs:318-341`, `Engine/Rendering/TimerView.cs`),
PM-F4 (`Net/Links/ContextFit.cs` new, `Net/Links/StagewatchLink.cs:500-519`, the test fake). Each with what it protects (§1). Declined, all
four become integration-pass items with these notes, and SHOWBUS-NODE says "do not paste a window address into MEDIA IMPORT" meanwhile.

**PM-F1, PM-F2 (G47)**
- `src/PlayMap.Core/Media/PrivateAddress.cs` (new, pure): `Looks(string)` — Patterns' `LooksPrivate` rule, word for word, held by a shared
  table test.
- `src/PlayMap.Core/Wire/VerbCatalog.cs` `Redacted` (`:617-646`): a word that `PrivateAddress.Looks` is shown as `‹private address›`, beside
  `CaptureUri.Shown`.
- `src/PlayMap.Engine/EngineCore.Content.cs:390-407`: refuse Web/Embed and private addresses (refusal words never echo the address);
  `.m3u8`/`.mpd` over https as Stream. `:447`, `:509`, `:528`: Web/Embed `ready:false`, `unplayable`, words.
- `src/PlayMap.Engine/Host/MediaBackend.cs:378-383`: readiness for Web/Embed `Present:true, Decodable:false`, words "a web page — PlayMap has
  no browser source (ROADMAP L10)".
- The engine-start scrub (in `EngineCore.Content.cs` beside the import, one `Edits.Apply` with one journal row).
- Integration-pass notes: the `MEDIA IMPORT` catalogue row's words (`VerbCatalog.cs:185`, `<path|url>` → "a file or a stream URL; pages,
  embeds and addresses carrying a pass are refused"); `FakeEngineLink.cs:697` files like the engine; `docs/wire/content.md`, ROADMAP L10,
  a CARRYOVER entry.

**PM-F3 (G12, air half)**
- `src/PlayMap.Engine/Host/NetworkHost.cs:318-341`: append `blackout`, `watermark` (the last non-empty watermark the link saw stays while
  untrusted).
- `src/PlayMap.Engine/Rendering/TimerView.cs:94-150`: `Read` returns a record (time, colour, title, message, blackout, watermark); `Draw`
  clears black and draws only the watermark under a blackout; the watermark bottom-right at 3% of the height, drawn last, in every state.
- Integration-pass notes: `StateSnapshot.ReadLink`/`TimerState` take `blackout` and `watermark` (`Mapper/src/PlayMap.App/State/StateSnapshot.cs:85`,
  `:285-292`); `StateExplainer` says "blacked out" (`Mapper/src/PlayMap.Assistant/StateExplainer.cs:263-275`); SHOWBUS-NODE §3 (`:49-50`).

**PM-F4 (G25)**
- `src/PlayMap.Net/Links/ContextFit.cs` (new, pure): `ForPost(string json, int maxBytes) → (string? Body, string Words)` by decision 11.
- `src/PlayMap.Net/Links/StagewatchLink.cs:500-519`: post the fitted body; words when trimmed, not posted, or answered 413.
- `tests/PlayMap.Net.Tests/StagewatchLinkTests.cs:53-56`: the fake answers 413 over 64 KiB, as Stagewatch will.
- Papers note: SHOWBUS-NODE §2 (the post fits 64 KiB), CARRYOVER (the 1.3 count rules into `ShowContext.ToJson` at the integration pass).

**PM-F5, `fix-showbus-node-papers` (G35, Mapper's half; papers only, outside §4's code rule)** — `docs/SHOWBUS-NODE.md:40` (Patterns'
address on PlayMap's `NodeSettings.TrustedAddresses`, or an open node, until Patterns' Show Bus node profile pairs by proof), `:72-73`
(proposed, not built), `:37` (redirect is words only until node links); `docs/FLEET.md:97-103` (P1–P3 answered by Patterns Round 92 when it
lands); the "do not paste a window address" line while PM-F2 waits.

### 4.6 Mapper — after round 3's integration pass (or on the owner's word)
- **G31** (with peers-not-screens PM-1): `NetworkHost.cs` (`StagewatchOptions` `:718-736` tries TLS for a typed address with no pin; `OnPeer`
  `:802-806` re-resolves a typed address against a matching hello and restarts the link once); `StagewatchLink.cs` (`Brief` `:256-259`; a
  plain 403 retries over TLS once; the desk probe in the beat loop `:468-472`; `TlsFirst`, `DeskProbeEvery = 60 s`); papers: SHOWBUS-NODE §2.
- **G12, the rest** — windows-for-peers PM-W2/W3, with §3.2's field order; the desk's and the assistant's readers with it.
- **G13** — cross-app-proposals unit 12.
- **G36** — `TimecodeModel.cs:5` `TimecodeKind.Patterns`; `src/PlayMap.Engine/Timecode/PatternsChase.cs` (new); `VerbCatalog.cs` the
  `TIMECODE SOURCE ADD` syntax and a `LINK PATTERNS SEND` row; `EngineCore.Router.cs:162-163` routing to `PatternsLink.SendAsync`.
- A contract test reading Patterns' Show Bus node session (G35) against PlayMap's real `WireSession`: HELLO, proof, `SUBSCRIBE OFF`,
  facts taken untagged, `@d` lines answered.

### 4.7 Stagewatch (this area's small units)
- **SW-25** `src/Timer.Remote/WebServer.cs` (`HttpRequest.ReadAsync` `:1569-1613`, the caller `:319-321`): an `Oversize` length on the request
  for a non-deferred path; the caller drains (≤ 1 MiB, 10 s) and answers 413 (§3.3). `docs/REMOTE.md`, `docs/WHATS_NEXT.md`.
- **SW-ST** `tools/Stagewatch.Rig/Program.cs` (`Tick` `:251-255`, the run loop): the `stall <ms>` standard-input line;
  `tests/e2e/rig.cjs` (`stall(ms)`); `tests/e2e/stall.e2e.cjs` (new).
- **SW-RM** `tools/Stagewatch.Rig/Program.cs` (`ExecuteWithReceiptAsync` `:184-195`, `Run` `:198-221`, `Frame` `:258-290`): message signals,
  `refs.messageId`, the frame's `MessageSignalId`/`MessageAck`/`MessageAckBy`, `ack`; `tests/e2e/message.e2e.cjs` (new).

---

## 5. Tests

### Patterns (all seven suites and the module's tests green before each push)
**89.1**
- `tests/Patterns.Core.Tests/ShowBusTests.cs` `AStagewatchFrameIsReadAndABeatOrAPongIsNotOne` (`:367-383`) **keeps** its `live:false` line
  (`:374`) and gains: `IsStuckBeat("{\"type\":\"beat\",\"live\":false}")` true; `{"type":"beat"}` false; `{"type":"beat","live":true}` false;
  `{"type":"state","live":false}` false; malformed false. New facts: a running frame with `Live=false` gives `StageFollow.Time(...).Phase ==
  Idle` and `Words` containing "stuck"; a sight with `Stuck` and no frame is **not** `Following` and its words say the desk's own timer runs;
  `StagewatchRelay.Of(stuck).ToJson()` parsed and `ToSight` keeps `Frame.Live == false`; `ShowBusView.Apps` on a stuck linked sight gives
  `Stuck`, `Peers` light `red`, `ShowAppCard.Of` hue `#B03030` and the stuck line; the Eye graph's show-app node is red with the words.
- `tests/Patterns.App.Tests/ShowBusAppTests.cs` `FakeStagewatch` (`:251-323`) gains `volatile bool Stuck` (each ping answered with the pong and
  the stuck beat bytes instead of a frame) and a `ContextDelay` hook. New `[AvaloniaFact]`s:
  - `AStuckStagewatchShowsNoTimeEverywhereAndItsNextFrameBringsTheTimeBack`: follow → `Stuck = true` → pump until `Stage.Time().Phase == Idle`;
    the sight is still `Live` (pongs); `StatusJson().follows.stuck` true; STATE `bus.stagewatch` `stuck:true`, `text:""`, `light:"red"`; the
    Eye's Stagewatch node red; the Nodes page card red; `Stage.Pause()` still refused (still following); the log has one "stuck" line over
    three stuck beats. `Stuck = false` → Running again, `stuck:false`, one "runs again" line.
  - `AContextLandingBesideAStuckBeatKeepsTheStageDark`: the fake answers `/api/context` while the stuck beat is in flight (the hook orders a
    context write after the beat's); the stage stays Idle across five context reads.
  - `AStagewatchStuckBeforeItsFirstFrameLeavesTheDesksTimer`: no frame, stuck beats only → `Stage.Following == ""`, `Stage.Pause()` allowed,
    STATE `stuck:true`.
- `tests/Patterns.App.Tests/ShowBus12AppTests.cs` `ATimerNodeFollowsItsDesksStagewatchByRelay…` (`:413`) gains a stuck leg: the node's stage
  goes Idle on the next relay without a `seq` change.
- Module `test/module.test.mjs`: `stagewatch_stuck` from the fixture; the feedback.

**89.2–89.3**
- Core `WindowAddressesTests` (new): the classification table (a `/w/` path of 26 alphabet letters; 25 letters; an `l` in it; `?view=`,
  `?pin=4321`, `#token=`, `user:pass@`; a plain page); `NewId` is 12 base32 from the bytes given; **the id is not a function of the URL**: two
  fresh books keeping the same URL with `RandomNumberGenerator` give different ids, and no id equals any 12-character window of the URL's
  SHA-256 in base32; `PublicForm`; `RedactText` over a log line. `WebPassBookTests`: the same URL kept twice reuses its id; 65 entries keep
  64; `Merge`. `WebPassMigrationTests`: a show with a private address in the pattern, a layer, SavedUrls and a look's JSON rewrites all four
  and counts 4 (ids from a fixed byte source).
- Rendering: a held address with no page is all black (no placeholder pixels).
- App `PrivateAddressFenceTests` (new): a show with `http://10.0.0.5:<fake port>/w/<26>` on a layer and `https://x/feed?pin=4321` on a pattern;
  asserts neither pass nor `4321` is in any of STATE, CONTEXT, EYE, the journal file, the saved settings and show file, the recovery record,
  the support bundle (built; the kept file named in `NeverBundled`), the twin's mirror payload with `SendSecrets` off, the module's recorded
  lines, OSC's status line after `/patterns/web/open <private url>`, and the log after a forced navigate failure and after a native-player
  open failure; the kept file holds both; `WEB OPEN` with a private address replies with the host only; a Round 88 settings file holding a
  `/w/` URL loads migrated with one journal row. **Twin leg:** a main and a standby in one process (the existing twin rig), `SendSecrets` on
  → the standby's book holds the entry and, after TAKE OVER, its source factory receives the real URL; `SendSecrets` off → the standby
  draws black and its `web.page` says "kept on another machine".
- App `LibraryWebTests` / `WebAppTests`: `WebEngine.SourceFactory` receives the held target and the resolver returns the real URL; an unknown
  kept id resolves to nothing and the source navigates `about:blank`.
- `SupportBundleTests`: the new file-name constant is in `NeverBundled` with its reason (the existing fence).
- Windows job: `--verify-runtime` reports the WebView2 probe (L26).

**90** — the siblings' tests (peers-not-screens PT-1; cross-app 89.1's order, risk and parse fences; spec-1-3's ControlService: an unpaired
remote connection hears NONCE first and no STATE, a paired one hears the greeting after `OK paired`, `Broadcast` skips the unpaired), the
`RemoteTrustTests.Wire` and `ShowBusAppTests` helpers pairing first, plus `ShowContext.Parse` of PlayMap's busy golden (copied as a fixture)
keeps all 199 commands with their verbs and words whole (max 307 and 403 characters, under 512), and a synthetic command with a 513-character
verb is dropped while one with 600 characters of words keeps 512.

**91.1** Core: `StagewatchLinkVerb.Parse` table (every form, `id:` versus a bare host, the refusals); `KeyGoesTo` truth table (node match,
address match, a heard id whose address matches an `addr:` binding, `none`, unbound after a line, mismatch); `BindAfterLine`. App: with the
key bound to fake1, `STAGEWATCH LINK localhost:<fake2>` → fake2 sees **no** `Authorization` header and the reply says so; with an unbound key
(AUTO, nothing answered yet) a `STAGEWATCH LINK localhost:<fake2>` → `KeyFor` `none` and fake2 sees no header; binding happens on a 2xx to
`POST /api/peers/context` with a `swk_` key and not with a PIN-shaped value; `STAGEWATCH LINK OFF`/`AUTO`/`FOLLOW OFF` move STATE; the
approval-desk origin is refused; `STAGEWATCH ACCEPT` with a wrong prefix refused, with the right one pins (extends
`TheLinkTrustsAStagewatchsCertificate…` `ShowBus12AppTests.cs:304`); the Eye menu entries carry the same wire lines (`DeskMenuAppTests`
pattern). Module: the three actions emit `test/lines.txt`'s lines.

**91.2** Core: `StageMessagePath.For` (spaces, `/`, `%`, 500/501 characters, flash → `slow`/`off`), `Receipt` (200 applied with refs, 200
accepted without, 409, 401, 403, an unreadable body), `StageSpeakerChoice.Of` (latest wins either way; an ACKed message leaves). App:
`FakeStagewatch` gains `/api/message/…`, `/api/clearmessage`, `/api/ack/…` handlers recording path, `Authorization` and `X-Request-Id`,
answering receipts (`applied` with `refs.messageId`, or `accepted` on a switch) and then frames with `messageId`, `messageAck` `sent` →
`seen`, `messageAckBy`. Asserts:
- with a node key: STAGE MESSAGE → `Requested`; the fake saw `/api/message/Wrap%20up/info/slow/ack` with the key and an id; the journal row
  "on the speaker's screen (message 41)"; STATE `stage.speakerFrom "stagewatch"`, `speakerAck` follows the frames; `pendingSpeaker` clears
  once `seen`; `/api/stage` `speakerShown.id == "sw:41"`; ACK `sw:41` reaches the fake as `/api/ack/41/seen/<desk>`;
- the `accepted` receipt journals "not yet applied" and binds when the frame shows message 41;
- **no key**: a cue step's STAGE MESSAGE → `Done`, nothing posted, the page shows it, `speakerFrom "desk"`, words say Stagewatch's displays do
  not show it;
- a 403 → the message turns local with the refusal journalled; the next message goes local without a post;
- a stale link → local, nothing posted;
- STAGE CLEAR with Stagewatch's own message on (an id this desk never posted) → no `clearmessage`, the words say it stays; with this desk's
  message on → `clearmessage` posted;
- two messages in 50 ms while the fake holds the first request → the second is sent, the first journalled "superseded";
- a message is posted while the fake delays `/api/context` by 3 s (the lane does not wait for the beat);
- `DeckSignature` changes when the frame's `messageAck` moves;
- FollowStage off sends locally as today.
Module: `stage_speaker_from`, `stage_speaker_ack` from the fixture; `stage_pending` false once the via message is seen.

**91.3** Core `PlaylistSequencerTests`: a held item does not advance at its end time; release moves its start by the held span; a schedule
due during the hold fires at release inside its window and is reported missed past it. App: VIDEO PAUSE on a playlist item → Done,
`video.paused` true, `pausable` and `stoppable` true; VIDEO STOP on a stinger's clip refused with `STINGER STOP`; STATE substrings of the
row's old fields unchanged (the new ones at the end).

**92.1** Core `DeviceProfilesTests`: `ShowBusNodeSession` against a scripted line peer — NONCE, then `HELLO Patterns FOH`, then the proof (HMAC
over nonce + "Patterns FOH"), `SUBSCRIBE OFF`, `SUBSCRIBE CONTEXT`; a `STATE {…}` line dropped and counted; `@d` tags and receipts on
commands; `EncodeFact("BLACKOUT 1")` untagged, `EncodeFact("OPEN 1")` sends nothing; a wrong-token reply stops sends; `CONTEXT` lines raised;
the fact-key copy equals the list in Mapper's WIRE.md item 6 (a fixture copied with its source line). App: a scripted PlayMap wire (TCP) with
a token: "Add as a device" prefill; pairing; `DEVICE <name> PLAYLIST GO show #3` gets `OK @d…`; a blackout on the desk reaches the wire as an
untagged `BLACKOUT 1` and draws no `ERR`; the Eye card lit from the pushed context.

### Mapper (both CI jobs; analyzers on tests)
- **G47**: `tests/PlayMap.Core.Tests` redaction tests: a `MEDIA IMPORT` line with a `/w/` address, a `SET <id> location "<…?pin=4321>"` line
  and a nested binding line all show `‹private address›`; a public URL stays; the shared private-address table equals Patterns'.
  `tests/PlayMap.Engine.Tests/Content/MediaImportTests` (new): the four imports of §3.2 — refusals add no item, the reply and the journal row
  (text and line columns) contain no token (searched in STATE `events` and the context's recent); `.m3u8` adds a Stream; an existing Web item's
  LIST is `ready:false`/`unplayable`; FETCH's words; a project loaded with a pass-bearing Web item is scrubbed with one journal row and the
  pass is in neither MEDIA LIST, STATE nor the context.
- **G12 air half**: `tests/PlayMap.Engine.Tests/Rendering/TimerViewTests` (new): `Read` parses `blackout` and `watermark`; one pixel test on a
  64×36 view drawn through the `BackendFixture` leg (the GL line holds): blackout leaves the time region black and the watermark region
  non-black; the watermark present with `state:"unknown"`. `tests/PlayMap.Net.Tests/StagewatchLinkTests.cs`: a frame with `blackout:true` and a
  watermark reaches `StagewatchTimerJson` with both at the end of the object.
- **G25**: `tests/PlayMap.Net.Tests/Links/ContextFitTests` (new): the busy golden alone → byte-identical, no extra item; the golden plus 20 recent
  rows of 64 KiB → ≤ 65,536 bytes, every recent row kept (cut to 200), every item kept, all 199 commands' verbs and risks identical and in
  order, `context:trimmed` present with the counts; a synthetic context whose items alone exceed 64 KiB → green items dropped first, every red
  and amber kept; an unfittable synthetic (reds alone over 64 KiB) → `Body == null` with the words. `StagewatchLinkTests`: the fake's 413 for an
  unfitted body produces the link's words, not a debug log only.
- **G31**, **G13**, **G36** (later): as the first draft (TLS first, plain only without a pin, a pinned address answering plain refused, a later
  hello restarts once with Approvals, the desk probe turns collection on, a plain 403 never reads as a key problem; `PatternsChaseTests` on a
  manual clock; `LINK PATTERNS SEND` against the fake Patterns wire; cross-app's list). Plus the G35 contract test against `WireSession`.

### Stagewatch (`tools/test.sh` lanes)
- Unit (`tests/Stagewatch.Tests`), **SW-25**: a real `HttpClient` against a real `WebServer` socket posts a 70,000-byte body to
  `/api/peers/context` and **reads** a 413 with the JSON error (not a reset); the next request on a new connection works; a 64 KiB body is
  accepted as today; a 25 MB `Content-Length` on `/api/holding/upload` is still read by its own path; a declared 2 MiB body on another path is
  closed without draining within the timeout.
- e2e `tests/e2e/stall.e2e.cjs` (new, **SW-ST**): a node-style socket on the rig → `rig.stall(5000)` → within `StuckAfter` + 2 s it receives
  exactly `{"type":"beat","live":false}` (repeated every 2 s) and no state frame until recovery; after recovery its first message is a state
  frame, then plain beats; pings are answered (pongs) throughout.
- e2e `tests/e2e/message.e2e.cjs` (new, **SW-RM**, the rig with peers-not-screens' `--node-keys 1`): `POST /api/message/Wrap%20up/info/slow/ack`
  with the node key and `X-Request-Id` → receipt `ok:true`, `stage:"applied"`, `refs.messageId`; the next frame has that `messageId` and
  `messageAck:"sent"`; `POST /api/ack/<id>/seen/Patterns%20FOH` → `messageAck:"seen"`, `messageAckBy` beginning "Patterns FOH"; a second
  message cancels the first signal; `clearmessage` clears. This is the contract Patterns' `FakeStagewatch` copies.

---

## 6. Units, in order (each buildable and testable alone)

| # | repo | unit | gaps | depends on |
|---|---|---|---|---|
| PT-89.1 | Patterns | Round 89.1: the stuck beat in every reader (single-writer flag, pages, STATE, nodes, Eye, Nodes card, brief, module, log) | G04 | — |
| PT-89.2 | Patterns | Round 89.2: held addresses, Core (rule, random ids, book, migration, twin word, bundle redaction, ShortName, black) | G09 | — |
| PT-89.3 | Patterns | Round 89.3: held addresses, App (kept store, entries, OSC, native player, load, resolver, CurrentUrl and logs, STATE, twin carry, WebView2 probe, the fence) | G09 | PT-89.2 |
| PT-89.4 | Patterns | Round 89 papers | — | PT-89.1, PT-89.3 |
| PM-X | Mapper | the exception request to the owner, quoting round 3 §4 | G47, G12, G25 | — |
| PM-F1 | Mapper | private address marker in `Redacted` (on the exception) | G47 | PM-X |
| PM-F2 | Mapper | `fix-media-page-import`: refuse pages and passes, scrub kept ones, honest LIST/readiness/FETCH, HLS as Stream | G47 | PM-F1 |
| PM-F3 | Mapper | `fix-stagewatch-timer-air`: blackout and watermark in STATE and on the surface | G12 | PM-X |
| PM-F4 | Mapper | `fix-stagewatch-context-size`: the post fits 64 KiB by bytes, said when trimmed | G25 | PM-X |
| PM-F5 | Mapper | `fix-showbus-node-papers`: SHOWBUS-NODE and FLEET corrected | G35 (docs) | — |
| SW-25 | Stagewatch | 413 for an oversize body, drained, deferred paths exempt | G25 | — |
| SW-ST | Stagewatch | rig `stall`, `stall.e2e.cjs` | G04 (conformance) | — |
| SW-RM | Stagewatch | rig message signals, receipts with refs, ack; `message.e2e.cjs` | G21 (contract) | peers-not-screens SW-6 (`--node-keys`) |
| PT-90.1 | Patterns | Round 90.1: node link and verdict (peers-not-screens PT-1) | (G32, G43) | Stagewatch SW-3 for the verdict; degrades safely |
| PT-90.2 | Patterns | Round 90.2: reads follow pairing, with the tests that read STATE first | G09 (remainder) | Stagewatch's master 1.3 final |
| PT-90.3 | Patterns | Round 90.3: the context's 15 rows; read 256 commands, 512 characters | G03, G33, G35 (cap) | rated right after Stagewatch cross-app unit 1 |
| PT-90.4 | Patterns | Round 90 papers | — | PT-90.1…3 |
| PT-91.1 | Patterns | Round 91.1: STAGEWATCH verbs, KeyFor and its bind rules, menus, module | G34 | PT-90.1 |
| PT-91.2 | Patterns | Round 91.2: the speaker's message through Stagewatch, with its fallback, lane, receipt stages, ACK and module | G21 | — (its contract e2e is SW-RM) |
| PT-91.3 | Patterns | Round 91.3: hold a playlist item or a stinger's clip; `pausable`, `stoppable` | G48 | — |
| PT-91.4 | Patterns | Round 91 papers | — | PT-91.1…3 |
| PT-92.1 | Patterns | Round 92.1: Show Bus node devices (HELLO, proof, untagged facts), PlayMap's context, kind words, the Video band | G35 | PT-90.3 |
| PT-92.2…4 | Patterns | windows-for-peers PT-W2, PT-W3 (grants on held addresses) | G10, G24 (theirs) | PT-89.3, Stagewatch SW-W3 |
| PT-92.5 | Patterns | Round 92 papers | — | PT-92.1…4 |
| PM-P1 | Mapper | post round 3: typed address re-resolved, TLS first, desk probe, 403 words (with peers-not-screens PM-1) | G31 | integration pass |
| PM-P2 | Mapper | post round 3: the timer's other fields, native windows, the desk's and assistant's readers | G12 | PM-F3 (or its notes), integration pass |
| PM-P3 | Mapper | post round 3: the assistant's desk (cross-app unit 12) | G13 | owner's decision 7 |
| PM-P4 | Mapper | post round 3: Patterns chase source, `LINK PATTERNS SEND`, the G35 contract test | G36 | integration pass; owner's yes |

---

## 7. Gaps

**Closed by this design**
- **G04** — the stuck beat turns the held frame to no time in every reader (pages, STATE, nodes, Eye, Nodes card, brief, module, log), with one
  writer per field so a context cannot undo it; a test with the real bytes, and a Stagewatch e2e pinning them.
- **G09** — the pass is never a key and the published id is random, so no Patterns artefact carries the pass or anything derived from it
  (fence-tested, OSC, native player, navigate failures and the twin included); a standby resolves kept pages when the twin carries secrets;
  its remainder (STATE to unpaired peers) closes in Round 90.2.
- **G47** — on the owner's exception: pages and passes refused with words; a pass is a marker in every shown line; kept passes scrubbed;
  readiness and LIST honest. Declined, it is designed and waits for the integration pass.
- **G12** — the air half on the exception (blackout, watermark); the rest with native windows (PM-P2), its order fixed here.
- **G25** — the post fits Stagewatch's 64 KiB by bytes, says when it trims, and Stagewatch answers 413 (a drained answer the sender reads).
- **G21** — one speaker message on the page (latest wins), through Stagewatch with its receipt stage and ACK when the link can carry it, on this
  desk with words when it cannot; the deck sees the ACK.
- **G34** — the link set from the wire, the deck and the menus; the key stays desk-typed and goes only to the Stagewatch it is bound to.
- **G48** — Patterns holds playlist items and stingers' clips, and publishes what is pausable for Stagewatch's link.
- **G03, G33** — cross-app's rows appended (APPLY review, the staged verbs, VIDEO STOP high).
- **G35** — PlayMap paired, driven, followed and drawn from Patterns; Mapper's papers corrected now.

**Ready, not closed**
- **G31** — designed; Mapper's files wait for the integration pass, with peers-not-screens PM-1.
- **G13** — designed by cross-app; waits for the owner's answer to round 2 decision 7.
- **G36** — designed minimal; waits for round 3 and the owner's yes; frame accuracy needs `ANCHOR` (`video.anchor`), which no unit builds.

**Left, with why**
- G12's desk and assistant readers (`StateSnapshot`, `StateExplainer`) are wave-owned files; they go to the integration pass as notes.
- G34's "assistant-proposable line" is declined (decision 16).
- G21: a forwarded speaker ACK can be refused under Stagewatch's crew-key policy (`WebServer.cs:1049-1051`); Patterns shows the refusal.
  The crew channel stays this desk's (a node key may run only `message`/`clearmessage`). Patterns' messages carry no urgency, so Stagewatch
  shows them at `info`.
- G09: Patterns' wire and twin stay plain TCP (OPEN L55, L36); pre-migration copies in `backups/` and the log stay on the machine (L40's class).
- PlayMap rendering real pages (ROADMAP L10) is not built.
- AGMessage's context is not read by Patterns (no gap of this area).

---

## 8. Risks

- **Kept passes are plain per-machine JSON** (OPEN L40's class), and with `SendSecrets` on they cross the twin link readable (L36's class).
  A show carried to another machine without the twin draws those pages black with words; the cross-machine road is a grant.
- **A standby with `SendSecrets` off** takes the show with kept pages black (OPEN L60, a §22 walk row). A page kept on a standby while it held
  the show stays on the standby after hand-back (the twin's secrets travel main → standby).
- **The load migration rewrites addresses in a show file**, including captured looks' JSON; a show opened by a Round 88 build afterwards sees
  `kept://` and draws black. Said in the CHANGELOG and the migration's journal row. Backups keep the old addresses (they are the undo path).
- **WebView2 resolution and certificates are unproven off a real machine** (OPEN L10, L26): Round 89 asks §22 walks and stays `unwalked`
  until they are filled.
- **The STAGE MESSAGE route changes an operator habit**: while following with a node key, a message goes to Stagewatch's speaker displays
  and Patterns' page shows the latest of the two sources. Without one, it stays local and says so; nothing is refused.
- **`KeyFor` on an upgraded desk** binds an old key to the configured host at load; an AUTO desk binds at its first node-scope answer. A line
  that repoints the link before that turns the key to `none`, which stops the key until the operator binds it again — safe, and said.
- **The playlist hold changes the sequencer's timing**; a schedule entry held past its window is skipped and journalled, not played late.
- **The Mapper exception may be declined.** Then PM-F1 to PM-F4 wait for the integration pass, a pasted window address stays a stored pass on
  PlayMap until then, and the SHOWBUS-NODE line is the only guard. If granted, `EngineCore.Content.cs` must land before W3 begins or it
  conflicts with desk-map.
- **Trimming changes what Stagewatch's AI reads of PlayMap** (recent cut to 200 first, which Stagewatch's reader does anyway; then command
  words and green items); the `context:trimmed` item says how many.
- **Blackout on a PlayMap timer surface** follows Stagewatch's room-wide blackout; an operator who drew the timer on a non-speaker surface sees
  it go dark — Stagewatch's contract for every screen; the source's `why` words say so.
- **https HLS over PlayMap's LGPL FFmpeg is unproven**; it is filed as a Stream (it played nothing as Web) and the papers say so.
- **Round renumbering**: §9's table reconciles the siblings' numbers; the maintainer's numbering wins.
- **More connections from Patterns** in Round 92: one device link per PlayMap node (bounded by the device list); each sends one STATE greeting
  before `SUBSCRIBE OFF`, dropped.

---

## 9. What each repo's process requires, and the round map

**Patterns** (`Patterns/CLAUDE.md`)
- Each round opens with `docs/field/round-NN/` (none: "what the files said: none this round") and `docs/OPEN.md`; the PLAN section's first
  lines name the requester and the evidence class (the maintainer's ask, answered from a handed-in critique).
- One commit per unit, `Round NN.k: <what landed>`, the maintainer's authorship, the reasoning in the body; each validated with `dotnet build
  Patterns.sln -c Debug -v q` (one build at a time; the analyzers are fences, tests included — the random id uses `RandomNumberGenerator`,
  never `System.Random`) and the suites it touches; all seven suites and `npm test` in the module green before the push.
- New STATE fields at the end of their rows (§3.1 fixes the cross-round order of `bus.stagewatch`'s tail; `stage` appends `speakerFrom`,
  `speakerAck`, `speakerAckBy`; `video` appends `pausable`, `stoppable`); a loopback test sets `ControlService.TrustLoopback = false` and
  restores it in `finally`.
- Papers per round: PLAN §NN (design, proof, what it did not do), REVIEW (found, fixed, items answered, "Walk: …"), CHANGELOG naming the
  evidence class, README, REMOTE/COMPANION where the wire or module moved, QUALIFICATION where a platform path moved (Round 89: WebView2 and the
  twin's takeover), OPEN rows added and closed, and the tag row for the round before.
- The closing sentence per round: 89 — no new verb; the stall's log lines and the migration's journal row; 90 — no verb an output shows;
  91 — STAGEWATCH LINK's and STAGE MESSAGE's journal rows (the receipt's stage named), read beside the stage page; 92 — DEVICE lines' receipts.

**Mapper** (`Mapper/CLAUDE.md`, `docs/round3/PLAN.md` §2, §4, §5.7)
- During round 3, §4 forbids every edit to a file a wave does not name; PM-F1 to PM-F4 go only on the owner's explicit exception (PM-X), which
  quotes §4 in full. PM-F5 is papers. Nothing touches `Core/Bus/ShowContext.cs` (the integration pass's), `PlayMap.App/**` or
  `PlayMap.Assistant` (wave-owned): their changes are notes.
- Commit subjects in the tree's `Area: sentence` form (`Engine: …`, `Net: …`, `Core: …`, `Papers: …`); papers as notes for the integrator
  (§5.4 item 7) plus a `docs/round2/CARRYOVER.md` entry per fix.
- A new verb is a catalogue row (G36's, later); a new STATE block has its fixture and card descriptor (none here: G12's fields are inside an
  existing object, and `main.json` holds `links.stagewatch` as `off`). The GL line holds: the timer's pixel test draws through its
  `BackendFixture` leg.
- Both CI jobs green; analyzers apply to tests; no skipped test; no model identifiers.

**Stagewatch** (`Stagewatch/CLAUDE.md`)
- `tools/test.sh` lanes build, unit, module, holo, e2e green; one build at a time; secrets in no log or frame (the 413 path logs no body).
- The rig's `stall` and message signals are the rig's alone (not product routes); WHATS_NEXT and REMOTE name the 413.
- The proposed §13 conformance line goes through spec-1-3's single owner of the master text.

**The round map** (the siblings' numbers → this order)

| sibling unit | its guess | here |
|---|---|---|
| windows-for-peers PT-W1 (held addresses) | Round 90.1 | Round 89.2–89.3 (safety first; no grant needed; ids random, twin carry added) |
| peers-not-screens PT-1, PT-2 | Round 89 | Round 90.1, 90.4 |
| spec-1-3 Patterns unit (reads follow pairing, copy 1.3) | "one round after the master" | Round 90.2, 90.4 |
| cross-app-proposals 89.1, 89.2 | Round 89 | Round 90.3, 90.4 |
| windows-for-peers PT-W2, PT-W3, PT-W4 | Round 90.2–90.4 | Round 92.2–92.5 |
| windows-for-peers PM-W1 (import refusal) | post round 3 | PM-F1, PM-F2 on the exception, else post round 3 |
| windows-for-peers PM-W2 (timer fields) | post round 3 | split: PM-F3 on the exception, PM-P2 later |
| cross-app-proposals unit 12 (G13) | post round 3 | PM-P3 |
| peers-not-screens PM-1 | fix branch | with PM-P1 (G31) |
| peers-not-screens SW-6 (`--node-keys`) | — | before SW-RM |

---

## 10. The critique, answered

| # | the critic's issue | verdict | what changed |
|---|---|---|---|
| 1 | G09 kept id = unsalted SHA-256 → PIN/code brute-force | **accepted** (rig PIN `4321`, `Program.cs:24`; free-text view code) | decision 5: random 60-bit id from `RandomNumberGenerator`, reuse by lookup; the "deterministic" test replaced by a not-derivable fence |
| 2 | "never twinned" breaks failover; file location | **accepted** | decision 8: `KEPT` twin word under `SendSecrets`; file in `BaseDirectory`; `NeverBundled`; OPEN L60, L36 amended; §22 row; twin leg in the fence |
| 3 | G21 refusals stop cues; flash; accepted receipts; clear wipes Stagewatch's own | **accepted**, urgency part adjusted | decisions 12–13: local fallback with words; `slow`/`off` from `flash`; urgency `info` because `StageMessage` has none (`ShowState.cs:845-868`); `Receipt` returns the stage; clear only Patterns' own |
| 4 | 413 then close resets; deferred paths; chunked | **accepted** | §3.3: drain ≤ 1 MiB/10 s, deferred paths exempt, chunked claim dropped, real-socket test |
| 5 | rig cannot run the G21 contract | **accepted**, option name corrected | new SW-RM; the key option is peers-not-screens' `--node-keys N` (SW-6), not `--node-key-file` |
| 6 | `_sight` has two writers | **accepted** (`:377`, `:533`) | decision 2: `_stuck` and `_context` single-writer fields, composed in the getter; an interleaving test |
| 7 | Nodes card; the `:374` assertion; no-frame stuck makes Following true | **accepted** | decision 3–4: `ShowAppCard` red; `:374` kept; a no-frame stuck leaves `Following` false and the desk's timer, tested |
| 8 | G09 misses readers | **accepted**, two parts narrowed | `:1402` log and `_status`, OSC `_lastLine`, the native player (resolve first; `VideoEngine.cs:331` names `Origin`). `backups/` is not on the bundle list (`RemoteAdmin.cs:76-87`); logs are: bundle-time `RedactText`; on-machine copies stay (L40) |
| 9 | Mapper §4 read as allowance | **accepted** | decision 6 and PM-X: an explicit exception request quoting §4 in full; G47's four Content.cs sites sized honestly (before W3, which has not begun); FakeEngineLink and the catalogue row as notes |
| 10 | G35 facts tagged; no HELLO | **accepted** | decision 19: `EncodeFact` untagged, only PlayMap's fact keys (`OPEN 1` not sent); HELLO before the proof; `SUBSCRIBE OFF`; STATE dropped; tests both ways |
| 11 | `pendingSpeaker` meaning; module; DeckSignature | **accepted** | decision 13: meaning kept; `speakerShown`, module variables and feedback; DeckSignature folds the frame's message |
| 12 | G25 governs unmeasured counts | **accepted** | decision 11: bytes only; recent cut to 200 first (Stagewatch's reader keeps 200, `ShowBus.cs:99`); counts left to the integration pass |
| 13 | G34 details | **accepted**, one claim corrected | FOLLOW high; prefix a typing check; `id:` prefix; node re-bind only over TLS with the pin; bind on `POST /api/peers/context` with a key-shaped key. The claim that `/api/context` answers 2xx "without the key" holds only for a non-key value on a PIN-less Stagewatch: a key-shaped unknown key is refused 401 (`WebServer.cs:1073-1082`). Also closed a hole the critic did not name: an unbound key never goes to a line-chosen target. Assistant reach stated (decision 16) |
| 14 | 200 versus 512 characters | **accepted** (golden: verb 307, words 403) | one cap, 512, for verbs and words; tests written to it |
| 15 | Round 90.2 breaks `RemoteTrustTests` | **accepted** | 90.2 names the helpers that pair first |
| 16 | G47 leaves passes in kept items, catalogue words, HLS unproven | **accepted** | engine-start scrub with a journal row; catalogue row words as an integration note; HLS unproven said |
| 17 | G21 message queued behind the beat | **accepted** | decision 14: `SpeakerLane`, latest wins, a test with a delayed context |
| missing | G12's `StateSnapshot`/`StateExplainer` readers | **accepted** | integration-pass notes; in "Left, with why" |
