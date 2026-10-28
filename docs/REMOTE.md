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
| Screens list | `GET /api/screens` | PIN | Every connected browser: id, name, page, device, address, and `claim` for a socket that only claims to be a show app; and the saved setups. Show apps on a node key or a node link aren't screens (2.18) |
| Presenter PCs | `POST /api/slides/feed` · `POST /api/slides/script?hash=…` | PIN | Stagewatch Slides sends its slideshow (see NODES.md) |
| MCP | `POST http://<pc>:9924/mcp` | PIN (`Authorization: Bearer <pin>` or `X-Stagewatch-Pin`) | AI assistants and Patterns tools reading and driving the show |
| HTTP API | `http://<pc>:9924/api/...` | PIN | vMix, Companion "generic HTTP", Stream Deck, scripts |
| WebSocket | `ws://<pc>:9924/ws` | PIN for control; a node key for a show app | Custom dashboards that want live state; show apps following the timer as node links (2.18) |
| OSC (UDP) | port 9925, **off by default** | none; `playout` and `patterns` only from this PC or a listed address (2.20) | QLab, lighting desks, TouchOSC |
| Companion TCP | port 9923 | none; `playout` and `patterns` only from this PC or a listed address (2.20) | Bitfocus Companion module (see COMPANION.md) |

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
| `screen` | `identify [screen]` | Flash each screen's name on it for 6 s (no screen = every browser screen; never a show app, 2.18) |
| `screen` | `go <setup \| /page> [screen]` | Send screens to a saved setup or a page on this Stagewatch (a screen is its name or id; none = every browser screen) |
| `screen` | `name <id> <name>` · `save <setup> <screen>` | Name a screen; save a screen's page and options as a setup |
| `holding` | `<number \| name \| id \| off \| toggle>` | Put a holding image full screen on every output, NDI and browser screen (`holding 2`, `holding Sponsors`, `holding off`) |
| `message … ack` | `<text> [urgency] [flash] ack` | As `message`, and ask the speaker to acknowledge ("Got it") |
| `signal` | `<message\|standby\|go\|call\|nudge> <target> [text] [noack]` | Send a cue the target confirms. Targets: `speaker`, `moderator`, `backstage`, `interpreter` or a department (`Lighting`) |
| `signal go all` | | ALL GO: a GO to every department standing by, at once (each with the cue its standby named) |
| `signal clear` / `signal resend` | `<target\|id>` / `<id>` | Cancel cues / send one again |
| `ack` | `<id> [seen\|done] [name]` | Acknowledge a cue (**no PIN needed**: speaker tablets and phones; without the PIN the device's address is added to the name, and the name, never the address, is cut to fit 40 characters). A message's cue completes on its first answer (`done`, never `seen`). One that would change nothing is refused (2.20): 403 at the door once the cue has left the frame's `signals` (cancelled, or 30 s after its answer; or, with crew links required, from anything but its crew link or the operator), and a 409 receipt or Companion `ERROR @id` while it is listed but already answered |
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
| `playout` | `[<link>] end [seconds]` · `[<link>] restart\|play\|pause\|stop` · `[<link>] seek <m:ss>` | The clip on air on a playout link's peer, a Patterns desk or a PlayMap node (Settings → Integrations → Playout links; 2.20): its last seconds (10 unless said), back to its top, play, pause, stop (back to its top, held) or a point. `<link>` is the link's name; with none, the only link switched on, else the link feeding the VT countdown |
| `playout` | `<link> go <playlist> [item] [together]` · `<link> cue <playlist> <item>` (a PlayMap node) · `<link> go <cue>` · `<link> cue <cue>` (a Patterns desk) | Start a playlist's item (its next one when none is named) or load one, its first frame on air at once; GO a cue (the desk's own standby decides) or stand one by. Never guessed between two links (2.20). `together`: at an instant on the node's own clock (`AT`) when that clock is fit for it, else at once — the receipt says which (see *A GO at one instant* below) |
| `patterns` | `end [seconds]` · `restart\|play\|pause\|stop` · `seek <m:ss>` | As `playout`, on link `patterns`, else the only link |
| `playout` | `<link> trust <8+ hex of its certificate>` | Refused on every transport, whoever sends it (2.20): trusting a playout link's certificate is a person's choice on the Stagewatch desk, beside its fingerprint (right-click its node on the Show network, or the status line). In the grammar only so that it is refused in words, never as unknown |

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

**A request's body is at most 64 KiB** (65,536 bytes as sent; 2.20). A longer one is answered
`413 {"ok":false,"error":"a request body is at most 65536 bytes; this one declares 70000"}`, with the length it declared,
before any route reads the request: no route runs, no PIN or key is checked, a wrong PIN on it is no guess toward a lockout,
Require HTTPS redirects nothing and `/ws` upgrades nothing. Stagewatch first reads and drops a body of up to 1 MiB, for at most
10 s, so its sender reads the answer instead of a reset; when the 10 s are up, or for one declared longer, it answers at once
and closes, and a sender still sending may see the reset instead. Nothing of the body is kept or logged. A length past what a
64-bit number holds is answered the same, its words saying `at least 9223372036854775807`. A `Content-Length` that is not one
decimal number of bytes (letters, a sign, a list, or two of them) is answered
`400 {"ok":false,"error":"a Content-Length this server cannot read: a request's length is one decimal number of bytes"}` the
same way, unrouted and undrained, never taken as a request with no body. Uploads (`/api/holding/upload`, 25 MB) and a deck's
script (`/api/slides/script`, 2 MB) keep their own caps and refusals. Before 2.20 a longer body closed the connection with no
answer, and an unreadable length was taken as no body.

### WebSocket

Connect to `/ws` (viewer) or `/ws?role=control&pin=1234`; add `&detail=1` (or `?detail=1`) for detail frames.
Read-only connections may still send `ack` and `question`. The server sends a `{"type":"state",…}` frame —
the same JSON as `/api/state` — on connect and whenever anything visible changes (about once a second while
running). Control clients send `{"cmd":"nudge","args":["60"]}`. Frames are latest-wins: a slow client skips
frames rather than falling behind. A show app (Patterns, PlayMap, AGMessage) connects as a node link instead, and
hears first what Stagewatch made of it (2.18, below).

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

### Who may drive another app's air (2.20)

A `playout` or `patterns` line goes on through Stagewatch's own paired link to a Patterns desk's or a PlayMap node's air,
so Stagewatch never relays one for a sender that proved nothing. It runs when it comes:

