# Remote control — browser, HTTP, WebSocket, OSC

Stagewatch can be watched and driven from other devices and show-control tools. Configure everything in
**Settings → Remote**. All of it is a Pro feature (also available during the trial); the MCP endpoint, the
multi-room beacon behind `/rooms` and the acknowledged-cue loop on the role pages need Production.

| Transport | Default | Auth | Use it for |
|---|---|---|---|
| Browser viewer | `http://<pc>:9924/viewer` (also `/speaker`) | none (read-only; may acknowledge cues) | Confidence monitor on any phone, tablet or laptop |
| Showcaller / Producer | `http://<pc>:9924/producer` | PIN | The whole show on one screen: running order with planned / expected / actual, curfews, assistant, cues, Q&A |
| Role pages | `/moderator`, `/backstage`, `/dept?name=Sound`, `/interpreter`, `/haptic?for=speaker` | none to view; a **crew link key** (`&key=…`) to confirm cues | Moderator clock + Q&A, green room calls, department STANDBY/GO, interpreter booth, pocket buzz on a phone |
| Audience | `/audience` (agenda, break countdown, QR), `/ask` (question form) | none | Agenda screens and audience questions |
| All rooms | `/rooms` | none | Every Stagewatch on the network: time, lateness, curfew risk |
| Holographic screen | `/holo` (or `/viewer?style=holo`) | none | The timer as a three.js hologram above a projector. `?floor=0` hides the base; `/viewer` switches by itself when the style is on (`?style=flat` opts out) |
| Holding screen | `/holding` | none | Shows only holding images, black otherwise (`?idle=blank` hides the faint name). `/viewer` and `/audience` show them too unless opened with `?holding=0` |
| Holding images | `GET /api/holding/<id>` · `POST /api/holding/upload?name=…` | none · PIN | Fetch an image (cached for ever: ids never change) · upload one (body = the file, PNG/JPG/GIF/BMP/TIFF/WebP, max 25 MB) |
| Browser remote | `http://<pc>:9924/control` | PIN | Operating the show from a phone |
| Studio clock | `/clock` | none | Time of day in the show's time zone with a ring of seconds, plus the show timer (`?h=12`, `?timer=0`, `?date=0`) |
| Countdown | `/countdown?to=next` | none | Time until the next item, item n (`?to=3`), a group's end (`?to=group:Morning`, `&at=start` for its start) or the show's end (`?to=end`); `&label=…` |
| Timeline | `/timeline` | none | The running order on a time axis, groups above, live now-line (`?span=3h` for a window around now) |
| Department notes | `GET /api/deptnotes?dept=Lighting&key=…` | crew link key or PIN | One department's notes on every item (only that department's crew link can read them) |
| Custom CSS | `/custom.css` | none | The operator's styling (Settings → Remote), loaded by every page; each page's `<html>` has class `sw-page-<name>` |
| Saved screen setups | `http://<pc>:9924/p/<name>` | none | Opens a page and its options saved in **Screens** (e.g. `/p/lobby` → `/audience?holding=0`) |
| Screens list | `GET /api/screens` | PIN | Every connected browser: id, name, page, device, address; and the saved setups |
| Presenter PCs | `POST /api/slides/feed` · `POST /api/slides/script?hash=…` | PIN | Stagewatch Slides sends its slideshow (see NODES.md) |
| MCP | `POST http://<pc>:9924/mcp` | PIN (`Authorization: Bearer <pin>` or `X-Stagewatch-Pin`) | AI assistants and Patterns tools reading and driving the show |
| HTTP API | `http://<pc>:9924/api/...` | PIN | vMix, Companion "generic HTTP", Stream Deck, scripts |
| WebSocket | `ws://<pc>:9924/ws` | PIN for control | Custom dashboards that want live state |
| OSC (UDP) | port 9925, **off by default** | none | QLab, lighting desks, TouchOSC |
| Companion TCP | port 9923 | none | Bitfocus Companion module (see COMPANION.md) |

The PIN is generated on first run and shown in Settings → Remote. Five wrong PINs from one address lock that
address out for a minute. A link with `?pin=1234` (e.g. `http://<pc>:9924/control?pin=1234`) logs in directly.

Windows asks once whether to allow Stagewatch through the firewall. Allow it on **private** networks to let
other devices connect. Untick "Allow other devices on the network" to keep everything on this PC.

## What's public and what isn't (2.5)

