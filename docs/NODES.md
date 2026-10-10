# Stagewatch on the network: nodes and protocols

Stagewatch and its helpers are small, self-describing network nodes, so other systems (for example Patterns) can
find them, read them and drive them. One executable, run in roles:

| Role | Start it with | What it does |
|---|---|---|
| Stagewatch (main) | `Stagewatch.exe` | The show: the one authority for its room |
| Stagewatch Slides (presenter PC) | `Stagewatch.exe --slides` | Reads this PC's PowerPoint slideshow and sends it to one or more Stagewatch machines |

## Discovery: UDP 9928

Every node broadcasts a hello every 2 s on UDP **9928** and listens for the others. JSON, under 1 KB:

```json
{"t":"main","v":1,"id":"3f9c2a1b7d0e","name":"Hall A","machine":"FOH-PC","web":9924,"presenting":false,
 "caps":["timer","screens","aux","slides-in","copilot","mcp"],"proto":1}
{"t":"agent","v":1,"id":"pc4be19a2c11","name":"Lectern laptop","machine":"LECTERN","web":0,"presenting":true,
 "caps":["slides"],"proto":1}
```

- `t`: `main` (a Stagewatch) or `agent` (a presenter PC). `id`: stable for the node's run (agents keep theirs).
- `caps`: what the node offers. `proto`: the slide-feed protocol version.
- No PINs, no slides, no show data are ever broadcast. Anything listening may ignore unknown fields; unknown `v` is ignored.

