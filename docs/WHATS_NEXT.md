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
  each locking a device out of its own kind (REMOTE.md, 2.19). *Since the lockout fix merged with 2.21's work:* a valid
  key's request no longer resets the wrong-PIN count. Until now every keyed poll from an app on the same address did, so
  five wrong PINs in a minute rarely added up on a PC that runs a keyed app; now they do. Five wrong PINs from a browser
  on the show PC (a mistyped or stale PIN) hold that PC's keyed apps off Stagewatch for the rest of the minute, Patterns,
  PlayMap and AGMessage among them when all four run on one laptop, and an app refused that way may hold its key back
  longer by its own link's rules. Only the right PIN or the minute's end clears the count (REMOTE.md, 2.10's API keys
  and 2.19's wrong passes).
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
  and repeat. Counting per address and key closes it. *Since then (the default branch's lockout fix, merged with 2.21's
  work):* a valid key clears nothing. Only the right PIN, or the minute running out, resets the wrong-PIN count, so the
  guessing is closed without a count per key; five wrong PINs from a browser hold a keyed app on the same machine off
  for the minute too (REMOTE.md, 2.10's API keys and 2.19's wrong passes). The suite holds Stagewatch's side (a locked
  address refuses a valid key in the lockout's words). No interop row reads the other side yet: the lane's laptop sends
  one wrong PIN and clears it with the right one, and no row sends five from the laptop's address and reads each real
  app's keyed link refused in those words, held, and back by itself after the minute. It is a row for a later lane
  unit, run last in its topology, since nothing but the right PIN or the minute clears the count.
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
  Windows (`pwsh tools\smoke.ps1`). *Since then (SW20.5):* that link is one of up to 8 playout links (Settings → Integrations), each to a
  Patterns desk or a PlayMap node, its kind heard from its STATE (or set; a peer that answers as another kind is amber and sent
  nothing), each running on its own as SW20.4 left the Patterns link, asked for its STATE and its `CONTEXT` at once once paired,
  and a PlayMap node for its `WHOAMI` (one that lets this PC only read is amber, with what its desk can do). `playout` and
  `patterns` lines run through one executor, `PlayoutDirector`, in the desk and the rig alike. It refuses from the peer's own
  facts, nothing sent — G48's Stagewatch half: Patterns' pause or stop of a playlist item or a stinger's clip, in Patterns' words,
  until its `video.pausable` / `video.stoppable` say otherwise; `VIDEO … ON <key>` only to a desk whose context lists that form;
  a PlayMap CUE only to a playlist seen idle or ended in a STATE at most 3 s old; never a sync follower's playlist; go and cue
  only once the kind is heard — and hands the rest to the link with a ticket that completes in the peer's own words (G07): the
  receipt is its first answer within the 3 s, and one that says accepted (or none by then) has one follow-up within 30 s of the
  send — the peer's final answer, never a later accepted, else Stagewatch's own "unconfirmed — … it may have run" — `"final":true`
  on a WebSocket and `OK @id final …` on Companion, with `refs.peerRequest`. An approved proposal (an AI's or a peer's playout
  line) settles on that final receipt: `done` only when the peer applied it, `failed` otherwise — an unconfirmed one with words that
  say it may have run, since §11 has no state between done and failed. The effect is looked for in a STATE read after the command
  went (after the peer's answer, when the STATE as it went already showed it) for the lead and 3 s more, one show-log row either way, at most 32 at once a link (`playout.seen.max`). The record's light
  reaches every reader: the context's `link:<name>` (amber at level 0, its flags in words) and `clock:<name>`, the Show network
  (each link's pulled context, a PlayMap node's only for the id its STATE gives), the AI's `playout_links`, the frame's `playout`
  row (`link`, `node`) and the config's `playoutLinks`; a peer that answers as another kind than set feeds none of them but its
  light; a clip's end moves the running order at most once an item, and an ended clip seen again after the link was stale is not a
  second end. The
  rig takes `--playout` (`tests/e2e/playout.e2e.cjs`, against fake PlayMap and Patterns peers). Not done: Patterns' `pausable`,
  `stoppable` and `ON <key>` land in its Round 94 (P93.2, P93.3), so today every pause or stop of a real Patterns playlist item is
  refused here and no real desk is sent `ON <key>` (*since then (SW20.13):* S4.1 pauses a real Patterns clip on its air through a
  playout link, at the lane's Patterns pin, which stays at round 91's 5a8ad8f (below, "The VT rows": at Round 94's fa5d76b ten
  LAN rows fail, Patterns' desk holding its key from the rig until a person presses Trust); at fa5d76b the same row read
  `pausable` true; no lane row yet pauses a real playlist item or
  sends `ON <key>`); `together` is refused with words, and no help line or context row offers it
  (the timed GO, with `INDEX` and `CUE LIST`, is SW20.7's); a link set to `wss` was not started (SW20.6 starts it, below); an AI's playout line is still a desk proposal, not re-targeted to
  the peer's own approvals (that needs `proposal.collect.ms`, SW21.2); the module's playout actions are SW20.12's, and it sends
  its Patterns lines with no id, so it reads no receipt; neither Patterns nor PlayMap sends a follow-up today, so a real peer's
  accepted answer gets its `final` only if that peer answers the id again (the e2e fakes do). Show Bus §8.1's frozen row still
  says Stagewatch's receipts carry no `final`: true of every receipt but a playout line's, and the contract stays as it is. A
  context a level-0 link pulled still counts as `verified` `key` for its id, and for a proposal's risk, while no key speaks for
  that id — the 2.19 rule for link patterns, kept for every link by choice (§11 counts a table read over the desk's own link) and
  bounded as above; the peer proves nothing at level 0, so it rests on the typed address until a LINK verdict replaces it. The
  desk's side — its status and show-log words for several links, the *Last 10 s* button on the link feeding the VT, the clip's
  end moving the running order — compiles here and is not walked on Windows (`pwsh tools\smoke.ps1`). *Since then (SW20.6):* a
  playout link reaches a PlayMap node over TLS — `playmap.lines.v1` on the `https` door its hello names, a line a message — when
  its transport is `auto` (or always, when `wss`; never, when `tcp`). The hello is the node's by id, else a PlayMap node heard at
  the link's host (its address, its own name, or an address the host name resolves to), never one older than 6 s; one under §2's
  conflict mark holds the link (13.4.4). Its certificate is judged by fingerprint alone and, at level 0, pinned `first` for the
  node id its STATE gives (Show Bus 13.8.2); a person trusts it by one press beside its short form — on the Show network row of its
  node (button and right-click) or the status line's right-click — the press bound to the certificate it showed. Any pin, `first`
  included, decides the transport (13.4.2: the pin alone decides): the link is never dialled plain again — it dials the door a fresh
  hello names, else the door kept beside the pin, else nothing, amber, and says so. A pin a person made refuses a changed
  certificate: CERTIFICATE CHANGED, red, no HTTP request, line or proof until a person compares the new one on the node's own
  screen and types at least 8 hex of it into an entry never filled in. A STATE naming another node than the one set is amber, sent
  nothing and pinned nothing. Pins are kept per node id once one is set, and with none set, per the address the link dials (a move
  to another address starts without one). `playout <link> trust <hex>` is a parser verb with its help line and High risk only so
  that OSC, Companion, HTTP, WebSocket and MCP refuse it in words, whoever sends it, as the desk's and the rig's executors do; it is
  never among the context's commands, never a plan's step, and refused when proposed to this desk. A `429` at the door holds the
  token as a lockout (its `Retry-After` when longer than the minute); PlayMap's `ERR … wrong token` (and `… — closed` then `4401`),
  or a `4401` alone, holds it as a wrong token, one proof; an upgrade without `playmap.lines.v1` is sent nothing; a paired plain
  link off this PC moves to TLS between commands once its node's hello names the door. Left: Settings has no box for a link's
  `transport` or `node` yet (SW20.11), so they come from the settings file, and nothing on the desk forgets a pin yet — a node that
  turned its TLS off for good is dialled at its kept door and never plain, until the settings file sets the link to another node
  (or, with none set, another address); no interop row dials PlayMap's TLS door yet
  (the suite plays the node in process, its ERR-then-4401 and `Retry-After` legs as PlayMap's code reads); the Show network row's
  note and button, the status line's entries and the typed entry compile here and are not walked on Windows (`pwsh
  tools\smoke.ps1`). The plan's "pinned per node id" is read with the contract: a level-0 link pins `first` on sight — which decides
  the transport, never the trust — and a person's pin only by their choice (13.8.2–3), so a pin by first sight never refuses a
  changed certificate. *Since then (SW20.7):* `together` is carried. A GO asked together goes at an instant the operator's lead
  (300 ms) ahead on this PC's clock: a PlayMap node gets `AT <unix ms>` on its own clock only when the link's clock is fit for it
  (Show Bus 13.11.4–6: a sample at most 10 s old, its error within what a round trip as long as the lead gives) and was measured
  over TLS or on a link to this PC; any other GO of the same start is held here and sent untimed half its round trip before the
  instant, or at the instant by this PC's clock alone when its clock was measured over plain TCP off this PC (Patterns' 9697 until
  its Round 97, which the rows say); a member up with no clock sample at all makes nothing together, said, and one whose link is
  down takes no timing from the others. One GO per PlayMap sync group, a follower's row saying whether its leader's GO was sent;
  a held GO goes only on the link and connection it was held for, and only while the licence allows. The receipt's effect, the
  show log and the status line say which — "sent with AT for 20:00:01.250 (± 3 ms)" as it goes, "scheduled for …" only once the
  node's own answer took it, "held here, to be sent at …", "not timed: … — sent at once" — and a timed GO whose node's STATE shows
  it started later than its AT by more than half the round trip and 20 ms has a "started late by" row. The running
  order's cues (a running-order item's `playout`, up to 4) run through `PlayoutDirector`'s planner in the desk and the rig
  (`--cue`): standby off by default and, when set, sent at the end of the turn that loaded the item unless it started in it (a
  PlayMap CUE only on an idle playlist); each cue GOes when its item first starts, a Patterns cue by `CUE GO` alone; a cue whose
  link is missing, off or not running, whose peer answers as another kind or node, or that names what its peer does not list, is
  amber advice before its item — first in the desk's advice, whether or not its assistant is on — and skipped with a row that
  reaches the status line too. `INDEX` (PlayMap) and `CUE LIST` (Patterns) are asked once the kind is heard and again when the
  STATE's revision moves, and a peer answering as another node than its link's feeds none of its list; what they list and the
  running order's cues reach the copilot's facts, and the context offers `[together]` on a PlayMap node's GO. Left: the desk's
  cue editor, its pickers from those lists, the running-order row's menus and Help (SW20.11, the maintainer's PC work); the
  module's playout actions (SW20.12); no real PlayMap or Patterns has run a cue yet (the VT interop rows, SW20.13, drive both
  through playout links but run no `--cue`: a cue against the real apps is a row still to come); "seen" and
  "started late" are read from the node's STATE, never from its outputs (PlayMap's round-3 observer); a Patterns desk's GO is
  never timed until its wire offers TLS; `INDEX` and `CUE LIST` answers longer than a line (256 KB) are not read, so such a
  peer's cues go unchecked and the peer's own refusal is the word; the link's record carries no `unfit` flag, so a clock not fit
  for a timed GO shows only in the rows of a GO that asked for one; and the believed clock sample is 13.11.3's smallest round trip
  among the last 8, one a ping each 2 s, so it can be older than `clock.fresh.ms` (10 s) while fresher ones are kept — a timed GO
  then falls back, said (held, or at once). Pinging every 1.25 s (inside `ping.min.ms`–`ping.max.ms`) would keep the window
  within 10 s; it is not done without a measure of how often a GO falls back on a real network (the design's "smallest round trip
  among those at most 10 s old" differs from 13.11.3, which is followed).
  None of the desk's side (the advice, the turn's end, the show-clock times in the rows) has been walked on Windows (`pwsh
  tools\smoke.ps1`).
  *Since then (SW20.12):* the Companion module drives the playout links: *Playout: the clip on air* (play, pause, stop, restart,
  its last seconds, seek), *Playout: GO* (a PlayMap playlist's item — its next with none named — or a Patterns cue; `together`)
  and *Playout: cue*, each with a link from the config's `playoutLinks` or typed (none named: the only link switched on, else for
  the clip on air the one feeding the VT). They exist where the config carries `playoutLinks` (2.20 on; an empty list keeps them,
  so a press with every link off is refused in Stagewatch's words) and the licence carries playout, and stay until a first
  `CONFIG` says otherwise. A link name the grammar would read as an action, `trust` or another integration's is refused in the
  module, never sent. Every playout line, and every *Patterns* action's, goes with a request id: Stagewatch's answer — the gate's
  refusal from a PC not in May drive playout, the peer's own words, the `final` follow-up, or "no answer" when the link dropped
  first — is one line in the connection's log and the `playout_answer` variable, never a done the module did not hear; at most 64
  wait for an answer, none past 40 s (an `accepted` whose words begin `unconfirmed` waits for nothing), and a drop never reports a
  final answer as owed. The frame's `playout.link` and `source` are `vt_link` and `vt_source`; the *Playout: The VT comes from…*
  feedback lights a key while a link (any, or vMix) feeds the VT, and the *Playout links* presets give each link, and a press with
  no link named, its transport keys — lit by what the press reaches, never by vMix's clip — and an answer key.
  `companion-module/test/playout-lines.tsv` holds the module's lines to the grammar from both ends (the module's tests press every
  action against it; the suite reads every line as the Companion server does). Left: Companion itself has not loaded these
  definitions here — the module's tests and `companion-module-check` ran, a deck has not (the maintainer's PC); the module reads
  no `INDEX` or `CUE LIST`, so GO and cue take ids or names typed, not picked (the desk's cue editor picks them); and a press's
  answer reaches the log and one variable, not a feedback of its own button.
  *Since then (SW20.11):* the desk sets all of it up. **Settings → Integrations → Playout links** lists every link with its status
  (Find, Add, Edit, Remove, Trust its certificate…, the buttons and the list's right-click enabled together): Find listens for
  Patterns desks and PlayMap nodes and adds the one picked with its kind, address, port and node id — one a link already reaches
  (its node, or its address and port) is that link's, selected and never added beside it; Add and Edit set every field — name,
  peer, address, port, token (proved, kept encrypted), node, transport, VT, next on end — each refused in words by the rules a
  settings file keeps, never mended (`PlayoutRules.EditLink`); a renamed link's running-order cues move with it, in every running
  order of the show (cues that already named the new name go with them, and are said), and a hold its peer's refusal started
  follows its token to the new name (`PlayoutKeptStore.For`); Remove says how many cues still name it (kept, and skipped with
  advice). Trust its certificate… offers only the certificate a row showed waiting, bound to it: one that comes to wait after the
  list was drawn is offered once it is drawn again, and one changed since is drawn again at the press, nothing trusted. The lead
  of a GO at one instant has its box (100–2000 ms). The 2.17 *Patterns video* box is gone: it was link `patterns`, which the list
  shows. **Settings → Remote → May drive playout** takes addresses one a line; Save refuses one it can't keep and says which,
  never widening it. **Settings → Triggers** has a **Secret** column — the kind, a password box checked and protected — and Save
  offers to move a `?pin=` out of an address only where the rule would then be sent. **Edit running order → Playout** edits up to
  4 cues an item, each picked from what its peer lists (or typed by id), with standby's words for its kind and together's. The
  running order's rows show ▶ and how many cues, and right-click *Cue playout now* and *GO playout now* run the planner by hand
  (the running order left where it is) on the item the row drew, found again at the press by its place and id — refused, nothing
  sent, when the running order changed under the menu (another made live from the wire, an undo). The VT chip's right-click (and
  *Last 10 s*'s) has the clip's Play, Pause, Stop, Restart, Last 10 s and Seek to… on the link feeding the VT when the menu opened
  — the lines a deck sends for that link, Last 10 s's `playout <link> end 10` among them — each greyed with the words its press
  would be refused in (the plan's own, the licence, the link down) or the peer's WHOAMI (`PlayoutMenus`). Help says it all, a
  Patterns standby moving the desk's own standby and a PlayMap one showing on air among it; the suite holds the seams
  (`PlayoutDeskTests`) and UiSmoke has checks for the list, the editor, the rows and the chip. Left: none of it has been walked on
  Windows — the WPF compiles here, its walk is the maintainer's (`pwsh tools\smoke.ps1`); nothing on the desk forgets a pin yet,
  nor offers *Link again now* (13.16.6); a trigger target's certificate is trusted from the status line, not from Settings; a cue
  in a show file not open keeps a renamed link's old name. *Cue playout now* and *GO playout now* are presses of the desk alone:
  they run no line, so Companion's recorder does not take them, they have no receipt, and no deck, wire, MCP client or copilot can
  run or propose them — a grammar form for an item's cues (its parser verb, help line, `Copilot.Classify` risk and context row) is
  a later unit's, named for 2.20's papers (SW20.14). Settings' list is drawn when it opens and after each change made in it: a
  certificate that comes to wait while it is open is offered on the status line and the node's Show network row, and in the list
  once it is drawn again.
- **Find still listens on Patterns' beacon.** Drop UDP 9700 from Find once Patterns' 9928 walk (its OPEN row L47) is filled:
  the release after that walk row is filled takes it. Find already reads a hello's `ports.wire` (else the top-level `wire` older
  senders give) and hears PlayMap's hello as well as Patterns', so a desk that names its wire only in `ports` is listed; Settings'
  *Playout links* Find asks for Patterns desks and PlayMap nodes only. `NodesDocTests` holds this row here while Find listens on 9700.
- **No button gives an install a new id** (the upgrade notes say how by hand). The desk's conflict words tell the operator to.
- **A conflict reaches the desk, the advice and the AI's facts, not the context or Companion.** By §6, an id two machines say has
  no `peers` row in Stagewatch's context, so other apps reading it and MCP's `get_context` do not list it, and the Companion
  module reads no peers at all.
- **Stagewatch dials no link by hello.** §2's "dial 127.0.0.1 first" for a peer heard on this machine is not done: its one link,
  to Patterns, is a typed address. *Since then (SW20.5):* each of its playout links, up to 8, Patterns desks and PlayMap nodes, is a typed address still.
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
  *Since then (SW21.1):* PeerContexts keeps §14's numbers as a reader, and is the one home of them in code: a context's first
  200 items, its first 256 commands, each verb whole up to 512 characters (a longer or malformed one — empty, holding a
  control character, or opening a bracket it never closes — dropped and counted, never cut) with its words up to 512, and its
  first 400 links whose ends and kind §6 allows (an end `@<id>/<item>` with a §2 id of 6 to 40, at most 120 characters), the
  rest counted. A proposal's risk for another app is the most specific whole form of that app's verified table of its own kind
  (§6's notation, `Timer.Core/VerbSyntax.cs`): a line no form takes is high, an exact tie rates higher, a carried line raises
  the risk, four deep. A carried line no form takes is high, as on its own: PlayMap reads `VT`, `CLIP` and `TC` as `VIDEO` and
  `TIMECODE`, and names a verb by its first words alone, rules no table states, so `BIND ADD note 60 TO VT STOP` is high here
  as there; more than 256 carried lines at every depth together is high, as PlayMap counts them. A form is matched as a flat
  run of steps, each against each word once, and a rating past 200,000 steps is high (about 20 ms on the cloud bench; the
  review's row of 22 optional values, which took 11 s and over a gibibyte, now takes 995 steps, and one of 85, all a verb
  holds, 12,213); a proposal is rated before the approval desk's lock is taken. One selector, `PeerContexts.Chosen`, gives the
  Show network's row and a proposal's risk the same context: the one this desk's own playout link pulled while it proves
  something — while no key speaks for that id — else the one posted with proof; a pulled context speaks only as the kind its
  link reads, so a peer at the operator's address never rates another kind of node. A context posted too big to read is named
  on the Show network: on the row of the node its key is bound to, or the one row at its address, else a row of its own apart,
  and the headline counts it. On PlayMap's busy golden context (`tests/Stagewatch.Tests/fixtures/`), with each line value
  filled by a listed low line 502 of its 504 form examples rate as their row and 2 higher (JSON in a row that carries lines);
  with x1 in every value 496 and 8 higher (a binding's carried x1 names no form); none lower either way, where 2.20's matcher
  rated 114 of its 144 low and review rows wrong. Against PlayMap's own `RiskOf` (its Core built at 6d781bc, in a scratch
  harness outside the tree), 1,314,587 lines made from the golden's forms, PlayMap's aliases among their words, rated none
  lower. Left: until PlayMap's context lists its word-dependent risks as rows of their own (its integration pass's PM-0),
  `ASSISTANT SET enabled|provider …`, `DECODE POLICY … FORCE` and `MACHINE SET GPU-PRIORITY REALTIME` rate review here where
  PlayMap's own `RiskOf` says high (PlayMap's policy still holds them to the Approved role while armed); a binding whose
  carried words name no verb, such as `BIND FORGET note 60`, is high here where PlayMap says review, the price of failing
  closed; the verdict says when a line fills a secret placeholder, but the desk refuses such a line only with SW21.2's line
  fence; a context posted too big reaches the desk's Show network and the rig's view, not the context's peers rows or the AI's
  facts; the window placements that read the kept links are SW22.2's; and the desk's rows are compiled here, never seen: a
  walk-through of the Show network row is the maintainer's PC work, not run.
  *Since then (SW21.2):* the desk keeps §11's rules (REMOTE.md, "The desk's rules"). Ids are `p<n>-<run>`, the run four base-32
  characters drawn as Stagewatch starts, `p<n>` alone naming this run's; an earlier run's id is `404` on a read and `409` on a
  result, in words that say the desk restarted. A line is refused, never cut: over 2048 characters, a control character, an
  invisible format character (a direction override, a zero-width space, tag characters; the zero-width joiner and non-joiner pass),
  half of a surrogate pair (raw, or as a JSON escape, which the reader would otherwise throw on), a key (13.5.1's shape under any
  prefix, or a word `swk_`, `ptk_`, `pmk_` or `agn_` opens), a window's address or a token one of this desk's windows holds, or a
  secret placeholder filled with anything but `‹secret›` — of the target's verified table, or of any table held for its id that
  proves nothing, which refuses but never lowers a risk. A reason or a node's name keeps none of those characters, and a kind or id
  holding one names no node. A `from` of kind `main` or `mcp`, or the desk's own id, is `403`; so is a proposal sent with an MCP key
  ("an mcp key posts no proposal here", an upgrade note: a script that posted with an MCP key proposes through `/mcp`); a target
  naming the desk's own id under any kind is the desk; a `peer` or `desk` line proposed to this desk is refused. A target raises the
  risk of its own proposal under its bound key, never lowers it. The desk records each target's last collect and approves nothing
  for one silent 15 s: the proposal stays pending, and Copilot, the status line and the show log say why. The `201` appends `form`
  and `formWords`, a proposal `collected` and `form`. `ProposeApproved` makes a person's line (born approved, one of §11's four role
  words in `decidedBy`, a listed line of a collecting target), which the desk's Copilot list shows as sent and follows to its
  result, and `NextPending` names the oldest pending one. Stagewatch's own commands rows are §6's notation, each form a line its
  parser takes at a risk no lower than its own: `start | pause`, `patterns end [<seconds>]` and
  `message <phrase> [info|warn|crit [off|slow|fast [ack]]]` (a flash only after an urgency, an ack only after a flash, as the parser
  reads them). The lines the other apps document parse and rate as they say (`tests/Stagewatch.Tests/fixtures/peer-lines.json`, with
  Patterns' and AGMessage's contexts at the pins the interop lane holds): AGMessage's four stage lines are taken, its 0.4.0 line is
  refused by the parser and so rates high, Patterns' `APPLY` and AGMessage's `notice` rate review. Left: the verbs that send a
  person's line or decide one (`peer`, `desk`) and the journal row that names who sent it from where are SW21.4's, the rig hosts no
  desk until then, and the context's `peers[].collects` and the operator frame's desk block are SW21.5's; the desk's Approve is not
  greyed for a target that is not collecting — it is refused, in words, when pressed — until SW21.6 (PC work); an AI's playout line
  still goes to the desk, not to the peer's own approvals; the copilot still cuts its own steps at 400 characters (release B), and
  `operator (copilot)` as a person's line's decider waits for it, since §11 names four role words; the desk's mirror of a person's
  line is compiled here and moves into SW21.3's `DeskService`, where its tests come; and the Copilot's words for a refused approval
  are compiled here, never seen: a walk-through is the maintainer's PC work, not run.
  *Since then (SW21.3):* the desk is one pure service, `DeskService` (Timer.Show), on an injected clock: the queue's node routes
  and their answers; how a line rates (this desk's own lines by `CommandRisk.Of`, Core's one risk table as a `LineRisk`, which
  `Copilot.Classify` now only maps; another app's by its verified table); which app a word names (a node id, a name one app has, or
  `playmap`, `patterns`, `agmessage`/`messenger` when one app of that kind is heard — never `stagewatch`, `main` or another
  Stagewatch); `Send`, a person's line (refused from an agent, the advisor, a copilot step or a proposal; refused for an app whose
  verbs are not verified or do not list it, or that is not collecting); `Decide` (refused from a trigger, an agent, the advisor, the
  copilot or a proposal; `next` the oldest and never a high one; a high one by its id and the word; nothing unticked; nothing for an
  app that is not collecting); `ResultOf` (sent, collected, done, refused, undelivered, in words that name the app); the apps, the
  summary and a row's quick verbs for the readers to come; and the show log's rows. `MainViewModel.Desk.cs` mirrors it. The rows
  are a journal kind of their own (`JournalKind.Desk`, appended), each opening with the desk's own word and the proposal's number
  (`Proposed p1: …`, `Sent p1 to …`, `Approved p1 (deck): …`, `Outcome p1: …`), so no note and no name a peer chose passes for
  one; their words name the role alone, and who and from where is the entry's `via` (appended, written only when set), which the
  context's `recent`, the report and the AI's facts never print. A refusal's row never quotes the line, and the same refusal again
  within a minute is counted and said once more when the minute ends (at most 16 held; the oldest gives way with its count) — a
  flood of *different* refusals is still a row each, which SW21.4's sender gate bounds, with its flood test there. `next` approves
  no high proposal even with the word; a host's run that throws fails the proposal instead of leaving it approved; a word that is
  one app's name and another's kind names neither. Approving a shared proposal honours its tick
  (G14's tick fix: 2.20 approved an unticked one). A receipted command that runs another inside itself keeps its own notes
  (`ReceiptStack`): the inner one's refusal, refs and peer are its own, and a wire's command is still a wire's after it. A receipt
  carries ids beside its numbers in `refs` (`RefIds`), and a refused receipt keeps the refs noted before it was refused. Every
  whole-line door reads a line into words by one function (`ShowCommandParser.Tokens`). The show report's AI section lists the
  desk's rows by their kind (proposed, sent, approved, not approved, rejected, the outcome) beside the copilot's notes; a
  rejected shared proposal is the desk's row, no longer "Copilot proposal discarded". A risk fence names every command kind
  that rates review: a kind appended later fails it until its risk is chosen. Left: nothing calls `Send` yet and `desk` has no
  verb — `peer` and `desk`, their gate, `refs.sharedProposal` on a wire and the rig's desk (it answers "no approval desk here"
  until then) are SW21.4's; Show Bus 1.3 §3 says `refs` names what was made "by number", and its text is final and hash-pinned,
  so the words for a string id are owed to the master before `refs.sharedProposal` goes on a wire (SW21.4, or 2.21's papers);
  the frame's desk block, config peers and the context's `collects` (the service's `Summary` and `Apps`) are SW21.5's; the row
  menu, the send dialog and the greyed Approve (its `QuickVerbs`) are SW21.6's; the journal's `Peer` kind, an entry's node and
  its own time, the report's show-network section and `network.csv` are SW21.7's. The brief's line that names
  `Copilot.Classify` as a new command's risk is the maintainer's to change: the table is `CommandRisk.Of` now. Compiled here,
  never seen: Copilot's words for an unticked or refused approval and for a line handed to a playout peer, the show log's
  order when an approval runs a line here, and the UI smoke's new unticked check (the maintainer's PC).
  *Since then (SW21.4):* the two verbs are on the wire (REMOTE.md, "A person's line to another app, and a person's decision").
  `peer <app> <line>` and `desk approve|reject <id>|next` are `ShowCommandKind.Peer` and `.Desk`, appended; the line is one
  argument, passed on whole, and a typed line whose own line holds quote marks is refused, never altered. No parser names a
  sender: the door that took the line stamps who sent it (`ShowCommand.By`, a `DeskOrigin`, now in Core), and a line nobody is
  named for sends and decides nothing. The sender gate (`PlayoutGate`) decides both over HTTP and a WebSocket by 2.20's rule —
  the PIN or a control key; with no PIN, this PC or a listed address — refuses both over MCP whatever key comes, and over
  Companion TCP and OSC in this release (release B's doors, SW22.8). Its list is renamed **May drive other show apps** in
  Settings, Help, Diagnostics, the notes, the module's tips and the papers (the design wrote "May drive other show apps from";
  the trailing word is left off so a refusal reads "its address in Settings → Remote → May drive other show apps"). The
  receipt of a sent line waits for its app (`DeskReceipts`): applied or refused in the app's words within 3 s, else accepted
  with one `final` follow-up within 30 s; every receipt names the proposal in `refs.sharedProposal`, a string. `GET /api/help`
  serves the grammar people read (`PersonHelp`); the copilot, the advisor and MCP keep `Help`, which leaves both verbs out, and
  each refuses a line of either. The rig hosts the desk (`--desk-fresh`), and `tests/e2e/desk.e2e.cjs` runs it against three
  show apps on its node routes. A flood of *different* unlisted lines from one sender is one row and a count, since a refusal
  never quotes the line. The words beside a line are held to its fence too: a key or a window's address where the app is
  named, or in a rejection's reason, is refused unquoted ("a key rides in no line"), sends or rejects nothing, and is in no
  log line and no journal row; a line that opens with either verb is refused by MCP and dropped from a copilot plan by its
  verb alone, whether or not the rest parses, so no model is handed the verbs' grammar or its own line back. *What this did
  not do:* the operator frame's `desk` block, config `peers`, the context's `collects` and
  the producer page's panel are SW21.5's; the Show network row's menu, the send dialog and the greyed Approve are SW21.6's (PC
  work) — until then a line is sent only over HTTP, a WebSocket or by a Command trigger's `peer` line (the app has nowhere to
  type one, and no menu sends one yet); Companion TCP, OSC and the module's actions
  are SW22.8's, the copilot's steps for other apps SW22.9's, MCP's `target` SW22.10's. Show Bus 1.3 §3 still words `refs` as
  "by number": the string id is on the wire now, and the words are owed to the master with 2.21's papers. The brief's line
  that names `Copilot.Classify` (and would name `PersonHelp`) is still the maintainer's to change. Compiled here, never seen:
  the app's executors for both verbs (`DeskCommand`), an operator's `peer` or `desk` line kept from Companion's recorder (no
  menu sends one yet), a trigger's `peer` line, the renamed Settings label and its help tip (the maintainer's PC; UiSmoke
  compiles with the new name).
  *Since then (SW21.5):* the desk has its readers (REMOTE.md, "Every reader takes the desk"). The operator's frame ends with
  `desk{pending,next,apps}` — on HTTP with a credential, a control page's socket, Companion's `SNAPSHOT` and MCP's state, never
  in a public frame — and `network` (SW21.8) follows it, so the order is desk, then network. `next` names its two ends as the
  desk knows their ids (the Show network's name, else kind and id — `DeskService.Known`, never the name a proposal gave), with
  `fromId` and `targetId` beside them, and carries the line's first 120 characters with `cut` when there are more; the desk's
  journal rows, its receipts' words, the queue's refusals and outcomes (`ProposalDesk.Names`) and the app's Copilot list name
  them the same way. `/api/config` and Companion's `CONFIG` end with `peers` (id, name, kind), only from a host that keeps a
  desk; each `peers` row of the context ends with `collects`. What the readers are told is one rule for every host
  (`DeskService.Refresh`, which the app and the rig both call when a proposal changes and once a second, never per frame): the
  frame when the summary reads differently (`DeskSummary` is equal to another that says the same, its apps an
  `EquatableArray`), the config when the apps differ — and that at most once in 5 s (`PeersHold`), a change inside the hold
  told at its end. The cause is counted, not measured on a show: a hello is not authenticated, and one that renames itself
  each second would have every page and deck fetch the config each second; `DeskTests` holds the bound on an injected clock
  (twelve renames in twelve seconds: twelve frames, two configs). The rig counts a `configVersion` (its config was cached at
  version 0 for a whole run: a link's kind heard after the first fetch never reached a page). The copilot is given the frame
  without `desk` and the config without `peers` (`AiContext`): another app's words reach the model only inside `other_apps`.
  The Showcaller page draws the frame's desk: the oldest proposal, Approve and Reject by the id shown, a high one by a second
  press that sends `high`, Approve greyed with its reason while the proposal's app is not collecting. It takes no press on
  what it has just drawn — a new proposal, the armed Approve, a button just pressed, for 0.8 s — so a double-click neither
  confirms a high proposal nor decides the one behind; and it reads a `cut` line whole from `GET /api/proposals/<id>` before
  Approve takes a press, greying Approve with words when it cannot. `common.js` hands a page its receipts (`onReceipt`).
  Tests: `DeskTests` (frame, config and context bytes; equality; names; what the readers are told; the copilot's view),
  `DeskWireTests` over a loopback web server (HTTP, a control socket, a page with no PIN, the config's hold, Companion's
  `SNAPSHOT` and `CONFIG`, a first-protocol deck's lines), `tests/e2e/desk.e2e.cjs` (the readers on the rig; the page in a
  browser against three apps: decisions by id, the high double press, a double-click on a high proposal and on the first of
  two, the arm running out and not carrying to the next proposal, names, a long line read whole, one that cannot be read)
  and `pages.e2e.cjs` (the page with an idle desk). *What this did not do:* the frame carries one proposal, by the design, so
  the page shows the oldest and a count, not the queue — one whose app is not collecting stays in front of those behind it
  until it is rejected there or decided at the desk; a short pending list at the end of `desk` is the fix if a show asks for
  it. `desk` counts the shared queue only: this Stagewatch's own copilot's proposals wait in the desk's Copilot list, so the
  page says "Nothing from other apps waits" while the desk may hold its own. The copilot's `recent` rows still quote the
  journal's "Proposed …" rows, a proposal's line among them, as before this unit; release B's `other_apps` is where they
  belong. The config is behind the viewing code like the rest of it, so `peers` (ids hellos already say) is not the PIN's;
  the AI's facts still carry no `collects` (release B's `PeerFact`, SW22.9); the module reads neither field yet (SW22.8); the
  Show network row's Collects mark and its menu are SW21.6's (PC work). Compiled here, never seen: the app's frame and config
  wiring (`RefreshDeskReaders`), its context rows and the Copilot list's names.
  *Since then (SW21.6):* the desk has its door (PC work). A Show network row's right-click and its … button open one menu,
  read from `DeskService.Door`: Send a line to <app>…, up to six quick verbs (AGMessage's `notice <text>…`, review, among
  them: G20's desk half), Its verbs…, Copy its headline, then the key entries; an entry that cannot work is grey and says
  why, in the words the send would refuse it in. The send dialog reads `DeskService.Preview` as the line is typed and once a
  second: the form that takes it, its words, its risk as a word on a chip (LOW, REVIEW, HIGH, or NOT LISTED only when no
  verified form takes the line; no chip for a line refused for anything else), whether the app collects, and the forms the
  typed words could still become. Send is live only while the send would take the line, and goes to `DeskService.Send`
  straight, never through the command box, so no deck recorder takes it; the box cuts nothing, so a line over 2048
  characters is refused whole. Preview and Send share one judgement (`Judge`, the queue's read-only `WhyNotSent`), and a desk
  full of lines counts only the lines not yet late, so the two agree at the same moment. The Copilot list's Run ticked steps
  is grey with the reason while the target app is not collecting (G14's grey), and each row says where its last sent line
  stands. Held by `DeskTests` (the door, previews agreeing with sends word for word over 56 cases, forms and leads, last
  sent, the greyed Approve's reason) and `DeskRulesTests` (a late line not counted). *What this did not do:* G20's fix also
  names the notice as a trigger action (on an item's start); there is no entry of that name — a Command trigger's
  `peer agmessage notice <text>` line already sends it through the wire's gate. A Copilot step showing → <app>, the
  Companion and OSC doors (SW22.8) and the copilot's steps for other apps (SW22.9) are release B's; REMOTE and FEATURES words
  for the door are 2.21's papers. Compiled here, never seen (the maintainer's PC): the row menu and … button, the send
  dialog, the greyed Approve, and the UI smoke's checks of them (`pwsh tools\smoke.ps1`, not run).
  *Since then (SW21.8):* one preflight (REMOTE.md, "One preflight: ready for doors"). `NetworkPreflight` (Timer.Show, pure,
  on the time it is given) judges the room once a second from the same join every other reader of the show network takes:
  ready while no row is red, a row per check — heard, context, own, key, link, clock, window. A node a person configured (a
  playout link's peer, the node a node key is bound to) is red when gone or never heard; one only seen is amber when it goes
  and is forgotten after 10 minutes (`SeenNodes`, 32 at most). Its doors: `GET /api/preflight` (read scope, no query, 404
  from a host with none), the operator frame's `network` after `desk`, MCP `get_preflight`, the copilot's `doors`, the
  module's `network_ready` / `network_red` / `network_amber`, its feedback and a preset key, the Showcaller page's chip with
  its reasons, the desk's Show network header with its tooltip and Copy the preflight (button and right-click), a help tip,
  and the rig, which judges and serves the same. Every door answers the stuck answer for a stuck show thread or a verdict
  older than 3 s (`PreflightWire.Served`, in Core beside the frame's `NetworkVerdict`, since the web server reads only
  Core). It is never an item of Stagewatch's own context. Held by `NetworkPreflightTests`, `WebV2Tests` (a real server: the
  route, the stuck hub, a stale verdict, MCP, the frame's last two fields), the module's test and `tests/e2e/preflight.e2e.cjs`
  (two scripted apps against the rig: amber at 6 s, the configured one red at 12 s and the seen one amber, every door, the
  page's chip in a browser, a stalled show thread). *What this did not do:* the design's `desk` check is not built — it
  reads "nodes whose hello says `approvals`" as nodes that should be collecting, but that cap names a node that keeps the
  queue (Show Bus §14), so the row would be red for every app that does not use this desk, against the rule that nothing a
  person did not configure blocks; whether an app collects stays where it is said today (the frame's `desk.apps`, the
  context's `collects`). `clock` is the amber `clock:<name>` item a playout link already raises while its clock is apart,
  so a clock within bounds has no green row. Window grants to peers are not a configured relationship yet (they come with
  windows for peers). A playout link whose peer has never said its id and whose settings name none configures no node: its
  own `link:<name>` row is red, and no `heard` row names a node. The page's rows are read again every 5 s while shown, so a
  row's seconds can lag that long. The frame carries the verdict from the first frame built after a judgement (within a
  tick), and the stuck one once that judgement is more than 3 s old. A keyed request refused is recorded under the id it
  named (`NodeBindings`), so the `key` check judges it beside what that node proves: red only for a node expected or heard
  with nothing of its own taken — which a holder of any node key can still cause for a node that is heard and has posted
  nothing yet, until that node's own post is taken. The show network's join keeps 32 rows by kind, then name, for every
  reader; a configured node past them is amber "not checked" here (the join was not given a keep-first set: its order is
  what the context's `peers` and the desk's rows show). Compiled here, never seen (the maintainer's PC): the Show network header's verdict, its tooltip, the Copy the
  preflight button and right-click entry, and the UI smoke's checks of them.
  *Asks of the other apps (SW21.2).* **Patterns** (Round 94 or after): its `APPLY` line keeps a look's title as typed, only
  shortened (`DeskProposals.Line`, at the pin `5a8ad8f8`), so a title holding a control character or an invisible format character
  is now refused `400` where 2.20 dropped the characters; and at the pin a refused proposal stays unsent and is posted again each
  cycle before the collect step, so Patterns stops collecting, and this desk then refuses every approval for it as not collecting,
  until Patterns restarts. The ask: leave those characters out of an `APPLY` title (the zero-width joiner and non-joiner may stay),
  and settle a proposal the desk refuses with `400` — say it and drop it — so the collect still runs. `fixtures/peer-lines.json`
  names the case (an `APPLY` title with a tab). **AGMessage** (its next unit): `stagelines.js` says this desk keeps 400 characters
  of a line (`LINE_MAX`); from 2.21 a line is up to 2048, refused rather than cut, so its limit may rise; and its `clean()` leaves
  out `\p{Cc}` only, so words holding an invisible format character (a right-to-left override) are refused `400`, which it shows its
  sender — the ask is to leave those out too, joiners kept (`fixtures/peer-lines.json` names it).
- **A non-English show fills its context sooner.** The context is written with the default JSON encoder, which writes each
  character outside ASCII as a six-byte escape, so such a show reaches 64 KiB with about a third of an English show's text. A
  relaxed encoder waits for every reader's parser to be measured with it.
- **The presenter PC's choice of certificate has no test of its own**: it needs a TLS Stagewatch on the bench.
- **LB-1's Windows rows are not run** (below): 1.3 names no carrier that finds apps on one machine with no network.
- **The interop lane is a track of its own.** The rig's options for it landed (2.18's list, above), and so has the
  lane's ground: `tools/test.sh interop` (not a default lane) builds Patterns' bench host, PlayMap's node and AGMessage
  at the pushed commits `tools/interop/siblings.json` pins, in clones of its own, and runs them with the rig on three
  topologies, each inside a user, network and PID namespace of its own that ends with its run: a one-kernel LAN, each
  app a host of its own on one switch; one show laptop, the four apps on one host with its Wi-Fi up, typed 127.0.0.1 as
  the maintainer types it; and, since 2.21, that laptop with no network at all, its loopback alone. Its rows, each added
  once it passed against the real apps, hold that each app is ready in its own namespace (S0.1); that, within 5 s of the
  last app ready, Patterns hears PlayMap and AGMessage, PlayMap hears Patterns and AGMessage, and AGMessage hears
  Patterns, each from the address its hellos came from wherever the app says one (PlayMap's NODES says none for the
  Patterns it follows once it holds that Patterns' context) (S1.1-S1.3, and S1c.1 on the laptop, where every hello comes
  from the laptop's own addresses, its Wi-Fi's by the broadcast or 127.0.0.1 where a loopback copy lands, and every link
  to the rig is live at 127.0.0.1); that PlayMap follows Patterns' clip and blackout, paired by proof, on the LAN and at
  127.0.0.1 (S4.5, S1c.2); that unknown words are refused by Patterns, PlayMap and the rig (S7.3's first three); that a
  stuck Stagewatch leaves PlayMap's link live and showing no time, for Stagewatch's own reason, and the rig's running
  time again once the show turns over (S6.2a); that the rig killed and restarted, or its cable pulled and put back, lets
  every link go, then gone, and brings every link back (S6.1, S6.1b); and that no secret of the run's register (the PIN,
  each app's token, AGMessage's admin token and the context token the lane learns from its admin) is in any log, frame,
  STATE, context, report or capture a run keeps (S7.1). After every row both of the rig's lockout tables (wrong PINs;
  wrong view passes) hold nothing against any address of the LAN, the switch's and every app's, nor, on the laptop,
  against 127.0.0.1 and the laptop's own address; a control from those addresses shows each table's reader sees them.
  Each topology's matrix (`artifacts/interop/<topology>`) says what each row read and how long it took. Where the kernel
  gives no such namespaces, a sibling is absent or a pin cannot be fetched, the lane says "not run", never passed. CI
  runs it at the pins, and nightly at the siblings' heads (below). `docs/BUILD.md` ("The interop lane") says how it
  runs: the four apps, its clones and pins, its topologies, its secrets, its rows and what a run leaves, and what it
  proves and what it does not; the suite's `InteropDocTests` hold its rows table to the drivers, and `tools/test.sh
  help` prints every lane and every variable a person sets for the lane.
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
    one peer), since on the LAN each app has one address, and on the laptop S1c.3 takes the rig's hello from either of the
    laptop's two addresses.
- **The VT rows (SW20.13).** The lane drives the real Patterns bench and PlayMap engine through Stagewatch's own playout links
  for the first time (G44's first untested pairing: Stagewatch's link against Patterns' wire and PlayMap's). The unit lands at
  the pins as they are, `tools/interop/siblings.json` unchanged: **the LAN rows prove against Patterns at round 91's pin
  (5a8ad8f8, Round 91.6's papers)** and PlayMap at 952940b5 (its wire answers every line the rows send there; the diff to
  Mapper's newest pushed commit, 783319e1, moves its dialled links and context, not its wire). There the four rows pass on the
  LAN with every row before them, 32 of 32. **Patterns' pin does not move to Round 94's papers (fa5d76b9), which the plan
  asked for (P93.4); moving it is a later unit of its own.** At fa5d76b S0.1 with S4.1–S4.4 were also run green, S4.1 reading
  the clip's `pausable` true; but ten LAN rows that pass at 5a8ad8f fail there: S1.5, S2.1b, S2.3, S2.4, S2.7, S3.3, S3.4,
  S7.3 (its Patterns leg), S7.4 and S8.1. The one reason: since its Round 93.2 Patterns' desk holds its node key from a
  Stagewatch on another machine until that Stagewatch proves itself (LINK, which the rig does not answer) or a person presses
  Trust beside its fingerprint on the desk's Nodes page (no wire verb, deck or assistant presses it; Patterns' `docs/OPEN.md`
  L96 is the same hold for a pin kept from before), and neither the bench (`tools/Patterns.Bench`, no control line for the
  press) nor the rig can stand in for that press yet. So Patterns' link reads the rig's public view without its key: its key
  is never bound (S2.7, S2.1b), its hello's row says "it cannot prove itself yet … press Trust" rather than live (S1.5), no
  context is posted and no verbs reach the rig's desk (S2.3, S2.4, S3.3, S3.4, S7.3's Patterns leg), the doors are not ready
  (S8.1), and S7.4 with them; the laptop and offline topologies pass (a Stagewatch at loopback gets the key as before). **What
  moves the pin**, in that later unit: Patterns' bench given a control line that presses Trust beside the fingerprint the desk
  shows (queued for Patterns' Round 95; the unit number is Patterns' to give), or the rig answering LINK so it proves
  itself (SW22.1a–c, `/ws` speaks LINK), whichever lands first; the pin then moves to a pushed Patterns commit at which
  every row passes. The rows, each added once it passed:
  - S4.1: a rig with `--playout patterns=<Patterns>:9697,patterns` and Patterns' own token is paired by proof as Patterns (kind
    and node its STATE says) within 5 s of its ready line, its record live, level 0 and amber for `plain` and `legacy`; its VT
    is the clip on Patterns' air (a 90 s synthetic clip, role `program`) within 1.5 s of the clip's own time left; `playout
    patterns pause` is applied in Patterns' words (`Patterns took VIDEO PAUSE`), the peer's request in
    `refs.peerRequest`, and Patterns' STATE paused and the VT stopped within 5 s.
  - S4.2: PlayMap's VT setup said on its wire (`PLAYLIST NEW show`, a 60 s `color:#202020` item, `PLAYLIST GO`; `PLAYLIST
    FOCUS` answers `already focused`, a node's one playlist being its focused one), a rig with `--playout
    stage-left=<PlayMap>:9941,playmap` and PlayMap's own token is paired as PlayMap within 5 s, its VT the item's own time left
    within 1.5 s; `playout stage-left pause` is applied (`VIDEO PAUSE`; PlayMap pauses a playlist item, Patterns' playlist rule
    is not PlayMap's) with PlayMap's STATE paused; and `playout stage-left go show #9` is refused, 409, its error holding
    PlayMap's own words (`show has no item #9`, read from PlayMap's own wire first as the oracle).
  - S4.3: one rig with both links, each with its own app's token, both paired within 5 s; the config names them by the kind
    each peer said; the context's `link:patterns` and `link:stage-left` are each in its own record's light; PlayMap's item runs
    again through its link (`playout stage-left play`); the VT names Patterns' running clip (the first link's in Settings'
    order) and, that paused through its link, PlayMap's running item.
  - S4.4: on one kernel each of that rig's links believes its peer's clock within 50 ms of its own within 10 s (both read about
    1 ms off, round trips of 1-4 ms), and its context carries no `clock:` item. The rig's new `links` control line reads it
    (each link's kind, node, pairing, believed clock and 13.16.3 record, never a token; `tests/e2e/playout.e2e.cjs` holds it).
  - Departures from the design, on purpose: the VT rows run a rig of their own beside the room's (its namespace, ports
    19950–19952, off the show network) and stop it when they end, where the design names the room's rig started with
    `--playout`: a playout link's peer is a configured node in the rig's preflight, so links on the room's rig would move the
    doors rows (S6.3, S8.1–S8.4) under them. Off the show network the rig hears no PlayMap hello naming its TLS door, so the
    PlayMap link is the line wire at 9941, plain, as an operator types it; the TLS carrier to a real PlayMap
    (`playmap.lines.v1`, SW20.6) is not read by a row yet. The rig takes each link's own token
    (`STAGEWATCH_RIG_PLAYOUT_TOKEN_<NAME>`, else the shared variable), as Settings keeps one per link, so S4.3 pairs each app
    with its own. S4.3's links are amber, not green as the design's row says: neither app proves itself to a connector yet
    (level 0, `legacy`) and both wires are plain TCP off this machine (`plain`), which 13.16.2 lights amber; green waits on
    each app answering LINK (and, for the line wire, on TLS). "Both VTs named" is read as the VT naming each link's clip in
    turn, the frame carrying one VT.
  - **Finding, for the owners (PlayMap's early STATE counted malformed)**, named here and asserted by no row: PlayMap's link
    also carries the `malformed` flag ("lines it could not read") from its first moment. PlayMap's TCP wire says its STATE right after `HELLO`, before the proof (`WireSession`, not
    strict: its `ws` channel holds the STATE until the proof), and Stagewatch's core counts every message heard during the
    handshake as malformed (13.6.3's "discarded and counted", `PeerLinkConnector.Hear`), so every level-0 line-wire link to
    PlayMap says it could not read lines it read whole. Whether an early STATE is counted apart from a malformed line
    (Stagewatch's core and SHOWBUS's §13.14 words) or PlayMap holds it until the proof (as its strict channel does) is the two
    owners' call.
  - Not read yet: S6.3's `link:stage-left` (a playout link to PlayMap red while it is gone), which needs the room's rig to run
    a playout link; a cue (`--cue`) against the real apps; the TLS carrier to PlayMap.
- **Interop rows that wait on another app.** Each is named here and not in the lane, which holds only rows that pass:
  - S2.2 and S2.5 (AGMessage's context reaching the rig, and AGMessage reading the rig's) are AGMessage's rows (SW-INT-AG):
    they come with the lane giving AGMessage a key of its own (0.5.0 posts no context without one).
  - *Since 2.21 (SW21.9):* the show laptop runs the rig on its Show Bus with node keys for Patterns and PlayMap, and S1c.3
    watches it for two minutes with every app linked and the laptop's addresses never counted; the offline laptop is a
    topology of its own (`offline`, its loopback alone), where S1b.1 links every pair at 127.0.0.1; and S7.4 on all three
    reads that no row counted an address and neither lockout table counts one at the end. F1 (a missing PIN counted) was
    fixed in 2.18's SW18.2: a request with no credential is not a guess.
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
  - *Since 2.21 (SW21.9):* each headless app's own context items (S8.0) are the LAN matrix's `ownItems`, read in S8.1, and
    none lights red (PlayMap's `machine` is amber: it says no renderer off a display); Patterns' desk and stuck rows (S3.3,
    S3.4, S6.2b), the doors (S6.3, S8.1–S8.4) and S7.3's desk word (a line an app does not list is refused at the rig's
    desk) are in the lane at the pins as they were. S8.1 holds Patterns' and PlayMap's heard and context rows green and
    AGMessage's heard row green; AGMessage's context row stays amber ("no context held") until SW-INT-AG gives it a node
    key, so 'ready for doors' is held on two of the three apps' contexts. Not in them: S7.3's AGMessage leg (an approved
    unknown line for AGMessage failed with words) moves to SW-INT-AG under the slip rule (R3), AGMessage holding no key in
    the lane, and this train's S7.3 covers Patterns and PlayMap; S6.3's `link:stage-left` (a playout link to
    PlayMap red while it is gone) waits for a playout link on the room's rig (the VT rows run a rig of their own, SW20.13,
    above), so the doors are configured there by PlayMap's node key; the rows
    decide on the rig's desk over HTTP with the PIN, since the rig's Companion door refuses `peer` and `desk` from another
    machine (May drive other show apps), and read the Companion module's `network_ready` off that door by the module's own
    rule, the module itself not running in the lane.
  - Two behaviours of the apps the lane found and works around, each named here rather than hidden in a driver: (a) S2.7's
    forged context post leaves a red key row on AGMessage's id for 30 s (`PeerContexts.KeepFor`): NetworkPreflight's key
    check by design, where a key refused for a node that takes nothing of its own lights red for its keep time, and so
    any node key can close the doors for 30 s by posting for an unkeyed node that is heard. The lane reads the doors (S8.1)
    before S2.7, and each later doors row waits up to 35 s for the room to be ready before it acts, a precondition and not
    a measured deadline. Whether a refused post for an unkeyed node should light red at all is Stagewatch's to decide with
    SW-INT-AG, when AGMessage holds a key. (b) Patterns' link to the rig restarts once it learns the rig's room name from
    its hello, so a row that pulls the rig's cable straight after the rig's restart (S6.1b) can catch that restart and read
    Patterns as off; S6.1 now ends only once Patterns follows the rig under its hello's name. That restart is Patterns'
    link's, to be named to its next round.
  - The design's later rows come with the work they read, each added once it passes (the VT rows, S4.1–S4.4, came with 2.20's
    playout links, SW20.13, above): windows on Patterns and the MCP desk rows (S5.1, S3.5, S3.7) with
    2.22's windows for peers, Xvfb, Mesa and Playwright joining the job with them; PlayMap's windows rows (S5.2, S5.3) with
    its PeerLink work; and AGMessage's desk, stuck and re-offer rows and one contract across the four copies (S1.3b, S3.1,
    S3.2, S3.8, S6.5, S7.2) with AGMessage's next releases. A release waits only on the rows in the lane.
- **The suite's timing races (SW21.9m).** Two checks failed once each in 2.21's whole runs on a loaded machine: each read two
  events as ordered that nothing ordered.
  - *Companion's push checks* ("pushed frame, no CONFIG when unchanged", "CONFIG re-sent on config change"). The product was
    wrong: the hub's worker pushing a frame and the client's reader answering `SUBSCRIBE JSON` (or `SNAPSHOT`, `CONFIG`,
    `STATE`) with the newest frame were two unordered writers to one client's queue, so a late push repeated 05:00 and could
    queue an older frame after a newer one, leaving a paused show's deck on a stale time. Both now take the client's frames
    lock: a push no newer (`Seq`) than the last frame queued is dropped; an answer reads the newest frame in the lock, carries
    the CONFIG a JSON subscriber has not had, and SUBSCRIBE turns JSON on in that hold. The two checks were left as they were;
    a new check holds the worker inside its push and fences the order (the moved CONFIG in the answer, no older frame after).
  - *"playout clock: a new connection starts with no sample"* (`PlayoutLinkTests`). The test was wrong: `Live` gave the desk's
    pong 300 ms of real time before the samples were read, and the clock was moved by the redial wait before the link had
    armed it; the link promised neither order. `Live` and the self-gap check now wait on the desk answering the ping stamped
    with that moment's wall clock and the link reading it (`FakeDesk.Answered`, `PingSettled`); each move by a redial or hold
    wait first waits for the wait to be armed (`RedialArmed`; Holds' 200 ms sleep stood for it). A wait whose event never
    comes fails as a check of its own.
  - *Found by the sweep, same shape:* OSC feedback sent its first full frame from the resolving thread beside the hub's
    worker, outside its lock, so an older frame could land after a newer; it now sends under the lock, only a newer `Seq`
    (the product was wrong). The playout scenario's per-second pong waits after the 32 silent commands ignored a 2 s bound;
    they now fold into its check. DeskWireTests' peer HTTP checks sent their first line beside a stand-in PlayMap started on
    `Task.Run`, before its first ask, and the desk rightly refused a line for an app that had not asked (four checks failed
    once under load here); the test was wrong and now waits on the desk's recorded ask (`LastCollect`).
  - *Read and left:* the WebSocket push (one sender per socket, reading the newest frame when woken); the WebSocket and OSC
    feedback checks in Program.cs, `CompanionV2Tests` and DeskWireTests' Companion lines (each reads past a repeat or reads
    after its first frame); `WebV2Tests` (reads wait on the frame with `WaitFor`/`WaitForAfter`); `ShowBusTests`,
    `PlayoutGateTests` and `WindowTests` (one publish before any client, read back from `Latest`); `PlayoutTlsTests` and
    the scenarios' S11 steps (absence checks; pongs off, timers fire inside `Advance`); the flapping check's 50 ms ping
    wait (a miss moves the clock 250 ms more, the count stays); the redial moves in PlayoutDirectorTests and the scenarios
    (already wait on the armed timer); tests/e2e (waits on frames and events). Left for a later unit: checks that sleep a
    while before reading a command's effect (Companion's verb routing 300 ms, OSC's commands 80 ms, CompanionV2's MESSAGE
    200 ms).
  - Proof: each race was put in by a hook and taken out again (the worker 1 s late, a newer OSC frame between the first
    frame's read and send, the desk's pongs 500 ms late, the redial armed 300 ms late, the stand-in PlayMap's first ask 1 s
    late): the old code failed under each and the mended code passed. Those hooks are gone; besides the new Companion check,
    the existing checks fail only under load. The Companion v2 block and PlayoutLinkTests then ran 20 rounds in a row beside
    a busy loop on every core, and the whole suite four more after the last mend.
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
