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

### Find: a playout link's address

Settings → Integrations → *Playout links* → **Find** listens for `find.listen.ms` (3 s) on UDP `find.hello.port` (9928) and,
beside it, on Patterns' own beacon, UDP `find.beacon.port` (9700), and lists each Patterns desk and PlayMap node with its wire
port: from a hello `{"t":"patterns","v":1,…,"ports":{"wire":9697}}` or `{"t":"playmap",…,"ports":{"wire":9941}}`, or a beacon
`{"patterns":1,…,"wire":9697}`. Hearing none, it names both ports for the firewall. It reads a hello's line wire from `ports.wire`,
else from the top-level `wire` the older senders give, and keeps a hello's id, `ports.https` and `tls` only where each has §2's
shape. It asks for those two kinds only, so no other node takes one of the `find.max` (32) places it lists; the one a person picks
becomes a new link of its kind, its address, port and node id filled in, and one a link already reaches — its node, or its address
and port — is that link's: it is said so in the list, and picking it selects that link, never adds another beside it (one node has
one link). A datagram that is not UTF-8 text names
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

## 5. Playout links (Stagewatch as a Patterns and PlayMap client)

Stagewatch reads the clip on air in Patterns desks and PlayMap nodes over their own line wires, as playout sources beside vMix, and
drives them. Each link Settings → Integrations names — at most `playout.links` (8), each by a name such as `stage-left`; the 2.17
*Patterns video* box became link `patterns` — runs on its own on Stagewatch's PeerLink core (Show Bus §13) at level 0: it keeps §13's
rules a connector keeps on its own, and offers no LINK yet. A link runs while it is switched on and the licence (Production) runs
playout links; its transport (`auto`, `tcp` or `wss`) says how it reaches its peer, and a PlayMap node is reached over TLS where
it can be (*TLS to a PlayMap node*, below).

- **Connect:** TCP at the link's address and port — `patterns.wire.port` (9697) unless another is set; a PlayMap node's line wire is
  9941 (Find, §2, fills each in). Stagewatch sends `HELLO Stagewatch` and a Show Bus 1.1 probe
  (`@hi PING`); once paired, a `PING` every `patterns.ping.ms` (2 s), and only a pong that answers one of its last pings is a clock
  sample. A link heard within `link.stale.ms` (6 s) is live; silent past it, it is stale — amber, and no countdown — and silent past
  `patterns.drop.ms` (12 s) it is dropped and redialled. A redial waits `patterns.redial.first.ms` (500 ms) at first, doubling to
  `patterns.redial.max.ms` (5 s), each wait × (0.8 + 0.4 j), j from a generator seeded once from the system's random generator; the
  count starts over only once a link that paired has stayed up 10 s, so a peer that answers and closes is backed off. A desk at its
  connection ceiling (`ERR busy …`) is shown in its own words and backed off the same way; nothing is sent to it. The link's checks
  run every 250 ms on a timer of their own; a pause of this PC longer than `link.selfgap.ms` (2 s) calls the link down for none of
  it, and puts one line in the show log (at most one a minute, naming the pauses it folds in). Each connection writes its lines in
  order on a lane of its own, so neither those checks nor the desk asking for a command ever waits on the socket; a write that has
  not finished within `link.write.deadline.max.ms` (10 s) ends the connection, and the link says so.
