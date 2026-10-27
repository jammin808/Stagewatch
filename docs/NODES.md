# Stagewatch on the network: nodes and protocols

This is Stagewatch's own node page: how other apps find it, what it hears of them, how they read and drive it, and the link
each show app has with it. The contract between the apps is [`SHOWBUS.md`](SHOWBUS.md) (Show Bus 1.3, which Stagewatch 2.19
carries); this page says what Stagewatch's code does of it, and its §8.1 row says what 2.19 does not keep yet. Every route and
verb, by the credential it needs, is in [`REMOTE.md`](REMOTE.md).

The page is written from the code, and the suite's `NodesDocTests` holds three things of it to the code, failing when either side
changes and the other does not follow: the two hellos in §2 are what `ShowBusHello` builds for the inputs they show; every number in
§8's table is the code's own, and §14's where §14 names it; and while Find listens on Patterns' 9700, §2 says until when. The prose
names a number by its row of that table wherever it has one. A shape it gives in words alone (an id's hex digits, a nonce's 32, a
verdict's `retry`, `patterns end`'s 10 s) is the code's as this page was written, and nothing holds it here.

## 1. Roles

One executable, run in roles:

| Role | Start it with | Kind (`t`) | What it does |
|---|---|---|---|
| Stagewatch | `Stagewatch.exe` | `main` | The show: the one authority for its room's time |
| Stagewatch Slides (presenter PC) | `Stagewatch.exe --slides` | `agent` | Reads this PC's PowerPoint slideshow and sends it to one or more Stagewatch machines (§3) |
| The headless rig | `dotnet run --project tools/Stagewatch.Rig` | `main` | Stagewatch's real servers around a simulated show, for tests and the interop lane; with `--bus PORT` it hears hellos and says the same hello ([`BUILD.md`](BUILD.md)) |

## 2. Discovery: the hello on UDP 9928

### What Stagewatch says

Every `hello.every.ms` (2 s) Stagewatch builds its hello afresh (`ShowBusHello.Main`) from the doors its servers bound, as each
bound it, and the settings as last applied, never as they are being edited; beside them only the licence (for `mcp`), the PC's
name, its clock and this run's `inst`. So a hello names only a door that is open:

```json
{"t":"main","v":1,"id":"3f9c2a1b7d0e","name":"Hall A","machine":"FOH-PC","web":9924,
 "caps":["timer","cues","messages","context","receipts","clock","screens","windows","aux","slides-in","copilot","approvals","mcp"],
 "proto":3,"ports":{"http":9924,"ws":9924,"https":9924},"clock":1760101234567,"host":"FOH-PC",
 "tls":"sha256:03e9b152ca42518c28128f36def699be8aea5eec11bb9d5afc18a314f8c3eb04","inst":"c3d4e5f6"}
```

- `t` is `main`, `v` 1, and `proto` 3: Show Bus 1.3, the revision whose shapes it speaks (1.1 and 1.2 Stagewatches sent 1).
- `id` is this install's id: 12 lowercase hex from the system's random generator, made once and kept in the settings, so a
  renamed Stagewatch is still the same node. A copied settings file copies the id with it (see *Two nodes, one id*).
- `inst` is 8 lowercase hex (`hello.inst.chars`), new at every start of the process.
- `name` is the room name (Settings → Integrations), or the PC's name when there is none; `host`, and the older `machine` beside
  it, is the PC's name. Each loses its control characters and keeps at most `hello.name.chars` (64) characters, never cutting a
  character in two. Only a hello that would pass `hello.bytes` (1200) as sent has them cut shorter, which a name of unassigned or
  escaped characters alone can need.
- `web`, older, is `ports.http` again.
- `ports` names only doors every receiver can open: `http` and `ws` on the web server's port; `https` on the same port while it
  has a certificate (TLS is recognised by its first byte); `companion` and `osc` only while those servers run bound to the
  network. A door bound to this PC is never named.
- `caps`, in this order: `timer`, `cues`, `messages`, `context`, `receipts`, `clock`, `screens`, `windows`, `aux`, `slides-in`,
  `copilot`, `approvals`, and `mcp` while MCP is on and the licence allows it. Each is a §14 `cap … main` row; Stagewatch
  declares no cap of its own.