- from this PC: a loopback address, and over HTTP, a WebSocket or MCP also a `Host` that names this PC there (none,
  `localhost` or a loopback address, at Stagewatch's port);
- over HTTP, a WebSocket or MCP, with the PIN Stagewatch asks for or a key (a control key; MCP an MCP key);
- or from an address listed in **Settings → Remote → May drive playout** (one a line in its box; `remote.playoutFrom` in the
  settings file: at most 16, each one IPv4 or IPv6 address written out; names, ranges, ports and loopback addresses are left
  out at load, and said; the box refuses to save one it can't keep, and says which).

A listed address is trusted by its source address alone. Over OSC, which is UDP, any machine on that network can send
from an address it does not own, so list addresses only on a show network you trust; over HTTP, a WebSocket or MCP a key
proves more than an address.

A web page of another site proves nothing, whichever machine its browser runs on: a request whose browser says so
(`Sec-Fetch-Site: cross-site` or `same-site`, or an `Origin` that is not the request's own `Host`) is refused even from this
PC or a listed address, unless it carries the PIN or a key. So is a request over loopback whose `Host` is some other name
(a name rebound to 127.0.0.1) or port (a forwarder carrying another machine in): it is judged as another machine's.

Anything else is refused before the show thread hears of it, so nothing reaches a peer, and the words name the door:

```
playout from another machine over OSC needs its address in Settings → Remote → May drive playout (OSC carries no PIN)
ERROR @c1 patterns from another machine over Companion TCP needs its address in Settings → Remote → May drive playout (Companion TCP carries no PIN)
403 {"ok":false,"error":"playout from another machine over HTTP needs a control key while no PIN is set, or its address in Settings → Remote → May drive playout"}
403 {"ok":false,"error":"patterns from another site's web page over HTTP needs a control key while no PIN is set"}
```

`playout <link> trust <hex>` is refused at every door, from this PC, a listed address or with the PIN or a key alike — a
certificate is trusted by a person on the desk, beside its fingerprint, never by a line — and MCP never makes it a proposal:

```
trusting a playout link's certificate is a person's choice on the Stagewatch desk, beside its fingerprint (right-click its node on the Show network, or the status line) — refused from MCP, whoever sends it
```

With a request id the refusal is a refused receipt under that id (HTTP 403, a WebSocket `receipt`, Companion `ERROR @id`).
Every refusal goes on the status line under its door's name (`OSC:`, `Companion:`, `Remote:` for HTTP and a WebSocket,
`MCP:`, whose agent also reads it as an error in the tool's result), the newest one when several come at once; Diagnostics
counts Companion's refused lines beside OSC's. Every other verb keeps the rules above.

**On upgrade**, a setup on another machine that drives Patterns stops until it proves itself: an OSC or Companion TCP one
until its address is listed, and, while no PIN is set, an HTTP or WebSocket one (a vMix, Stream Deck or Companion "generic
HTTP" button calling `/api/patterns/…`) or an MCP agent there until it sends a key or its address is listed. A web page of
another site that called those routes from a browser on this PC stops too, unless it sends a key.

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
report's *AI suggestions and decisions*. Settings → Integrations can let agents act directly instead, except for
`playout` and `patterns`: a line that drives another app's air is always a proposal (2.20), and with no PIN set it comes
only from this PC, with an MCP key, or from an address in May drive playout (above). Where nobody approves (the headless
rig), such a line is refused, and the tool result is an error, never read as queued.

## Screens: what browsers are told (2.8)

Every page connects with `ws://<pc>:9924/ws?cid=<id>&page=<path>`; the id lives in that browser's storage, so a
name given to a screen sticks. The page never sends its PIN in `page`. Besides `state` frames and `beat`s, a page
may receive:

| Message | Effect |
|---|---|
| `{"type":"redirect","to":"/audience?holding=0"}` | Open that page (only ever a path on this Stagewatch) |
| `{"type":"identify","name":"Lobby TV","id":"…"}` | Show the screen's name full screen for 6 s |
| `{"type":"beat","live":false}` | Stagewatch is answering but its show has stopped updating — the page goes stale |

From 2.18 a show app on a node key or a node link is not a screen and never receives `redirect`, `identify`, `reload` or
`disconnect`; a socket that only claims to be one stays in the list, marked, and is never reached by "all" (see 2.18).

## Design notes

The app is the single authority. Every input — buttons, keys, Companion, HTTP, WebSocket, OSC — becomes a
command executed on the UI thread, and every output (operator UI, output window, NDI, browsers, OSC feedback)
renders the same immutable snapshot. A snapshot is published only when something visible changes and is
serialized once, however many clients are connected. Network failures are contained per connection: a stalled,
slow or malicious client is timed out or dropped without touching the timer.

## 2.10: keys, layouts, chimes and screen options

- **API keys** (Settings → Remote → API keys): `Authorization: Bearer swk_…`, `X-Stagewatch-Pin: swk_…` or
  `?pin=swk_…`. Scopes: **read** (`/api/state?detail=1`, reports, `/api/screens`), **control** (+ `/api/<verb>`,
  layouts, crew links), **mcp** (+ `/mcp`), **node** (read, stage messages, its own context and proposals; it speaks for
  one node id, see 2.18). Unknown or revoked keys are refused (401); from 2.18 they never count
  toward the 5-a-minute lockout, and revoking reaches links already open. Each key: 600 requests a minute; replies
  carry `X-RateLimit-Limit`, `X-RateLimit-Remaining`, `X-RateLimit-Reset` (seconds); over the limit → `429` with
  `Retry-After`.
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

### A playout line's receipt is its peer's answer (2.20)

A `playout` or `patterns` line that passes the door above is checked against the link's own facts before anything is sent,
and is refused here with why — nothing written to the peer — when it names no link Stagewatch runs, or a link not running
yet; when the peer answers as another kind than Settings set, or its kind is not yet heard from its STATE (only the
clip's verbs go then); when a PlayMap `cue` finds its playlist not idle or ended in a STATE at most 3 s old, a `go` with no
item has no next item to name, or the playlist follows another node's sync group; when a Patterns desk is asked for more
than one cue by its id; and when Patterns' own STATE says the clip is a playlist item or a stinger's, whose `pause` or `stop` Patterns
itself refuses — in Patterns' words: `Patterns: 'opener.mp4' is a playlist item — its own clock keeps running, so it is not
paused (end or seek move it)`. A Patterns desk whose context lists a verb with `ON <key>` is sent the clip's own key with it.

Otherwise the line goes on the link's one writer with a request id (`s<n>-<inst>`), and its receipt is the peer's own
answer (Show Bus 13.12.3), never a "done" the peer did not say:

| The peer | The receipt |
|---|---|
| `OK @id …` | `ok:true`, `stage:"applied"`, `effect`: `PlayMap Stage left took PLAYLIST GO pl1 it1 (its words)` |
| `ERR @id …` | `ok:false` (HTTP 409), `error`: `PlayMap Stage left refused PLAYLIST GO pl1 it9: Main show has no item it9` |
| `OK @id {"stage":"accepted",…}` | `ok:true`, `stage:"accepted"`, `effect`: `PlayMap Stage left: <its words>` — taken, not run yet; a follow-up comes |
| nothing within the receipt's 3 s | `ok:true`, `stage:"accepted"`, `effect`: `sent to PlayMap Stage left — waiting for its answer`; a follow-up comes |
| the link dropped before it answered | `ok:true`, `stage:"accepted"`, `effect`: `unconfirmed — …` (it may have run) |
| never written | `ok:false`, `error`: `not sent — …` |

`refs.peerRequest` is the request's number, the `<n>` of the id the peer answered. A receipt that says accepted while the
peer still owes its answer has one follow-up, the same id, within `receipt.final.ms` (30 s) of the send: the peer's final
answer — applied, refused, unconfirmed or not sent, never a later accepted, so a peer that answers accepted only after the
first receipt went is followed to its final word — and when none has come by then, Stagewatch's own: `ok:true`,
`stage:"accepted"`, `effect`: `unconfirmed — PlayMap Stage left took it and gave no final answer in 30 s — it may have run;
look at its state` (`no answer from PlayMap Stage left in 30 s` when it never answered). A WebSocket hears it as a second
receipt with `"final":true` as its last field; Companion TCP as the word `final` after the id; HTTP answers once, so its
sender has the first receipt alone.

```
{"type":"receipt","id":"w1","ok":true,"stage":"accepted","effect":"PlayMap Stage left: loading Walk-in","error":null,"refs":{"peerRequest":7}}
{"type":"receipt","id":"w1","ok":true,"stage":"applied","effect":"PlayMap Stage left took PLAYLIST CUE pl1 it2 (Walk-in cued)","error":null,"refs":{"peerRequest":7},"final":true}
OK @c1 accepted PlayMap Stage left: starting Intro
OK @c1 final applied PlayMap Stage left took PLAYLIST GO pl1 it1 (playing Intro)
```

A receipt says what the peer took, never what the room saw. That is the show log's: one row when the command's outcome
first settles (its moves after it go on the status line), and, for a GO, a CUE, a play or a pause, one more within the
operator's lead and 3 s of the peer's answer — `seen: Main show plays it1 on PlayMap Stage left` when a STATE the link read
after the command went shows it (after the peer's answer, when the STATE as it went already showed it: a STATE from before the
command proves nothing of it), or `not seen in PlayMap Stage left's STATE within 3.3 s: …` when none does. At most 32 effects (`link.inflight.max`) are looked for at once on one link; a command past
that still goes, and its sender's status line says its effect is not looked for.

### A GO at one instant, and the running order's cues (2.20)

`playout <link> go <playlist> [item] together` asks a PlayMap node to start at one instant, the operator's lead (300 ms unless
Settings say otherwise, 100–2000) from now: the line carries `AT <unix ms>` on the node's own wall clock — this PC's instant
plus the link's measured offset — only when that clock is fit for it (Show Bus 13.11.4–6: its believed sample at most 10 s old,
its error within the error a round trip as long as the lead gives) and was measured over TLS or on a link to this PC. A clock
measured over a plain link off this PC may be shown and schedules nothing. Otherwise the GO goes at once, untimed, and says
why. The receipt's `effect` ends with what was done, in the stage it reached: `… · sent with AT for 20:00:01.250 (± 3 ms)`
while the node has not answered (or never does), `… · scheduled for 20:00:01.250 (± 3 ms)` only once its answer says it took
the GO (applied or accepted), the node's own words alone when it refuses it, and `… · not timed: <why> — sent at once` for one
sent untimed. The show log has the same rows: one as it goes, one more once the node took it. A Patterns desk takes no `AT`
(its wire has no TLS until its Round 97), so its GO asked together goes at once and says so. The clock believed is the sample
with the smallest round trip among the last 8 (Show Bus 13.11.3), one each 2 s, so it can be older than 10 s while fresher ones
are kept: a timed GO then falls back and says its clock is not fit.

A running-order item can hold up to four playout cues, each naming its link and a PlayMap playlist's item or a Patterns
cue, by id. Nothing is sent when an item loads unless a cue is set to stand by (off by default: a PlayMap standby shows the
item's first frame on air, a Patterns one moves that desk's own standby): then, at the end of the show thread's turn that
loaded it and only if the item did not start in that turn, a PlayMap `PLAYLIST CUE` (only on a playlist its STATE showed idle
or ended at most 3 s ago) or a Patterns `CUE STANDBY`. When the item first starts, each cue GOes — a PlayMap `PLAYLIST GO`, a
Patterns `CUE GO` alone (never STANDBY beside it; its own standby fence decides) — one GO per PlayMap sync group (a
follower's cue is left to its leader's GO among the item's cues, and refused naming its leader when there is none), and the
cues marked together at one instant: a node whose clock is fit gets `AT`; any other is held on this PC and sent untimed half
its round trip before the instant, or at the instant itself by this PC's clock alone when its clock was measured over a plain
link off this PC; when any of them whose link is up has no clock sample at all, nothing is together and every GO goes at once,
said; and one whose link is not up is sent nothing, its own row saying why, without taking the others' timing. A follower's cue
is left to its leader's GO only where that GO's plan passes, and its row says whether the GO was sent. A held GO is sent only on
the link and connection it was held for and while the licence allows: a drop, a link Settings stopped or replaced, or a lapsed
licence while it is held leaves it not sent, with a row. A cue whose link this PC lacks, is switched off or not running, whose
peer answers as another kind or node than its settings name, or that names what its peer does not list (a PlayMap node's
`INDEX`, a Patterns desk's `CUE LIST`, asked once the link hears the peer's kind and again when its STATE's `project.index` or
`cuestack.listRev` moves; a peer answering as another node feeds none of it) is amber advice before its item, on the desk
whether or not its assistant is on, and is skipped at its start with a row. Every row — skipped, not stood by, not sent, sent
with AT, scheduled for, held, started late — is in the show log against the item, and so in the context's recent lines, and on
the desk's status line with the item's number; a timed GO whose node's STATE shows it started later than its `AT` by more than
half the round trip and 20 ms has a row `PlayMap Stage left: Main show started late by 140 ms (AT arrived after its instant)`.
The cues, and what each peer lists, reach the copilot's facts (`playout_links[].cues`, `listed`, at most 50 each).

An AI's or a peer's approved proposal that went to a playout link (Show Bus §11) is settled by the command's final receipt:
`done` only once the peer said it applied it; `failed` in the peer's words when it refused or nothing was sent; and one the
peer only took, or never answered within 30 s, is never `done` — §11 has no state between done and failed, so it is `failed`
with words that start `unconfirmed —` and say it may have run.

Every reader takes the links: the frame's `playout` row (the clip feeding the VT) ends with `link` and `node`, the link's
name and its peer's node id (empty for vMix); the config ends with `playoutLinks`, each link switched on under the licence —
`{"name":"stage-left","kind":"playmap","label":"PlayMap Stage left"}`, its kind as heard (or as set; empty while neither);
the context has a `link:<name>` item (kind `playout`) per link in its PeerLink record's light — amber at Show Bus level 0,
"proved at level 0 — the peer does not prove itself to this link" — with `clock:<name>` (kind `health`) amber while its clock
is apart, each link's verbs among the commands, and the context it pulled from its peer among the peers — only for the node
id the peer's STATE gives, when it gives one. A peer that answers as another kind than Settings set feeds none of these but
its `link:<name>` item, which says why: no clip, no verbs, no context.

### HTTP triggers that carry a secret (2.20)

An HTTP trigger (Settings → Triggers) may carry an auth profile — `"auth":{"kind":…,"secret":…}` on its rule in the settings
file, the secret DPAPI-protected for this Windows user, never in clear — and sends it the way its kind says:

| Kind | Headers | Request id |
|---|---|---|
| `playmap` | `X-PlayMap-Token: <secret>` | `X-Request-Id: tr<n>-<inst>` |
| `patterns` | `X-Patterns-Token: <secret>`, `X-Patterns-Client: Stagewatch` | before the line in the body: `@tr<n>-<inst> CUE GO 12` |
| `stagewatch` | `X-Stagewatch-Pin: <PIN or key>` | `X-Request-Id: tr<n>-<inst>` |
| `bearer` | `Authorization: Bearer <secret>` | — |

A profile is made in **Settings → Triggers**, the rule's **Secret** column: its kind, and a password box the desk checks
(printable ASCII) and protects; Save says why a rule can't keep it, and keeps nothing until it can. The file holds only what the
desk protected. A secret typed
into the file in clear is not one this PC can read: the trigger says so and sends nothing. A secret is printable ASCII, at most
256 characters — anything else no header carries, and it is refused before anything is dialled.

The secret goes only over a channel that keeps it (Show Bus 13.6.8, 13.8): to this PC — `localhost` or a loopback address, the
socket's own address checked as it connects, so a name that resolves elsewhere is dialled nowhere — or over `https://` to the
certificate a person pinned on this desk. Plain `http://` to another machine is refused when the rule is saved and again as it
fires, and nothing is sent. Over HTTPS the target's certificate is judged by its SHA-256 fingerprint alone, in the handshake:

- **First use.** The certificate shown is pinned `first` — by `addr:<host>:<port>` and, when the hello heard at that host and
  port names a node, by its node id too — and nothing is sent: `10.0.0.12:9940 showed the certificate 03e9 b152 ca42 518c … —
  pinned first; nothing sent`. A `first` pin carries no secret, however often the rule fires, until a person compares it on the
  target's own screen and trusts it with one press (right-click the status line, or the node's Show network row) — the press
  bound to the whole fingerprint it showed, the trust kept under both keys. A person's pin under either key is the pin: a hello
  lost, heard again or forged never moves a target off it, and a node heard at a new address keeps its pin there.
- **Changed.** A certificate other than the one a person pinned is `CERTIFICATE CHANGED`, refused at the handshake with no
  request written, red, until a person types at least 8 hex of the new one.

The pins are kept in `trigger-pins.json` beside the settings, with the holds below; a file that cannot be read is no pins and no
holds, said amber (13.8.4) until the next write lands. It keeps at most 128 pins (64 targets by address and node): a first pin
makes room before any a person made and never takes a person's place.

With a profile, the scheme, host and port are fixed text: a placeholder there is refused at save and at fire. Show text fills the
path and query alone, each value escaped (`{TITLE}` "Walk in/out" is `Walk%20in%2Fout`). The request goes through no proxy, takes
no cookie and follows no redirect: a 3xx after the request went reads unconfirmed — "a redirect — not followed; it may have run;
set its address to where it points" — since a 303 can follow a command that ran. A secret in the query beside a profile is
refused. A rule with no profile fires as it always did (its whole address filled in, redirects followed): one whose query carries
a secret (`?pin=`, `token=`, `api_key=`…) gets a warning at save and in its status, naming the parameter, never its value, and
the PIN's move into a `stagewatch` profile is offered at Save — made only where the rule would then be sent (to this PC, or over
`https://`); to another machine over plain HTTP it is not made, said, and the rule fires as before.

A fire has 3 s to reach its target and, once its request may have gone, 5 s for its answer (`receipt.deadline.ms` and 2 s), so
an `accepted` said at the acceptor's own deadline is read. Every HTTP trigger's answer is read by 13.12.3: `applied`, `refused`
in the target's words (`{"ok":false,…}` on a 200 among them), `accepted` (a `stage:"accepted"`, or a 2xx that says neither stage
nor msg — "took it — this node does not say whether it ran"), `unconfirmed` (a 408, a 5xx, an answer cut off after its headers,
or none in time once the request was written) or `not sent`. Patterns' `/api/cmd` answers CUE GO with its outcome's JSON inside
`msg` (`{"ok":true,"msg":"OK {\"outcome\":\"Confirm\",…}","id":…}`); 13.12.3's HTTP rows do not name a msg that carries JSON,
so Stagewatch reads that object by the line rows, as the wire reads `OK @id {…}`: `Confirm` is accepted — "asks for a second GO
— a person's" — `Requested` accepted, `Done` applied with the cue it ran (`cue 12 'Walk-in'`), never the JSON as words.

A 401 holds that secret for a minute and a 429 for its `Retry-After` (a minute at least, ten at most): not sent to that target
as that kind again until the hold ends or a new secret is set, so it never teaches its target to lock this PC out. The hold
names the secret by a keyed hash under a key this process draws and never writes — every rule that resolves to the same secret
is held with it — and is kept in `trigger-pins.json` with its end in wall time and the tags of the protected forms it covered,
never a hash of the secret, so a runner the supervisor rebuilds and a restart keep it; a wall clock stepped back never lengthens
it. The outcome is the triggers' status (Settings → Integrations, diagnostics and the context's `link:triggers`: red while a
certificate changed or a secret is held, amber while a certificate is pinned first only, the pins were lost, or a rule's last
fire with a profile was not sent or is unconfirmed — until its next answer), and every fire with a profile is one row in the
show log, whatever ends it — `Trigger 'GO VT' → 10.0.0.12:9940 (playmap): applied — playing Intro`. The secret is in no status,
row, log line, frame or context; a target that echoes it back has it masked. A Settings save refuses a rule that breaks these
rules and keeps every rule's profile.

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
| **Approval queue** | Stagewatch is the show's approval desk (hello cap `approvals`). `POST /api/proposals` (a node or control key, or the PIN; an MCP key posts none — 2.21) `{"from":{t,id,name},"target":{t,id},"line","reason"}` → `{"id":"p1-k3m9","state":"pending"}`. The proposal appears in Copilot beside Stagewatch's own, with the target's own risk for that verb (unknown = high). Approve or discard once. Aimed at Stagewatch, it runs here with a receipt; aimed at another app, that app collects it — `GET /api/proposals?target=<its id>&state=approved` with its node key — runs it through its own checks and reports `POST /api/proposals/<id>/result {"ok","words"}`. `GET /api/proposals/<id>` (read) → state (`pending`, `approved`, `rejected`, `done`, `failed`, `undelivered` after 60 s uncollected), `ok`, `words`, who decided. A node key is bound to the node it first spoke for; one app's key can't collect another's. Everything is in the show log. |

