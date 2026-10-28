# PeerLink — layer 2 of the Show Bus, one standard for every link in the four apps (adoption first)

Area: peerlink (adoption angle). Design only: no repo was edited or built. Paths are repo-relative and prefixed with the repo;
line numbers are from the trees as read on 2026-10-25 (Stagewatch `855e8b6`, Patterns `bcdbf28`, Mapper `f647094`). `AGMessage@0.5.0`
means AGMessage's branch `AGMessage's 0.5.0 branch` at `7b97cca`, read with `git show`; plain `AGMessage/` means its
checked-out `main` (0.4.0). Sibling designs are cited as `design/<area>.md:<line>`. Test vectors in §5.3 were computed in the
scratchpad with Python's `hmac`/`hashlib` (stdlib, no file written); nothing else was run.

**The angle.** Four apps already speak three carriers, and almost every byte of layer 2 is already the same. So the profile is written
*onto* the bytes the apps send today: no new carrier, no new port, no field moved; one new line verb (`PEERLINK`), one optional proof
verb (`PROVE`), one appended verdict field, one fifth PONG word, behind negotiation. Most of the profile is **unilateral** — an app
meets it alone, in its own release, whatever its peers run — and the few rules that need both ends are **features negotiated after
pairing**, so an old peer simply never sees them. One kit (JSON vectors, JSON conversation scripts, one C# and one JS interpreter, all
byte-identical in four trees and pinned by one hash in the spec) holds every link to the same answers.

**Numbering.** spec-1-3 owns the single master edit (`design/spec-1-3.md:32-48`) and drafted "The numbers" as §13
(`design/spec-1-3.md:536`). This area hands its words to that edit as **§13 PeerLink**; the machine-read block becomes **§14 The
numbers**, with the `link.*`, `receipt.*`, `clock.*`, `show.*`, `peerlink.*` rows of §2.20 appended. Keys spec-1-3 already has
(`link.beat.ms` … `request.id.chars`) keep their names and values.

---

## 0. What exists (evidence)

### 0.1 The links today

| # | link | role, binding, carrier | evidence |
|---|---|---|---|
| L1 | Stagewatch `PatternsLink` → Patterns 9697 (or a typed PlayMap 9941) | line connector, plain TCP | `Stagewatch/src/Timer.Integrations/PatternsLink.cs:186-233` |
| L2 | Stagewatch `/ws` + HTTP | JSON acceptor (WS on 9924, TLS sniffed by the first byte) | `Stagewatch/src/Timer.Remote/WebServer.cs:286-340,1141-1356` |
| L3 | Patterns `StagewatchLinkService` → Stagewatch | JSON connector + HTTP side calls | `Patterns/src/Patterns.App/Services/StagewatchLinkService.cs:168-355,508-540` |
| L4 | Patterns wire 9697 + HTTP 9696 | line acceptor (plain TCP); HTTP sniffs TLS | `Patterns/src/Patterns.App/Services/ControlService.cs:320-512,925-968,1333-1369` |
| L5 | Patterns twin and its node relays | Patterns↔Patterns, own carrier | `Patterns/src/Patterns.Core/Services/Twin.cs:162-196,484-509`; `Patterns/src/Patterns.Core/Services/LinkClock.cs:5-28` |
| L6 | PlayMap `StagewatchLink` → Stagewatch | JSON connector + HTTP side calls | `Mapper/src/PlayMap.Net/Links/StagewatchLink.cs:227-560` |
| L7 | PlayMap `WireClient` (`PatternsLink`) → Patterns 9697 | line connector, plain TCP | `Mapper/src/PlayMap.Net/Links/WireClient.cs:145-418`; `Mapper/src/PlayMap.Net/Links/PatternsLink.cs:200-280` |
| L8 | PlayMap wire 9941 and `/ws` subprotocol `playmap.lines.v1` | line acceptor, TCP or a WS frame per line | `Mapper/src/PlayMap.Net/Wire/WireSession.cs:178-419`; `Mapper/docs/wire/companion.md:6-23` |
| L9 | AGMessage `StagewatchLink` → Stagewatch | JSON connector + HTTP side calls | `AGMessage@0.5.0 app/lib/nodes.js:205-460` |
| L10 | AGMessage `PeerLink` → Patterns / PlayMap (new in 0.5.0) | **HTTP polling** `/api/context` every 5 s, commands `POST /api/cmd` | `AGMessage@0.5.0 app/lib/peerlinks.js:79-168` |

(The task's "eight implementations" counts the acceptors with their connectors; listed apart here because each side converges on its
own schedule. Patterns' Round 92 adds an eleventh, the `ShowBusNode` device link to PlayMap, `design/patterns-playmap-side.md:385-395`.)

### 0.2 What is already the same everywhere — kept byte for byte

| rule | evidence (each implementation) |
|---|---|
| Proof = lowercase hex HMAC-SHA256(key = normalised token, msg = nonce + HELLO name) | Stagewatch `PatternsLink.cs:379-384`; Patterns `ShowBusNetwork.cs:322-326`; PlayMap `Mapper/src/PlayMap.Core/Bus/Pairing.cs:54-59`; Patterns' Companion module `Patterns/integrations/companion-module-patterns/src/main.js:361-364` |
| Nonce = 16 random bytes as 32 lowercase hex, replaced after every proof | Patterns `ShowBusNetwork.cs:320`, `ControlService.cs:454-457`; PlayMap `Pairing.cs:51`, `WireSession.cs:337-338` |
| Request ids `[A-Za-z0-9_-]{1,32}`, answers `OK @id …` / `ERR @id …` | Patterns `ShowBusNetwork.cs:240-276`; Stagewatch `Timer.Core/ShowState.cs:1207-1208`; PlayMap `WireClient.cs:353-378` |
| Clock exchange `PING t1` → `OK PONG t1 t2 t3`; WS `{"cmd":"ping","t"}` → `{"type":"pong","t","t2","t3"}` | Patterns `ControlService.cs:407-411`; PlayMap `WireSession.cs:306-315`; Stagewatch `WebServer.cs:1349-1355` |
| Offset = min-rtt sample of the last 8 | Stagewatch `Timer.Core/Resilience.cs:244-282`; Patterns `LinkClock.cs:41-95`; PlayMap `Mapper/src/PlayMap.Core/Time/ClockOffsetEstimator.cs:48-60`; `AGMessage@0.5.0 app/lib/resilience.js:162-179` |
| Beat 2 s, stale 6 s, drop 12 s | Patterns `ShowBusNetwork.cs:16-41`; PlayMap `Mapper/src/PlayMap.Core/Bus/ShowBus.cs:17-26`; `AGMessage@0.5.0 app/lib/nodes.js:30-32`; Stagewatch `PatternsLink.cs:43-44` (no stale state) |
| Redial 0.5 s doubling to 5 s × U[0.8, 1.2] | Patterns `ShowBusNetwork.cs:31-37`; PlayMap `ShowBus.cs:232-236` + `Mapper/src/PlayMap.Net/Common/Timing.cs:46-60`; `AGMessage@0.5.0 app/lib/nodes.js:203,352-353` |
| One writer per connection; replies ordered, ≤ 256 queued, 10 s write deadline; pushes latest-wins | Patterns `Patterns/src/Patterns.App/Services/WirePeer.cs:17-96`; PlayMap `Mapper/src/PlayMap.Net/Common/LineWriter.cs:15-80`, `ConnectionLedger.cs:58-65` |
| Five wrong proofs close the connection | Patterns `ControlService.cs:443-447,541`; PlayMap `ConnectionLedger.cs:65`, `WireSession.cs:359-365` |
| Key in `Authorization: Bearer`, never in the URL | Patterns `StagewatchLinkService.cs:269`; PlayMap `StagewatchLink.cs:278-279`; `AGMessage@0.5.0 app/lib/nodes.js:306,326` |
| Pinned TLS: SHA-256 of DER per node id, CERTIFICATE CHANGED until accepted | Stagewatch `Timer.Core/Approvals.cs:240-275`; Patterns `Patterns/src/Patterns.Core/Services/ShowBusTls.cs:34-71`; PlayMap `StagewatchLink.cs:261-268`; `AGMessage@0.5.0 app/lib/tls.js:166-200` |
| Stuck show = `{"type":"beat","live":false}` every 2 s, a fresh frame first after | Stagewatch `WebServer.cs:1262-1297`; Companion TCP's `PONG … STUCK` precedent `Stagewatch/src/Timer.Companion/CompanionServer.cs:389-399` |

That table is the profile's spine: none of it changes.

### 0.3 Where they diverge — each is a small, local fix

| D | divergence | evidence | the profile's rule (§2) | size of the fix |
|---|---|---|---|---|
| D1 | Liveness boundary: 6000 ms silent is live in two apps, stale in two | `<=`: Patterns `ShowBusNetwork.cs:40`, Stagewatch `Timer.Core/ShowBus.cs:33`; `<`: PlayMap `ShowBus.cs:226-229`, `AGMessage@0.5.0 app/lib/nodes.js:58` | 13.9.2 inclusive of live ("stale *after* 6 s") | one character in PlayMap and AGMessage |
| D2 | CLOCKS APART at exactly 2 s: `>=` in Patterns, `>` elsewhere | Patterns `LinkClock.cs:97`; Stagewatch `Resilience.cs:274`; `AGMessage@0.5.0 app/lib/resilience.js:176` | 13.10.4 strictly greater ("exceeds 2 s", §5) | one character in Patterns |
| D3 | Clock-sample admission: four rule sets | Stagewatch refuses rtt < 0 or > 60 s (`Resilience.cs:256`); Patterns refuses only stamps ≤ 0 and clamps rtt to ≥ 0 (`LinkClock.cs:59-63`); PlayMap refuses rtt < 0, t4 < t1, t3 < t2 (`ClockOffsetEstimator.cs:48-51`); AGMessage refuses t4 < t1, rtt < 0 (`resilience.js:165-167`) | 13.10.2: the union | one condition each |
| D4 | Stagewatch redials 250 ms doubling, no jitter, reset on every connect | `PatternsLink.cs:189,209,229-230` | 13.15 | playout-links' `PlayoutLink` (`design/playout-links.md:112`) |
| D5 | Backoff resets on a mere connect or open, so an acceptor that accepts and closes is redialled at 0.5 s for ever | `PatternsLink.cs:209`; `StagewatchLinkService.cs:184-185`; `StagewatchLink.cs:236-237`; `AGMessage@0.5.0 app/lib/nodes.js:329`; PlayMap resets when the peer "said anything" (`WireClient.cs:247`) | 13.15.2: reset only after a verified connection that lived 10 s | one condition each |
| D6 | Pongs refresh show liveness, so a stuck Stagewatch looks live | `StagewatchLinkService.cs:521-522` (any message); `AGMessage@0.5.0 app/lib/nodes.js:333-336` (pong refreshes; stuck never shown); PlayMap clears stuck on a plain beat `StagewatchLink.cs:381` | 13.9.5: link liveness ≠ show liveness; only a frame clears stuck | Patterns Round 89.1 already designs it (`design/patterns-playmap-side.md:247-262`) |
| D7 | The beat and the drop check wait behind HTTP side calls (up to 3 × 4 s) | `StagewatchLinkService.cs:328-355`; `StagewatchLink.cs:438-475` | 13.9.3, 13.14.2: side calls on their own lane | split one loop in two (Patterns, PlayMap) |
| D8 | "No answer" is reported as a refusal; a drop fails everything as "lost" | `WireClient.cs:160-165,332`; `PatternsLink.cs:225-226`; `AGMessage@0.5.0 app/lib/nodes.js:387-389`, `peerlinks.js:151-152` | 13.11.4: `unconfirmed` (it may have run) vs `not-sent` | an outcome enum in each connector |
| D9 | Request ids restart every run (`s1`, `pm1`, `c1`) | `PatternsLink.cs:167`; `WireClient.cs:153` | 13.11.1: `<prefix><n>-<inst>` | zero cost for every acceptor (they treat ids as opaque) |
| D10 | Pending receipts evicted silently past 64 | `PatternsLink.cs:169` | 13.11.2: 32 in flight, the 33rd not sent | playout-links D5 (`design/playout-links.md:119-121`) |
| D11 | Token normalisation differs: Patterns strips `- SP HT CR LF`, .NET-side Stagewatch and PlayMap strip `char.IsWhiteSpace` (NBSP included), JS strips `\s` (U+FEFF included) | `Patterns/src/Patterns.Core/Services/PairingToken.cs:54-63`; `PatternsLink.cs:381`; `Pairing.cs:39-48`; `companion-module-patterns/src/main.js:362` | 13.5.1.3: after stripping, a token is `[A-Z0-9]+` or it is refused where it is typed | a pasted NBSP today pairs on PlayMap and fails on Patterns with an invisible cause; every app's alphabet is already ASCII (`PairingToken.cs:19`, `Pairing.cs:13`) |
| D12 | A NONCE of any characters is proved | Stagewatch `nonce.Length is > 0 and <= 128` (`PatternsLink.cs:311`); PlayMap `line[6..].Trim()` (`WireClient.cs:314`); the module (`main.js:217`) | 13.5.1.2: exactly 32 lowercase hex | one check; closes 13.17's reflection argument |
| D13 | Plain `AUTH <token>` to any host after 1.5 s with no NONCE | `PatternsLink.cs:280-284` (G08); also Patterns' own Companion module, a deck (`main.js:196-204`) | 13.5.1.5 | playout-links D3; the module's own Patterns round |
| D14 | A secret on a plain carrier off the machine: a node key over `ws://` when the hello names no TLS; Patterns'/PlayMap's **full pairing token** in `X-Patterns-Token` / `X-PlayMap-Token` over plain HTTP | `StagewatchLinkService.cs:225-243,269-276`; `StagewatchLink.cs:63-80`; `AGMessage@0.5.0 app/lib/peerlinks.js:108-121` | 13.3.1, 13.3.2 | §1 D5 moves AGMessage's `PeerLink` onto the line binding; the JSON connectors try TLS first |
| D15 | Owed §11 results posted once; "ran" sets cleared whole at 1000 | `StagewatchLinkService.cs:460-465`; `StagewatchLink.cs:538-552` | 13.14.3 (agmessage D3's schedule) | one retry list each |

### 0.4 What only a shared kit would have caught: the fakes disagree with the peers they fake

Each repo tests its link against its own idea of the peer, so a cross-app defect passes every suite (G44):

- **AGMessage reads Patterns' command answers wrongly, and its fake hides it.** `PeerLink.send` posts the bare line to `/api/cmd` with
  `X-Request-Id` (`AGMessage@0.5.0 app/lib/peerlinks.js:145-153`). Patterns ignores that header and takes a request id only from an `@id`
  at the head of the body (`Patterns/src/Patterns.App/Services/ControlService.cs:1337`), and answers JSON
  `{"ok":…,"msg":"OK …"|"ERR …"}` (`:1366-1368`). AGMessage's `readReceipt` reads a JSON body's `stage` and `effect|words|error`
  (`peerlinks.js:62-66`): every Patterns `OK` therefore reads **`accepted`, no words** (it ran), and every `ERR` reads **"said 200"**
  (the reason is lost). Cue verbs are refused by Patterns without `X-Patterns-Client` (`ControlService.cs:1355`), which AGMessage never
  sends. AGMessage's fake Patterns answers `text/plain` `OK @<x-request-id> …` (`AGMessage@0.5.0 app/test/helpers.js:122,137-138`) —
  a Patterns that does not exist — so its tests pass.
- **Stagewatch's fake desks** answer `OK pong` to `PING` and take plain `AUTH` (`Stagewatch/tests/Stagewatch.Tests/Program.cs:1276-1318`),
  and the 1.1 fake answers `OK paired`/`ERR wrong token` by its own reading (`Stagewatch/tests/Stagewatch.Tests/ShowBusTests.cs:239-263`).
- **PlayMap's fakes** (`Mapper/tests/PlayMap.Net.Tests/LinkTests.cs:17` `FakePatternsDesk`, `StagewatchLinkTests.cs:20` `FakeStagewatch`)
  and **Patterns'** (`Patterns/tests/Patterns.App.Tests/ShowBusAppTests.cs`, `ShowBus12AppTests.cs`) are likewise their own.

One scripted peer, written once from the real acceptors and run by every connector's tests, replaces all of them (§5).

### 0.5 Seams the kit needs, and which exist

- Virtual time: PlayMap has it (`IMonoClock` in `WireClientOptions.Clock`, `WireClient.cs:30-32`; `StagewatchLink(…, IMonoClock? mono)`,
  `StagewatchLink.cs:117`; `Mapper/tests/Shared/ManualTime.cs` is a `TimeProvider`). Stagewatch's `SnapshotHub` takes a clock
  (`Stagewatch/src/Timer.Core/ShowState.cs:207`) but `PatternsLink` uses a `Stopwatch` and static `TimeSpan`s (`PatternsLink.cs:41-55`).
  Patterns' link reads the static `ShowClock` and waits on `Task.Delay` (`StagewatchLinkService.cs:79-80,204-214`), which the
  peers-not-screens critique already found untestable (`design/json/peers-not-screens.json`, critique "cannot be written as claimed").
  AGMessage uses `Date.now()` and `setInterval` (`nodes.js:227,358`), which `node:test`'s mock timers can drive.
- A cross-language JSON fixture precedent exists: Mapper's OSC interop (`Mapper/tests/interop/osc/generate.mjs`, consumed by
  `Mapper/tests/PlayMap.Core.Tests/Control/OscTests.cs:187-231`), and Stagewatch already compiles a linked source file into its test
  runner (`Stagewatch/tests/Stagewatch.Tests/Stagewatch.Tests.csproj`, `<Compile Include … Link=…>`).
