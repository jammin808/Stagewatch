# Stagewatch — the agent brief

Read this before any change. The papers people read are `README.md` and `docs/`; this is for whoever works the tree.

## What this is

Stagewatch is a pro countdown timer and show-calling tool for live events: one portable Windows exe (WPF, .NET 10) with
a speaker output window (NDI, chroma key), browser pages for every role, windows it controls at their own addresses
(`/w/<token>`), a Bitfocus Companion module, OSC, HTTP and WebSocket APIs, an MCP endpoint, a PowerPoint add-in (VSTO,
.NET Framework 4.7.2) and the micro-apps. Part of the Patterns ecosystem with Patterns, PlayMap (repo Mapper) and
AGMessage; the contract between them is `docs/SHOWBUS.md`. `docs/ARCHITECTURE.md` is the design; read it first.

## Where development happens

The maintainer develops on a Windows PC (`C:\Users\User\Documents\Timer`), where the PowerPoint add-in, the installer,
the WPF desk and anything that needs Windows, Office or a GPU is built and run. A cloud session sees only GitHub
(`jammin808/Stagewatch`): work done on the PC reaches it once it is pushed. Before building on top of the PC's work, check
that it has been pushed; a cloud session cannot read the PC's folder. Work that needs the PC (the add-in, plugins, UI
walk-throughs, the installer) is named as such and left for it, never imitated.

## The standing brief (the maintainer's)

Speed, user experience, security and stability, taken from the latest gaming and medical practice: a single writer and
immutable frames (a game's simulation and presentation split), latest-wins delivery, nothing slow on the show thread,
fail-safe states drawn locally (a screen never shows a confident stale time), observed state over sent state, bounded
everything, deterministic tests on injected time. Every selection and view change is instant. Anything done is followed
down every chain it affects — Core, the show controller, the desk (buttons and right-click menus in step), the wire (one
grammar for HTTP, WebSocket, OSC, Companion, MCP), the browser pages, the Companion module, the context (the Eye, AI),
the copilot's risk, the papers.

## The doctrine

- **Attempts are not facts.** Sent → applied → observed. A command that would change nothing is refused with the reason
  (`lobby already shows /clock`); receipts say applied, refused or only accepted. A window is green only when it says it
  shows the revision it was sent. Tests assert fields and published state, never words alone.
- **One writer.** The show thread (`MainViewModel`, the rig's owner thread) runs every command; other threads read
  immutable snapshots (`SnapshotHub`, `SettingsStore.Snapshot`, the window list).
- **Fail closed on the wire.** An unknown word is refused, never defaulted; the PIN, keys and window addresses ride in no
  log line, no journal row and no frame; wrong PINs, viewing codes and window addresses lock a device out, and unknown or
  revoked keys never count toward that lockout (a key cannot be guessed; counting it only lets a peer's old key lock out
  its own machine's PIN).
- **Measured, then governed.** No limit or governor without a measured cause and a deterministic test.

## Constraints that bite

- **One `dotnet build` at a time** in a tree. A `--no-build` test run is safe beside edits.
- **The guard rails are build failures** (`src/BannedSymbols.txt`, `TreatWarningsAsErrors`): `Thread.Sleep`,
  `GC.Collect`, unbounded `Task.Wait`/`WaitAll` and `Environment.TickCount` are banned in `src/`; a deliberate use on a
  worker thread says why with `#pragma warning disable RS0030`. Culture-invariant parsing and formatting.
- **Windows-only checks** go through `Bench.OnWindows("what")` in the suite: on Windows they run; elsewhere they are
  named in the summary and never counted as passed. Never skip, disable or quarantine a test to get green.
- **Frames and the wire.** STATE frames are hand-written JSON (`SnapshotJson`); tests match some fields as substrings, so
  a new field goes at the end of its object. New commands are a `ShowCommandKind` appended at the end, a parser verb, a
  help line, the copilot's risk (`Copilot.Classify`) and, when peers or AI may propose them, `ShowContext.Commands`.
- **Comments state the invariant**, not the history. No model or tool identifiers in anything pushed.
- **Line endings.** `.gitattributes` keeps `.ps1`, `.bat`, `.iss` and `.sln` CRLF and `.sh` LF. `docs/BUILD.md` is stored
  with CRLF: stage it byte for byte (`git hash-object -w --no-filters` and `git update-index --cacheinfo`) so a change
  doesn't rewrite every line.
- **Never** commit the licence key generator, signing keys or the master serial (`.gitignore` names them).

## The loop

```
tools/setup-dev.sh                 # Linux: .NET 10 SDK, Node packages, Chromium's headless shell
tools/test.sh                      # build, unit, module, holo, e2e (or name lanes: tools/test.sh unit e2e)
tools/test.sh pageshot | rig       # PageShot's screenshots; the headless rig in the foreground
dotnet run --project tests/Stagewatch.Tests -c Debug --no-build      # the suite alone
pwsh tools\smoke.ps1 · pwsh tools\release.ps1 -NoBump                # Windows: UI smoke; build, test, package
```

CI (`.github/workflows/ci.yml`): `build-test-package` and `smoke` on Windows, `linux` (tools/test.sh with the browser
workflows and PageShot), and `release` on a `v*` tag once all three are green.

## Where things live

| What | Where |
|---|---|
| Models, the engine, the grammar (`ShowCommandParser`), frames, settings, pure systems, window rules (`ScreenWindows`) | `src/Timer.Core` |
| The show controller, frame builder, context (`ShowContext`), what AI sees (`AiContext`), reports | `src/Timer.Show` |
| Web server, pages (`wwwroot`), windows (`WindowDirector`, `window.js`), OSC, MCP | `src/Timer.Remote` |
| The desk (WPF): `MainViewModel.*.cs` is the executor; Screens window (browsers and windows) | `src/Timer.App` |
| Companion TCP server; the module (TypeScript) | `src/Timer.Companion`; `companion-module` |
| Integrations, copilot (`Copilot.Classify` is the risk), sensors | `src/Timer.Integrations` |
| The suite; browser workflows; UI and page smoke | `tests/Stagewatch.Tests`; `tests/e2e`; `tests/Stagewatch.UiSmoke`, `tests/Stagewatch.PageShot` |
| The headless rig (real servers, simulated show) | `tools/Stagewatch.Rig` |
| The wire and pages; Companion; build; features; what's next | `docs/REMOTE.md`; `docs/COMPANION.md`; `docs/BUILD.md`; `docs/FEATURES.md`; `docs/WHATS_NEXT.md` |

## House words

**the show thread** — the one writer; **a frame** — an immutable `ShowSnapshot`, serialized once per change; **a
screen** — any browser showing Stagewatch; **a window** — a browser Stagewatch controls at its own address;
**the holding page** — what a window shows when held or when its link drops, drawn by the window itself; **a receipt**
— what a command did (Show Bus §3); **the context** — Show Bus §6, what the Eye and an AI read; **the rig** — the
headless servers around a simulated show; **a lane** — one of `tools/test.sh`'s steps.
