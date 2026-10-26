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
| `screen` | `identify [screen]` | Flash each screen's name on it for 6 s (no screen = every browser screen; never a show app, 2.18) |
| `screen` | `go <setup \| /page> [screen]` | Send screens to a saved setup or a page on this Stagewatch (a screen is its name or id; none = every browser screen) |
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

Stagewatch's own link to a Patterns desk (TCP 9697, Settings → Integrations → Patterns video):
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
- **Commands, panic, air and transport first:** `blackout`; `window hold`, `show` and `revoke`; `start / pause`; `next`;
  `patterns end 10`; `signal go all`; `signal go <dept> <cue>`. The rest follow by what an operator needs most.
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
