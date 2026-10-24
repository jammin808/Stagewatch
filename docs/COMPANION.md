# Bitfocus Companion

Stagewatch ships a full Companion module in [`companion-module/`](../companion-module). It is TypeScript on `@companion-module/base` **2.1.3**, which needs **Companion 5.0 or newer**. It speaks the TCP protocol below on port **9923**. Companion control is a **Pro** feature, included in the trial.

## Install the module

**From the package (operators):**
1. In Companion, open **Modules → Import module package** and choose `stagewatch-<version>.tgz` (the installer puts it in the Stagewatch folder under `Companion`; builds write it to `companion-module/`).
2. Add a **Stagewatch** connection and set the address (`127.0.0.1` on the same PC) and port 9923.
3. In Stagewatch, check that **Settings → Companion → Enabled** is on. For a second PC, also turn on **Bind all interfaces**.

**From source (developers):**
```pwsh
cd companion-module
npm install
npm test            # builds, then runs the unit + fake-server tests
npm run package     # → stagewatch-<version>.tgz (runs companion-module-build)
npx companion-module-check
```
For live development, point Companion's **Developer modules** folder (Launcher → cog → Advanced → Developer) at the folder containing `companion-module`. Then run `npm run dev`; Companion restarts the module whenever `dist/` changes.

What the module provides is listed in [`companion-module/companion/HELP.md`](../companion-module/companion/HELP.md): actions, feedbacks, variables, and auto-generated presets for every Stagewatch preset and running-order item.

## How it stays live and stable

```
Stagewatch (single authority)                         Companion module
  SnapshotHub ── on visible change (~1/s) ──► SNAPSHOT {json} ──► diff → one setVariableValues (changed only)
                                                                    └► one checkFeedbacks (affected ids only)
  config change (presets/messages/rundown) ─► CONFIG {json}   ──► labels via variables (instant);
                                                                    actions/presets rebuilt only if the
                                                                    show's shape changed (debounced 300 ms)
  button press ◄── TOGGLE / NEXT / NUDGE 60 … ◄── action callback (dropped, not queued, while offline)
```

**Server side (Stagewatch):**
- Every client has a bounded outbound queue (512 lines, oldest dropped) drained by its own writer task. Publishing never touches a socket.
- A client that stalls a write for 10 s is disconnected. A stuck or slow Companion can't block the engine or the UI; the tests publish 20,000 frames past a client that never reads.
- Each frame is serialized once, however many clients are connected.
- Every verb is executed by the app on its UI thread, the same single-writer path as the keyboard, HTTP and OSC.
- Limits: 32 clients, 4 KB maximum line length, TCP keepalive on.

**Module side:**
- Reconnects forever with backoff from 1 s up to 10 s.
- Sends a `PING <n>` heartbeat every 4 s and declares the link dead after 12 s of silence, so a pulled cable or a sleeping laptop is caught quickly.
- Offline, the timer variables read `OFFLINE` instead of freezing on a stale time.
- Presses made while offline are dropped, because replaying a stale START/NEXT after a reconnect could disrupt a show.
- Free mode is reported as *Insufficient permissions* along with the reason, and retries only every 30 s.

## Protocol

TCP, UTF-8, one command per line (`\n`; `\r` ignored). Quoted strings use `"…"` with `\"`, `\\` and `\n` escapes.

On connect the server sends `READY stagewatch/2`, then either `STATE …` (licensed) or `DENIED license "<reason>"` followed by a close (Free mode).

### v2 stream (used by the module)

| Send | Receive |
|---|---|
| `SUBSCRIBE JSON` | `CONFIG {json}` then `SNAPSHOT {json}` right away; afterwards `SNAPSHOT` on every visible change and `CONFIG` whenever presets, quick messages or the running order change |
| `SNAPSHOT` | one `SNAPSHOT {json}` |
| `CONFIG` | `CONFIG {json}` + `SNAPSHOT {json}` |
| `PING [token]` | `PONG [token]`, or `PONG [token] STUCK` when Stagewatch answers but its show has stopped updating (the module then shows a warning and greys the keys) |