- Unknown words: Patterns answers an unknown line `ERR @id unknown command '…'` (`Patterns/src/Patterns.App/Services/CommandRouter.cs:99-100`,
  queries pass the pairing gate, `ControlService.cs:501`); PlayMap sends unknown words to the endpoint, which refuses them
  (`Mapper/src/PlayMap.Net/Commands/CommandRunner.cs:10-14`), and its strict WS channel answers `ERR not paired` before pairing
  (`WireSession.cs:229-233`). Stagewatch's `/ws` ignores unknown query keys (it reads named keys only, `WebServer.cs:1146-1213`); every
  JSON connector ignores unknown message types and fields (`StagewatchLinkService.cs:523-538`, `StagewatchLink.cs:377-433`,
  `nodes.js:331-338`). Those are the extension points the profile uses — and the only ones.

---

## 1. Decisions

1. **No new carrier, port or message shape: two bindings written onto today's bytes.** The *line binding* is §3's `OK`/`ERR` grammar
   (Patterns TCP 9697; PlayMap TCP 9941; PlayMap `/ws` subprotocol `playmap.lines.v1`, one text frame per line). The *JSON binding* is
   Stagewatch's `/ws` node link (§7.1, peers-not-screens). HTTP side calls ride beside either. *Why:* every app already speaks one or both;
   a third carrier would be a flag day. *Alternative:* one new WebSocket protocol for all — four new servers and clients, no gain the
   bindings lack.

2. **Most of the profile is unilateral; only four features need both ends, and they are negotiated after pairing.** Liveness, redial,
   outcome mapping, request ids, sample admission, logging, carrier choice, token and nonce checks are each one end's behaviour and apply at
   level 0. `stuck` (line binding), `final`, `dedupe` and `prove` need the other end and are used only when both said so. *Why:* each app
   converges in its own release (Patterns' rounds, Mapper's round-3 gate, AGMessage's branch, Stagewatch's releases) with no ordering
   between them. *Alternative:* a profile version that both ends must meet — a flag day across four processes.

3. **The level is said on the link, after authentication, in one verb or one field.** Line: `@id PEERLINK v=1 t=<kind> id=<node id>
   can=<features>` after `OK paired` / `OK open`, answered `OK @id PEERLINK v=1 t id can` or, by a level-0 acceptor, `ERR @id unknown
   command …` (Patterns) / `ERR @id …` (PlayMap). JSON: the connector adds `peerlink` to the upgrade's `can=` list (windows-for-peers'
   list, `design/windows-for-peers.md:430-431`, raised from 4 to 8 words); a level-1 acceptor appends `"peerlink":{"v","id","can"}` to
   the verdict (peers-not-screens decision 7, `design/peers-not-screens.md:169`). *Why:* the hello's `proto` is unauthenticated and must
   never decide a safety question (spec-1-3 decision 3, `design/spec-1-3.md:210`); a word said after pairing can decide which extras are
   used. *Alternatives rejected:* extending `HELLO` (Patterns takes the argument, less a ` module=…` word, as the name, `ControlService.cs:461-468`, and the
   proof covers the name — every old acceptor would refuse the proof); extending the `NONCE` line (three readers take everything after
   `NONCE ` as the nonce, `PatternsLink.cs:309`, `WireClient.cs:314`, `main.js:217` — every proof would break); PlayMap's `WHOAMI`
   (`Mapper/src/PlayMap.Engine/EngineCore.Companion.cs:110` already says `"proto"`) — Patterns has no `WHOAMI`, and its answer is a
   controller's, not a link's.

4. **Level 0 is never refused, and is never trusted more than level 0.** A level-1 connector talks to a level-0 acceptor with every
   unilateral rule on and the features off, and shows a grey "PeerLink 0" tag (information, not a fault). The one guard: an id seen at
   level 1 that answers lower is shown amber, and an id that once proved itself and no longer can is not followed for facts until the
   operator accepts (13.8.5, 13.17.4) — the same shape as a pin.

5. **AGMessage's `PeerLink` moves from HTTP polling onto the line binding, before 0.5.0 ships if it can.** It sends the operator's
   full-control pairing token in a header, over plain HTTP whenever the node's hello names no TLS (D14), polls instead of hearing, has
   no liveness and misreads Patterns' answers (§0.4). On the line binding the token never crosses the wire (a proof), STATE and CONTEXT
   arrive as they change, PING gives liveness and the clock, and `OK @id`/`ERR @id` are the same words Patterns' and PlayMap's own wire
   use. Carriers: Patterns TCP `wire`; PlayMap `playmap.lines.v1` over pinned WSS on its `https` door when the hello names `tls`, else TCP
   `wire` (as playout-links D4 does for Stagewatch, `design/playout-links.md:113-118`). If 0.5.0 ships first, its smallest guard is one
   condition: no token header to a non-loopback base without pinned TLS. *Why:* the transport fix is cheaper than a scoped-credential
   scheme in two acceptors, and it is the carrier those acceptors already harden. *Left:* a messenger-scoped credential on Patterns and
   PlayMap (agmessage decision 10 says the token is "too strong for a messenger", `design/agmessage.md:325-330`) — §8.

6. **Show liveness on the line binding is a fifth PONG word, and only a measured node offers it.** `OK @id PONG t1 t2 t3 STUCK`, the
   form Stagewatch's Companion port already uses (`CompanionServer.cs:389-399`), sent only to a connector that negotiated `stuck`
   (PlayMap's and Patterns' pong parsers require exactly four words, `WireClient.cs:384`, `ShowBusNetwork.cs:302`, so an old reader must
   never see it). A node announces `stuck` only once its `stuckMs` is set from a measured honest stall of its show writer: Patterns'
   own papers say honest UI stalls run "several seconds" (`Patterns/src/Patterns.Core/Services/Resilience.cs:37-45`), so a 3 s number
   copied from Stagewatch (`ShowState.cs:217`) would blank a VT countdown on a page switch. *Why:* measured, then governed. *Alternative:*
   an untagged `BEAT live=0` push — a new line kind every reader must learn, when the pong already arrives every 2 s.

7. **Outcomes are the connector's reading; the wire's answers do not change.** Five outcomes — `not-sent`, `refused`, `accepted`,
   `applied`, `unconfirmed` — with one mapping table (13.11.9) that encodes every acceptor's real answers, Patterns' `/api/cmd` JSON and
   `CUE GO`'s `outcome` included. `observed` stays a layer-3/4 fact (a connector that sees the effect in the peer's state may say so).
   *Why:* the defect is in the readers (D8, §0.4); the acceptors already say the truth.