- `clock` is its Unix time in ms as it builds the hello; `tls` its certificate's fingerprint, absent while it has none.
- No PIN, key, window address or show fact is ever in Stagewatch's hello.

**Where it goes.** Only while the web server is bound to the network (Settings → Remote → *Allow other devices on the network*):
to 255.255.255.255:9928, then a copy to 127.0.0.1:9928, each send on its own, so a broadcast that fails never stops the copy. With
Remote set to this PC it says no hello at all (Show Bus §2): other apps on this PC link it at 127.0.0.1 and its port, typed in
their link settings. With Remote off there is no web server to name, and no hello.

**What the desk shows.** Producer → Show network's first line says what the last beat did and, when it went nowhere, why: *Said on
the network as Hall A (3f9c…)*; *Said on this PC only — no network: …* (only the copy went); *Not said: …* (every send failed); *Not
said — Remote is set to this PC; other apps reach it at 127.0.0.1:9924*; *Not said — Remote is off, so no other app can reach this
Stagewatch*. After `hello.failed.beats` (3) beats in a row with every send failed, Stagewatch's own context carries an amber
`bus:hello` item ("this Stagewatch's hello can't go out: … — other apps can't find it"), so other apps and their AI know why they
cannot find it.

**A presenter PC's hello** (`ShowBusHello.Agent`) has no doors, the one cap `slides`, and `presenting` only while it shows a slide,
the one show fact a presenter PC's hello carries:

```json
{"t":"agent","v":1,"id":"pc4be19a2c11","name":"Lectern laptop","machine":"LECTERN","web":0,"presenting":true,
 "caps":["slides"],"proto":3,"clock":1760101234567,"host":"LECTERN","inst":"9a8b7c6d"}
```

Its `id` is its feed's `agent` id (§3): `pc` and 10 hex, made once and kept in the presenter PC's own settings. A Slides agent has no
doors to bind, so it always broadcasts.

### What Stagewatch hears

Stagewatch (and every Slides agent) listens on UDP 9928 on every interface, sharing the port (`SO_REUSEADDR`). What it takes:

- A datagram of at most `hello.read.bytes` (2048), JSON of `v` 1, with a kind of at most `hello.kind.chars` (16) and an id of at
  most `hello.id.max` (40) letters, digits, `-` and `_`; a presenter PC's id must also be a feed's id (`slides.id.min` to
  `slides.id.max`, 6 to 32). Anything else is dropped. Anything on the network can send anything: nothing else a hello carries is
  trusted.
- Of what it carries: its first `hello.caps` (32) caps of §2's shape (lowercase letters, digits, `.` and `-`, at most
  `hello.cap.chars`, 24), its first `hello.ports` (8) doors, its name and host without control characters and cut at 64, and its
  `inst` only when it has §2's shape.
- **Every kind** is heard, Stagewatch's own and the ones it does not know. At most `peers.max` (128) ids are held; a hello from a new
  id beyond that is dropped. An id unheard for `peer.forget.ms` (60 s) is forgotten.
- **One id, one peer.** Per id it keeps every address heard within `peer.stale.ms` (6 s), at most `peer.addresses` (4), each with
  the certificate its own last hello named: a new address past that takes the place of the one heard longest ago, never of the one
  dialled. The dialled address stays while it is still heard and moves to the newest only once it goes stale, so a node heard from
  its broadcast and from its copy here is one peer that never flips between the two.
- **Two nodes, one id.** An `inst` heard within 6 s coming back after a different one (A, B, A; for three copies A, B, C, A) marks the
  id in conflict; for a node that sends no `inst`, a second host within 6 s does. A restart (A, then only B) is not a conflict, nor
  is a run of restarts, and one `inst` from two addresses is one node. The mark clears `peer.conflict.clear.ms` (60 s) after the last
  return.
  - *Where it shows:* the Show network row, amber, "two machines say they are 3f9c2a1b7d0e: FOH-PC at 10.0.0.5 and FOH-PC2 at
    10.0.0.7" (a context that id posts, or a node link its key proves, still shows under it, the conflict's words first); the count
    of such ids on the network line; the producer's advice; and the AI's facts (amber).
  - *Where it does not:* that id's hello adds no `peers` row to Stagewatch's context (Show Bus §6 forbids the row), so
    `/api/context`, MCP's `get_context` and other apps reading the context do not list it; the Companion module reads no peers at all.
    Nothing refuses that id's node link or context post, and Stagewatch dials no link by hello.
