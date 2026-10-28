# PeerLink — layer 2 of the Show Bus: one link standard for every link in the four apps (reliability first)

Area: peerlink-profile. Angle: reliability first, from gaming netcode (a server tick is not a socket; snapshots with counters,
latest wins; inputs with ids, acked, never applied twice; min-RTT clock filters; jittered reconnects) and medical alarm practice
(a lead-off is a technical alarm, not a reading; an invalid value is blanked, never shown as current; alarm conditions need a
fixed delay and escalate; the alarm system watches itself; a late order is not delivered silently). Design only: no repo was
edited or built. Paths are repo-relative; line numbers are from the trees as read on 2026-10-25; `AGMessage@0.5.0` means AGMessage's
`AGMessage's 0.5.0 branch` (read with `git show`). Sibling designs are cited as `design/<area>.md`.

Gaps this area touches: G04 (stuck beat), G07 (receipts that complete), G08 (token in clear), G16/G17 (reports owed), G28
(clock offsets thrown away), G31 (typed address, paired = connected), G32 (fixed cids), G43 (connected on a public frame),
G44 (no conformance), G45 (reads before pairing, as a link rule). It closes none alone; it is the standard the units in the
sibling designs implement, plus the units it adds (§6).

---

## 0. The eight links today (evidence)

| # | link | carrier | beat / stale / dead | show liveness | redial | ids and receipts | clock | time source |
|---|---|---|---|---|---|---|---|---|
| 1 | Stagewatch `PatternsLink` → Patterns 9697 / PlayMap 9941 | TCP lines | ping 2 s, no stale state, dead 12 s (`Stagewatch/src/Timer.Integrations/PatternsLink.cs:43-44,287-301`) | none (Patterns/PlayMap send none) | 250 ms doubling to 5 s, **no jitter**, reset on every connect (`:189,209,229-230`) | `s<n>` restarting every run (`:167`); pending **evicted silently past 64** (`:169`); on drop every pending reported as a failure (`:225-226`) | `ClockOffsetEstimator` min-RTT of 8, offset only (`Stagewatch/src/Timer.Core/Resilience.cs:244-282`) | `DateTimeOffset.UtcNow` for t1/t4 (`PatternsLink.cs:206,325`) |
| 2 | Patterns `StagewatchLinkService` → Stagewatch `/ws` + HTTP | WS + side calls | beat 2 s / 6 s / 12 s (`Patterns/src/Patterns.Core/Services/ShowBusNetwork.cs:16-40`) | **ignored**: any message, pongs included, refreshes the last word (`Patterns/src/Patterns.App/Services/StagewatchLinkService.cs:522-537`; G04) | 0.5→5 s ±20 % (`ShowBusNetwork.cs:30-36`); attempt reset on any normal end (`StagewatchLinkService.cs:184-185`) | approvals `_ranDeskIds` cleared whole at 1000 (`:460,465`); result POST not retried (`:462-463`) | `LinkClock` min-RTT of 8 with delay (`Patterns/src/Patterns.Core/Services/LinkClock.cs:41-95`) | static `ShowClock` (`Patterns/src/Patterns.Core/Services/ShowClock.cs:12-30`), not injectable |
| 3 | PlayMap `StagewatchLink` → Stagewatch `/ws` + HTTP | WS + side calls | 2 s / 6 s / 12 s on an injected `IMonoClock` (`Mapper/src/PlayMap.Net/Links/StagewatchLink.cs:117-126,443-454`) | **honoured**: `live:false` sets `_stuck`, a frame clears it (`:380-382,420`) | ±20 % via `Jitter` (`:250`; `Mapper/src/PlayMap.Net/Common/Timing.cs:46-60`; `Mapper/src/PlayMap.Core/Bus/ShowBus.cs:232-236`); attempt reset on a normal return (`StagewatchLink.cs:236-237`) | `_ran` cleared whole at 1000 (`:538-541`); result POST not retried (`:551-552`) | estimator with drift (`Mapper/src/PlayMap.Core/Time/ClockOffsetEstimator.cs:17-111`), read only by tests (G28) | `IMonoClock` + `ShowWallClock` (`Mapper/src/PlayMap.Core/Time/Clocks.cs:7-61`) |
| 4 | PlayMap `PatternsLink` (over `WireClient`) → Patterns 9697 | TCP lines | 2 s / 6 s / 12 s on the options' clock (`Mapper/src/PlayMap.Net/Links/WireClient.cs:30-38,395-418`) | none | ±20 %, reset only when the peer said something (`:247-248`) | `pm<n>` restarting every run (`:153`); **no answer in 10 s and a drop are both reported as failures** (`:160-165,332,423-428`) | as 3 | injected |
| 5 | AGMessage `StagewatchLink` → Stagewatch `/ws` + HTTP | WS + side calls | ping every 1 s, 6 s / 12 s (`AGMessage@0.5.0 app/lib/nodes.js:30-32,227,356-365`) | stuck beat skips the refresh, but pongs refresh it (`:333-334`) and nothing shows stuck | ±20 % CSPRNG (`:203`); delay reset **on open** (`:329`) | `ag<12 hex>` (`:201`); a timeout after the POST is reported "not sent" (`:383-389`) | `ClockSampler` min-RTT of 8 (`AGMessage@0.5.0 app/lib/resilience.js:162-179`) | `Date.now()` (`nodes.js:329,333-334,358`) |
| 6 | AGMessage `PeerLink` → Patterns / PlayMap (new in 0.5.0) | **HTTP polling** `/api/context` every 5 s | stale after 12 s without an answer, no dead, no ping (`AGMessage@0.5.0 app/lib/peerlinks.js:19-20,123-142`) | none | none (the 5 s interval) | `agm<10 hex>`; a timeout after the POST is "not sent" (`:145-153`) | none | `Date.now()` (`:135,140`) |
| 7 | Patterns twin (main ↔ standby) | Patterns' twin port | beat 1 s from the UI thread's tick, silent 5 s (`Patterns/src/Patterns.Core/Services/Twin.cs:484-499`; `Patterns/src/Patterns.App/Services/TwinService.cs:595-627`) | the beat *is* the UI thread's, so a hung desk and a cut cable read the same (`Twin.cs:503-509` says so) | dial every 1 s, fixed (`Twin.cs:488-489`) | — | `TwinBeat` echo stamps, NTP arithmetic (`LinkClock.cs:5-28`) | injectable `Clock()` |
| 8 | Patterns node links (callers, stage timers → desk) | the twin carrier | as 7 | as 7 | as 7 | an `ACT` forward answers `Requested … sent` and never hears back (`Patterns/src/Patterns.App/Services/TwinService.Followers.cs:47-55`) | as 7 | as 7 |