(The older multi-room beacon on UDP 9927 is separate and carries each room's timer summary — see FEATURES.md.)

## Slide feed: presenter PC → Stagewatch

`POST http://<stagewatch>:9924/api/slides/feed` with `X-Stagewatch-Pin: <pin>`, body JSON (≤ 64 KB):

```json
{"v":1,"agent":"pc4be19a2c11","name":"Lectern laptop","machine":"LECTERN","presenting":true,
 "presentation":"Keynote.pptx","slide":4,"count":22,"next":"Results","notes":"…current slide's notes…",
 "budgets":[30,45,60],"slideMs":12250,"showMs":301000,"script":"9a1f…(16 hex)","seq":1042}
```

Rules (game netcode, on purpose):

- **Whole state every time**, on every change and at least once a second. A lost packet doesn't matter.
- **Durations, not clock times** (`slideMs`, `showMs`), so the two PCs' clocks never need to agree.
- **`seq` grows**; Stagewatch ignores a late, older packet.
- **Latest wins per target**: a sender never queues behind a slow Stagewatch; it skips to the newest state.
- The **script** (all speaker notes, for speech pace) is identified by `script` (SHA-256, 16 hex chars) and sent
  once, only when asked: the reply is `{"ok":true,"needScript":true,"proto":1}`; then
  `POST /api/slides/script?hash=<hash>` with the UTF-8 text (≤ 2 MB). Stagewatch keeps it only if the hash matches.
- Replies: `200 {"ok":true,"proto":1}` · `401` wrong or missing PIN (five wrong PINs lock the address out for a
  minute) · `403` not licensed (presenter PCs need Pro on the Stagewatch PC) · `400` not a feed.
- A presenter PC that stays silent for 4 s counts as gone; *Automatic* then falls back to the local PowerPoint.

## Reading and driving Stagewatch

- **State:** `GET /api/state` (public frame) or `/api/state?detail=1` with the PIN (operator frame);
  `ws://<pc>:9924/ws` pushes every frame plus a heartbeat (`{"type":"beat"}`; `"live":false` = Stagewatch is up
  but its show has stopped updating — treat the last frame as stale).
- **Commands:** one grammar for everything (HTTP `/api/<verb>/…`, WebSocket `{"cmd":…,"args":[…]}`, OSC, the
  Companion TCP line protocol, triggers, MCP). `GET /api/help` returns it. See REMOTE.md.
- **MCP:** `POST /mcp` (JSON-RPC, PIN). By default an AI agent's `run_command` becomes a proposal the operator
  approves — the human stays in the loop.
- **Screens:** `GET /api/screens` (PIN) lists connected browsers; `screen go|identify|name|save|reload|disconnect`
  commands move them.
- **Layouts:** `GET /api/layouts` and `/api/layouts/<name>` (anyone who may view) return designed screens as JSON
  (elements on a 100×100 canvas with `{placeholder}` text); `POST /api/layouts/save|delete|ai` need control.
  A node with its own display can render Stagewatch layouts from the same frames.
- **API keys:** nodes should use a scoped key (`Authorization: Bearer swk_…`) rather than the PIN — read for
  dashboards, control for show control, MCP for agents. Replies carry `X-RateLimit-Limit/Remaining/Reset`.

## Patterns video (Stagewatch as a Patterns client)

Stagewatch reads the clip on air in Patterns over Patterns' own wire, as a playout source beside vMix:

- **Connect:** TCP `9697` (Settings → Integrations → *Patterns video*; **Find** listens for the Patterns beacon on
  UDP `9700` and takes its `wire` port). Stagewatch sends `HELLO Stagewatch`, then `AUTH <token>` when a pairing
  token is set (stored DPAPI-protected), and `PING` every 2 s; a link silent for 7 s is dropped and redialled with
  backoff (250 ms → 5 s).
- **State:** Patterns pushes `STATE <json>`, latest-wins. Stagewatch reads `video{file,tag,position,length,playing,
  ended,loops}`, and from Patterns Round 86 `positionMs`, `lengthMs` (null = not known yet), `paused`, `rate` and
  `seq`. An unknown length or `loops` means no countdown; at a rate other than 1 the countdown is real time. Position counts on locally from a
  monotonic clock between frames; a playing clip with no frame for 3 s is treated as stale and the VT countdown
  disappears rather than lie. Lines over 256 KB and unreadable frames are dropped and counted.
- **Commands:** a fixed set, nothing else:
  - `VIDEO END <n>` — Stagewatch `patterns end [seconds]` (default 10) and the *Last 10 s* button.
  - `VIDEO RESTART` — `patterns restart`.
  - Round 86 desks only (detected from the precise fields): `VIDEO PLAY`, `VIDEO PAUSE`, `VIDEO STOP` (the clip back
    to its top, held) and `VIDEO SEEK <s>` — `patterns play | pause | stop | seek <m:ss>`. An older desk is told
    nothing it doesn't know.

  No raw lines, and never a bare `STOP`, which on Patterns turns every output off. Nothing is queued while
  disconnected.
- **Discovery:** *Find* listens on Patterns' beacon (UDP 9700) and the show network's hello (UDP 9928,
  `{"t":"patterns","v":1,…,"wire":9697,"caps":[…]}`).
- **Both sources on:** the VT countdown follows whichever is playing (Patterns first if both).

## AGMessage (board ↔ research messaging)

- Announces itself on UDP 9928 as `{"t":"messenger","v":1,"id":…,"name":"AGMessage","web":3023,"caps":[…]}` —
  Stagewatch ignores kinds it doesn't know. It **listens** on Patterns' 9700 but never sends there (a standby twin
  must never mistake it for a main machine).
- Reads `ws://<stagewatch>:9924/ws` for the live timer (public frame; the key is passed as `pin=` only to get past a
  viewing code) and sends `POST /api/message/<text>/<urgency>/off/ack` with `Authorization: Bearer swk_…` (a
  **control** key) when the research team puts an answer on the confidence monitor.

## Conventions for anything joining

- Mutating anything needs the PIN or a control key; reading the public frame doesn't.
- Every message carries a version (`v` / `proto`); unknown versions are ignored, not guessed.
- Fail safe: no fresh state means "stale", never "fine".
- Untrusted input is validated and clamped at the edge (ids, sizes, paths, numbers).