`SNAPSHOT` is the same JSON as the HTTP `/api/state?detail=1` (see [REMOTE.md](REMOTE.md)):
- `mode`, `state`, `seconds`, `text`, `total`, `phase`, `color`
- `title`, `next`, `item`, `items`, `preset`
- `endTime`, `timeOfDay`, `offset`, `schedule`
- `message`, `urgency`, `flash`, `flashTimer`, `blackout`, `watermark`, `show{…}`, `configVersion`
- 2.0: `speakerText`, `cue`, `finishBy`, `preview`, `stretching`, `room`, `messageId`, `messageAck`, `messageAckBy`,
  `signals[]`, `slides`, `speech`, `playout`, `qaLive`, `qaPending`, `qaQueued`, `qaFit`, `hardOut`, `hardOutMargin`,
  `forecast`, `delay`, `clockOffsetMs`
- detail: `timings[]` (planned / expected / actual per item), `advice[]`, `questions[]`, `rooms[]`

`CONFIG` contains `{presets:[{slot,label,seconds}], messages:[{label,text,urgency,flash}], rundown:[{title,speaker,seconds}],
ladders:[{id,name}], departments:[…], room, ladder}`.

`RECORD <command line>` is sent to JSON clients whenever the operator does something in Stagewatch itself
(`nudge 60`, `next`, `message "Wrap up" warn slow`…). The Companion module turns these into actions while a
button is recording.

### Commands

| Command | Effect |
|---|---|
| `START`, `STOP` / `PAUSE`, `TOGGLE`, `RESET` | Transport |
| `SET <mm:ss \| hh:mm:ss \| secs>`, `SETMS <ms>` | Load duration |
| `NUDGE <±seconds>` | Add or remove time live |
| `ENDAT <HH:MM[:SS] \| clear> [elastic\|realtime]` | Finish at a wall-clock time. Elastic stretches the loaded duration; realtime counts the actual time left at normal speed. Omit to use the operator's setting |
| `MODE <countdown \| countup \| tod>` | Clock mode |
| `PRESET <1-12> [START]` | Load a preset |
| `NEXT`, `PREV`, `GOTO <n> [start]` | Running order |
| `MESSAGE "text" [info\|warn\|crit] [off\|slow\|fast]`, `CLEARMESSAGE` | Presenter message |
| `FLASH <off\|slow\|fast>`, `FLASH TIMER <on\|off>` | Flashing |
| `BLACKOUT [on\|off\|toggle]` | Hide outputs |
| `SIGNAL`, `ACK`, `TRIM`, `DELAY`, `CATCHUP`, `PREVIEW`, `LADDER`, `FORMAT`, `Q`, `REHEARSE`, `LISTEN`, `REPORT`, `NOTE` | The 2.0 commands — same grammar as HTTP/OSC (see REMOTE.md) |
| `OUTPUT`, `NDI`, `HOLDING`, `STYLE`, `LTC` | Outputs, holding images, the 3D display and LTC chase (2.2–2.7; see REMOTE.md) |
| `ROLL`, `RUNDOWN`, `DEPTNOTE` | Roll, switching running orders and department notes (2.9). Module: *Roll* and *make another live* actions, `roll` / `rundown` variables and feedbacks |
| `AUX`, `SCREEN` | Extra timers and browser screens (2.8; see REMOTE.md). Module: *Extra timer* actions and keys, `aux_1`…`aux_3` variables, *Screens: send / identify* |
| `UNDO`, `REDO`, `ENDOFSHOW <HH:MM\|clear>`, `LOOP <off\|n\|forever>`, `SCREEN RELOAD\|DISCONNECT [screen]` | 2.10. Module: *Undo the last live edit*, *Redo*, *Running order: show ends at*, *Running order: loop*, *Screens: reload / disconnect* |
| `WINDOW NEW\|SHOW\|CUE\|HOLD\|RELEASE\|IDENTIFY\|RELOAD\|LOSS\|FADE\|RENAME\|REVOKE\|DELETE …` | Windows Stagewatch controls (2.17; see REMOTE.md). Module: *Windows: show a page* (show or cue), *Windows: holding page* (on, toggle, words, image), *Windows: release*, *Windows: identify*; presets *holding page on every window (toggle)* and *identify all*. No window feedbacks yet: the module's frame doesn't carry windows |
| `STATE`, `SUBSCRIBE [JSON]`, `UNSUBSCRIBE`, `PING`, `QUIT` | Session |

Bad input gets `ERROR <reason>` and the connection stays open.

### v1 events (default; used by the PowerPoint add-in)

Without `SUBSCRIBE JSON`, clients receive the original stream:

```
TICK ms=<ms> formatted=<text> state=<state>      ~20 Hz while running
STATE state=<state> / MODE <mode> / MESSAGE "text" urgency=<u> flash=<f>
```
