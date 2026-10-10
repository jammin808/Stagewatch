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