| Who | Gets |
|---|---|
| Anyone on the network | The display frame (no speaker notes); with `detail=1`, the agenda timings, room status and *moderated* questions — never assistant advice or raw audience submissions |
| A crew page opened with its crew link | The same, plus confirming ("Got it") the cues sent to **its** target. Links: Showcaller page → Crew links, or `GET /api/crew-links` (PIN). Settings → Remote → *New crew links* revokes them all. The show log records the confirming device's address |
| PIN | Control, `/producer`, `/report`, `/api/report.csv`, `/api/state?detail=1&pin=…` (full operator frame), `/mcp` |

Every WebSocket gets a `{"type":"beat"}` at least every 2 s. The pages treat 6 s of silence as a dead link: the
screen dims under a **NOT CONNECTED** banner (never a confident stale time) and reconnects.

A live link isn't proof the show is live: if Stagewatch's show thread stops turning over for 3 s, the server sends
`{"type":"beat","live":false}` instead, and no frames (not even on reconnect) until it recovers. Pages don't count
those beats, so the screen goes stale the same way. A paused or idle show is fine — it keeps reporting.

## Commands

One grammar is shared by every transport — HTTP, WebSocket, OSC and the Companion TCP protocol.

| Command | Arguments | Effect |
|---|---|---|
| `start` | | Start / resume |
| `pause` (alias `stop`) | | Pause (also pauses overtime) |
| `toggle` | | Start if stopped, pause if running |
| `reset` | | Back to the loaded duration |
| `next` | | Next running-order item (starts it if the show is live) |
| `previous` (alias `prev`) | | Previous item (loaded, not started) |
| `goto` | `<n> [start]` | Load item *n* (1-based) |
| `preset` | `<slot> [start]` | Load preset 1–12 |
| `nudge` | `<±seconds>` | Add or remove time live |
| `duration` | `<secs \| mm:ss \| hh:mm:ss>` | Load an ad-hoc duration |
| `endat` | `<hh:mm \| clear> [elastic\|realtime]` | Finish at a wall-clock time. Elastic stretches the loaded duration; realtime counts the actual time left at normal speed. Omit to use the operator's setting |
| `message` | `<text> [info\|warn\|crit] [off\|slow\|fast]` | Show a message to the presenter |
| `clearmessage` | | Clear the message |
| `blackout` | `[on \| off \| toggle]` | Hide everything on the outputs |
| `mode` | `<countdown \| countup \| tod>` | Switch clock mode |
| `output` | `<full \| keyed \| mini \| closed \| toggle>` | Open the speaker output full screen, keyed for a vision mixer, as a floating mini timer, or close it (toggle = full screen / closed) |
| `ndi` | `[on \| off \| toggle]` | Send the output over NDI (the output must be open) |
| `style` | `<holo \| egg \| standard \| toggle>` | Holographic 3D display or 3D egg timer on the output and browser screens (Pro) |
| `ltc` | `chase [on \| off \| toggle]` | The timer follows incoming LTC: starts when it rolls, pauses when it stops (Production) |
| `ltc` | `end <HH:MM:SS:FF \| clear>` | While chasing, count down exactly to this timecode, e.g. a video's last frame |
| `aux` | `<1-3> start \| pause \| toggle \| reset \| stage \| unstage \| up \| down` | Extra timers beside the show; `stage` puts one as a second line on the output and speaker screens |
| `aux` | `<1-3> set <time>` · `<1-3> nudge <±secs>` · `<1-3> name <text>` | Load, adjust or name an extra timer |
| `deptnote` | `<item> <department> [text]` | A department's note on an item (empty clears). Crew pages may send it for their own department with their crew link |
| `roll` | `[on \| off \| toggle]` | Roll: the running order follows its planned times until the operator takes over |
| `rundown` | `<name>` | Make another of the show's running orders live |
| `screen` | `identify [screen]` | Flash each screen's name on it for 6 s (no screen = all) |
| `screen` | `go <setup \| /page> [screen]` | Send screens to a saved setup or a page on this Stagewatch (a screen is its name or id; none = all) |
| `screen` | `name <id> <name>` · `save <setup> <screen>` | Name a screen; save a screen's page and options as a setup |
| `holding` | `<number \| name \| id \| off \| toggle>` | Put a holding image full screen on every output, NDI and browser screen (`holding 2`, `holding Sponsors`, `holding off`) |
| `message … ack` | `<text> [urgency] [flash] ack` | As `message`, and ask the speaker to acknowledge ("Got it") |
| `signal` | `<message\|standby\|go\|call\|nudge> <target> [text] [noack]` | Send a cue the target confirms. Targets: `speaker`, `moderator`, `backstage`, `interpreter` or a department (`Lighting`) |
| `signal go all` | | ALL GO: a GO to every department standing by, at once (each with the cue its standby named) |
| `signal clear` / `signal resend` | `<target\|id>` / `<id>` | Cancel cues / send one again |
| `ack` | `<id> [seen\|done] [name]` | Acknowledge a cue (**no PIN needed**: speaker tablets and phones) |
| `trim` | `<item> <±seconds>` | Cut or extend a running-order item (kept apart from the plan for the report) |
| `delay` | `<seconds \| mm:ss \| clear>` | Announce a delay before the next item; it ripples on until a gap, buffer or fixed start absorbs it |
| `catchup` | | Re-time the live item (elastic) so it lands on its planned end |
| `preview` | `[on\|off\|toggle]` | Briefing preview: the cue ladder at 10× on every output |
| `ladder` | `<id \| default>` | Choose the cue ladder (keynote, panel, toastmasters, awards, spoken, or your own) |
| `format` | `<standard\|calm\|progress>` | Speaker clock: MM:SS, "12 MIN", or progress bar only |
| `question` | `<text> [name]` | Audience question (**no PIN**; rate-limited per device) |
| `q` | `<id> approve\|star\|live\|answered\|dismiss`, `q next`, `q clear` | Moderate Q&A |
| `rehearse` / `listen` | `[on\|off]` | Record slide timings from PowerPoint / follow the speaker through their script |
| `report` | `[html\|csv]` | Save the planned-vs-actual report |
| `note` | `<text>` | Log a note in the show report |

