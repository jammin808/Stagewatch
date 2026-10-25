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
  but its show has stopped updating — treat the last frame as stale). A show app dials it as a node link (below).
- **Commands:** one grammar for everything (HTTP `/api/<verb>/…`, WebSocket `{"cmd":…,"args":[…]}`, OSC, the
  Companion TCP line protocol, triggers, MCP). `GET /api/help` returns it. See REMOTE.md.
- **MCP:** `POST /mcp` (JSON-RPC, PIN). By default an AI agent's `run_command` becomes a proposal the operator
  approves — the human stays in the loop.
- **Screens:** `GET /api/screens` (PIN) lists connected browsers, and sockets that only claim to be a show app
  (`claim`); `screen go|identify|name|save|reload|disconnect` commands move them. A show app's node link is never a
  screen: no screen command reaches it, and naming one is refused.
- **Layouts:** `GET /api/layouts` and `/api/layouts/<name>` (anyone who may view) return designed screens as JSON
  (elements on a 100×100 canvas with `{placeholder}` text); `POST /api/layouts/save|delete|ai` need control.
  A node with its own display can render Stagewatch layouts from the same frames.
- **API keys:** nodes should use a scoped key (`Authorization: Bearer swk_…`) rather than the PIN — read for
  dashboards, control for show control, MCP for agents, **node** for another show app (read, stage messages, its own
  context and proposals). A node key speaks for one app (one node id): give each app, and each install, a key of its
  own. Replies carry `X-RateLimit-Limit/Remaining/Reset`. An unknown or revoked key is refused (401) and never counts
  toward the address lockout.

## Show apps on /ws (2.18)

A show app follows the timer on `/ws` as a **node link**: its own Show Bus id and kind in the address, its key in the
header, never in the URL. It hears what Stagewatch made of it before any frame, and it never hears a screen command.
REMOTE.md's 2.18 section has every message and refusal.

| App | `t` | Its node link |
|---|---|---|
| Patterns | `patterns` | `/ws?node=<its Show Bus id>&t=patterns&cid=<the same id>&page=/patterns` |
| PlayMap | `playmap` | `/ws?node=<its node id>&t=playmap&cid=<its cid today>&page=/playmap&detail=1` |
| AGMessage | `messenger` | `/ws?node=<its node id>&t=messenger&cid=<the same id>&page=%2Fagmessage` |

- Each sends its own **node key** as `Authorization: Bearer swk_…`. A control key, or the PIN when this Stagewatch asks
  for one, also proves it (verified `operator`) while no key speaks for that id. A read key, an MCP key, a PIN on a
  Stagewatch with no PIN set, or nothing makes it only a claim: it stays in Screens, flagged, never reached by "all".
- `cid` and `page` are ignored when `node` is there; a Stagewatch older than 2.18 ignores `node` and lists each install
  under its own `cid`, which it keeps only at 6–32 letters, digits, `-` or `_` (so PlayMap keeps the `cid` it sends
  today, `playmap-<id>` for a shorter id).
- The first message is the verdict: `{"type":"auth","ok":true,"scope":"node","node":{"t":"patterns","id":"9f8e7d6c",
  "verified":"key"}}`. `verified` `none` comes with `words` saying why (a key that speaks for another app, an id that
  speaks with its own key, no key); the frames keep coming, and only the node routes (context, proposals) refuse it. A
  refused key hears `{"type":"auth","ok":false,"error":"unknown or revoked API key","retry":60}` and keeps the public
  frame: wait `retry` seconds before keyed calls, and don't redial in a loop, since no key reason closes the socket.
- A later change (the key revoked, the key moved to another app, the PIN changed) is sent again, latest wins.
- A node key binds to the first id it speaks for while that id is free (on a node link, a context, a proposal or a
  collect); the same key under another id, or another kind, is refused (403 on the HTTP routes; on a node link, a
  verdict of verified `none`) with `this key speaks for <kind> <id> — make a key for <kind> <id>`. The operator moves a
  key with Let it speak for… (Settings → Remote) or Let a key speak for this node… (Producer → Show network).
- Today's dials (`/ws?cid=patterns&page=/patterns`, `/ws?cid=<its cid>&page=/playmap&detail=1`,
  `/ws?cid=agmessage&page=%2Fagmessage`) still work. With a node key in the header such a socket already leaves Screens
  (a show app listed under its key, speaking for no node) and hears no screen command; with a control key, the PIN, a
  read key or no key it is still a browser screen to Stagewatch, so "all" reaches it until the app dials as a node.
- `?pin=` on a node link is refused (400): the key rides only in the header.

## Patterns video (Stagewatch as a Patterns client)

Stagewatch reads the clip on air in Patterns over Patterns' own wire, as a playout source beside vMix:

- **Connect:** TCP `9697` (Settings → Integrations → *Patterns video*; **Find** listens for the Patterns beacon on
  UDP `9700` and takes its `wire` port). Stagewatch sends `HELLO Stagewatch` and a Show Bus 1.1 probe (`@hi PING`),
  and `PING` every 2 s; a link silent for 12 s is dropped and redialled with backoff (250 ms → 5 s).
- **Pairing (2.18): the token never crosses the network.** The pairing token is stored DPAPI-protected. A desk's
  `NONCE` is answered with `AUTH-PROOF` (HMAC-SHA256(token, nonce + "Stagewatch")) only when it is exactly 32
  lowercase hex, once per connection. Plain `AUTH <token>` goes only to a Patterns on this PC (a loopback
  socket) that asks for no proof, never on a connection that heard a NONCE. Nothing the desk sends (STATE, receipts,
  clock samples, context) is taken before the link pairs (`OK paired`) or is open (a desk that asks for no proof: a
  Show Bus 1.1 desk, or one on this PC while this link holds no token), and no command is written to it before then. A
  Patterns older than Show Bus 1.1 on another PC therefore stays amber ("update Patterns") and is not followed.
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
- Reads `ws://<stagewatch>:9924/ws` for the live timer, its key in the `Authorization` header (a key in force also
  opens a viewing code), and sends `POST /api/message/<text>/<urgency>/off/ack` with `Authorization: Bearer swk_…`
  (a **node** key, which may send stage messages, or a control key) when the research team puts an answer on the
  confidence monitor. Its node link, `t=messenger`, is in the table above.

## Conventions for anything joining

- Mutating anything needs the PIN or a control key; reading the public frame doesn't.
- Every message carries a version (`v` / `proto`); unknown versions are ignored, not guessed.
- Fail safe: no fresh state means "stale", never "fine".
- Untrusted input is validated and clamped at the edge (ids, sizes, paths, numbers).