### How the desk rates another app's line (2.21)

A proposal aimed at another app takes that app's risk from its table, read by Show Bus §6's notation (`A|B` one word of
either, `[…]` optional, a bare `…` any further words, a `<…>` value one word but for the form's last and a `words`, `text` or
`line` value), and only from a verified table of the target's own kind: the context Stagewatch's own playout link pulled from
that app, while no key speaks for its id, and else the context the app posted with its own node key (or the PIN while no key
speaks for its id). The Show network's row shows that same context, so what the row says of a table is what rates its lines; a
context that proves nothing never stands in front of one that proves something, and one that names another kind of node than
the proposal's target never rates it. The most specific whole form decides — most literal words, then most single literals, an
exact tie the higher risk — and a line no form takes, a line of more than 64 words, or any line for an app whose table is
unverified is `high`. A form's value named `line` (PlayMap's `BIND ADD … TO <target line>`) raises the risk to the carried
line's, four lines deep; a carried line no form of the table takes is `high`, as it would be on its own, because the app may
run it by a rule its table does not state (PlayMap reads `VT STOP` as `VIDEO STOP`, and `PANIC NOW` as `PANIC`); more than 256
carried lines, at every depth together, is `high`, as PlayMap counts them; and JSON in such a row is `high`. Every rating is
bounded: a form is matched one place at a time against each word, and a rating past 200,000 steps (about 20 ms) is `high`. A
proposal is rated before the approval desk's lock is taken. So a line that only starts like a listed verb is `high` now, where
2.20 rated it by the verb's leading words (`pause every screen for good` against a row `pause`). As a reader of another app's
context Stagewatch keeps §14's numbers: 200 items, 256 commands (each verb whole up to 512 characters, a longer or malformed
one dropped and counted), words up to 512 and 400 links; the Show network row says what it did not keep, and names a context
posted too big to read (refused `413` before it is read) on the row of the node its key is bound to, or by its address.