- **This Stagewatch's own id, said by another machine** (another `inst`, or none): never a peer. The Show network line adds
  "another machine says it is this Stagewatch: FOH-PC2 at 10.0.0.7 — give one of them a new id", `bus:hello` is amber with it (so MCP's
  `get_context` and other apps carry it), and the advice warns. Its own echo (its own `inst`) is dropped. A presenter PC's window says
  the same of another presenter PC saying its id.
- **Who reads it.** Producer → Show network lists every kind but presenter PCs, joined with the contexts and node links of the same
  id (§4). A Slides agent reads the `main` hellos to choose `https` (§3).

### Find: the Patterns link's address

Settings → Integrations → *Patterns video* → **Find** listens for `find.listen.ms` (3 s) on UDP `find.hello.port` (9928) and,
beside it, on Patterns' own beacon, UDP `find.beacon.port` (9700), and lists each Patterns machine with its wire port: from a
hello `{"t":"patterns","v":1,…,"ports":{"wire":9697}}` or a beacon `{"patterns":1,…,"wire":9697}`. Hearing none, it names both
ports for the firewall. It reads a hello's line wire from `ports.wire`, else from the top-level `wire` the older senders give, and
keeps a hello's id, `ports.https` and `tls` only where each has §2's shape. Find hears PlayMap's hello (`t` `playmap`, its line
wire 9941) the same way, for the playout links; the *Patterns video* box is link `patterns`, a Patterns desk's, so it asks Find for
Patterns machines only, and no other node takes one of the `find.max` (32) places it lists. A datagram that is not UTF-8 text names
no machine. 9700 stays in Find until Patterns' own hello on 9928 has been heard on a real network (Patterns' OPEN row L47, a walk);
`WHATS_NEXT.md` names the change that drops it then. Stagewatch never sends on 9700. (The older multi-room beacon on UDP 9927 is
separate and carries each room's timer summary: `FEATURES.md`.)

## 3. Slide feed: presenter PC → Stagewatch

`POST http(s)://<stagewatch>:9924/api/slides/feed` with `X-Stagewatch-Pin: <pin>` (the PIN, or a control key), body JSON of at most
`http.body.bytes` (64 KB):

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
- The **script** (all speaker notes, for speech pace) is identified by `script` (the first `slides.script.hex` (16) hex digits of
  its SHA-256) and sent once, only when asked: the reply is `{"ok":true,"needScript":true,"proto":1}`; then
  `POST /api/slides/script?hash=<hash>` with the UTF-8 text (at most `slides.script.bytes`, 2 MB). Stagewatch keeps it only if the
  hash matches. The feed's `proto` 1 is the feed's own version, not Show Bus's.
- Replies: `200 {"ok":true,"proto":1}` · `401` a wrong or missing PIN (five wrong PINs a minute lock that device out of the PIN and
  every key for the rest of the minute) · `403` not licensed (presenter PCs need Pro on the Stagewatch PC) · `400` not a feed.
- **Encrypted when it can be.** The agent uses `https` when the hello heard at the address typed for that Stagewatch names a
  certificate, pins that certificate at its first success, and from then on refuses another one (CERTIFICATE CHANGED, until the
  operator presses **Trust new certificate**); otherwise plain `http`, where the PIN crosses the network in clear (Show Bus §8.1).
- A presenter PC that stays silent for `slides.stale.ms` (4 s) counts as gone; *Automatic* then falls back to the local PowerPoint.

## 4. Reading and driving Stagewatch

- **The public view** is open by design (Show Bus §1 rule 4, §8): the frame on `/ws` and `GET /api/state`, `detail=1`'s public
  detail, the viewer, role and audience pages, `/api/holding/<id>`, `/tls`, and a window's page to whoever holds its address.
  Everything else asks for the PIN or a key: the operator frame (`/api/state?detail=1` with the PIN), `/api/context` (the PIN or
  any key), the screens list, a window's address, every command.
- **Frames.** `ws://<pc>:9924/ws` pushes every frame, latest wins (each with `v` and a rising `seq`), and a `{"type":"beat"}` at
  least every `ws.beat.ms` (2 s). When the show thread stops turning over for `show.stuck.ms` (3 s) it sends
  `{"type":"beat","live":false}` and no frame until it recovers: treat the last frame as stale.
- **Commands:** one grammar for everything (HTTP `/api/<verb>/…`, WebSocket `{"cmd":…,"args":[…]}`, OSC, the Companion TCP line
  protocol, triggers, MCP). `GET /api/help` returns it. Send an id (`X-Request-Id`, `"id"`, `@id`) and the answer is a receipt that
  says applied, refused or only accepted (Show Bus §3).
- **Keys.** A scoped key goes in `Authorization: Bearer swk_…`, never in an address but under `?pin=`, a named exemption (Show Bus
  §8.1): **read** for dashboards, **control** for show control, **mcp** for agents, **node** for another show app (read, stage
  messages, its own context and proposals). A node key speaks for one app (one node id): give each app, and each install, a key of
  its own. Replies carry `X-RateLimit-Limit/Remaining/Reset` (`key.requests.per.minute`, 600). An unknown or revoked key is refused
  (401) and never counted toward a lockout. Wrong PINs (`pin.wrong.per.minute`, 5) and wrong view passes — window addresses,
  viewing codes, crew links (`view.wrong.per.minute`, 5) — are counted apart, each locking a device out of its own kind for the rest
  of a minute (`REMOTE.md`, 2.19).
- **Context.** `GET /api/context` is the room in one JSON (Show Bus §6): its light and headline, its items (the timer, cues,
  links, windows…), recent events, the commands it accepts with their risk, panic, air and transport first, and the peers it
  hears. It keeps within §14: at most `context.bytes` (64 KiB) as sent, `context.items` (200) items, `context.links` (400) links
  and `context.commands` (256) commands; one over is trimmed in §6's order (oldest recent lines, grey links, then other links,
  commands that are not panic, air or transport from the end, grey items, then green) and says what it left out in a grey
  `context:trimmed` item. MCP's `get_context` is the same JSON. Another app posts its own to `POST /api/peers/context` (its node
  key, or a control key or the PIN while no key speaks for its id), at most `context.bytes` as sent, counted in UTF-8 bytes (a
  longer one is answered `413` with the size it declared, and stored nowhere, as any body over `http.body.bytes` is); one is kept
  `context.kept.ms` (30 s). As a reader of those, Stagewatch does not keep §14's numbers yet (it keeps a context's first
  `read.context.items` (64) items and `read.context.commands` (64) commands, cuts a verb at `read.context.verb.chars` (80)
  characters and reads no links: Show Bus §8.1).
