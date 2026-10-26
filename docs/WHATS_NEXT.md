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
  stage-message work in 2.20 (message signals and acknowledgements on the rig).
- **The rig isn't the interop lane's yet.** Each run makes a new node id, it prints its PIN and the node keys it made
  in its ready line, it has no line to read the lockout and doesn't stop when its input closes. *Since then (2.19's lane
  track):* `--node-id`, `--journal` with `/report`, `--pin-file`, `--secrets-out`, `--exit-on-eof` and the `auth` and
  `exit` control lines (`docs/BUILD.md`, `tests/e2e/rig-faults.e2e.cjs`). The lane that drives it against the real
  apps is still to come.
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
  lane. *Since then:* Show Bus 1.3 §7 and §7.1 have the words.
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
- **Nothing speaks PeerLink yet.** Stagewatch's PeerLink core, the vectors and the scripted peer run in the suite only: `/ws`
  node links still prove a bearer key or the operator's credential, the Patterns link still pairs by `NONCE` and `AUTH-PROOF`,
  the hello carries no cap `peerlink`, and PeerLink's wrong proofs (13.5.6) count nowhere. The playout links and `/ws` take
  LINK in later releases (Show Bus §8.1).
- **Find still listens on Patterns' beacon.** Drop UDP 9700 from Find once Patterns' 9928 walk (its OPEN row L47) is filled:
  the release after that walk row is filled takes it, with Find reading `ports.wire` (today it reads the top-level `wire` a
  Patterns hello still sends, so a hello that named its wire only in `ports` would not be listed). `NodesDocTests` holds this row
  here while Find listens on 9700.
- **No button gives an install a new id** (the upgrade notes say how by hand). The desk's conflict words tell the operator to.
- **A conflict reaches the desk, the advice and the AI's facts, not the context or Companion.** By §6, an id two machines say has
  no `peers` row in Stagewatch's context, so other apps reading it and MCP's `get_context` do not list it, and the Companion
  module reads no peers at all.
- **Stagewatch dials no link by hello.** §2's "dial 127.0.0.1 first" for a peer heard on this machine is not done: its one link,
  to Patterns, is a typed address.
- **As a reader of contexts, Stagewatch keeps 2.18's limits**: a context's first 64 items and 64 commands, a verb cut at 80
  characters, no links read, and an over-long post closed unanswered rather than refused with 413 (§8.1, with what ends each).
- **A non-English show fills its context sooner.** The context is written with the default JSON encoder, which writes each
  character outside ASCII as a six-byte escape, so such a show reaches 64 KiB with about a third of an English show's text. A
  relaxed encoder waits for every reader's parser to be measured with it.
- **The presenter PC's choice of certificate has no test of its own**: it needs a TLS Stagewatch on the bench.
- **LB-1's Windows rows are not run** (below): 1.3 names no carrier that finds apps on one machine with no network.
- **The interop lane is a track of its own.** The rig's options for it landed (2.18's list, above); the lane that runs the real
  apps against each other, and CI's job for it, come after.
- **The agent brief's doctrine line** speaks of one lockout; since 2.19 there are two counts (wrong PINs; wrong view passes). It
  is the maintainer's to change.

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