### HTTP

`GET` or `POST` `/api/<command>[/<arg>/<arg>…]`, with the PIN as `?pin=` or an `X-Stagewatch-Pin` header.
Arguments can also go in the query string: `value` (or `text`, `slot`, `seconds`), then `urgency` / `start=1`, then `flash`.

```
/api/start?pin=1234
/api/nudge/-60?pin=1234
/api/preset/3/start?pin=1234
/api/message?pin=1234&text=Please%20wrap%20up&urgency=warn&flash=slow
/api/blackout/toggle?pin=1234
```

Read-only, no PIN: `/api/state` (current frame, JSON; add `?detail=1` for the running-order projection, advice,
Q&A and rooms), `/api/config` (presets, quick messages, running order, departments, cue ladders), `/api/help`
(command list), `/report` and `/api/report.csv` (planned vs actual), `/api/qr?text=…` (QR code, SVG).

`ack` and `question` need no PIN — speaker tablets and audience phones send them. Questions are rate-limited
(one every 10 s, 20 an hour per device).

### WebSocket

Connect to `/ws` (viewer) or `/ws?role=control&pin=1234`; add `&detail=1` (or `?detail=1`) for detail frames.
Read-only connections may still send `ack` and `question`. The server sends a `{"type":"state",…}` frame —
the same JSON as `/api/state` — on connect and whenever anything visible changes (about once a second while
running). Control clients send `{"cmd":"nudge","args":["60"]}`. Frames are latest-wins: a slow client skips
frames rather than falling behind.

### OSC

Address `/stagewatch/<command>` (the `/stagewatch` prefix is optional). Arguments can be OSC arguments, extra
address segments, or both:

```
/stagewatch/start
/stagewatch/next 1            ← a button press (1) fires; the release (0) is ignored
/stagewatch/preset/3 "start"
/stagewatch/nudge 60
/stagewatch/message "Wrap up" "warn" "slow"
/stagewatch/blackout/on
```

**Feedback.** Set "Send feedback to" (host + port) and Stagewatch sends a bundle whenever something changes,
containing only the fields that changed: `/stagewatch/time` (s), `/seconds` (i), `/state` (s), `/mode` (s),
`/phase` (s: normal|warning|wrapup|overtime), `/title` (s), `/next` (s), `/item` (i, 1-based), `/offset` (i, seconds
behind schedule; negative = ahead), `/message` (s), `/blackout` (i), `/progress` (f, 0–1).

## Running order & schedule

The running order (right panel → Running order → Edit…) is an agenda you step through with **Next** (button,
`N` key, or the `next` command). Paste rows straight from Excel / Google Sheets or import a CSV:

```
Title,Duration,Speaker,Message,Auto
Welcome,5,Ana Ruiz,,yes
Keynote,45:00,Sam Patel,Keynote — 45 min,
Panel,1h,,,
```