- **Proposals.** Stagewatch keeps the show's approval queue (cap `approvals`, Show Bus §11): `POST /api/proposals`; another app
  collects what was approved for it with `GET /api/proposals?target=<its id>&state=approved` and its node key, and reports
  `POST /api/proposals/<id>/result`. REMOTE.md's 2.16 section has the shapes.
- **MCP:** `POST /mcp` (JSON-RPC; the PIN or an MCP key). By default an AI agent's `run_command` becomes a proposal the operator
  approves: the person stays in the loop.
- **Screens, windows and show apps.** A *screen* is any browser showing Stagewatch: `GET /api/screens` (PIN) lists them, and `screen
  go|identify|name|save|reload|disconnect` moves them. A *window* is a browser Stagewatch controls at an address of its own,
  `/w/<token>` (REMOTE.md 2.17, Show Bus §12). A show app on a node link is neither: no screen command reaches it, and naming one is
  refused.
- **Layouts:** `GET /api/layouts` and `/api/layouts/<name>` (anyone who may view) return designed screens as JSON (elements on a
  100×100 canvas with `{placeholder}` text); `POST /api/layouts/save|delete|ai` need control. A node with its own display can
  render Stagewatch layouts from the same frames.

### Show apps on /ws: node links (2.18)

A show app follows the timer on `/ws` as a **node link**: its own Show Bus id and kind in the address, its key in the header, never
in the address. It hears what Stagewatch made of it before any frame, and it never hears a screen command. REMOTE.md's 2.18
section has every message and refusal.

| App | `t` | Its node link |
|---|---|---|
| Patterns | `patterns` | `/ws?node=<its Show Bus id>&t=patterns&cid=<the same id>&page=/patterns` |
| PlayMap | `playmap` | `/ws?node=<its node id>&t=playmap&cid=<its cid today>&page=/playmap&detail=1` |
| AGMessage | `messenger` | `/ws?node=<its node id>&t=messenger&cid=<the same id>&page=%2Fagmessage` |