### The desk's rules (2.21)

Show Bus §11 as the desk keeps it from 2.21 (`ProposalDesk`, `LineFence`):

- **Ids are unique across the desk's runs:** `p<n>-<run>`, the run four base-32 characters drawn when Stagewatch starts
  (`p17-k3m9`); `p17` alone names this run's. An id of an earlier run is `404` on `GET /api/proposals/<id>` and `409` on its
  result, in words that say the desk restarted since ("no such proposal — this desk has restarted since p17-k3m9 was made (it runs
  as q7zx now)"), so a target that remembers what it ran never takes a new `p1` for an old one.
- **A line is refused, never cut** (`400`, nothing queued, no key bound): over 2048 characters ("a line is at most 2048 characters
  — it is refused, never cut"); a control character ("a line holds no control characters"); an invisible format character — a
  direction override or isolate, a zero-width space, a word joiner, a byte-order mark, a soft hyphen, tag characters — which makes
  the line a person reads differ from the line that runs ("a line holds no invisible format characters (a direction override, a
  zero-width space) — what a person reads is what runs"; the zero-width joiner and non-joiner, which emoji and several scripts
  need, pass); half of a surrogate pair, sent raw or as a JSON escape ("a line holds no half of a surrogate pair"); a key — 13.5.1's
  shape under any prefix, or any word that opens with `swk_`, `ptk_`, `pmk_` or `agn_` ("a key rides in no line"); a window's
  address — `/w/` and a window's token, or a token one of this desk's windows holds now ("a window's address rides in no line"); or
  a secret placeholder (`<token>`, `<key>`, `<password>`, `<passcode>`, `<secret>`, or a row's `"secret": n`) filled with anything
  but `‹secret›` ("a secret rides in no line — type it on PlayMap Hall itself") — of the target's verified table, or of any table
  held for its id that proves nothing now, which may refuse a line but never lowers its risk. A proposer that keeps a secret sends
  `‹secret›` and runs its own record. The free words beside a line — a reason, a node's name — keep none of these characters: they
  are left out, not refused. 2.20 cut a line at 400 characters and dropped its control characters.
- **The desk's own `from` is refused** (`403`): kind `main` or `mcp`, or the desk's own id — an AI on this desk proposes through
  `/mcp`, as the desk. An MCP key posts no proposal: `403` "an mcp key posts no proposal here — an AI on this desk proposes through
  /mcp, as the desk's own". A target `{"t":"main"}`, or the desk's own id under any kind, is this desk (the proposal names it
  `main`). A `peer` or `desk` line proposed to this desk is refused: those are a person's.
- **A target raises its own risk, never lowers it:** a proposal whose `from` and `target` are one node, posted with the key bound
  to it, is rated the higher of its table's risk and the `risk` it states.
- **The desk watches collection.** Each target's last `GET /api/proposals?target=<id>&state=approved` is recorded. A proposal for
  a target that has not asked in 15 s (§14 `proposal.collect.ms`) is not approved: it stays pending, and Copilot, the status line
  and the show log say why ("PlayMap Hall has not asked this desk for approved lines in 40 s — it would not collect this").
- **The answers append** after §11's fields: the `201` gives `form` (the table's form that rated the line, or `null`) and
  `formWords` (that row's words); a proposal ends `…,"words":…,"collected":<Unix ms or null>,"form":…`.
- **A person's line** to another app — born approved, from this desk, `decidedBy` one of §11's role words (`operator`, `deck`,
  `wire`, `trigger`), only a line the app's verified table lists, only while the app collects — is the desk's rule from 2.21; it
  joins Copilot as sent, and its done, failed or undelivered reaches Copilot, the status line and the show log as any approved
  line's does. The verbs that send one (`peer <app> <line>`, `desk approve|reject`) come with 2.21's grammar.

## 2.17: windows — addresses Stagewatch controls

A **window** is a browser Stagewatch drives through an address of its own: `http://<pc>:9924/w/<token>`. Open it on a
TV, a projector PC, a signage player, or an OBS or vMix browser input, and Stagewatch decides what it shows. The address
is the window's identity and its pass: it views (past the viewing code) and controls nothing. Identity lives in
Stagewatch, not in the browser, so two OBS browser sources (which share storage) are two windows, and a player that
clears its storage is still the same window. Windows need the web server (Pro) and stay with the PC (never in a show
file).

| | |
|---|---|
| Window page | `GET /w/<token>`: no PIN. Its own policy: it frames only this Stagewatch's pages, no other site may frame it, no referrer leaves it, nothing caches or indexes it. An address not in use says so (404); five wrong addresses a minute from one device lock that device out for a minute (the page retries by itself); from 2.19 they count with wrong viewing codes and crew links, apart from wrong PINs (see 2.19: wrong passes) |
| Window list | `GET /api/windows` (read): each window's name, what it should show (`content`, `hold`, `holdText`, `holdImage`, `onLoss`, `fadeMs`, `rev`), `connected`, what it says it shows (`shown`: `rev`, `content`, `hold`, `ms`, `at`, `error`), `inStep` (it shows the revision it was sent), `device`, `address`. `path` (`/w/<token>`) only with the PIN or a control key |
| Context | Each window is a Show Bus item `window:<name>` (kind `screen`): green only when it says it shows what it was sent; amber while a change is sent but not shown, when it couldn't show a page, or when its link is lost; grey until someone opens it. A lost lobby screen never turns the show red |

### Commands

One grammar on every transport (HTTP `/api/window/...`, WebSocket, OSC `/stagewatch/window`, Companion `WINDOW …`, MCP
`run_command`, the copilot). A window is named with letters, digits, `-` and `_`; `all` (or nothing) is every window.

| Command | Effect |
|---|---|
| `window new <name> [page]` | Make a window (on its holding page, or showing the page). Copy its address from Screens → Windows, or `/api/windows` with the PIN |
| `window show <window\|all> <page\|setup\|holding\|black>` | Show a page on this Stagewatch (`/clock?h=12`, `/layout?name=foyer`…), a saved setup's page, the holding page or black. Pages that need the PIN (`/control`, `/producer`, `/designer`, `/report`) and other sites are refused |
| `window cue <window\|all> <page>` | Load the page behind the holding page; `release` shows it at once |
| `window hold <window\|all> [toggle \| text <words> \| image <n\|name\|off>]` | The holding page, drawn by the window itself (so it shows with no network): words, a holding image, or the show's holding image when one is on air, else black. The page stays loaded underneath. `toggle` releases them all if any is holding, else holds them all |
| `window release <window\|all>` | Back to the page, at once |
| `window identify\|reload <window\|all>` | Name on the window for 6 s / reload it (it comes back to what it showed). Refused for a window nobody has open |
| `window loss <window\|all> <hold\|warn>` | When the link drops: the holding page (audience screens; the default) or dimmed with NOT CONNECTED (crew screens). Never a confident stale clock |
| `window fade <window\|all> <ms>` | Crossfade between pages, 0 (cut) to 5000 |
| `window rename <window> <name>` · `window revoke <window\|all>` · `window delete <window>` | Rename; a new address (the old one stops at once, and a screen on it says so); delete (one at a time) |

A command that would change nothing is refused with the reason (`lobby already shows /clock`), and a receipt says so.
The copilot rates `show`, `cue`, `hold`, `release`, `identify`, `reload`, `revoke` and `delete` high (they change what
the room sees) and `new`, `rename`, `loss` and `fade` low.

### What a window does

- **One link.** The window holds one WebSocket (`/ws?win=<token>&detail=1`): it hears `{"type":"window",id,name,content,
  hold,holdText,holdImage,onLoss,fadeMs,rev}` before any frame and again on each change, then public detail frames. It
  may send only what any public page may (cue acknowledgements, questions). The pages inside take their frames from the
  window (stamped with when they arrived), so a page switched in draws at once and the screen counts one connection.
- **Swapped in only when drawn.** Two frames: the next page loads behind the one on screen and is shown only once it
  has loaded and drawn its first frame (two painted frames, then the cut or fade). A page that won't load in 8 s, or
  isn't a Stagewatch page, never reaches the screen: the old page stays, the window retries (2 s, 4 s… 30 s) and reports
  why. After each swap, hold or failure it reports `{"type":"shown",rev,content,hold,ms,error}`.
- **Blackout** covers every window. A revoked or deleted address shows "This window's address was replaced or removed"
  and stops trying. Reconnects back off with jitter, so a room of windows doesn't reconnect in one burst.
- Double-click or `F` on the window toggles full screen; the screen is kept awake.

## 2.18: show apps on /ws — one key, one node

Patterns, PlayMap and AGMessage follow the timer over `/ws`, as a browser does. Until 2.18 Stagewatch couldn't tell them
from browsers: they were listed in Screens, `screen identify|reload|go|disconnect all` reached them, any node key could
post a context under any app's id, and the PIN or a control key could speak for an app that had a key of its own. Now a
show app that names its node (`node=`) and proves it is a **node link**: it hears first what Stagewatch made of it, it
is never a screen, and no screen command reaches it. A socket on a node key leaves Screens too, even before its app
names its node; one on a control key or the PIN stays a browser screen until its app does. A node key speaks for one
app, checked before anything is stored. Following the timer is never refused for a key problem alone: frames are what a
stage reads.

### A node link

```
GET /ws?node=9f8e7d6c&t=patterns HTTP/1.1
Upgrade: websocket
Authorization: Bearer swk_…      ← a node key; a control key, or the PIN when one is set, also proves; nothing = a claim
```

- `node` is the app's Show Bus id (its hello's `id`): 1–40 letters, digits, `-` or `_`. `t` is its kind (its hello's
  `t`: `patterns`, `playmap`, `messenger`…): 1–16 letters, digits, `-` or `_`.