Durations accept `45:00`, `1:30:00`, `15` (minutes), `20m`, `1h30m`, `90s`. Columns are matched by header name
(Title/Segment/Session, Duration/Length, Speaker/Presenter, Message/Notes, Auto/Link) or read in that order.
Rows marked **Auto** roll into the next item by themselves at zero.

Set **Starts** (HH:MM) to the planned start of item 1 and Stagewatch shows how far ahead or behind the show is,
and when it will now finish. Leave it blank and the plan starts when the first item runs.

### MCP (Model Context Protocol)

`POST /mcp` speaks JSON-RPC 2.0 (MCP protocol 2025-06-18, stateless HTTP). Tools:

| Tool | Does |
|---|---|
| `get_state` | The live detail frame (JSON) |
| `get_running_order` | Planned / expected / actual for every item |
| `run_command` | One command line from the grammar above, e.g. `trim 7 -180` |
| `get_report` | The planned-vs-actual report as CSV |

Use the Remote PIN as `Authorization: Bearer <pin>`. Commands run through the same single input path as a
button press, so an AI tool can never do anything an operator can't.

**Human in the loop (2.8):** by default `run_command` doesn't run the command — it becomes a proposal in the
operator's **Copilot** window with the agent's `reason`, and the tool result says so ("Queued for the operator's
approval (proposal #12)"). The operator ticks and runs it, or discards it; both go in the show journal and the
report's *AI suggestions and decisions*. Settings → Integrations can let agents act directly instead.

## Screens: what browsers are told (2.8)

Every page connects with `ws://<pc>:9924/ws?cid=<id>&page=<path>`; the id lives in that browser's storage, so a
name given to a screen sticks. The page never sends its PIN in `page`. Besides `state` frames and `beat`s, a page
may receive:

| Message | Effect |
|---|---|
| `{"type":"redirect","to":"/audience?holding=0"}` | Open that page (only ever a path on this Stagewatch) |
| `{"type":"identify","name":"Lobby TV","id":"…"}` | Show the screen's name full screen for 6 s |
| `{"type":"beat","live":false}` | Stagewatch is answering but its show has stopped updating — the page goes stale |

## Design notes

The app is the single authority. Every input — buttons, keys, Companion, HTTP, WebSocket, OSC — becomes a
command executed on the UI thread, and every output (operator UI, output window, NDI, browsers, OSC feedback)
renders the same immutable snapshot. A snapshot is published only when something visible changes and is
serialized once, however many clients are connected. Network failures are contained per connection: a stalled,
slow or malicious client is timed out or dropped without touching the timer.

## 2.10: keys, layouts, chimes and screen options

- **API keys** (Settings → Remote → API keys): `Authorization: Bearer swk_…`, `X-Stagewatch-Pin: swk_…` or
  `?pin=swk_…`. Scopes: **read** (`/api/state?detail=1`, reports, `/api/screens`), **control** (+ `/api/<verb>`,
  layouts, crew links), **mcp** (+ `/mcp`). Unknown or revoked keys count toward the 5-a-minute lockout like wrong
  PINs. Each key: 600 requests a minute; replies carry `X-RateLimit-Limit`, `X-RateLimit-Remaining`,
  `X-RateLimit-Reset` (seconds); over the limit → `429` with `Retry-After`.
- **Layouts:** `GET /api/layouts` → `{"layouts":[names]}`; `GET /api/layouts/<name>` → the layout JSON;
  `POST /api/layouts/save` (body: layout JSON), `POST /api/layouts/delete?name=`, `POST /api/layouts/ai`
  (body `{"request":"…","current":{layout}|null}` → `{"ok","layout","notes"}`, a draft that is **not** saved).
  Screens: `/layout?name=<name>`; designer: `/designer`.
- **Chimes:** frames carry `chimeSeq` and `chime` (`builtin:warning|wrapup|zero|start` or `sound:<id>`);
  `GET /api/sounds/<id>.wav` serves library sounds. Pages opened with `?sound=1` play them.
- **Screen options** (any page): `?mirror=h|v|both`, `?delay=0-60` (seconds), `?view=<code>` (viewing code),
  `/clock?tz=<IANA zone>`.
- **Commands:** `undo`, `redo`, `endofshow <HH:MM|clear>`, `loop <off|n|forever>`, `screen reload [screen]`,
  `screen disconnect [screen]`, `patterns end [seconds]` (the clip on air in Patterns jumps to its last seconds,
  default 10), `patterns restart`, and with Patterns Round 86 `patterns play`, `patterns pause`, `patterns stop` (the
  clip back to its top — never outputs off) and `patterns seek <m:ss>`.

## 2.15: Show Bus 1.1 — receipts, context, node keys

The contract between Stagewatch, Patterns, AGMessage and the micro-apps is `docs/SHOWBUS.md`. What Stagewatch serves:

| | |
|---|---|
| **Receipts** | HTTP: send `X-Request-Id: a1` with any `/api/...` command → `{"type":"receipt","id":"a1","ok":true,"stage":"applied","effect":"…","refs":{"messageId":12}}` (409 with `error` when refused; `stage:"accepted"` if the show thread didn't confirm within 3 s). WebSocket: `{"cmd":"next","args":[],"id":"a1"}` → the same receipt message. Companion TCP: `@a1 NEXT` → `OK @a1 applied <effect>` / `ERROR @a1 <why>`. MCP `run_command` (acting directly) reports what happened; `get_proposal {id}` says what became of a queued proposal. |
| **Frames** | Every state frame carries `"v":1` and `"seq"` (rises with each published change; smaller after a reconnect = Stagewatch restarted). |
| **Clock** | WebSocket `{"cmd":"ping","t":<unix ms>}` → `{"type":"pong","t":…,"t2":…,"t3":…}`; Companion `PING <unix ms>` → `PONG t1 t2 t3`. Offset = ((t2−t1)+(t3−t4))/2. |
| **Context** | `GET /api/context` (PIN or any key) — the room in one JSON: light, headline, parts, recent events, verbs with risk, the peers it hears. MCP `get_context` returns the same. `POST /api/peers/context` (a `node` or control key, or the PIN) — another app's context, kept 30 s. |
| **Keys** | New scope **node**: read + stage messages + post its context (for AGMessage and similar). Keys now work everywhere the PIN does (slides script, holding upload, WebSocket via `Authorization: Bearer` — never in a URL); a key is never counted as a wrong PIN. |
| **Discovery** | The UDP 9928 hello now carries a stable `id`, `ports` (`http`, `ws`, `companion`, `osc`), `clock` and `host`; Stagewatch hears every kind of node (patterns, messenger, …) and shows them in Producer → Show network. |
| **Patterns** | The link pairs by proof (`NONCE` → `AUTH-PROOF` = HMAC-SHA256(token, nonce + "Stagewatch")) with 1.1 desks and sends ids, timed pings and `CONTEXT`; older desks get the plain lines they know. A clip ending fires the **Clip ends** trigger and, if set, moves a video item on. |

## 2.16: Show Bus 1.2 — HTTPS and the shared approval queue

| | |
|---|---|
| **HTTPS** | The web port (9924) also speaks TLS with this install's own certificate (self-signed ECDSA P-256, 825 days, renewed automatically; key protected for this Windows user). A connection that starts with a TLS handshake is HTTPS/WSS, anything else plain HTTP — existing screens keep working. `GET /tls` shows the fingerprint and how to trust the certificate on Windows, macOS, iPhone/iPad and Android; `GET /tls.cer` downloads it. The hello carries `"tls":"sha256:…"` and `ports.https`. **Require HTTPS** (Settings → Remote) sends plain HTTP from other devices to `https://` and refuses plain WebSockets; this PC and the certificate pages stay reachable. |
| **Pinning** | Apps connecting to each other trust a certificate by its fingerprint, pinned on the first authenticated success; a different one later is refused ("CERTIFICATE CHANGED") until the operator accepts it. Stagewatch Slides on presenter PCs does this (pins in `%APPDATA%\Stagewatch\slides-pins.json`; "Trust new certificate" after a reinstall). |
| **Approval queue** | Stagewatch is the show's approval desk (hello cap `approvals`). `POST /api/proposals` (node, MCP or control key, or the PIN) `{"from":{t,id,name},"target":{t,id},"line","reason"}` → `{"id":"p1","state":"pending"}`. The proposal appears in Copilot beside Stagewatch's own, with the target's own risk for that verb (unknown = high). Approve or discard once. Aimed at Stagewatch, it runs here with a receipt; aimed at another app, that app collects it — `GET /api/proposals?target=<its id>&state=approved` with its node key — runs it through its own checks and reports `POST /api/proposals/<id>/result {"ok","words"}`. `GET /api/proposals/<id>` (read) → state (`pending`, `approved`, `rejected`, `done`, `failed`, `undelivered` after 60 s uncollected), `ok`, `words`, who decided. A node key is bound to the node it first spoke for; one app's key can't collect another's. Everything is in the show log. |