8. **Request ids become run-unique at no cost to any acceptor.** `<prefix><n>-<inst>`, inst being §2's 8 hex per process start; every
   acceptor validates only charset and length (`ShowBusNetwork.cs:261`, `ShowState.cs:1207-1208`). Dedupe (`dedupe`) is an acceptor
   feature, not a requirement, because 13.11.5 forbids a connector to resend a command by itself.

9. **`PROVE` lets a line acceptor prove itself, as a feature in 1.3 and a requirement in 1.4.** A connector on plain TCP today takes a
   blackout from whoever answers the address (PlayMap follows Patterns' facts, `Mapper/src/PlayMap.Net/Links/PatternsLink.cs:217-247`;
   Stagewatch moves its running order on a clip end). `@id PROVE <32 hex>` after pairing → `OK @id PROVE <hmac>` over a domain-separated
   message; an id that proved once and cannot now is not followed. One case in each acceptor; it stops a box that answers the address
   without the token, and the text says plainly that only TLS stops a relay.

10. **Boundaries and admission rules take the majority, or the words of the contract.** "Stale after 6 s" is inclusive of live (D1);
    "exceeds 2 s" is strict (D2); the sample admission is the union (D3). Each is a one-line change in the minority apps, fenced by
    vectors.

11. **The kit is data plus two interpreters, mastered in Stagewatch, copied byte-identical into each tree, and pinned by one hash in the
    spec's numbers block.** Each tree's existing `ShowBusSpec` test (spec-1-3 decision 10, `design/spec-1-3.md:324-338`) also hashes
    its kit copy against `peerlink.kit` in its copy of §14, so a kit and its spec move together and an accidental local edit fails that
    tree's build; a stale pair is seen only by `tools/showbus-copies.sh` in the interop lane — the same honest limit spec-1-3 states.
    *Why one C# file and one JS file, not a runner per repo:* three C# trees would otherwise each write their own reading of the scripts,
    which is how §0.4 happened. The C# file is written to the stricter analyzer set (Patterns' and Mapper's `.editorconfig` and
    `BannedSymbols.txt`; Stagewatch has none), uses only the BCL, and is linked into each test project as Stagewatch already links
    `HelpContent.cs`. *Alternative:* a Node process spawned by C# tests — Node on every C# CI job and a process per test; rejected.

12. **Virtual time is a seam each link grows once: `TimeProvider` in C#, an injected clock (or `node:test` mock timers) in JS.** PlayMap
    already has it; Stagewatch's `PlayoutLink` and Patterns' link take a `TimeProvider` defaulting to today's clock. Pure timing rules are
    tested on pure functions first, so no adoption waits on the seam.

13. **The twin is a named exemption, not a PeerLink.** Its 1 s beat and 5 s silence are failover's own numbers (`Twin.cs:484-499`),
    its mutual proof is its own (`Twin.cs:162-196`), and it has one peer, so no stampede. It adopts 13.11 (a node's forwarded `ACT`
    hears its outcome — today `Requested … sent` and nothing more, `Patterns/src/Patterns.App/Services/TwinService.Followers.cs:47-55`)
    and 13.18, in a Patterns round of its own; its §8 row says so.

14. **Each repo adopts in its own process, in show-risk order; nothing waits for the kit's last row.** §6.

---

## 2. The profile — Show Bus 1.3 §13 (normative)