- The credential goes only in the `Authorization` header. `detail=1` works as before. `cid` and `page` are ignored; send
  `cid=<your id>&page=/<app>` as well, and a Stagewatch older than 2.18 (which ignores `node`) lists each install under
  its own id. It keeps a `cid` only when it is 6–32 letters, digits, `-` or `_`; send the id you send today when yours
  isn't.
- These are refused before the upgrade with HTTP 400 `{"ok":false,"error":"…"}`, and are never counted toward a lockout:

| Request | Error |
|---|---|
| `node` with `win`, or `Authorization: Window …` | `a window's link is not a node link` |
| `node` with `?pin=` | `a node link carries its key in the Authorization header, never in the URL` |
| `node` with `role=control` | `a node link reads; it does not control` |
| a bad `node` | `a node id is 1-40 letters, digits, - or _` |
| a bad or missing `t` | `a node link names its kind (t=patterns, playmap, messenger…)` |
| this Stagewatch's own id | `that's this Stagewatch` |

### The verdict comes first

Before any frame, and before the stuck beat when the show has stopped, a node link hears one `auth` message:

```json
{"type":"auth","ok":true,"scope":"node","node":{"t":"patterns","id":"9f8e7d6c","verified":"key"}}
{"type":"auth","ok":true,"scope":"pin","node":{"t":"messenger","id":"a1b2c3d4","verified":"operator"}}
{"type":"auth","ok":true,"scope":"node","node":{"t":"patterns","id":"5e6f7a8b","verified":"none"},"words":"this key speaks for patterns 9f8e7d6c — make a key for patterns 5e6f7a8b"}
{"type":"auth","ok":true,"scope":"none","node":{"t":"patterns","id":"5e6f7a8b","verified":"none"},"words":"no key — the id is only claimed"}
{"type":"auth","ok":false,"error":"unknown or revoked API key","retry":60}
```

- `scope` is the credential: `node`, `control`, `read`, `mcp`, `pin` or `none`. `verified` is what speaks for the id:
  `key` (its own node key), `operator` (the PIN or a control key) or `none`.
- `words` comes only with `none`: `no key — the id is only claimed`; `a read key reads; only a node key speaks for a
  node`; `this key speaks for <kind> <id> — make a key for <kind> <id>`; `<kind> <id> speaks with its own key`.
- A refused credential hears `ok:false` with `retry`, the seconds to wait before trying again: `unknown or revoked API
  key`, `wrong PIN` and `too many wrong PINs — wait a minute` (60), `rate limit: too many requests with this key` (the
  seconds left in the key's minute). The socket stays open with the public frame. No key reason closes a socket, so an
  app redialling with a bad key never loops.
- With a viewing code set, a socket that proves nothing must give the code (`&view=`), as any screen must. Without it, it
  hears one `{"type":"auth","ok":false,"view":true,"error":"This screen needs the viewing code"}` and closes; a refused
  key's words follow as `"retry":60,"refused":"unknown or revoked API key"`. A key in force or the PIN opens the viewing
  code by itself.

| On a `node=` socket | It is | `verified` | In Screens |
|---|---|---|---|
| a node key bound to this id, or bound to nothing while the id is free (it binds now) | a node link | `key` | no |
| a node key bound to another id, or to this id under another kind, or an id another key holds | a node link | `none`, with the binding's words | no |
| a control key, or the PIN when this Stagewatch asks for one, for an id no key holds | a node link | `operator` | no |
| a control key, or the PIN when this Stagewatch asks for one, for an id a key holds | a node link | `none`, with the binding's words | no |
| a read or MCP key, the PIN on a Stagewatch with no PIN set, or nothing | a claim | `none`, with words | yes, flagged as a claim, never in "all" |
| a refused key or PIN (unknown, revoked, wrong, locked out, over its rate) | a claim | — (the `ok:false` line) | yes, flagged as a claim, never in "all" |

- A node link verified `none` (two installs pasted one key, say) keeps its frames: only the node routes below refuse it.
  A Patterns twin given the main machine's key still shows the countdown.
- A node link reads what its credential reads: the public frame, or with `detail=1` the operator frame.
- A node link never hears `redirect`, `identify`, `reload` or `disconnect`. A claim hears them only when it is named by its
  own screen id (`claim-` and 12 hex, in `/api/screens`) or a name the operator gave it.
- One install is one row in Producer → Show network however many sockets it opens; two installs are two rows.

### Sockets that name no node

Browsers, and show apps that don't send `node=` yet:
- Admission is as before, and a control page hears its `auth` as before. An accepted key hears nothing new: the first
  message is a frame.
- A refused key (in the header, or `?pin=swk_…`) on a page that isn't a control page hears one
  `{"type":"auth","ok":false,"error":"unknown or revoked API key","retry":60}` first, then keeps the public frame.
- A **node** key takes a socket out of Screens even without `node=`: it is a show app listed under its key, which names
  no node id. It follows the timer and speaks for no node. A control key, a read key or no key without `node=` is a
  browser screen, as before.
- A browser keeps its own `cid` only when it can't be taken for an id Stagewatch makes: one starting `claim-` or `win-`
  gets an `anon-` id instead.

### While a link is open

Every change reaches open sockets at once, latest wins:
- **A key revoked:** `{"type":"auth","ok":false,"error":"this key was revoked","retry":60}`. The socket drops to the
  public frame (a node link becomes a claim), or, when a viewing code is set and the socket didn't give the code itself
  (or come on a crew link), hears that message with `"view":true` and closes. Node links and keyed browser sockets alike.
- **A binding moved** (the operator's choice, or the key's first use on another route): a node link hears a fresh verdict
  with its new `verified`.
- **The PIN changed:** what the PIN admitted drops to a viewer with
  `{"type":"auth","ok":false,"error":"the PIN changed — enter the new one"}` (with `"retry":60` on a node link, which
  becomes a claim), or closes with `"view":true` on the same rule as a revoked key; the control page asks again.
  **The PIN cleared:** a node link it admitted becomes a claim and hears its new verdict (`no key — the id is only
  claimed`).

### One node key, one node

A node key speaks for one node id. It binds on its first proven use (a node link, a context, a proposal's `from` or a
collect) when the id is free. From then on:
- the key is refused for any other id, and for its id under another kind: `this key speaks for <kind> <id> — make a key
  for <kind> <id>`;
- an id that has a key is spoken for only with that key, so the PIN, a control key or no credential are refused for it:
  `<kind> <id> speaks with its own key`.

The check runs before anything is stored, queued or claimed; a refused request binds nothing and changes nothing. The
credential each request carries: a node key proves its own node (`key`); a control key, or the PIN when this Stagewatch
asks for one and it is given right, proves the operator (`operator`); a read or MCP key, a PIN on a Stagewatch with no
PIN set, or nothing, proves nothing (`none`).

| Route | Names its node by | Refused |
|---|---|---|
| `POST /api/peers/context` | the body's `node.id` | **403** with the binding's words (was 400); 400 `not a Show Bus context (v 1, node.t, node.id; each key once)` for a body it can't read |
| `POST /api/proposals` | `from.id` | **403** (was 400); 400 `a proposal is a JSON object that names each key once` or `from.t and from.id are required` |
| `GET /api/proposals?target=<id>&state=approved` | `target` | **403**; a target that isn't a node id is 400 (was 403) |
| `POST /api/proposals/<id>/result` | the proposal's target | 409, as before |

- A context taken answers `200 {"ok":true,"verified":"key"|"operator"|"none"}`. A context never replaces one of stronger
  proof for the same id while that one is held (30 s): 403 `<kind> <id> spoke with a stronger proof 12 s ago`.
- A context verified `none` is kept and labelled. It never lowers a proposal's risk (unknown = high).
- A body that repeats a key, at any depth, is refused 400 before anything reads it.
- `POST /api/context` answers 405 `this is Stagewatch's own context — POST yours to /api/peers/context` (it used to answer
  as a read). A proposal `from` this Stagewatch's own id, or a collect for it, is refused 403.