The acceptors:
- **Stagewatch `/ws`**: a one-slot mailbox per socket, a 5 s send timeout (`Stagewatch/src/Timer.Remote/WebServer.cs:17-21,29`); a beat every 2 s and `{"type":"beat","live":false}` while the show thread is stuck past 3 s, with no frame until it recovers (`:1262-1297`; `Stagewatch/src/Timer.Core/ShowState.cs:214-226`); the pong is answered off the show thread with t2 = t3 (`WebServer.cs:1349-1355`); a receipt says `accepted` after 3 s but the command stays queued on the UI dispatcher and **runs whenever it gets there** (`Stagewatch/src/Timer.App/ViewModels/MainViewModel.Receipts.cs:21-27`); no request id is remembered, so a repeated id runs again; 64 connection slots, an extra one closed without a word (`WebServer.cs:276-281`); every HTTP answer closes its connection (`:943,1497`). Companion TCP appends ` STUCK` to its pong (`Stagewatch/src/Timer.Companion/CompanionServer.cs:389-399`).
- **Patterns 9697**: STATE greeting before NONCE (`Patterns/src/Patterns.App/Services/ControlService.cs:356-363`), pong answered on the connection's task (`:405-411`), one writer per peer with ≤ 256 replies and a 10 s write deadline, STATE latest-wins (`Patterns/src/Patterns.App/Services/WirePeer.cs:17-96`), five wrong tokens close the connection (`ControlService.cs:443-450,541`; PlayMap the same five, `Mapper/src/PlayMap.Net/Common/ConnectionLedger.cs:65`), ids echoed (`ShowBusNetwork.cs:240-276`), no dedupe. Its UI thread already beats to the supervisor, and the papers say honest stalls run up to several seconds ("the tick budget, a page switch, a save", `Patterns/src/Patterns.Core/Services/Resilience.cs:37-45`).
- **PlayMap 9941 / `playmap.lines.v1`**: `LineWriter` with ≤ 256 replies, a 10 s write deadline, one latest-wins slot per push kind and a PONG whose t3 is taken at write time (`Mapper/src/PlayMap.Net/Common/LineWriter.cs:15-80`); receipts with `accepted`/`applied` (`Mapper/src/PlayMap.Core/Bus/Pairing.cs:88-96`); devices already speak refused/unreached/sent/rejected/accepted/**observed** (`Mapper/src/PlayMap.Devices/Commands/CommandResult.cs:7-26`).

### Defects found while reading (each becomes a profile rule and a kit vector)

| F | defect | evidence | rule |
|---|---|---|---|
| F1 | "No answer" is reported as a failure: a command that may have run reads as refused | `WireClient.cs:160-165,332`; `PatternsLink.cs:225-226`; `nodes.js:387-389`; `peerlinks.js:151-152` | §13.9 outcome `unconfirmed` |
| F2 | A pending receipt is evicted without completing | `PatternsLink.cs:169` | §13.11.1 |
| F3 | Request ids restart every run, so no receiver can remember them safely | `PatternsLink.cs:167`; `WireClient.cs:153` | §13.9.1 run-unique ids |
| F4 | A pong refreshes the link while the show is stuck, so a stuck Stagewatch looks live | `StagewatchLinkService.cs:522`; `nodes.js:334` | §13.7.5 |
| F5 | The ping waits behind HTTP side calls of up to 4 s each, so clock samples stop while a desk is slow | `StagewatchLinkService.cs:328-355`; `StagewatchLink.cs:456-472` | §13.11.4 side-call lane |
| F6 | The backoff resets on a mere connect: an acceptor that accepts and closes is redialled every 0.25–0.5 s | `PatternsLink.cs:209`; `nodes.js:329`; `StagewatchLinkService.cs:184-185`; `StagewatchLink.cs:236-237` | §13.12.2 |
| F7 | Liveness and clock on a wall clock that can step | `nodes.js:329,358`; `peerlinks.js:135,140`; `PatternsLink.cs:206,325` | §13.8.1, §13.16 |
| F8 | Thresholds and boundaries differ: 12 s stale with no dead (AGMessage `PeerLink`); `<=` against `<` at the boundary | `peerlinks.js:19-20`; `ShowBusNetwork.cs:38` vs `ShowBus.cs:226-229` | §13.7.2 exact boundaries |
| F9 | No jitter at all on Stagewatch's link | `PatternsLink.cs:189,229-230` | §13.12.1 |
| F10 | "Ran" sets are cleared whole, while desk ids restart per desk run: an id can run twice or be skipped | `StagewatchLinkService.cs:465`; `StagewatchLink.cs:540-541`; design/cross-app-proposals.md D5 | §13.9.5, §13.9.7 |
| F11 | Reports owed are sent once | `StagewatchLinkService.cs:462-463`; `StagewatchLink.cs:551-552`; G16 | §13.9.7 |
| F12 | A node's forwarded action never hears its outcome | `TwinService.Followers.cs:47-55` | §13.9 on the twin |
| F13 | A Stagewatch key rides plain `ws://` to another machine when the hello names no TLS; AGMessage's `PeerLink` sends `X-Patterns-Token` / `X-PlayMap-Token` over plain HTTP | `StagewatchLinkService.cs:269-276`; `StagewatchLink.cs:278-284`; `nodes.js:326`; `peerlinks.js:110-112,121` | §13.4, §13.5 |
| F14 | A line-wire acceptor never proves itself: a follower takes a blackout from whoever answers at the address | `Mapper/src/PlayMap.Net/Links/PatternsLink.cs:217-247` → `Mapper/src/PlayMap.Engine/Host/NetworkHost.cs:696-699` | §13.3.4 PROVE |
| F15 | Stagewatch writes Patterns commands synchronously from the show thread | `PatternsLink.cs:172-176` called from `Stagewatch/src/Timer.App/ViewModels/MainViewModel.Patterns.cs:71-83` | §13.11.5 |
| F16 | A command queued behind a stuck show thread runs late without a word | `MainViewModel.Receipts.cs:21-27` | §13.9.3 `expired`, `lateMs` |
| F17 | Dropped and malformed lines are counted and never shown | `PatternsLink.cs:64,88-89,274` | §13.11.3 |

### Measured here: does today's jitter stop a room from stampeding?

`scratchpad/design/peerlink-stampede.py` (output beside it) simulates N links losing one acceptor at once and redialling until it
is back, 2,000 trials each:

| case (acceptor back after 6 s, 32 links) | most connects in any 250 ms (median / p95) | all back within (median) |
|---|---|---|
| ±20 % around 0.5 s doubling to 5 s (Patterns, PlayMap, AGMessage today) | 9 / 12 | 2.5 s |
| ×[0.5, 1.0] ("half" jitter) | 6 / 8 | 4.5 s |
| ×[0, 1.0] ("full" jitter) | 6 / 8 | 4.3 s |
| no jitter, 250 ms doubling (Stagewatch's link today) | **32 / 32** | 1.75 s |

With cable-pull detection (silence spread over 10–12 s) the jitterless rule still lands 8–11 of 32 in one window. Conclusion: the
±20 % the three apps already use spreads a room enough at this scale (a few connects per 250 ms costs milliseconds) and gets every
link back about 2 s sooner than wider jitter; the defect is the link with no jitter (F9) and the reset on a mere connect (F6). The
profile keeps ±20 % and fixes those two. No governor is added for the burst of side calls after a reconnect: it is unmeasured,
Stagewatch's HTTP connections are short-lived (`Connection: close`), and the lab line LP-2 (§6) measures it first.

---

## 1. Decisions

1. **Two livenesses, never one.** The link is alive when messages arrive; the show is alive when the sender's single writer turns
   over. The sender decides "stuck" against its own declared threshold; the reader honours it and never infers it. *Why:* G04 and F4:
   pongs answered off the show thread kept a stuck Stagewatch green in Patterns and AGMessage, while PlayMap, which separates the two,
   was right (`StagewatchLink.cs:380-382`). Medical practice separates a technical alarm (lead off) from a reading. *Alternative:*
   one liveness from frames only — a paused show sends no frames and would look dead.
2. **The numbers stay 2 s / 6 s / 12 s, with exact boundaries.** live below 6000 ms of silence, stale from 6000, dead from 12000;
   evaluated at least every second. *Why:* already the contract (`Stagewatch/docs/SHOWBUS.md:77-80`) and spec-1-3's numbers; three
   missed beats before attention and six before action is the alarm-delay pattern; one boundary everywhere ends F8.
3. **The stuck threshold is the sender's, declared, 1–10 s, default 3 s.** *Why:* Stagewatch measures 3 s (`ShowState.cs:217`), while
   Patterns' own papers name honest UI stalls of several seconds (`Resilience.cs:41-45`); one global number would either miss
   Stagewatch's stalls or cry wolf on Patterns' saves. The node page states its writer and the reason for its number.
4. **The alarm system watches itself.** An evaluator that did not run for over 2 s declares nothing dead on that run, pings at once
   and decides 2 s later. *Why:* a laptop lid, a debugger or a GC pause is this node's gap, not evidence about the peer.
5. **Seven outcomes, `unconfirmed` among them.** not-sent, refused, accepted, applied, observed, expired, unconfirmed. *Why:* F1: an
   unanswered GO may have run; calling it failed invites a second GO. `observed` comes from a frame counter the receipt names, so a
   connector knows the effect reached the published state it reads (gaming's "server ack of the input sequence").
6. **Run-unique ids, receiver-side dedupe, no blind resends, and a RECEIPTS question after a drop.** *Why:* gaming resends inputs
   because the server dedupes; medicine never repeats an order without confirmation. Both hold here: an acceptor that remembers ids
   makes a duplicate harmless; a connector that asks "what became of s17" after a reconnect turns `unconfirmed` into a fact without
   risking a second GO. Reports owed (§11 results) are the one class retried, because their receiver is idempotent by design.
7. **A late command is not run silently.** An acceptor runs a command at most `expireMs` (declared, default 5000, at most 10000)
   after it read it, and says `lateMs` when it ran over a second late. *Why:* F16; a GO that lands 20 s later when a stuck thread
   recovers is worse than none. Timed verbs (`AT`) are the only way to ask for a later instant.
8. **Bounds everywhere, and nothing blocks the beat.** ≤ 32 in flight (playout-links D5), ≤ 256 replies, one push slot per kind,
   write deadlines, a side-call lane, the show writer off the socket. *Why:* F2, F5, F15.
9. **Reconnect: keep ±20 %, add it where missing, reset only after a verified link.** *Why:* the measurement above; F6, F9.
10. **Clock with quality.** Publish offset, error bound (rtt/2), age and samples; "fit for a tolerance" rather than a single
    "good"; CLOCKS APART only when the offset minus its error exceeds 2 s. *Why:* G28; playout-links' AT rule (sample ≤ 10 s old, rtt ≤
    lead) generalised; an alarm on an uncertain reading is a false alarm.
11. **Negotiated per connection, never from the hello.** Profile 0 is Show Bus 1.2 as built; profile 1 is this section. New shapes go
    only on connections that asked for them. *Why:* old readers parse `OK PONG` as exactly four words
    (`Patterns/src/Patterns.Core/Services/ShowBusNetwork.cs:299-307`; `WireClient.cs:383-388`) and a HELLO extension would land in the
    name the proof covers (`Patterns/src/Patterns.Core/Services/CompanionWords.cs:50-56`); the hello is unauthenticated.
12. **Proof both ways on line wires; a bearer secret only over pinned TLS or loopback.** *Why:* F13, F14. The acceptor's proof uses a
    key derived from the token, so it can never be replayed as a connector's proof (§2 shows the reflection it prevents), and it is
    answered only after pairing.
13. **One state model for every reader; safety facts latch.** *Why:* "every reader" (all four briefs); medical latching: a blackout
    followed from a desk stays held, with its age, until the desk says otherwise or a person releases it — PlayMap already does this on
    a drop (`NetworkHost.cs:696-714` releases only on stop).
14. **Pure link machines on injected time and injected randomness, held by one kit of vectors and scripts.** *Why:* the four trees
    share no package (Patterns.Core references none; Mapper's Core only the runtime); the shared truth is data. PlayMap already has the
    seams (`IMonoClock`, `Mapper/tests/Shared/ManualTime.cs`).
15. **Numbering: PeerLink is §13; spec-1-3's numbers block becomes §14.** *Why:* the profile is prose the block serves, and the
    machine-read block is easiest to parse as the body's last section. Cost: 23 "§13" references in five unlanded designs become
    "§14" (spec-1-3.md 18, ecosystem-lane.md 2, patterns-playmap-side.md 2, cross-app-proposals.md 1); spec-1-3 owns the edit.

---

## 2. The profile (normative text for Show Bus 1.3 §13; the rows for §14 follow)

> ## 13. PeerLink — one link between two nodes
>
> Discovery (§2) says a node exists. A **PeerLink** is one authenticated connection between two nodes that carries this contract's
> messages (§3–§6, §11, §12). This section is the same for every link of every node. MUST, MUST NOT, SHOULD and MAY are used as in
> RFC 2119. Numbers in `code` are §14 keys; times are on the node's monotonic clock unless called wall time.
>
> ### 13.1 Words
>
> - The **connector** dials; the **acceptor** answers. Either may be the follower.
> - A **link** is a line wire (TCP, or `playmap.lines.v1` over WSS) or a node link on a WebSocket (§7.1), together with the HTTP calls
>   that ride beside it to the same node (**side calls**). HTTP polling alone is never a link.
> - A node's **show writer** is its single writer of the show: Stagewatch's show thread, Patterns' desk (UI) thread, PlayMap's engine
>   control lane, AGMessage's event loop. Each node's page names it and its `stuckMs` with the reason for the number.
> - **Heard**: a complete message arrived on the connection. **Verified**: §13.3.2. **Profile**: §13.6.
> - Decks (the Companion module), browsers, windows and Slides agents are not PeerLinks; the acceptor rules below that say "every
>   connection" apply to them too.
>
> ### 13.2 Transports
>
> | connector → acceptor | carrier | the connector proves | secret on the wire |
> |---|---|---|---|
> | any node → Stagewatch | `/ws?node=<id>&t=<kind>&peerlink=1` (§7.1) over pinned WSS on the hello's `https` door, or plain only to loopback; side calls HTTPS to the same door | a node key (`Authorization: Bearer`); the verdict comes first | the key, only inside pinned TLS or to loopback |
> | any node → Patterns | line wire, TCP 9697 | `AUTH-PROOF`; Patterns answers `PROVE` | none |
> | any node → PlayMap | `playmap.lines.v1` over WSS on 9940 when its hello names `tls` or its id is pinned; otherwise line wire, TCP 9941 | `AUTH-PROOF`; PlayMap answers `PROVE` and shows its pinned certificate | none |
> | Patterns ↔ Patterns (twin, nodes) | Patterns' twin link | its own; its beat and silence numbers are its own (failover), and 13.7.4–13.7.5, 13.9 and 13.15 apply | its own |
>
> - A node MUST NOT draw a peer as linked (a green link light) from HTTP polling alone. A *reader* that only polls a context shows the
>   context's age, grey at best, and sends no command.
> - A plain carrier is allowed where no secret rides on it; the link carries the `plain` flag (§13.13).
>
> ### 13.3 Identity and pairing by proof
>
> 1. **Each end names itself by its Show Bus id.** The connector in `@pl PEERLINK 1 <id> <kind>` or in `/ws?node=<id>&t=<kind>`; the
>    acceptor in its answer's `self` (§13.6). A hello is a hint: it chooses where to dial and never verifies anything.
> 2. **Verified** means one of: a key bound to that id answered ok (§13.4); the connector proved the acceptor's token (`AUTH-PROOF`);
>    the acceptor proved the token (`PROVE`) or presented the certificate pinned for that id (§13.5). A link is verified **both ways**
>    when each end holds such evidence of the other, **one way** when only the acceptor does.
> 3. **The connector's proof.** An acceptor with a token says `NONCE <32 lowercase hex>` as its first line (§7.2); a connector that
>    heard none asks `@pn NONCE`. `AUTH-PROOF <hex>` = lowercase hex of HMAC-SHA256(K, nonce ‖ name), K = the UTF-8 of the token in
>    upper case with dashes and spaces removed, name = the connector's `HELLO` name. A nonce is used once, right or wrong. Nonces come
>    from a CSPRNG.
> 4. **The acceptor's proof** (profile 1). After `OK paired` the connector sends `@pv PROVE <cnonce>`, cnonce = 32 lowercase hex,
>    fresh per connection from a CSPRNG. The acceptor answers `OK @pv <hex>` = lowercase hex of HMAC-SHA256(Kₐ, cnonce ‖ acceptorId),
>    Kₐ = HMAC-SHA256(K, "showbus/peerlink/acceptor/1"). An acceptor MUST NOT answer `PROVE` on a connection that has not paired. An
>    open acceptor answers `ERR @pv open — this node asks for no token`. A connector MUST verify the answer before it acts on any fact
>    the acceptor publishes (a blackout, a clip's end, a time); a wrong answer makes the link `refused` ("the node at <address> does not
>    hold the token"), and the connector sends nothing more on it and follows none of its facts.
> 5. **Plain `AUTH <token>`** is sent only on a socket whose remote address is loopback, and only to an acceptor older than Show Bus 1.1
>    (no `NONCE`, and `ERR` to `@pn NONCE`). Anywhere else nothing is sent and the link is amber: "older than Show Bus 1.1 — it pairs only
>    by sending its token in clear".
> 6. **A refused proof stops proofs.** After `ERR … wrong token` (or close 4401) a connector sends no proof, and does not redial, until
>    its token setting changes. An acceptor closes a connection after `proof.wrong.close` wrong proofs and MAY lock the address out for
>    60 s (`429` at an upgrade, or `ERR` words on a line wire); wrong proofs count toward the lockout, unknown keys do not (§13.4).
> 7. **The node it meant.** When the connector expected an id (from its settings or the hello it dialled) and the acceptor's `self.id`
>    differs, the link is `other-node`: amber, no command sent, until a person accepts the new id.
>
> ### 13.4 Keys, bindings and holds
>
> - One node, one key (§7): a node key speaks only for its bound id, the binding is kept with the key, and a node link hears the
>   verdict before any frame and again whenever it changes; the latest verdict wins.
> - A bearer credential (a key, the PIN) is sent only inside TLS whose certificate matched the pin, or to loopback. A connector that
>   would have to send it in clear sends none, reads the public view, and carries the `keyless` flag: "Stagewatch at <address> offers
>   no TLS — the key stays on this machine".
> - A `401`, or a verdict `ok:false`, holds every keyed call and every keyed redial for its `retry` seconds (60), or `Retry-After` for a
>   `429`. The hold is recorded before any I/O and kept on every path out of the connection. A `403` binding refusal holds nothing; its
>   words name both ids.
> - Unknown or revoked keys never count toward an address lockout; wrong PINs, wrong proofs and wrong view passes do.
>
> ### 13.5 TLS and pinning
>
> §10 applies, and:
> - The pin key is `node:<id>` when the id is known, else `addr:<host>:<port>`.
> - A pin is set only after the first authenticated success over that certificate: a verdict `ok:true` with a scope other than
>   `none`, `OK paired` (and `PROVE` when offered), or an authenticated `2xx`. Never on the connect, and never on a public frame.
> - A changed certificate is refused with CERTIFICATE CHANGED (red); nothing — no key, no proof, no line — is sent until a person
>   accepts it.
> - **No downgrade.** Once a node id is pinned its links dial TLS only. A hello without `tls` leaves the link amber ("<name> offers no TLS
>   now — not dialling plain"); it is never dialled plain.
> - The certificate is checked before any byte of a secret or a proof leaves the connector.
>
> ### 13.6 Profile negotiation
>
> - A connection speaks **profile 0** (the shapes of Show Bus 1.2) or **profile 1** (this section). Shapes marked (1) below are sent only
>   on a profile-1 connection.
> - **Line wire.** Right after `HELLO`, before pairing, the connector sends `@pl PEERLINK 1 <id> <kind>`. A profile-1 acceptor answers
>   ```
>   OK @pl {"peerlink":1,"self":{"t":"patterns","id":"9f8e7d6c","inst":"a41f09c2"},"beatMs":2000,"stuckMs":3000,"expireMs":5000,"idem":256,"final":true,"prove":true}
>   ```
>   `ERR @pl …`, an untagged `ERR` while it is asked, or no answer within `receipt.deadline.ms` means profile 0. `PEERLINK` joins the
>   words a connection may send before pairing (§7.2); its answer carries nothing of the show.
> - **Node link.** The connector adds `peerlink=1` to its `/ws` request; a profile-1 Stagewatch appends
>   `"link":{"peerlink":1,"self":{"t":"main","id":"…","inst":"…"},"beatMs":2000,"stuckMs":3000,"expireMs":5000,"idem":256,"final":true}`
>   to the verdict. No `link` means profile 0.
> - A connection speaks the lower of what was asked and what was offered; an acceptor answers the highest profile it speaks that is not
>   above the one asked. An argument of `PEERLINK` the acceptor does not know is refused, never defaulted.
> - Nothing is decided from the hello's `proto` or `caps`.
> - A profile-0 peer keeps working with what it gives; its link carries the `legacy` flag: "older peer — it cannot say if its show is
>   stuck, and keeps no receipts across a drop".
>
> ### 13.7 Liveness — the link and the show
>
> 1. **Beats.** Each end hears the other at least every `link.beat.ms` (2000). The connector pings every `ping.min.ms`–`ping.max.ms`
>    (1000–2000); the acceptor answers each ping at once and never on its show writer; a WebSocket acceptor also sends a frame or
>    `{"type":"beat"}` at least every 2000 ms.
> 2. **The link.** Every heard message sets `heardAt`. With s = now − `heardAt`: **live** when s < 6000 (`link.stale.ms`), **stale** when
>    6000 ≤ s < 12000, **dead** when s ≥ 12000 (`link.drop.ms`): the connector closes and redials (§13.12), the acceptor closes. The state
>    is evaluated at least every `link.check.max.ms` (1000). A stale link is live again with its next message on the same connection.
> 3. **Its own gap.** When the evaluator's consecutive runs are more than `link.selfgap.ms` (2000) apart — this node was paused, asleep
>    or starved — it MUST NOT move any link to dead on that run. It pings each link at once, decides again 2000 ms later, and logs one
>    line naming its own pause.
> 4. **The show.** Every beat says whether the sender's show writer turned over within its `stuckMs` (declared in §13.6, between
>    `show.stuck.min.ms` and `show.stuck.max.ms`, 3000 by default):
>    - WebSocket: `{"type":"beat","live":false}` while stuck, and no frame until it recovers; a frame, or a beat without `live`, means live;
>    - line wire (1): the fifth word of the pong, `OK PONG <t1> <t2> <t3> live` or `… stuck`;
>    - the connector (1): `PING <t1> live|stuck`, `{"cmd":"ping","t":…,"live":false}`, so the acceptor can show a stuck follower.
>    The flag is read from a stamp the show writer sets each turn; the answer is written off the writer.
> 5. **Reading it.** Show liveness is kept apart from link liveness. A stuck beat refreshes `heardAt` and sets **stuck**; only a frame,
>    or a beat or pong that says live, clears it; a pong without the word (profile 0) clears nothing. While the peer is stuck, stale or
>    dead, a counting value from it is not shown (`?` with the reason) and nothing taken from its last frame is presented as current.
> 6. **A running count** is shown only while its newest frame is younger than `count.stale.ms` (3000), counted on locally from that
>    frame's arrival on the monotonic clock; a sender sends a frame at least every `count.frame.every.ms` (1000) while a count runs.
>
> ### 13.8 Clock offset and its quality
>
> 1. Every ping is an exchange: t1, the connector's clock as it writes the ping; t2, the acceptor's as it reads it; t3, the acceptor's as
>    it writes the answer (at the write, not when it was queued); t4, the connector's as it reads the answer. Each node's clock is wall
>    time read once at its start plus monotonic time since; it never steps.
> 2. offset = ((t2 − t1) + (t3 − t4)) / 2; rtt = (t4 − t1) − (t3 − t2). A sample with rtt < 0, t4 < t1, t3 < t2 or rtt > `clock.rtt.max.ms`
>    (60000) is refused.
> 3. The last `clock.window` (8) samples are kept; the one with the smallest rtt is believed. A link publishes `offsetMs`, `errMs` =
>    ⌊(rtt + 1) / 2⌋ + 1 of that sample, `ageMs` since its t4, and `samples`.
> 4. **Quality.** `unknown` with no sample; **fresh** while `ageMs` ≤ `clock.fresh.ms` (10000); **fit for** a tolerance T while fresh and
>    `errMs` ≤ T. A timed verb (`AT`) uses the offset only when it is fit for that verb's tolerance; otherwise it falls back and says so.
> 5. **CLOCKS APART** when |`offsetMs`| − `errMs` > `clock.apart.ms` (2000).
> 6. A display moved onto a peer's clock changes its applied offset only when the estimate moves by more than max(`clock.apply.deadband.ms`
>    (50), `errMs`).
> 7. The window is emptied when the peer's `inst` changes and when this node re-reads its own wall time.
>
> ### 13.9 Commands — request ids, receipts and outcomes
>
> 1. **Ids.** A request id is `[A-Za-z0-9_-]{1,32}` (§3). (1) A connector's ids are unique across its restarts: `<prefix><n>-<run>`, run =
>    `request.run.chars` (6) characters of `[a-z2-7]` drawn once per process start from a CSPRNG (`s17-k3m9qa`).
> 2. Every command on a link carries an id. An acceptor older than Show Bus 1.1 echoes none; such a ticket ends at once as
>    unconfirmed, "sent — this node gives no receipts", and only its published state can say more.
> 3. **Outcomes.** Each command sent has one ticket. Its outcome only moves forward: accepted → applied → observed; unconfirmed →
>    applied, refused, expired or observed when a final receipt, a `RECEIPTS` answer or its published state says so. not-sent, refused,
>    expired and observed never change:
>    - **not-sent** — nothing left this node: the link is not live, the peer's show is stuck, 32 are in flight, or a local refusal.
>    - **refused** — the acceptor said no, with its words.
>    - **accepted** — the acceptor took it and had not run it within `receipt.deadline.ms` (3000).
>    - **applied** — it ran on the acceptor's show writer; `effect` says what changed; (1) `frame` is the counter of the first state frame
>      published after it ran, and `lateMs` is present when it ran more than 1000 ms after it was read.
>    - **observed** — applied, and the connector then received a frame whose counter is at least `frame` (or the acceptor read the effect
>      back before answering).
>    - **expired** (1) — the acceptor did not run it, because it would have run more than its `expireMs` after it was read.
>    - **unconfirmed** — it was written and no final answer came: the link dropped, or `receipt.final.ms` (30000) passed. It is unknown,
>      never shown as refused and never as applied; the words are "unknown — <peer> did not answer; look at its state".
> 4. **Deadlines.** An acceptor answers within 3000 ms with applied, refused or accepted. An accepted command gets one more receipt, its
>    final one, carrying `"final":true`, within 30000 ms. An acceptor never runs a command more than `expireMs` after it read it
>    (`cmd.expire.default.ms` 5000, at most `cmd.expire.max.ms` 10000); a later instant is asked for only through `AT`.
> 5. **Idempotency** (1). An acceptor keeps the final receipts of the last `idem.keep` (256) ids of each verified peer — of each
>    connection, for one not verified — for `idem.keep.ms` (600000). An id it holds is never run again: its receipt is answered again
>    with `"replay":true` (or `accepted` while it still runs).
> 6. **No blind resends.** A connector MUST NOT send a command again once any of its bytes may have reached the acceptor. After a
>    reconnect it MAY ask (1) `@rq RECEIPTS <id> …` (at most `receipts.ask.max`, 32) or `{"cmd":"receipts","args":[…],"id":"rq"}`,
>    answered `OK @rq {"inst":"a41f09c2","receipts":{"s17-k3m9qa":{…receipt…},"s18-k3m9qa":"unknown"}}`. "unknown" from an acceptor
>    whose `inst` changed stays unconfirmed. Sending the command again is a person's act, with a new id.
> 7. **Reports owed** — a §11 result, an acknowledgement — are idempotent at their receiver (a second one is answered 409 "settled",
>    which counts as delivered) and are retried by their sender from `report.retry.first.ms` (3000) doubling to `report.retry.max.ms`
>    (30000) with ±20 % jitter, for `report.retry.for.ms` (30 minutes), recorded before the reported act runs.
> 8. **One row per attempt.** Each command that reached the socket leaves exactly one row in the connector's journal when its ticket
>    first settles (anything but accepted), and one more only when an unconfirmed ticket is later resolved or an applied one is
>    observed (the "seen" row).
>
> ### 13.10 State — ordering and latest wins
>
> 1. Every state stream carries a frame counter that rises with every published frame (Stagewatch `seq`, Patterns `rev`, PlayMap `seq`;
>    §4). On one connection a reader keeps a frame only when its counter is above the last it kept. The first frame after a reconnect is
>    kept whatever its counter; a lower counter than the last connection's, or a new `inst`, means the peer restarted, and every anchor
>    and held value taken from it is dropped.
> 2. **Latest wins.** A reader keeps one frame per stream, the newest. A sender keeps one pending push per kind and replaces it with a
>    newer one; it MAY skip a push identical to the last it wrote. Frames are never queued or replayed.
> 3. Commands, replies and receipts are never coalesced or dropped by latest-wins. Replies leave in the order their lines came;
>    receipts are matched by id, never by order.
> 4. Counting values travel as durations with a discontinuity counter inside the object they count (§4).
>
> ### 13.11 Bounds and backpressure
>
> 1. **Connector.** At most `link.inflight.max` (32) commands wait on one link; the next is not-sent ("32 commands are waiting on <peer>'s
>    answers"). Nothing is queued for later, and no ticket is ever evicted.
> 2. **Acceptor, every connection.** One writer. At most `link.replies.max` (256) replies wait; past that the connection is cut with the
>    words "not reading its answers". One pending push per kind. A write that does not finish within its deadline (at most
>    `link.write.deadline.max.ms`, 10000) cuts the connection.
> 3. **Size.** A reader accepts a message of at least `link.message.read.min.bytes` (262144). A larger one it does not take whole is
>    counted and shown amber with its size (`oversize`); the link stays up. A reader MAY read a fixed list of fields from the first part
>    of an over-long state message, saying which it could not reach, but never treats a cut message as complete.
> 4. **Side calls** run in their own lane: never in the beat's path, at most one in flight per kind, `link.sidecall.timeout.ms` (4000) each,
>    a newer post replacing one not yet sent. Their failures are counted on their own item (§13.13) and never move the link's liveness.
>    They reuse one keep-alive connection where the acceptor keeps one.
> 5. The show writer never writes to, reads from or waits on a socket.
> 6. An acceptor at a connection or rate ceiling says so before it closes (a line of words, or `429` with `Retry-After`); a connector
>    takes `Retry-After` as a hold (§13.12).
>
> ### 13.12 Reconnect, jitter and holds
>
> 1. **Wait before redial n** (n counts failed dials since the link was last verified live, from 0): base = min(`link.redial.max.ms`,
>    `link.redial.first.ms` × 2ⁿ) = min(5000, 500 × 2ⁿ); wait = ⌊base × (800 + ⌊400u⌋) / 1000⌋ ms, u uniform in [0, 1) from a generator
>    seeded once per process from a CSPRNG (a test injects u). So a first redial waits 400–599 ms and none waits more than 5995 ms.
> 2. **n returns to 0 only after a verified link**: a verdict `ok:true`, `OK paired` / `OK open`, `PROVE` verified, or the first frame of a
>    link that needs no credential. A connect alone resets nothing, so an acceptor that accepts and closes is backed off.
> 3. **Holds replace the wait:** `401` / `ok:false` → its `retry` (60 s); `429` → `Retry-After`; a person's disconnect at the acceptor →
>    30 s; a refused proof → no redial until the token changes; CERTIFICATE CHANGED → a dial at most every 10 s to show reachability, with
>    no secret and no proof.
> 4. **Where.** The connector dials the address it uses while the hello still names it (§2), and a peer on its own machine at 127.0.0.1
>    first, then at the heard address.
> 5. **Nothing crosses a drop** (§4): a ticket not yet written becomes not-sent, a written one unconfirmed.
>
> ### 13.13 Link states — degraded and refused, and what each side shows
>
> | state | meaning | light | commands | a count from it |
> |---|---|---|---|---|
> | `off` | not configured | grey ○ | not-sent | none |
> | `finding` | no address heard yet | grey ○ | not-sent | `?` |
> | `dialling` | connecting; `attempt`, `nextMs` | amber △; red ✕ when the show needs it and it has been down 12 s or more | not-sent | `?` |
> | `pairing` | connected; proving and negotiating | amber △ | not-sent | `?` |
> | `live` | heard within 6 s, verified as its carrier requires | green ✓ | sent | shown |
> | `stale` | heard 6–12 s ago | amber △ "quiet for N s" | sent, the ticket marked quiet | `?` |
> | `stuck` | live, but the peer's show writer is stuck | amber △ on the link; red ✕ on what depends on it | not-sent | `?` |
> | `held` | a hold is running (§13.12.3) | amber △ with the time left | not-sent | `?` |
> | `refused` | a proof, key, certificate or node was refused | red ✕ with the fix | not-sent | `?` |
>
> - **Flags**, amber △ with their words, blocking nothing by themselves: `plain`, `keyless`, `legacy`, `open`, `other-node`, `clock-apart`,
>   `clock-unfit`, `oversize`, `read-only`, `conflict` (§2).
> - **"The show needs it"** means configured: a followed, playout or granted link. A link only seen is amber at worst.
> - **Every reader reads the same fields** — the desk and its right-click menus, STATE, the context's `link:` items, the Eye, the
>   Companion module, the AI's facts: `state`, `flags`, `since` (wall ms), `heardMs`, `attempt`, `nextMs`, `show` (`live`|`stuck`|`unknown`),
>   `clock` {`offsetMs`, `errMs`, `ageMs`, `samples`}, `profile`, `verified` (`none`|`one-way`|`both`), `counts` {`unconfirmed`, `expired`,
>   `replayed`, `oversize`, `malformed`}, `words`. No reader derives a link fact of its own.
> - **The acceptor** lists each connected node with its verified id, profile, its show flag and when it was last heard.
>
> ### 13.14 Safety on loss
>
> - A fact a node follows for safety (a blackout, a hold) is **held** when the link goes stale, stuck or dead, and shown with its age
>   ("holding Patterns' blackout since 20:14:03 — link lost"). It is released by the peer's live word or by a person, never by silence.
> - A counting value is never held (§13.7.5).
> - What the link feeds to air goes to that output's own loss state (a window's `onLoss`; black for a drawn window), never to the last
>   picture of a live count.
> - No command is sent to a stuck show (§13.13), and no acceptor runs a command later than its `expireMs` (§13.9.4).
>
> ### 13.15 Logging and secrets
>
> - Tokens, keys, the PIN, view passes, nonces with their proofs, `Authorization` headers and any URL holding `pin=`, `key=` or `win=` ride
>   in no log line, journal row, STATE, context, frame or hello. An exception's message passes the node's redactor before it is written.
> - A link logs one line per change of state (from, to, reason, attempt) and, while a state repeats, one summary a minute
>   (`log.repeat.summary.ms`): "still dialling Stage left: 11 attempts, last: refused".
> - The show journal takes: a needed link lost and back while the show runs, refusals, certificate changes, expired commands, and one row
>   per ended ticket (§13.9.8).
> - Counts are published (§13.13 `counts`), not only logged.
>
> ### 13.16 Conformance
>
> A node that answers `peerlink 1` passes the PeerLink kit (`docs/showbus/peerlink/`, its hash in §14) on injected time and injected
> randomness: its link logic is a machine driven by (now, event) that reads no clock and draws no random number of its own. The
> cross-app rows of the interop lane (P1–P6) run the four apps together.

### §14 additions (the rows of spec-1-3's block that this area owns; spec-1-3's existing `link.*` rows stay as they are)

```showbus
peerlink 1
link.check.max.ms 1000
link.selfgap.ms 2000
link.connect.timeout.ms 5000
link.redial.jitter.permille 400
link.redial.floor.permille 800
link.hold.refused.s 60
link.hold.disconnect.s 30
link.write.deadline.max.ms 10000
link.replies.max 256
link.inflight.max 32
link.message.read.min.bytes 262144
link.sidecall.timeout.ms 4000
show.stuck.default.ms 3000
show.stuck.min.ms 1000
show.stuck.max.ms 10000
count.frame.every.ms 1000
count.stale.ms 3000
clock.window 8
clock.rtt.max.ms 60000
clock.fresh.ms 10000
clock.apply.deadband.ms 50
receipt.final.ms 30000
cmd.expire.default.ms 5000
cmd.expire.max.ms 10000
request.run.chars 6
idem.keep 256
idem.keep.ms 600000
receipts.ask.max 32
report.retry.first.ms 3000
report.retry.max.ms 30000
report.retry.for.ms 1800000
nonce.hex 32
proof.wrong.close 5
log.repeat.summary.ms 60000
peerlink.kit.sha256 <64 hex>
```

Kept from spec-1-3's block as written: `link.beat.ms 2000`, `link.stale.ms 6000`, `link.drop.ms 12000`, `link.redial.first.ms 500`,
`link.redial.max.ms 5000`, `ping.min.ms 1000`, `ping.max.ms 2000`, `clock.apart.ms 2000`, `receipt.deadline.ms 3000`, `request.id.chars 32`.

**Words handed to the rest of the master** (spec-1-3's single edit): §4's liveness paragraph becomes "A link follows §13.7; nothing is
queued while a link is down (§13.12.5)"; §5 keeps its arithmetic and gains "its quality: §13.8"; §7.2's pre-pairing words gain
`PEERLINK`; §3 gains `final`, `replay`, `frame`, `lateMs` and the stages `observed` and `expired` as the last fields and words; §1 rule 3
gains "and a command is never applied twice (§13.9.5)".

**The reflection the acceptor's derived key prevents.** Had `PROVE` used K directly over cnonce ‖ "acceptor:" ‖ id, a client that says
`HELLO acceptor:9f8e7d6c` and asks `PROVE <the acceptor's own nonce>` would receive exactly the `AUTH-PROOF` that pairs it. With Kₐ (and
with `PROVE` answered only after pairing) the two never meet. Worked values for the kit, token `abcd-efgh-ijkl` (K = `ABCDEFGHIJKL`):
- `AUTH-PROOF` over nonce `3f9a0c1d2e4b5a69788796a5b4c3d2e1` and name `Stagewatch` =
  `7490a3de9065f4040b38fb07c1ffc489947348137fe81dca8d47f2fabd4f7f38`;
- `PROVE` over cnonce `0b1c2d3e4f5061728394a5b6c7d8e9f0` by acceptor `9f8e7d6c` =
  `9bde0f81c87ead6c86f0fbda112519534187b0f5a30986441e7e6c28e4e45d25`;
- redial (n, u) → ms: (0, 0) 400; (0, 0.999999) 599; (1, 0.5) 1000; (3, 0.25) 3600; (4, 0) 4000; (9, 0.999999) 5995;
- errMs from rtt: 0 → 1; 1 → 2; 11 → 7; 250 → 126.

---

## 3. The layers, precisely

1. **Discovery (§2), unauthenticated.** The hello on UDP 9928: what a node is (`t`, `id`, `inst`, `name`, `host`), where its doors are
   (`ports`), what it says it can do (`caps`), its certificate's fingerprint as a hint (`tls`), and one show fact (`presenting`). The peer
   table: live / stale / gone / forgotten by hello, one id one peer, id conflicts. Discovery may *choose* a door, a carrier and a pin key;
   it never authorises, verifies, pairs, carries show state or decides safety. Its liveness is the node's existence on the network, not
   any link's.
2. **PeerLink (§13), one connection between two nodes.** Carrier choice, identity and pairing by proof, keys and holds, TLS and pins,
   profile negotiation, link liveness and show liveness, the clock exchange and its quality, request ids, receipts and outcomes,
   idempotency and the RECEIPTS question, frame counters and latest-wins mechanics, bounds and backpressure, the side-call lane,
   reconnect and holds, the state model and its flags, safety on loss, logging and secrets. It knows nothing of a VT, a cue, a window or
   a proposal: it carries lines, frames and calls and says what became of them.
3. **Show Bus messages (§3–§6, §11, §12), over a verified link.** What the lines and frames mean: the verbs and their risk, receipt
   `effect` words and `refs`, STATE and frame contents, contexts, approvals and their states, windows and grants. HTTP side calls are
   layer 3 content riding layer 2's lane and identity.
4. **Views.** The Eye (Patterns' God's Eye), Stagewatch's Show network and producer page, PlayMap's desk and preflight, AGMessage's halls
   board, the Companion modules, every AI brief. They read layer 2's fields (§13.13) and layer 3's content and derive no link fact of
   their own. The Eye draws each node from its context and each link as an edge carrying `state`, `show`, `verified`, `profile` and
   `clock`, so "the room" is the same picture on every desk. A view's operator actions (accept a certificate, dial again now, let a key
   speak for a node, release a held fact) go down as verbs, never as private calls.

---

## 4. Each app, each link: today and the change

(Full rows; the structured answer carries the same.)

### Stagewatch
- **Connector: `PatternsLink` → `PlayoutLink`** (playout-links). Today: F2, F3, F6 (the backoff back to 250 ms on every connect, `PatternsLink.cs:209`), F9 (no jitter), no stale state, F1 on drop,
  F15 (writes from the show thread), plain AUTH to any host after 1.5 s (G08), a late NONCE ignored (`PatternsLink.cs:279-284,311`),
  wall-clock t1/t4, F17. Change: playout-links' `PlayoutLink` already brings tickets ≤ 32, not-sent/unconfirmed, `@pn NONCE` failing
  closed, TLS without downgrade and one journal row per attempt; this profile adds `@pl PEERLINK`, `@pv PROVE` before following a clip or
  a blackout, the stale state at 6 s, ±20 % jitter with the verified-only reset, the pong's fifth word into the VT (a stuck Patterns or
  PlayMap shows no countdown), `@rq RECEIPTS` after a reconnect, ids `s<n>-<run>`, `frame`-observed tickets in the journal ("seen in
  frame 1234"), `clock:<link>` items with `errMs`/`ageMs`, a pure `PeerLinkRules` in `Timer.Core` (state machine, outcomes, redial,
  clock) on an injected `TimeProvider` and jitter source, the oversize/malformed counts on the link item.
- **Acceptor: `WebServer` `/ws`, HTTP, Companion TCP.** Today: beats and the stuck beat right; pong t2 = t3 (fine within errMs); F16;
  no dedupe; silent shedding at 64 connections. Change: the verdict's `link` block and `self` (with peers-not-screens SW-3); the
  connector's ping `live` recorded on the Show network row; `idem` 256 per bound node with `replay:true`; `expireMs` 5000 in
  `ExecuteWithReceiptAsync` (a queued command checks its read time before it runs; the final receipt says expired or `lateMs`); `frame`
  in receipts (the hub's next `seq`); `{"cmd":"receipts"}` on WS and `@rq RECEIPTS` on Companion TCP; final receipts (playout-links);
  a line of words before shedding a connection; one log line per change, a summary a minute.

### Patterns
- **Connector: `StagewatchLinkService`.** Today: F4 (G04), F5, F6, F10, F11, F13, static `ShowClock`, `cid=patterns` (G32). Change:
  patterns-playmap-side's G04 flag (written only by the receive loop) plus §13.7.5's rule that a pong never clears it; peers-not-screens'
  `node=<id>&t=patterns&peerlink=1`; a side-call lane separate from the ping; keyless off-machine without TLS; the verified-only reset;
  a bounded LRU of desk ids keyed by the desk's run-unique ids (cross-app-proposals D5); result reports retried 3→30 s for 30 minutes,
  written ahead; `TimeProvider` injected into the link (the `ShowClock` read moves behind a seam); STATE `bus.stagewatch` gains the §13.13
  fields at its end (`clock` with `errMs`, `ageMs`; `show`; `profile`; `verified`).
- **Acceptor: `ControlService` (9697).** Today: pong off the UI thread with no show word; no PEERLINK, PROVE, dedupe, expiry or
  RECEIPTS; STATE before NONCE (G45, spec-1-3's Round 90.2 if the maintainer agrees). Change: `PEERLINK` answer; the pong's fifth word from
  the UI thread's heartbeat stamp, with `stuckMs` declared from a measurement (until it is measured: 10000, the papers saying why — it is
  the live hang timeout the supervisor already trusts, `Resilience.cs:41-45`); `PROVE`; `idem` per paired peer; `expireMs` at the
  router's dispatch; `frame` = STATE `rev` after the action; `@rq RECEIPTS`; profile-1 answers `OK @id {json}` with `stage`. Round
  discipline: one unit per change, `TrustLoopback = false` in every loopback test, STATE fields at the end of their rows.
- **Twin and node links (`TwinService*`).** Today: 1 s beats from the UI tick, 5 s silence, a fixed 1 s dial, `ACT` forwards that never
  hear back (F12). Change: the shapes and the 1 s / 5 s numbers stay (failover is Patterns' own; §13.2 names the exemption); `ACT` gains an
  id and an `ACTED <id> <outcome> <words>` answer, so a caller's GO is applied, refused or unconfirmed (§13.9.3) and the node's journal row
  says which; a second, background keep-alive beat lets a standby tell "the main's desk is stuck" (link heard, show beat silent) from "the
  main is gone" — shown at once as words and a light; the takeover rule (`TwinWatch.AutoTakeOverBlocked`) is unchanged until a measurement
  says otherwise; the dial gets ±20 % jitter (no measurement; a desk with many nodes). A twin change is a platform path: a §22 walk row.

### PlayMap (after round 3, or by the owner's leave: `src/PlayMap.Net/Links` and `src/PlayMap.Net/Wire` are named for no wave, and round 3 §4 says nothing in that wave edits them, `Mapper/docs/round3/PLAN.md:2122-2125`)
- **Connector: `StagewatchLink`.** Today: the closest to the profile (injected clock, stuck honoured, seq latest-wins, ±20 %, 30 s
  disconnect hold); F5, F6 (reset on a normal return), F10, F11, F13, G06/G28/G31. Change: `node=<id>&t=playmap&peerlink=1`
  (peers-not-screens); the side-call lane; the verified-only reset; keyless without TLS off-machine; a bounded id LRU and result retries;
  STATE `links.stagewatch.clock {offsetMs, errMs, ageMs, samples}` (G28) and `verified`/`profile`/`show`; `identify` a journal row
  (peers-not-screens PM).
- **Connector: `PatternsLink` / `WireClient`.** Today: injected clock, write deadline, plain AUTH only to loopback, blackout held across a
  drop; F1 (no answer and a drop read as failures), F3, proofs retried on every redial after a refusal, a pong parser that needs exactly
  four words. Change: the seven outcomes (`WireReply` gains the outcome, `unconfirmed` instead of `ok:false`); ids `pm<n>-<run>`;
  `PEERLINK`; `PROVE` verified before `FOLLOW-BLACKOUT` acts on a fact (F14); a refused proof holds until the token changes; the fifth
  pong word; `@rq RECEIPTS`; held blackout shown with its age (§13.14).
- **Acceptor: `WireServer` / `WireSession` (9941, `playmap.lines.v1`).** Today: `LineWriter` meets §13.11.2 already, PONG t3 at write time;
  no PEERLINK, PROVE, dedupe, expiry, RECEIPTS or show word. Change: those five, the show word from the engine control lane's stamp
  (`stuckMs` 3000 unless PlayMap's lab line says otherwise), `frame` = STATE `seq`.

### AGMessage (0.5.x)
- **Connector: `StagewatchLink`, one per hall.** Today: F4 variant (pongs refresh, stuck never shown), F6 (reset on open), F7, F1 ("not
  sent" after a timeout), F13, G43 (connected and pinned on a public frame), G16. Change: the agmessage design's key-only link, verdict-
  first evidence, `DeskRuns` write-ahead with 3/6/12/24/30 s retries (it already matches §13.9.7); and from the profile:
  `performance.now()` for liveness and t1/t4 from a never-stepping clock (`timeOrigin + performance.now()`); `node=<id>&t=messenger&
  peerlink=1`; the stuck view (`?`, the hall board says "Stagewatch's show is stuck"); the verified-only reset; `unconfirmed` for a send
  whose request may have arrived; keyless without TLS off-machine; injected `now`, timers and `rand` in tests.
- **Connector: `PeerLink` → Patterns / PlayMap.** Today: HTTP polling every 5 s, stale at 12 s, no dead, no clock, tokens in headers that
  can ride plain HTTP, "not sent" after a timeout. Change: a line-wire link (`net` to 9697 / 9941, or `playmap.lines.v1` over pinned WSS
  for PlayMap) with `HELLO AGMessage`, `@pl PEERLINK`, `AUTH-PROOF`, `@pv PROVE`, `PING` every 2 s, `@ctx CONTEXT` every 5 s after
  pairing, commands `@a<n>-<run>` with the seven outcomes; HTTP only to loopback or pinned HTTPS, as a reader (§13.2). Until that lands,
  the halls board draws these rows as readers ("read 4 s ago"), never as green links.

---

## 5. The conformance kit

**Where it lives.** The master copy sits beside the master spec: `Stagewatch/docs/showbus/peerlink/`. Copies at the same path in
Patterns, Mapper (with spec-1-3's M1 copy, after round 3 or by the owner's leave) and AGMessage. `MANIFEST` lists every file with its
sha256; the sha256 of `MANIFEST` is §14's `peerlink.kit.sha256`. Each tree's existing `ShowBusSpec` test (spec-1-3 decision 10) also hashes
its kit copy against that row (an accidental edit fails that tree); `tools/showbus-copies.sh` in the interop lane finds a copy older than
the master. Every secret in the kit is the fixed fixture token `abcd-efgh-ijkl` or `swk_FIXTURE…`, and the files say they are fixtures (as
AGMessage's secret scan asks).

```
docs/showbus/peerlink/
  MANIFEST
  vectors/   proof.json  clock.json  liveness.json  redial.json  ids.json  receipts.json  order.json
             bounds.json  states.json  negotiate.json  secrets.json  outcomes.json
  scripts/   c-ws-*.json  c-line-*.json  a-ws-*.json  a-line-*.json  twin-*.json
  peer/      ScriptedPeer.cs     (one file, BCL only, net10)
             scripted-peer.mjs   (one file, Node stdlib only)
```

**Vectors** are pure input → output tables, one suite per file, so a language-agnostic loop runs them:
```json
{"kit":"peerlink","v":1,"suite":"liveness","cases":[
  {"name":"just live","silenceMs":5999,"state":"live"},
  {"name":"stale at the boundary","silenceMs":6000,"state":"stale"},
  {"name":"dead at the boundary","silenceMs":12000,"state":"dead"},
  {"name":"own gap is no evidence","silenceMs":15000,"evaluatorGapMs":4000,"state":"stale","then":{"afterMs":2000,"heard":false,"state":"dead"}}]}
```
```json
{"kit":"peerlink","v":1,"suite":"clock","cases":[
  {"t1":1000,"t2":1505,"t3":1506,"t4":1012,"accept":true,"offsetMs":499.5,"rttMs":11,"errMs":7},
  {"t1":1000,"t2":900,"t3":950,"t4":990,"accept":false,"why":"t4 < t1"},
  {"window":[{"rtt":40,"offset":120},{"rtt":4,"offset":100}],"offsetMs":100,"errMs":3,"apart":false},
  {"window":[{"rtt":3000,"offset":3000}],"offsetMs":3000,"errMs":1501,"apart":false},
  {"fit":{"errMs":7,"ageMs":9000,"tolerance":10},"result":true}]}
```
`redial.json` gives (n, u) → ms with the integer formula of §13.12.1 (so C# and JavaScript agree to the millisecond); `proof.json` the
token canonicalisation and both HMACs (the worked values in §2); `ids.json` valid and invalid ids and the run-unique form;
`receipts.json` line and JSON answers → outcome, `final`, `replay`, `frame`, `lateMs` (profile 0 and 1); `order.json` sequences of frame
counters across connections → kept / dropped / restarted; `bounds.json` the 33rd ticket, the 257th reply, sizes; `states.json` (state,
flags) → light, shape, command allowed, count shown (word keys, never words); `negotiate.json` offers and answers → profile;
`secrets.json` (carrier, credential, address, pinned) → send / keyless / refuse; `outcomes.json` drop-before-write, drop-after-write,
30 s silence, RECEIPTS answers → outcome.

**Scripts** are timed conversations on injected time, one role under test:
```json
{"kit":"peerlink","v":1,"script":"c-ws-stuck-beat","role":"connector","carrier":"ws","profile":1,
 "settings":{"self":{"id":"9f8e7d6c","t":"patterns"},"key":"swk_FIXTURE0000000000000000000000","pinned":true},
 "steps":[
  {"at":0,"expect":{"dial":{"path":"/ws","query":{"node":"9f8e7d6c","t":"patterns","peerlink":"1"},"auth":"bearer","tls":true}}},
  {"at":5,"peer":{"send":{"type":"auth","ok":true,"scope":"node","node":{"t":"patterns","id":"9f8e7d6c","verified":"key"},
                          "link":{"peerlink":1,"self":{"t":"main","id":"3f9c2a1b7d0e","inst":"a41f09c2"},"beatMs":2000,"stuckMs":3000,"expireMs":5000,"idem":256,"final":true}}}},
  {"at":10,"peer":{"send":{"type":"state","v":1,"seq":41,"seconds":600}}},
  {"at":10,"expect":{"state":"live","show":"live","verified":"both","count":"shown"}},
  {"at":2000,"peer":{"send":{"type":"beat","live":false}}},
  {"at":2000,"expect":{"state":"stuck","show":"stuck","count":"?"}},
  {"at":2010,"peer":{"send":{"type":"pong","t":2000,"t2":2005,"t3":2005}}},
  {"at":2010,"expect":{"show":"stuck"}},
  {"at":9000,"peer":{"send":{"type":"state","v":1,"seq":42,"seconds":592}}},
  {"at":9000,"expect":{"state":"live","show":"live"}},
  {"at":21000,"expect":{"state":"dialling","attempt":0,"nextMs":{"min":400,"max":599}}}]}
```
Every `expect` must hold at its `at` within the implementation's declared check step (≤ `link.check.max.ms`); a `sent` list is exact (no
extra line), and any byte equal to a fixture secret on a plain carrier fails the run.

**The scripted peer** plays the other side of a script over real loopback sockets: TCP lines, WS (and WSS with a fixture certificate
whose fingerprint the script pins). The harness runs in lockstep: it moves the injected clock to the next step's `at`, lets the peer send
that step's messages, then waits (real time, ≤ 2 s) for the outputs the step expects. Timings exist only on the injected clock, so a run is
deterministic however slow the machine is.

**How each tree consumes it.**
| tree | pure rules (vectors) | real link classes (scripts) | runner |
|---|---|---|---|
| Stagewatch | `Timer.Core/PeerLinkRules.cs` | `PlayoutLink` (connector), `WebServer` node links and Companion TCP (acceptor), on an injected `TimeProvider` | `tests/Stagewatch.Tests` group `PeerLinkKit` (the suite's own runner); `ScriptedPeer.cs` linked as a compile item; `tests/e2e` runs `scripted-peer.mjs` against the rig |
| Patterns | `Patterns.Core/Services/PeerLinkRules.cs` | `StagewatchLinkService` (connector), `ControlService` (acceptor), the twin's `ACT`/`ACTED` | xUnit theories in `Patterns.Core.Tests` (vectors) and `Patterns.App.Tests` (scripts, `TrustLoopback = false` restored in `finally`) |
| PlayMap | `PlayMap.Core/Bus/PeerLinkRules.cs` | `StagewatchLink`, `WireClient`/`PatternsLink`, `WireSession` (acceptor) on `tests/Shared/ManualTime.cs` | xUnit in `PlayMap.Core.Tests` and `PlayMap.Net.Tests` |
| AGMessage | `app/lib/peerlink-rules.js` | `nodes.js` `StagewatchLink`, `peerlinks.js` `PeerLink`, with injected `now`, timers and `rand` | `node --test app/test/peerlink.test.js` (added to the `test` script) |

**What each link must pass.**
| suite | what it holds | connectors 1–6 | acceptors (Stagewatch, Patterns, PlayMap) | twin/node (7–8) |
|---|---|---|---|---|
| ID | pairing words, proof both ways, other-node, refused-proof hold | ✓ | ✓ | `ACT` ids only |
| TLS | pin after authenticated success, CHANGED refuses, no downgrade, secret only on pinned TLS or loopback | ✓ | ✓ (serves pins) | — |
| NEG | profile 0/1 both ways, unknown argument refused | ✓ | ✓ | — |
| LIVE | 2/6/12 boundaries, check step, self-gap | ✓ | ✓ (closes at 12 s) | own numbers, vectors with its constants |
| SHOW | stuck beat, pong word, frames clear, pong never clears | ✓ | ✓ (emits) | ✓ (show beat vs keep-alive) |
| CLOCK | sample filter, best of 8, errMs, fresh, fit, apart, reset on inst | ✓ | t3 at write | ✓ |
| RCPT | seven outcomes, deadlines, final, idem/replay, expiry, lateMs, RECEIPTS, reports owed | ✓ | ✓ | `ACTED` |
| ORDER | counters, restart, latest-wins, commands never coalesced | ✓ | ✓ (one slot per kind) | — |
| BOUND | 32 in flight, 256 replies, write deadline, sizes, side-call lane | ✓ | ✓ | — |
| REDIAL | formula, reset only after verified, holds | ✓ | — | jitter |
| STATE | fields and lights per state and flag, the acceptor's list | ✓ | ✓ | ✓ |
| LOG | no fixture secret in any captured log, one line per change, a summary a minute | ✓ | ✓ | ✓ |

**The cross-app rows** (handed to ecosystem-lane, real apps on its one-kernel LAN):
- P1 a stuck rig (`stall 12000`): every follower shows no time within `stuckMs` + 2 s + 2 s, and every link stays `live` throughout.
- P2 the rig killed: every link is `dialling` with first redials spread over 400–599 ms of the drop (from their logs), all `live` within
  6 s of the restart, and no address in the rig's lockout table.
- P3 a pulled cable with a GO in flight: the ticket ends `unconfirmed`; after the cable returns `RECEIPTS` turns it `applied` or
  `observed`, and the GO ran once (the peer's journal has one row).
- P4 a stuck acceptor holding a queued command past `expireMs`: the final receipt says `expired`, and the command never runs.
- P5 profile mixing: one tree pinned at a profile-0 commit; every link to it is `legacy`, and nothing breaks.
- P6 no fixture or run secret appears in any capture (the lane's sweep).

---

## 6. Units, migration and what is refused when

**Profile 0 keeps working.** Profile 0 is Show Bus 1.2 as built. Every new shape is sent only on a connection that negotiated profile 1;
readers already ignore unknown JSON fields and unknown untagged lines; the only changed line shapes (the pong's fifth word, `OK @id {json}`
answers) appear only on profile-1 connections, because today's parsers need exactly four pong words.

**Order.**
1. **The words and the kit** (no behaviour change): this §13 and the §14 rows in spec-1-3's single master edit (its S1/S8); the kit beside
   the master; each tree's copy with its `ShowBusSpec` hash.
2. **Connector fixes that need no negotiation** — each tree's next unit; nothing on the wire changes for a peer:
   - the seven outcomes (F1), no eviction (F2), run-unique ids (F3), pongs never clear stuck (F4, G04), the side-call lane (F5), the
     verified-only reset (F6), a never-stepping clock (F7), one boundary (F8), jitter in Stagewatch (F9), id LRUs (F10), reports retried
     (F11), no blocking socket on a show thread (F15), counts shown (F17);
   - the secret rules: keyless off-machine without TLS (F13), no plain AUTH off loopback (G08, with playout-links), pins only after
     authenticated success (G43).
3. **Acceptors offer profile 1**, the hub first: Stagewatch (the verdict's `link`, dedupe, expiry, RECEIPTS — with peers-not-screens
   SW-3); then Patterns' `ControlService` (its round, after its G04 round); then PlayMap's `WireSession` (after round 3 or by the owner's
   leave). Patterns' twin `ACT`/`ACTED` lands with a walk row.
4. **Connectors ask for profile 1** where acceptors offer it: `PROVE`, the pong word, `RECEIPTS`, `observed`.
5. **The lane's rows P1–P6** turn on as each pinned commit gains its units.
6. **Show Bus 1.4** makes profile 1 required for node links: a profile-0 connection is then served as a screen (the public view), never
   as a node, and each tree's papers name the oldest build that still links.

**Refused, and when.**
| refused | from |
|---|---|
| plain `AUTH` to another machine | each connector's first profile release (Patterns already refuses it as acceptor, `ControlService.cs:427-430`) |
| a bearer key or a token header over plain non-loopback | each connector's first profile release |
| a plain dial to a pinned node id | the same |
| a pin set on a connect or a public frame | the same |
| an unknown `PEERLINK` argument, `PROVE` before pairing | each acceptor's first profile release |
| an id already run (answered as replay, never run) | each acceptor's first profile-1 release, for profile-1 peers |
| a command waiting past `expireMs` | the same, for every connection (a late command is a defect whoever sent it) |
| a profile-0 node link | Show Bus 1.4 (served as a screen) |

**What the profile adds as units** (each tree's own process; numbers are the maintainers'):
- Stagewatch: SW-PL1 `PeerLinkRules` + the kit runner; SW-PL2 acceptor profile 1 (verdict `link`, idem, expiry, RECEIPTS, `frame`,
  shedding words); SW-PL3 `PlayoutLink` profile 1 (with playout-links' units); SW-PL4 the kit master, MANIFEST and §14 rows (with
  spec-1-3 S1/S8).
- Patterns: PT-PL1 `PeerLinkRules`, the connector fixes and the `TimeProvider` seam; PT-PL2 `ControlService` profile 1; PT-PL3 twin
  `ACT`/`ACTED`, the keep-alive beat and dial jitter (walk row); PT-PL4 the kit copy and papers.
- PlayMap: PM-PL1 `PeerLinkRules` and `WireReply` outcomes; PM-PL2 `WireSession` profile 1; PM-PL3 `StagewatchLink`/`PatternsLink`
  profile 1 and STATE fields; PM-PL4 the kit copy — all after round 3 or as one exception request quoting round 3 §4.
- AGMessage: AG-PL1 `peerlink-rules.js`, monotonic time and outcomes in `StagewatchLink`; AG-PL2 `PeerLink` on the line wire; AG-PL3 the
  kit copy and `peerlink.test.js`.
- Lab lines (no measurement this round): LP-1 each node's honest show-writer stall distribution over a walk, which sets its `stuckMs`
  (Patterns first); LP-2 Stagewatch's connection slots and side-call burst with 32 links reconnecting at once; LP-3 Windows sleep/resume
  and the self-gap rule on each tree.

---

## 7. Risks

- `PROVE` and dedupe make line wires depend on a shared token more than today: a leaked token now lets someone impersonate an acceptor
  too, not only a connector. Mitigation: tokens rotate with NEW TOKEN everywhere, and the papers say a token is a pair's secret.
- A declared `stuckMs` can be set to hide stalls (10 s); the cap and the node page's stated reason are the only fence until LP-1.
- `expireMs` 5000 can refuse a GO a person pressed during an honest Patterns save; the receipt says why and a person presses again.
  The number is unmeasured (LP-1 sets it).
- Holding a followed blackout through a loss can keep a stage dark when the desk is gone for good; the held fact names its age and a
  person can release it (§13.14).
- Patterns' static `ShowClock` is read across the desk; moving the link onto an injected `TimeProvider` touches many readers if done
  wholesale — the seam stays inside the link services.
- Mapper's round-3 ownership may delay PlayMap's acceptor; Stagewatch and AGMessage then see PlayMap as `legacy` for a while (amber).
- More amber during migration (`legacy`, `keyless`, `one-way`) risks alarm fatigue: views group flags per peer ("3 older peers"),
  and only configured links can turn red.
- The kit becomes a fifth copy-fenced artefact; a stale copy is found only by the interop lane (as spec-1-3 says of the spec copies).
- Renumbering spec-1-3's numbers block to §14 touches 23 references in five unlanded designs.
- A profile-0 Stagewatch keeps answering pongs off its show thread with no live word on Companion TCP's `PONG … STUCK` form; decks keep
  their own words, which this profile does not change.

---

## 8. What this asks of the sibling designs (kept unless shown wrong)

- **spec-1-3**: carries §13 into its single master edit; its numbers block becomes §14 with the rows above (its `link.*`, `ping.*`,
  `clock.apart.ms`, `receipt.deadline.ms` and `request.id.chars` rows unchanged); §3, §4, §5, §7.2 and §1 rule 3 take the words in §2; the
  kit's hash joins the copy rule. Nothing it decided is reversed.
- **playout-links**: kept whole (32 tickets, not-sent/unconfirmed, 30 s, `final`, `@pn NONCE` failing closed, no downgrade, one row per
  attempt, ±20 %). One correction: a ticket to a desk that gives no receipts ends `unconfirmed` ("sent — this node gives no receipts"), not
  accepted, because no answer came; its "seen in STATE" row is how it is resolved. `PlayoutLink` adds `PEERLINK`, `PROVE`, the pong word,
  `RECEIPTS` and `frame`-observed.
- **peers-not-screens**: kept whole (node identity on `/ws`, verdict first, holds, one node one key, unknown keys uncounted). The verdict
  gains `link` (with `self`) last, on `peerlink=1`.
- **agmessage**: kept (key only, evidence-first `connected`, `DeskRuns` write-ahead, 3→30 s retries for 30 minutes — the profile's
  §13.9.7 is that rule). Added: monotonic time, the stuck view, `unconfirmed`, keyless off-machine without TLS, and the 0.5.0 `PeerLink`
  (which no design covered) moved onto the line wire.
- **patterns-playmap-side**: G04's single-writer flag kept; the profile adds that a pong never clears it. Patterns' `ControlService`
  profile 1 and the twin's `ACT`/`ACTED` are later rounds of Patterns' own numbering.
- **cross-app-proposals**: D5's run-unique proposal ids are the same principle as §13.9.1's request ids.
- **ecosystem-lane**: rows P1–P6 (§5), the kit in `tools/showbus-copies.sh`, and its deadline rule (contract number × 1.5 + 2 s) applied to
  `stuckMs`, the 12 s drop and the 30 s final receipt.
- **windows-for-peers**: D4's "a page path travels only over TLS or a local link" is §13.4's bearer rule applied to view passes; nothing
  changes.