> ## 13. PeerLink — one link between two nodes
>
> Discovery (§2) says a node exists and where its doors are. A **PeerLink** is one authenticated connection between two nodes that carries
> this contract's messages (§3–§6, §11, §12), together with the HTTP calls that ride beside it to the same node. This section is the same
> for every PeerLink of every node. It changes no message of §3–§6: it says how a link carries them. MUST, MUST NOT, SHOULD and MAY are
> used as in RFC 2119. Names in `code` are §14 keys. Times are on the node's monotonic clock unless called wall time.
>
> ### 13.1 Words
>
> - The **connector** dials; the **acceptor** answers.
> - The **line binding** is §3's line grammar (`OK [@id] …`, `ERR [@id] …`): Patterns' TCP `wire` (9697), PlayMap's TCP `wire` (9941),
>   and PlayMap's `/ws` with subprotocol `playmap.lines.v1`, where one text frame is one line. The **JSON binding** is a node link on
>   Stagewatch's `/ws` (§7.1).
> - A **side call** is an HTTP request to the same node with the link's scheme, host, port, pin and credential.
> - **Verified**: the acceptor checked the connector's credential (a node key bound to its id, the operator's credential, a pairing proof)
>   or says it needs none (an open node, a trusted address — said as such); the connector checked the acceptor (a pin, or 13.17).
> - A node's **show writer** is its single writer of the show; its **stuckMs** is how long it may not turn over before the node says its
>   show is stuck. Each node's §8 row names both.
> - The **level** is what the two ends agreed in 13.8: 0 or 1. A **feature** is a word a level-1 end announces: `stuck`, `final`,
>   `dedupe`, `prove`. Unknown feature words are ignored: they are abilities, not commands.
> - Decks (a Companion module), browsers, windows and Slides agents are not PeerLinks. 13.5.1, 13.7 and 13.18 still apply to every
>   connector, and 13.13 to every connection an acceptor takes.
>
> ### 13.2 What a PeerLink is not
>
> - **Not discovery.** A hello is a hint: where to dial, which kind, which fingerprint to expect on first use. It never chooses a plain
>   carrier for a pinned id and never decides a safety question (§2).
> - **Not the messages.** Verbs, frames, contexts, proposals and windows are §3–§6, §11, §12; a PeerLink carries them unchanged. It owns
>   only the envelope of a receipt (id, ok, stage, final, repeat), never its `effect` or `refs`.
> - **Not a view.** Lights, words, the Eye and an assistant's facts read what 13.16 publishes, never a link's private state.
> - **HTTP polling alone is not a PeerLink.** A node that only polls another's context is a *reader*: it shows the context's age (grey at
>   best) and sends no command through it.
>
> ### 13.3 Bindings and carriers
>
> | connector → acceptor | binding and carrier | the connector proves | a secret on the wire |
> |---|---|---|---|
> | any node → Stagewatch | JSON: `GET /ws?node=<id>&t=<kind>&can=peerlink[,…]` over pinned WSS on its `https` door, or plain to loopback; side calls HTTPS on the same door | a node key (or the operator's credential) in `Authorization: Bearer` (§7.1) | the key, only inside pinned TLS or to loopback |
> | any node → Patterns | line, TCP `wire` | `AUTH-PROOF` | none |
> | any node → PlayMap | line, `/ws` subprotocol `playmap.lines.v1` over pinned WSS on its `https` door when its hello names `tls` or its id is pinned; otherwise TCP `wire` | `AUTH-PROOF` | none |
> | Patterns ↔ Patterns (twin, node relays) | Patterns' twin carrier: its own beat, silence and dialling (failover's); 13.11 and 13.18 apply | its own mutual proof | as its §8 row says |
>
> 1. A connector chooses the carrier in this order: (a) a pin is held for the acceptor's id (or for `addr:<host>:<port>`): TLS only — a
>    hello without `tls`, or no hello, leaves the link `refused` "<node> offers no TLS now — not dialling plain", never plain; (b) the
>    hello names `tls` and its door: TLS, pinning that fingerprint on the first verified answer (13.7); (c) the operator typed `https://`,
>    `wss://` or a fingerprint: TLS; (d) a secret would ride and the address is not loopback: TLS on the same door first, and if no TLS
>    answers, plain **without** the secret where the acceptor serves anything keyless (public frames), `degraded` "no TLS — the key stays
>    on this machine"; (e) otherwise plain.
> 2. A PeerLink MUST NOT carry a key, token, PIN or view pass over a plain carrier to a non-loopback address. A proof is not a secret.
> 3. A plain carrier is allowed where no secret rides. The link's `carrier` is `plain`, `tls` or `local` (loopback); `plain` is
>    information, not a fault.
>
> ### 13.4 Identity
>
> 1. A node is named by its hello `id` and `t` (§2). On the JSON binding the connector names itself in the upgrade (`node`, `t`) and the
>    verdict says what the acceptor verified (§7.1). On the line binding the connector names its kind and id in `PEERLINK` (13.8) after
>    pairing; the acceptor lists them as *claimed by a paired connection*.
> 2. The acceptor names itself: in the verdict's `peerlink.id` (JSON), in the `PEERLINK` answer's `id` (line). A connector that dialled a
>    heard id and is answered with another treats it as §2's conflict: amber, both ids named, no fact followed from it (a blackout, a clip
>    end, an approval), no pin moved.
> 3. Pins, key bindings and the "proved before" mark (13.17) are kept per node id. A typed address with no heard id is kept as
>    `addr:<host>:<port>` and is rebound to `node:<id>` only over TLS with that pin held, once the acceptor's id is known.
>
> ### 13.5 Pairing by proof (line binding)
>
> **13.5.1 The connector**
> 1. Says `HELLO <name>`. The name (the words after `HELLO`, without a trailing ` module=…`, at most 64 characters) is what its proof
>    covers.
> 2. Answers a `NONCE <n>` line, or `OK [@id] <n>` to its own `[@id] NONCE` ask, only when `<n>` is exactly 32 lowercase hex characters;
>    otherwise it sends nothing for it and the link is `refused` "malformed NONCE". The answer is `[@id] AUTH-PROOF <p>`, `p` the lowercase
>    hex of HMAC-SHA256(key = UTF-8 of the normalised token, message = UTF-8 of `n` followed by the name).
> 3. The normalised token is the operator's text with `-` and every white-space character removed, upper-cased. A token that then holds
>    anything but `A`–`Z` and `0`–`9` is refused where it is typed ("a pairing token is letters and digits") and never used.
> 4. It sends one proof per nonce and stops after a refusal: `ERR … wrong token` makes the link `refused`, and no proof is sent again until
>    the token setting changes.
> 5. It MUST NOT send plain `AUTH <token>` to a non-loopback address, and never on a connection that heard a `NONCE` or an `OK` to its
>    `NONCE` ask. On loopback, with no `NONCE` within `link.nonce.wait.ms` and no answer to its ask within `link.ask.wait.ms`, it MAY send
>    plain `AUTH` (a node that trusts loopback, or one older than 1.1).
> 6. With no token for a node that sent `NONCE`, it shows `refused` "not paired — <node> wants its pairing token in <where>" and sends
>    nothing but `PING` (§7.2).
>
> **13.5.2 The acceptor**
> 1. A connection it does not trust hears `NONCE <32 lowercase hex>` (16 random bytes), fresh per connection, replaced after every proof,
>    right or wrong, and on every `NONCE` ask (answered `OK [@id] <32 hex>`). Once the node keeps §7.2, it is the first line and nothing of
>    the show precedes pairing.
> 2. It verifies in constant time; refuses plain `AUTH` except from loopback where it trusts loopback; after `link.proofs.wrong.close`
>    wrong proofs on one connection answers `ERR [@id] wrong token … — closed` and closes; where it keeps a per-address ledger, more than
>    `link.proofs.wrong.per.minute` from one address in a minute lock that address for the minute (WS close 4401, HTTP 429).
> 3. Its answers begin with their class (13.16.3): `paired`, `open`, `wrong token`, `not paired`, `too many wrong tokens`, `plain AUTH is
>    for this machine only`.
>
> ### 13.6 Keys and their binding (JSON binding)
>
> 1. A node key travels in `Authorization: Bearer`, never in a URL. The acceptor's verdict is the first message on a node link
>    (`{"type":"auth",…}`, §7.1); a key refused at admission or later is answered `ok:false` with `retry`, and the socket stays open with
>    the public frames — no socket is closed for a key reason.
> 2. A key bound to a node id speaks only for that id, and that id is spoken for only with that key (§7, §11). Unknown or revoked keys are
>    never counted toward an address's lockout.
> 3. The connector records a refusal before any further I/O: on `ok:false` or a 401 on any side call, every keyed request waits
>    max(`link.hold.refused.ms`, `retry`); a 429 waits its `Retry-After`. The hold is persisted with its end, so a restart loop cannot
>    sum past a lockout.
> 4. The first message decides the evidence: a verdict first is the socket's word (the latest verdict wins); a frame first is an acceptor
>    older than §7.1, and the connector's first authenticated side call (`GET /api/context`) is the evidence. No verdict and no frame within
>    `link.verdict.wait.ms`: close and redial (13.15).
>
> ### 13.7 TLS and pinning
>
> 1. A connector checks the certificate's fingerprint (`sha256:` + lowercase hex of SHA-256 of the DER), never a name or an authority,
>    over TLS 1.2 or 1.3.
> 2. The first fingerprint is taken from the hello (or the operator) and is pinned per node id only after a verified answer came over it.
> 3. A different fingerprint later is refused as CERTIFICATE CHANGED until the operator accepts it (typing at least 8 hex of the new one);
>    meanwhile the connector dials at most once per `link.hold.certificate.ms` and sends nothing.
> 4. Once an id is pinned, its links are TLS only (13.3.1a).
>
> ### 13.8 Versions and negotiation
>
> 1. Three versions, three jobs. The hello's `proto` (§2) says which shapes a node speaks and decides nothing about safety. The **level**
>    (this section) says which features two ends use on one link; it is said after authentication. Message versions (`v` in frames and
>    contexts) stay as §4 and §6 say.
> 2. **Line binding.** After `OK [@id] paired` or `OK [@id] open` — or, on a connection that heard no `NONCE` within `link.nonce.wait.ms`,
>    then — the connector sends `@<id> PEERLINK v=1 t=<its kind> id=<its id> can=<features>`. A level-1 acceptor answers
>    `OK @<id> PEERLINK v=1 t=<its kind> id=<its id> can=<the features it will use on this connection>`: those it offers that the
>    connector listed. An `ERR @<id> not paired …` means "ask again after pairing"; any other `ERR @<id> …` is level 0; no answer within
>    `link.ask.wait.ms` is level 0 for this connection, logged once. `v` is 1–3 digits; `t` and `id` follow §2; `can` is at most 8
>    comma-separated words of `[a-z-]{1,16}`; unknown keys and words are ignored. An acceptor answers the highest `v` it speaks that is
>    not above the one offered; this section is `v=1`. An acceptor MUST answer `PEERLINK` on every paired or trusted connection and MAY
>    answer it before pairing.
> 3. **JSON binding.** The connector adds `peerlink` to the upgrade's `can` list (at most 8 words of `[a-z-]{1,16}`). A level-1 acceptor
>    appends `"peerlink":{"v":1,"id":"<its id>","can":[…]}` to every `ok:true` verdict on a node link. No such field, or no verdict, is
>    level 0.
> 4. **Level 0 is not refused.** Every rule of this section that one end keeps alone — 13.3, 13.4, 13.5.1, 13.6, 13.7, 13.9.1–13.9.5,
>    13.10, 13.11.1–13.11.6, 13.12–13.16, 13.18 — applies at level 0 too. Only the features need level 1 on both ends.
> 5. **Downgrade.** A connector keeps, per acceptor id, the highest level and the features it was verified at. Answered lower later, the
>    link is `degraded` "<node> spoke PeerLink 1 before and does not now — an older build, or not the same node", and works at the lower
>    level. `prove` is 13.17.4.
> 6. Features of `v=1`: `stuck` (13.9.6), `final` (13.11.7), `dedupe` (13.11.8), `prove` (13.17).
> 7. The `NONCE`, `HELLO` and `AUTH-PROOF` lines are byte-identical at every level: readers take everything after `NONCE ` as the nonce and
>    the `HELLO` argument (less a ` module=…` word) as the name a proof covers, so an added word would break every proof.
>
> ### 13.9 Liveness
>
> 1. **The beat.** The connector pings every `ping.min.ms` to `ping.max.ms`: line `[@id] PING <t1>`, JSON `{"cmd":"ping","t":<t1>}`. The
>    acceptor answers at once and never through its show writer: `OK [@id] PONG <t1> <t2> <t3>`, `{"type":"pong","t","t2","t3"}`. A JSON
>    acceptor also sends a frame or `{"type":"beat"}` at least every `link.beat.ms`.
> 2. **Link liveness** is the silence since the last complete message from the other end, whatever it was (a pong counts): `live` while
>    the silence is at most `link.stale.ms`, `stale` while at most `link.drop.ms`, then the connector drops the connection and redials
>    (13.15). 6000 ms of silence is live and 6001 ms stale; 12000 ms is stale and 12001 ms is dropped.
> 3. The connector evaluates liveness at least every `link.check.max.ms`. The beat and the check run on their own timer and never wait
>    behind a side call or a write.
> 4. An acceptor measures a connection by its own bounds (13.13): its write deadline and its line timeout.
> 5. **Show liveness** is separate: `live`, `stuck` or `unknown`. JSON binding: `{"type":"beat","live":false}` makes it `stuck`; only the
>    next state frame makes it `live`; a plain beat or a pong never clears it. While `stuck` the connector blanks every counting value it
>    took from that acceptor (it shows "?", never a frozen or counted-on time); the link stays `live`.
> 6. **Line binding, feature `stuck`.** The acceptor answers a PONG with a fifth word `STUCK` while its show writer has not turned over
>    for its `stuckMs`; a PONG without it makes show liveness `live`. An acceptor announces `stuck` only once its `stuckMs` comes from a
>    measured honest stall of its own show writer and its §8 row names that number (`show.stuck.ms.main` for Stagewatch's show thread).
> 7. A connector without show liveness from an acceptor (line binding without `stuck`) shows it `unknown` and claims nothing.
>
> ### 13.10 Clock offset
>
> 1. t1–t4 are Unix milliseconds on each end's wall clock: t1 sent, t2 when the acceptor read the ping, t3 as late as its writer allows
>    and never before t2, t4 received.
> 2. offset = ((t2 − t1) + (t3 − t4)) / 2; rtt = (t4 − t1) − (t3 − t2). A sample is refused when any stamp is ≤ 0, t4 < t1, t3 < t2,
>    rtt < 0 or rtt > `clock.rtt.max.ms`.
> 3. The believed offset is the sample with the smallest rtt among the last `clock.samples`, none received more than
>    `clock.sample.age.max.ms` ago; with none, the offset is unknown.
> 4. CLOCKS APART when |offset| > `clock.apart.ms`.
> 5. The connector publishes the believed offset and rtt where the link is shown (13.16.4).
>
> ### 13.11 Requests and receipts
>
> 1. **Ids.** 1–32 of `[A-Za-z0-9_-]`, unique for the connector's process and never repeated by a later process: `<prefix><n>-<inst>`
>    (a prefix of 1–4 letters, a counter from 1, §2's `inst`) is the form. An acceptor treats an id as opaque.
> 2. **In flight.** At most `link.inflight.max` commands per link wait for an answer. The next one is not sent and is told so; nothing
>    already waiting is ever evicted.
> 3. **Nothing queued.** A command to a link that is `off`, `connecting`, `down`, `held` or `refused` is `not-sent` at once (§4).
> 4. **Outcomes.** Each command ends in exactly one: `not-sent` (never written; it did not run), `refused` (the acceptor said no; it did
>    not run), `accepted` (the acceptor queued it; not yet run), `applied` (it ran), `unconfirmed` (written, then no answer within
>    `receipt.unconfirmed.ms`, or the link dropped first — it may have run). The connector shows "waiting for <node>'s answer" after
>    `receipt.deadline.ms`.
> 5. A connector MUST NOT send a command again by itself after `unconfirmed` or a drop. A person or a rule decides again, with the outcome
>    in view.
> 6. One journal row per command that reached the socket, written when its outcome is known; a command not sent leaves its refusal and
>    the status line.
> 7. **`final`.** An acceptor that answered `accepted` sends one follow-up within `receipt.final.ms` once it knows: line
>    `OK @<id> final <json>` or `ERR @<id> final <words>`; JSON `{"type":"receipt",…,"final":true}`. A connector waits for it that long
>    after `accepted`, then keeps `accepted` with the words "no final word in 30 s".
> 8. **`dedupe`.** An acceptor remembers, per verified peer, each request id it answered for `receipt.dedupe.ms` (at most
>    `receipt.dedupe.max`) and answers a repeat with the same receipt (JSON adds `"repeat":true`) without running it again.
> 9. **The mapping** (every connector reads answers by this table):
>
> | the answer | outcome | words |
> |---|---|---|
> | line `OK @id`, or `OK @id <words>` that is not JSON | applied | the words |
> | line `OK @id {…}` with `"stage":"accepted"` (PlayMap) | accepted | `effect` |
> | line `OK @id {…}` with `"outcome":"Requested"` or `"Confirm"` (Patterns `CUE GO`) | accepted | Patterns' words; `Confirm` adds "asks for a second GO — a person's" |
> | line `OK @id {…}` with `"stage":"applied"`, `"outcome":"Done"`, or neither | applied | `effect` or the words |
> | line `ERR @id <words>` | refused | the words |
> | line `OK @id final {…}` / `ERR @id final <words>` | as above, final | |
> | JSON receipt `"ok":true,"stage":"applied"` | applied | `effect` |
> | JSON receipt `"ok":true,"stage":"accepted"` | accepted (a `final` may follow) | `effect` |
> | JSON receipt `"ok":false` | refused | `error` |
> | HTTP 2xx `{"ok":true}` with neither `stage` nor `msg` (an acceptor before 1.1) | accepted | "took it — this node does not say whether it ran" |
> | HTTP 2xx `{"ok":true,"msg":"OK …"}` (Patterns `/api/cmd`) | applied | `msg` after `OK` and any `@id` |
> | HTTP 2xx or 4xx `{"ok":false,"msg":"ERR …"}` or `{"ok":false,"error":"…"}` | refused | `msg` after `ERR` and any `@id`, else `error` |
> | HTTP 401 | refused, and the link's refusal hold (13.6.3) | the body's `error` |
> | HTTP 403, 404, 409, 413 | refused | the body's words |
> | HTTP 429 | refused | "rate limited — again in N s" (`Retry-After`) |
> | HTTP 408, 5xx, or no response after the request was written | unconfirmed | |
> | no connection, a hold, `link.inflight.max` reached, the link `off`, `connecting`, `down`, `held` or `refused` | not-sent | why |
> | written, then no answer within `receipt.unconfirmed.ms`, or the link dropped first | unconfirmed | "no answer in 30 s — it may have run" / "the link dropped before <node> answered" |
>
> ### 13.12 Ordering and latest-wins
>
> 1. Replies leave in the order their lines came (line binding). Receipts are matched by id, never by order.
> 2. A connector takes a state frame whose counter (§4) is not lower than the last one taken on the same connection; the first frame on a
>    new connection is taken whatever its counter (the peer may have restarted).
> 3. An acceptor keeps one slot per push kind per connection (a slow reader gets the newest, never a backlog) and never drops a reply
>    (13.13).
>
> ### 13.13 Backpressure and bounds
>
> | bound | value |
> |---|---|
> | a command line an acceptor reads | ≤ `link.line.command.bytes`; longer: `ERR … — closed` |
> | a pushed line or WS message a connector reads | ≥ `link.line.read.bytes`; longer is counted and dropped, never fatal |
> | a JSON message a node link sends to Stagewatch's `/ws` | ≤ `link.ws.in.bytes` |
> | replies waiting at an acceptor | ≤ `link.reply.queue.max`, then the connection is cut |
> | one write | ≤ `link.write.deadline.ms`, then the connection is cut |
> | commands in flight per link | ≤ `link.inflight.max` (13.11.2) |
> | side calls in flight per link | `link.sidecall.inflight`, each ≤ `link.sidecall.timeout.ms` |
> | connections an acceptor takes | its own ceiling, said in its own words before it closes an extra one (or, where the carrier gives no words, closed at once) |
>
> ### 13.14 Side calls
>
> 1. A side call uses the link's scheme, host, port, pin and credential, and goes out only after the link is verified — or, on the legacy
>    JSON path (13.6.4), as the evidence call.
> 2. Side calls run on their own lane: `link.sidecall.inflight` at a time per link, latest-wins for a post that replaces the last (a
>    context post), never ahead of or behind the beat (13.9.3).
> 3. One door for refusals: a 401 starts the link's refusal hold; a 429 waits `Retry-After`; a 403 says its words and holds nothing; a
>    timeout or a 5xx is retried on the call's own schedule for a read, a context post or an owed §11 result (until the desk takes it, at
>    most `proposal.result.retry.max.ms`), and never for a command (13.11.5).
> 4. A side call that carries a command carries its id as `X-Request-Id` (Stagewatch, PlayMap) or as the line's `@id` (Patterns
>    `/api/cmd`), and is read by 13.11.9.
>
> ### 13.15 Reconnect
>
> 1. After a drop or a failed dial the connector waits min(`link.redial.max.ms`, `link.redial.first.ms` × 2^attempt) × (1 − p + 2p·j),
>    with p = `link.redial.jitter.pct` / 100 and j uniform in [0, 1) from a generator seeded per process.
> 2. `attempt` returns to 0 only after a connection that was verified and lasted `link.stable.ms`. A connection accepted and closed, or
>    verified and dropped sooner, counts as a failure.
> 3. Holds replace the backoff: the acceptor's `disconnect` → `link.hold.disconnect.ms`; a refusal → 13.6.3; a changed certificate →
>    13.7.3; a wrong token → no proof until the setting changes (the link may still dial, ping and show its state).
> 4. A hold that guards a lockout is persisted with its end time.
>
> ### 13.16 States, and what each side shows
>
> **13.16.1 The connector's link**
>
> | state | when | light, mark, words | counting values | a command |
> |---|---|---|---|---|
> | `off` | the operator turned it off | grey ○ "off" | none | not-sent "link off" |
> | `connecting` | dialling, handshaking, waiting for the verdict or `OK paired` | grey ○ "dialling <address>…" | none | not-sent |
> | `live` | verified, heard within `link.stale.ms` | green ✓ "<kind> <name> · PeerLink <level> · <carrier>" | counted on | sent |
> | `degraded` | live, but one of: level 0 where 1 was seen; no TLS so the key stays here; a read-only key; an unproven acceptor that proved before; CLOCKS APART; the other end's id differs | amber △ the one reason, worst first | counted on (durations stay right when clocks are apart) | sent |
> | show `stuck` | live, the acceptor says its show writer is stuck | red ✕ "<name> says its show is stuck — no time shown" | blanked | sent; the words say an answer may be `accepted` |
> | `stale` | verified, silent `link.stale.ms`–`link.drop.ms` | amber △ "quiet for N s" | blanked | sent; may end `unconfirmed` |
> | `down` | dropped or unreachable, redialling | red ✕ when the node's configuration needs this link (a playout link, a followed timer); amber △ otherwise; "not answering — dialling again in N s" | none | not-sent "nothing is queued" |
> | `held` | the acceptor's disconnect, a refusal hold | amber △ "linking again in N s — <why>" | public frames only, when the acceptor still sends them | not-sent |
> | `refused` | wrong token, key refused, CERTIFICATE CHANGED, plain refused, locked out, PROVE failed, malformed NONCE | red ✕ the acceptor's words and where to fix it | public frames only, when the acceptor still sends them | not-sent |
>
> Level 0 adds a grey `i` "PeerLink 0" tag to `live`; `plain` adds `i` "plain"; neither is a fault.
>
> **13.16.2 The acceptor's side.** An acceptor lists every PeerLink it holds — the peer's verified id and kind, or its claim; the level;
> the carrier; how it was verified (`key`, `operator`, `proof`, `trusted`, `open`, `none`); when it was last heard — on its own operator
> surface and in its context.
>
> **13.16.3 Refusal classes**, read from the first words of an `ERR`, a verdict's `error`, an HTTP body or a WS close: `wrong token`,
> `not paired`, `too many wrong tokens` (close 4401, HTTP 429), `plain AUTH is for this machine only`, `unknown or revoked API key`, a
> binding refusal (`this key speaks for …`, `… speaks with its own key`), `CERTIFICATE CHANGED`, `requires wss://` (403 under Require
> HTTPS), a busy acceptor (its ceiling's words). Each maps to one state and one hold above.
>
> **13.16.4 Published.** Each end publishes per link one context item `{"id":"link:<name>","kind":"link","label":"<Kind> <name>","light",
> "state"}` (`state` in the words above) and one `links` row to the peer's node (§6); its `peers` row carries `link` and `verified` (§6)
> and appends `level`, `carrier`, `offsetMs` and `rttMs` (empty when unknown). The Eye draws every node's links one hop from these rows;
> every light is drawn with its mark.
>
> **13.16.5** One log line and one journal row per state transition, never per beat.
>
> ### 13.17 PROVE — the acceptor proves itself (feature `prove`)
>
> 1. After pairing, `@<id> PROVE <c>` (`c` 32 lowercase hex, fresh each time) is answered `OK @<id> PROVE <p>`, `p` the lowercase hex of
>    HMAC-SHA256(key = UTF-8 of the normalised token, message = UTF-8 of `peerlink-prove` LF `c` LF `<acceptor id>`). A node that asks
>    no token answers `OK @<id> PROVE open`.
> 2. A wrong `p` makes the link `refused` "<address> answered as <name> but did not prove it holds the pairing token — nothing from it is
>    followed", drops it and holds `link.hold.refused.ms`.
> 3. Until a token acceptor that offers `prove` (or proved before) has proved itself on this connection, the connector shows what it
>    hears but acts on nothing that changes its own air or running order (a blackout fact, a clip end that moves the running order).
> 4. An id that proved itself before and no longer can (level 0, or no `prove`) is shown amber "<node> proved itself before and does not
>    now — Accept on the link to follow it anyway", and is not followed until the operator accepts.
> 5. PROVE stops a box that answers the address without the token. It does not stop a relay between two real ends on a plain carrier;
>    only TLS does.
>
> ### 13.18 Logging and secrets
>
> 1. Never in a log line, journal row, context, STATE, frame, hello, report or crash file: a key, token, PIN or view pass; the values of
>    `Authorization`, `X-Patterns-Token`, `X-PlayMap-Token`, `X-Stagewatch-Pin`; URL query values of `pin`, `key`, `token`, `win`,
>    `view`, `code`. Nonces and proofs are not logged either.
> 2. A line kept in a log is its verb and id with the argument replaced: `AUTH (token)`, `AUTH-PROOF (proof)`, `PROVE (nonce)`.
> 3. One line per state transition: the link's name, the peer's kind and id, the address, from → to, the refusal words. Counters
>    (malformed lines, oversize lines, refused proofs, refused clock samples) are shown with the link and logged at most once per
>    `log.repeat.min.ms` per kind.
> 4. A link's words never echo a line that may carry a secret.
>
> ### 13.19 Conformance
>
> Each node holds every one of its links to the PeerLink kit named by `peerlink.kit` (§14): the pure vectors, and the scripts for each
> role its links play. A node's §8 row lists any kit topic it does not keep yet and what ends it; nothing else may be left out.

### 2.20 The rows appended to §14 (machine-read)

Existing spec-1-3 rows reused unchanged: `link.beat.ms 2000`, `link.stale.ms 6000`, `link.drop.ms 12000`, `link.redial.first.ms 500`,
`link.redial.max.ms 5000`, `ping.min.ms 1000`, `ping.max.ms 2000`, `clock.apart.ms 2000`, `receipt.deadline.ms 3000`,
`request.id.chars 32`. New:

```showbus
peerlink.level 1
peerlink.verb PEERLINK
peerlink.can.words 8
peerlink.feature stuck
peerlink.feature final
peerlink.feature dedupe
peerlink.feature prove
peerlink.kit <sha256 of conformance/peerlink/kit.json>
link.check.max.ms 1000
link.stable.ms 10000
link.redial.jitter.pct 20
link.connect.timeout.ms 5000
link.nonce.hex 32
link.nonce.wait.ms 1500
link.ask.wait.ms 5000
link.verdict.wait.ms 12000
link.proofs.wrong.close 5
link.proofs.wrong.per.minute 5
link.hold.refused.ms 60000
link.hold.disconnect.ms 30000
link.hold.certificate.ms 60000
link.inflight.max 32
link.reply.queue.max 256
link.write.deadline.ms 10000
link.line.command.bytes 65536
link.line.read.bytes 262144
link.ws.in.bytes 4096
link.sidecall.inflight 1
link.sidecall.timeout.ms 4000
receipt.unconfirmed.ms 30000
receipt.final.ms 30000
receipt.dedupe.ms 600000
receipt.dedupe.max 1024
proposal.result.retry.max.ms 1800000
clock.samples 8
clock.sample.age.max.ms 20000
clock.rtt.max.ms 60000
show.stuck.ms.main 3000
log.repeat.min.ms 60000
```

Where each number comes from: 1500 ms NONCE wait (`PatternsLink.cs:47`, `WireClient.cs:45`); 5 s ask and 32 in flight and 30 s
unconfirmed/final (playout-links D3, D5, `design/playout-links.md:101-130`); 12 s verdict wait (agmessage D7,
`design/agmessage.md:262-315`); 60 s and 30 s holds (peers-not-screens D12, agmessage D8, `StagewatchLink.cs:59`); 256 replies and 10 s
writes (`WirePeer.cs:20-23`, `ConnectionLedger.cs:63-64`); 64 KiB command lines (`ControlService.cs:731`, `ConnectionLedger.cs:61`);
256 KiB reads (`PatternsLink.cs:40`, playout-links D7); 4 KiB into Stagewatch's `/ws` (`WebServer.cs:28`); 4 s side calls
(`StagewatchLinkService.cs:289`, `StagewatchLink.cs:307`); 60 s rtt cap (`Resilience.cs:256`); 20 s sample age = 8 pings at 2 s plus one
beat (the playout-links critique found 10 s too tight for a best-of-8, `design/json/playout-links.json`); 30 minutes for an owed result
(agmessage D3, `design/agmessage.md:147-185`); 3000 ms Stagewatch `StuckAfter` (`ShowState.cs:217`); `link.check.max.ms` 1000 is
AGMessage's watch tick today (`nodes.js:227`), the slowest checker that meets the rule; `link.stable.ms` 10 s, ±20 % jitter: the sibling
reliability area's stampede simulation shows ±20 % already spreads 16 links to a peak of 5 per 250 ms where no jitter puts all 16 in one
(`design/peerlink-stampede.out.txt`), so the jitter rule three apps already keep stays.

---

## 3. The bindings, exact

### 3.1 Line binding, both ends at level 1 (Stagewatch → a Patterns desk with a token, once Patterns keeps §7.2)

Token `K7QM-3XWD-P9RA`, Stagewatch's `inst` `a41f09c2`, its id `3f9c2a1b7d0e`, Patterns' id `9f8e7d6c`:

```
← NONCE 3f9a0c5e7b2d4f6a8c1e3b5d7f9a0c2e
→ HELLO Stagewatch
← OK
→ AUTH-PROOF f4196515d05ce2e593ac57af719ef450f4390826f1c20cce7079ac8b7ba75a13
← OK paired
← STATE {…}                                                     the greeting, now (§7.2)
→ @p1-a41f09c2 PEERLINK v=1 t=main id=3f9c2a1b7d0e can=stuck,final,prove
← OK @p1-a41f09c2 PEERLINK v=1 t=patterns id=9f8e7d6c can=final,prove        (no `stuck`: not measured yet)
→ @v2-a41f09c2 PROVE c0ffee00112233445566778899aabbcc
← OK @v2-a41f09c2 PROVE 731bb4184cfa827b3ed7f3b54100d667afa75bb78fe41c375fbd3f2d14121b60
→ @k3-a41f09c2 PING 1761400000000
← OK @k3-a41f09c2 PONG 1761400000000 1761400000412 1761400000413
→ @s4-a41f09c2 CUE GO 01.020
← OK @s4-a41f09c2 {"outcome":"Requested",…}                    → accepted
← OK @s4-a41f09c2 final {"outcome":"Done",…}                   → applied (feature `final`, when Patterns builds it)
```

### 3.2 The same connector, a level-0 acceptor (Patterns as of Round 88.1)

```
← STATE {…}                                                     greeting before NONCE (Patterns PLAN §103.7's open read)
← NONCE 3f9a0c5e7b2d4f6a8c1e3b5d7f9a0c2e
→ HELLO Stagewatch                  ← OK
→ AUTH-PROOF f419…5a13              ← OK paired
→ @p1-a41f09c2 PEERLINK v=1 t=main id=3f9c2a1b7d0e can=stuck,final,prove
← ERR @p1-a41f09c2 unknown command 'PEERLINK v=1 t=main id=3f9c2a1b7d0e can=stuck,final,prove'
                                                                 level 0: live, grey "PeerLink 0"; no PROVE, no STUCK, no final
→ @k2-a41f09c2 PING 1761400000000   ← OK @k2-a41f09c2 PONG 1761400000000 1761400000412 1761400000412
```

### 3.3 Line binding over WS (any node → PlayMap with TLS)

`wss://<address>:9940/ws`, `Sec-WebSocket-Protocol: playmap.lines.v1`, the certificate checked against the pin; the same lines as §3.1,
one per text frame; close 4401 after five wrong proofs or a locked-out address (`Mapper/docs/wire/companion.md:17-19`). On TCP 9941 the
same lines; PlayMap greets `STATE` after `HELLO` there (its owner's decision 17, `WireSession.cs:263-276`), so a connector that wants no
STATE says `@o SUBSCRIBE OFF` and then `@c SUBSCRIBE CONTEXT` (patterns-playmap-side D19's sequence).

### 3.4 JSON binding, both ends at level 1

```
GET /ws?node=9f8e7d6c&t=patterns&can=peerlink HTTP/1.1          (TLS to the pinned certificate)
Authorization: Bearer swk_…
← {"type":"auth","ok":true,"scope":"node","node":{"t":"patterns","id":"9f8e7d6c","verified":"key"},"peerlink":{"v":1,"id":"3f9c2a1b7d0e","can":["stuck","dedupe"]}}
← {"type":"state","v":2,"seq":881,…}
→ {"cmd":"ping","t":1761400000000}
← {"type":"pong","t":1761400000000,"t2":1761400000411,"t3":1761400000411}
← {"type":"beat","live":false}                                  show stuck: counting values blanked, link live
← {"type":"beat","live":false}
← {"type":"state","v":2,"seq":882,…}                             show live again
```

A level-0 Stagewatch (2.17) sends a frame first and no verdict: the connector takes the HTTP evidence path (13.6.4).

### 3.5 Side calls

```
POST /api/message/Wrap%20up/info/slow/ack     Authorization: Bearer swk_…     X-Request-Id: m7-a41f09c2
← 200 {"type":"receipt","id":"m7-a41f09c2","ok":true,"stage":"applied","effect":"…","error":null,"refs":{"messageId":41}}
  the same id again within 10 min, at an acceptor with `dedupe`:
← 200 {"type":"receipt","id":"m7-a41f09c2","ok":true,"stage":"applied","effect":"…","error":null,"refs":{"messageId":41},"repeat":true}

POST /api/cmd      X-Patterns-Token: …       body: @c9-a41f09c2 VIDEO END 10      (Patterns over TLS or loopback only)
← 200 {"ok":true,"msg":"OK @c9-a41f09c2 Clip ends in 10 s","id":"c9-a41f09c2"}      → applied, "Clip ends in 10 s"
```

---

## 4. Per app: today, and the changes, in each repo's own process

### 4.1 Stagewatch (its release train with playout-links, peers-not-screens and spec-1-3)

**L1 `PatternsLink` → `PlayoutLink` (line connector).** Today: no jitter, 250 ms start, reset on connect (D4, D5); plain `AUTH` to any
host after 1.5 s (D13); any NONCE ≤ 128 characters proved (D12); ids restart (D9); 64 pending then silent eviction (D10); a drop reports
every pending command as lost (D8); no stale state, no show liveness; `Stopwatch` and static times (§0.5). Changes, inside playout-links'
`PlayoutLink` unit (it already decides D3–D5, `design/playout-links.md:98-134`): a pure `Timer.Core/PeerLinkRules.cs` (`Redial`,
`LinkState`, `Outcome.Read`, `Token.Normal`, `Nonce.IsValid`, `RequestIds`); `PEERLINK` after pairing; `PROVE` when offered and the
"proved before" mark in the link's settings beside its pin; 5-word PONG read; `final` read; `TimeProvider` in the constructor; the
liveness check stays on the reader's 500 ms read timeout (`PatternsLink.cs:249`).

**L2 `/ws` + HTTP (JSON acceptor).** Today: stuck beat, pong off the show thread with t2 = t3, 64 slots, 4 KiB inbound, one-slot mailbox
— all already §13. Changes, inside peers-not-screens' SW-3 (`design/peers-not-screens.md:169-199`): the verdict appends `peerlink`;
`can` keeps up to 8 words; `dedupe` for side calls that run commands (`/api/message`, `/api/proposals`, the command routes) keyed by
(key id, `X-Request-Id`), announced only when built; the `final` follow-up receipt on WS and Companion is playout-links D5's.

**Kit master.** `conformance/peerlink/**` (§5), `tests/Stagewatch.Tests/PeerLinkKit.cs` (pure vectors against `PeerLinkRules`,
`ClockOffsetEstimator`, `PeerSummary.Liveness`, `CommandReceipt.CleanId`; scripts S1–S6 against an in-process `WebServer`; scripts
L1–L10 against `PlayoutLink`). The vectors need one change in Stagewatch: `ClockOffsetEstimator.Add` also refuses stamps ≤ 0, t4 < t1 and
t3 < t2 (`Resilience.cs:253-257`). `tools/showbus-copies.sh` (spec-1-3) compares the kit's hash in every tree.

### 4.2 Patterns (rounds; the maintainer's numbers win — patterns-playmap-side's map is used)

**L3 `StagewatchLinkService` (JSON connector).** Today: dials `?cid=patterns` (`StagewatchLinkService.cs:276`, G32); TLS only when the
hello names it or a pin is held for a typed address (`:225-243`), so a key rides plain `ws://` to a typed address (D14); every message,
pongs included, refreshes the last word (D6); the ping, the drop check, the context read and post and the approval queue run in one
loop (D7); backoff resets on any normal end (D5); owed results posted once (D15); static `ShowClock` (§0.5).
- **Round 89.1** (already planned, G04): the stuck beat with a single writer (`design/patterns-playmap-side.md:247-276`) is 13.9.5.
- **Round 90** (the 1.3 contract from Patterns' side), one unit "PeerLink 1": a `TimeProvider` in the constructor (default reads
  `ShowClock`); the beat and the watchdog on their own timer, side calls on a `SideCallLane` (Round 91.2's `SpeakerLane` posts through it
  later); `/ws?node=<id>&t=patterns&can=peerlink` and the verdict (peers-not-screens' Patterns unit); TLS first when a key is set
  (13.3.1d); reset after verified + 10 s; owed results kept and retried (13.14.3); one log line per transition. Every reader: STATE
  `bus.stagewatch` appends `level` (after `key`, at the end of the row, the wire's shape rule), the Eye's show-app card and the context's
  peers row show level and carrier, the module gains `stagewatch_level`, the assistant's brief names them.

**L4 `ControlService` wire 9697 (line acceptor).** Today: STATE before NONCE (`ControlService.cs:358-363`); PING answered with t3 taken
when the reply is queued, not written (`:410`); no `PEERLINK`, `PROVE`; unknown words answered `ERR @id unknown command`.
- **Round 90**, same unit: `PEERLINK` (a `RemoteCommandKind.PeerLink` query answered on a paired or trusted connection: `t=patterns
  id=<ShowBusIdentity> can=prove`), `PROVE` (`WireProof.Prove`, beside `WireProof.Proof`), STATE `decks[]` rows append `level`, `t`, `id`
  (claimed). `stuck` is not announced: "no measurement this round" goes in PLAN §NN, with a Super Check row that records the desk thread's
  stall distribution so a later round can set `stuckMs`. `final` is announced only when `CUE GO`'s Requested gains a later Done.
- Spec-1-3's §7.2 reads-after-pairing (Round 90.2, scope A or B, an ADR) is that design's, not this one's.

**Core vectors (Round 90):** `PairingToken` refuses a token with anything but A–Z0–9 after normalising, at the Remote page and `NEW TOKEN`
(D11); `LinkClock.Sample` admission (D3); `LinkClock.Apart` `>=` → `>` (D2); kit copy `tests/conformance/peerlink/` linked into
`Patterns.Core.Tests` (pure) and `Patterns.App.Tests` (scripts A1–A8 against `ControlService` with `TrustLoopback = false` in a
`finally`, W1–W7 against `StagewatchLinkService`). The closing sentence: the PeerLink transitions are one journal row each and a Super Check
row "Show network links: level, carrier, state", read beside the Nodes page.

**L5 twin and node relays.** Exempt from 13.3, 13.9's numbers and 13.15 (decision 13). A later round gives a node's forwarded `ACT` its
outcome (13.11) through a twin word, and applies 13.18. Its §8 row names both.

**Round 92 (G35):** the `ShowBusNode` device profile is a line connector from its first commit: `HELLO Patterns <desk name>`, proof,
`PEERLINK`, `PROVE`, `SUBSCRIBE OFF` + `SUBSCRIBE CONTEXT`, ids `d<n>-<inst>`, outcomes; it runs L1–L10.

### 4.3 PlayMap (after round 3's integration pass, or by its owner's exception; Mapper's §4 ownership forbids earlier edits)

The four units go in the same request patterns-playmap-side makes for PM-F1–F4 (`design/patterns-playmap-side.md:282-302`), one `fix-*`
branch each, or with the integration pass.

**PM-PL1 (Core, pure):** `Liveness.Of` inclusive (`ShowBus.cs:226-229`, `<` → `<=`, D1); `ClockOffsetEstimator.Add` refuses stamps ≤ 0
and rtt > 60 s (`ClockOffsetEstimator.cs:48-51`, D3); `Pairing` refuses a non-alphanumeric token at entry (D11); kit copy at
`tests/interop/peerlink/` beside `tests/interop/osc/`, read by `PlayMap.Core.Tests` as `OscTests` reads its fixtures.

**PM-PL2 (`WireClient`, L7 line connector):** `WireReply` gains an `Outcome`; a reply timeout becomes `unconfirmed` at 30 s with
"waiting" at 3 s, a drop after the write `unconfirmed`, a dead link `not-sent` (`WireClient.cs:145-170,332`, D8); NONCE 32-hex check
(`:303-315`, D12); `PEERLINK` after pairing; ids `IdPrefix + n + "-" + inst` (`:153`, D9); `attempt` resets only after verified + stable
(`:247`, D5); a 5-word PONG accepted (`:384`); `PROVE` when offered; `OnConnected` sends `SUBSCRIBE` after `PEERLINK`.

**PM-PL3 (`StagewatchLink`, L6 JSON connector):** peers-not-screens PM-1 (`node=`, `t=playmap`, `can=peerlink`, the verdict); only a frame
clears `_stuck` (`StagewatchLink.cs:381`, today a plain beat does); context and approvals on a side-call lane apart from the beat
(`:438-475`, D7); TLS first for a typed address with a key (G31, patterns-playmap-side D20, `design/patterns-playmap-side.md:396`); owed
results retried and `_ran` keyed by desk id, proposal id and `created` (agmessage D3).

**PM-PL4 (`WireSession`, L8 line acceptor):** `PEERLINK` and `PROVE` in `HandleSession` (`WireSession.cs:252-297`); the client table
(`registry.Hello`, STATE `clients`) appends `level`, claimed `t` and `id`; `final` for verbs whose first answer is `accepted`; `stuck` not
announced until the engine control lane's stall is measured — a lab line, "not run" until then (Mapper's "no lab this round").

### 4.4 AGMessage (0.5.x; the branch's owner)

**AG-PL0 (smallest, before 0.5.0 ships):** `PeerLink._resolve` refuses to send `X-Patterns-Token`/`X-PlayMap-Token` to a base that is not
loopback and not pinned TLS: status `refused`, "this node offers no TLS — AGMessage will not send its pairing token in clear"
(`peerlinks.js:108-121`). One condition.

**AG-PL1 (pure):** kit copy `app/test/conformance/peerlink/`, `app/test/peerlink-kit.test.js` (node:test, the JS runner): `peerStatus`
inclusive (`nodes.js:58`, D1); `ClockSampler.add` admission (`resilience.js:165-167`, D3); token and nonce rules; `readReceipt` per
13.11.9 (`peerlinks.js:62-71`, `nodes.js:188-199`) — Patterns' `msg` read, `{"ok":true}` alone `accepted`.

**AG-PL2 (`StagewatchLink`, L9 JSON connector; with agmessage AG-5, `design/agmessage.md:262-315`):** `node=`, `t=messenger`,
`can=peerlink`; the verdict; a stuck beat sets the board's show `stuck` (no countdown) and a pong refreshes the link only
(`nodes.js:333-336`, D6); the delay resets after verified + 10 s, not on open (`:329`, D5); ids `ag<n>-<inst>` (`:201`).

**AG-PL3 (`PeerLink` → line binding, L10):** a new `app/lib/linewire.js` (net/tls/`ws` carriers, a 256 KiB line reader, HMAC proof,
`PEERLINK`, `PROVE`, the beat and 13.9, 13.11 outcomes, 13.15 redial); `PeerLink` keeps its public surface (`view()`, `send(line)`,
`matchCommand`) and its fail-closed listed-verb rule, but dials Patterns' `wire` (`HELLO AGMessage`, `@c CONTEXT` every 5 s, STATE lines
counted and dropped) or PlayMap's lines (pinned WSS, else TCP; `SUBSCRIBE OFF` + `SUBSCRIBE CONTEXT`); no token in any header, ever;
`test/helpers.js`' `fakeNode` gives way to the kit's scripted acceptor, so AGMessage's tests meet Patterns' and PlayMap's real grammar
(§0.4).

---

## 5. The conformance kit

### 5.1 Where it lives and how it travels

- **Master:** `Stagewatch/conformance/peerlink/`:
  - `kit.json` — the manifest: `{"kit":"peerlink","v":1,"spec":"1.3","files":{"vectors/proof.json":"<sha256>",…}}`;
  - `vectors/*.json` — pure cases;
  - `scripts/*.json` — conversations the scripted peer plays;
  - `selftest/*.json` — transcripts with the verdict each interpreter must reach;
  - `runner/PeerScript.cs` and `runner/peer-script.cjs` — the two interpreters;
  - `README.md` — the format.
- **The pin:** `peerlink.kit <sha256 of kit.json>` in §14. Each tree's `ShowBusSpec` test reads that row from its own copy of the spec and
  compares it with the hash of its kit copy; every file's hash is checked against `kit.json`.
- **Copies:** Patterns `tests/conformance/peerlink/`; Mapper `tests/interop/peerlink/`; AGMessage `app/test/conformance/peerlink/`. Every
  copy is byte-identical (UTF-8, LF, as spec-1-3's body rule). A local edit fails that tree's build; a stale copy is found by
  `tools/showbus-copies.sh` in the interop lane only — said in each tree's papers.
- **Changing the kit** is a master change in Stagewatch, copied by each tree's own process (a Patterns round, Mapper's lane, an AGMessage
  release); a tree that cannot keep a new topic lists it in its §8 row until it can.

### 5.2 Vector files (pure; no sockets, no time)

Each file: `{"kit":"peerlink","v":1,"topic":"<topic>","rule":"13.x","cases":[{"name":"…","in":{…},"out":{…}}]}`. A runner looks up
the topic's adapter, feeds `in`, compares `out`; numbers compare within the tolerance a topic names (`"tol":1` for ms).

| topic | `in` → `out` | each repo's function under test |
|---|---|---|
| `token` | operator text → normalised or `"invalid"` | Patterns `PairingToken.Normal`; PlayMap `Pairing.Normalise`; Stagewatch `PeerLinkRules.Token`; AGMessage `linewire.token`; the module's `authProof` key |
| `nonce` | a NONCE argument → valid or not | the connectors' checks |
| `proof` | token, nonce, name → hex | `WireProof.Proof`, `Pairing.Proof`, `PatternsLink.Proof`, `authProof` |
| `prove` | token, cnonce, acceptor id → hex | the acceptors' and connectors' PROVE |
| `liveness` | silence ms → `live`/`stale`/`drop` | `ShowBusLiveness.StateOf`, `Liveness.Of`, `PeerSummary.Liveness`, `peerStatus` |
| `redial` | attempt, j → ms (`tol` 1) | `ShowBusLiveness.Redial`, `Liveness.RedialMs`, `PeerLinkRules.Redial`, AGMessage's `jitter` |
| `reset` | a sequence of connection results → the next attempt number | each link's reset rule |
| `clock` | samples (t1–t4, age) → admitted, believed offset and rtt, apart (`tol` 1) | the four estimators |
| `ids` | an id → valid or not; a generator's first ids → match `^[A-Za-z]{1,4}[0-9]+-[0-9a-f]{8}$` and differ across two `inst` | `WireReceipt.IsId`, `CommandReceipt.CleanId`, PlayMap's grammar |
| `receipts` | an answer (line, JSON, HTTP status + body, timeout, drop) → outcome and words | each connector's reader |
| `negotiate` | an offer and an acceptor's features, or an ERR → level and features | `PEERLINK` readers and writers |
| `refusal` | an ERR line / verdict / status → class, state, hold | each connector |
| `redact` | a log line, a header, a URL → what may be kept | Patterns `Secrets.Scrub`, PlayMap `VerbCatalog.Redacted`, Stagewatch's log, AGMessage's audit |

Sample cases, computed here (token `K7QM-3XWD-P9RA` typed as `k7qm-3xwd p9ra`, nonce `3f9a0c5e7b2d4f6a8c1e3b5d7f9a0c2e`):

```json
{"name":"proof Stagewatch","in":{"token":"k7qm-3xwd p9ra","nonce":"3f9a0c5e7b2d4f6a8c1e3b5d7f9a0c2e","name":"Stagewatch"},
 "out":{"proof":"f4196515d05ce2e593ac57af719ef450f4390826f1c20cce7079ac8b7ba75a13"}}
{"name":"proof PlayMap","in":{…,"name":"PlayMap"},"out":{"proof":"b0238ea5b571ccfce155ba2b6f5580a1a69de362880109f0eec139f6f26731c2"}}
{"name":"proof AGMessage","in":{…,"name":"AGMessage"},"out":{"proof":"2ef65b369e44cce991143688d1d6747f48ddef77f5364e07473d15a461481bb8"}}
{"name":"proof before HELLO","in":{…,"name":""},"out":{"proof":"55a5168b897ea1fee5b63b65c8ce355cb02069695ed11fd346cde6a553312c76"}}
{"name":"prove 9f8e7d6c","in":{"token":"K7QM3XWDP9RA","cnonce":"c0ffee00112233445566778899aabbcc","acceptor":"9f8e7d6c"},
 "out":{"proof":"731bb4184cfa827b3ed7f3b54100d667afa75bb78fe41c375fbd3f2d14121b60"}}
{"name":"NBSP in a token","in":{"token":"K7QM 3XWD-P9RA"},"out":{"normal":"K7QM3XWDP9RA"}}
{"name":"non-breaking hyphen","in":{"token":"K7QM‑3XWD"},"out":{"normal":"invalid"}}
{"name":"nonce not hex","in":{"nonce":"3f9a0c5e7b2d4f6a8c1e3b5d7f9a0c2Z"},"out":{"valid":false}}
{"name":"live at 6000","in":{"silentMs":6000},"out":{"state":"live"}}
{"name":"stale at 6001","in":{"silentMs":6001},"out":{"state":"stale"}}
{"name":"stale at 12000","in":{"silentMs":12000},"out":{"state":"stale"}}
{"name":"drop at 12001","in":{"silentMs":12001},"out":{"state":"drop"}}
{"name":"redial 0..4","in":{"attempt":[0,1,2,3,4],"j":0.25},"out":{"ms":[450,900,1800,3600,4500]},"tol":1}
{"name":"redial j→1","in":{"attempt":4,"j":0.999},"out":{"ms":5998},"tol":1}
{"name":"best of four","in":{"samples":[[1000,1520,1521,1050],[3000,3530,3531,3100],[5000,5512,5513,5030],[7000,7600,7601,7300]]},
 "out":{"offsetMs":497.5,"rttMs":29,"apart":false},"tol":1}
{"name":"t3 before t2","in":{"samples":[[1000,1520,1519,1050]]},"out":{"admitted":0,"offsetMs":null}}
{"name":"apart at exactly 2000","in":{"offsetMs":2000},"out":{"apart":false}}
{"name":"Patterns /api/cmd OK","in":{"http":200,"body":{"ok":true,"msg":"OK @c9-a41f09c2 Clip ends in 10 s","id":"c9-a41f09c2"}},
 "out":{"outcome":"applied","words":"Clip ends in 10 s"}}
{"name":"Patterns /api/cmd ERR","in":{"http":200,"body":{"ok":false,"msg":"ERR X-Patterns-Client header required for cue commands"}},
 "out":{"outcome":"refused","words":"X-Patterns-Client header required for cue commands"}}
{"name":"PlayMap accepted","in":{"line":"OK @s4-a41f09c2 {\"stage\":\"accepted\",\"effect\":\"queued on the engine lane\"}"},
 "out":{"outcome":"accepted","words":"queued on the engine lane"}}
{"name":"written, dropped","in":{"written":true,"event":"drop"},"out":{"outcome":"unconfirmed"}}
{"name":"never written","in":{"written":false,"event":"down"},"out":{"outcome":"not-sent"}}
```

(The one odd-sum clock case, offset 497.5 where C#'s `long` arithmetic gives 497, is why the topic carries `"tol":1`.)

### 5.3 Scripts: the scripted peer

Each script: `{"kit":"peerlink","v":1,"id":"L1","binding":"line"|"json","peer":"acceptor"|"connector","level":0|1,"features":[…],
"token":"…","ids":{"peer":"9f8e7d6c"},"time":"virtual","auto":{…},"steps":[…]}`. `peer` is the role the scripted peer plays; the link
under test plays the other. Steps:

| step | meaning |
|---|---|
| `{"send":"<line>"}` / `{"send":{json}}` | the peer sends it |
| `{"expect":"<pattern>"}` | the next line from the link (after `auto` lines) matches: `{name}` the link's HELLO name, `{id:x}` captures a request id (and checks 13.11.1), `{proof}` / `{prove:c}` computed by the runner, `{n:x}` captures a number, `*` the rest |
| `{"auto":{"PING":"pong","CONTEXT":"ctx"}}` | lines the link may send at any time, answered by rule and not counted as steps |
| `{"advance":"link.stale.ms+1"}` | moves the link's virtual clock by a §14 expression |
| `{"silence":"link.drop.ms+1"}` | the peer says nothing while the clock moves |
| `{"command":"<line>"}` | the test asks the link to send a command (through its adapter) |
| `{"outcome":"applied","words":"…"}` | the last command's outcome |
| `{"state":{"link":"live","show":"stuck","level":1,"features":["final"]}}` | the link's published state (13.16.4) |
| `{"close":4401}` / `{"close":"reset"}` / `{"expect":"close"}` | the peer closes, or the link must |
| `{"never":"AUTH *"}` | holds for the whole script |

Each repo writes a small **adapter** per link (start against an address, send a command, read the published state, own the virtual clock);
the interpreters do the rest. The C# interpreter is `TimeProvider`-driven (Mapper's `ManualTime` is the model, `Mapper/tests/Shared/ManualTime.cs`);
the JS one takes an injected clock or uses `node:test`'s mock timers. TLS scripts (W6, PlayMap lines) take a certificate the host test
makes: `CertificateRequest.CreateSelfSigned` in C#, AGMessage's own `tls.makeCertificate` in JS (`AGMessage@0.5.0 app/lib/tls.js:72`).

**Scripts, by the role under test.**

| id | role under test | what it holds |
|---|---|---|
| L1 | line connector | NONCE → HELLO, the exact proof, `OK paired`, `PEERLINK` after pairing, ids by 13.11.1 |
| L2 | line connector | `OK open` / no NONCE on loopback: plain `AUTH` only there, never with `never` off-box |
| L3 | line connector | level-0 acceptor: `ERR @id unknown command …` → level 0, no PROVE, no STUCK parsing expected, link live |
| L4 | line connector | `ERR wrong token` → `refused`, no second proof, no reconnect storm |
| L5 | line connector | a non-hex NONCE → no proof, `refused` "malformed NONCE" |
| L6 | line connector | commands: applied, accepted→final, refused, 33rd not sent, written-then-drop `unconfirmed`, silence `unconfirmed` at 30 s |
| L7 | line connector | liveness: stale at 6001, drop at 12001, `PONG … STUCK` → show stuck, a plain PONG → live |
| L8 | line connector | redial: refused dials follow `redial`; accept-and-close does not reset; verified + 10 s resets |
| L9 | line connector | PROVE: right, wrong (refused, held), missing after a proof (amber, facts not followed) |
| L10 | line connector | an id answered differently from the one dialled → conflict, no facts followed |
| W1 | JSON connector | upgrade carries `node`, `t`, `can=peerlink` and the key in the header only; verdict → verified, level 1 |
| W2 | JSON connector | a frame first → HTTP evidence path; 200 + `X-RateLimit-Limit` verifies; 401 refuses |
| W3 | JSON connector | `ok:false,retry:60` → no keyed call for 60 s, public frames still shown |
| W4 | JSON connector | stuck beats blank counting values; pongs keep the link live; only a frame clears stuck |
| W5 | JSON connector | `disconnect` → 30 s hold |
| W6 | JSON connector | TLS: first use pinned after the verdict; a changed certificate refused, one dial a minute, nothing sent; once pinned, never plain |
| W7 | JSON connector | a side call that hangs 4 s does not delay a ping or the drop check |
| A1 | line acceptor | a connection it does not trust hears NONCE (first, once §7.2 is kept); answers only what pairing needs before it |
| A2 | line acceptor | right proof, wrong proof (fresh NONCE), five wrong → `ERR … — closed`; plain AUTH off-box refused |
| A3 | line acceptor | PING: t1 echoed, t2 ≤ t3, answered while the show writer is held (the adapter blocks it) |
| A4 | line acceptor | `PEERLINK` answered after pairing with its id and the intersection of features; unknown `can` words ignored |
| A5 | line acceptor | `PROVE` right, and `open` on a node with no token |
| A6 | line acceptor | ids echoed; a malformed `@` refused without running |
| A7 | line acceptor | a reader that stops reading: pushes stay one per kind, replies cut at 256 |
| A8 | line acceptor | `STUCK` only to a connection that negotiated `stuck`, never to level 0 |
| S1 | JSON acceptor (Stagewatch) | a node link hears the verdict before any frame, with `peerlink` |
| S2 | JSON acceptor | a refused key: one `ok:false` with `retry`, the socket stays open with public frames |
| S3 | JSON acceptor | a stuck producer: `beat live:false` every 2 s, no frame; a fresh frame first after |
| S4 | JSON acceptor | pong stamps; a message over 4 KiB closes 1009 |
| S5 | JSON acceptor | side-call receipts; `dedupe` repeat; `final` follow-up on control sockets |
| S6 | JSON acceptor | unknown query keys ignored; `can` over 8 words trimmed |

**Self-test.** `selftest/*.json` pairs a script with a recorded transcript and the verdict an interpreter must reach (pass, or fail at a
named step). Both interpreters run them in every tree, so a runner that reads the format differently fails before any link is tested.

### 5.4 What each link must pass

| link | vectors | scripts | runs in |
|---|---|---|---|
| Stagewatch `PlayoutLink` → Patterns, PlayMap | all but `redact` of others' | L1–L10 | `Stagewatch.Tests` |
| Stagewatch `/ws` + side calls | `liveness`, `clock`, `ids`, `refusal`, `redact` | S1–S6 | `Stagewatch.Tests` |
| Patterns `StagewatchLinkService` | all connector topics | W1–W7 | `Patterns.App.Tests` (headless) |
| Patterns wire 9697 | `proof`, `prove`, `nonce`, `token`, `ids`, `negotiate`, `redact` | A1–A8 | `Patterns.App.Tests` (`TrustLoopback = false` in `finally`) |
| Patterns `ShowBusNode` (Round 92) | connector topics | L1–L10 | `Patterns.Devices.Tests` |
| PlayMap `StagewatchLink` | connector topics | W1–W7 | `PlayMap.Net.Tests` |
| PlayMap `WireClient` → Patterns | connector topics | L1–L10 | `PlayMap.Net.Tests` |
| PlayMap wire 9941 and `playmap.lines.v1` | acceptor topics | A1–A8 on both carriers | `PlayMap.Net.Tests` |
| AGMessage `StagewatchLink` | connector topics | W1–W7 | `app/test/peerlink-kit.test.js` |
| AGMessage `PeerLink` (line) → Patterns, PlayMap | connector topics | L1–L10 | `app/test/peerlink-kit.test.js` |
| Patterns twin | `clock`, `redact` | — (its own suite; exempt from the rest, §8 row) | `Patterns.Core.Tests` |

Pass mark: every case and step listed for the link, with nothing left out but the topics the node's §8 row names. The interop lane
(`design/ecosystem-lane.md:306-505`) then runs the real apps; its matrix gains one row per link: "level, carrier, verified, state".

---

## 6. Migration

### 6.1 Order (each step stands alone; no step waits for another app)

1. **The words and the kit, no behaviour change.** §13, §14's rows and `conformance/peerlink/**` land in Stagewatch with spec-1-3's
   single master edit (its S1/S8). Stagewatch's own pure vectors pass with one change (D3).
2. **Safety first, unilateral.** AGMessage AG-PL0 (no token in clear) before 0.5.0 ships; Stagewatch's `PlayoutLink` (no plain `AUTH`
   off-box, jitter, outcomes); Patterns Round 89.1 (the stuck beat, already first in its order).
3. **Each app's pure vectors and connectors**, in its own process: Patterns Round 90's "PeerLink 1" unit; AGMessage AG-PL1–AG-PL3; Mapper
   PM-PL1–PM-PL3 at its integration pass (or on the owner's exception).
4. **The acceptors' features**, negotiated: Stagewatch's verdict `peerlink` and `dedupe` (with peers-not-screens SW-3); Patterns'
   `PEERLINK`/`PROVE` (Round 90); PlayMap's PM-PL4. Each feature works the day both ends of one link have it.
5. **Measured liveness on the line binding.** `stuck` from Patterns and PlayMap only after a measured stall record sets their `stuckMs`.
6. **1.4.** `prove` becomes a MUST for a line acceptor with a token, and facts from a token acceptor that cannot prove itself are not
   followed by default — when every node's §8 row says it keeps `prove`.

### 6.2 Old and new, end to end

| connector \ acceptor | acceptor at level 0 | acceptor at level 1 |
|---|---|---|
| **connector at level 0** (today's code) | today | unchanged for it: no `PEERLINK` asked, so no feature is used; the verdict's `peerlink` field and Stagewatch's extra receipt types are ignored by every JSON reader (`StagewatchLinkService.cs:523-538`, `StagewatchLink.cs:377-433`, `nodes.js:331-338`); a 5-word PONG is never sent to it |
| **connector at level 1** | every unilateral rule; features off; grey "PeerLink 0"; an id never seen at level 1 is not amber | all features both offer |

The behaviours an old peer *does* see change come from sibling designs, not this one: Patterns' and PlayMap's reads after pairing
(spec-1-3 decision 7) and Stagewatch's node-link admission (peers-not-screens decisions 1–7), each with its own rollout.

### 6.3 What is refused, from when

| refused | from | the operator sees |
|---|---|---|
| plain `AUTH <token>` to another machine | each connector's adoption (Stagewatch with `PlayoutLink`; the module in its own Patterns round) | the link amber: "this Patterns desk is older than Show Bus 1.1 and pairs only by sending its token in clear — update Patterns" (playout-links D3) |
| a key over plain `ws`/`http` to another machine | Patterns Round 90, PlayMap PM-PL3, AGMessage AG-PL2 | `degraded` "no TLS — the key stays on this machine"; the public frames still follow |
| a pairing token in an HTTP header without TLS off the machine | AGMessage AG-PL0 (0.5.0) | `refused` with the words in AG-PL0 |
| a NONCE that is not 32 lowercase hex | each connector's adoption | `refused` "malformed NONCE" |
| a typed token with a character that is not a letter or digit | each app's settings entry, at its adoption | refused at entry; nothing saved |
| a token acceptor that does not prove itself, for facts | 1.4 (13.17 → MUST) | amber, and facts not followed until accepted |
| level 0 | never by date: when every §8 row lists PeerLink 1, a revision may drop the fallback | — |

---

## 7. Risks

1. **The C# interpreter may trip one tree's analyzers.** It is written to Patterns' and Mapper's rules (no empty catch, no dead stores, no
   `System.Random`, invariant culture, no banned time APIs) and the BCL only. If a later analyzer rule rejects it, the master changes —
   never a suppression in one tree. Stagewatch has no analyzers, so the master's own build does not catch it; the first Patterns round to
   copy it does.
2. **Copies go stale while every tree is green.** Same limit as the spec's copies; only `tools/showbus-copies.sh` sees it. Each tree's
   papers say so.
3. **PlayMap stays level 0 through round 3.** Mapper's §4 ownership forbids the edits until the integration pass or the owner's word; every
   peer's fallback covers it, and AGMessage's and Stagewatch's links to PlayMap work at level 0.
4. **AGMessage keeps the full pairing token at rest.** The line binding takes it off the wire, not out of AGMessage's settings. A
   messenger-scoped credential on Patterns and PlayMap is §8 work.
5. **AGMessage on Patterns' wire receives every STATE push** (Patterns has no `SUBSCRIBE OFF`). Lines are counted and dropped without a
   JSON parse; the risk is bandwidth on a busy desk, measured in the interop lane before AG-PL3 ships.
6. **A `stuck` number set too low blanks a countdown on an honest stall.** The profile lets a node offer `stuck` only after a measurement;
   until then its connectors show show liveness `unknown`, which is what they show today.
7. **The `TimeProvider` seam touches constructors.** Stagewatch's `PlayoutLink` is new code anyway; Patterns' link gains a parameter with a
   default; PlayMap already has the seam; AGMessage uses mock timers.
8. **PROVE is honest only about what it stops.** On a plain carrier a relay between two real ends can still inject lines after the proof;
   the text says so (13.17.5), and Patterns' wire stays plain until L55 (`STARTTLS`).
9. **"Proved before" and "level before" marks can alarm after an operator rolls a peer back.** Both are amber with words that say so, and
   the operator's accept clears them; neither refuses the link.
10. **Two numbers move for the minority apps** (inclusive stale, strict apart). Invisible to operators; fenced by vectors.
11. **The kit's scripts lean on adapters each repo writes.** An adapter that reads the link's state wrongly would pass a broken link; the
    adapters read only what 13.16.4 publishes, the same rows the Eye reads.

---

## 8. What this design does not do

- No `STARTTLS` on Patterns' or Stagewatch's Companion line ports (Patterns OPEN L55); no TLS on Patterns' wire.
- No scoped credential for messengers on Patterns or PlayMap.
- No new liveness for decks (the Companion module), browsers or windows; they keep their own, under 13.13 at the acceptor.
- No change to the hello, discovery or reads after pairing (spec-1-3), to node-link admission (peers-not-screens), to grants (windows-for-
  peers) or to proposals (cross-app-proposals).
- No `stuckMs` for Patterns or PlayMap: no measurement this round.
- No twin redesign: the twin's §8 row names its exemption and the later round that gives forwarded `ACT`s their outcomes.

## 9. Gaps this area touches

G04 (13.9.5), G07 (13.11), G08 (13.5.1.5), G28 (13.10.5, 13.16.4), G31 (13.3.1, 13.4.3), G32 (13.4.1), G43 (13.6.4), G44 (§5), G45 (as a
link rule, 13.5.2.1 — the decision is spec-1-3's). It closes none alone: it is the standard the sibling designs' link units meet, plus the
units in §4.