- Bindings are saved on the keys (`remote.apiKeys[].node` and `nodeKind` in the settings, never a secret) and survive a
  restart. Only the operator moves one: Settings → Remote → API keys → **Let it speak for…**, or **Let a key speak for
  this node…** on a Producer → Show network row. Each first use and each move is journalled with names and ids. Keys are
  never managed over the wire.

### What counts toward the lockout

Wrong PINs, wrong viewing codes and wrong window addresses: five a minute lock the address out, and a locked address is
refused even a valid key (`too many wrong PINs — wait a minute`). An unknown or revoked key and a request with no PIN
no longer count: neither is a guess (a key is 160 random bits), and an app still polling with an old key used to lock its
whole PC out, browsers included. From 2.19 wrong viewing codes, window addresses and crew links count apart from wrong PINs,
and nothing but the minute clears their count (see 2.19: wrong passes).

### What other readers see

- **Context** (`GET /api/context`, MCP `get_context`): each `peers` row ends with `link` (`"ws"` while a node link that a
  key or the operator proves follows the timer, else `""`) and `verified` (what proves the row's light and headline now:
  `key`, `operator` or `none`; `""` for an app only heard). A claim, a key conflict or a key that names no id adds no row,
  and a context that proves nothing never shows under an id a link proves. At most 32 rows.