- **TLS to a PlayMap node:** where the node's hello (§2) names its `https` door beside its certificate (`tls`), a link set to `auto`
  dials `wss://<host>:<https>/ws` asking for `playmap.lines.v1` — the same lines, one text message each — through no proxy; `wss`
  dials TLS only, `tcp` the line wire only. The hello it reads is the node's by the id Settings set (`node`), else a PlayMap node
  heard at the link's host — at an address its hello came from, by its own host or machine name, or at an address the host name
  resolves to (resolved off the link's threads, again every 30 s) — heard within 6 s; a hello only names a door to try (Show Bus
  13.4.2), never a pin. A hello under §2's conflict mark — two running machines say the id — holds the link while the mark stands:
  nothing new is dialled, amber, "two machines say they are …" (13.4.4); a link already up keeps running. The certificate is judged
  by its fingerprint alone (13.8.1: SHA-256 of its DER), never a name, chain or authority, in the handshake; a TLS connection whose
  certificate the link did not judge is ended with nothing sent. At level 0 nothing proves the node, so the one it pairs over is
  pinned `first` for the id its STATE gives — kept to compare, not trust (13.8.2). Pinned `first` or by a person, the link is never
  dialled plain (13.4.2: the pin alone decides): it dials the door a fresh hello names, else the door its certificate last answered
  at, kept beside the pin (`playout-links.json`), so a lost, late or forged hello never sends it to the line wire; with neither it
  dials nothing, amber, "… is pinned, so it is never dialled plain, and no hello names its TLS door now, and none is kept beside its
  pin — nothing sent". A node whose STATE names another id than the one Settings set is amber with both ids named, sent nothing, read
  for nothing, and its certificate is pinned nothing (13.4.3). Pins are kept per node id once Settings set one: a link set to
  another node starts without the old node's pin, and a link new under its name — or one whose own entry keeps none — finds its
  node's. With no node set, a pin is the one kept at the address the link dials: moved to another address it starts with none (the
  node there may be another), and a link renamed at the same address finds it. A person trusts a certificate on the desk only — the
  Show network row of its node has a button and a right-click entry, *Trust …'s certificate 03e9 b152 … (compared on its screen)*,
  and the status line's right-click lists every link's waiting certificate, a node with no row among them — one press, bound to the
  certificate it showed: should another be pinned `first` in its place before the press, the press trusts nothing. From then on a
  changed certificate is refused. A pinned node that shows another certificate is CERTIFICATE CHANGED, red: the handshake ends there
  — no HTTP request, line or proof goes to it — until a person compares the new one on the node's own screen and types at least 8
  hex of it (the short form, spaces and either case taken) into the entry the row's button or a right-click opens, which is never
  filled in; the new pin is handed to the keeper in that step, before the link dials again — held at once, written on the keeper's
  own lane, a write that fails said once in the show log — and the show log has a row for each try, trusted or not. `playout <link>
  trust <hex>` is in the grammar so that every door refuses it in words — OSC, Companion, HTTP, a WebSocket and MCP, from this PC or
  with a credential — and the desk's and the rig's executors refuse it too: it is High risk, never among the context's commands,
  never a step of a plan, and refused (400) when another app proposes it to this desk, so the approval desk never shows it. A `429`
  at the door is a lockout: red, the token held (a minute, or the `Retry-After` when longer), nothing sent. A wrong proof is held
  like any refused proof: PlayMap answers `ERR … wrong token` (or `… — closed` and then closes `4401`), and a close `4401` alone is
  read the same way — red, held once, one proof. An upgrade that does not take `playmap.lines.v1` is no line channel, and nothing is
  sent on it. A paired link on the line wire to a node off this PC moves to TLS when its hello names the door, between commands,
  never with one waiting for its answer. Plain `AUTH` never goes on the TLS line channel (PlayMap takes proofs only there). A clock
  measured over TLS may time a command, as one on this PC may (13.11.6).
- **Kind:** the peer's kind is what its STATE says — a whole STATE with no `t` is a Patterns desk, `"t":"playmap"` a PlayMap node —
  and Settings may set it. A peer that answers as another kind than the one set is amber with both kinds named, and sent nothing; one
  that names a kind Stagewatch does not speak to is sent nothing either, nor is one whose STATE names another node id than the one
  Settings set (`node`), both ids named (Show Bus 13.4.3). None of them feeds any reader but its own light: no clip for the VT, the
  frame or a clip's end, no verbs among the context's commands, no playlists in the AI's facts and no context on the Show network
  or for a proposal's risk, until its kind (or node) is set again. Until a STATE is heard, `go` and `cue` are refused ("its kind
  is not known yet — no STATE heard"); the clip's transport needs no kind.
