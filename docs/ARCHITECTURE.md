# Architecture

```
┌────────────────────────────────────────────────────────────────────┐
│ Timer.App  (WPF, the .exe operators run)                           │
│  ┌──────────────────────┐    ┌──────────────────────┐              │
│  │ MainWindow + VM      │◀──▶│ Timer.Output         │              │
│  │ presets, nudge,      │    │ OutputWindow (WPF)   │──▶ NDI       │
│  │ messages, transport  │    │ ChromaKey + Floating │──▶ Monitor   │
│  └──────────┬───────────┘    └──────────────────────┘              │
│             │                                                      │
│             ▼                                                      │
│  ┌──────────────────────┐    ┌──────────────────────┐              │
│  │ Timer.Core           │    │ Timer.Companion      │              │
│  │ TimerEngine          │    │ TCP server :9923     │◀── BitFocus  │
│  │ AppSettings (JSON)   │    │ line protocol        │     Companion│
│  │ Presets, Messages    │    └──────────────────────┘              │
│  └──────────┬───────────┘                                          │
│             ▼                                                      │
│  ┌──────────────────────┐                                          │
│  │ Timer.Licensing      │                                          │
│  │ Trial, tiers,        │                                          │
│  │ ECDSA P-256 + AES-GCM│                                          │
│  │ HardwareId binding   │                                          │
│  └──────────────────────┘                                          │
└──────────┬─────────────────────────────────────────────────────────┘
           │
           │ loopback TCP :9923 (same Companion protocol — DRY)
           │
           ▼
┌────────────────────────────────────────────────────────────────────┐
│ Timer.PowerPoint  (VSTO add-in, lives inside POWERPNT.EXE)         │
│  ThisAddIn → connects to Stagewatch on startup, subscribes to TICK │
│  PresenterOverlay → frameless window, lower-left of presenter view │
│  TimerRibbon → "Show timer" toggle and "Recenter"                  │
└────────────────────────────────────────────────────────────────────┘
```

## Show loop (single authority)

```
inputs ──► MainViewModel (UI thread) ──► ShowSnapshot ──► SnapshotHub ──┬─► operator UI
  buttons, keys,      one writer          immutable,        latest-wins  ├─► OutputWindow / NDI
  Companion TCP,      executes            built only when                ├─► WebServer → browsers (WebSocket)
  HTTP, WebSocket,    ShowCommand         something visible               └─► OscServer → feedback
  OSC                                     changed (~1/s)
```

- Every transport parses into the same `ShowCommand` (`ShowCommandParser`), so there is one grammar.
- The engine ticks at 20 Hz; the frame is rebuilt only when the displayed second, state, message, title,
  schedule, etc. change. The progress bar alone updates per tick (render transform, no layout).
- Network sends are latest-wins with a one-slot mailbox per client and a 5 s send timeout: slow clients skip
  frames, stalled ones are dropped, nothing queues. Each snapshot is serialized once (`Utf8JsonWriter`).
- `SnapshotHub` isolates subscribers: one throwing consumer can't stop the others or the timer.

### The show controller (Timer.Show) and the view model

`ShowController` is the show's core, with no UI: the running-order cursor and every transition around it (load,
start, end, expiry and auto-advance, loops, Roll, switching running orders, the schedule's anchors), trims, delays,
department notes, undo (one history per running order) and session checkpoints. It owns the journal's identity stamping
(running order, item, execution).

- **One writer, still.** The controller is called on the owner thread only (the UI thread in the app) and never locks.
- **Narrow edge.** It asks its host for exactly what belongs outside it, through `IShowHost`: permission (licensing),
  a status line, and edge effects (an item's message on screens, triggers, chimes, the overrun history, rows to
  rebuild, a frame to publish). `MainViewModel` is that host, plus presentation: observable properties, rows, windows
  and outputs.
- **Time is injected.** `TimerEngine` and the controller take a `TimeProvider`; tests drive whole workflows on a
  manual clock (`tests/Stagewatch.Tests/ShowWorkflowTests.cs`): switching running orders, nudging during Roll, loop
  passes, auto-advance, recovery and undo, editing the running order mid-show, crash restore.
- **Identity, not position.** The cursor and the record of what ran follow items by id when the running order is
  edited, so deleting or moving an item above the live one never points the show at a different item.
- **No side effects on construction.** Building a `MainViewModel` opens no sockets: `StartServices()` starts the
  Companion server, remotes, OSC, integrations and the LTC poll once the window exists.
