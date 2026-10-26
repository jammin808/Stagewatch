# Stagewatch

Pro countdown timer and show-calling tool for corporate events. Windows 10/11. Part of the **Patterns** show control ecosystem.

2.0 closes the loop around the clock: cue ladders and acknowledged cues, department STANDBY/GO and green-room calls, schedule recovery with curfews and an assistant that proposes trims, a probabilistic end-of-show forecast, PowerPoint slide and rehearsal awareness, speech-to-script pacing, a Showcaller/Producer page, audience Q&A, multi-room dashboard, DMX/Hue cue lights, LTC, triggers, planned-vs-actual reports and an MCP endpoint for AI tools. See [`docs/FEATURES.md`](docs/FEATURES.md).

## Quick links

- [`docs/ANALYSIS.md`](docs/ANALYSIS.md) — IrisDown analysis & how Stagewatch differentiates
- [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) — tech architecture
- [`docs/BUILD.md`](docs/BUILD.md) — how to build, sign, package
- [`docs/LICENSING.md`](docs/LICENSING.md) — trial / tiers / master serial / threat model
- [`docs/COMPANION.md`](docs/COMPANION.md) — BitFocus Companion module spec
- [`docs/POWERPOINT.md`](docs/POWERPOINT.md) — installing the PowerPoint add-in
- [`docs/REMOTE.md`](docs/REMOTE.md) — browser pages, windows Stagewatch controls (`/w/<token>`), show apps on `/ws` (node links, one key per app), Show Bus 1.3's changes (2.19), HTTP API, WebSocket, OSC, MCP, running order
- [`docs/NODES.md`](docs/NODES.md) — Stagewatch's node page, written from the code and held to it by the suite: the hello it says and what it hears, presenter PCs, reading and driving it, each show app's link, the Patterns link, its numbers
- [`docs/SHOWBUS.md`](docs/SHOWBUS.md) — Show Bus 1.3, the contract between Stagewatch, Patterns, PlayMap and AGMessage (this tree holds the master)
- [`docs/ECOSYSTEM.md`](docs/ECOSYSTEM.md) — why the four apps share one contract, and where it is going
- [`docs/FEATURES.md`](docs/FEATURES.md) — what's new in 2.0 and how to use it

## Solution layout

```
Timer.sln
src/
  Timer.Core/         core models, timer engine, settings persistence
  Timer.Licensing/    trial, tiered features, signed licenses, tamper guard
  Timer.Companion/    TCP/UDP server for BitFocus Companion
  Timer.Output/       output window, chroma key, NDI sender
  Timer.Remote/       browser pages + HTTP/WebSocket API, OSC, MCP (no extra runtime)
  Timer.Integrations/ PowerPoint, vMix, speech, time server, LTC, DMX/Hue, triggers, rooms, assistant
  Timer.App/          WPF admin UI (the main exe)
  Timer.PowerPoint/   VSTO ribbon + presenter overlay
companion-module/     Bitfocus Companion module (TypeScript, @companion-module/base 2.1)
tools/
  KeyGen/             owner-only license generator (kept on the maintainer's PC, never in the repo)
  Stagewatch.Rig/     the headless rig: the real servers around a simulated show, on any OS
  BusCarrierProbe/    lab line LB-1: which carrier finds apps on one machine with no network (docs/WHATS_NEXT.md)
  showbus/            Show Bus 1.3's scripted peer, which every tree that copies the contract carries (docs/SHOWBUS.md §13.20)
  showbus-copies.sh   the copies check: holds each tree the contract's Landed line names to this master (§14.5)
  setup-dev.sh, test.sh   the Linux bench: install, then every test lane (docs/BUILD.md)
tests/
  Stagewatch.Tests/   the suite (dotnet run); e2e/ browser workflows (Playwright); UiSmoke/, PageShot/ smoke
installer/
  Stagewatch.iss      Inno Setup script
```

## Release (one command)

