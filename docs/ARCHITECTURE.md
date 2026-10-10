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
- **The journal is event sourcing**: every start, end, cue, acknowledgement, trim and delay is appended (owner
  thread, O(1)) and streamed to JSON-lines by a background writer. Reports and the forecaster's history are
  built from it; crash recovery re-opens the same journal.
- **Value equality keeps traffic low**: lists in the snapshot use `EquatableArray<T>`, ages are quantised (5 s),
  forecasts to the minute, pace to 5 s — so an unchanged show produces no new frames.
- **Two frame levels**: viewers get the basic frame; producer pages, Companion and MCP ask for the detail frame
  (projected running order, advice, Q&A, rooms). Each level is serialised once per change.

### Threading rule (enforced)

Network loops are started with `Task.Run` (web, Companion, OSC, rooms). Starting an `async` loop directly from
the UI thread would capture the WPF dispatcher and run every continuation — every HTTP request — on the show
thread. Entry points that can be reached from network threads (`SetMessage`, `SetFlash`, `Report`) check
`Dispatcher.CheckAccess()` and marshal to the owner thread.