- **Frames are built by a pure function.** `FrameBuilder` (Timer.Show) turns a `FrameInput` — the tick, the controller,
  the settings, the preview and stretch state — into the show's part of a frame: cue phase and colours, flashing,
  the schedule projection, the speaker's face (honest clock, hide at zero), message placeholders, hard-outs, the
  forecast line, timings and groups. The host reads its sensors and outputs once into an "ambient" snapshot and the
  builder completes it, so a frame is still one immutable object. `Project` runs first because the host's forecast
  and advice are built from the projection.
- **Services, not view-model code.** Integration lifecycle is `IntegrationSupervisor` (Timer.Integrations: start,
  background retirement, re-apply when the old instance has let go, capped parallel shutdown); reports are
  `ShowReports` (Timer.Show: tier gating, build from the journal, render, safe file name); which holding image is on air
  is `HoldingSelection` (Timer.Core). The view model keeps only what is WPF: decoding bitmaps, thumbnails, windows.
- **What the AI sees is tested.** `AiContext` (Timer.Show) builds the JSON the models get: the copilot's context
  (operator frame, config, screens, link health, overrun history, the last 30 journal lines), the cloud assistant's
  compact show view, and the journal a show recap is drafted from (items named by identity across running orders).
  The view model gathers the facts (`CopilotFacts`) and keeps the proposal queue, which is UI.
- **Watchdog.** The first copy of Stagewatch is a supervisor (`Watchdog.Supervise`, no window) that runs the app as a
  child with `--child --beat <pipe>`; the child beats once a second from its UI thread (byte 2 = a show is live). The
  rules are `WatchdogPolicy` (Core, tested): restart at once, back off 1/2/5/10 s, give up after six failures in ten
  minutes, hung = 10 s without a beat while live (30 s otherwise, 90 s before the first). A restarted child gets
  `--recovered <note>` and restores the session without asking. UI faults are contained by `FaultCounter` (fatal or a
  storm → let it die, the watchdog brings it back). Saves go through `AtomicFile` (flush to disk, swap, `.bak` of the
  last whole file). Guard rails: `src/BannedSymbols.txt` (Thread.Sleep, GC.Collect, unbounded waits, TickCount) fails
  the build; deliberate uses on worker threads say why.
- **Show Bus.** `docs/SHOWBUS.md`. Frames are numbered by `SnapshotHub` (`FrameSeq` compares equal, so numbering never
  makes an unchanged frame look new). Receipts: `IShowCommands.ExecuteWithReceiptAsync` runs on the owner thread and
  collects refusals (`Gate`) and refs (message / signal ids). Context: `ShowContext` (Timer.Show, pure) builds the §6
  JSON once a second; `PeerContexts` (Core) holds what peers post; the Producer's Show network, the advice and every AI
  brief read them. `ClockOffsetEstimator` (Core) does the NTP-style offset.
- **TLS and the approval desk (Show Bus 1.2).** `WebServer` peeks the first byte of each connection: 0x16 → `SslStream`
  with `NodeCertificate` (App; DPAPI-protected PKCS#12 in `%APPDATA%\Stagewatch\tls`), anything else plain HTTP. How
  long it lives is `CertificateLife` (Remote, pure): 825 days in all, a day's backdating included, and replaced at a start
  when fewer than 30 days remain or its span is longer. `CertificateFiles` (Remote; NodeCertificate gives it only DPAPI and
  the making) keeps, replaces or makes it: a replacement is saved before it serves, one atomic swap of the PKCS#12 under
  the stored password, and every outcome is a `CertificateRead` the desk journals and puts on the status line — a
  replacement is a new fingerprint every pinned peer sees as CERTIFICATE CHANGED once.
  `TlsPins` (Core) is the pin store for outgoing TLS (Slides agent). `ProposalDesk` (Core, pure, thread-safe) is the
  shared approval queue; `MainViewModel.Desk.cs` mirrors it into the Copilot list (`CopilotProposal.SharedId`), runs
  desk-targeted lines through `RunWithReceipt`, and journals every decision. Node keys are bound one-to-one to node ids.
- **Windows (2.17).** `ScreenWindows` (Core, pure) holds the rules: tokens (130 bits, compared in constant time), what a
  window may show, `Apply` for every `window …` command (refusing a no-op with the reason), and the context items.
  `WindowDirector` (Remote) runs the commands for the app, the rig and the tests: it applies, saves (the app: settings),
  swaps an immutable list readers take, and tells `WebServer`, which pushes each window its assignment latest-wins on its
  own link, takes its `shown` reports, and lets go of a revoked address. The window page (`window.js`) is a small
  presentation loop: one link, two frames (the next page drawn behind the one on screen, then swapped), and its own
  holding page, black and link warning, so the fail-safe state needs no page and no network. Pages inside take frames
  from it (`SWShell`) stamped on a clock every page shares (`performance.timeOrigin`), so a relayed frame's countdowns
  stay exact.
- **Panels.** The side panels' arrangement is `PanelLayout` (Timer.Core: order, torn-out windows, width; every panel
  always somewhere, each once; repaired on load). `PanelDock` (Timer.App) only moves the tab contents between the
  strip and `PanelWindow`s to match. A torn-out panel is the same controls and bindings, re-parented: one view model,
  one frame loop, nothing duplicated.