```pwsh
pwsh tools\release.ps1              # bump patch versions, test, publish, package the Companion module, build the installer
pwsh tools\release.ps1 -Bump minor  # or -Version 3.0.0 -ModuleVersion 3.0.0, or -NoBump
```

## Build (TL;DR)

```pwsh
# Prereqs: .NET 10 SDK (LTS), Visual Studio Build Tools 2022 (for VSTO),
#          NDI SDK 5+ (set NDI_SDK_DIR env var), Inno Setup 6 (installer).
dotnet restore
dotnet build -c Release
# Then run from src/Timer.App or use the installer in installer/.
```

See `docs/BUILD.md` for the full build, signing, and packaging procedure.

## Tiers at a glance

One-off licences, offline activation. Production covers 2 machines (main + backup); early adopters get lifetime
updates, later Production updates are £20/year (the last entitled version keeps working). A 30-day Production
event pass is £15 (it warns before it ends and has 2 days' grace, so an event is never cut short). Charities,
churches and education: 30% off (Standard £6, Pro £20, Production £55, pass £11); 5-packs 20% off for rental houses.
Details: [`docs/LICENSING.md`](docs/LICENSING.md).

| Feature | Trial (14 days) | Free (after trial) | Standard £9 | Pro £29 | Production £79 (launch £59) |
|---|---|---|---|---|---|
| Countdown / count-up / time-of-day, start/stop/reset, live nudge | ✅ | ✅ | ✅ | ✅ | ✅ |
| Full-screen output window | ✅ | ✅ (watermarked) | ✅ | ✅ | ✅ |
| Typed messages, warning colours, progress bar, blackout | ✅ | ✅ | ✅ | ✅ | ✅ |
| Show files (save / open per event), 12-hour clock, crash recovery | ✅ | ✅ | ✅ | ✅ | ✅ |
| Presets | 12 | 2 | 12 | 12 | 12 |
| Quick messages, sound alerts, end-at (elastic) timing | ✅ | ❌ | ✅ | ✅ | ✅ |
| Chroma-key / floating output, NDI output (needs the free NDI Runtime) | ✅ | ❌ | ✅ | ✅ | ✅ |
| Holding images on every screen (output, NDI, browser screens) | ✅ | ❌ | ✅ | ✅ | ✅ |
| Windows: browser screens Stagewatch controls at their own addresses, with a holding page | ✅ | ❌ | ❌ | ✅ | ✅ |
| Cue ladders, cue label, calm clock, colour-blind colours, briefing preview, placeholders | ✅ | ❌ | ✅ | ✅ | ✅ |
| Running order (Next, auto-advance, ahead/behind, CSV import), schedule recovery, curfews | ✅ | ❌ | ❌ | ✅ | ✅ |
| Browser viewer, remote and role pages, HTTP API, OSC, Bitfocus Companion | ✅ | ❌ | ❌ | ✅ | ✅ |
| PowerPoint slides + rehearsal ghost, Q&A, triggers, CSV report | ✅ | ❌ | ❌ | ✅ | ✅ |
| Holographic 3D display (three.js) on the output and browser screens | ✅ | ❌ | ❌ | ✅ | ✅ |
| Acknowledged cues, departments STANDBY/GO, green-room calls, pocket buzz | ✅ | ❌ | ❌ | ❌ | ✅ |
| End-of-show forecast, curfew trim suggestions, producer assistant | ✅ | ❌ | ❌ | ❌ | ✅ |
| Multi-room dashboard, cue lights (Art-Net / sACN / Hue), LTC, time-server drift check | ✅ | ❌ | ❌ | ❌ | ✅ |
| vMix VT countdowns, speech pacing, full printable report, MCP | ✅ | ❌ | ❌ | ❌ | ✅ |
| Machines per licence | — | — | 1 | 1 | 2 (main + backup) |
| Watermark "StageWatch by Ben Rohleder" on every output | ❌ | ✅ | ❌ | ❌ | ❌ |

## Master serial

Master serials are owner-signed (see `docs/LICENSING.md`). Yours lives in `%USERPROFILE%\.stagewatch\master-serial.txt` — never in this repo.