- Each sends its own **node key** as `Authorization: Bearer swk_…`. A control key, or the PIN when this Stagewatch asks for one,
  also proves it (verified `operator`) while no key speaks for that id. A read key, an MCP key, a PIN on a Stagewatch with no PIN
  set, or nothing makes it only a claim: it stays in Screens, flagged, never reached by "all".
- `cid` and `page` are ignored when `node` is there; a Stagewatch older than 2.18 ignores `node` and lists each install under its
  own `cid`, which it keeps only at `cid.min` to `cid.max` (6 to 32) letters, digits, `-` or `_` (so PlayMap keeps the `cid` it
  sends today, `playmap-<id>` for a shorter id).
- The first message is the verdict: `{"type":"auth","ok":true,"scope":"node","node":{"t":"patterns","id":"9f8e7d6c",
  "verified":"key"}}`. `verified` `none` comes with `words` saying why (a key that speaks for another app, an id that speaks with
  its own key, no key); the frames keep coming, and only the node routes (context, proposals) refuse it. A refused key hears
  `{"type":"auth","ok":false,"error":"unknown or revoked API key","retry":60}` and keeps the public frame: wait `retry` seconds
  before keyed calls, and don't redial in a loop, since no key reason closes the socket.
- A later change (the key revoked, the key moved to another app, the PIN changed) is sent again, latest wins.
- A node key binds to the first id it speaks for while that id is free (on a node link, a context, a proposal or a collect); the
  same key under another id, or another kind, is refused (403 on the HTTP routes; on a node link, a verdict of verified `none`)
  with `this key speaks for <kind> <id> — make a key for <kind> <id>`. The operator moves a key with Let it speak for… (Settings →
  Remote) or Let a key speak for this node… (Producer → Show network).
- Today's dials (`/ws?cid=patterns&page=/patterns`, `/ws?cid=<its cid>&page=/playmap&detail=1`,
  `/ws?cid=agmessage&page=%2Fagmessage`) still work. With a node key in the header such a socket already leaves Screens (a show app
  listed under its key, speaking for no node) and hears no screen command; with a control key, the PIN, a read key or no key it is
  still a browser screen to Stagewatch, so "all" reaches it until the app dials as a node.
