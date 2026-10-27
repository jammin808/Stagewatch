# What's next — punch list before first release

The repo has the full architecture in place. To get a shippable build you need to do these things, in order. None require new design decisions; all are mechanical.

## P0 — required to build

- [x] **.NET 10 SDK** on the build machine (moved from .NET 8, which leaves support on 10 Nov 2026).
- [ ] `dotnet restore && dotnet build -c Release`
- [ ] **Open `Timer.sln` in Visual Studio 2022** with the "Office/SharePoint development" workload to compile `Timer.PowerPoint`. Sign the manifest the first time.
- [ ] Add a real `stagewatch.ico` at `src/Timer.App/Resources/stagewatch.ico`. WPF will refuse to load if missing.

## P0 — required to ship a real release

- [x] **Replace the build pepper** (done 2026-10-08).
- [x] **Real license signatures**: ECDSA P-256, private key in `%USERPROFILE%\.stagewatch\` (done 2026-10-08). **Back the key up offline.**
- [x] **New owner master serial**, signed; the public dev serial no longer works (done 2026-10-08).
- [ ] **Apply a commercial protector** (Themida, .NET Reactor, Eazfuscator, ConfuserEx successor) to the published `Stagewatch.exe`. The source-level licensing layer is **not** the protection; the protector is.
- [ ] **Code-sign** the exe and the installer with an EV cert.

## Selling (Production tier, added 2.1 — 2026-10-09)

- [x] Tiers and prices in code (`Pricing`): Standard £9, Pro £29, Production £79 (launch £59), event pass £15 / 30 days, updates £20/year; Help → License → Plans.
- [x] KeyGen: `license production <main>,<backup>`, `--updates`, `event-pass`.
- [ ] Decide when the early-adopter (lifetime updates) window closes; from then on issue Production with `--updates 1`.
- [ ] A shop / payment page (e.g. Lemon Squeezy or Paddle as merchant of record for UK VAT) that collects the hardware ID(s) with the order.
- [ ] A sales log: licence id, buyer, tier, discount (charity / church / education 30%, rental 5-pack 20%), event-pass credit, update end date.

## P1 — feature completeness

- [x] Run-of-show scheduler UI (Pro): running order panel, Next/Prev, auto-advance, ahead/behind, CSV/paste import. See REMOTE.md.
- [ ] Multi-output: open multiple `OutputWindow`s on different monitors, each with independent element flags. Engine and view-model already support this; need a small UI for it.
- [x] NDI: native runtime binding, verified end-to-end with an NDI receiver; missing runtime → clear install message.
- [ ] PowerPoint add-in: needs Visual Studio with the Office workload to build (VSTO). Not in the installer until then.

## Show safety (done 2026-10-08)

- [x] Licence hardware ID no longer includes network adapters (docking / venue Ethernet / VPN can't break it).
- [x] Keeps Windows and the displays awake while a show is live.
- [x] Crash / power-cut recovery: the live show is saved every 2 s and offered back on the next start.
- [x] Close guard while a show is live; single instance (a second launch brings the first to the front).
- [x] Sound alerts: choose the output device (not the PA), Test button, off by default on new installs.

## P1 — next

- [x] Show files: Open show / Save show (Ctrl+O / Ctrl+S), `.stagewatch` double-click opens, handed to a running copy.
- [x] 12-hour clock option (display + "2:30 pm" / "7pm" input everywhere).
- [x] Per-user install (no admin rights) or all users; 64-bit install mode. Tested: install, open by file, uninstall.
- [x] Settings → About → Copy diagnostics (never includes PIN or serial); crash log covers all threads.
- [ ] Code-sign the installer and exe (waiting on certificate).
- [ ] Online check-in for Pro: nightly POST to your license server with HWID + nonce; server can revoke.
- [ ] Auto-update channel: check a versioned `update.json` on your CDN once a day.

## 2.0 — shipped (2026-10-09)

- [x] Cue ladders, cue label + frame, colour-blind palette, calm / progress-only speaker clock, honest clock, stable layout, briefing preview, message placeholders, per-cue audio devices and spoken cues.
- [x] Acknowledged cues, department STANDBY/GO pages, green-room calls, pocket buzz (phone PWA), escalation.
- [x] Schedule recovery: kinds, fixed starts, buffers, minimum durations, curfews, delays, catch up, trims; time zones and dated (multi-day) times.
- [x] Overrun forecaster (Monte Carlo, learns from history), rule-based assistant + optional Claude, MCP endpoint.
- [x] PowerPoint slide awareness over COM (no add-in), rehearsal capture and ghost run, speech-to-script pacing (offline).
- [x] Showcaller/Producer page, moderator / backstage / department / interpreter / haptic / audience / rooms pages, Q&A with QR.
- [x] Multi-room beacon + dashboard, Art-Net / sACN / Hue cue lights, LTC out, SNTP clock check, outbound triggers.
- [x] Event journal + planned-vs-actual report (HTML / CSV).
- [x] Companion module 2.0: Showcaller page, layered timer ring, template department keys, value feedbacks, actions with results, action recording, HTTP pages.
- [x] `tools/release.ps1`: version bump + test + publish + module package + installer in one command.

## Next (not in 2.0)

- [ ] Smart-glasses output (Meta Ray-Ban Display / Even Realities) — waiting for a confirmed third-party SDK.
- [ ] Native watch app (the phone PWA covers Android vibration; iOS only flashes).
- [ ] LTC *input* (chase external timecode); USB busylight output.
- [ ] Custom cue-ladder editor in the UI (custom ladders can be added to settings.json today).
- [ ] Whisper engine behind `ISpeechTranscriber` for better recognition than Windows' dictation.
- [ ] Interview UK production companies before pricing a "Production" tier (multi-room, LTC/DMX, reports).

## P2 — polish

- [ ] Replace the placeholder `stagewatch.ico` with branded artwork.
- [ ] Add a small in-app changelog screen.
- [ ] Localise UI (Pro market is mostly English-speaking but corporate AV in DE/FR/ES is nontrivial).
- [ ] Telemetry (opt-in only): how often each preset is used, average session length — to inform what to add next.

## Owner quick path for testing

```pwsh
dotnet build Timer.slnf -c Debug
$env:STAGEWATCH_MASTER = (Get-Content "$env:USERPROFILE\.stagewatch\master-serial.txt" | Where-Object { $_ -like 'SWM2.*' })
dotnet run --project src/Timer.App -c Debug
```

## Windows (2.17) — what this round did not do

- **Not seen on Windows yet.** The Screens window's Windows tab and its right-click menu compile on Linux and have a
  smoke mode (`screens-windows`), but nobody has looked at them in the running app; the Windows job's screenshot is the
  first look. The browser side ran in headless Chromium only: a real TV browser, an OBS or vMix browser input and a
  signage player are untested.
- **No window feedbacks in Companion.** The module's frame doesn't carry windows, so a key can't light when a window is
  holding or lost. Adding a compact `windows` block to the detail frame (at the end) would do it.
- **No QR for a window's address.** Copy address is the way today; a QR on the Windows tab would suit phones and tablets.
- **Window content is a page path.** A saved setup is resolved when the command runs (editing the setup later doesn't
  move the window). Patterns and PlayMap could take a window address as a browser source; nothing there does yet.
- **The fail-safe has no timer of its own on the server.** A window's link loss is seen by the window (6 s); Stagewatch
  shows the lost link in the context once the socket closes.

## Show apps on /ws (2.18) — upgrade notes, the release, and what this round did not do

### Upgrade notes

- **A node key two apps share.** 2.18 lets one node key speak for one app (one node id). No binding was saved before,
  so after the upgrade the first app to use a shared key keeps it. The second keeps following the timer, but its
  context, proposals and collects are refused (403, "this key speaks for patterns 9f8e7d6c — make a key for playmap
  5e6f7a8b"), and on a node link its verdict says the same. Its row in Producer → Show network says why and offers
  **Make a key for <app>**: one press makes a node key that speaks for it and shows the secret once ("Paste it into
  <app> in place of the shared key."). If the wrong app took the key first, **Let a key speak for this node…** on the
  right one's row (or Settings → Remote → API keys → **Let it speak for…**) moves it. One key never speaks for two apps;
  every binding is saved with its key and journalled.
- **The PIN or a control key no longer speaks for an app that has a key of its own** ("patterns 9f8e7d6c speaks with
  its own key"). An app posting with the PIN keeps working while no key speaks for its id.
- **Old keys no longer lock a PC out.** Unknown and revoked keys, and requests with no PIN, aren't counted toward the
  lockout any more; wrong PINs, viewing codes and window addresses still are. The lockout now reads "too many wrong PINs
  — wait a minute". *Since 2.19:* wrong viewing codes, window addresses and crew links are counted apart from wrong PINs,
  each locking a device out of its own kind (REMOTE.md, 2.19).
- **Show apps that don't name their node yet.** Patterns, PlayMap and AGMessage still dial `/ws?cid=…` until each moves
  to its node link (NODES.md). With a node key in the header they already leave Screens and hear no screen command; with
  a control key, the PIN, a read key or no key they are still browser screens to Stagewatch, so `screen … all` reaches
  them.
- **Patterns desks.** A Patterns older than Show Bus 1.1 on another PC is no longer followed (amber: update Patterns),
  and a wrong token on this PC is red: the token never crosses the network.
- **Wire answers that changed:** a context refused for its binding is 403 (was 400), so is a proposal (was 400); a
  collect for something that isn't a node id is 400 (was 403); `POST /api/context` is 405; a body that repeats a key is
  400. REMOTE.md's 2.18 section has them all.

### The release (the maintainer's, on the PC)

```pwsh
pwsh tools\smoke.ps1                                           # the PC checks below
pwsh tools\release.ps1 -Version 2.18.0 -ModuleVersion 2.18.0   # app, module, installer and release date move together
```

2.17 goes first if it is released on its own (`pwsh tools\release.ps1 -Version 2.17.0 -ModuleVersion 2.17.0` on its last
commit, "Papers: windows on the wire and for operators…"); otherwise 2.18 carries it, and 2.17's sections stay as
written. Versions are never edited by hand. `-NoBump` only rebuilds a failed run: it keeps the old release date.

**The PC checks** (`pwsh tools\smoke.ps1`): nothing of the desk's 2.18 work has been seen in the running app yet. The
`panels` mode checks Producer → Show network and saves `producer-show-network` (a key-verified row ✓, a key shared by a
second install ○ with **Make a key for Patterns**, a node key that names no id ○, the headline's "2 unverified on
/ws", the right-click's four items); the `settings` mode checks Remote → API keys and saves `settings-remote-keys`
("speaks for patterns 9f8e7d6c", the buttons in step with the right-click menu, **Let it speak for…**). The Screens
window's line naming the show apps and a claim's "○ claims to be …" row are not in the smoke (its shot has nothing
connected): look at them in the running app with a show app linked.

### What this round did not do

- **Not seen on Windows yet.** The Show network's marks, link lines and right-click, and Settings' key lines and **Let
  it speak for…**, compile on Linux and have UiSmoke checks; the Screens window's show-app line and claim rows compile
  and have none. Nobody has looked at any of them in the running app.
- **The peers don't dial as nodes yet.** The node link is Stagewatch's side only; Patterns, PlayMap and AGMessage adopt
  it in their own rounds (and only then hear their verdict). Until then an app without a node key is a browser screen
  to Stagewatch.
- **A stuck show still has a confident context.** `/api/context`, MCP's `get_context` and the Eye are built on the show
  thread, so while it is stuck they keep its last light, though `/ws` already sends the stuck beat. The fix is a stuck
  overlay on the context (or the context built off the show thread), with a `stall.e2e.cjs` case for it.
- **A valid key still clears its address's wrong-PIN count.** A key holder can send four wrong PINs, one keyed request,
  and repeat. Counting per address and key closes it.
- **MCP reads JSON-RPC leniently.** A repeated key, or a method that isn't a string, drops the connection with no answer;
  the strict parse the node routes use is the fix.
- **A node key may still put any message on the stage.** It runs only `message` and `clearmessage`, but with any
  urgency and flash, so a show app's key can flash the speaker's screen. What a show app may say there is left to the
  stage-message work in 2.20 (message signals and acknowledgements on the rig). *Since then (SW20.10):* the rig keeps a
  message's acknowledgement as the app does, on the app's own signal board: the receipt's `refs.messageId`, the frame's
  `messageId`, `messageAck` and `messageAckBy` and its `signals`, `ack`, a newer message cancelling the old signal and
  `clearmessage` clearing it. The journal keeps the signal, the answer and an escalation; a cancel or a clear writes no
  row, in the app as on the rig. `tests/e2e/message.e2e.cjs` pins the bytes a show app sends and reads (the bytes
  Patterns' fake Stagewatch is to copy), and `docs/NODES.md` §4 says them for show apps. A message signal completes on
  its first answer, so a forwarded `ack … seen` reads `done`, never `seen`: Patterns' design for its Round 93 expects
  `seen`, and is to read `seen` or `done` as the speaker's answer. Who answered keeps the device's address whole: a long
  or made-up name is cut, never the address. An ACK that would change nothing is refused: with a 403 at the door once
  the cue has left the frame's `signals` (cancelled, or 30 s after its answer), and with a 409 receipt (Companion
  `ERROR @id`) while it is listed but already answered. The rig's half runs here; the app's half (before, its receipt
  said applied with no effect) is compiled here and not run, and its walk is the maintainer's (`pwsh tools\smoke.ps1`,
  whose review step ACKs twice and reads the refusal). Still open: what a show app may say there (its urgency and
  flash); a message the app puts up without its signal (a licence with no crew cues) is answered `applied` with no
  `refs.messageId`, which the papers now say means no answer follows, where the doctrine would refuse it or say so in
  the receipt; with crew links required, a show app's forwarded ACK is refused at the door (403, "This screen can't
  confirm cues for speaker…"), and the crew-key policy that would let it through is declined for now (the owner's call).
  The rig's escalation after 20 s is journalled, but no test waits for it; SignalBoard's own checks cover the rule.
- **The rig isn't the interop lane's yet.** Each run makes a new node id, it prints its PIN and the node keys it made
  in its ready line, it has no line to read the lockout and doesn't stop when its input closes. *Since then (2.19's lane
  track):* `--node-id`, `--journal` with `/report`, `--pin-file`, `--secrets-out`, `--exit-on-eof` and the `auth` and
  `exit` control lines (`docs/BUILD.md`, `tests/e2e/rig-faults.e2e.cjs`), and since SW19.13m the `nodes` line, the rig's own
  record of each node link and what speaks for it (`tests/e2e/nodes.e2e.cjs`). The lane that drives it against the real
  apps has its ground and its first cross-app rows since then too (`tools/test.sh interop`, below).
- **A resumed show's earlier journal rows can go missing on Windows.** The app starts the journal's writer before it
  reads the file back (`OpenJournal`); the writer's handle shares reading only, so on Windows the read can fail and the
  report and the context's recent lines lose the rows from before the restart. The rig reads first since 2.19's lane
  track; the app's fix is the same three lines (read the file, then make the journal, then seed it).
- **A stuck show answers `/report` with "no report".** The show thread builds the report, and when it doesn't answer
  within a receipt's wait, `/report` and `/api/report.csv` say 404 "no report", in the app and the rig alike. Telling a
  stuck show apart (503 with words) is the app's and the rig's change together, beside the stuck overlay on the context.
- **Settings' key list doesn't refresh while it is open.** A binding made in the background shows after the next key
  action, or when Settings is opened again.
- **What the Patterns link set aside isn't shown.** Lines a desk sent before pairing are counted, not published; the
  playout links' link record (2.20) is where that count goes.
- **No Show Bus text.** `docs/SHOWBUS.md` doesn't describe node links, the verdict or one key per node yet; those words
  go into Show Bus 1.3 (2.19). Until then REMOTE.md's 2.18 section is the reference, and `tests/e2e/nodes.e2e.cjs` and
  `stall.e2e.cjs` pin the bytes against the rig (`tools/test.sh e2e`; `SW_RIG_ARGS="--node-keys 2" tools/test.sh rig`
  runs a rig with node keys by hand). The interop rows that replay them against the real apps come with the interop
  lane. *Since then:* Show Bus 1.3 §7 and §7.1 have the words, and the lane's row S6.2a replays the stall against
  PlayMap's real link (below); the node-link rows wait for the rig's keys in the lane. *Since then (SW19.13):* the rig makes
  keys in the lane, and Patterns and PlayMap dial it as nodes with their own, over TLS; S2.1, S2.3 and S2.7 hold one key per
  node against the real apps (below), and S2.1 reads PlayMap's paired verdict at its pin. The verdict's own row (a key the
  rig refuses, held red) and S1.6's leg for PlayMap's Show Bus 1.3 reader are SW19.13m's, which waits on Mapper's PM-4 being
  pushed (rule R3). *Since then (SW19.13m):* PlayMap's pin holds PM-4 and PM-5; S2.1b reads the rig's verdict on PlayMap's node
  link both ways, from PlayMap's link and the rig's own record of it (the rig's `nodes` line), and S1.6 PlayMap's 1.3 reader
  (below).
- **The agent brief's doctrine line** still says keys lock a device out; it is the maintainer's to change (keys no
  longer count, wrong PINs, viewing codes and window addresses do). *Since then:* the brief says unknown and revoked keys
  never count; 2.19 splits the count in two, which its line does not say yet (2.19, below).

## Show Bus 1.3 (2.19) — upgrade notes, the release, and what this release did not do

2.19 carries Show Bus 1.3, final (`docs/SHOWBUS.md`). REMOTE.md's 2.19 section has every change on the wire, `NODES.md` is
Stagewatch's node page written from the code, and the contract's §8.1 says what 2.19 does not keep of 1.3 yet.

### Upgrade notes

- **The certificate is replaced once.** The first start of 2.19 replaces a certificate an older Stagewatch made (it spanned
  826 days; 1.3 and Apple devices allow 825). Presenter PCs and apps that pinned the old one say CERTIFICATE CHANGED once, and
  devices that trusted it warn: compare the first 8 hex with Settings → Remote and accept it on each. Upgrade off show day.
- **No hello while Remote is set to this PC.** A Stagewatch whose Remote keeps everything on this PC now says no hello at all
  (Show Bus §2): an app on this PC that found it by its hello links it by its typed address, `127.0.0.1` and the port (9924
  unless set otherwise), and a presenter PC on this PC types `127.0.0.1`. With other devices allowed, the hello names Companion
  and OSC only while they are bound to the network too, and no longer carries a stray `HttpPort` field, which no reader read.
- **A copied install shows itself.** Two Stagewatches with one id (a settings folder copied to a second PC) are amber on every
  other Stagewatch's Show network, naming both machines, and each says "another machine says it is this Stagewatch". No button
  gives an install a new id yet: close Stagewatch on one of the two, set `"nodeId"` to `""` in
  `%APPDATA%\Stagewatch\settings.json`, and start it; it makes a new id, and other apps hear it as a node they have not met.
- **Wrong passes count apart.** Five wrong viewing codes, window addresses or crew links in a minute lock that device out of
  view passes only; the PIN and keys still open its operator pages, and a right PIN or key no longer clears that count. The show
  PC is not exempt: a stale crew tab can turn away that PC's own window sources (vMix, OBS) for up to a minute. A crew link
  replaced by **New crew links** counts as a wrong one: reopen such pages from their new links.
- **The context's shape.** `/api/context` and MCP's `get_context` list panic, air and transport commands first, cut long fields
  with "…", and stay within §14's sizes; one that has to leave something out says what in a grey `context:trimmed` item. A
  reader that relied on the order of Stagewatch's commands sees a new one.

### The release (the maintainer's, on the PC)

```pwsh
pwsh tools\smoke.ps1                                           # the PC checks below
pwsh tools\release.ps1 -Version 2.19.0 -ModuleVersion 2.19.0   # app, module, installer and release date move together
```

One release per train (the owner's answer for 2.18 and 2.19): 2.18 is released first, on its own last commit, as its section
above says (and 2.17 before it, if it is released on its own); 2.19 is released from the commit that carries this section. The
Companion module has no change of its own in 2.19; it takes 2.19.0 with the app. The lane track's commits (the rig's options,
later the interop lane) need no release of their own. Versions are never edited by hand.

**The PC checks** (`pwsh tools\smoke.ps1`): no smoke check was added for 2.19's desk work, and none of it has been seen in the
running app. The smoke's shots are the first look, with nothing in them asserted: `panels` (Producer → Show network's hello line
and the counts line under it), `settings` (Settings → Remote, where the certificate's lines are, its fingerprint in groups of
four, when the smoke's Stagewatch has one) and `slides-agent` (the agent's target rows). An amber hello, a conflict row and the status line's certificate words need a
running app on a network: two copies of one install for the conflict, an old certificate for the replacement.

### What this release did not do

- **Not seen on Windows yet.** The Show network's hello line and its amber, the conflict rows and the counts line, the
  certificate's lines in Settings and on the status line, and the presenter PC window's words compile on Linux; nobody has looked
  at them in the running app.
- **Nothing speaks PeerLink yet.** Stagewatch's PeerLink core, the vectors and the scripted peer run in the suite only: `/ws` node
  links still prove a bearer key or the operator's credential, the Patterns link still pairs by `NONCE` and `AUTH-PROOF`, the
  hello carries no cap `peerlink`, and PeerLink's wrong proofs (13.5.6) count nowhere. The playout links and `/ws` take LINK in
  later releases (Show Bus §8.1). *Since then (SW20.4a, SW20.4b):* the Patterns link runs on the PeerLink core as a level-0
  connector (it still offers no LINK, SW23.1): request ids with the process's `inst`, 32 commands in flight, 13.12.3's outcomes,
  matched pongs, the jittered redial; each connection writes on a lane of its own, so the show thread and the link's checks never
  wait on the socket, and a command still on the lane when the link drops, or after 30 s, is not sent and never written; a refused
  proof's hold is kept with its end in `playout-links.json` beside the settings, for the token it held — the link holds it at once
  and a lane of the file's own writes it, off the show thread — so a restart keeps it (for a minute at most) and a token changed
  in Settings ends it (an older Stagewatch ignores the file); and the show log has a row when a command's outcome first settles,
  filed against the item it was asked for, one more when an unconfirmed one is answered after all, and one per change of the
  link's state with the desk's address, repeats — a redial going round, a flapping link — folded into a summary row a minute.
  `PatternsLink` is gone. SW20.4b closes nothing of G48: its Stagewatch half — the link's record in the Eye, the AI's context and
  the copilot's facts; the refusals by a clip's role and key, pausable and stoppable; the kind profiles (patterns, playmap) and
  the local refusals they carry (a sync follower); and the asks after pairing (`WHOAMI`, and `CONTEXT` at once) — is SW20.5's, and
  `INDEX` and `CUE LIST`, which give running-order cues their ids, SW20.7's. Left: *Link again now* (13.16.6), which would end a
  hold before its minute, is on no surface yet — the desk's buttons and menus are SW20.11's; the link's record, not yet its own
  light and words, reaches the Show network, the context's `link:patterns`, the AI's facts and Companion with SW20.5; the desk's
  new status and log words, and its close that runs its queue up to the links' last rows, compile here and are not walked on
  Windows (`pwsh tools\smoke.ps1`).
- **Find still listens on Patterns' beacon.** Drop UDP 9700 from Find once Patterns' 9928 walk (its OPEN row L47) is filled:
  the release after that walk row is filled takes it. Find already reads a hello's `ports.wire` (else the top-level `wire` older
  senders give) and hears PlayMap's hello as well as Patterns', so a desk that names its wire only in `ports` is listed; Settings'
  *Patterns video* box asks Find for Patterns desks only. `NodesDocTests` holds this row here while Find listens on 9700.
- **No button gives an install a new id** (the upgrade notes say how by hand). The desk's conflict words tell the operator to.
- **A conflict reaches the desk, the advice and the AI's facts, not the context or Companion.** By §6, an id two machines say has
  no `peers` row in Stagewatch's context, so other apps reading it and MCP's `get_context` do not list it, and the Companion
  module reads no peers at all.
- **Stagewatch dials no link by hello.** §2's "dial 127.0.0.1 first" for a peer heard on this machine is not done: its one link,
  to Patterns, is a typed address.
- **As a reader of contexts, Stagewatch keeps 2.18's limits**: a context's first 64 items and 64 commands, a verb cut at 80
  characters, no links read, and an over-long post closed unanswered rather than refused with 413 (§8.1, with what ends each).
  *Since then (SW20.9):* a body over 64 KiB, a context's or any other's, is answered `413` with the length it declared, before
  any route, credential, lockout count, Require HTTPS redirect or `/ws` upgrade sees the request. A body of up to 1 MiB is read
  and dropped first, for at most 10 s, so its sender reads the answer, not a reset; one declared longer, or one still coming
  when the 10 s are up, is answered at once and closed, and its sender may see the reset. Nothing of it is kept or logged. A
  `Content-Length` that is not one decimal number is answered `400` the same way, never read as no body. Uploads and a deck's
  script keep their own caps, and past them still close unanswered. The other three limits are SW21.1's, whose byte caps count
  on this 413. A poster reads it as 13.12.3's table has it, refused with the body's words; fitting PlayMap's posted context to
  64 KiB is PlayMap's own (its PM-8). Left as they are: a sender holds one of the 64 connection slots for up to 15 s per such
  request (5 s for its head, 10 s draining), with no cap on drains at once until a rig measures slots running out; a body sent
  chunked, with no `Content-Length`, is still read as none; and 13.6.9's mark of a key seen in clear (SW22.1b) must stand above
  both these answers and the Require HTTPS redirect, which all return before any credential is read. The 413's words are
  ASCII, so the Slides agent's status, which matches the raw answer, shows them as sent; it still shows other words' em dash
  as its JSON escape, a fix of its own.
- **A non-English show fills its context sooner.** The context is written with the default JSON encoder, which writes each
  character outside ASCII as a six-byte escape, so such a show reaches 64 KiB with about a third of an English show's text. A
  relaxed encoder waits for every reader's parser to be measured with it.
- **The presenter PC's choice of certificate has no test of its own**: it needs a TLS Stagewatch on the bench.
- **LB-1's Windows rows are not run** (below): 1.3 names no carrier that finds apps on one machine with no network.
- **The interop lane is a track of its own.** The rig's options for it landed (2.18's list, above), and so has the lane's
  ground: `tools/test.sh interop` (not a default lane) builds Patterns' bench host, PlayMap's node and AGMessage at the
  pushed commits `tools/interop/siblings.json` pins, in clones of its own, and runs them with the rig on two topologies,
  each inside a user, network and PID namespace of its own that ends with its run: a one-kernel LAN, each app a host of its
  own on one switch, and one show laptop, the four apps on one host with its Wi-Fi up, typed 127.0.0.1 as the maintainer
  types it. Its rows, each added once it passed against the real apps, hold that each app is ready in its own namespace
  (S0.1); that, within 5 s of the last app ready, Patterns hears PlayMap and AGMessage, PlayMap hears Patterns and
  AGMessage, and AGMessage hears Patterns, each from the address its hellos came from wherever the app says one (PlayMap's
  NODES says none for the Patterns it follows once it holds that Patterns' context) (S1.1-S1.3, and S1c.1 on the laptop,
  where every hello comes from the laptop's own addresses, its Wi-Fi's by the broadcast or 127.0.0.1 where a loopback copy
  lands, and every link to the rig is live at 127.0.0.1); that PlayMap follows Patterns' clip and blackout, paired by proof,
  on the LAN and at 127.0.0.1 (S4.5, S1c.2); that unknown words are refused by Patterns, PlayMap and the rig (S7.3's first
  three); that a stuck Stagewatch leaves PlayMap's link live and showing no time, for Stagewatch's own reason, and the rig's
  running time again once the show turns over (S6.2a); that the rig killed and restarted, or its cable pulled and put back,
  lets every link go, then gone, and brings every link back (S6.1, S6.1b); and that no secret of the run's register (the
  PIN, each app's token, AGMessage's admin token and the context token the lane learns from its admin) is in any log, frame,
  STATE, context, report or capture a run keeps (S7.1). After every row both of the rig's lockout tables (wrong PINs; wrong
  view passes) hold nothing against any address of the LAN, the switch's and every app's, nor, on the laptop, against
  127.0.0.1 and the laptop's own address; a control from those addresses shows each table's reader sees them. Each
  topology's matrix (`artifacts/interop/<topology>`) says what each row read and how long it took. Where the kernel gives no
  such namespaces, a sibling is absent or a pin cannot be fetched, the lane says "not run", never passed. CI runs it at the
  pins, and nightly at the siblings' heads (below). `docs/BUILD.md` ("The interop lane") says how it runs: the four apps,
  its clones and pins, its topologies, its secrets, its rows and what a run leaves, and what it proves and what it does not;
  the suite's `InteropDocTests` hold its rows table to the drivers, and `tools/test.sh help` prints every lane and every
  variable a person sets for the lane.
- **The rig on the show network, in the lane (SW19.13).** On the LAN the rig runs as the app runs on a show laptop: on the
  Show Bus port (`--bus 9928`), serving its certificate beside plain HTTP on its one port (`--tls`, in a folder of its own in
  its home, so a restart serves the one its peers pinned) and making three node keys, one handed to Patterns and one to
  PlayMap in their own settings, one to nobody; AGMessage is given none. The rows read PlayMap's link with the rig's
  certificate pinned and Patterns' link carried over TLS with its key; that neither app ever sends its key in clear off its
  own machine is held by its own tests (Patterns' Round 89.5, PlayMap's PM-2), not by the lane. The rig's `heard` line reads
  its hello table, hellos alone, so a row of the rig hearing an app is that app's hello, never its node link or its posted
  context. The rows, each added once it passed: the rig's hello table lists Patterns, PlayMap and AGMessage, each from its own
  address, live and unmarked, and its context's peers each live, AGMessage's row its hello's alone (S1.4), and every app hears
  the rig, kind `main`, its node id, from the rig's address (S1.5), each first read within 5 s of the last app ready (a row
  that only gets to read later fails rather than claim it, S1.1–S1.3 alike); read every 500 ms for 20 s, the rig's hello table
  and context, Patterns and PlayMap each list every show app they hear once (S1.6, today's readers on the LAN); the rig's row
  for PlayMap and for Patterns says that app's own context light and headline, proved by its own key on its node link, and a
  change of it within 9.5 s, there and back (S2.1: PlayMap's link paired with the rig's certificate pinned, the change
  Patterns' blackout, which PlayMap follows; S2.3: Patterns' link over TLS with its key, the change a deck pairing with
  Patterns and leaving); Patterns' STATE says the rig's own headline, and its change (the running order's next item and back)
  within 9.5 s (S2.4); PlayMap's, Patterns' and the rig's own contexts are each under 64 KiB as carried (S2.6, the sizes
  recorded); and a node key cannot speak for another node: with Patterns' and PlayMap's keys bound to their own nodes and the
  third to none, Patterns' key posting PlayMap's id and AGMessage's, PlayMap's posting Patterns' and the free key posting
  PlayMap's are each refused 403 with Stagewatch's words; no forged word is in the rig's context read at once, in the first it
  built after the posts (it builds one a second; AGMessage posts no context, so a forgery applied for its id would still be
  there) or in any read after; its rows stay each app's own, every key stays bound as it was, and no address is counted
  (S2.7). The certificate's password, kept unsealed off Windows, is in the sweep's register (S7.1). The pins are unchanged:
  Patterns' newest pushed commit is still 5a8ad8f (Round 91.6; nothing of Round 92 is pushed), and PlayMap's eda7cd4 already
  holds its node link (PM-5). Departures from the design, on purpose: S2.7 uses Patterns' and PlayMap's keys where the design
  names AGMessage's (AGMessage is keyed by its own rows), and reads the forgery in the rig's context alone: the rig keeps no
  peer's context in its journal or report, so neither could show one, and a `Peer` row in the report comes with 2.21's report
  work.
- **PlayMap's legs at its new pin (SW19.13m).** PlayMap's pin moves to 952940b5, Mapper's newest pushed commit, which adds its
  Show Bus 1.3 copy and hello reader (PM-4) to the node link and paired verdict eda7cd4 already held (PM-5); every row before
  passes at it unchanged. Two legs are added, each once it passed against the real apps:
  - S1.6 reads PlayMap's STATE peers and its context's peers beside its NODES, each listing every show app once for 20 s; at
    every read the rig's row is live and unmarked, heard at the rig's one address, and the context (which lists no id in
    conflict, §6) lists it. A control after the watch makes "unmarked" mean something: the driver says hellos of an id of its
    own from two senders in turn (inst A, B, A…), from a port of its own to PlayMap alone, and PlayMap's STATE shows that id
    amber in its mark's words, naming both senders at the switch's address, while its context lists no row for it and the
    rig's row stays unmarked. No other app hears those hellos; the id is left to go stale and be forgotten.
  - S2.1b reads the rig's verdict on PlayMap's node link from both ends: PlayMap's link, and the rig's own record of it, read
    by the rig's `nodes` control line (each node link on its `/ws` by the id it named, what speaks for that id, key, operator
    or none, the key that admitted it by its id, and the rig's words for one it verifies none; `tests/e2e/nodes.e2e.cjs` and
    `rig-faults.e2e.cjs` hold the line). PlayMap's link is first taken off (`LINK STAGEWATCH OFF`, until STATE says off and
    the rig holds no node link for PlayMap, so a refused link is told from no link at all). Then given the free key, which
    speaks for no node (`LINK STAGEWATCH <rig> <key>`), PlayMap dials the rig and is refused in the rig's own words (`playmap
    <id> speaks with its own key`) within 5 s, and the rig's record has that key's link verified none in the same words. From
    that answer on its link is never paired, though live: refused through a 7 s watch past its next context post, amber in
    its own context, the rig's row for PlayMap there with no proven link at every read, and never a proven link in the rig's
    record. Given its own key again it is paired again within 5 s, the rig's certificate still pinned, its context's link
    item green, the rig's record its key's link alone, verified key, and the rig's row its key's on its node link (the key's
    binding is to playmap and PlayMap's id, so paired is the link dialled as `node=<its id>&t=playmap`). Every key stays
    bound as it was, no address is counted, and S7.1 finds neither key anywhere the run kept. The two key lines are said as
    PlayMap's operator says them at its own machine: a helper started in PlayMap's namespace
    (`tests/interop/apps/playmap-operator.cjs`) reads each key from an owner-only file of the run's secrets folder, pairs by
    proof and says it to PlayMap's wire at 127.0.0.1, so no key crosses the run's LAN. PlayMap answers each with a receipt
    (`accepted`), and the row reads what the link did from STATE. A row that fails partway keeps what it read in its matrix
    row, gives PlayMap its own key back and waits up to 5 s for it to pair again, saying how that went on standard error, so
    the rows after it fail for their own reasons.
  - For PlayMap's owner, found while building S2.1b and read by no row: `LINK STAGEWATCH OFF` answers `{"stage":"accepted",
    "effect":"LINK STAGEWATCH started"}`, the words of a start for a stop (its router words every network line's receipt
    that way); and a `LINK STAGEWATCH` or `LINK PATTERNS` line carrying a key or a token is taken over plain TCP from
    another machine, accepted and recorded nowhere as seen in clear, where plain `AUTH` from another machine is refused and
    its token reported, and `CREDENTIAL SET` is this machine's only. The lane says its key lines at 127.0.0.1 and depends on
    neither.
  - Departure from the design, on purpose: the design names a refused key "held red". Stagewatch keeps a socket whose key it
    refuses, for the public frame (§7.1: no socket is closed for a key reason), so PlayMap's link stays live, not paired and
    amber; PlayMap says held (red) only while a refusal's hold runs with no connection up, which the watch accepts should the
    connection drop. Not read: PlayMap hearing one id at every address it says hellos from (a node heard at two addresses is
    one peer), since on the LAN each app has one address and on the laptop the rig is off the show network until SW-R7.
- **Interop rows that wait on another app.** Each is named here and not in the lane, which holds only rows that pass:
  - S2.2 and S2.5 (AGMessage's context reaching the rig, and AGMessage reading the rig's) are AGMessage's rows (SW-INT-AG):
    they come with the lane giving AGMessage a key of its own (0.5.0 posts no context without one).
  - The show laptop topology still runs the rig off the show network and gives no app a key: the rig on the laptop and nobody
    locking the laptop out (S1c.3), and the offline laptop (S1b.1), are SW-R7's, after F1's fix.
  - S6.4 (Patterns killed) and S6.7 (Patterns' cable pulled) wait on PlayMap: at eda7cd4 (the pin then; not run again at
    952940b5, the pin since, whose diff touches no code of PlayMap's Patterns link) its air stays blacked out by a Patterns
    that is gone (`air.blackout` and
    `air.patternsBlackout` still true 22 s after the desk was killed or its cable pulled, the link down and still said
    `paired`), until that Patterns comes back and says otherwise. Whether a gone
    desk's blackout is held or let go is PlayMap's owner's call; the rows assert the design's "let go".
  - S6.2c (AGMessage shows no time while Stagewatch is stuck) waits on AGMessage: its link's pongs keep it fresh through the
    stuck beat, so it stays connected and green with the last time.
  - Discovery on the offline laptop (S1b.2, every app hearing every other with no network) waits on Show Bus 1.4's carrier
    on one machine (spec-1-3's decision 5), which lab line LB-1 measures first.
  - PlayMap proposing to the rig's desk (S3.6) waits on Mapper's PM-P3, PlayMap's assistant on Stagewatch's approval desk,
    after the owner's decision 7.
  - Each headless app's own context items (S8.0, a measurement and not a row: PlayMap's, Patterns' and AGMessage's items
    with their lights, as each one's own context says them) come with the doors rows they serve, with 2.21's preflight
    (SW-G49c): S8.1 (ready with everything up) can pass only once S8.0 shows no app with a red item of its own.
  - The design's later rows come with the work they read, each added once it passes: the VT rows (S4.1–S4.4) with 2.20's
    playout links; Patterns' desk and stuck rows, the doors and nobody locking anybody out (S3.3, S3.4, S6.2b, S6.3,
    S8.1–S8.4, S7.4) with 2.21's desk and preflight; windows on Patterns and the MCP desk rows (S5.1, S3.5, S3.7) with
    2.22's windows for peers, Xvfb, Mesa and Playwright joining the job with them; PlayMap's windows rows (S5.2, S5.3) with
    its PeerLink work; and AGMessage's desk, stuck and re-offer rows and one contract across the four copies (S1.3b, S3.1,
    S3.2, S3.8, S6.5, S7.2) with AGMessage's next releases. A release waits only on the rows in the lane.
- **The agent brief's doctrine line** spoke of one lockout; since 2.19 there are two counts (wrong PINs; wrong view passes).
  *Since then:* the maintainer accepted the drafted words (below), and the brief says so.

### The interop lane in CI — what it does, and the maintainer's actions

- **CI's interop job** (`.github/workflows/ci.yml`, job `interop`) runs on this repository's pushes to main, its tags, its own
  pull requests and its runs by hand. It checks out Patterns, Mapper and AGMessage at the commits `tools/interop/siblings.json`
  pins (with every branch, so the lane sees each pin on one of its repository's branches, as on the bench), runs
  `tools/test.sh interop` with `SW_INTEROP_REQUIRED=1` (a lane that could not run fails the job; the lane first checks its own
  scripts, `tools/interop/selfcheck.py`: pins.py, build.sh at pins and at heads with a stub dotnet, summary.py and publish.py
  on fixtures of their own) and writes the lane's summary to the run (each build, and each topology's rows, the failed ones
  with their words). **A GitHub Release now waits for it** beside the three other jobs.
- **What the run's artifact holds** (`interop-<commit>`, kept 30 days; the heads run's `interop-heads-<run>`, 14 days):
  `tools/interop/publish.py` copies `builds.json` and each topology's `matrix.json` and `matrix.md`, and, only for a topology
  whose sweep (row S7.1) passed in that run, its logs (`run/logs`: each app's output and error, the wires' lines), its captures
  (`run/captures`: what the driver read from the rig's doors) and each app's journals and logs from its home (`*.log`,
  `*.jsonl`, `*.csv`): every one of them was read by that sweep, after the apps stopped and the files each keeps a secret in by
  design were removed, and held none of the run's secrets. A topology stopped before its end (a timeout, a crash: the apps'
  secret files were never removed) or whose sweep found a secret keeps its run on the runner, and the artifact's README and the
  run's summary say so. Never in it: the clones and their builds, the run's secrets folder, an app's settings, keys, recovery
  record or media.
- **The job's reach.** A fork's pull request gets no secret, so the job does not run for one, and the Linux job's summary says
  "interop: not run". The siblings are read only with `SIBLINGS_READ_TOKEN`, only in their checkout steps, which keep no
  credential, so no build of a sibling and no app of the lane can read it. On the runner, and only there, the job loads
  `bridge` and `veth` and lifts Ubuntu's AppArmor confinement of unprivileged user namespaces
  (`kernel.apparmor_restrict_unprivileged_userns=0`), as the owner's answer S-interop-ci allows.
- **The nightly heads run** (`.github/workflows/interop-heads.yml`) runs the same lane every night at 03:23 UTC with each
  sibling at the head of its default branch, or, run by hand from the Actions tab, at the branch or full commit named for it
  (`SW_INTEROP_HEADS=1`: `tools/interop/build.sh` holds each head to the pins' rule, pushed, and says how far it is from its
  pin). Its summary is the drift report: each head, how far it is from its pin, and which rows fail there. It gates nothing and
  moves no pin; a red night says the heads no longer run what the pins prove, or the lane could not run there. Today Patterns'
  main has no bench host (it is on the branch its pin is on), so a night at the default branches is red, and GitHub mails the
  maintainer a failure notice each night, until that branch is merged: that red is the drift report, never a green run over
  failed rows (the maintainer can disable the workflow from the Actions tab meanwhile); a run by hand can name the working
  branches. Schedules and runs by hand start only once this file is on the default branch. Measured on the bench (2026-10-26,
  checkouts made as CI makes them): at the default branches the builds stop at Patterns (its main is 1 commit of its own and
  276 of the pin's history away from its pin; PlayMap's main is 8 commits behind its pin; AGMessage's 14 after it), and the
  report says so; at the working branches (Patterns and PlayMap at their pins, AGMessage's 0.5.1 head 11 commits after its pin)
  S6.1, S6.1b and S1c.1 fail, because 0.5.1's link to the rig says `viewing`, not `live`, without a key from the rig.
  AGMessage's pin moves past 0.5.0 only with the lane giving it a key (the rows SW19.13 carries).
- **Owner action: make `SIBLINGS_READ_TOKEN`.** It does not exist yet. Until it does, the interop job and the heads run fail on
  their first step and say why, and no tag publishes a release. On GitHub: Settings → Developer settings → Personal access
  tokens → Fine-grained tokens → Generate new token; resource owner jammin808; repository access "Only select repositories":
  Patterns, Mapper and AGMessage; repository permissions: Contents, read-only (Metadata, read-only, comes with it), nothing
  else; an expiry the maintainer renews (an expired token fails the siblings' checkout steps). Then, in Stagewatch: Settings →
  Secrets and variables → Actions → New repository secret, named `SIBLINGS_READ_TOKEN`. The three repositories are public
  today; the job reads them only with this token all the same, so it keeps working if they turn private.
- **Owner action: the runner change.** The owner's answer S-interop-ci lets the two workflows load `bridge` and `veth` and
  set `kernel.apparmor_restrict_unprivileged_userns=0` on GitHub's hosted runner, which is thrown away after its job: there
  is nothing to change by hand, and the first run (below) shows the step "User namespaces for the lane (this runner only)"
  passing. The change is for a throwaway runner alone. It is a setting of the whole machine and outlives the job, so a
  self-hosted or long-lived runner never takes it from a workflow: moving either workflow's `runs-on` to one removes that
  step first, and that machine's owner decides whether its users get user namespaces (until they do, the lane says "not run"
  there, which `SW_INTEROP_REQUIRED=1` turns into a failure). The bench needs no change: its kernel gives the lane's
  namespaces to its user, and the lane changes no setting of any machine.
- **Owner action: the first run.** CI runs on pushes to main, tags and pull requests, so pushing this branch alone runs
  nothing. Once the token exists, run it by hand on the branch (`gh workflow run ci.yml --ref <branch>`; main's `ci.yml`
  already takes runs by hand, and the run uses the branch's own workflow), or open a pull request from it, and read the run for
  its head commit:
  `gh run watch --exit-status $(gh run list --workflow ci.yml --commit <head commit> --limit 1 --json databaseId -q '.[0].databaseId')`.
  Pushed is not checked. That run is the first time the lane meets a GitHub runner (the sysctl, the bridge and veth, the Node
  AGMessage needs fetched and checked). On the bench the lane passed every row with the siblings checked out as the job checks
  them out (from GitHub, every branch, detached at each pin), so what that run proves is the runner; until it is green the
  workflow is checked with a YAML parser and actionlint only.
- **When the gate starts.** A tag runs the workflow of the commit it names, so a release waits for the interop job from the
  first tag on a commit that carries this job; a tag on an earlier commit (2.19's, if it is made on the commit its section
  names) is released without it.
- **The agent brief's lines** (the owner's answer S-brief: the maintainer writes them, or accepts them as drafted; no unit
  edits the brief). *Done:* the maintainer accepted all four as drafted, the doctrine line in its S-D0 form, and `CLAUDE.md`
  now carries them: the loop's `tools/test.sh interop` line; **Fail closed on the wire** with its two counts kept apart and
  Show Bus §1 rule 5's two carriers for a view pass; the CI paragraph's `interop` job and `interop-heads.yml`; and the
  interop lane's row in *Where things live*.
- **What the job does not do yet.** No NuGet cache: each run restores the three builds' packages afresh (a few minutes). No
  browser: Xvfb, Mesa and Playwright join with the rows that need one. Its actions are named by version, as the rest of
  `ci.yml`, not by commit.

## Lab line LB-1 — finding apps on one machine with no network (Show Bus 1.4's carrier)

Show Bus 1.3 names no carrier that finds apps on one machine with no network (`docs/SHOWBUS.md` §2): there a typed
address (127.0.0.1 and the port) links them, in each app's link settings. `tools/BusCarrierProbe` measures which carrier
would. It binds six listeners on UDP 9928, in this order, each with SO_REUSEADDR, as the apps bind theirs: 1 and 2 on
every interface (as Stagewatch and AGMessage listen, PlayMap on any bind but loopback, and Patterns with its wire on
every interface), joined to 239.255.99.28 on 127.0.0.1; 3 on every interface, joined to nothing, as every reader is
today; 4 on 127.0.0.1, as PlayMap listens when its bind is loopback, and 5 on 127.0.0.1, as Patterns listens when its
wire is bound to loopback (Patterns binds its hello listener to the wire's bind address), both joined; 6 on one
adapter's own address, as Patterns listens when its wire is bound to its control network, joined, and left out with
every adapter off, as no node can bind one then. It sends five datagrams to each of 127.0.0.1, 127.255.255.255 and
255.255.255.255 from an unbound socket that follows the route, as Stagewatch's hello does, and to 239.255.99.28 out of
127.0.0.1, and prints who heard what as Markdown, with the listeners in one process and then each in a process of its
own. A carrier for 1.4 reaches every listener a node using it would be: all six (five with every adapter off), or for
multicast every one that joins.

Before each layout binds and while it listens, the probe reads the machine's table of UDP sockets on the port (every
process's, the operating system's own list). A socket there that is not the probe's — an app that listens without a
word, as a node bound to loopback does, since §2 has it say no hello — or a datagram from this machine that is not the
probe's prints a △ line, leaves that layout's every-node column `unclaimed`, and the run exits 1. A listener that stops
before the probe is done with it (its socket failing receive after receive, or its own process reaching the limit it
reckons from the run's numbers) is shown stopped, with why, and the carriers it counts for read `not measured`. The
suite's `BusCarrierProbeTests` hold these rules, and run the probe on a free port clean, beside a silent socket on
127.0.0.1 and beside an app sending there. 1.4 names the carrier from the Windows records; until they are in, nothing
changes.

### The run (the maintainer's, on the PC)

```pwsh
dotnet run --project tools\BusCarrierProbe -c Release     # close Stagewatch, Patterns, PlayMap and AGMessage first
```

Twice: with the network up, and with every adapter off — Wi-Fi, Ethernet and the virtual ones (Hyper-V, WSL, VPN).
Close Stagewatch, Patterns, PlayMap and AGMessage first; on an installed PlayMap node `PlayMap.Supervisor.exe stop` stops
its engine and holds it until `resume` (the service starts it again otherwise; stopping the service alone leaves the
engine running). The first run may bring Windows' firewall prompt for the probe while it runs: allow it, check that the
network's profile is Private (Settings → Network & internet → the adapter → Network profile type) or that the rule it
made covers the profile in use, and take that first run again, as its rows were measured while the prompt was open. Its
`network` line says which condition it measured and names any adapter still up. A run with a △ line, or with a cell
reading `stopped`, is taken again; only a run that exits 0 goes in. Each run's Markdown goes below as it printed.

### The records

| condition | where, when | what it says |
|---|---|---|
| Linux, network up | the cloud bench: Ubuntu 24.04.5, .NET 10.0.13, 2026-10-26 | no carrier reached every node |
| Linux, every adapter off | the same, in a network namespace holding only `lo` (`unshare -n`, `lo` brought up) | no carrier reached every node |
| Windows 11, network up | the maintainer's PC | **not run** |
| Windows 11, every adapter off | the maintainer's PC | **not run** |

**Linux, network up** (three runs, each with the listeners in one process and apart, every one exiting 0 with no △
line; the six layouts alike but for which 127.0.0.1-bound listener took the unicast: 5 in five of them, 4 in one, as
here, never both; listener 6 was on eth0's address):

| carrier | sent | 1: 0.0.0.0, joined | 2: 0.0.0.0, joined | 3: 0.0.0.0, not joined | 4: 127.0.0.1, joined | 5: 127.0.0.1, joined | 6: an adapter's address, joined | every node |
|---|---|---|---|---|---|---|---|---|
| 127.0.0.1 | 5 | 0 | 0 | 0 | 5 from 127.0.0.1 | 0 | 0 | no |
| 127.255.255.255 | 5 | 5 from 127.0.0.1 | 5 from 127.0.0.1 | 5 from 127.0.0.1 | 0 | 0 | 0 | no |
| 255.255.255.255 | 5 | 5 from eth0 | 5 from eth0 | 5 from eth0 | 0 | 0 | 0 | no |
| 239.255.99.28 via 127.0.0.1 | 5 | 5 from 127.0.0.1 | 5 from 127.0.0.1 | 5 from 127.0.0.1 | 0 | 0 | 0 | no |

Listeners: 1 joined · 2 joined · 3 joins nothing · 4 joined · 5 joined · 6 joined.

**Linux, every adapter off** (two runs, each in both layouts, every one exiting 0 with no △ line; listener 4 took the
unicast in all four):

| carrier | sent | 1: 0.0.0.0, joined | 2: 0.0.0.0, joined | 3: 0.0.0.0, not joined | 4: 127.0.0.1, joined | 5: 127.0.0.1, joined | 6: an adapter's address, joined | every node |
|---|---|---|---|---|---|---|---|---|
| 127.0.0.1 | 5 | 0 | 0 | 0 | 5 from 127.0.0.1 | 0 | none | no |
| 127.255.255.255 | 5 | 5 from 127.0.0.1 | 5 from 127.0.0.1 | 5 from 127.0.0.1 | 0 | 0 | none | no |
| 255.255.255.255 | 0 · NetworkUnreachable | 0 | 0 | 0 | 0 | 0 | none | no |
| 239.255.99.28 via 127.0.0.1 | 5 | 5 from 127.0.0.1 | 5 from 127.0.0.1 | 5 from 127.0.0.1 | 0 | 0 | none | no |

Listeners: 1 joined · 2 joined · 3 joins nothing · 4 joined · 5 joined · 6 none: no adapter is up, so no node can bind
one's address.

**What Linux says.** No one carrier reaches the 127.0.0.1-bound listeners and the ones on every interface together, and
the four together do not either: the 127.0.0.1-bound listener the unicast passed over, and the one on eth0's address,
heard none of them. A unicast to 127.0.0.1 goes to one 127.0.0.1-bound listener alone: two nodes bound there share
that one address, so a copy sent to 127.0.0.1 reaches one of them, never both. 127.255.255.255 and the group on loopback
reach every listener on every interface and neither 127.0.0.1-bound one, joined or not; the group also reached listener
3, which joined nothing (Linux hands a socket bound to every interface the groups any socket on the machine joined).
255.255.255.255 needs an adapter up, arrives from that adapter's address, and with none fails to send. The listener on
eth0's own address heard nothing at all: Linux gives a broadcast or a group's datagram only to a socket bound to every
interface or to that very address, and a unicast only to its own. So on Linux a 1.4 carrier would need every node's
hello listener on every interface (PlayMap's and Patterns' narrower binds moved, for the hello alone), whichever of
127.255.255.255 and the group it named. These runs had SO_REUSEADDR and SO_REUSEPORT both: on Linux .NET sets the second
with the first, as in Stagewatch, Patterns and PlayMap, and a unicast to an address several sockets share then goes to
one of them by a hash of where it came from (the probe's listeners line says so); Node's `reuseAddr`, AGMessage's, sets
SO_REUSEADDR alone, which gives such a unicast to the socket bound last, again to one of them. The verdicts are the same
either way. Windows is unmeasured on every row; its documentation calls unicast delivery to a shared port indeterminate.
