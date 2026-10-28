# PeerLink — layer 2 of the Show Bus, one profile every app-to-app link follows (security first)

Area: peerlink (security angle). Design only: nothing in any repo was edited or built. Line numbers are from the trees as read on
2026-10-25; AGMessage's are from `AGMessage's 0.5.0 branch` (0.5.0) unless marked "main". Sibling designs are cited as
`design/<area>.md`. The test vectors in §13.18 were computed in the scratchpad (`scratchpad/peerlink-vec/vectors.py`, Python stdlib,
cross-checked in Node 22 with `check.cjs`); nothing else was run.

**Numbering.** spec-1-3 (decision 1, its "Who writes which words") owns the one master edit and has already used §13 for "The
numbers". This area hands its words to that edit as **§13 PeerLink**; the machine-read block becomes **§14 The numbers** with the
`peerlink.*` rows of §13.17 appended. If the master's owner prefers to keep §13 for the numbers, PeerLink is §15 and nothing else
changes.

---

## 0. What exists (evidence)

### 0.1 Eight link implementations, each doing layer 2 its own way

| # | link | carrier today | how it proves | what goes on the wire in clear | evidence |
|---|---|---|---|---|---|
| 1 | Stagewatch `PatternsLink` → Patterns 9697 / PlayMap 9941 | plain TCP | 1.1 `AUTH-PROOF` = HMAC(token, nonce + "Stagewatch"), one-sided | **plain `AUTH <token>` to any host** when no NONCE within 1.5 s (G08); every STATE/CONTEXT | `Stagewatch/src/Timer.Integrations/PatternsLink.cs:192-199,280-284,311,379-384` |
| 2 | Stagewatch `/ws` + HTTP (node side) | TLS **or** plain on one port; plain from the LAN unless Require HTTPS (off by default) | bearer `swk_…` in `Authorization` or `?pin=` | the bearer key, on plain | `Stagewatch/src/Timer.Remote/WebServer.cs:300-318,325-331,1150-1151`; `Stagewatch/src/Timer.Core/AppSettings.cs:259-261` |
| 3 | Patterns `StagewatchLinkService` → Stagewatch | `wss` only when the **hello** names a certificate, else `ws`/`http` | bearer node key | the bearer key whenever the hello is missing, forged, or lacks `tls` | `Patterns/src/Patterns.App/Services/StagewatchLinkService.cs:225-243,269-272,290` |
| 4 | Patterns wire 9697 + HTTP (node side) | plain TCP; HTTP sniffs TLS | 1.1 proof; HTTP `X-Patterns-Token` = the token itself | STATE greeting before NONCE, pushes to unpaired peers (G45); the token in HTTP headers on plain | `Patterns/src/Patterns.App/Services/ControlService.cs:356-363,501,763-774`; `Patterns/docs/OPEN.md` L55 |
| 5 | Patterns twin / node asks | plain TCP on every interface | twin: mutual HMAC over both nonces; node asks: nothing | the whole show; device passwords and the weather key (`SendSecrets` default **on**) | `Patterns/src/Patterns.App/Services/TwinService.cs:488,512`; `Patterns/src/Patterns.Core/Services/Twin.cs:160-196,380-398`; `Patterns/src/Patterns.Core/Model/ShowState.cs:2080,2117-2123`; `Patterns/src/Patterns.App/Services/NodesService.cs:185-227` |
| 6 | PlayMap `StagewatchLink` → Stagewatch | `wss` only when the hello names `tls` and `https`; a typed address always plain (G31) | bearer key | the bearer key on plain | `Mapper/src/PlayMap.Net/Links/StagewatchLink.cs:63-80,278-284`; `Mapper/src/PlayMap.Engine/Host/NetworkHost.cs:715-736` |
| 7 | PlayMap `PatternsLink`/`WireClient` → Patterns; PlayMap wire/`/ws` lines/HTTP (node side) | plain TCP; `/ws` `playmap.lines.v1` over TLS; HTTP sniffs TLS | 1.1 proof (byte-for-byte Patterns'); trusted addresses need no proof | STATE/CONTEXT on 9941 after HELLO (open read) | `Mapper/src/PlayMap.Net/Links/WireClient.cs:280-316`; `Mapper/src/PlayMap.Core/Bus/Pairing.cs:6-10,54-77,113-115`; `Mapper/docs/wire/companion.md:6-23` |
| 8 | AGMessage `StagewatchLink` → Stagewatch; `PeerLink` → Patterns / PlayMap | `wss` only when the URL is `https`, the hello names `tls` or a fingerprint is typed | bearer node key; **Patterns'/PlayMap's pairing token itself** in `X-Patterns-Token` / `X-PlayMap-Token` | the bearer key and **the operator's full-control pairing token** on plain | `AGMessage/app/lib/nodes.js:251-268,306,326`; `AGMessage/app/lib/peerlinks.js:19-23,108-121,129,149` |

### 0.2 The patterns repeated across them (each is a finding)

- **F1 — the hello chooses the scheme (downgrade).** Patterns (`StagewatchLinkService.cs:238-242`), PlayMap (`StagewatchLink.cs:66`)
  and AGMessage (`nodes.js:255`, `peerlinks.js:111`) decide TLS from the unauthenticated UDP hello. A held pin is not consulted for the
  scheme. Anyone who suppresses or forges a hello for the same id without `tls` gets the key sent in clear. Show Bus §10 already says
  "the pin — not the hello — is the trust" (`Stagewatch/docs/SHOWBUS.md:173-177`); playout-links D4 accepted this for its own link
  (`design/playout-links.md:28,113-118`); the other five clients still have it.
- **F2 — secrets on the wire.** Bearer keys on plain (rows 2, 3, 6, 8); Patterns' pairing token in an HTTP header on plain (rows 4, 8);
  plain `AUTH <token>` off loopback (row 1); twin secrets in clear (row 5).
- **F3 — the 1.1 proof is one-sided and unbound.** HMAC(token, nonce ‖ name) (`ShowBusNetwork.cs:317-339`, `Pairing.cs:54-59`,
  `PatternsLink.cs:379-384`): the server never proves anything, so on a plain carrier a fake server harvests a proof; nothing binds it
  to the connection, so a machine in the middle relays it (and a dialler that uses one token for two nodes can be relayed between
  them); the message has no label or separator; the "name" is the dialler's own HELLO claim, not an id.
- **F4 — the pairing token is weak where it travels.** 12 symbols of a 31-letter alphabet, 31¹² ≈ 7.9 × 10¹⁷ ≈ 2^59.4
  (`Patterns/src/Patterns.Core/Services/PairingToken.cs:19-22`), but a free text box (`Patterns/src/Patterns.App/Views/Sections/WebSection.axaml:38`)
  accepts anything, and it is saved with the show (`PairingToken.cs:11-14`). One sniffed NONCE + AUTH-PROOF on plain 9697 is an
  offline guessing target: a made token costs about 10⁸ GPU-seconds at 5 × 10⁹ HMAC/s (roughly 4 × 10⁴ GPU-hours worst case — out of a
  prankster's reach, not a determined attacker's); a typed "foh1" falls in milliseconds.
- **F5 — one token, every verb.** Patterns' and PlayMap's pairing token opens every mutating verb; there is no per-peer key, no scope
  and no revocation of one peer (NEW TOKEN cuts all, `WebSection.axaml:40-41`). AGMessage 0.5.0 therefore holds Patterns' operator
  token to read a context (`peerlinks.js:5-12`), which agmessage's own design rejected as "too strong for a messenger"
  (`design/agmessage.md`, decision 10).
- **F6 — address as credential.** Loopback is trusted by default (`ControlService.cs:548`; `NetAddress.cs:36-45`), PlayMap's
  `TrustedAddresses` pair without proof (`Pairing.cs:113-115`), and a Patterns wire proof trusts that **address** for OSC for 12 h
  (`ControlService.cs:556-560`) — UDP source addresses are forged trivially on a venue LAN.
- **F7 — pins confirmed on unauthenticated contact.** Keyless TLS pins on the WebSocket's answer (`StagewatchLinkService.cs:284`;
  `StagewatchLink.cs:296-302`) — acceptable for a keyless link; AGMessage pins on the first `state` frame that any client gets, key or
  no key (`nodes.js:335`; G43).
- **F8 — liveness and redial disagree, and resets cause loops.** Stagewatch 250 ms → 5 s without jitter (`PatternsLink.cs:189,229-230`);
  the others 0.5 → 5 s ± 20 % (`ShowBusNetwork.cs:31-37`; `Mapper/src/PlayMap.Net/Common/Timing.cs:60-61`; `nodes.js:203,347-353`),
  but each resets its attempt count on a TCP connect or an upgrade (`StagewatchLinkService.cs:184-185`; `StagewatchLink.cs:236-237`;
  `nodes.js:329`), so a refusal after the upgrade becomes a 0.5 s loop (peers-not-screens §0), which on today's Stagewatch also counts
  toward the per-address lockout shared with browsers (`WebServer.cs:1077-1082`).
- **F9 — clock samples are not matched.** Stagewatch takes any `OK PONG` line as a sample (`PatternsLink.cs:318-327`); on a plain
  carrier anyone on the path skews the offset that playout-links D8 schedules `AT` from.
- **F10 — receipts.** Stagewatch evicts pending receipts silently over 64 (`PatternsLink.cs:169`); no node dedupes a resent request id,
  so "unconfirmed" can never be safely resolved after a drop.
- **What already works and is kept as the model:** PlayMap's `WireClient` (plain AUTH only to loopback, one proof per connection, a
  `PairState`, nothing queued: `WireClient.cs:56-68,149-152,280-316`); PlayMap's line channel (pairing words only until paired, STATE
  after `OK paired`, 4401, 429, Origin and Host checks: `Mapper/docs/wire/companion.md:6-23`; `Mapper/src/PlayMap.Net/Http/HttpHost.cs:297-345`);
  Patterns' twin's **mutual** proof over both nonces (`Twin.cs:160-196`); Stagewatch's keys — 160 random bits, hashed at rest
  (`Stagewatch/src/Timer.Core/ApiKeys.cs:32-33,49-55,108`); AGMessage's probe-then-pinned client (`AGMessage/app/lib/tls.js:204-225`);
  every node's TLS-sniffing HTTP port (`WebServer.cs:300-318`; `Patterns/src/Patterns.Core/Services/ShowBusTls.cs:129-139`; `tls.js:142-158`).

### 0.3 What the eight designs already decided at this layer (kept)

- peers-not-screens D1–D15: a node names itself on `/ws` (`node=<id>&t=<kind>`); claims stay screens; one key speaks for one node on
  every route that writes for a node, checked before anything is stored; a binding conflict keeps the frames; **unknown or revoked keys
  never count toward the address lockout**; no new close for a key reason; the verdict is in-band and first; revocation re-checks live
  sockets; explicit rebind (`design/peers-not-screens.md:124-212`).
- playout-links D3 (pairing fails closed; plain AUTH only to loopback; a refused proof is not retried), D4 (once pinned, `wss` only),
  D5 (32 in flight, a final receipt within 30 s, "unconfirmed", one journal row per command), D7 (256 KiB reader cap, oversize read in
  part and said), D10 (a trigger's secret only to loopback or pinned https) (`design/playout-links.md:101-186`).
- spec-1-3 decisions 3 (`proto` decides nothing), 5–6 (where a hello goes; one id one peer; conflicts), 7 (reads after pairing, §7.2),
  8 (certificate span ≤ 825 days) (`design/spec-1-3.md:198-322`).
- windows-for-peers D4 (`clearSeen`: a key seen in clear while TLS is offered loses grants) — generalised in §13.5.8.
- agmessage decision 7 (a key only; the verdict from the evidence; one door for keyed HTTP; a 60 s hold, persisted; keyless links pin
  on the first frame) (`design/agmessage.md:270-322`).
- patterns-playmap-side decisions 2 (the stuck flag), 15 (a key is bound to its target, never sent to a line-chosen one), 19 (the
  Show Bus node device pairs by proof) (`design/patterns-playmap-side.md:249-257,334-346,382-392`).

### 0.4 Where this design departs from a sibling, and why

1. **Bearer node keys for app links are replaced by a proof** (peers-not-screens keeps `Authorization: Bearer` on `/ws` and HTTP).
   *Why:* a bearer key crosses the wire on every request; one first-use machine-in-the-middle, one plain hop, one copied capture holds
   it for good. A channel-bound proof gives the middle nothing, and costs one HMAC. Bearer stays for tools and scripts.
2. **First use is not "the hello's fingerprint"** (playout-links D4's words). The certificate is accepted for the handshake when it
   matches the hello, and **pinned only after the node's own proof verifies** (a forged hello plus a matching forged certificate would
   otherwise be pinned). The trees already wait for an authenticated success (`StagewatchLinkService.cs:312-318`; `PinStore.cs:73-74`);
   D4's wording was weaker than the code.
3. **The 1.1 proof becomes "legacy"** — accepted in 1.3, never the standard (F3).
4. **A proof grants nothing to an address** (F6): Patterns' 12 h OSC carry-over is named as the maintainer's call to end.
5. **Timed commands only on links with integrity** (playout-links D8 narrowed): no `AT` from a clock measured over plain.

---

## 1. Decisions

1. **One profile, four carriers, one handshake.** `line` (Patterns 9697, PlayMap 9941 and PlayMap's `/ws` `playmap.lines.v1`), `ws`
   (Stagewatch's JSON `/ws`), `http` (`POST /api/link`, then any route), `twin` (Patterns' JOIN/CHALLENGE). Each carries the same
   transcript and proofs (§13.5). *Why:* eight implementations of layer 2 is the gap; four carriers is what the apps already listen on.
   *Alternative:* one new port and protocol for all app links — a fifth listener, another firewall rule, and nothing reused.
2. **Off this machine, `ws` and `http` are TLS or nothing.** Every node already serves TLS on its HTTP port by sniffing the first byte
   (§0.2), so no app loses a carrier; a dialler with no TLS on offer links keyless (the public view) and says why. *Alternative:* keep
   plain with a proof — every request and frame would still be readable and alterable on venue Wi-Fi.
3. **Line wires adopt the same sniff (TLS on 9697 and 9941, plain kept for Companion decks and loopback); until they do, a plain line
   link is "degraded": proof only, amber, never scheduling, never carrying a secret.** *Why:* the VT link Stagewatch → Patterns must keep
   working the day this lands; Patterns' L55 is the honest end date.
4. **Mutual, channel-bound proof; the dialler proves first.** T binds version, carrier, both ids, the key id, both nonces and the
   certificate each end saw (tls-server-end-point binding — portable: .NET exposes no TLS exporter, Node does; every tree already
   computes the certificate's SHA-256). The dialler proves first because a node's port is open to anyone on the LAN: a server-first
   order hands every stranger who connects a guessing target. The dialler acts on nothing until the node's proof verifies.
   *Alternatives:* client certificates (mutual TLS) — browsers share these ports and some prompt for a certificate when asked, and the
   certificates carry `serverAuth` only (`ShowBusTls.cs:94-96`; `tls.js:85-88`); a PAKE (SPAKE2, CPace) — the right tool for a 59-bit
   code, but neither .NET nor Node has one, and hand-rolled curve arithmetic in two languages is a worse risk than enrolment over a
   pinned channel (decision 6).
5. **Keys are scoped and bound to one pair.** A node issues each key for one dialler id with one scope (`follow`, `post`, `drive`,
   `operate`); the dialler holds it for one node id and its pin. *Why:* F5; peers-not-screens' binding generalised to Patterns and PlayMap.
6. **A pairing code enrols once, over TLS, by derivation.** The person types Patterns'/PlayMap's pairing token into the dialler as
   today; the first link proved with it over TLS derives a 256-bit link key on both ends (`K_link = HMAC(K_code, "enrol\n" ‖ T)`), never
   sent. From then on the code is not used by that link, so F4's offline guess has nothing to work on, and each peer is a row the node's
   operator can revoke. NEW TOKEN keeps its meaning by revoking every key enrolled with the old code.
7. **No secret on any wire, ever; HTTP requests carry a MAC, not a key.** `Authorization: ShowBus lid=…, n=…, mac=…` with a per-link
   key and a counter window: no secret travels, nothing replays, no clock needed. *Alternative:* a session ticket (a bearer secret
   inside TLS) — simpler, but still a secret on the wire.
8. **The hello may raise a link's guard and never lower it; floors make it stick.** A pin and a version floor per node id refuse plain,
   legacy or no-TLS offers from a node that once did better.
9. **Liveness, clock, receipts, order, bounds and redial are spec-1-3's numbers plus matched pongs, request-id dedupe and a stable
   reset.** *Why:* F8–F10, each with a deterministic test.
10. **One closed set of link states and one link record every reader takes.** The Eye, the desks, STATE, context, Companion and the AI
    read the same record; every transition is one journal row and one log line.
11. **Secrets: one list of where they never ride, one canary fence per tree, and the cross-app sweep of the interop lane.**
12. **Conformance is a vector block inside the hashed spec body plus one scripted peer copied byte-for-byte into each tree.** *Why:*
    spec-1-3 decision 10 already holds the body by hash in every tree; the peer is Node 22, which all four CIs install.

---

## 2. The master text: §13 PeerLink (normative, for the one master edit)

The text below is what this area hands to spec-1-3's single edit. It is written to be pasted.

```markdown
## 13. PeerLink — one authenticated link between two nodes (1.3)