- `?pin=` on a node link is refused (400): the key rides only in the header.
- **The speaker's message and its answer** (AGMessage today, Patterns from its Round 93):
  `POST /api/message/<text>/<urgency>/<flash>/ack` with the node key and an `X-Request-Id`. A receipt `applied` with
  `refs.messageId` names the speaker's signal; `applied` without it asked no answer (a licence with no crew cues puts the message
  up alone), so none will follow; `accepted` names nothing yet, and the signal is the next frame's new `messageId` beside the text
  sent. Frames carry `messageId`, `messageAck` and `messageAckBy`: a message's signal completes on its first answer, so
  `messageAck` goes `sent` → `done` and is never `seen` — read `seen` or `done` as the speaker's answer. A show app forwards its
  own page's answer as `POST /api/ack/<id>/seen/<its desk's name>`; Stagewatch adds the device's address to the name, cutting the
  name, never the address, to fit `ack.by.chars` (40). An answer that would change nothing is refused: at the door, 403
  `{"ok":false,"error":…}`, once the signal has left the frame's `signals` (or, with crew links required, from anything but the
  speaker's crew link or the operator: "This screen can't confirm cues for speaker…"); with a 409 receipt while it is listed but
  already answered.
  `signal.kept.ms` (30 s) after the answer, or after a newer message or a clear cancels it, the three fields go back to `0`, `""`,
  `""` while the message may stay on stage: keep `done` for the `messageId` you sent. A receipt's `effect` is free words; show or
  assert none of it.
- Stagewatch's `/ws` speaks no PeerLink yet (Show Bus §13): a node link proves a bearer key or the operator's credential, at level 0
  (§7.1). The PeerLink kit — Stagewatch's core, the vectors and the scripted peer — runs in the suite only, in 2.19.

## 5. The Patterns link (Stagewatch as a Patterns client)

Stagewatch reads the clip on air in Patterns over Patterns' own wire, as a playout source beside vMix. This section is the link as
2.19 builds it; it is rewritten when Stagewatch's playout links run on the PeerLink core, in a later release.

- **Connect:** TCP `patterns.wire.port` (9697) at the address in Settings → Integrations → *Patterns video* (Find, §2, fills it).
  Stagewatch sends `HELLO Stagewatch` and a Show Bus 1.1 probe (`@hi PING`), and a `PING` every `patterns.ping.ms` (2 s). A link
  silent for `patterns.drop.ms` (12 s) is dropped and redialled, `patterns.redial.first.ms` (250 ms) at first, doubling to
  `patterns.redial.max.ms` (5 s). It marks no link stale before it drops, and it never jitters its redial: Show Bus 1.3's
  `link.stale.ms` and `link.redial.first.ms` are not kept yet (§8.1).
- **Pairing: the token never crosses the network.** The pairing token is stored DPAPI-protected. A desk's `NONCE` is answered with
  `AUTH-PROOF` (HMAC-SHA256(token, nonce + "Stagewatch")) only when it is exactly 32 lowercase hex, once per connection — the token
  as Show Bus 13.6.8 keys it: dashes, spaces, tabs, CRs and LFs removed, the rest upper case. A token that still holds a character
  1.1 desks read apart (another kind of space, a line or paragraph separator, a byte order mark) is never proved: the link stays
  amber and says 13.6.8's words, "this token holds an invisible character — retype it". A desk that
  sends no NONCE within `patterns.nonce.wait.ms` (1.5 s) asks this connection for none: plain `AUTH <token>` then goes only to a
  Patterns on this PC (a loopback socket), never on a connection that heard a NONCE. Nothing the desk sends (STATE, receipts, clock
  samples, context) is taken before the link pairs (`OK paired`) or is open (a desk that asks for no proof: a Show Bus 1.1 desk, or
  one on this PC while this link holds no token), and no command is written to it before then. A Patterns older than Show Bus 1.1 on
  another PC therefore stays amber ("update Patterns") and is not followed.
- **State:** Patterns pushes `STATE <json>`, latest wins. Stagewatch reads `video{file,tag,position,length,playing,ended,loops}`,
  and from Patterns Round 86 `positionMs`, `lengthMs` (null = not known yet), `paused`, `rate` and `seq`. An unknown length or
  `loops` means no countdown; at a rate other than 1 the countdown is real time. Position counts on locally from a monotonic clock
  between frames; a playing clip with no frame for `patterns.clip.stale.ms` (3 s) is treated as stale and the VT countdown
  disappears rather than lie. A STATE longer than `patterns.line.bytes` (256 KB) is read in part (Show Bus 13.14): its clip when
  `video` lies within the first 256 KB, else no countdown, never the last clip kept; the link stays up, amber, with the STATE's size
  ("STATE 293 KB — over this link's 256 KB"), until a whole STATE comes. Any other line that long is counted and not read. A STATE
  that is not JSON, or whose names or strings are not whole UTF-8 text, is counted and not read, the link, its light and its last
  clip as they were; one whose `video` is of another shape (not an object or null) is amber with no countdown.
- **Show Bus 1.1 desks** get request ids (so a command's answer is a receipt), timed pings (the clock offset, CLOCKS APART past
  `clock.apart.ms`, 2 s) and `CONTEXT` every `patterns.context.every.ms` (5 s), shown on the Show network; older desks get the
  plain lines they know.
- **Commands:** a fixed set, nothing else:
  - `VIDEO END <n>` — Stagewatch `patterns end [seconds]` (default 10) and the *Last 10 s* button.
  - `VIDEO RESTART` — `patterns restart`.
  - Round 86 desks only (detected from the precise fields): `VIDEO PLAY`, `VIDEO PAUSE`, `VIDEO STOP` (the clip back to its top,
    held) and `VIDEO SEEK <s>` — `patterns play | pause | stop | seek <m:ss>`. An older desk is told nothing it doesn't know.

  No raw lines, and never a bare `STOP`, which on Patterns turns every output off. Nothing is queued while disconnected or unpaired.
- **Both sources on:** the VT countdown follows whichever is playing (Patterns first if both).

## 6. AGMessage (board ↔ research messaging)

From Stagewatch's side; AGMessage's own `docs/NODES.md` is its reference.

- **Heard** on UDP 9928 as `{"t":"messenger","v":1,…,"ports":{"http":3023,…},"caps":[…]}`: Stagewatch shows it on the Show network
  as AGMessage, and `ports.http` is where its pages are. AGMessage hears and says hellos on 9928 only; it has no part in Patterns'
  9700 beacon.
- **Follows the timer** on `/ws`, today as `/ws?cid=agmessage&page=%2Fagmessage` with its **node** key in `Authorization: Bearer`,
  never in the address: with that key it already leaves Screens and hears no screen command (§4, *Today's dials*). It moves to its
  node link (§4's table) in a release of its own. A key in force also opens a viewing code.
- **Puts a message on the stage** with `POST /api/message/<text>/<urgency>/off/ack` and its node key (a node key may send stage
  messages), with an `X-Request-Id`: the receipt names the message (`refs.messageId`), and the frames carry the speaker's
  acknowledgement of that message.
- **Its context** goes to `POST /api/peers/context` on change and every 5 s while linked (kept 30 s here); it reads Stagewatch's
  `/api/context` on connect and every 5 s.
- **Proposals:** it proposes a message for the speaker's screen to Stagewatch's approval queue (`POST /api/proposals`, target
  `main`), and collects the approved lines aimed at it — its one verb is `notice <text>` — runs them and reports each result.

## 7. PlayMap

From Stagewatch's side; PlayMap's own `docs/SHOWBUS-NODE.md` is its reference.

- **Heard** on UDP 9928 as kind `playmap` — its `ports` name its doors: `http`, `https` and `ws`, its line wire `wire` (9941),
  and `sync`, its clock exchange — and shown on the Show network as PlayMap (Show Bus §14's kind row).
- **Follows the timer** as a node link (§4's table), with `detail=1`, its node key in the header; posts its context to
  `POST /api/peers/context` and collects approved proposals aimed at its id, as AGMessage does.
- **No link of Stagewatch's own.** Stagewatch dials no PlayMap: its one line connector is the Patterns link (§5), and Show Bus §8.1
  says so. That link can be pointed at a PlayMap node's line wire (its host and 9941), which answers in Patterns' grammar; Stagewatch
  then shows and drives it as a Patterns link in every word (PlayMap's `docs/SHOWBUS-NODE.md` §2).

## 8. Joining

- **The contract is [`SHOWBUS.md`](SHOWBUS.md)**, Show Bus 1.3: §2 the hello, §6 the context, §7 keys and the node link, §8 each
  node's row (§8.1 is Stagewatch's, with what it does not keep yet), §11 the approval queue, §12 windows, §13 PeerLink and §14 the
  numbers every node checks itself against. A new app takes a kind (a §14 `kind` row, or a word of its own), says its hello by §2,
  asks the operator for a node key of its own, dials its node link (§4), and posts its context by §6.
- Mutating anything needs the PIN or a key whose scope allows it; reading the public view doesn't.
- Every message carries a version (`v` / `proto`); an unknown `v` is ignored, not guessed, and `proto` never decides a safety
  question.
- Fail safe: no fresh state means "stale", never "fine".
- Untrusted input is validated and clamped at the edge (ids, sizes, paths, numbers).

### Stagewatch's numbers

Each number the page names, as Stagewatch's code has it. A name that is a §14 row has §14's value: Stagewatch keeps that row. The
`find.`, `slides.`, `cid.`, `http.`, `ws.`, `show.`, `pin.`, `key.`, `read.`, `ack.`, `signal.` and `patterns.` rows, and
`hello.failed.beats`, are Stagewatch's own.

| number | value | what it is |
|---|---|---|
| `hello.port` | 9928 | the UDP port a hello goes to and is heard on |
| `hello.every.ms` | 2000 | how often a hello goes out |
| `proto` | 3 | the Show Bus revision whose shapes a hello says Stagewatch speaks |
| `hello.bytes` | 1200 | the most bytes a hello is as sent |
| `hello.read.bytes` | 2048 | the largest datagram read as a hello |
| `hello.caps` | 32 | the caps a heard hello keeps |
| `hello.cap.chars` | 24 | the longest cap word |
| `hello.ports` | 8 | the doors a heard hello keeps |
| `hello.name.chars` | 64 | a name's or a host's characters, sent and kept |
| `hello.kind.chars` | 16 | the longest kind a heard hello may carry |
| `hello.id.min` | 6 | the fewest characters of an id a hello may say |
| `hello.id.max` | 40 | the longest id a heard hello may carry |
| `hello.inst.chars` | 8 | an `inst`'s lowercase hex digits |
| `peers.max` | 128 | the most ids held |
| `peer.addresses` | 4 | the most addresses kept for one id |
| `peer.stale.ms` | 6000 | an address, or an `inst`, unheard this long is stale |
| `peer.forget.ms` | 60000 | an id unheard this long is forgotten |
| `peer.conflict.clear.ms` | 60000 | a conflict mark clears this long after the last return |
| `hello.failed.beats` | 3 | beats in a row with every send failed before `bus:hello` is amber |
| `find.hello.port` | 9928 | Find listens here for Patterns' and PlayMap's hellos |
| `find.beacon.port` | 9700 | and here for Patterns' own beacon |
| `find.listen.ms` | 3000 | how long Find listens |
| `find.max` | 32 | the most machines one Find lists, of the kinds it asks for |
| `slides.id.min` | 6 | the fewest characters of a feed's `agent` id, and so of a presenter PC's hello id |
| `slides.id.max` | 32 | the most |
| `slides.stale.ms` | 4000 | a presenter PC silent this long counts as gone |
| `slides.script.bytes` | 2097152 | the most bytes of a deck's script |
| `slides.script.hex` | 16 | the hex digits of a script's id |
| `cid.min` | 6 | the fewest characters of a `cid` Screens keeps |
| `cid.max` | 32 | the most |
| `http.body.bytes` | 65536 | the most bytes of a request's body: a feed, a context, a proposal; a longer one is answered `413` with its size |
| `http.drain.bytes` | 1048576 | the most of a longer body read and dropped first, so its sender reads the `413`; one declared longer is answered at once |
| `http.drain.ms` | 10000 | how long that reading lasts at most; the `413` follows either way |
| `ws.beat.ms` | 2000 | the most time between two messages on `/ws` |
| `show.stuck.ms` | 3000 | a show thread silent this long is stuck: `{"type":"beat","live":false}` |
| `pin.wrong.per.minute` | 5 | wrong PINs that lock a device out of the PIN and keys for the rest of its minute |
| `view.wrong.per.minute` | 5 | wrong view passes that lock a device out of view passes for the rest of its minute |
| `key.requests.per.minute` | 600 | requests a key may make in a minute |
| `context.bytes` | 65536 | the most UTF-8 bytes of Stagewatch's own context as sent, and of one posted to it |
| `context.items` | 200 | the most items in Stagewatch's own context |
| `context.links` | 400 | the most links in Stagewatch's own context |
| `context.commands` | 256 | the most commands in Stagewatch's own context |
| `context.kept.ms` | 30000 | how long a context posted here is kept |
| `ack.by.chars` | 40 | the most characters of who answered a cue: a name, or a name and the device's address |
| `signal.kept.ms` | 30000 | an answered or cancelled cue stays on the board, and in the frame, this long |
| `read.context.items` | 64 | the items Stagewatch keeps of a context posted to it |
| `read.context.commands` | 64 | the commands it keeps of one |
| `read.context.verb.chars` | 80 | a kept command's verb is cut to this |
| `cert.days` | 825 | its certificate's whole span, NotBefore to NotAfter |
| `cert.renew.days` | 30 | fewer days than this left, and a new certificate is made |
| `patterns.wire.port` | 9697 | the Patterns link's port unless another is set |
| `patterns.ping.ms` | 2000 | how often the Patterns link pings |
| `patterns.drop.ms` | 12000 | a Patterns link silent this long is dropped and redialled |
| `patterns.redial.first.ms` | 250 | its first wait before a redial |
| `patterns.redial.max.ms` | 5000 | its longest wait before a redial |
| `patterns.nonce.wait.ms` | 1500 | how long it waits for a NONCE before judging the desk asks for none |
| `patterns.clip.stale.ms` | 3000 | a playing clip with no frame this long shows no countdown |
| `patterns.line.bytes` | 262144 | the most of one line from the desk kept: a longer STATE is read in part, any other longer line counted and not read |
| `patterns.context.every.ms` | 5000 | how often it asks a Show Bus 1.1 desk for its `CONTEXT` |
| `clock.apart.ms` | 2000 | a Patterns desk's clock further off than this shows CLOCKS APART |