- **Screens** (`GET /api/screens`): browsers and claims; each row ends with `claim` (`"patterns 9f8e7d6c"`, `""` for a
  browser). Show apps aren't listed.
- **The copilot and the assistant:** `screens` rows end with `claim`; `other_apps` rows end with `link` and `verified`.
  A context that proves nothing reaches the advice marked `unverified:`, one level lower, never as urgent.

### Screen commands reach browsers only

`screen identify|reload|disconnect|go|save` look for their target in a fixed order, so a name a show app chose can never
shadow a screen:
1. `all`, or nothing: every browser that is neither a control page nor a claim (`Screens: no browser screen is
   connected` when there is none);
2. a screen id (a claim's included);
3. a name the operator gave a screen;
4. a show app's node id, or the name of one verified by its own key: refused, `Screens: 9f8e7d6c is a show app (Patterns
   FOH), not a screen — screen commands reach browsers only (Producer → Show network)`;
5. else `Screens: nothing matches "…"`.

`screen name <id> <name>` names a screen id (a claim's included, or one still to connect), never a show app's.

The same refusal comes back on every transport (HTTP, WebSocket, OSC, Companion, MCP), in its receipt where one was
asked for. There is no command that disconnects a show app: revoking its key is the wall.

### Stagewatch's Patterns link: the token never crosses the network

Stagewatch's own link to a Patterns desk (TCP 9697, Settings → Integrations → Playout links):
- **A proof, never the token.** The desk's `NONCE` is answered with `AUTH-PROOF` only when it is exactly 32 lowercase
  hex, once per connection: a desk that refuses the proof and sends a fresh NONCE gets no second one.
- **Plain `AUTH <token>` only on this PC.** It goes only to a socket whose remote address is loopback, and never on a
  connection that heard a NONCE. To a Patterns on another PC older than Show Bus 1.1, nothing secret is sent: the link
  stays amber, "`<host>:<port>` is older than Show Bus 1.1 and pairs only by sending its token in clear — update
  Patterns; the token stays on this PC".
- **Nothing before pairing.** Every STATE (the desk's greeting included), receipt, clock sample and context that comes
  before the link pairs (`OK paired` to its proof, or on this PC to its token) or is open (a desk that asks for no proof:
  a Show Bus 1.1 desk, or one on this PC while this link holds no token) is set aside and counted. Commands are refused
  here with the reason, and nothing is written. Once it pairs or is open, a 1.1 desk is asked once for its STATE
  (`@st STATUS`), so the clip reads at once.
- **Open desks.** A desk that asks this link for no key is followed when it speaks Show Bus 1.1, or when it is on this
  PC and this link holds no token. While this link holds a token the desk never asked for, it is amber ("asks for no key
  — the token stays on this PC").

In 2.20 every playout link keeps these rules, a PlayMap node's line wire (TCP 9941) among them: Settings → Integrations →
Playout links names up to 8, each with its own address and token. A link to a PlayMap node whose hello names its `https` door
goes over TLS (`wss://…/ws`, subprotocol `playmap.lines.v1`, a line a message), where it proves the token the same way and
never sends plain `AUTH`; its certificate is checked by fingerprint, pinned first and trusted by a person on the desk. Once
pinned the link is never dialled plain — it dials the door a hello names, else the one kept beside the pin — and once a person
trusted it a changed certificate is CERTIFICATE CHANGED until a person types at least 8 hex of the new one (`NODES.md` §5). A
`429` at that door and a wrong proof (PlayMap's `ERR`, or its close `4401`) hold the token like any refused proof. A
`playout <link> trust` line proposed to this desk (`POST /api/proposals`) is refused, 400, and never queued.

Two things change for older desks: a Patterns older than Show Bus 1.1 on another PC is no longer followed, even when
neither side holds a token ("pairs only on its own PC — update Patterns"), so its VT countdown stops until it is
updated; and a wrong or stale token on this PC leaves the link red and unread, even though Patterns trusts this PC.

## 2.19: Show Bus 1.3

2.19 carries Show Bus 1.3, final (`docs/SHOWBUS.md`): the contract with §12 Windows, §13 PeerLink and §14 The numbers, its body
held by the hash its header prints. What changes for whoever reads or drives Stagewatch: its certificate's span (first, because an
upgrade shows it), wrong passes counted apart from wrong PINs, its hello, and its own context within §14's sizes. The contract's
§8.1 says what 2.19 does not keep of 1.3 yet, and `NODES.md`, written from the code, is Stagewatch's node page. Nothing on the wire
speaks PeerLink yet (below).

### The certificate: 825 days in all, replaced once

This install's certificate (2.16) spans 825 days in all, NotBefore to NotAfter, its day of backdating for a skewed clock
included (Show Bus 1.3 §10: Apple devices refuse a TLS server certificate valid for longer). Until 2.19 it spanned 826 —
a day back and 825 on. Stagewatch reads its certificate once per run, the first time it needs it: at start with Remote and
HTTPS on, otherwise when Settings opens. That read makes a new one when fewer than 30 days of the one it has remain, or
when that one spans more than 825 days. So **the first start of 2.19 replaces a certificate an older Stagewatch made, once**;
the new one is kept until fewer than 30 days of it remain, about 795 days on.

A new certificate is a new fingerprint, so everything that pinned or trusted the old one says so once, until its operator
accepts the new one:
- Stagewatch Slides on presenter PCs: CERTIFICATE CHANGED on the target's row, with the first 8 hex of the new one —
  compare them with this Stagewatch's Settings → Remote, then press **Trust new certificate**;
- other apps' links to this Stagewatch that pinned it (Patterns', PlayMap's and AGMessage's do): CERTIFICATE CHANGED —
  accept it there, after the same comparison;
- browsers and devices that trusted the old certificate (`GET /tls`): a certificate warning on `https://` — trust the new
  one the same way. With **Require HTTPS** on, their screens wait until they do.

Settings → Remote shows the fingerprint's first 32 hex in groups of four (its tooltip has all 64), grouped as the show log,
a presenter PC's row and the other apps group them; the first 8 are what each compares. The desk's status line says the
replacement when it happens, and the show log says when and why ("Certificate replaced: it spanned 826 days, more than the
825 Apple devices and Show Bus 1.3 allow. The new one's fingerprint starts ab12 cd34 …, as Settings → Remote shows it (its
tooltip has all of it): …"). A show resumed after an unexpected close carries the same row, so its report says it too.
Upgrade off show day, or allow for that one accept on each device.

The certificate is replaced only once the new one is saved: the new one is written beside the old and swapped in whole,
keeping the stored password, so the pair on disk is never half old and half new. When something gets in the way, the
status line and the show log say what happened, and nothing is quiet:
- the new one can't be made or saved while the old one has not ended: the old one stays ("Certificate kept, not replaced
  …"), every peer keeps its pin, and the next start tries again;
- the stored certificate can't be opened (damaged, or sealed for another Windows user): a new one is made, as on a first
  start, and journalled as a replacement;
- a new one can't be saved at all: it serves this run only, and the next start makes another;
- the files can't be read (locked, denied): no certificate this run, and the files are left as they are for the next start.

With HTTPS on and no certificate, the remote line says "HTTPS off: no certificate", and with **Require HTTPS** on the status
line says it is not in force, rather than plain HTTP serving quietly.

### Wrong passes: what each wrong attempt counts toward

Stagewatch keeps two counts of wrong attempts per device (its address), each in a minute of its own that starts with that
device's first wrong attempt (Show Bus 1.3 §12.2; `view.wrong.per.minute` is 5, §14):