A *PeerLink* is one connection, or one HTTP session, between two nodes that have proved to each other who they are. Discovery (§2)
finds a node; a PeerLink proves it, keeps it alive and bounds it; commands, receipts, state, context, proposals and windows (§3–§6,
§11, §12) travel on it; the desks, the Eye and the AI read what the link knows (§13.14). Every app-to-app connection in this contract is
a PeerLink: Stagewatch's playout links and the node links on its `/ws`; Patterns' Stagewatch link, twin, node asks and Show Bus node
devices; PlayMap's Stagewatch and Patterns links; AGMessage's Stagewatch link and its links to Patterns and PlayMap. Operator doors are
not PeerLinks and keep their own rules (§13.15).

MUST, MUST NOT, SHOULD and MAY are used as RFC 2119 uses them. The *dialler* opens a link; the *node* is dialled. Numbers are §14's
`link.*` and `peerlink.*` rows.

### 13.1 What the venue network can and cannot do

Anything on the show network — the venue's Wi-Fi included, where every client may read every other's traffic — is assumed hostile.

| An attacker on the network can | Under this section it cannot |
|---|---|
| hear every hello: names, ids, hosts, ports, caps, fingerprints (public by design) | learn a key, code, PIN or view pass from the wire: none is ever sent (§13.5, §13.16) |
| forge hellos for any id, kind, address or fingerprint | turn a link plain, change a pin, choose the key proved or mark a link verified (§13.3) |
| answer at a node's address (ARP, a stolen IP, a forged hello) | complete a link without the key, or relay a proof between two nodes: proofs are mutual and bound to the certificate each end saw (§13.5) |
| intercept a first contact with its own certificate | have that certificate pinned on a keyed link: the proofs fail, and the dialler shows it red (§13.7) |
| replay anything it recorded | replay a proof (fresh nonces), a request (counter window) or a command (request-id dedupe) (§13.5, §13.6, §13.11) |
| strip TLS or the handshake from a link it sits in | downgrade a node it was linked to before: floors (§13.8) |
| read and alter traffic on a plain link off this machine | use that link to schedule, or to carry a secret; the link says it is degraded (§13.2, §13.10) |
| send wrong guesses from its own address | lock a peer or a browser out: wrong proofs count per address and key in a table of their own; unknown keys count nowhere (§13.4) |
| drop, delay or flood traffic; jam the Wi-Fi; see who talks to whom and when | — denial of service and traffic analysis are not prevented; every link says what it lost (§13.14) |
| read what a node publishes to anyone (§8's public view) | read anything else of the show: it travels only on PeerLinks (§1 rule 4) |

### 13.2 Carriers

1. A PeerLink rides one of four carriers:

   | carrier | where | handshake |
   |---|---|---|
   | `line` | a line wire: Patterns 9697, PlayMap 9941, PlayMap's `/ws` with subprotocol `playmap.lines.v1` | §13.5.3 |
   | `ws` | a node's JSON WebSocket: Stagewatch's `/ws?node=<id>&t=<kind>&link=1` | §13.5.4 |
   | `http` | request and response: `POST /api/link`, then any `/api/…` route | §13.5.5 |
   | `twin` | Patterns' twin port, inside its own JOIN and CHALLENGE words | §13.5.6 |

2. **Off this machine, a `ws` or `http` PeerLink MUST be TLS (§13.7).** A dialler MUST NOT open a plain `ws` or `http` link to another
   machine, whatever a hello, a typed `http://` address or a setting says. With no TLS on offer it links keyless to the public view at
   most, and says why (§13.14).
3. A `line` or `twin` carrier MUST be TLS when the node offers TLS on that port. Where it offers none, a line link MAY run plain off this
   machine **only degraded**: proved, never sending a secret, shown amber, its clock never scheduling (§13.10), and its data understood
   to be readable and alterable on the path.
4. **This machine.** A link is *local* only when the socket's remote address is a loopback address — never because of a host name, the
   hello's `host`, or an address the node owns. Local links MAY be plain. A node that forwards outside traffic to one of its ports
   through loopback (a tunnel, a reverse proxy) MUST tell that traffic apart and MUST NOT treat it as local. Before any listener grants
   anything to loopback it MUST check that the request names the node by one of its own hosts (`Host`) and comes from no foreign page
   (`Origin` absent or the node's own), so that a page in a browser on this machine cannot borrow loopback's trust.
5. Every listener a PeerLink uses MUST honour the node's bind address.
6. Where each node is dialled, and what it may dial:

   | node | dialled at | dials |
   |---|---|---|
   | Stagewatch (`main`) | `ws` and `http` on its web port (9924), TLS sniffed on the same port | Patterns 9697 (`line`); PlayMap's `/ws` `playmap.lines.v1` (`line`, TLS) or 9941 |
   | Patterns (`patterns`) | `line` on 9697 (TLS sniffed on the same port), `http` on its web port, `twin` on its twin port (TLS) | Stagewatch (`ws`, `http`); PlayMap (`line`) as a Show Bus node device; its twin |
   | PlayMap (`playmap`) | `line` on 9941 (TLS sniffed) and its `/ws` `playmap.lines.v1` (TLS), `http` on 9940 | Stagewatch (`ws`, `http`); Patterns 9697 (`line`) |
   | AGMessage (`messenger`) | — (nobody links to it with a key in 1.3) | Stagewatch (`ws`, `http`); Patterns and PlayMap (`http`, or PlayMap's `line`) |

### 13.3 Identity

1. A node is its hello `id` and `t` together with its certificate (§10). The id names it; its certificate's key and the link key prove it.
2. A dialler keeps one **link record** per remote node id: the pin, the floor (§13.8), the key bound to that node, the clock, the state.
   An address is where to dial, never who.
3. **A hello is a hint that may raise a link's guard and MUST NOT lower it.** It may add a peer to dial, an address to try, and the hints
   `tls` and cap `peerlink` (try TLS, try PeerLink first). It never turns a link plain, never changes or confirms a pin, never chooses which
   key is proved or where a key goes, and never marks anything verified. A typed address follows the same rule.
4. A typed address is recorded as `addr:<host>:<port>` until the first verified link names the node's id; the record then becomes
   `node:<id>` — over TLS, with that pin — and the key typed for it is bound to that id. A key bound to one id MUST NOT be proved to, or
   sent toward, another id: a typed address that is now answered by another id is refused with both ids named.
5. An id in conflict (§2) gets no new link.

### 13.4 Keys

1. **Classes.**
   - *key* — machine-made, at least 128 random bits from the platform's CSPRNG, shown once: Stagewatch's `swk_…` (160 bits) and every
     node's link keys. `K = SHA-256(UTF-8(key))`, which is what Stagewatch already stores for a key.
   - *code* — a pairing code a person reads and types: Patterns' and PlayMap's pairing token, Patterns' twin key.
     `K = SHA-256(UTF-8("showbus-peerlink-1 code\n" + canon(code)))`, where canon removes `-`, space, tab, CR and LF and upper-cases.
   - *link* — a key both ends derive at enrolment (§13.4.6) and never send. `K` is its 32 bytes.

   A key's id: `kid` = lowercase hex of the first 8 bytes of `SHA-256(UTF-8("showbus-peerlink-1 kid\n") ‖ K)`; a code's kid is the
   word `code`.
2. **One key, one pair.** A node binds each key to the one dialler `t:id` it speaks for — at creation, or at its first verified use —
   and refuses it for any other (`this key speaks for <kind> <id>`); such a refusal is never counted. A dialler holds each key for one
   node id and its pin (§13.3.4). A code belongs to its node and may be proved by several diallers, each named by its own id.
3. **Scope.** Every key has one scope from this ladder, mapped by each node's §8 row onto its own verbs: `follow` (state, context,
   subscriptions), `post` (and its own context, proposals and their results — Stagewatch's `node` scope), `drive` (and the node's
   transport-class verbs), `operate` (every verb, as the operator — a code, Stagewatch's PIN or a control key). The node MUST enforce the
   scope; a refusal names it (`this link's key is scope follow — <node>'s operator can raise it`). A dialler's own fence is extra.
4. **At rest.** A node keeps only what verifies (`K`, or the hash it already keeps). A dialler keeps its keys protected (Windows DPAPI
   for the user, an owner-only file elsewhere). No key, code or `K` is in a show file, a project, a backup, an export or a twin's
   traffic — save a twin's secrets under its `SendSecrets` switch over a TLS twin link.
5. **Revocation reaches live links at once.** Revoking or re-binding a key, or a new code, MUST refuse every verified link that used it
   (§13.14) and end its sessions (§13.6). A new code (Patterns' NEW TOKEN) also revokes every link key enrolled with the old one.
6. **Enrolment.** A link proved with a *code* over TLS enrols: both ends derive `K_link = HMAC-SHA256(K_code, "enrol\n" ‖ T)` (T of
   §13.5.1); the node stores it bound to the dialler's id with the scope its operator set for that kind, journals it and names it in its
   verdict (`"enrolled":"<kid>"`); the dialler proves with that kid from then on. A code proved over a plain carrier MUST NOT enrol.
   A node shows each enrolled key with the dialler's name, id, scope and when, so an enrolment nobody expected is visible.
7. **Counting.** Unknown keys and binding refusals are never counted toward any lockout. Wrong proofs are counted in a table of their
   own per (address, kid), and per address for `code` — five a minute lock that pair (or that address's code) for the minute, and a
   locked pair is refused before its proof is checked. That table never locks out a PIN, a viewing code or a browser.

### 13.5 Pairing by proof

1. **The transcript.** T is these lines in UTF-8, joined by LF, with no final LF:

   ```
   showbus-peerlink-1
   carrier=<line|ws|http|twin>
   offer=<the dialler's highest PeerLink version>
   speaks=<the version the node chose>
   server=<node t>:<node id>
   client=<dialler t>:<dialler id>
   kid=<kid>
   snonce=<32 lowercase hex: 16 bytes from the node's CSPRNG>
   cnonce=<32 lowercase hex: 16 bytes from the dialler's CSPRNG>
   bind=<tls:sha256:<64 lowercase hex> | plain>
   ```

   `bind` is the certificate this connection showed — the one the dialler was shown, the node's own on the node's side — or `plain`. Each
   field is checked against its shape (§2, §14) before T is built, so no LF can enter it.
   - client proof = lowercase hex of `HMAC-SHA256(K, "client\n" ‖ T)`
   - server proof = lowercase hex of `HMAC-SHA256(K, "server\n" ‖ T)`
   - `lid` (the link's id, not a secret) = lowercase hex of the first 8 bytes of `SHA-256("showbus-peerlink-1 lid\n" ‖ T)`
2. **Order.** The dialler proves first. The node verifies, then answers with its own proof. The dialler MUST NOT take state, context, a
   receipt, a clock sample or a pin from the link before the node's proof verifies. Each nonce answers one proof, right or wrong. One
   proof per connection: a wrong one ends the connection (`line`, `twin`), the session (`http`), or the socket's node standing (`ws`, which
   stays a public viewer as §7.1 says).
   A node MUST treat an unknown kid exactly like a known one until the proof, and refuse it with the same words as a wrong key — *"the
   proof did not verify: a wrong key, or another machine between us"* — so a kid is no oracle; its own journal MAY name the kid.
3. **`line`** (appended verbs of the line grammar):

   ```
   → @l1 LINK 1 main 3f9c2a1b7d0e ffeeddccbbaa99887766554433221100 code
   ← OK @l1 LINK 1 patterns 9f8e7d6c 00112233445566778899aabbccddeeff tls:sha256:03e9b152…
   → @l2 LINK PROOF a0622441e11e24be7577abcff6599b5b8c5031a06739a2774e94307d70b5c09f
   ← OK @l2 paired {"v":1,"scope":"drive","verified":"key","proof":"a628a0aa…","lid":"c1bb791379aa366c","floor":1,"enrolled":"936086ee6bc93cef"}
   ← STATE {…}                                   the greeting, now; then on every change
   ```

   `LINK <offer> <t> <id> <cnonce> <kid>`; the answer `OK LINK <speaks> <t> <id> <snonce> <bind>`; `LINK PROOF <hex>`. Before `paired`
   the node answers only what finding and pairing need (§7.2's list, plus `LINK`) and nothing of the show. An older node's
   `ERR @l1 … unknown word` is §13.8's.
4. **`ws`.** The dialler opens `wss://<node>/ws?node=<its id>&t=<its kind>&link=1` with no `Authorization` header.
   - The node's first message: `{"type":"link","v":1,"speaks":1,"server":{"t","id"},"snonce","bind"}`.
   - The dialler's: `{"type":"link","v":1,"offer":1,"client":{"t","id"},"cnonce","kid","proof"}`.
   - The node's verdict (§7.1's, with `proof`, `lid` and `floor` appended):
     `{"type":"auth","ok":true,"scope":"node","node":{"t","id","verified":"key"},"proof","lid","floor":1}`, or
     `{"type":"auth","ok":false,"error","retry":60}`.
   No frame is sent to a `link=1` socket before its verdict.
5. **`http`.** `POST /api/link` `{"v":1,"offer":1,"client":{"t","id"},"cnonce","kid"}` → `200 {"v":1,"speaks":1,"server":{"t","id"},"snonce","bind","hid"}`
   (`hid`: 16 hex, one use, valid 10 s); then `POST /api/link` `{"hid","proof"}` → `200 {"ok":true,"scope","verified":"key","proof","lid","idleMs":60000}`
   or `403 {"ok":false,"error","retry":60}`.
6. **`twin`.** JOIN carries `offer`, `client`, `cnonce`, `kid`; CHALLENGE carries `speaks`, `server`, `snonce`, `bind`; the proofs are
   this section's; the twin's other words are its own (layer 3).
7. **Legacy proofs.** A node MAY accept 1.1's `NONCE` / `AUTH-PROOF` (HMAC over the nonce and the HELLO name) in 1.3, as
   `verified:"legacy"`, amber on both ends. A dialler MAY send one only to a node with no floor (§13.8) that answered `LINK` as an unknown
   word. Plain `AUTH <secret>` MUST NOT be sent off this machine (§7).
8. **Bearer keys** (`Authorization: Bearer`) remain for tools and scripts. A dialler that speaks PeerLink MUST NOT send a key to a node it
   links with. A node that receives a key in clear from another machine MUST mark that key *seen in clear* — on its key row and in its
   context, amber, with when and from where: "replace this key" — and gives it no window grant (§12); from 1.4 it refuses it.

### 13.6 Requests on a verified link

1. On `line`, `ws` and `twin` the connection is the session: its messages need nothing more.
2. On `http` — and for the HTTP side requests of a `ws` link — every request carries
   `Authorization: ShowBus lid=<16 hex>, n=<counter>, mac=<64 hex>`:
   - `K_req = HMAC-SHA256(K, "request\n" ‖ T)`;
   - `mac` = lowercase hex of `HMAC-SHA256(K_req, "showbus-peerlink-1 request\n" + lid + "\n" + n + "\n" + METHOD + "\n" + request-target + "\n" + lowercase hex SHA-256(body))`,
     where request-target is the exact bytes of the request line's target and an empty body hashes as empty;
   - `n` starts at 1 and grows by one per request on the session; the node accepts an `n` it has not seen among the newest 64 and refuses
     any other `401 {"ok":false,"error":"…","relink":true}`. A parameter it does not know refuses the request.
3. A session lives while its link's socket lives; an `http` session ends after 60 s unused or 12 h. A refused session is linked again
   once; a second refusal is §13.13's hold.

### 13.7 TLS and pinning

1. TLS 1.2 or 1.3 with ephemeral key exchange; the node's certificate is §10's. A dialler checks the certificate by its fingerprint and
   by nothing else — never a name, a chain or an authority.
2. **First use.** With no pin, a certificate is accepted for the handshake only if it matches the hello's `tls` (when a hello names one).
   It is **pinned only after the node's proof verifies** — or, on a keyless link that reads the public view only, after its first frame.
   The dialler shows the short fingerprint when it pins, so a person may compare it with the node's screen.
3. **Changed.** A pinned node showing another certificate is refused before anything is sent — CERTIFICATE CHANGED — until the operator
   accepts the new one. The dialler MAY probe every 60 s to show the current fingerprint, and MAY say whether the node can prove the
   link key over it; the operator still decides. A certificate that contradicts the hello is refused.
4. A pin store that cannot be read is said (amber: "pins lost — every node is new here; compare fingerprints"); until it is rebuilt,
   floors come from the hints (§13.3.3).
5. A node SHOULD serve TLS on every port a PeerLink uses, telling TLS from plain by the first byte (0x16), as §10 does for HTTP.

### 13.8 Versions and floors

1. The PeerLink version is chosen in the handshake (`offer`, `speaks`), and both are inside T, so a changed offer fails the proof. A
   hello's `proto` (§2) decides nothing here.
2. **Floors.** After a verified link a dialler records, for that node id, the floor `{tls, peerlink}` it reached. A later link to that id
   that offers less — plain where TLS was, `LINK` refused as an unknown word, a lower `speaks` — MUST be refused, red: *"<node> used to
   prove itself with PeerLink 1 over TLS; it now offers less — not linking"*, until the operator lowers the floor (one press, journalled).
3. A hello's `tls` and cap `peerlink` raise the attempt and MAY set the floor of a node never linked; they never lower one.

### 13.9 Liveness

1. The dialler pings every 1–2 s (§5's PING with its time); the node answers every ping. Any message from the verified node counts as
   heard. **Stale** after 6 s without one; **gone** after 12 s: dropped and redialled (§13.13).
2. A pong counts only when it answers one of the dialler's last 8 pings (its `t1`).
3. **Stuck.** A node that knows its producer has stopped says so at least every beat and sends no state until it turns over:
   `{"type":"beat","live":false}` on `ws`, an untagged `BEAT STUCK` line on `line`. The link stays live — its transport answers — but what
   it held is not a fact: every reader shows no time and says the node is stuck, until the next state frame. A plain beat changes nothing.
4. A connection that has not paired within 10 s is closed. A node holds at most 4 unpaired connections per address and 64 `http`
   handshakes in all.

### 13.10 Clock offset

1. §5's exchange on every verified link; a sample only from a matched pong (§13.9.2); the best of the last 8 by round trip; a sample
   older than 10 s is not used to schedule. CLOCKS APART beyond 2 s.
2. A clock measured over a degraded link (plain, off this machine) MAY be shown and MUST NOT schedule a timed command (`AT`).

### 13.11 Receipts and request ids

1. Every command a dialler sends on a PeerLink carries a request id `<inst>-<n>`: its hello's `inst` and a counter — unique for the
   process's life, at most 32 characters (§3).
2. A node keeps, per verified dialler id, the receipts of its last 256 request ids for 5 minutes. A command whose id it holds MUST NOT run
   again: it is answered with the receipt it had, `"repeat":true` appended. A dialler that lost an answer in a drop MAY send the same line
   with the same id once the link is back, to learn what happened. That is the only resend this contract allows; nothing is resent on
   its own.
3. §3's receipts: `accepted` when not applied within 3 s, one follow-up `"final":true` within 30 s; no answer in 30 s is *unconfirmed* at
   the dialler. A command to a link that is not live fails at once ("not sent").
4. At most 32 commands in flight per link; the 33rd is refused at the dialler, never evicted.

### 13.12 Order and latest wins

1. One writer per connection. Replies and receipts are never dropped and are matched by id, not order. State is latest-wins: a slow
   reader gets the newest frame, never a backlog. An event stream (every item matters) is ordered and counted; a reader that falls behind
   is cut with words saying where to resume.
2. A state frame whose counter (§4) is smaller than the last one on the link means the node restarted: the reader takes it and drops what
   it inferred from the old one.

### 13.13 Bounds, back-pressure and redial

1. A command a dialler sends is at most 4 KiB; any message a node sends at most 1 MiB; a posted context at most 64 KiB (§6). A reader
   that keeps less reads only what it keeps and says so with the size; an over-long message is dropped and counted, never taken whole.
2. A node queues at most 64 replies per connection, plus one latest-state slot; a peer that reads nothing for 12 s is cut.
3. **Redial:** 0.5 s doubling to 5 s, each wait ± 20 % from a generator seeded by the CSPRNG. The attempt count resets only after the
   link has been verified and live for 10 s — never on a TCP connect or a WebSocket upgrade alone.
4. **Holds.** After a key is refused (unknown, revoked, bound to another, a wrong proof, locked out, out of scope) the dialler holds that
   key for 60 s or the node's `retry` / `Retry-After`, whichever is longer, across its own restarts, and proves it again only when the hold
   ends, the key changes, or the operator presses *Link again now*. A `ws` link keeps the public frames meanwhile. CERTIFICATE CHANGED, a
   floor refusal and an id conflict link nothing until the operator acts. The node operator's `disconnect` holds 30 s.

### 13.14 States, and what each side shows

A dialler keeps one state per link, from this closed set; the light carries its shape as well as its colour.

| state | light | when | the dialler says (example) | the node shows |
|---|---|---|---|---|
| `off` | ○ grey | not configured | "link off" | — |
| `looking` | ○ grey | no address and no live hello of the kind | "listening for a Stagewatch on the show network" | — |
| `connecting` | ○ grey, △ amber after 10 s | dialling, TLS, handshake | "dialling Hall A at 10.0.0.5:9924 over TLS" | — |
| `live` | ✓ green | verified: TLS with its pin (or local) and both proofs; or keyless on purpose, to the public view | "Hall A · pinned 03e9 b152 ca42 518c · key 52f6… · scope post · +12 ms" | peer row ✓ "Patterns FOH · key 52f6… · TLS · since 20:01" |
| `degraded` | △ amber | verified, but plain off this machine, or a legacy proof, or a scope below what the link needs, or keyless where a key is set | "Patterns FOH: proved, not private — its wire offers no TLS yet; timed GO off" | peer row △ "legacy proof, plain — update <app>" |
| `stale` | △ amber | 6–12 s silent | "Hall A quiet for 8 s" | peer row △ "quiet 8 s" |
| `gone` | ✕ red if the show uses it, else △ amber | silent > 12 s, redialling | "Hall A not answering — dialling again in 2.4 s" | peer row "gone at 20:14" |
| `held` | ✕ red | its key was refused | "Hall A refused this desk's key (revoked) — next try 20:15:05; make a node key on Hall A" | refusal row ✕ "PlayMap Stage left (10.0.0.12): key 52f6… refused (revoked) ×3 this minute" |
| `cert-refused` | ✕ red | CERTIFICATE CHANGED, or not the hello's | "CERTIFICATE CHANGED: Hall A now shows ab12 … — nothing sent; accept it on the Nodes page" | — |
| `floor-refused` | ✕ red | the node offers less than before | "Hall A used to prove itself over TLS; it now offers plain — not linking" | — |
| `conflict` | △ amber | an id in conflict (§2) | §2's words | — |

Beside the state: `stuck` (✕ on the data: "Hall A says its show is stuck — no time shown") and `apart` (△ CLOCKS APART).

**Every reader.** The dialler publishes a link record, appended at the end of each row that carries a link (its STATE, its context item
`link:<name>`, the Eye's edge, its desk's node card, its Companion variables, its AI facts):
`{"peer":{"t","id","name"},"carrier","tls":"pinned|first|none|local","pin":"<short>","proof":"mutual|legacy|none","scope","state","light","words","heard","clock":{"offsetMs","rttMs","apart"},"stuck","floor"}`.
Every state change is one journal row and one log line, with the words and never a secret. The node shows each verified peer (its
context `peers` rows carry `link` and `verified`, §6), each refusal with its words and its count this minute, and each key's bound node,
scope, last use, enrolment and *seen in clear*.

### 13.15 Doors that are not PeerLinks

OSC, Stagewatch's Companion TCP, Companion decks on the line wires (legacy proofs until the module adopts `LINK`), browser pages and
their `?pin=`, PlayMap's sync and fleet traffic. Each is named in its node's §8 row with what it does without a proof, and each node's
health lights say what these doors leave open. A PeerLink proof grants nothing to an address: it proves a key on one connection.

### 13.16 Secrets

1. A key, code, `K`, `K_req`, `K_link`, PIN or view pass is never sent on any carrier — only proved — and rides in no URL, hello, frame,
   STATE, context, receipt, journal row, log line, report, CSV, support bundle, error or exception message, Companion variable, AI brief
   or proposal.
2. Proofs, MACs, nonces and transcripts are not logged either (a proof made with a code is a guessing target). A log names a key by its
   kid, or its name and last four characters; a certificate by its short fingerprint; a peer by kind and id.
3. A line that may carry a secret (a legacy `AUTH`, a verb with a token argument) is redacted where it is read, before any logger,
   journal or fault path sees it; a fault answer never echoes the line.
4. Each tree runs §13.18's vectors and its scripted-peer scenarios with canary secrets, and fails when a canary — or a proof or MAC made
   with it — appears in anything it captured.

### 13.17 Numbers (rows of §14's block)

    peerlink.version 1
    peerlink.label showbus-peerlink-1
    peerlink.nonce.bytes 16
    peerlink.kid.bytes 8
    peerlink.lid.bytes 8
    peerlink.key.bits.min 128
    peerlink.handshake.ms 10000
    peerlink.http.handshakes 64
    peerlink.unpaired.per.address 4
    peerlink.wrong.proofs.per.minute 5
    peerlink.refused.hold.ms 60000
    peerlink.cert.probe.ms 60000
    peerlink.stable.ms 10000
    peerlink.redial.jitter.pct 20
    peerlink.pings.matched 8
    peerlink.clock.fresh.ms 10000
    peerlink.inflight 32
    peerlink.receipt.final.ms 30000
    peerlink.receipts.kept 256
    peerlink.receipts.kept.ms 300000
    peerlink.command.bytes 4096
    peerlink.message.bytes 1048576
    peerlink.replies.queued 64
    peerlink.reader.stall.ms 12000
    peerlink.request.window 64
    peerlink.session.idle.ms 60000
    peerlink.session.max.ms 43200000
    peerlink.disconnect.hold.ms 30000

(`link.beat.ms 2000`, `link.stale.ms 6000`, `link.drop.ms 12000`, `link.redial.first.ms 500`, `link.redial.max.ms 5000`,
`clock.apart.ms 2000`, `receipt.deadline.ms 3000` and `request.id.chars 32` are already §14's.)

### 13.18 Conformance

A node sends cap `peerlink` only when its tree passes the vectors below and the scripted peer's scenarios (`tools/showbus/peerlink-peer.mjs`,
one file byte-identical in every tree, its SHA-256 printed in this page's header) for every carrier it uses, in each role it plays.

```peerlink
{"case":"canon","code":"k7qm-3xwd p9ra","canon":"K7QM3XWDP9RA","K":"5155f2dfc471eeab6498c03b18e3e3df5f8c9f7a2e5f85c8ed4526aaad49e5c8","kid":"code"}
{"case":"key","key":"swk_abcdefghijkmnpqrstuvwxyz234567","K":"98bfa2c5a8399cb4c76056ad0f56f2fdc325b81ea7b6f3c1323edf367f754fdb","kid":"52f6be8e5259dfef"}
{"case":"proof","carrier":"line","offer":1,"speaks":1,"server":"patterns:9f8e7d6c","client":"main:3f9c2a1b7d0e","kid":"code","snonce":"00112233445566778899aabbccddeeff","cnonce":"ffeeddccbbaa99887766554433221100","bind":"tls:sha256:03e9b152ca42518c28128f36def699be8aea5eec11bb9d5afc18a314f8c3eb04","K":"5155f2dfc471eeab6498c03b18e3e3df5f8c9f7a2e5f85c8ed4526aaad49e5c8","Tsha256":"0a7706734a78a999335af163a788f9ace1ac831a53d999bc45b46c72ce7986c9","client":"a0622441e11e24be7577abcff6599b5b8c5031a06739a2774e94307d70b5c09f","server":"a628a0aa5691349ced0d2e1d8b9b13203b55bbd3d994a9e45925b74fc1369e02","lid":"c1bb791379aa366c","enrolK":"fb7d7f2a95b74ba83c4dd61e4382fad70056f92f7267d5921471df4905c5dd44","enrolKid":"936086ee6bc93cef"}
{"case":"refuse","why":"bind","as":"proof","bind":"tls:sha256:8e8bc45bb14f9c7413604ef78eb1c42b465437cf700ae04a8f9553ffb0286edc","client":"b1117233650616c95cb2d776c67e24aacd286f08c8a7361bbaa31c5b0fe628b6","expect":"refused"}
{"case":"refuse","why":"offer","as":"proof","offer":0,"client":"4ac3e7c904c4e6ada67f1e283bf8eac7f21c7a797b992e43e6143fac5b262571","expect":"refused"}
{"case":"refuse","why":"role","as":"proof","client":"a628a0aa5691349ced0d2e1d8b9b13203b55bbd3d994a9e45925b74fc1369e02","expect":"refused"}
{"case":"proof","carrier":"ws","offer":1,"speaks":1,"server":"main:3f9c2a1b7d0e","client":"patterns:9f8e7d6c","kid":"52f6be8e5259dfef","snonce":"00112233445566778899aabbccddeeff","cnonce":"ffeeddccbbaa99887766554433221100","bind":"tls:sha256:03e9b152ca42518c28128f36def699be8aea5eec11bb9d5afc18a314f8c3eb04","K":"98bfa2c5a8399cb4c76056ad0f56f2fdc325b81ea7b6f3c1323edf367f754fdb","client":"a462d48f46a2cabe1d99d6ef1a96f3169621a2190beaec047a27686f8f1162e5","server":"834bb29b51465d4d8cc839836a78188b25600016eb4407bd978ba6f2fd7d09d3","lid":"b4da667a1f3077c9","Kreq":"1b0e10518e12024201b74d254e51fa7ca3230d4ce1cb21d6c93c1e2145117525"}
{"case":"request","lid":"b4da667a1f3077c9","Kreq":"1b0e10518e12024201b74d254e51fa7ca3230d4ce1cb21d6c93c1e2145117525","n":1,"method":"POST","target":"/api/peers/context","body":"{\"v\":1}","mac":"5f54f56f8a086ae3cd4a769420a4999d646717645ae9c853e0f9b1a8ce71d6d8"}
{"case":"request","lid":"b4da667a1f3077c9","Kreq":"1b0e10518e12024201b74d254e51fa7ca3230d4ce1cb21d6c93c1e2145117525","n":2,"method":"GET","target":"/api/context","body":"","mac":"4482cbdb6c039f295edf5d9ffc864af2978cd9aec689cb338b45d14c52196bbe"}
{"case":"proof","carrier":"line","offer":1,"speaks":1,"server":"playmap:0a1b2c3d","client":"patterns:9f8e7d6c","kid":"code","snonce":"00112233445566778899aabbccddeeff","cnonce":"ffeeddccbbaa99887766554433221100","bind":"plain","code":"ABCD-EFGH-JKMN","client":"228994337068a5a5c3ca67af80275b72d2480c6b7d393d9cbe94806f06b377f4","enrol":false}
{"case":"proof","carrier":"http","offer":1,"speaks":1,"server":"playmap:0a1b2c3d","client":"messenger:5e6f7a8b","key":"pmk_3vq8xk2m9tz7w4hc6r5n2b8d7f4g9j3k","kid":"00e409c820630818","snonce":"00112233445566778899aabbccddeeff","cnonce":"ffeeddccbbaa99887766554433221100","bind":"tls:sha256:03e9b152ca42518c28128f36def699be8aea5eec11bb9d5afc18a314f8c3eb04","client":"a927a1aa5eed82705d5f0777903ae4cd5d4b28815f54ae9841ce09f00d45b621","server":"624dbafbf90179e98a7c9f819b05329d9476dd052b2d8a580202832c2df32248","lid":"829127f2f1ba4b52"}
{"case":"request","lid":"829127f2f1ba4b52","from":"proof http above","n":7,"method":"POST","target":"/api/cmd?x=a%20b","body":"PLAYLIST GO show intro","mac":"581bd275a81c08875d3a5227dff9d2c756332f5c2ef5e3a9c402acb3eabb3133"}
```
```

*(The "refuse" rows reuse the first "proof" row's fields with the one named field changed; `role` presents the server proof as a client
proof. The block also gains parse rows — LF or CR inside a field, upper-case hex, 31 or 33 hex nonces, a kid of 15 hex, an `n` of 0,
a repeated `n`, an unknown `Authorization: ShowBus` parameter — each with `expect":"refused"` and the refusal words; and machine rows —
event sequences in, states and lights out — listed in §5 below. They are written when the text is pasted.)*

---

## 3. Layers, precisely

| layer | owns | does not own | Show Bus section |
|---|---|---|---|
| 1 Discovery | the hello: kind, id, inst, name, host, addresses, ports, caps, `tls`, `proto`, `presenting`; hearing, conflicts, forgetting | trust, keys, schemes, liveness of a link, show data | §2 |
| 2 PeerLink | carriers, TLS and pins, identity of the two ends, keys (classes, scope, binding, rest, revocation, enrolment), the handshake and proofs, request MACs, versions and floors, liveness and stuck, clock samples, request ids and dedupe, ordering classes, bounds, back-pressure, redial and holds, the link's states and its record, secret hygiene | what a command means, what is in STATE or a context, risk, approvals, windows | §13 (new), §5, §7, §10 |
| 3 Show Bus messages | commands and receipts' content, state frames, context, proposals and the desk, windows and grants | how they are authenticated, delivered or bounded on the wire | §3, §4, §6, §11, §12 |
| 4 Views | the Eye (God's Eye: every node, device and link as nodes and edges, the link record as the edge's state), the desks' Show network and Nodes pages, preflight, Companion variables and feedbacks, the AI's facts, the journal and report | any decision about trust: views read the link record and never make one | §8, each app's papers |

Rules that follow: a hello may only raise layer 2's guard; layer 3 never sees a secret (layer 2 strips nothing — nothing was there);
layer 4 shows the state and words layer 2 published and never invents a light; a node with no layer-2 link to another reads it one hop
away through a context's `peers` (§6) and the Eye draws that edge grey ("heard, not linked").

---

## 4. Per app: today and the changes

### Stagewatch — `PatternsLink` → `PlayoutLink` (dialler to Patterns 9697 and PlayMap)

**Today.** Plain TCP only (`PatternsLink.cs:192-199`); 1.1 proof, one-sided (`:379-384`); plain `AUTH <token>` to any host after 1.5 s
without a NONCE (`:280-284`, G08); a late NONCE ignored (`:311`); an empty token counts as proved and asks CONTEXT every 5 s (`:202,292-295`);
any `OK PONG` is a clock sample (`:318-327`); receipts evicted silently over 64 (`:169`); 250 ms → 5 s with no jitter (`:189,229-230`);
256 KiB lines (`:40`); finder takes only `t:"patterns"` (`:459`). The token is DPAPI-protected at rest (`AppSettings.cs:150-151`).

**Changes.** playout-links' `PlayoutLink` (its D2–D5 and D7 kept) speaks PeerLink: `LINK` first; to PlayMap over `wss`
`playmap.lines.v1` with pin and floor; to Patterns proof-only plain (degraded, amber, "update Patterns: its wire offers no TLS") until
9697 offers TLS, then TLS with a floor; enrolment from the pairing token over TLS; never plain AUTH off loopback (D3); clock samples only
from matched pongs, and `AT` only on TLS or local links (D8 narrowed); request ids `<inst>-<n>`; the shared redial helper with jitter and
the 10 s stable reset; persisted refusal holds; the link record in the context item `link:<name>` and `clock:<name>`, the frame's
`playout` row and the copilot's facts. A pure `Timer.Core/PeerLink.cs` (transcript, proofs, MAC, window, machine) is shared with the
server side.

### Stagewatch — `/ws` node links and HTTP node routes (node for Patterns, PlayMap, AGMessage)

**Today.** TLS and plain on one port (`WebServer.cs:300-318`); plain accepted from the LAN unless Require HTTPS, which is off by default
(`:325-331`; `AppSettings.cs:261`); HTTPS itself can be turned off (`AppSettings.cs:259`; `MainViewModel.cs:955-956`); keys as bearer in
`Authorization` or `?pin=` (`:1150-1151,1059`); unknown keys counted in the PIN's per-address lockout (`:1077-1082,873`); a refused key on a
non-control socket gets the public frame silently (`:1154-1164,1216-1219`); `BindKey` first-come, in memory, ignored for contexts
(`Approvals.cs:63-72`, G15).

**Changes.** peers-not-screens SW-units as designed, plus: `link=1` sockets get the `link` message first and the verdict with `proof`,
`lid`, `floor`; `POST /api/link`; the `ShowBus` MAC on `/api/peers/context`, `/api/proposals*`, `/api/context`, `/api/message/*` and the
result route, with the 64-wide window; receipt dedupe per verified node id on HTTP and WS; bearer from a node still accepted over TLS or
loopback, plain from the LAN marks the key *seen in clear* (Settings → Remote → API keys row, amber context item, no grants — windows-for-peers
D4 generalised) and is refused from 1.4; wrong proofs in their own table; `Host`/`Origin` before loopback trust; key rows show bound node,
last use, seen in clear. With HTTPS off, Settings says "other show apps can't link with keys while HTTPS is off".

### Patterns — `StagewatchLinkService` (dialler to Stagewatch)

**Today.** TLS only when the hello names a certificate, else plain `ws`/`http` with the bearer key (`StagewatchLinkService.cs:238-242,269-272,290`);
a typed address dials TLS only when a pin is already held (`:235`); keyless TLS pins on the upgrade (`:284`); authenticated pin after
`/api/context` (`:350,380`); fixed `cid=patterns` (`:276`, G32); the stuck beat ignored (`:523-538`, G04); attempt reset after any normal
return (`:184-185`); 1 MB message cap (`:517`).

**Changes (one Patterns round; its number is the maintainer's).** Off this machine always TLS: try TLS on the hello's or the typed port,
never fall back to plain (amber "Hall A offers no TLS — the key stays here"); pin and floor per node id; `node=<ShowBusId>&t=patterns&link=1`
and the proof with the Stagewatch node key (`K` = SHA-256 of it); HTTP calls (context read and post, proposals, collect, results, the
speaker lane) carry the MAC on the link's `lid` — no bearer; the stuck flag (patterns-playmap-side decision 2); 60 s holds, persisted;
the stable reset; the key bound to `node:<id>` (decision 15). Every reader in the same round: STATE `bus.stagewatch` gains the link
record's fields at its end, the Eye's Stagewatch edge, the Nodes card, the Run strip, right-click menus (*Link again now*, *Accept
certificate*, *Lower floor*), Companion variables `stagewatch_link_state` / `_light`, the assistant's facts, Super Check's Remote row,
one journal row per transition.

### Patterns — wire 9697 and HTTP (node for Stagewatch, PlayMap, AGMessage)

**Today.** Plain TCP; STATE greeting before NONCE, pushes to every peer, queries unpaired (`ControlService.cs:356-363,501,763-774`, G45);
1.1 proof with a fresh nonce after each proof (`:453-457`), five wrong → closed (`:443-447,541`); plain AUTH only from loopback (`:428`);
loopback trusted (`:548`); a proved address trusted for OSC 12 h (`:556-560`); one token for every verb, a free text box
(`WebSection.axaml:38`), saved with the show (`PairingToken.cs:11-14`); HTTP takes `X-Patterns-Token`, the token itself.

**Changes.** TLS sniffed on 9697 (closes OPEN L55; the Companion module stays plain on loopback or LAN with its legacy proof); `LINK`,
`LINK PROOF` verbs and `BEAT STUCK` if the desk detects a stalled producer; `/api/link` and the MAC on `/api/context`, `/api/cmd`,
`/api/state`; an **App links** key table on the Nodes page — rows bound to a dialler id, scope `follow`/`drive`/`operate`, last use,
enrolled-from-code, *seen in clear*, Revoke — with the key material DPAPI-protected beside the settings, never in the show; enrolment from
the pairing token over TLS, and NEW TOKEN revoking enrolled keys; reads after pairing for untrusted connections (spec-1-3 §7.2, its
Round 90.2 ADR — this profile needs scope A at least); `X-Patterns-Token` arriving plain from the LAN marks the token *seen in clear* in
Super Check; a Super Check row for a typed token that is not 12 symbols of the alphabet. **For the maintainer's ADR:** the 12 h OSC
carry-over from a wire proof gives an address what a proof proved; this profile asks that it end (OSC ALLOW stays the operator's list).

### Patterns — twin, node asks, and the Show Bus node device to PlayMap

**Today.** The twin listens on every interface whatever the bind (`TwinService.cs:488,512`), proves mutually over both nonces
(`Twin.cs:160-196`) but carries the show and, by default, devices' passwords and the weather key in clear (`ShowState.cs:2080,2117-2123`;
`Twin.cs:380-398`); its key is 16 symbols ≈ 79 bits (`Twin.cs:231-247`). Node asks are one unpaired line per connection
(`NodesService.cs:185-227`). Lines devices to PlayMap have no handshake (G35).

**Changes.** The twin is a `twin`-carrier PeerLink: TLS pinned by the twin node's id, the transcript fields inside JOIN/CHALLENGE, the
dialler (standby or caller) proving first, the bind address honoured, `SendSecrets` refused on a plain twin link with words; node asks
over a paired `line` PeerLink; the Show Bus node device (patterns-playmap-side decision 19) speaks `LINK`, TLS where PlayMap offers it,
with a PlayMap link key. A walk row (twin over TLS, takeover) in QUALIFICATION, since the twin is a platform path.

### PlayMap — `StagewatchLink` (dialler to Stagewatch)

**Today.** TLS only when the hello names `tls` and `https` (`StagewatchLink.cs:66`); a typed address plain with approvals off
(`NetworkHost.cs:715-736`, G31); the bearer key (`:278-279`); keyless TLS pins on the upgrade (`:296-302`); `paired` = Live
(`NetworkHost.cs:315`); attempt reset on a close (`:236-237`); the key in plain `playmap.node.json` (`Pairing.cs:122-123`; `NodeHome.cs:39`).

**Changes.** TLS always off this machine with pin and floor; `link=1` proof; the MAC on every HTTP call; the key in `NodeCredentialStore`
(DPAPI, `Mapper/src/PlayMap.Engine/Devices/NodeCredentialStore.cs:9-16`); `paired` from the verdict (peers-not-screens PM-1); holds and the
stable reset; STATE `links.stagewatch` and preflight take the link record (appended); `AT` only from a TLS clock.

### PlayMap — `PatternsLink`/`WireClient` (dialler to Patterns) and the node side (9941, `/ws` lines, HTTP)

**Today.** The best dialler (§0.2); 4 MiB default line (`WireClient.cs:47`), 1 MiB for Patterns (`PatternsLink.cs:133`). The node side
sends NONCE first (`WireSession.cs:178-181`); the line channel answers pairing words only until paired, STATE after `OK paired`, 4401/429,
Origin and Host checks (`companion.md:6-23`; `HttpHost.cs:297-345`); `TrustedAddresses` pair without proof (`Pairing.cs:113-115`);
9941 answers STATE and CONTEXT before pairing (WIRE.md items 2 and 4, its owner's decision 17).

**Changes.** `WireClient` sends `LINK` first and uses the 1.1 proof only without a floor; TLS to 9697 when Patterns offers it; an
enrolled Patterns link key. Node side: `LINK` on 9941 and `/ws`; TLS sniffed on 9941; `/api/link` and the MAC; a link-key table;
`TrustedAddresses` kept only for legacy Lines devices, shown amber "trusted by address — not a proof", never for app links; reads after
pairing on 9941 put to the owner again (spec-1-3 decision 7). Process: round-3 ownership — `Net/Links/**`, `Net/Wire/**`, `Net/Http/HttpHost.cs`
and `Core/Bus/Pairing.cs` are round-2 files; the security fixes go to the owner as one exception request (patterns-playmap-side decision 6's
precedent) or land with the integration pass.

### AGMessage — `StagewatchLink` (dialler to Stagewatch), 0.5.0

**Today.** TLS only when the URL is `https`, the hello names `tls`, or a fingerprint is typed (`nodes.js:255`), else plain with the bearer
key (`:306,326`); probe-then-pinned TLS (`tls.js:204-225`); `connected` on the upgrade and the pin confirmed on any first `state` frame
(`nodes.js:328-335`, G43); attempt reset on open (`:329`); fixed `cid=agmessage` (`:323`); the key sealed at rest (`secretbox`).

**Changes.** agmessage decision 7 kept, plus: TLS always off this machine, pin and floor; `link=1` proof; the MAC in the one door
`_call`; the pin confirmed only after the node's proof; the stable reset; `app/lib/peerlink.js` (pure) with the vectors in node:test.

### AGMessage — `PeerLink` to Patterns and PlayMap, 0.5.0

**Today.** Polls `/api/context` every 5 s and posts `/api/cmd` with **the pairing token itself** in `X-Patterns-Token` / `X-PlayMap-Token`
(`peerlinks.js:23,121,129,149`), over plain HTTP unless the URL is `https` or the hello names `tls` (`:111-112`); pins after a context 2xx
(`:133`).

**Changes, before 0.5.0 ships.** The `http` carrier: `/api/link` with a link key enrolled from the code over TLS (scope `follow`, or
`drive` where the node's operator grants it); never the token in a header, never plain; floor; MAC; "drive" only verbs the node lists as
low or review **and** its scope allows — the node enforces, AGMessage's fence is extra. Until Patterns and PlayMap serve `/api/link`,
the link reads nothing from them and says "update Patterns to link from AGMessage" — the honest cost of not sending their operator token.

---

## 5. Conformance kit

**Vectors** — the ```` ```peerlink ```` block inside §13 (inside the hashed body, so spec-1-3's copy fence holds them in every tree with no
new mechanism). One JSON object per line, three families:
- *crypto rows* (`canon`, `key`, `proof`, `request`, `refuse`) — computed above, cross-checked in Python and Node;
- *parse rows* — each malformed `LINK` line, `link` message, `/api/link` body or `Authorization: ShowBus` header with the exact refusal;
- *machine rows* — `{"events":[["connect"],["link-ok",{"tls":true}],["silent",6000]],"state":"stale","light":"amber"}`: an event list
  in, the state, light and hold out, on injected time. Required rows: silence 6 s → stale, 12 s → gone; a refusal after an upgrade → held,
  and the attempt count not reset; verified + live 10 s → reset; a hello without `tls` for a pinned node → no dial; LINK refused as
  unknown with a floor → floor-refused; CERTIFICATE CHANGED → nothing sent; stuck beat → live + stuck; unmatched pong → no sample;
  33rd command → refused; a repeated request id → stored receipt with `repeat`.

How each tree consumes them:
- **Stagewatch:** `tests/Stagewatch.Tests` (its own runner) gains `PeerLinkVectors` reading `docs/SHOWBUS.md`'s block into the pure
  `Timer.Core/PeerLink.cs`; `tools/test.sh unit` runs it on Linux and Windows.
- **Patterns:** `tests/Patterns.Core.Tests/PeerLinkVectorsTests.cs` over `Patterns.Core/Services/PeerLink.cs` (Core keeps no package
  references); `tests/Patterns.App.Tests/PeerLinkWireTests.cs` for the wire and twin (each test that connects from loopback sets
  `ControlService.TrustLoopback = false` and restores it in `finally`).
- **PlayMap:** `tests/PlayMap.Core.Tests/Bus/PeerLinkVectorsTests.cs` over `PlayMap.Core/Bus/PeerLink.cs` (Core references only the
  runtime); `PlayMap.Net` tests for the carriers; both CI jobs.
- **AGMessage:** `app/test/peerlink.test.js` (node:test) over `app/lib/peerlink.js`.

**The scripted peer** — `tools/showbus/peerlink-peer.mjs`: one dependency-free Node 22 file (`node:net`, `node:tls`, `node:crypto`,
`node:http`), byte-identical in all four trees, its hash in the spec header and checked by each tree's ShowBusSpec test. It plays either
end on any carrier, honestly or as an attacker, with injected certificates and keys from 0600 files (never argv):

| id | mode | a dialler must | a node must |
|---|---|---|---|
| P1 | honest | reach `live`, verify the node's proof, publish the record | verify, prove, publish the peer row |
| P2 | wrong key | go `held` ≥ 60 s, not retry the key, keep public frames on `ws` | refuse with the constant words; count per (address, kid); a browser's PIN still works |
| P3 | other certificate at first use | refuse red, pin nothing | — (its proof fails) |
| P4 | strip `LINK` on a floored node | go `floor-refused`, send no legacy proof | — |
| P5 | forged hello without `tls`, or at another address | dial no plain, send no key | — |
| P6 | changed certificate | send nothing, probe ≤ every 60 s, accept on a press | — |
| P7 | replayed proof or nonce | — | refuse |
| P8 | replayed / out-of-window `n`, tampered body | — | 401 with `relink` |
| P9 | repeated request id after a reconnect | — | not run twice; stored receipt with `repeat` |
| P10 | silence | stale at 6 s, gone at 12 s, waits within ± 20 % | cut a reader silent 12 s |
| P11 | stuck beats | stay live, show no time | send `live:false` / `BEAT STUCK`, no state |
| P12 | forged pong; plain clock | no sample; no `AT` scheduled | — |
| P13 | oversize message; 33 commands | drop and count, amber with the size; refuse the 33rd | refuse a 4 KiB+ command |
| P14 | slow reader | — | latest-wins state, replies kept, cut at 12 s |
| P15 | unpaired flood; slow handshake | — | ≤ 4 unpaired per address; close at 10 s |
| P16 | canary secrets everywhere | no canary in any capture | no canary in any capture |
| P17 | revoke while live | go `held` at once | refuse live links at once, end sessions |
| P18 | enrol with a code over TLS; NEW TOKEN; code over plain | store `K_link`, prove with it | store, journal, revoke on a new code; never enrol on plain |
| P19 | key bound to another dialler | — | refuse, not counted |
| P20 | request via a local forwarder (AGMessage only) | — | not local |

**Which link must pass what:** every dialler (Stagewatch's playout links; Patterns' Stagewatch link, node device, twin standby/caller;
PlayMap's Stagewatch and Patterns links; AGMessage's two) passes P1–P6, P10–P13, P16, P17, P19 on each carrier it uses, and P9 when it
sends commands; every node end (Stagewatch's `/ws` and HTTP; Patterns' 9697, HTTP and twin main; PlayMap's 9941, `/ws` lines and HTTP)
passes P1, P2, P7–P9, P11 (where it has a stall detector), P13–P19, and P20 where it runs a forwarder. The interop lane
(`design/ecosystem-lane.md`) adds the cross-app rows: S7.1 (no secret anywhere) and "no app's address counted" become PeerLink rows,
plus one per real pair: "<dialler> links to <node> over <carrier>, mutual, pinned, floor set".

---

## 6. Migration

**Older peers keep working.** In 1.3 every node keeps its 1.2 doors: the line wires' `NONCE`/`AUTH-PROOF`, bearer keys over TLS or
loopback, Companion decks. A 1.3 dialler tries `LINK`, and on `ERR … unknown word` from a node with no floor uses the 1.1 proof, amber.
A 1.2 dialler talking to a 1.3 node works as before, except: its bearer key arriving plain from the LAN is marked *seen in clear*, and
Patterns' reads wait for pairing once its ADR lands (spec-1-3 decision 7).

**Order.**

| phase | what | where first | refuses from then on |
|---|---|---|---|
| 0 — client hygiene (no wire change) | never send a key, token or `AUTH <secret>` in clear off this machine; TLS chosen by pin and probe, never by the hello; pins confirmed only after an authenticated answer; matched pongs; holds and the stable reset; secret canaries | AGMessage 0.5.0 (on its branch now — cheapest), Stagewatch 2.18 (with playout links), Patterns' next round, PlayMap by exception request | each dialler: plain keyed links off this machine (they go keyless and say why) |
| 1 — the hub speaks PeerLink | Stagewatch: `link=1`, `/api/link`, MAC, dedupe, *seen in clear*, the vectors and the peer | Stagewatch | nothing new |
| 2 — diallers to the hub | AGMessage, then Patterns, then PlayMap link to Stagewatch by proof; floors recorded | in that order | legacy to a floored node |
| 3 — Patterns and PlayMap as nodes | TLS on 9697 / 9941, `LINK`, `/api/link`, key tables, enrolment; Stagewatch's playout links, PlayMap's Patterns link and AGMessage's PeerLink enrol | Patterns (closes L55), then PlayMap | the pairing token in an HTTP header from a PeerLink dialler; enrolment over plain |
| 4 — 1.4 | flip the defaults | all | bearer keys plain from the LAN; legacy proofs off this machine for app links; plain `line` app links off this machine where the node offers TLS |

The Companion modules (Patterns', PlayMap's) keep the legacy line proof until their maintainers adopt `LINK`; §13.15 names them.

---

## 7. Risks

- First use on a **keyless** link is still trust-on-first-use; only keyed links are protected by the binding. The short fingerprint is
  shown on both screens; nothing forces a person to compare it.
- Until Patterns' 9697 offers TLS, Stagewatch → Patterns stays a plain, legacy-proof link: STATE is readable and lines alterable on the
  path, and one recorded exchange is an offline target for the 59-bit token (typed ones far weaker). Mitigation: the amber state, the
  Super Check row, and L55 as the end date.
- Four hand-written implementations in two languages. Mitigation: no new primitive (HMAC-SHA256, SHA-256 and the platform's TLS only),
  the vectors, the same peer in every tree. No PAKE: declined on purpose (no platform implementation in .NET or Node).
- Channel binding is to the server certificate (tls-server-end-point), not to the TLS session: weaker than an exporter binding, but a
  middle machine never holds the node's private key, which is all the binding needs.
- The request MAC covers the exact request-target; a client library that re-escapes a URL breaks it. The vectors include an escaped
  target; each client signs the string it writes.
- Address spoofing on one L2 segment can still make a peer's address look locked; separate tables limit it to wrong proofs, and the same
  attacker can drop the traffic anyway.
- A Stagewatch with HTTPS off can no longer hold keyed links from other machines (fail closed). The words say how to turn HTTPS on
  (default on).
- AGMessage 0.5.0's `PeerLink` conflicts with agmessage decision 10; under this profile it reads nothing from Patterns or PlayMap until
  they serve `/api/link` — a visible feature loss in 0.5.0 if it ships first.
- Enrolled keys add secrets at rest on both ends; PlayMap keeps today's tokens in a plain JSON file and must move them to
  `NodeCredentialStore` in the same change.
- The scripted peer is one more byte-identical copy; only the interop lane sees a stale one (as spec-1-3 decision 10 says of the spec).
- §13 numbering conflicts with spec-1-3's §13; one decision in the master edit.
- Mapper's round-3 ownership may hold PlayMap's half until the integration pass, leaving PlayMap's typed Stagewatch links plain-keyed
  (G31) — unless the exception request is granted.
- Patterns' twin over TLS touches a platform path (takeover): unwalked until its walk row is filled.
- Traffic analysis and denial of service are not addressed.

## 8. Process notes

- **Stagewatch:** one release; `docs/SHOWBUS.md` changes only through spec-1-3's single edit; new code in Timer.Core (pure), Timer.Remote
  (node side), Timer.Integrations (diallers); the desk's Show network and Settings → Remote rows in step with right-click menus; tests on
  injected time; no `Environment.TickCount` (the lockout tables use `TickCount64` today, kept).
- **Patterns:** its own round, numbered by the maintainer: PLAN §NN with requester and evidence class ("a handed-in design"), REVIEW,
  CHANGELOG, README, REMOTE.md, OPEN rows (L55 closed; new rows for the OSC carry-over ADR, Companion module `LINK`, a typed weak token),
  QUALIFICATION walk rows for the twin; "every reader" — desk, Run strip, menus, STATE, the Eye, the wire, the deck, the assistant — in the
  same round.
- **Mapper:** round-3 ownership; the request names the files and quotes §4; Core stays runtime-only; `[SupportedOSPlatform("windows")]`
  on the DPAPI path (already so in `NodeCredentialStore`); a catalogue row for `LINK`.
- **AGMessage:** before 0.5.0 ships; node:test; `peerlink.js` pure.