### Contracts

- **Commands carry typed arguments.** Commands with several values (a signal's target and words, a department and
  its note, a screen action and which screens, an extra timer's action, LTC, acknowledgements, questions) carry a
  typed `ShowArgs` record (`SignalArgs`, `DeptNoteArgs`, `ScreenArgs`…), read with `command.Get<T>()`. The parser and
  the factories (`ShowCommand.Signal`, `.DeptNote`, `.Question`) set it; the older packed `Text` stays for
  compatibility, and the packed form is read in exactly one place (`ShowArgs.From`).
- **Focused interfaces.** `IShowControl` is the union of `IShowCommands` (run commands, frames, config),
  `IShowAccess`, `IShowCrew`, `IShowScreens`, `IShowReports`, `IShowAgents` and `ISlideFeeds`. Consumers depend on
  what they use: OSC on `IShowCommands`; MCP on commands, reports and agents; the web server on the whole surface.
- **Settings have an owner.** `SettingsStore.Settings` is the live object: the owner thread reads it and changes it
  only through `Update`. Other threads (web requests, the trigger worker) read `SettingsStore.Snapshot`, a private
  copy rebuilt after each change, so they never see a half-made change or a list mid-edit.
- **Frames are immutable all the way down.** `EquatableArray` copies the array it is given, so a caller writing to its
  array later can't change a frame that's already published.

## The bench (off Windows)

`tools/test.sh` runs the same lanes on Linux as CI's Linux job: the solution build (WPF compiles; nothing WPF runs), the
suite (`Bench.OnWindows` names what only Windows can run), the Companion module and 3D display tests, and the browser
workflows (`tests/e2e`, Playwright) against `tools/Stagewatch.Rig` — the real servers around a simulated show on one
owner thread. The Windows jobs keep the UI smoke tests (`tools/smoke.ps1`), the installer and the PowerPoint add-in.

## Why these choices

- **WPF over WinUI 3 / MAUI**: WPF still has the cleanest path to fast multi-monitor full-screen output on Windows 10/11, and no MSIX packaging weirdness for IT-restricted laptops.
- **C# everywhere**: VSTO add-ins are .NET Framework 4.7.2+; rest of the app is .NET 10 (LTS, supported to November 2028). Same language, two TFMs.
- **Loopback TCP for IPC** between the main app and the PowerPoint add-in: it's the same protocol the Companion server already speaks, so we don't build two transports. Ports collide if both clients try to listen — only the main app listens; the add-in is a client.
- **JSON settings, debounced auto-save**: text format is editable for support, atomic temp-file write avoids torn files, debounce avoids hammering disk on every nudge.
- **Drift-free engine**: reference timestamp + elapsed delta, never `+= dt`. 50 ms tick keeps the seconds digit smooth without burning CPU.

## Threads

- UI thread (WPF Dispatcher): all UI mutation, view models, output window updates.
- Engine ticker (System.Threading.Timer): raises `Tick` events; the App marshals to UI.
- Companion server: each client runs an async loop on a thread-pool thread.
- Web server (Timer.Remote): raw-socket HTTP/1.1 + WebSocket on the thread pool — no HttpListener URL ACLs, no
  admin rights, no ASP.NET runtime. 64-connection cap, 8 KB header cap, 5 s header timeout.
- OSC server: one UDP receive loop; feedback goes out on a separate socket.
- NDI sender: `CompositionTarget.Rendering` callback on UI thread (cheap; we render at FPS).

### Latency boundaries

The show thread never waits on anything slow:

- **Frame consumers run off the show thread.** `SnapshotHub.Publish` only swaps a reference in each consumer's
  one-slot mailbox and, if that consumer is idle, queues one work item. Companion, the web server and OSC feedback
  handle frames on workers; a consumer that falls behind skips to the newest frame (latest-wins) and sees frames in
  order. A consumer slower than 100 ms on a frame is reported in Diagnostics (*Slow frame consumer*); one that throws
  is isolated.
- **Integrations stop in the background.** Changing an integration's settings disposes the old instance on a task;
  its replacement starts on the owner thread once the old one has released its port or device, so there are no
  "address in use" races. Settings that change again meanwhile are applied when the retirement finishes. On exit,
  integrations stop in parallel with a 3 s cap.
- **Time is measured monotonically.** The engine's countdowns use the time provider's monotonic timestamp, so a UI
  stall delays a frame but never changes the time a frame shows.

## Settings location

```
%APPDATA%\Stagewatch\settings.json    AppSettings (presets, message bank, output config, etc.)
%APPDATA%\Stagewatch\license.dat      AES-GCM-encrypted, hwid-bound license
%APPDATA%\Stagewatch\trial.dat        Encrypted trial start anchor (1 of 3 anchors)
%APPDATA%\Stagewatch\crash.log        Last unhandled WPF exception
```

## Stagewatch 2.0: systems, sensors and renderers

2.0 keeps the single-authority loop and organises everything new around it the way a game engine does:

```
 sensors (own threads, latest-wins)        systems (owner thread, once per frame)          renderers (read the snapshot)
 ─────────────────────────────────         ────────────────────────────────────────         ─────────────────────────────
 PowerPoint (COM, 4 Hz, STA)  ─┐           CueLadder        → phase, colour, cue label  ┌─► WPF operator UI + output window
 vMix playout (HTTP, 4 Hz)     │           ScheduleEngine   → planned/expected/actual   ├─► NDI
 Speech (System.Speech)        ├─► frame ─►SignalBoard      → cues + acknowledgements   ├─► browsers (basic / detail frames)
 Time server (SNTP, 10 min)    │   build   QaBoard          → audience Q&A              ├─► Companion (detail frames, RECORD)
 Rooms (UDP 9927 listener)    ─┘           Forecaster       → P50 / P80 (thread pool)   ├─► OSC feedback, MCP
                                           RuleAdvisor (+ optional Claude, thread pool) ├─► cue lights (Art-Net / sACN / Hue, 30 Hz)
                                           ShowJournal      → event log (async writer)  ├─► LTC (audio stream, own thread)
                                                                                         ├─► triggers (OSC / HTTP / Companion)
                                                                                         └─► room beacon (UDP 1 Hz)
```

- **Pure systems in `Timer.Core`** (`CueLadder`, `ScheduleEngine`, `SignalBoard`, `QaBoard`, `Forecaster`,
  `RuleAdvisor`, `GhostRun`, `ScriptTracker`, `ShowReport`): no clocks, no I/O, exhaustively unit-tested.
  `ScheduleEngine.Project` is a pure function of (plan, what happened, now).
- **Sensors in `Timer.Integrations`** run on their own threads (`PollingSensor<T>`) and expose a latest-wins
  value (`Latest<T>`, a volatile reference swap). The frame reads them like a game reads input state — a missing
  or slow device can never stall the timer.
- **Renderers** subscribe to the immutable snapshot or read `SnapshotHub.Latest`; none of them writes show state.
  The cue-light output runs its own 30 Hz timer (flashing, DMX keep-alive) and never overlaps itself.
- **The journal is an audit log, with session checkpoints beside it**: every start, end, cue, acknowledgement,
  trim and delay is appended (owner thread, O(1)) and streamed to JSON-lines by a background writer. It records
  events, not every state change, so it explains a show rather than rebuilding one; crash recovery restores state
  from the session checkpoint and re-opens the same journal. Each entry about an item carries stable identities:
  the running order's id, the item's id and an execution id that is new for every run of the item (Next, Go to,
  Roll, each loop pass). Reports and the copilot's summary match entries by identity, never by position, so
  switching running orders or reordering items can't move history onto another item. Journals from before
  identities fall back to position.
- **Value equality keeps traffic low**: lists in the snapshot use `EquatableArray<T>`, ages are quantised (5 s),
  forecasts to the minute, pace to 5 s — so an unchanged show produces no new frames.
- **Two frame levels**: viewers get the basic frame; producer pages, Companion and MCP ask for the detail frame
  (projected running order, advice, Q&A, rooms). Each level is serialised once per change.

### Threading rule (enforced)

Network loops are started with `Task.Run` (web, Companion, OSC, rooms). Starting an `async` loop directly from
the UI thread would capture the WPF dispatcher and run every continuation — every HTTP request — on the show
thread. Entry points that can be reached from network threads (`SetMessage`, `SetFlash`, `Report`) check
`Dispatcher.CheckAccess()` and marshal to the owner thread.
