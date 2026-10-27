# Bitfocus Companion

Stagewatch ships a full Companion module in [`companion-module/`](../companion-module). It is TypeScript on `@companion-module/base` **2.1.3**, which needs **Companion 5.0 or newer**. It speaks the TCP protocol below on port **9923**. Companion control is a **Pro** feature, included in the trial.

## Install the module

**From the package (operators):**
1. In Companion, open **Modules → Import module package** and choose `stagewatch-<version>.tgz` (the installer puts it in the Stagewatch folder under `Companion`; builds write it to `companion-module/`).
2. Add a **Stagewatch** connection and set the address (`127.0.0.1` on the same PC) and port 9923.
3. In Stagewatch, check that **Settings → Companion → Enabled** is on. For a second PC, also turn on **Bind all interfaces**; its Patterns (and playout) buttons also need that PC's address in **Settings → Remote → May drive playout** (2.20; `remote.playoutFrom` in the settings file), since Companion TCP carries no PIN — until then each press is refused with those words in the module's log. List addresses only on a show network you trust: an address is all Companion TCP proves.

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
- Sends a `PING <n>` heartbeat every 2 s and declares the link dead after 12 s of silence, so a pulled cable or a sleeping laptop is caught quickly.
- Offline, the timer variables read `OFFLINE` instead of freezing on a stale time.
- Presses made while offline are dropped, because replaying a stale START/NEXT after a reconnect could disrupt a show.
- A press that drives another app's air (`PLAYOUT …`, `PATTERNS …`) goes with a request id, and Stagewatch's answer to it is read
  and shown, never assumed (2.20, below). At most 64 such lines wait for an answer at once (the oldest is forgotten, never resent),
  none longer than 40 s.
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
ladders:[{id,name}], departments:[…], room, ladder}`, and from 2.20 `playoutLinks:[{name,kind,label}]` last: each playout link
switched on under the licence, its kind as its peer's STATE said it (`playmap`, `patterns`; as Settings set it until then, empty
for neither) and its peer as people read it (`PlayMap Stage left`, `Patterns`). A link whose kind is heard sends `CONFIG` again.
The snapshot's `playout` (the clip feeding the VT) ends with `link` and `node`: the link's name and its peer's node id.

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
| `AUX`, `SCREEN` | Extra timers and browser screens (2.8; see REMOTE.md). Module: *Extra timer* actions and keys, `aux_1`…`aux_3` variables, *Screens: send / identify*. From 2.18 screen commands reach browser screens only (below) |
| `UNDO`, `REDO`, `ENDOFSHOW <HH:MM\|clear>`, `LOOP <off\|n\|forever>`, `SCREEN RELOAD\|DISCONNECT [screen]` | 2.10. Module: *Undo the last live edit*, *Redo*, *Running order: show ends at*, *Running order: loop*, *Screens: reload / disconnect* |
| `WINDOW NEW\|SHOW\|CUE\|HOLD\|RELEASE\|IDENTIFY\|RELOAD\|LOSS\|FADE\|RENAME\|REVOKE\|DELETE …` | Windows Stagewatch controls (2.17; see REMOTE.md). Module: *Windows: show a page* (show or cue), *Windows: holding page* (on, toggle, words, image), *Windows: release*, *Windows: identify*; presets *holding page on every window (toggle)* and *identify all*. No window feedbacks yet: the module's frame doesn't carry windows |
| `PLAYOUT [<link>] END [secs]\|RESTART\|PLAY\|PAUSE\|STOP\|SEEK <m:ss>`, `PLAYOUT <link> GO\|CUE …`, `PATTERNS …` | The clip on air, a PlayMap node's playlist items and a Patterns desk's cues over Stagewatch's playout links (2.20; see REMOTE.md), from this PC or an address in May drive playout. Module (2.20): *Playout: the clip on air*, *Playout: GO*, *Playout: cue* and the *Patterns* actions, each sent with a request id; `vt_link`, `vt_source` and `playout_answer`; the *Playout: The VT comes from…* feedback; the *Playout links* presets (below) |
| `STATE`, `SUBSCRIBE [JSON]`, `UNSUBSCRIBE`, `PING`, `QUIT` | Session |

**Screen commands reach browsers only (2.18).** `SCREEN IDENTIFY|RELOAD|DISCONNECT|GO` with no screen, or `all`, reach
every browser screen that isn't a control page. A show app (Patterns, PlayMap, AGMessage) on Stagewatch's `/ws` with a
node key, or on a node link (REMOTE.md, 2.18), is never a screen, and a socket that only claims to be one is never part
of "all" (it can still be named by its own screen id). Until each app dials as a node, one on a control key or the PIN
is still a browser screen, and "all" reaches it. Naming a show app, in any `SCREEN` command, is refused:
`@a1 SCREEN IDENTIFY 9f8e7d6c` → `ERROR @a1 Screens: 9f8e7d6c is a show app (Patterns FOH), not a screen — screen
commands reach browsers only (Producer → Show network)`. The module's four screen actions say so: the *Screen name or id*
field reads "empty = all browser screens", and each description adds that show apps and unproven claims are never
included (*Screens: disconnect*: revoking a show app's key is the wall). The preset is *Screens: identify all browser
screens*.

**A playout line's receipt is its peer's answer (2.20).** With a request id, `@c1 PLAYOUT stage-left go pl1 it1` is answered
in the node's own words, never a "done" it did not say: `OK @c1 applied PlayMap Stage left took PLAYLIST GO pl1 it1`, or
`ERROR @c1 PlayMap Stage left refused PLAYLIST GO pl1 it9: Main show has no item it9`. A node that takes it and runs it later
answers `OK @c1 accepted PlayMap Stage left: starting Intro`, and then comes its one follow-up, the word `final` after the id:
`OK @c1 final applied PlayMap Stage left took PLAYLIST GO pl1 it1 (playing Intro)` (or `ERROR @c1 final …`), within 30 s of
the send — the node's final word, never a later accepted; when it gives none by then, Stagewatch's own: `OK @c1 final accepted
unconfirmed — PlayMap Stage left took it and gave no final answer in 30 s — it may have run; look at its state`. A line
Stagewatch refuses from the peer's own facts (a Patterns
playlist item's pause, a PlayMap playlist not idle for a CUE) is `ERROR @c1 <why>`, and nothing reaches the peer. A client
that reads `OK @id` lines takes `final` after the id as the follow-up's mark, as the module does (below).

**The module's playout keys (2.20).** Three actions drive Stagewatch's playout links: *Playout: the clip on air* (`PLAYOUT
[<link>] play|pause|stop|restart|end <s>|seek <m:ss>`), *Playout: GO* (`PLAYOUT [<link>] go <playlist> [<item>] [together]` for a
PlayMap node, `go <cue>` for a Patterns desk) and *Playout: cue* (`cue <playlist> <item>`, or `cue <cue>`). The link is picked
from the config's `playoutLinks` (by name, its peer's name beside it) or typed; none named sends no link, so Stagewatch takes the
only link switched on, else (for the clip on air) the link feeding the VT, and otherwise refuses, naming the links. They exist
when the config carries `playoutLinks` (Stagewatch 2.20 or later) and the licence carries playout (`features` holds `vmix`); an
empty list keeps them, so with every link switched off a press is refused in Stagewatch's words rather than doing nothing; before
any `CONFIG` they stay, so a button keeps its action while Stagewatch is away. A name Stagewatch would read as an action, `trust`
or another integration's name is refused in the module, never sent (`PLAYOUT go pause` would GO a cue called pause); a playlist,
item or cue with a space or a quote mark is quoted; one called `together`, last, is refused (the word asks for a GO together).
Each of these lines, and each *Patterns* action's, is sent as `@c<n> <line>`, and what comes back is one line in the connection's
log and in `$(stagewatch:playout_answer)`: `applied — PlayMap Stage left took …`, `accepted — …` then `final applied — …` (or
`final accepted — unconfirmed …`, Stagewatch's own word when the node gave none), `refused — <why>` — the playout gate's words
from a PC not in May drive playout among them — or, when the link drops before the answer, `no answer — the link to Stagewatch
dropped first, so it may or may not have run`, and after an `accepted`, `accepted; the link to Stagewatch dropped before any final
answer came`. An `accepted` whose words begin `unconfirmed` is settled (Stagewatch sends no follow-up to it), and a line sent more
than 40 s ago is forgotten without a word, since no answer to it can still come. In the variable, an earlier press's late answer
names its line before its words. A press while offline is dropped and says so; nothing is queued. `vt_link` is the frame's
`playout.link` (empty for vMix or none), `vt_source` its `source`; the feedback *Playout: The VT comes from…* lights a key while a
link (any link, or vMix) feeds the VT, optionally only while it runs or in its last N seconds. The *Playout links* preset section
has last 10 s, pause, play and restart with no link named (the only link switched on, else the one feeding the VT) and for each
link — blue while what the key reaches feeds the VT, red in that clip's last 10 s, never for vMix's, grey without the licence —
and a key showing the last answer. [`companion-module/test/playout-lines.tsv`](../companion-module/test/playout-lines.tsv) holds
the module's lines to this grammar from both ends: the module's tests press every action and expect exactly its line, and the
suite reads each line as this server does (tokenized, then the shared grammar) and expects its kind and arguments, and agrees with
the module on every name a link may not take.

Bad input gets `ERROR <reason>` and the connection stays open.

### v1 events (default; used by the PowerPoint add-in)

Without `SUBSCRIBE JSON`, clients receive the original stream:

```
TICK ms=<ms> formatted=<text> state=<state>      ~20 Hz while running
STATE state=<state> / MODE <mode> / MESSAGE "text" urgency=<u> flash=<f>
```