- **Pairing: the token never crosses the network.** The pairing token is stored DPAPI-protected. A peer's `NONCE` is answered with
  `AUTH-PROOF` (HMAC-SHA256(token, nonce + "Stagewatch")) only when it is exactly 32 lowercase hex, once per connection — the token
  as Show Bus 13.6.8 keys it: dashes, spaces, tabs, CRs and LFs removed, the rest upper case. A token that still holds a character
  1.1 desks read apart (another kind of space, a line or paragraph separator, a byte order mark) is never proved: the link stays
  amber and says 13.6.8's words, "this token holds an invisible character — retype it". A refused proof (or token) is red, and the
  token is held for a minute (Show Bus 13.15.3): the connection ends and no proof goes again until the hold ends or the token
  changes, so a wrong token never walks the peer's lockout. The hold is kept with its end in `playout-links.json` in Stagewatch's
  data folder, by the link's name: the link holds it in the step that refused the proof, and nothing is proved or dialled before it
  ends, while a lane of its own writes the file — never the show thread, never under the link's lock — so restarting Stagewatch,
  turning the link off and on or changing its address keeps it. It is kept for the token it held (beside a tag of the token as
  Settings stores it, protected, never the token or a hash of it), so a token changed in Settings is proved at once; a hold read back
  ends within a minute of the start, whatever the file or a wall clock stepped back says, and a word the file holds outside Show Bus
  13.16.3's set holds as `key`. The file holds no token, proof or nonce. Words the peer sends back are shown with the token taken out,
  so a service that echoes a plain `AUTH` line puts no token on the status line or in the show log. A peer that sends no NONCE within
  `patterns.nonce.wait.ms` (1.5 s) asks this connection for none: plain `AUTH <token>` then goes only to a peer on this PC (a
  loopback socket), never on a connection that heard a NONCE. Nothing the peer sends (STATE, receipts, clock samples, context) is
  taken before the link pairs (`OK paired`) or is open (a peer that asks for no proof: a Show Bus 1.1 desk, or one on this PC while
  this link holds no token), and no command is written to it before then. A Patterns older than Show Bus 1.1 on another PC therefore
  stays amber ("update Patterns") and is not followed.
- **State:** the peer pushes `STATE <json>`, latest wins. Stagewatch reads `video{file,tag,position,length,playing,ended,loops}`,
  and from Patterns Round 86 (and from PlayMap) `positionMs`, `lengthMs` (null = not known yet), `paused`, `rate` and `seq`, and
  Patterns' `role` and `key` and, once Patterns publishes them, `pausable` and `stoppable`; and `t`, `id`, `seq`, `at`, PlayMap's
  `playlists` (each with its item on air and its next), `sync` and `project.index`, and Patterns' `cuestack`. An unknown length or
  `loops` means no countdown; at a rate other than 1 the countdown is real time. Position counts on locally from a monotonic clock
  between frames; a playing clip with no frame for `patterns.clip.stale.ms` (3 s) is treated as stale and the VT countdown
  disappears rather than lie. A STATE longer than `patterns.line.bytes` (256 KB) is read in part (Show Bus 13.14): its clip when
  `video` lies within the first 256 KB, else no countdown, never the last clip kept; the link stays up, amber, with the STATE's size
  ("STATE 293 KB — over this link's 256 KB"), until a whole STATE comes. Any other line that long is counted and not read. A STATE
  that is not JSON, or whose names or strings are not whole UTF-8 text, is counted and not read, the link, its light and its last
  clip as they were; one whose `video` is of another shape (not an object or null) is amber with no countdown.