| A wrong attempt | Counts toward | Five in a minute | What clears it |
|---|---|---|---|
| a wrong PIN (`?pin=`, `X-Stagewatch-Pin`, `Authorization: Bearer`, a control page signing in, a node link that brings the PIN) | **the PIN's count** | the PIN and every key are refused that device for the rest of the minute: `too many wrong PINs — wait a minute` (401) | the minute's end; below the limit, the right PIN or a key in force (a locked device's PIN and keys are refused before they are checked) |
| a wrong window address (`/w/<token>`, `win=`); a wrong viewing code (`view=`); a crew link key that is no crew link (`key=`), where a crew link is asked for: at a page's door while a viewing code is set and nothing else opened the view, and on a cue's confirmation or a department's notes while **Crew pages confirm cues only** is on (with neither, no key is checked and none counts) | **the view passes' count** | every view pass is refused that device for the rest of the minute: a window page says so and tries again by itself (429), a window's link closes and retries, never told its address is gone, and a fetch, a screen or a crew page hears `too many wrong viewing codes, window addresses or crew links from this device — wait a minute` | only the minute's end: never the right PIN, a key or a right pass |

Counted nowhere: a request with no PIN; an unknown or revoked key; a crew page with no key; another page's crew link (Lighting's
link on Sound's notes or cues is refused with its words, and is a real link, not a guess); a pass that decided nothing (a crew
link replaced by **New crew links**, given beside the right viewing code); a node link refused before the upgrade (400); a key over
its rate, which has its own count per key (600 requests a minute, 429 with `Retry-After`).

- **Apart.** Five wrong view passes never lock out the PIN or a key, so the operator's pages still open on a device locked out of
  view passes; five wrong PINs never lock out a viewing code, a window or a crew link. Until 2.19 the two were one count, which the
  right PIN or a key cleared (a key holder could go on guessing view passes), and wrong crew links counted nowhere.
- **Once per socket.** A page's WebSocket gives its crew link key once, when it opens: a key that is no crew link counts once
  however many cues the page tries to confirm. Each HTTP request gives its key again, and counts again.
- **A stale crew link counts with every attempt.** A link replaced by **New crew links** is no crew link any more, and nothing
  tells it apart from a guessed one: a page opened from it counts once as it connects, and a mistyped viewing code on it counts
  twice (the code and the key), so such a phone is locked out after two mistyped codes, not five. Reopen the page from its new
  link. Each pass a request gives is counted because each is a guess: counting one would let one request try two.
- **The show PC too.** Loopback is not exempt, and nothing at the desk ends the wait: five wrong passes from the show PC (a stale
  crew tab, a mistyped code) turn away that PC's own windows (a vMix or OBS browser source on `/w/<token>`) as they reconnect, for
  up to a minute. They wait and try again by themselves, never told their address is gone; the PIN and keys still open the desk's
  own pages.
- **Why five.** A window's address is 130 random bits, so for windows the count stops noise, not guessing; a crew link key is 40
  bits and a viewing code is what the operator typed, and for those it is what stops a guesser.
- PeerLink's wrong proofs will count in a table of their own, per address and key (Show Bus 13.5.6), once this Stagewatch's `/ws`
  speaks LINK, in a later release; it does not yet.

### The hello (Show Bus 1.3 §2)

`NODES.md` §2 shows the hello as the code builds it, and its numbers. What changed:

| | Until 2.19 | 2.19 |
|---|---|---|
| **Where it goes** | broadcast whenever the web server ran, however it was bound | only while the web server is bound to the network (Settings → Remote → *Allow other devices on the network*): to 255.255.255.255:9928, then a copy to 127.0.0.1, each send on its own. With Remote set to this PC, none: apps on this PC link it at 127.0.0.1 and its port |
| **What it names** | `proto` 1; `companion` and `osc` whenever they ran; a stray `HttpPort` field | `proto` 3; `inst` (8 hex, new at every start); cap `windows`; only doors other machines can open (`companion` and `osc` only while bound to the network); no `HttpPort` |
| **Names** | as typed | no control characters, at most 64 characters, the whole hello within 1200 bytes |
| **What the desk says** | nothing | Producer → Show network's first line: what the last beat did and, when it went nowhere, why. After 3 beats in a row with every send failed, the context's `bus:hello` item is amber ("this Stagewatch's hello can't go out: … — other apps can't find it"); it is amber too while another machine says this Stagewatch's id |
| **What it hears** | 16 caps, names cut at 60, each id under the address it was last heard from | every kind; at most 128 ids, each forgotten after 60 s unheard; a datagram of at most 2048 bytes; its first 32 caps and 8 doors; names cut at 64. One peer per id, with every address heard within 6 s (at most 4), and the dialled one kept while it is heard |
| **Two nodes, one id** | one row, whichever spoke last | an `inst` coming back after another within 6 s (A, B, A) marks the id in conflict for 60 s after the last return; a restart is not one. The Show network row is amber, naming both machines and addresses, and so are the advice and the AI's facts; the context lists no `peers` row for that id (§6), and the Companion module reads no peers. Its own id said by another machine makes `bus:hello` amber |

### Stagewatch's own context within §14

`GET /api/context` and MCP's `get_context` (Show Bus §6) stay within §14's numbers:

- **Sizes.** At most 64 KiB as sent (UTF-8), 200 items, 400 links, 20 recent lines, 256 commands and 32 peers. A headline keeps
  200 characters, a label 80, a state 160 and a recent line 200, each cut with "…"; an item's id keeps 60 and its kind 30, cut with
  no mark. No cut splits a character in two. An item light that is not one of §6's is sent grey. The context's name is the hello's,
  by the same rule.
- **Commands, panic, air and transport first:** `blackout`; `window hold`, `show` and `revoke`; `start | pause`; `next`;
  `patterns end [<seconds>]`; `signal go all`; `signal go <dept> <cue>`. The rest follow by what an operator needs most. Each verb
  is §6's notation (2.21): every form of it, written out, is a line this desk's parser takes, at a risk no lower than its own, so
  `message <phrase> [info|warn|crit [off|slow|fast [ack]]]` takes a message of one word or in double quotes, and its urgency
  before its flash, as the parser reads it.
- **Trimming.** A context over a count or 64 KiB is trimmed, each step only until it fits, in §6's order: the oldest recent lines;
  grey links, then the other links; commands that are not panic, air or transport, from the end; grey items, then green. Only if it
  still does not fit: amber states, then red ones, cut to 40 characters (Stagewatch's own length; §6 leaves it to the sender); then
  amber items, red items, and panic, air and transport commands, from the end. A link whose end names an item left out goes too.
  What was left out is said last, in a grey `context:trimmed` item: "left out: 3 recent, 12 links, 40 commands, 9 items" (its
  nonzero parts). The context's light is still the worst of every item, those left out among them.

### Show Bus 1.3, its kit and the copies

- **1.3 is final.** `docs/SHOWBUS.md`'s header prints its body's SHA-256 on a line of its own, and names on its Landed line the
  trees whose copies hold it: Stagewatch alone today. Patterns, PlayMap and AGMessage each take a copy in a release of their own;
  `tools/showbus-copies.sh` compares the trees the Landed line names (§14.5).
- **The PeerLink kit** is in this tree: Stagewatch's core (`src/Timer.Core/PeerLink.cs`), every vector in §13's block, and the
  scripted peer (`tools/showbus/peerlink-peer.mjs`, pinned by §14's `peerlink.peer.sha256`), with which the suite runs §13.20's
  scenarios. **Nothing on the wire speaks it yet:** `/ws` node links prove a bearer key or the operator's credential as in 2.18,
  the Patterns link pairs by `NONCE` and `AUTH-PROOF` as before, and the hello carries no cap `peerlink`.

### Wire answers that changed

- The hello: none while Remote is set to this PC; `proto` 3, `inst` and cap `windows`; Companion and OSC named only while bound
  to the network; no `HttpPort`.
- The certificate: 825 days in all; one an older Stagewatch made is replaced once, so its fingerprint changes once.
- Wrong view passes: their own count, with their own words and, for a window page, `429`; a right PIN or key no longer clears it.
- The context: within §14's sizes, commands in a new order, long fields cut, and `context:trimmed` when something is left out.
