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
  — wait a minute".
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
  in its ready line, it has no line to read the lockout and doesn't stop when its input closes. A stable id, a journal
  and its report, secrets off standard output and the lockout readable come with the interop lane's rig (a lane track
  beside 2.19).
- **Settings' key list doesn't refresh while it is open.** A binding made in the background shows after the next key
  action, or when Settings is opened again.
- **What the Patterns link set aside isn't shown.** Lines a desk sent before pairing are counted, not published; the
  playout links' link record (2.20) is where that count goes.
- **No Show Bus text.** `docs/SHOWBUS.md` doesn't describe node links, the verdict or one key per node yet; those words
  go into Show Bus 1.3 (2.19). Until then REMOTE.md's 2.18 section is the reference, and `tests/e2e/nodes.e2e.cjs` and
  `stall.e2e.cjs` pin the bytes against the rig (`tools/test.sh e2e`; `SW_RIG_ARGS="--node-keys 2" tools/test.sh rig`
  runs a rig with node keys by hand). The interop rows that replay them against the real apps come with the interop
  lane.
- **The agent brief's doctrine line** still says keys lock a device out; it is the maintainer's to change (keys no
  longer count, wrong PINs, viewing codes and window addresses do).