- **Show Bus 1.1 peers** get request ids `s<n>-<inst>` (unique for the process, the hello's `inst` at the end, so a command's
  answer is matched to it), timed pings (the clock offset, CLOCKS APART once the offset less its error is past `clock.apart.ms`,
  2 s), and once paired `STATUS` and `CONTEXT` at once, then `CONTEXT` every `patterns.context.every.ms` (5 s), shown on the Show
  network (each link's, one per node id, the first link in Settings' order that gives one; a PlayMap node's only when it names the
  id its STATE gives); a PlayMap node is also asked `WHOAMI` every 5 s once its kind is heard, and one that lets
  this PC only read is amber — "… is armed and lets this PC only read — its desk can SHOW SET REMOTE-VIEW-ONLY OFF" — while its
  commands still go, its own `ERR` the refusal. What a running-order cue may name is asked once the kind is heard — a PlayMap
  node's `INDEX` (its playlists and their items), a Patterns desk's `CUE LIST` — and again when its STATE's `project.index` or
  `cuestack.listRev` moves, at most once each 5 s and once a revision on a connection; one that refuses the verb is not asked
  again on that connection. At most 64 playlists of `playout.list.items` (256) items, or `playout.list.cues` (512) cues, are read;
  past that the list is cut, and what it does not hold is not known rather than missing. Older desks get the plain lines they know. The clock starts with no sample on each new
  connection (the link never learns the peer's `inst`, so it may be another peer, or the same one restarted), and a step of this
  PC's own wall clock empties it (Show Bus 13.11.7).
- **Its light** in Settings is the link's own: green once paired and live, amber while stale or pairing or when the peer is told
  what to fix, red when refused or held, and never green while the link is down or dialling. The PeerLink core's record of the link
  (Show Bus 13.16.3) is amber at this level — `legacy`: the peer cannot prove itself to this link yet, so anything at its address
  could answer — and `plain` off this PC; the context's `link:<name>` item (kind `playout`, labelled with its peer: "PlayMap Stage
  left", "Patterns"), and so the Show network, the AI's facts and Companion that read that context, takes that record's light, its
  flags in words beside the link's own words; `clock:<name>` (kind `health`) is amber while CLOCKS APART.
- **Commands:** a fixed set, by the peer's kind, nothing else:
  - the clip on air, to either: `VIDEO END <n>` — `playout [<link>] end [seconds]` (default 10), `patterns end`, the *Last 10 s*
    button (on the link feeding the VT); `VIDEO RESTART`; and, Round 86 desks and PlayMap only (detected from the precise fields),
    `VIDEO PLAY`, `VIDEO PAUSE`, `VIDEO STOP` (the clip back to its top, held) and `VIDEO SEEK <s>`. A Patterns desk whose context
    lists the verb with `ON <key>` is sent the clip's own key with it (`VIDEO PAUSE ON <key>`), when the key reads back bare;
  - to a PlayMap node: `PLAYLIST GO <playlist> <item>`, `PLAYLIST GO <playlist> EXPECT <its next item>` (`playout <link> go
    <playlist>`), either with `AT <unix ms>` last when it goes at an instant on the node's own clock, and `PLAYLIST CUE <playlist>
    <item>`, each word quoted by PlayMap's line rule where it needs it;
  - to a Patterns desk: `CUE GO <cue id>` and `CUE STANDBY <cue id>`, the id bare, never the two together — its own standby fence
    decides a GO, and a standby moves that desk's own standby.

  `playout [<link>] …` reaches the link it names, else the only link switched on, else — for the clip's transport — the link
  feeding the VT; `go` and `cue` never guess between two. `patterns …` reaches link `patterns`, else the only link. No raw lines, and
  never a bare `STOP`, which on Patterns turns every output off. Nothing is queued while disconnected or unpaired.
- **Refused here, from the peer's own facts** (nothing sent): Patterns' `pause` or `stop` of a playlist item or a stinger's clip, in
  Patterns' own words ("'opener.mp4' is a playlist item — its own clock keeps running, so it is not paused (end or seek move it)"),
  until it publishes `video.pausable` / `video.stoppable`, which then decide — PlayMap is never refused by role; a GO or CUE on a
  playlist of a sync group whose node's STATE says it is the follower ("… follows sync group main (leader …) — GO its leader"); a
  `PLAYLIST CUE`, which puts the item's first frame on air at once, unless the playlist is seen idle or ended in a STATE at most
  `playout.cue.idle.ms` (3 s) old ("not cued — Show is playing on PlayMap Stage left; GO will play it"); a `go <playlist>` whose
  STATE names no next item; a Patterns cue that is not one bare id.
- **A GO at one instant** (`playout <link> go … together`, and the running order's cues marked together): the instant is the
  operator's lead (`playout.lead.ms`, 300 unless Settings say otherwise) from now on this PC's clock. A PlayMap node whose clock is
  fit for it (Show Bus 13.11.4–6: its believed sample at most `clock.fresh.ms` (10 s) old, its error within the error a round trip
  as long as the lead gives) and measured over TLS or on a link to this PC gets `AT` = that instant plus the link's offset, sent at
  once. Any other GO of the same start is held on this PC and sent untimed half its round trip before the instant — or at the
  instant itself, by this PC's clock alone, when its clock was measured over a plain link off this PC, which may be shown and
  schedules nothing (a Patterns desk's 9697 until its Round 97); a lone GO (the one whose link is up) is never held: at its instant
  when fit, else at once; when any GO of a start whose link is up has no clock sample at all, nothing is together and every GO
  goes at once; and a GO whose link is not up and paired is sent nothing — its own row says why — and takes no timing from the
  others. Each says what it did, in its row and its receipt, as an attempt until the node answers: a GO with `AT` is "sent with AT
  for 20:00:01.250 (± 3 ms)" as it goes, and "scheduled for 20:00:01.250 (± 3 ms)" — a row, and the receipt's words — only once
  the node's own answer says it took it (applied or accepted); one the node refuses keeps its words alone. A held GO is sent only
  on the link and connection it was held for, and only while the licence allows: a drop, a link Settings stopped or replaced, or
  a lapsed licence while it is held leaves it not sent, with a row. The STATE that shows a timed GO's playlist playing says when
  it started (its `at` less the playlist's `positionMs`): later than the `AT` by more than half the round trip and
  `playout.late.margin.ms` (20) is a row, "PlayMap Stage left: Main show started late by 140 ms (AT arrived after its instant)".
  The picture reaching the outputs is not observed. The believed clock sample is the smallest round trip among the last
  `clock.window` (8), one a ping every `patterns.ping.ms` (2 s), as Show Bus 13.11.3 has it — so it can be up to about 16 s old
  while fresher samples are kept, and a timed GO then falls back (held, or at once) and says the clock is not fit. The design's
  "the smallest round trip among the samples at most 10 s old" is not what 13.11.3 says, and the contract is followed.
- **The running order's cues** (at most `playout.cues.per.item` (4) an item; *Edit running order → Playout* picks them from what each
  peer lists, its walk on Windows the maintainer's):
  each names its link and a PlayMap playlist's item or a Patterns cue, by id. A load sends nothing unless a cue is set to stand by
  (off by default: a PlayMap standby shows the item's first frame on air, a Patterns one moves that desk's own standby); then, once
  the show thread's turn that loaded it is over, and only when the item did not start in that turn, PlayMap's `PLAYLIST CUE` (on a
  playlist seen idle, as above) or Patterns' `CUE STANDBY`. When the item first starts each cue GOes — Patterns by `CUE GO` alone —
  one GO per sync group (a follower's cue is left to its leader's GO among the item's cues whose plans pass, and its row says
  whether that GO was sent; with no such leader it is refused naming its leader), those marked together at one instant as above.
  A cue whose link this PC lacks, is switched off or not running, whose peer answers as another kind or node than its settings
  name, or that names what its peer does not list, is amber advice before its item — on the desk whether or not its assistant is
  on, ahead of the assistant's own — and is skipped at its start with a row; a cue the plan refuses is not sent, with a row. Every
  row is filed against the item and is on the desk's status line too ("Item 4: …"). A peer that answers as another node than the
  one its link is set to feeds nothing of its list (13.4.3): the list read before is dropped, and asked again once it answers as the
  node meant. The planner (`PlayoutDirector`) is the desk's and the rig's (`--cue`).
- **Outcomes** (Show Bus 13.12.3): an answer is applied, accepted (taken, not run yet) or refused with the peer's words. At most 32
  commands wait for answers and the next is not sent, nothing evicted; nothing is queued while the link is not up. A command
  written before the link dropped, or with no answer in 30 s, is unconfirmed — it may have run: look at the peer — never failed; one
  still on the connection's lane when it dropped, or after 30 s, is not sent — none of it reached the peer, and the lane passes over
  it, never writing it later. A desk older than Show Bus 1.1 echoes no id, so its command is unconfirmed once written. Each command
  carries a ticket: its first outcome and its final one; a stopped link hands every ticket still waiting an outcome.
- **Receipts** (Show Bus §3): a sender with a request id hears the peer's own answer within the same 3 s — applied, in Stagewatch's
  words where the peer gave none ("PlayMap Stage left took PLAYLIST GO pl1 it1"), refused in its words, accepted, unconfirmed or not
  sent — and with none by then accepted, "sent to PlayMap Stage left — waiting for its answer"; `refs.peerRequest` names the peer's
  request. On `/ws` and Companion TCP an accepted receipt is followed once, within 30 s of the send, by that command's final receipt
  with the same id and `"final":true` (on Companion TCP, the word `final` after the id): the peer's final answer — never an accepted
  that came after the first receipt went — and when it gives none in time, Stagewatch's own, accepted and "unconfirmed — PlayMap
  Stage left took it and gave no final answer in 30 s — it may have run; look at its state". HTTP answers once.
- **Seen in STATE:** after a PlayMap GO or CUE, or a clip's pause or play, that the peer took, its STATE is looked at for the
  operator's lead (300 ms) and `playout.seen.ms` (3 s) from that answer: one row, "seen: Main show plays it1 on PlayMap Stage left"
  or "not seen in PlayMap Stage left's STATE within 3.3 s: …". Only a STATE the link read after the command went counts, and when
  the last one before it already showed the effect, only one read after the peer's answer — a STATE from before the command proves
  nothing of it, while a peer that pushes its new STATE ahead of its answer is still seen. Seen means seen in its STATE, never on its
  outputs. At most `playout.seen.max` (32) effects are looked for at once on one link; a command past that still goes, and the
  status line and its sender say its effect is not looked for.
- **The show log** (Show Bus 13.12.10, 13.16.7, 13.19.4), at the link's own time: one row when the outcome of a command that reached
  the peer first settles (applied, accepted, refused or unconfirmed), filed against the running-order item it was asked for, and
  one more when an unconfirmed one is answered after all; any other move (accepted, then applied) and a command never written have
  the status line alone. One row per change of the link's state, its start among them — the peer and its address, from, to, its
  words and 13.15.1's attempt. A change already said within `log.repeat.summary.ms` (60 s) — a redial going round, a link flapping
  live and stale, its coming live again — is folded into one summary row that often, naming the last change it folds, never a row
  per change. The rows reach listeners in the order the link made them, and those that settle as Stagewatch closes reach the
  journal's file before it closes. No row carries the token, a proof or a nonce.
- **The VT and clips' ends:** the VT countdown follows, among the links that may feed it (*follow VT*) in Settings' order, the first
  whose clip runs, else vMix's running clip, else the first with a clip; the frame's `playout` row names its `link` and `node`. Each
  link's clip end is one row (and the "clip ends" trigger); it moves the running order on a video item only when that link fed the VT
  at that moment and its *next on end* is on, and at most once an item execution, however many links end. A link stale or dropped a
  while, or a clip gone stale, changes nothing of what was said: the same ended clip seen again after the gap is not a second end.
- **An AI's line** to a playout link is always a proposal a person approves (Show Bus §11); the approved line runs through the link,
  and the proposal settles on that command's final receipt: `done` only once the peer said it applied it; `failed` in the peer's
  words when it refused or nothing was sent; one the peer only took (Patterns' Confirm among them) or never answered within 30 s is
  never `done` — §11 has no state between, so it is `failed` with words that start "unconfirmed —" and say it may have run.
- **A context pulled over a link** counts, as the operator's own link to an address the operator set, for its node's row on the
  Show network (`verified` `key`, its words naming the link: "its context comes over this Stagewatch's playout link stage-left") and
  for a proposal's risk, while no key speaks for that id (Show Bus §6, §11: a table read by the desk over its own link). At level 0
  the peer does not prove itself, so this rests on the address alone; Stagewatch takes it only from a peer of the kind Settings
  names, and from a PlayMap node only for the id its STATE gives. A LINK verdict replaces it (a later release).

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
- **A playout link to it** (§5) at level 0, paired by proof: over TLS (`playmap.lines.v1` on its `https` door) where its hello
  names that door, its certificate pinned `first` and trusted by a person on the desk, else its line wire (9941) until its
  certificate is pinned; never plain once pinned, first or by a person, and CERTIFICATE CHANGED once a person pinned it until a
  person types 8 hex of a new one. Heard to be PlayMap by its STATE's
  `t`, it is driven in PlayMap's own verbs (`PLAYLIST GO`, `PLAYLIST CUE`, the clip's `VIDEO` transport), asked `WHOAMI`, and its
  playlists, sync group and id read from its STATE; its context joins the Show network by its node id.

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
`find.`, `slides.`, `cid.`, `http.`, `ws.`, `show.`, `pin.`, `key.`, `read.`, `ack.`, `signal.`, `patterns.` and `playout.` rows,
and `hello.failed.beats`, are Stagewatch's own.

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
| `playout.links` | 8 | the most playout links Stagewatch keeps |
| `playout.cue.idle.ms` | 3000 | a PlayMap playlist is cued only when a STATE this fresh shows it idle or ended |
| `playout.seen.ms` | 3000 | how long past the lead a command's effect is looked for in its peer's STATE |
| `playout.seen.max` | 32 | the most commands whose effect is looked for at once on one link (`link.inflight.max`); one past it still goes, its effect not looked for |
| `playout.lead.ms` | 300 | a GO at one instant is set this far ahead unless Settings say otherwise (100 to 2000) |
| `playout.late.margin.ms` | 20 | past half its round trip, how much later than its `AT` a timed GO may start before a row says it started late |
| `playout.cues.per.item` | 4 | the most playout cues on one running-order item |
| `playout.list.items` | 256 | the most items of one playlist read from a PlayMap node's `INDEX` |
| `playout.list.cues` | 512 | the most cues read from a Patterns desk's `CUE LIST` |
| `patterns.wire.port` | 9697 | a Patterns desk's port unless another is set |
| `patterns.ping.ms` | 2000 | how often a playout link pings, once paired |
| `link.stale.ms` | 6000 | a playout link silent longer than this is stale: amber, and no countdown |
| `patterns.drop.ms` | 12000 | a playout link silent this long is dropped and redialled |
| `link.selfgap.ms` | 2000 | a pause of this PC longer than this calls a playout link down for none of it, and is logged |
| `patterns.redial.first.ms` | 500 | its first wait before a redial, before its jitter (× 0.8–1.2) |
| `patterns.redial.max.ms` | 5000 | its longest wait before a redial, before its jitter |
| `patterns.nonce.wait.ms` | 1500 | how long it waits for a NONCE before judging the peer asks for none |
| `patterns.clip.stale.ms` | 3000 | a playing clip with no frame this long shows no countdown |
| `patterns.line.bytes` | 262144 | the most of one line from the peer kept: a longer STATE is read in part, any other longer line counted and not read |
| `patterns.context.every.ms` | 5000 | how often it asks a Show Bus 1.1 peer for its `CONTEXT` |
| `clock.apart.ms` | 2000 | a playout peer's clock further off than this, less the sample's error, shows CLOCKS APART |
| `link.write.deadline.max.ms` | 10000 | a playout link's write not finished this long ends its connection |
| `log.repeat.summary.ms` | 60000 | a change of a playout link's state already said this recently goes to the show log in one summary row this often |
