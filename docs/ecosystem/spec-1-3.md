# Show Bus 1.3 — the master contract, its numbers, and how four trees stay in step (revised after review)

Area: spec-1-3. Gaps: G22, G27, G38, G39, G40, G41, G42, G45. Design only: nothing in any repo was edited or built.

Paths are repo-relative and prefixed with the repo (`Stagewatch/…`, `Patterns/…`, `Mapper/…`, `AGMessage/…`). Line numbers are
from the trees as read on 2026-10-25. Sibling designs are cited as `design/<area>.md:<line>`. The first draft is kept beside this
one (`scratchpad/spec13-rev/spec-1-3.v1.md`); §11 answers the review item by item.

**What changed in this revision**
- **No carrier is named for a hello on one machine until it is measured on Windows.** A unicast to 127.0.0.1 on the shared port
  reaches one listener of several (measured again here, §0). So a loopback-only node stays silent in 1.3 (G27's own fix), the
  loopback copy a networked node sends is a hint nobody relies on, and the same-machine carrier is lab line LB-1.
- **A hello leaves from a socket bound to the node's door address**, so the address it comes from is always one its doors answer
  on (AGMessage with a LAN `HOST` breaks this today).
- **Hearing keeps every address of an id**, dials the one in use while it is heard, and treats one id from two running nodes as
  a conflict shown amber, never merged. All four nodes send `inst`.
- **§12 is split with windows-for-peers.** This area writes the window, the view-pass class and the window link as built. Grants,
  the operator-frame block and the Companion feedbacks are windows-for-peers'. Leases, `windows.lend`, `web.show`, `window.join`,
  `Authorization: Window` and units S5/S7/S8 of the first draft are gone.
- **Reads after pairing on Patterns is the maintainer's call (an ADR), in two scopes**, with its prerequisites named: the
  Companion module pairs before it reads, each wire peer keeps its pairing, the desk's own cross-machine asks are inventoried, six
  test files change. PlayMap's TCP read is a question to its owner, not a unit.
- **`proto` means the revision whose shapes a node speaks**; its §8 row says what it does not keep yet.
- **The numbers follow what nodes send**: context items 200 and links 400 (Patterns' writer), a reader that keeps commands keeps at
  least 256 (PlayMap's owner, decision 18), and §6 gains a byte-trimming order.
- **Certificates already issued are replaced once** when their span is over 825 days.
- **The copy fence is described as what it is**: each tree's test stops accidental edits; only the interop lane sees a stale copy.
- **Patterns' units fold into Round 90** as patterns-playmap-side numbered it.

---

## Who writes which words of the master

This area owns **the master text** (`Stagewatch/docs/SHOWBUS.md`), the copy rule and §13. Every other area that changes the
master hands its words to this area's single edit; nobody else edits the file.

| Section | Words from | Evidence of the hand-over |
|---|---|---|
| §2 kind `playmap`, §3 the `final` receipt, §8 PlayMap's VT row | playout-links | `design/playout-links.md:318-321` (§2.6) |
| §6 command notation and caps, §11 risk and line rules, §8 the desk flows | cross-app-proposals | `design/cross-app-proposals.md:431-447` (§2.6) |
| §6 `peers` fields, §7 one node one key, §7.1 a node on another node's `/ws`, §11 bound id | peers-not-screens | `design/peers-not-screens.md:497` |
| §1 rule 5's carrier exemption (D0), §2 caps `pages`/`window-draw`, §6 the cross-node link end, §12.4–12.6 grants, the black end, held forms, placement claims, the operator frame's `windows` | windows-for-peers | `design/windows-for-peers.md:553-573` (§2.9), `:221-229` (D0, a maintainer's call) |
| §11 `GET /api/proposals/<id>` shape, `undelivered` also "collected, no result in 5 minutes", `redeliver` | agmessage | `design/agmessage.md:472-475` |
| §6 one sentence (a posted context may appear in the desk's report), §8 one consumes cell | ecosystem-lane | `design/ecosystem-lane.md:591-597` |
| §13 `reserved video.anchor`; Patterns' round numbers for the copy | patterns-playmap-side | `design/patterns-playmap-side.md:494-504,825-841` |
| §1 rule 4, rule 5's view-pass class, §2 (hello, carrier, `proto`, limits, hearing, conflict), §4 counters, §6 sizes, trimming and posting, §7.2, §8's table and exemptions, §9, §10 span and renewal, §12.1–12.3, §13, the copy rule | **spec-1-3** | — |

---

## 0. What exists (evidence)

**The master is 1.2 and contradicts itself.**
- Header: 1.2 "adds … hello `tls`, caps `approvals`" (`Stagewatch/docs/SHOWBUS.md:3-4`), but §2 defines `proto` only as
  "1 = 1.1 contract" (`:38`), the caps line has no `approvals` (`:41`), the ports line no `https`, `companion` or `sync` (`:40`),
  the example no `tls` (`:31-32`), and the kinds no `playmap` (`:37`).
- §4 gives `seq` two meanings: a frame counter and a discontinuity counter.
- §6 caps only `recent` (≤ 20); no byte, item or command limit. No hello size, caps, `t` or `id` limit anywhere.
- §8 has no PlayMap row and none of 1.2's flows. Nothing mentions windows.
- §9 names the AI service; Patterns' copy replaces it (`Patterns/docs/SHOWBUS.md:156`, header `:1`). G39 (3) records that this is
  not a breach of the authorship rule; the change is for byte-equal copies only.
- PlayMap is named nowhere in the master or `Stagewatch/docs/ECOSYSTEM.md` (`:1`, `:31-32` still "Show Bus 1.1").

**The copies.** Collapsing whitespace, the copies differ from the master in three places: Patterns' header line and its §9 word;
AGMessage's header line and its §10 "five years" (`AGMessage/docs/SHOWBUS.md:168`). Nothing checks a copy against the master in
any tree. `Mapper/docs/SHOWBUS-NODE.md` is PlayMap's node page, claiming 1.2 and proposing seven 1.3 items (`:67-89`).

**What each node sends (measured from the code, compact JSON, 64-hex `tls`).**

| node | bytes | caps | `inst` | built at |
|---|---|---|---|---|
| PlayMap | 491 (648 with 64-char name/host/build) | 16: video, video.clock, video.transport, video.seek, video.end, state, context, receipts, clock, mcp, playlist, media, mapping, scan, eye, show | 8 hex per process | `Mapper/src/PlayMap.Net/Bus/HelloService.cs:66,99-114`; `Mapper/src/PlayMap.Core/Bus/ShowBus.cs:116-117` |
| Stagewatch | 424 | 12: timer, cues, messages, context, receipts, clock, screens, aux, slides-in, copilot, approvals, mcp | none | `Stagewatch/src/Timer.App/ViewModels/MainViewModel.Slides.cs:21-48` |
| Patterns | 435 | 10: video, video.clock, video.end, video.seek, video.transport, state, eye, context, receipts, clock | 8 hex per process | `Patterns/src/Patterns.Core/Services/ShowBus.cs:28-53`; `Patterns/src/Patterns.Devices/BeaconService.cs:48,85-100` |
| AGMessage | 308 | 7: messages, action-prompts, documents, ai, context, receipts, proposals | none | `AGMessage/app/lib/nodes.js:34,72-75` |

Every node sends `proto: 1`; no reader branches on it (only PlayMap's equality check in `Mapper/src/PlayMap.Net/Bus/PeerTable.cs:134`
and AGMessage storing it, `nodes.js:48`). Stagewatch's record calls it the slide feed's version
(`Stagewatch/src/Timer.Integrations/RemoteSlides.cs:117`). Stagewatch's hello also carries `presenting`
(`RemoteSlides.cs:114`), true on a Slides agent showing a slide (`Stagewatch/src/Timer.Integrations/SlidesAgent.cs:72`), read only by
the agent window's list of other agents (`Stagewatch/src/Timer.App/SlidesAgentWindow.xaml.cs:113`).

**What each node reads.**

| reader | bytes | `t` / `id` | caps kept | name/host kept | peers kept | address on a new datagram | evidence |
|---|---|---|---|---|---|---|---|
| Stagewatch | ≤ 2048 | ≤ 16 / ≤ 40 `[A-Za-z0-9_-]` | 16 | 60 | 128, forgotten after 60 s | overwritten | `RemoteSlides.cs:181-197,206-220` |
| Patterns | ≤ 4096 | ≤ 32 / ≤ 64 | 32 of ≤ 32 chars | — | no bound | overwritten | `Patterns/src/Patterns.Core/Services/ShowBusNetwork.cs:106-147` |
| PlayMap | ≤ 4096 (comment says 2048) | ≤ 16 / ≤ 40 | 16 of ≤ 32 | — | no bound | overwritten, raises `Changed` | `Mapper/src/PlayMap.Core/Bus/ShowBus.cs:127-170`; `PeerTable.cs:53-74` |
| AGMessage | ≤ 4096 | known kinds only / ≤ 40 | 20 of ≤ 24 chars | 60 | no bound (gone after 12 s) | overwritten, calls `onChange` | `AGMessage/app/lib/nodes.js:33-48,83-89` |

PlayMap's 16 caps sit exactly on Stagewatch's and its own cut: one more PlayMap cap is lost by both.

**Context readers and writers.** Stagewatch keeps ≤ 64 KB (counted in UTF-16 characters), 64 items, 20 recent, 64 commands with
the verb cut to 80 characters, 32 peers (`Stagewatch/src/Timer.Core/ShowBus.cs:60,93-102`); a context over the limit is refused
whole (`:60`). Patterns **writes** up to 200 items (`Patterns/src/Patterns.Core/Services/ShowContext.cs:68,180`) and reads ≤ 512 KB,
200 items, 400 links, 64 commands, 32 peers (`:88-100`). PlayMap's reader states its own bounds as ≤ 512 KB, ≤ 200 items, ≤ 400 links,
≤ 32 peers, with no command cap (`Mapper/docs/round3/PLAN.md:2333-2340`, §5.6 item 7). PlayMap's busy golden context is 43,640 bytes
as stored and **38,142 bytes compact**, 33 items, 25 links, 199 commands (32,643 bytes of commands, 164 on average), longest verb 307,
longest `words` 403, 13 commands with `words` over 200; at 256 commands it would be about 47.5 KB compact (measured here from
`Mapper/tests/PlayMap.Engine.Tests/Fleet/Golden/context-busy.json`; cross-app-proposals measured the same,
`design/cross-app-proposals.md:34`). PlayMap's owner: "Use at least 256, but only where necessary" — a reader that keeps a command
cap keeps at least 256 (`Mapper/docs/round3/PLAN.md:2594-2610`, decision 18). With every field at its maximum, 256 commands alone
would be several times 64 KiB, so the byte limit binds first.

**G27 — Stagewatch's hello sender.**
- `SlidesBeacon.Send` sends only to `255.255.255.255:9928` inside one `try` that swallows every `SocketException`
  (`RemoteSlides.cs:161-170`).
- The hello is built whenever `_web` is non-null (`MainViewModel.Slides.cs:21`), so with **Allow other devices** off (`WebBindAll`,
  default true, `Stagewatch/src/Timer.Core/AppSettings.cs:257`) the server listens on 127.0.0.1 (`Stagewatch/src/Timer.Remote/WebServer.cs:212`)
  and Stagewatch still broadcasts its port, against `SHOWBUS.md:44-45`. Peers dial the hello's source address
  (`Patterns/src/Patterns.App/Services/StagewatchLinkService.cs:237`; `Mapper/src/PlayMap.Net/Links/StagewatchLink.cs:66-71`), which refuses them.
- The hello names `companion` whenever that server runs (`MainViewModel.Slides.cs:45`), but it binds loopback by default
  (`AppSettings.cs:314`; `Stagewatch/src/Timer.Companion/CompanionServer.cs:44`). `osc` likewise ignores `OscBindAll` (`AppSettings.cs:270`).
- The hello is built on the timer's thread from the live `Settings` (`MainViewModel.Slides.cs:21-30`), not `_store.Snapshot`.
- The only listener test passes a null hello (`Stagewatch/tests/Stagewatch.Tests/Program.cs:1860`).
- The node id is 12 hex (`Stagewatch/src/Timer.Core/SettingsStore.cs:201-202`); the master says 8.

**Measured here — a unicast to a shared port reaches one listener.** Three sockets bound to `0.0.0.0:<port>` with `SO_REUSEADDR`
(Linux 6.18, Python; and .NET 10 `UdpClient`, `scratchpad/critique-spec13/udp.py`, `udpnet/u.cs`):

| sent to | delivered to |
|---|---|
| 127.0.0.1 (5 datagrams) | **one** socket got all five; the other two none |
| 255.255.255.255 (network up) | all three — from the eth0 address, not 127.0.0.1 (`design/ecosystem-lane.md:199-205`) |
| 255.255.255.255 (only loopback up) | `ENETUNREACH`, nobody (`design/ecosystem-lane.md:201`) |
| 127.255.255.255 | all three any-bound sockets; a 127.0.0.1-bound socket nothing |
| 239.255.99.28 on `lo` (`IP_MULTICAST_IF` 127.0.0.1, loop on), two any-bound sockets joined, one 127.0.0.1-bound not | the two joined; the 127.0.0.1-bound nothing (`scratchpad/spec13-rev/mc.py`) |

A 127.0.0.1-bound socket on the shared port takes **every** loopback unicast (`design/ecosystem-lane.md:202`), and PlayMap binds its
listener to 127.0.0.1 when its own bind is loopback (`HelloService.cs:124-125`). Windows' documentation calls unicast delivery to a
shared port indeterminate; nothing here is measured on Windows. Consequences: today's loopback copies (`BeaconService.cs:314-324`;
`HelloService.cs:58-62`; `nodes.js:95`) each reach one arbitrary listener, and the first draft's "loopback only" hello and
"loopback-first hearing" would have been nondeterministic.

**A hello whose source its doors do not answer on.** AGMessage listens on `HOST` (`AGMessage/app/server.js:44,1245`), announces
whenever `HOST` is not loopback (`:1003-1005`), and sends both copies from one socket bound to every address (`nodes.js:91,95`). With
`HOST` a LAN address, its loopback copy arrives from 127.0.0.1, where nothing listens. Patterns sends from a socket bound to its bind
address (`BeaconService.cs:107-117,311-324`) and PlayMap from one bound to `bind ?? Any` (`HelloService.cs:131-132`), so neither has this.

**One peer, two addresses.** Every reader overwrites the address with the latest datagram's (`RemoteSlides.cs:197`;
`ShowBusNetwork.cs:111`; `PeerTable.cs:63-65`, which raises `Changed`; `nodes.js:89`, which calls `onChange`). AGMessage matches its
Stagewatch peer by address or host name (`nodes.js:108-113`) and takes the pin key, TLS wish and desk cap from that match
(`nodes.js:247-252,413-416`).

**Ids travel with settings.** Patterns keeps its id in a file beside its settings (`ShowBusNetwork.cs:191-203`), Stagewatch in
`Settings.NodeId`, AGMessage in `settings.nodeId` (`server.js:82`). A copied folder or an imaged laptop carries the id. Today the
address flip at least raises `Changed`/`onChange`; nothing names two machines with one id.

**G45 — reads before pairing.**
- Patterns greets every TCP connection with `STATE` before `NONCE` (`Patterns/src/Patterns.App/Services/ControlService.cs:356-363`),
  pushes to every peer paired or not (`:763-774`), and lets every query through unpaired (`:501`, `ControlProtocol.IsQuery` at
  `Patterns/src/Patterns.Core/Services/ControlProtocol.cs:1454`). This is a documented decision (`Patterns/docs/PLAN.md:11648-11654`,
  §103.7; `Patterns/docs/REMOTE.md:38-41`) resting on "a Companion presents `AUTH` before it asks anything".
- That premise is false today: the Patterns module sends `HELLO` and then asks `NAV` at once (`Patterns/integrations/companion-module-patterns/src/main.js:150-164`),
  before the `NONCE` arrives; `authenticate()` sets `authed` before `OK paired` (`:196-206`); an `ERR not paired` goes through
  `trustProblem` into BadConfig (`:215-224,373`).
- The desk's own cross-machine asks are unpaired single lines (`Patterns/src/Patterns.App/Services/NodesService.cs:185-227`): `ARCADE
  STATUS/GAMES/SCORES` (`:149-153`) and a node's `ASSISTANT MODERATE` to its desk (`Patterns/src/Patterns.App/Services/ServiceKernel.cs:97-101`),
  both queries (`ControlProtocol.cs:1249`).
- A fresh desk has a token (`Patterns/tests/Patterns.App.Tests/ShowBusAppTests.cs:241-247`).
- The token a connection presented is a local of the handler (`ControlService.cs:369`); `Broadcast` pushes to every `WirePeer`
  (`:763-774`). "NEW TOKEN cuts it off" holds for verbs, not pushes.
- Tests assert today's order on purpose: the `Wire` helper expects `STATE {` then `NONCE` (`ShowBusAppTests.cs:71-82`);
  `RemoteTrustTests.cs:132-150` asserts `STATUS` answers unpaired ("reading never needs the token"). Tests that set
  `TrustLoopback = false`: `NavAppTests`, `RemoteTrustTests` (3), `ShowBus12AppTests`, `ShowBusAppTests` (2).
- PlayMap's TCP wire sends `NONCE` first, but `HELLO` makes the session a state listener and STATE, CONTEXT and other queries are
  open (`Mapper/docs/WIRE.md:85-98`, items 1, 2, 4); its `/ws` line channel answers only pairing words until it pairs
  (`Mapper/src/PlayMap.Net/Wire/WireSession.cs:228-233`). PlayMap's owner, decision 17: `/api/context` stays behind the token and
  "Patterns reads PlayMap's context over the wire (`SUBSCRIBE CONTEXT`), as today" (`Mapper/docs/round3/PLAN.md:2594-2595`).
- Stagewatch's link with no token counts the proof as sent (`Stagewatch/src/Timer.Integrations/PatternsLink.cs:202`), reports
  green "connected" (`:210`) and asks `@ctx CONTEXT` every 5 s (`:291-295`). playout-links replaces it: CONTEXT and the rest are asked
  only after `OK paired`/`OK open`, and "not paired" is amber (`design/playout-links.md:101-112,311`).

**G39 — certificate lifetime.** Apple counts the whole span from NotBefore to NotAfter.
- Stagewatch: now − 1 day to now + 825 days: **826 days** (`Stagewatch/src/Timer.App/NodeCertificate.cs:66`); renewed only when
  NotAfter is within 30 days (`:34`).
- Patterns: now − 1 day to now + 5 × 365 days: **1,826 days** (`Patterns/src/Patterns.Core/Services/ShowBusTls.cs:84,103`; pinned by
  `Patterns/tests/Patterns.Core.Tests/ShowBus12Tests.cs:26`); `NeedsRenewal` reads NotAfter only (`:106`).
- AGMessage: now − 1 h to now + 825 days: **825 days + 1 h** (`AGMessage/app/lib/tls.js:78-79`); renewed when fewer than 30 days remain
  (`:124`).
- So every certificate already issued stays until it nears its end: about five years for Patterns.

**G22 — windows.** The window address `/w/<token>` (26 characters of a 32-letter alphabet, `Stagewatch/src/Timer.Core/ScreenWindows.cs:80-86`)
is the window's identity and pass; the window link takes it as `?win=` (`WebServer.cs:1145-1150`). `/api/windows` gives `path` only
to the PIN or a control key (`:676-689`). Wrong window addresses, wrong PINs and wrong keys count in one table per address
(`:35,625-642,1076-1081`), and a right key clears it (`:1083`). The hello has no `windows` cap (`MainViewModel.Slides.cs:34-39`).

**G41 — `Stagewatch/docs/NODES.md`** has not changed since 2.11.1 (hello fields `:13-17`, `id` per run `:22`, `proto` as the slide
feed `:23`, plain `AUTH` and a 7 s drop `:73-75`, AGMessage on 9700 `:96-101`). The code: NONCE/AUTH-PROOF with an `@hi` probe and a
1.5 s fallback (`PatternsLink.cs:46,199-206,279-284,311-314`), 12 s drop (`:44`), backoff 250 ms doubling to 5 s (`:189,229-230`),
Find on 9700 and 9928 (`:418-444`).

**G42 — AGMessage** says a ping every 2 s (`AGMessage/app/lib/nodes.js:9`; `AGMessage/docs/NODES.md:10`); the code pings on every
1 s tick (`nodes.js:221,350-354`).

---

## 1. Decisions

1. **One master edit, one owner; every sibling supplies words.** The table above lists every area that changes the master. Their
   words go into S1 (the draft) and S8 (final) here; no other unit edits `Stagewatch/docs/SHOWBUS.md`. *Why:* the copy fence
   (decision 10) needs one hash per revision. *Alternative:* each area edits its sections — rejected for the fence and merge noise.

2. **`playmap` joins with every cap PlayMap sends, and §8 gets a PlayMap row.** PlayMap's §5 proposals are answered in
   `SHOWBUS-NODE.md` one by one: 1 (kind and caps) adopted; 2 and 3 (Patterns adds PlayMap as a device, reads third-party contexts)
   are Patterns' work (G35, Round 92); 4 (a Patterns destination) is not contract; 5 (Stagewatch finds PlayMap) is playout-links'
   (G01); 6 (`video.anchor`, `ANCHOR`) is a **reserved** word until a second node reads it; 7 (commands) is §6's rule below. *Why:* a
   word enters the contract when a node sends it and its meaning is fixed.

3. **`proto` is the revision whose shapes a node speaks: 1 = 1.1, 2 = 1.2, 3 = 1.3.** It only grows. A node sends 3 when it sends and
   reads within §13 and speaks 1.3's messages; what it does not keep yet is listed in its §8 row with what ends it. A reader never
   refuses a hello for its `proto` and never decides a safety question by it: a node that shows nothing before pairing is recognised
   by `NONCE` as its first line and by the §7.2 refusal words. The slide feed keeps its own version in its replies. *Why:* every node
   sends 1 and nobody branches on it (§0); one meaning stops three. *Alternative (first draft):* "keeps 1.3 in full" — no node would
   meet it on the day it sends 3, so a reader could never trust it.

4. **Limits are sender maxima; every reader accepts at least them; the byte limit binds first.** One table (§13) gives each number
   once. Chosen from what nodes send:

   | number | value | why |
   |---|---|---|
   | hello bytes sent / read | 1200 / 2048 | one unfragmented datagram on any LAN, VPN or bridge; today's largest is 648 worst case; Stagewatch reads 2048 today |
   | caps | 32 words of `[a-z0-9.-]{1,24}` | PlayMap sends 16 and is the first to need more; 24 is AGMessage's cut and fits every word sent |
   | `t` / `id` / `inst` | `[a-z0-9_-]{1,16}` / `[A-Za-z0-9_-]{6,40}` / 8 lowercase hex | Stagewatch's reader; Slides ids are 6–32 (`Stagewatch/src/Timer.Core/SlideFeed.cs:65`); Patterns' and PlayMap's `inst` |
   | name / host | ≤ 64 characters, read whole | Stagewatch and AGMessage cut at 60 today (`RemoteSlides.cs:220`; `nodes.js:44`) and rise to 64 |
   | ports | ≤ 8 | Stagewatch's reader (`RemoteSlides.cs:191`) |
   | peers kept | 128, forgotten after 60 s | Stagewatch's table (`RemoteSlides.cs:195,213`); anything on the LAN can send a hello |
   | context | 64 KiB as UTF-8 bytes, 200 items, 400 links, 20 recent, 256 commands, 32 peers | 200/400 are Patterns' writer and PlayMap's reader; 256 is PlayMap's owner's figure |
   | item id / kind / label / state, headline, recent line, link end | 60 / 30 / 80 / 160, 200, 200, 120 | Stagewatch's reader's cuts made the senders' rule; the link end is windows-for-peers' |
   | verb / words | 512 / 512 sent; a reader keeps the whole verb or drops it, and at least 200 of `words` | PlayMap's longest verb is 307, `words` 403 (measured); cross-app-proposals' reader keeps 200 (`design/cross-app-proposals.md:180,441`) |
   | ping | every 1–2 s | Stagewatch, Patterns, PlayMap at 2 s; AGMessage at 1 s (G42) |
   | context post | on change, at least every 10 s while linked, at most once a second; kept 30 s | PlayMap posts every 5 s (`StagewatchLink.cs:50`); Stagewatch keeps 30 s (`ShowBus.cs:42`) |

   **Commands.** A reader that keeps a peer's commands keeps at least 256 of them (the owner's decision 18). A *view* built from
   them — a prompt's brief, a drawn card — may show fewer, panic, air and transport rows first, and says how many it left out.
   **Bytes.** A sender over 64 KiB trims in this order and stops when it fits: the oldest `recent` lines; grey links, then other
   links; commands other than panic, air and transport, from the end of its list (senders list those first, as PlayMap's
   `ContextOrderTests` hold, `Mapper/docs/round3/PLAN.md:2333-2336`); grey items, then green items. Reds, ambers and the panic, air
   and transport commands are never trimmed. It then adds one grey item `context:trimmed` naming what it left out ("left out: 12
   recent, 40 links, 30 commands, 5 items"). *Why:* two numbers per limit let a node grow without a flag day; the trimming order keeps
   what an operator needs on a failing night. *Alternative:* one number for both sides — the strictest reader breaks on the day a
   node grows, exactly where PlayMap's 16 caps sit.

5. **Where a hello goes (G27), with no carrier promised on one machine until it is measured.**
   - A node whose doors the network can reach broadcasts its hello to 255.255.255.255:9928 every 2 s.
   - It sends the hello **from a socket bound to the address its doors are bound to** (every interface, or the one address), so
     the address a hello comes from is always one its doors answer on.
   - It may also send a copy to 127.0.0.1:9928, each send in its own `try`. A reader never relies on that copy: on a shared port it
     reaches one listener of several (§0).
   - A node whose doors are bound to this machine only sends **no hello** (G27's fix, and what Patterns, PlayMap and AGMessage do
     today: `ShowBus.MayAnnounce`, `Patterns/src/Patterns.Core/Services/ShowBus.cs:66-73`; `HelloService.Announcing`, `:84`;
     `server.js:1004`).
   - `ports` names only doors every receiver of that hello can open.
   - How apps on one machine find each other with no network is **lab line LB-1**: on Windows 11, measure which of 127.0.0.1,
     127.255.255.255 and loopback multicast reaches every listener sharing 9928, any-bound and 127.0.0.1-bound, with the network up and
     with every adapter off. 1.4 names the carrier from that record. Until then a typed address (127.0.0.1 and the port) works in each
     app's link settings.

   *Why:* the measurement (§0) shows the first draft's "loopback only" would have found one app of several, at random.
   *Alternatives:* 127.255.255.255 now — reaches every any-bound listener on Linux but not PlayMap's 127.0.0.1-bound one, and Windows
   is unmeasured; loopback multicast — the same two unknowns.

6. **Hearing: one id is one peer with every address it was heard from; two running nodes with one id are a conflict.**
   - A reader keeps, per id, every address heard in the last 6 s. It dials the address its link already uses while that address is
     still heard, and moves only when it goes stale. An address change alone raises no change event.
   - A peer heard from 127.0.0.1 or from one of this machine's own addresses is on this machine: a reader may dial it at 127.0.0.1
     first and, if refused, at the heard non-loopback address. Host-name equality alone is not proof (two machines can share a name).
   - Matching a typed address to a heard peer (AGMessage's `find`) matches any address heard for the id, or its host.
   - Every node sends `inst`, 8 random hex per process start. A reader that hears one id with `inst` A, then B, then A again within
     6 s — or, from a node that sends no `inst`, two different `host` values within 6 s — marks the id **in conflict**: it lists it
     amber ("two machines say they are 3f9c2a1b7d0e: FOH-PC at 10.0.0.5 and FOH-PC2 at 10.0.0.7"), keeps the link it already had,
     dials no new link to that id, and clears the mark after 60 s without the alternation. A restart (A, then only B) is not a
     conflict; one `inst` from two addresses is one multi-homed node.

   *Why:* stops the per-beat flip (§0) deterministically, whoever receives the loopback copy; keeps AGMessage's pin and desk detection
   when the operator typed a LAN address; and makes a cloned install visible instead of silently merged. *Alternative (first
   draft):* loopback wins — nondeterministic under one-listener delivery, and wrong for AGMessage with a LAN `HOST`.

7. **Reads after pairing is the rule; each node reaches it on its own decision.** §1 rule 4 centres on a per-node **public view**
   (§8). Everything else travels only on an authenticated link, to loopback the node trusts, or from a node left open (no token),
   which says so amber. §7.2 says what a line wire does on a token node. Where a node still serves more, §8 lists it as an exemption
   with what ends it — and "ends when its maintainer decides" is an honest entry.
   - **Patterns** (Round 90.2, if the maintainer says yes; an ADR reverses PLAN §103.7). Two scopes:
     - *A (proposed first):* the greeting `STATE`, the pushes and the answers to `STATE`, `STATUS`, `CONTEXT` and `SUBSCRIBE` wait for
       `OK paired`; other queries stay open and are listed in §8 by name until the desk's own cross-machine asks pair by proof.
     - *B:* every query waits.
   - Its prerequisites, in the same round: the Companion module pairs before it reads; each `WirePeer` holds its pairing and every
     push re-checks it, so NEW TOKEN stops STATE too; the inventory of the desk's own cross-machine asks (`ARCADE …`, `ASSISTANT
     MODERATE`); the six test files that read the greeting with `TrustLoopback = false`; Super Check's Remote row and a log line for a
     refused unpaired read.
   - **Stagewatch's link** must show a token-less link to a token node amber and ask nothing but `PING` — playout-links' `PlayoutLink`
     does (`design/playout-links.md:101-112,311`); Round 90.2 waits for it.
   - **PlayMap's TCP wire** stays as decision 17 left it. Whether it should read after pairing is put to PlayMap's owner as a question
     for the round-3 call log; if yes, it lands after Patterns' Lines devices pair by proof (G35, Round 92).

   *Why:* STATE carries the show name, deck and device addresses; the readers that matter can pair first once the module is fixed.
   *Alternative:* name a permanent open-read tier (PLAN §103.7's stance) — still open to the maintainer through the ADR; the contract
   states the rule and the exemption, not the date.

8. **Certificates: 825 days in all, and an over-long one is replaced once.** NotAfter − NotBefore ≤ 825 days, backdating included
   (NotBefore at most 24 h before creation); NotAfter computed from NotBefore. At a start a node makes a new certificate when fewer than
   30 days remain **or** the span is over 825 days. Each tree's papers announce the one CERTIFICATE CHANGED its pinned peers will see.
   *Why:* code fixed alone leaves Patterns' 1,826-day certificates for five years (§0). *Alternative left for 1.4:* key-continuity
   rollover (the next fingerprint announced over the pinned link) — a new pinning rule in four trees.

9. **Windows: this area writes what a window is; windows-for-peers writes how a peer holds one.**
   - §12.1 the window and its address, §12.2 the view-pass class, §12.3 the window link as built in 2.17.
   - §1 rule 5's class: a window's address, a viewing code and a crew link are *view passes*.
   - Where a pass may travel besides a browser's address bar is windows-for-peers' carrier exemption (its D0, a maintainer's call),
     carried into rule 5 word for word.
   - View passes are counted apart from credentials: five wrong view passes a minute lock a device out of view passes; a right PIN or
     key never clears that count; wrong PINs and keys keep their own count.
   - Leases, `windows.lend`, `web.show`, `window.join` and `Authorization: Window` are dropped. G10/G11 are windows-for-peers' grants.
   - Words this area asks windows-for-peers to accept before S1 pins a hash:
     - a grant is taken only from a door that authenticates the operator (the desk, the PIN, a control key, MCP with a control
       key); OSC and an unauthenticated Companion TCP refuse it with the reason (`Stagewatch/src/Timer.Remote/OscServer.cs:7-14` has no
       PIN; `AppSettings.cs:270` binds OSC to every interface by default once it is on);
     - D4's loopback dial falls back to the heard address when 127.0.0.1 refuses, and never rests on host-name equality alone;
     - the `windows` hello cap lands in this area's S2 builder.

   *Why:* windows-for-peers is the deeper design of the lessee side and already reconciles grants with the Eye, the twin and black on
   air (`design/windows-for-peers.md:221-378`). Two verbs with two security postures in one contract would serve nobody.

10. **Copies are held by a hash and a numbers block — and that fence stops accidental edits, not stale copies.**
    - The body is the bytes from the line `## 1. Rules every node follows` to the end: UTF-8 without a BOM, every CRLF turned to LF,
      ending in exactly one LF. It is byte-identical in the master and each copy; only the title and one header paragraph differ.
    - Each tree's `ShowBusSpec` test (a) hashes its body and compares it with a constant pinned in the test and printed in the header,
      and (b) parses §13's ```showbus block and holds its own code to every number it implements.
    - Each CI checks out only its own tree, so a newer master leaves the copies stale while every tree stays green. Only
      `tools/showbus-copies.sh` in G44's interop lane sees it, and the papers say so.
    - A node may send caps of its own that its node page declares in a ```showbus-node block (`cap <word>`); the vocabulary check
      accepts §13's rows for its kind plus those. A word moves into §13 when a second node reads it.
    - §9 says "The language model", so Patterns' declared exception goes and every copy is byte-equal. PlayMap gets a copy at
      `docs/SHOWBUS.md`.

    *Why:* drift is the whole of G38–G42. *Alternatives:* fetch the master in CI (network, credentials, a private repo); a git submodule
    (shallow clones, two round processes).

11. **Stagewatch's `NODES.md` is rewritten from the code and held by a test that its hello example is what the builder makes.** The
    9700 Find stays until Patterns L47 is walked.

12. **AGMessage keeps `proposals` and its 1 s ping.** No reader branches on the cap name; renaming costs a test change
    (`AGMessage/app/test/tls.test.js:82`). Its papers say "every second".

13. **`presenting` stays, named.** §2 lists it as the one show fact a hello may carry: a Slides agent's boolean that other agents'
    windows show. Rule 4 says so. *Alternative:* drop it — the agent window would lose its only source for other agents.

---

## 2. The master text, 1.3 (the changed and new parts)

Unchanged sections are not repeated.

### Title and header (outside the hashed body)

```
# Show Bus 1.3 — the contract between Stagewatch, Patterns, PlayMap, AGMessage and their micro-apps

*Version 1.3, <date> (1.1: 2026-10-10; 1.2: 2026-10-11; 1.3: the `playmap` kind, `proto` as the revision, the hello's numbers,
where it is sent from and how one id is heard, reads after pairing and each node's public view, the certificate's whole span,
§12 windows, §13 the numbers each node checks itself against). This page is the contract; `docs/ECOSYSTEM.md` is the why.
Patterns, PlayMap and AGMessage each keep a copy whose text from §1 to the end is this one byte for byte (body sha256
`<64 hex>`). Each tree's own test stops an accidental edit of its copy; a copy older than this master is found only where the
four trees are compared (`tools/showbus-copies.sh`). This one is the master.*
```

A copy's header paragraph: `*<App>'s copy of the Show Bus contract, 1.3 (body sha256 <64 hex>). The master is Stagewatch's
docs/SHOWBUS.md; where they differ, the master wins. How <App> implements it: <its page>.*`

### §1 rules 4 and 5 (replacing them)

> 4. **Discovery says what a node is, never what the show is doing; show data goes only where the node knows the reader.** The one
>    show fact a hello carries is a Slides agent's `presenting`. A node's *public view* — what any screen in the room may show,
>    listed for each node in §8 — is open by design. Everything else (its state beyond that view, its context, its journal lines,
>    the answers to its queries) travels only on an authenticated link (a key, a proof, a pairing: §7), to this machine where the
>    node trusts it, or from a node whose operator left it open (no token), which says so amber in its own context. A node that still
>    serves more lists it in §8, with what ends it.
> 5. **Secrets ride in no URL, no journal line, no frame and no beacon.** Keys, tokens and PINs go in a header or a proof (§7). The
>    one exception is a *view pass* (§12.2) — a window's address, a viewing code, a crew link: view-only or acknowledge-only,
>    revocable and counted — which a browser can be given only as an address. *(windows-for-peers' carrier words follow here: where
>    else a view pass may ride.)* A node that holds another's view pass keeps it as a secret.

### §2 Discovery (replacing the section)

> Every node says hello every **2 s** on UDP 9928 and **every node listens** on 9928 (shared port, `SO_REUSEADDR`).
>
> **Where a hello goes.** A node whose doors the network can reach broadcasts its hello (255.255.255.255:9928), sent from a socket
> bound to the address its doors are bound to — every interface, or the one address — so the address a hello comes from is one its
> doors answer on. It may also send a copy to 127.0.0.1:9928; each send is on its own, so a broadcast that fails never stops the copy.
> **No reader relies on the copy**: on a shared port a datagram sent to one address reaches one listener of several. A node whose
> doors are bound to this machine only says no hello. `ports` names only doors every receiver of that hello can open. *(Finding
> nodes on one machine with no network: lab line LB-1, then 1.4.)*
>
> ```json
> {"t":"main","v":1,"proto":3,"id":"3f9c2a1b7d0e","inst":"a41f09c2","name":"Hall A","host":"FOH-PC",
>  "ports":{"http":9924,"https":9924,"ws":9924},
>  "caps":["timer","cues","messages","context","receipts","clock","screens","windows","aux","slides-in","copilot","approvals","mcp"],
>  "clock":1760101234567,"tls":"sha256:<64 hex>"}
> ```
>
> | field | meaning |
> |---|---|
> | `t` | node kind, `[a-z0-9_-]{1,16}`: `main` (Stagewatch), `agent` (Stagewatch Slides on a presenter PC), `patterns`, `playmap` (PlayMap), `messenger` (AGMessage) — §13 `kind` |
> | `v` | the hello's format: 1. A reader drops a hello whose `v` it does not know |
> | `proto` | the Show Bus revision whose shapes the node speaks, a whole number that only grows: 1 = 1.1, 2 = 1.2, 3 = 1.3 (1.2 nodes sent 1). What it does not keep yet is in its §8 row. A reader never refuses a hello for it, and never decides a safety question by it |
> | `id` | stable per install, random (at least 32 bits), `[A-Za-z0-9_-]{6,40}`, never derived from a secret, a serial or a licence |
> | `inst` | 8 lowercase hex, new at every start of the process |
> | `name` / `host` | the node's name and its machine's, each ≤ 64 characters |
> | `ports` | ≤ 8 doors: `http`, `https`, `ws`, `wire` (a line wire: Patterns 9697, PlayMap 9941), `osc`, `companion` (Stagewatch's Companion TCP), `sync` (PlayMap's clock exchange) — §13 `port` |
> | `caps` | ≤ 32 words of `[a-z0-9.-]{1,24}` — §13 `cap`, plus words a node's own page declares; a dotted word is a finer ability of the word before the dot; a reader ignores words it does not know |
> | `clock` | the sender's Unix time in ms (a first, rough offset; §5 is the real one) |
> | `tls` | `sha256:<64 lowercase hex>` of the certificate's DER, with `ports.https` (§10) |
> | `presenting` | a Slides agent only: whether it is showing a slide (the one show fact a hello carries, rule 4) |
> | anything else | ignored by readers that do not know it (`build`, `role`, and the older top-level `web`, `wire`, `machine`) |
>
> A hello is at most **1200 bytes** as sent (one unfragmented datagram); a reader takes any up to at least 2048 bytes, keeps at
> least 32 caps and keeps `name` and `host` whole.
>
> **Hearing.** A reader lists every kind that passes these rules and acts only on kinds it knows. A peer is **live** while hellos
> keep coming, **stale** after 6 s without one, **gone** after 12 s; a reader forgets it after 60 s and keeps at most 128 peers (a
> new id beyond that is dropped).
>
> **One id, one peer.** A reader keeps, per id, every address it heard in the last 6 s, and dials the address its link already uses
> while that address is still heard; an address change alone is not a change of the peer. A peer heard from 127.0.0.1 or from one of
> the reader's own addresses is on the reader's machine: the reader may dial 127.0.0.1 first and, if refused, the heard address.
>
> **Two nodes, one id.** Ids are not authenticated and travel with copied settings. A reader that hears one id with `inst` A, then B,
> then A again within 6 s — or, from a node that sends no `inst`, two different `host` values within 6 s — marks the id in conflict:
> it shows it amber naming both hosts and addresses, keeps the link it had, dials no new link to that id, and clears the mark after
> 60 s without the alternation. A restart (A, then only B) is not a conflict; one `inst` from two addresses is one node.
>
> The older beacons (Patterns 9700, Stagewatch rooms 9927) remain for their own features and are not used for discovery between apps.

### §4 State frames (first paragraph replaced)

> Every state stream carries its frame format version and a **frame counter** that rises with every published frame, so a receiver
> that sees a smaller one after a reconnect knows the sender restarted: Stagewatch's frames `v` and `seq`, PlayMap's STATE `seq`,
> Patterns' STATE `rev` (§8). The node a stream belongs to is named by how it was found (its hello) or by the first message of the link
> (§7.1); a frame need not repeat it. Counting values travel as durations (`seconds`, `remainingMs`, `positionMs`) with a
> **discontinuity counter inside the object they count** — `video.seq` in Patterns' STATE, `clip.seq` in PlayMap's — that moves on
> every seek, pause, nudge, loop and item change and never for the playhead running on.

Second paragraph gains: "A link pings every 1–2 s (§5)."

### §6 Context (additions after the `peers` bullet)

> - **Sizes (§13).** A context is at most 64 KiB as sent, counted in UTF-8 bytes, with at most 200 items, 400 links, 20 `recent`,
>   256 `commands` and 32 `peers`; `headline` ≤ 200 characters; an item's `id` ≤ 60, `kind` ≤ 30, `label` ≤ 80, `state` ≤ 160; a
>   link's end ≤ 120; a recent line ≤ 200; a command's `verb` ≤ 512 and `words` ≤ 512. A reader accepts at least these numbers. A
>   reader keeps a `verb` whole or drops it (and counts it), and keeps at least 200 characters of `words`. A reader that keeps a peer's
>   commands keeps at least 256; a view of them (a prompt's brief, a drawn card) may show fewer, panic, air and transport rows first,
>   and says how many it left out.
> - **Trimming.** A sender lists panic, air and transport commands first. Over 64 KiB it leaves out, in this order and only until it
>   fits: the oldest `recent` lines; grey links, then the other links; commands other than panic, air and transport, from the end of
>   its list; grey items, then green items. It never leaves out a red or amber item or a panic, air or transport command. It adds one
>   grey item `{"id":"context:trimmed","kind":"health","label":"Context","light":"grey","state":"left out: 12 recent, 40 links, 30 commands, 5 items"}`.
> - **Posting.** A node that posts its context to a desk (`POST /api/peers/context`) does so on change and at least every 10 s while
>   linked, never more than once a second; the desk keeps a context 30 s and answers a context over 64 KiB with 413 and its size.
> - *(cross-app-proposals: the command notation and the desk's risk matching; peers-not-screens: the `peers` fields; windows-for-peers:
>   the cross-node link end; ecosystem-lane: a posted context may appear in the desk's show report.)*

### §7.2 Reading a node that asks for a token (new)

> - **Line wires** (Patterns 9697, PlayMap 9941). On a node with a token, a connection the node does not trust (not from this machine
>   where it trusts loopback, not from a trusted address) hears `NONCE <32 hex>` as its first line and nothing of the show until it
>   pairs. `STATE` (the greeting and the pushes) and the answers to `STATE`, `STATUS`, `CONTEXT` and every subscription begin after
>   `OK paired`, and stop again when the node's token changes. Before pairing a connection may send what finding and pairing need —
>   `HELLO`, `NONCE`, `AUTH-PROOF`, `PING`, `HELP`, `VERBS` — and what the node's §8 row names as still open; anything else is answered
>   `ERR [@id] not paired — this node shows its state only to a paired connection`. An open node (no token) greets with `STATE` at once.
>
>   ```
>   ← NONCE 3f9a0c…(32 hex)                    first line, nothing else of the show
>   → HELLO Stagewatch
>   ← OK
>   → @s1 STATE
>   ← ERR @s1 not paired — this node shows its state only to a paired connection
>   → @pa AUTH-PROOF <64 hex>                  HMAC-SHA256(token, nonce + "Stagewatch")
>   ← OK @pa paired
>   ← STATE {…}                                 the greeting, now; then on every change
>   ```
>
> - **HTTP and WebSockets**: every read beyond the public view wants the key, token or PIN (§7), as in 1.2.
> - **A reader pairs before it reads.** It answers `NONCE` with `AUTH-PROOF` (or asks `@n NONCE`), sends its reads only after `OK
>   paired` or `OK open`, and takes `STATE` whenever it comes. With no token for a node that sent `NONCE` it shows that link amber —
>   "not paired — <node> shows its state only to a paired link; its pairing token goes in <where>" — and sends nothing but `PING`.

### §8 What each node publishes and consumes (replacing the table)

> | node (`t`) | publishes | consumes | public view (open by design) | not kept yet — and what ends it |
> |---|---|---|---|---|
> | **Stagewatch** (`main`) | hello; `/ws` frames (`v`, `seq`; public, public detail, operator); receipts; `/api/context`; pong with times; the approval desk (§11); windows (§12); `/tls` | Patterns' and PlayMap's STATE and context (its playout links); every node's posted context; proposals and their results; Slides agents' feeds | the public frame on `/ws` and `/api/state`, `detail=1`'s public detail, the viewer, role and audience pages, `/api/holding/<id>`, `/tls`; a window page to whoever holds its address | `?pin=` takes the PIN or a key in an address (bookmarks, scripts) — ends when Stagewatch's maintainer retires it (not scheduled); Companion TCP 9923 has no authentication when the operator binds it to the network (loopback by default) — not scheduled |
> | **Patterns** (`patterns`) | hello; wire STATE (`rev`); receipts `@id`; `CONTEXT` and `/api/context`; its context and proposals to a desk; approved lines collected (`APPLY`) | Stagewatch's frames and context; its twin nodes | the pages themselves, the EDID files, the audience port | the wire's greeting STATE and its answers to queries before pairing (Patterns PLAN §103.7) — ends with Round 90.2 if its maintainer chooses it; under its scope A, `NAV`, `MENU`, `CUE LIST`, `ARCADE …` and `ASSISTANT …` stay open until the desk's own cross-machine asks pair by proof; `STARTTLS` not offered (OPEN L55) |
> | **PlayMap** (`playmap`) | hello (with `sync`, `inst`, `build`); TCP 9941 line wire in Patterns' grammar, receipts; `/api/context` (token), `/mcp`, the `/ws` line channel `playmap.lines.v1` (paired); its context to a desk; approved lines collected | Stagewatch's `/ws` frames; Patterns' STATE and context; other PlayMap nodes (sync anchors, media) | `/health`, `/fleet`, `/tls` | TCP 9941 pushes STATE on `HELLO` and answers STATE/CONTEXT before pairing (`docs/WIRE.md`, items 2 and 4; its owner's decision 17) — open question to its owner |
> | **AGMessage** (`messenger`) | hello; `/api/context` (room, counts, action-prompt titles — no content); its context and proposals to a desk; `notice` collected | Stagewatch's frames (timer, message acknowledgement by id, VT), receipts and context | `/api/health` (`ok`, version, uptime), `/tls` | drops hellos of kinds it does not know (G18) — ends with the agmessage area's unit |
> | **Slides agent** (`agent`) | hello (with `presenting`); slide feed (its own `seq` and version) | — | — | none |
>
> *(the siblings' rows and cells: playout-links' VT row, cross-app-proposals' desk flows, ecosystem-lane's consumes cell.)*

### §9 (one word)

"Claude: copilot plans, …" becomes "**The language model**: copilot plans, …".

### §10 first bullet (replacing it)

> - **One certificate per install.** On first run each node makes a self-signed certificate (ECDSA P-256, SHA-256) whose whole span —
>   NotBefore to NotAfter, any backdating for a skewed clock included — is **at most 825 days** (Apple devices refuse a TLS server
>   certificate valid for longer, even one the user chose to trust), NotBefore at most 24 h before it was made. At a start it makes a
>   new one when fewer than 30 days remain or when the one it has spans more than 825 days (a new fingerprint: every pinned peer sees
>   CERTIFICATE CHANGED once). Subject = the node's name; SANs = the host name, `localhost`, 127.0.0.1 and the machine's LAN
>   addresses at the time. Its private key is protected at rest and never copied between machines.

### §12 Windows (new; §12.1–12.3 here, §12.4 onward windows-for-peers')

> **12.1 What a window is.** A *window* is a browser that a node with the `windows` cap controls through an address of its own:
> `/w/<token>`, the token 26 characters of `abcdefghijkmnpqrstuvwxyz23456789` (130 random bits). A node keeps at most 64. The address
> is the window's identity and its pass: whoever opens it sees what the controlling node puts there, and controls nothing. Identity
> lives in the controlling node, not in the browser, so two browser sources of one program are two windows.
>
> **12.2 View passes.** A window's address — like a viewing code and a crew link — is a *view pass*. A node that issues one makes it
> view-only (or acknowledge-only), revocable at once (`window revoke`: the old address stops and its screen says so), never cached or
> indexed, and sends no referrer from it. It counts wrong view passes per device apart from wrong PINs and keys: 5 a minute lock that
> device out of view passes for the minute, and a right PIN or key never clears that count. It lists a window's address only to its
> operator (the PIN or a control key; never a read or node key). A node that holds another's view pass keeps it as a secret: never in a
> hello, a context, STATE, a frame, a journal line, a log line or a proposal.
>
> **12.3 The window link.** A window holds one WebSocket to its node, passing its token as `win=<token>` (a page can set no header on
> a WebSocket, and its own address already holds the token). Before any frame, and again on each change, it hears
> `{"type":"window","id","name","content","hold","holdText","holdImage","onLoss","fadeMs","rev"}`, then public detail frames;
> `{"type":"identify","name","id"}` and `{"type":"reload"}` when told; `{"type":"window","revoked":true}` when its address is gone (it
> stops trying). After each swap, hold or failure it reports `{"type":"shown","rev","content","hold","ms","error"}`; the node shows a
> window green only when it reports the revision it was sent.
>
> *(12.4 onward: windows-for-peers' grants — `how` page or draw, the `grants` and `window-*` node-link messages, the path rule, the
> black end, held forms, placement claims, the host browser reading the shell's state, and the operator frame's `windows` block.)*

### §13 The numbers (new; machine-read)

Inside the hashed body, one fenced block every tree's `ShowBusSpec` test parses (`key value…`, one per line):

```showbus
revision 1.3
proto 3
hello.port 9928
hello.every.ms 2000
hello.bytes 1200
hello.read.bytes 2048
hello.caps 32
hello.cap.chars 24
hello.ports 8
hello.name.chars 64
hello.kind.chars 16
hello.id.min 6
hello.id.max 40
hello.inst.chars 8
peers.max 128
peer.stale.ms 6000
peer.gone.ms 12000
peer.forget.ms 60000
peer.conflict.clear.ms 60000
link.beat.ms 2000
link.stale.ms 6000
link.drop.ms 12000
link.redial.first.ms 500
link.redial.max.ms 5000
ping.min.ms 1000
ping.max.ms 2000
clock.apart.ms 2000
receipt.deadline.ms 3000
request.id.chars 32
context.bytes 65536
context.items 200
context.links 400
context.link.end.chars 120
context.recent 20
context.commands 256
context.peers 32
context.headline.chars 200
context.item.id.chars 60
context.item.kind.chars 30
context.item.label.chars 80
context.item.state.chars 160
context.recent.chars 200
context.verb.chars 512
context.words.chars 512
context.words.read.chars 200
context.post.min.ms 1000
context.post.max.ms 10000
context.kept.ms 30000
proposal.line.chars 2048
proposal.undelivered.ms 60000
cert.days 825
cert.backdate.hours 24
cert.renew.days 30
window.token.chars 26
window.token.alphabet abcdefghijkmnpqrstuvwxyz23456789
window.max 64
view.wrong.per.minute 5
window.grants 4
kind main Stagewatch
kind agent Stagewatch Slides
kind patterns Patterns
kind playmap PlayMap
kind messenger AGMessage
port http
port https
port ws
port wire
port osc
port companion
port sync
cap timer main
cap cues main
cap messages main messenger
cap context main patterns playmap messenger
cap receipts main patterns playmap messenger
cap clock main patterns playmap
cap screens main
cap windows main
cap aux main
cap slides-in main
cap slides agent
cap copilot main
cap approvals main
cap mcp main playmap
cap ai messenger
cap video patterns playmap
cap video.clock patterns playmap
cap video.transport patterns playmap
cap video.seek patterns playmap
cap video.end patterns playmap
cap state patterns playmap
cap eye patterns playmap
cap pages patterns
cap window-draw playmap
cap action-prompts messenger
cap documents messenger
cap proposals messenger
cap playlist playmap
cap media playmap
cap mapping playmap
cap scan playmap
cap show playmap
reserved video.anchor playmap
```

A prose table above the block gives each cap's meaning (`timer`: a show timer and its frames on `/ws`; `windows`: §12; `approvals`:
keeps the §11 queue; `proposals`: sends its AI's proposals to a desk; `state`: line-wire STATE pushes; `show`: Show Mode's facts in
STATE and context; `pages`/`window-draw`: windows-for-peers' meanings; …). A `reserved` word is named on a node's own page and joins as
`cap` when a second node reads it. Rows added by sibling areas (agmessage's 5-minute no-result) go in the same block in S1.

---

## 3. Stagewatch's hello sender and reader (G27, decisions 5 and 6) — code

**`Stagewatch/src/Timer.Integrations/ShowBusHello.cs` (new, pure).**
```csharp
public enum HelloScope { None, Network }
public sealed record HelloDoors(int WebPort, bool WebBindAll, string? Tls, int CompanionPort, bool CompanionBindAll,
    int OscPort, bool OscBindAll);   // 0 = not running
public static class ShowBusHello
{
    public const int Proto = 3;
    public static (SlidesBeacon.Hello? Hello, HelloScope Scope) Main(string nodeId, string inst, string name, string machine,
        HelloDoors doors, bool mcp, long clockMs);
    public static IReadOnlyList<IPEndPoint> Targets(HelloScope scope, int port);   // Network: broadcast, then loopback; None: nothing
    public static bool CapShape(string? cap);                                         // [a-z0-9.-]{1,24}
    public static string NewInst();                                                   // 8 lowercase hex from RandomNumberGenerator
}
```
- Scope: `None` with no web server or when `WebBindAll` is off (the doors are on this machine only); else `Network`.
- Ports: `http`/`ws` (and `https` with a certificate) = the web port. `companion` only when it runs with `CompanionBindAll`; `osc`
  only when it runs with `OscBindAll`. No door bound to this machine is named.
- `name` and `host` cut to 64; `proto` 3; `inst`; caps in a fixed order with `windows` (the web server serves windows) and `mcp` when
  allowed.
- `SlidesBeacon.Hello` gains `Inst` (JSON `inst`), appended after `Tls`.
- `MainViewModel.Slides.cs:21-31` calls it with `_store.Snapshot` (room name, `Remote.WebBindAll`, Companion and OSC binds) and the
  servers' running ports — never the live `Settings` — and passes the scope to the beacon. The send socket stays bound to every
  interface, which matches Stagewatch's doors (any or loopback, never one LAN address: `WebServer.cs:212`).

**`Stagewatch/src/Timer.Integrations/RemoteSlides.cs` (`SlidesBeacon`).**
- The constructor takes `Func<(Hello?, HelloScope)>` (an overload keeps `Func<Hello?>` for the agent, scope `Network`), a listen
  port and an optional `IReadOnlyList<IPEndPoint>? targets` override, so a test sends to a port only its own socket binds.
- `Send` sends to each target in its own `try`, keeping per target `Sent`, `Failed` and the last failure's words. `Status` is
  `Said` (the broadcast went), `ThisPcOnly` (only the loopback copy went), `NotSaid` (every target failed) or `Silent` (scope
  `None`), with the beats it has held.
- Reader: caps `Take(32)` of `CapShape` words (today `Take(16)` of anything, `:190`); name and host cut at 64 (`:220`). Per id an entry
  keeps `Addresses` (address → last heard), `Dial` (sticky: kept while heard within 6 s, else the newest), `Insts` (the last two
  with their order) and `Conflict` (decision 6). `Peers()` returns `Peer(Hello, Dial, AgeSeconds, Addresses, Conflict)`.
- The `Proto` comment (`:117`) says "the Show Bus revision whose shapes this node speaks (§2)".

**`Stagewatch/src/Timer.Integrations/SlidesAgent.cs:72`** — the agent's hello carries `Proto: ShowBusHello.Proto` and an `inst`.

**Desk and context.**
- Producer → Show network gains a first line from the beacon's `Status`: "Said on the network as Hall A (3f9c…)" / "Said on this PC
  only — no network: <words>" / "Not said: <words>" / "Not said — Remote is set to this PC; other apps reach it at 127.0.0.1:9924".
- When every target has failed for 3 beats, the context gains an amber item `bus:hello` ("this Stagewatch's hello can't go out:
  <words> — other apps can't find it"). Only the broadcast failing is not amber: with no network, no other machine can be reached
  either, and the line says so.
- A heard id in conflict shows its peer row amber with both hosts and addresses, and the context's `peers` entry carries it.
- No new verb, menu or Companion change. `MainViewModel.ShowBus.cs:100`'s kind switch calls `ShowBusKinds.Label`.

**The rig.** `tools/Stagewatch.Rig/Program.cs` gains `--bus <listen port>` and `--bus-to <host:port>` (default off). Its hello is
`ShowBusHello.Main(...)` over the rig's own servers and `--bind`; `--bus-to` replaces the targets, so the e2e listens on a port the
rig never binds.

---

## 4. File-by-file changes

### Stagewatch

| file | change |
|---|---|
| `docs/SHOWBUS.md` | 1.3 (§2 of this design), with every sibling's words |
| `docs/NODES.md` | rewritten from the code (outline below) |
| `docs/ECOSYSTEM.md` | title and node table name PlayMap; `:31-32` → "specified in `docs/SHOWBUS.md` (Show Bus 1.3)" with each app's release |
| `docs/REMOTE.md` | the 2.18 section: the hello (scope, `proto` 3, `inst`, `windows`), one id one peer and the conflict light, view passes counted apart, the certificate's span and the one renewal |
| `docs/FEATURES.md`, `docs/WHATS_NEXT.md` | the Show network's hello line; what 1.3 did not do (LB-1, rollover, `?pin=`, 9923) |
| `src/Timer.Core/ShowBusKinds.cs` (new) | `Label(kind)` from §13 (`playmap` → PlayMap) |
| `src/Timer.Integrations/ShowBusHello.cs` (new), `RemoteSlides.cs`, `SlidesAgent.cs` | §3 |
| `src/Timer.App/ViewModels/MainViewModel.Slides.cs` | the builder from the snapshot; scope; the status line |
| `src/Timer.App/ViewModels/MainViewModel.ShowBus.cs` | `bus:hello` when every target fails; conflict rows; `ShowBusKinds.Label` |
| `src/Timer.Show/ShowContext.cs` | Stagewatch's own context within §6: ≤ 200 items, the byte-trimming order, `context:trimmed` |
| `src/Timer.Remote/CertificateLife.cs` (new, pure) | `Span = 825 days`, `Backdate = 1 day`, `Window(now) → (NotBefore, NotBefore + Span)`, `NeedsRenewal(notBefore, notAfter, now)`: fewer than 30 days left or span over 825 days |
| `src/Timer.App/NodeCertificate.cs` | `:34` and `:66` use `CertificateLife` |
| `src/Timer.Remote/WebServer.cs` | a second table `_passFailures` for view passes (window addresses, viewing codes, crew links); `LockedOut`/`CountFailure` (`:625-636`) use it; `Authorize`'s success (`:1083`) clears only `_authFailures` |
| `tools/Stagewatch.Rig/Program.cs` | `--bus`, `--bus-to` |
| `tools/BusCarrierProbe/` (new, a console) | LB-1: binds N sockets on a port (any and 127.0.0.1, `ReuseAddress`), sends to 127.0.0.1, 127.255.255.255, 255.255.255.255 and 239.255.99.28 on loopback, prints who heard what; run on the maintainer's PC with the network up and off |
| `tools/showbus-copies.sh` (new) | prints each tree's header hash and body hash, diffs against the master; exit 1 on a mismatch; "not run" for an absent tree |

Readers' numbers that other areas own: `PeerContexts` commands 256, verb kept or dropped, `words` 200, UTF-8 bytes and 413
(cross-app-proposals SW-1); items 200 and links 400 (windows-for-peers SW-W1); the operator frame's `windows` and the Companion window
feedbacks (windows-for-peers SW-W4, SW-W6).

**`docs/NODES.md`, rewritten (outline).** 1. Roles. 2. Discovery: the hello as `ShowBusHello.Main` builds it (example held by a test),
scope, what Stagewatch hears (every kind, ≤ 128, 60 s, every address, the sticky dial, conflicts), Find (9928, plus 9700 until
Patterns L47 is walked). 3. Slide feed (unchanged). 4. Reading and driving Stagewatch: the public view vs keyed reads; `/ws` frames
and beats; one grammar; keys and the `node` scope; MCP; screens vs windows (REMOTE 2.17, SHOWBUS §12). 5. The Patterns link as built
(playout-links rewrites this paragraph with its unit). 6. AGMessage: `ports.http`, a `node` key in `Authorization: Bearer`, frames,
receipts, context every 5 s, proposals and `notice`. 7. PlayMap. 8. Joining: the contract is `SHOWBUS.md`; its §13 numbers.

### Patterns (Round 90, as patterns-playmap-side numbers it; the maintainer's numbering wins)

| unit | file | change |
|---|---|---|
| 90.2 | `docs/ADR.md` | ADR-016 "The wire shows its state only to a paired connection", reversing PLAN §103.7 with the scope chosen (A or B) — the maintainer's call |
| 90.2 | `src/Patterns.App/Services/WirePeer.cs` | `Presented` and `Loopback` on the peer; `Paired(token)` reads them |
| 90.2 | `src/Patterns.App/Services/ControlService.cs` | on a token desk, an untrusted connection hears `NONCE` first and no greeting (`:356-363`); `STATE` after `OK paired`; `Broadcast` (`:763-774`) re-checks `peer.Paired(currentToken)` at every push, so NEW TOKEN stops STATE; the scope's reads refused unpaired; one log line per connection for a refused unpaired read ("wire: <address> asked STATE unpaired — refused (reads follow pairing)") |
| 90.2 | `src/Patterns.Core/Services/ControlProtocol.cs` | `OpenBeforePairing(cmd)` beside `IsQuery` (`:1454`): `HELLO`, `NONCE`, `AUTH`, `AUTH-PROOF`, `PING`, `HELP`, `VERBS`, and under scope A the named open queries; the refusal words |
| 90.2 | Super Check (the Remote row) | "reads follow pairing on the wire" beside today's amber for no token (`docs/REMOTE.md:30`) |
| 90.2 | `integrations/companion-module-patterns/src/main.js` | `NAV`, `RECORD ON` and every read are asked after `OK paired`/`OK open` (`AUTH-PROOF` goes through `ask` so its answer is seen); with no token and a `NONCE` heard: status "pairing token needed — this desk shows its state only to a paired connection"; `trustProblem` (`:373`) tells "no token configured" from "wrong token" |
| 90.2 | tests | the `Wire` helpers (`ShowBusAppTests.cs:71-82`; `RemoteTrustTests`) read `NONCE` first and `STATE` after pairing on a distrusted token desk; `RemoteTrustTests.cs:132-150` inverted for `STATUS`; `NavAppTests`, `ShowBus12AppTests` checked |
| 90.5 (before the papers) | `src/Patterns.Core/Services/ShowBusTls.cs` | `Life = 825 days` as the whole span: `CreateSelfSigned(utcNow.AddDays(-1), utcNow.AddDays(-1) + Life)` (`:84,103`); `NeedsRenewal` also when the span is over `Life` (`:106`); comment `:77` |
| 90.5 | `src/Patterns.Core/Services/ShowBus.cs`, `ShowBusNetwork.cs` | `Proto = 3` (`:43`); the reader keeps 128 peers, every address per id with the sticky dial, the conflict mark (`:106-113`) |
| 90.4 | `docs/SHOWBUS.md`, `tests/Patterns.Core.Tests/ShowBusSpecTests.cs` | the 1.3 copy and its test, in the round's papers commit |
| 90.4 | papers | PLAN §108, REVIEW, CHANGELOG (evidence class), README, REMOTE (Trust `:28-41` and the Show Bus section `:588-596`), COMPANION, OPEN rows |

`KindWords` for `playmap` (`ShowBusNetwork.cs:168`) is patterns-playmap-side's 92.1. Patterns' hello socket is already bound to its
bind address (`BeaconService.cs:116-117`) and silent on loopback (`ShowBus.MayAnnounce`), so §2's sending rules need no change there.

### AGMessage (its next release, 0.4.1, shared with peers-not-screens and windows-for-peers)

| file | change |
|---|---|
| `docs/SHOWBUS.md` | the 1.3 copy |
| `app/lib/tls.js` | `to = from + days` (`:78-79`); `loadOrCreate` also renews when `validTo − validFrom` is over 825 days (`:124`); comment `:12-15` |
| `app/lib/nodes.js` | `proto: 3`, `inst` (`:72-75`); caps kept 32 of `[a-z0-9.-]{1,24}`, name and host 64 (`:44-47`); 128 peers; per id every address with the sticky dial and the conflict mark; `onChange` not called for an address change alone (`:86-89`); `find` matches any heard address or the host (`:108-113`); when `HOST` is one LAN address the hello goes from a second socket bound to `HOST` and no loopback copy is sent (`:91-96`); header `:5,9` "a ping every second" |
| `app/server.js` | `Discovery` gets `HOST` (`:1004-1005`) |
| `app/test/showbus.test.js` (new), `app/package.json` | §5 tests; added to `scripts.test` |
| `docs/NODES.md`, `CHANGELOG.md` | 1.3; a ping every second; 825 days in all and the one renewal |

### Mapper (after round 3 closes, or by its owner's leave)

| file | change |
|---|---|
| `docs/SHOWBUS.md` (new) | the 1.3 copy |
| `docs/SHOWBUS-NODE.md` | "implements Show Bus 1.3" (`:3`); §1 reader note: ≤ 1200 sent, 32 caps kept (`:21`); §5 rewritten as "answered in 1.3", item by item (decision 2); the reader's summary no longer says the contract bounds only `recent` |
| `src/PlayMap.Core/Bus/ShowBus.cs` | the reader keeps 32 caps (`:167`); comment `:127` (2048 → 4096 as built); `Proto` 3 in `PlayMapHello` |
| `src/PlayMap.Net/Bus/PeerTable.cs` | 128 peers; every address per id with the sticky dial; an address change alone is not `Changed`; the conflict mark from `inst` (`:53-74`) |
| `tests/PlayMap.Core.Tests/ShowBusSpecTests.cs` (new) | §5 |

PlayMap already says no hello when bound to loopback (`HelloService.Announcing`, `:84`) and sends from a socket bound to its bind
(`:131-132`), so §2's sending rules need no change. Its TCP wire is the open question of decision 7.

---

## 5. Tests

**Stagewatch suite (`tests/Stagewatch.Tests`, run from `Program.cs`).**
- `ShowBusSpecTests.cs` (new): reads `docs/SHOWBUS.md` the way `Program.cs:2036` reaches `src/Timer.App`.
  - The body (no BOM, LF, one final LF) hashes to `BodySha256` pinned in the test and printed in the header; the failure prints the
    new hash and "Patterns, PlayMap and AGMessage follow".
  - The `showbus` block parses; `ShowBusKinds.Label` equals every `kind` row.
  - `ShowBusHello.Main` with the worst inputs (64-character emoji name, every door, every cap, `tls`) is ≤ `hello.bytes`; its caps are
    `cap … main` rows or `docs/NODES.md`'s ```showbus-node caps; its ports are `port` rows; its `proto` is the block's; `inst` is
    `hello.inst.chars` hex. The agent's caps are `cap … agent` rows.
  - `SlidesBeacon` keeps `hello.caps` caps, names of `hello.name.chars`, takes `hello.read.bytes`, keeps `peers.max`.
  - `ScreenWindows.TokenLength`, the alphabet and `Max` are the block's; `CertificateLife.Span` is `cert.days`.
  - `docs/NODES.md`'s hello example parses to the builder's output for the same fixed inputs.
  - Rows owned by other units are asserted from S8 on, once those units have landed: `PeerContexts` keeps `context.items`,
    `context.links`, `context.commands`, `context.verb.chars` whole or dropped, `context.words.read.chars`, and refuses over
    `context.bytes` UTF-8 bytes.
- `ShowBusTests.cs`:
  - `ShowBusHello.Targets` per scope; the scope from binds; `companion`/`osc` absent when bound to loopback; no hello when
    `WebBindAll` is off.
  - Hearing tables on injected time: a loopback datagram then a LAN one, alternating each second for 20 s, keeps one `Dial`
    address and raises one peer; after 6 s without the dialled address the newest is dialled; `inst` A, B, A within 6 s → `Conflict`;
    A then only B (a restart) → none; one `inst` from two addresses → one peer, no conflict; the mark clears after 60 s.
  - A live check: a `SlidesBeacon` listening on port P with targets `[127.0.0.1:Q]` (Q bound only by the test's `UdpClient`) is heard
    within 3 s with `proto` 3, `inst` and `windows` (G27's missing test). A target that throws does not stop the next, and `Status`
    reads `ThisPcOnly` when only the broadcast throws.
- `CertificateLife`: NotAfter − NotBefore = 825 days for any now; renewal at 29 days left, not at 31; renewal for an 826-day and a
  1,826-day certificate with 800 days left.
- `ShowContext`: 64 windows + 8 links + 30 departments + 20 long recent lines → ≤ 200 items and ≤ 64 KiB, trimmed in the §6 order,
  `context:trimmed` grey with the counts, no red or amber item left out.
- `WebServer` (the suite's live server tests): five wrong window addresses from one address lock out window addresses, while the PIN
  from that address still works; a right key from that address does not clear the window count; five wrong PINs still lock out PINs.

**Rig e2e (`tests/e2e`).** `bus.e2e.cjs` (new): start the rig with `--bind all --bus 19929 --bus-to 127.0.0.1:19928`; a `dgram` socket
on 19928 alone hears a hello within 3 s with `t:"main"`, `proto:3`, `inst`, `windows` in caps, `ports.http` the rig's port, ≤ 1200
bytes. With `--bind local` the same socket hears nothing for 5 s.

**Patterns** (Round 90): Core `ShowBus12Tests.cs:26` → span exactly 825 days, and `NeedsRenewal` true for a 1,826-day certificate;
Core `ShowBusSpecTests.cs` (copy hash; numbers; `Capabilities` ⊆ `cap … patterns`; `ShowBusNetwork.Parse` keeps 32 caps; the peer
table keeps 128 and the hearing table above); App (headless) `RemoteTrustTests`, with `TrustLoopback = false` in `try`/`finally`: a
token desk greets with `NONCE` and no `STATE`, refuses `STATE`/`STATUS`/`CONTEXT` unpaired with the words, pushes nothing to that
connection, sends `STATE` after `OK paired`, and stops the pushes after `NEW TOKEN`; an open desk and a trusted loopback are unchanged;
under scope A, `NAV` still answers unpaired. Module node tests: a token desk that sends `NONCE` first still fills the Navigator (NAV
after `OK paired`); no token on a token desk shows the status words, not BadConfig's wrong-token words.

**AGMessage** (`npm test`): `showbus.test.js` — copy hash; `CAPS` ⊆ `cap … messenger`; `hello()` ≤ 1200 bytes with a 64-character
name, with `proto` 3 and `inst`; `parseHello` keeps 32 caps, a 64-character name, takes 2048 bytes; 128 peers; an address flip raises
no `onChange` and `find` matches either address; the conflict table; with `HOST` a LAN address, the injected send goes from a socket
bound to `HOST` and never to 127.0.0.1; `tls.test.js`: `validTo − validFrom ≤ 825 days`, and an 826-day certificate is renewed.

**Mapper**: `ShowBusSpecTests` (copy hash; `PlayMapHello` ≤ 1200 bytes, caps ⊆ `cap … playmap`, ports ⊆; `Parse` keeps 32;
`SHOWBUS-NODE.md`'s §1 JSON is what `PlayMapHello` builds for its fixed inputs); `PeerTable`: an address flip is not `Changed`, the
sticky dial, the conflict mark, 128 peers; `EyeContextTests.cs:97` and `WireAndBusTests.cs:313` (16 caps) follow the reader's 32.

**Cross-tree**: `tools/showbus-copies.sh ../Patterns ../Mapper ../AGMessage` in G44's interop lane (`design/ecosystem-lane.md:783`,
row S7.2); "not run" where the trees are absent.

**Lab line LB-1** (not a test): `tools/BusCarrierProbe` on the maintainer's Windows 11 PC, network up and every adapter off; the table
goes into `docs/WHATS_NEXT.md` and decides 1.4's carrier. ecosystem-lane's row S1b waits for it (`design/ecosystem-lane.md:718`).

---

## 6. Units (commit-sized, in order)

| # | repo | unit | gaps | after |
|---|---|---|---|---|
| S1 | Stagewatch | Show Bus 1.3 draft: master text with every sibling's words (and the three words asked of windows-for-peers), `ShowBusSpecTests` (hash of the draft, the block parses, kinds), `ShowBusKinds` | G38, G40, G22 (text), G39 (text), G45 (rule), G42 | the siblings' words agreed |
| S2 | Stagewatch | Hello 1.3: `ShowBusHello` from the snapshot, scope (silent on loopback), per-target sends with `Status`, doors receivers can open, `proto` 3, `inst`, `windows`; reader 32 caps, 64-character names, every address per id, sticky dial, conflict; Show network line, `bus:hello`, conflict rows; rig `--bus`/`--bus-to`; `bus.e2e.cjs` | G27, G40 | S1 |
| S3 | Stagewatch | Stagewatch's own context within §6: ≤ 200 items, the byte-trimming order, `context:trimmed` | G40 | S1 |
| S4 | Stagewatch | Certificates: 825 days in all, an over-long one replaced once (`CertificateLife`, `NodeCertificate`) | G39 | S1 |
| S5 | Stagewatch | View passes counted apart from credentials (`WebServer`) | G22 (§12.2) | S1 |
| S6 | Stagewatch | LB-1's probe (`tools/BusCarrierProbe`) and its WHATS_NEXT row; the Windows run is the maintainer's | G27 (carrier) | — |
| S7 | Stagewatch | Papers: `NODES.md` from the code with its test, `ECOSYSTEM.md`, REMOTE 2.18, FEATURES, WHATS_NEXT | G41, G38 | S2–S5 |
| S8 | Stagewatch | 1.3 final: the header says final, `BodySha256` pinned, the other units' rows asserted, `tools/showbus-copies.sh` | G38, G39, G40, G41, G42 | S1–S7; cross-app-proposals SW-1 and SW-7; windows-for-peers SW-W1 and SW-W7's words; playout-links' §2.6 words; agmessage's §11 words |
| PT-90.2 | Patterns | Reads follow pairing (scope A or B), ADR-016, `WirePeer` pairing, module pairs before it reads, tests, Super Check row, log line | G45 | S1; the maintainer's yes; playout-links' `PlayoutLink` in a Stagewatch release |
| PT-90.5 | Patterns | Certificate 825 days in all and the one renewal; hello `proto` 3, 128 peers, every address, sticky dial, conflict | G39, G40 | S8 |
| PT-90.4 | Patterns | The round's papers with the 1.3 copy and `ShowBusSpecTests` | G38, G40 | S8, PT-90.2, PT-90.5 |
| A1 | AGMessage | 825 days in all and the one renewal; the 1.3 copy and `showbus.test.js`; `proto` 3, `inst`, 32 caps, 64-character names, 128 peers; send from `HOST`; every address, sticky dial, conflict, `find` on any address; NODES ping wording | G39, G40, G42 | S8 |
| M1 | Mapper | The 1.3 copy, `SHOWBUS-NODE.md` answers, `ShowBusSpecTests`; reader 32 caps, 128 peers; `proto` 3; the peer table's address rule and conflict | G38, G40 | S8; round 3 closed or the owner's leave |
| M2 | Mapper | Only if PlayMap's owner says yes to the question: the TCP wire reads after pairing on a token node | G45 | M1; Patterns' Lines devices pair by proof (Round 92) |

S2–S6 are independent once S1 lands. PT-90.2 can land before S8: it needs the rule, not the final hash.

---

## 7. Gaps closed and left

**Closed:**
- **G38** — the `playmap` kind, its caps, a §8 row, ECOSYSTEM naming PlayMap and 1.3; the copies follow through S8, PT-90.4, A1, M1.
- **G40** — `proto` as the revision of shapes; the kind, port and caps tables from what nodes send; §4's two counters; §13; every
  reader raised to 32 caps and 64-character names; Stagewatch's hello within the rules.
- **G27** — no hello when bound to loopback; per-target sends that are counted and shown; doors receivers can open; built from the
  snapshot; a live test on a port only the test binds. The same-machine carrier with no network is LB-1 (left below).
- **G39** — the span rule with the three code fixes and the one renewal of certificates already issued; §9's wording for byte-equal
  copies.
- **G41** — `NODES.md` from the code, held by a test against the builder.
- **G42** — the caps vocabulary as sent; pings every 1–2 s; AGMessage's papers corrected.
- **G22** (this area's part) — §12.1–12.3 and the view-pass class, the `windows` cap, view passes counted apart. The operator frame's
  `windows` and the Companion feedbacks close with windows-for-peers SW-W4/SW-W6.
- **G45** (as a contract) — §1 rule 4 and §7.2, with each node's exemption and what ends it in §8; Patterns' code by PT-90.2 on its
  maintainer's yes.

**Left:**
- *The hello carrier on one machine with no network* — LB-1 measures it on Windows; 1.4 names it.
- *Key-continuity certificate rollover* — a new pinning rule in four trees; 1.4.
- *`?pin=` keys in browser addresses and Companion TCP 9923 without authentication when bound to the network* — named in §8 as
  not scheduled; Stagewatch's maintainer's calls.
- *Patterns' reads before pairing* — until its maintainer decides (ADR-016) and Round 90.2 lands.
- *PlayMap's TCP reads before pairing* — a question to its owner (decision 17); M2 only on a yes, after G35.
- *Lessee sides* (G10, G11, G24, G46) — windows-for-peers.
- *AGMessage hearing every kind* (G18) — the agmessage area.
- *The 413 and PlayMap's posted-context budget* (G25) — cross-app-proposals SW-1 and patterns-playmap-side; §6 states the limit.
- *STARTTLS on the line wires* (Patterns OPEN L55) — unchanged.
- *An unpaired `ASSISTANT ASK`/`MODERATE` spends the desk's model budget today* (`ControlProtocol.cs:1249`) — named in PT-90.2's
  inventory for the maintainer; not decided here.

---

## 8. Risks

- **Reads after pairing breaks token-less readers.** A reader with no token that follows a token desk read-only today sees nothing
  after PT-90.2. Mitigation: §7.2's amber words on every reader, PT-90.2 after `PlayoutLink` ships, the module fixed in the same unit,
  ADR-016 and REMOTE's Trust paragraph. An older Companion module against a 90.2 desk loses its Navigator until updated; Round 90's
  papers say to update the module with the desk.
- **Patterns' own cross-machine asks** (arcade nodes, a node's moderation ask) break under scope B. Scope A keeps them open and named
  in §8; scope B waits for them to pair.
- **One CERTIFICATE CHANGED per upgraded node.** Every pinned peer of Stagewatch (826 days), Patterns (1,826) and AGMessage (825 + 1 h)
  asks once. Mitigation: each tree's release notes say so; a show-day upgrade is the operator's call.
- **The hash fence makes every master edit a change in four trees,** and per-tree CI cannot see a stale copy. Mitigation: 1.3 frozen
  once (S8), the interop lane's S7.2 row, and node-declared caps so a small feature does not wait on four releases.
- **Same-machine finding with no network stays unsolved in 1.3.** A Wi-Fi-off show laptop finds peers only by a typed address until
  LB-1 is run and 1.4 lands. The loopback copy still reaches one listener, at random; nothing relies on it.
- **The conflict rule could hide a real peer** when two machines share an id: neither gets a new link until the operator fixes one.
  That is the fail-safe; the amber row names both machines.
- **Separate view-pass counts give a guesser 5 view passes and 5 PINs a minute** where today the five are shared. View passes are 130
  random bits, so the count protects against noise, not guessing.
- **Merge order on shared files** (`ShowContext.cs`, `MainViewModel.ShowBus.cs`, `WebServer.cs`, `SHOWBUS.md`) with playout-links,
  peers-not-screens, cross-app-proposals and windows-for-peers: each lands in its unit's order; the master has one owner.

---

## 9. Each repo's process

- **Stagewatch** (`Stagewatch/CLAUDE.md`): one `dotnet build` at a time; `tools/test.sh build unit module e2e`; banned symbols stay out
  of `src/` (the builders take time, ports and `inst` as inputs; `Environment.TickCount` is banned); the hello record's new field goes
  at its end; no new `ShowCommandKind` or verb in this area; the WPF parts (the Show network line, the conflict rows) are named for the
  Windows job's smoke and a look on the maintainer's PC; LB-1's Windows run is the PC's work, named and left; no model identifiers
  (hence §9's wording); papers: REMOTE 2.18, NODES, ECOSYSTEM, FEATURES, WHATS_NEXT. The doctrine's "wrong PINs, keys, viewing codes and
  window addresses lock a device out" still holds with two counts; REMOTE says which count each feeds.
- **Patterns** (`Patterns/CLAUDE.md`): Round 90 opens with the newest `docs/field/round-NN/` ("what the files said") and
  `docs/OPEN.md`; the requester and evidence class are named (the maintainer's list; a handed-in critique — this gap analysis and its
  review; the contract `docs/SHOWBUS.md` 1.3). PT-90.2 is a maintainer's decision recorded as ADR-016 before code. One commit per
  unit, `Round 90.k: …`; all seven suites and the module's tests green; a loopback test sets `ControlService.TrustLoopback = false` in
  `try`/`finally`; STATE's JSON shape is unchanged (only when it is sent changes). Papers: PLAN §108 with what the round did not do,
  REVIEW (Walk: `unwalked`), CHANGELOG with the evidence class, README ledger, REMOTE (Trust and Show Bus), COMPANION (the module's
  pairing order), OPEN (L47, L50, L55, L56 stay; patterns-playmap-side's L59 closes with 90.2), the tag row for Round 89. The closing
  sentence names the rig: the verb an output shows is the refused unpaired read, read beside its log line and Super Check's Remote row.
- **PlayMap** (`Mapper/CLAUDE.md`, `Mapper/docs/round3/PLAN.md` §4, §5.6–5.7): `docs/SHOWBUS-NODE.md`, `Core/Bus/*`, `Net/Bus/*` and
  `Net/Wire/*` have no round-3 owner ("nothing in that wave edits it", `:2122-2125`), so M1 lands after the round or by the owner's
  leave; M2 is a question for the §9 call log against decision 17, not a commitment; no new verb or STATE block; analyzers apply to
  tests.
- **AGMessage** (no brief): one 0.4.1 CHANGELOG entry with the test count, shared with the other areas' AGMessage units; the new test
  file added to `package.json`'s `test` script.

---

## 10. Wire summary (exact shapes)

- **Hello** (UDP 9928, every 2 s, unauthenticated): adds `inst` (8 hex per start); `proto` 3; name/host ≤ 64; ≤ 32 caps; ≤ 1200 B
  sent, ≥ 2048 B read; sent from a socket bound to the doors' address; broadcast plus an optional loopback copy; silent when the doors
  are on this machine only. Stagewatch's caps add `windows`; `companion`/`osc` only when bound to the network.
- **Hearing**: every address per id, sticky dial, 127.0.0.1 first for a same-machine peer with fallback, conflict on alternating
  `inst` (or `host`), 128 peers, 60 s.
- **§6 context**: 64 KiB UTF-8, 200 items, 400 links (ends ≤ 120), 20 recent, 256 commands, 32 peers; field lengths as §13; the
  trimming order and `context:trimmed`; posting 1–10 s, kept 30 s, 413 over the limit.
- **§7.2 line wires on a token node**: `NONCE` first; open before pairing: `HELLO`, `NONCE`, `AUTH-PROOF`, `PING`, `HELP`, `VERBS` and the
  node's §8-named queries; else `ERR [@id] not paired — this node shows its state only to a paired connection`; `STATE` after `OK
  paired`, stopped by a token change.
- **§10**: NotAfter − NotBefore ≤ 825 days; NotBefore ≥ creation − 24 h; renew at start when < 30 days left or span > 825 days.
- **§12.2–12.3**: view passes counted apart from credentials; the window link's messages as built in 2.17.
- **Stagewatch context item** `bus:hello` (kind `health`, amber) when every hello target fails 3 beats; peer conflict carried amber.
- **Patterns**: a refused unpaired read's log line; Super Check's Remote row words; the module's status words.
- No new verb, endpoint, Companion action or frame field in this area.

---

## 11. The review's items, answered

| # | item | answer | where |
|---|---|---|---|
| 1 | A loopback hello reaches one app; tests share ports | **Accepted.** Reproduced here (one socket got all five unicasts; broadcast and 127.255.255.255 reached all; loopback multicast reached the joined sockets). Loopback-only nodes stay silent; the loopback copy is a hint nobody relies on; LB-1 measures Windows; tests send to ports only they bind | decisions 5, 6; §2; §5; S6 |
| 2 | §12 contradicts windows-for-peers | **Accepted.** Leases, `windows.lend`, `web.show`, `window.join`, `Authorization: Window`, S5/S7/S8 dropped; §12.4+ and the frame block are windows-for-peers'; it is in the ownership table | decision 9; ownership table |
| 3 | P2 breaks the Patterns module even with a token | **Accepted.** Verified at `main.js:150-164,196-206,215-224,373`. The module asks every read after `OK paired`; a node test where the desk sends `NONCE` first and the Navigator still fills | §4 Patterns 90.2; §5 |
| 4 | P2 breaks Patterns' cross-machine asks and its own tests; NEW TOKEN; ADR | **Accepted.** Verified (`NodesService.cs:149-227`, `ServiceKernel.cs:97-101`, `ControlService.cs:369,763-774`, the six test files). Scopes A/B, `WirePeer` pairing re-checked per push, the tests listed, ADR-016 as the maintainer's call | decision 7; §4 |
| 5 | AGMessage sends a loopback copy its doors do not answer | **Accepted.** Verified (`server.js:44,1003-1005,1245`; `nodes.js:91,95`). Contract rule: send from a socket bound to the doors' address; A1 implements it with a LAN `HOST` test | decision 5; §2 |
| 6 | Loopback-first breaks AGMessage's `find`, pin and desk | **Accepted.** Every address kept per id; `find` matches any of them; dialling is separate from identity | decision 6; A1 |
| 7 | One id, one peer hides cloned ids | **Accepted, refined.** A second address alone is not a conflict (a multi-homed node or a reader on two subnets hears one node twice); alternating `inst` is. All nodes send `inst` | decision 6; §2 |
| 8 | Leases not reconciled; shared lockout | **Leases: moot** (dropped; windows-for-peers sends the whole `grants` list on every link open, `design/windows-for-peers.md:296-303,428-445`). **Lockout: accepted** in part — a right key clearing the count is a deterministic flaw (`WebServer.cs:1083`), so view passes are counted apart (S5). Tombstones: windows-for-peers rejected them for lack of a measured cause (its D15); not reopened here | decision 9; S5 |
| 9 | `window lend` is an escalation path | **Moot for lend.** Its lasting part goes to windows-for-peers as a word S1 asks it to accept: grants only from doors that authenticate the operator, never OSC or an unauthenticated Companion TCP. `all` and ambiguous names it already refuses (`:387`) | decision 9 |
| 10 | `proto` contradicts the exemptions | **Accepted.** `proto` = the revision of shapes spoken; §8 lists what is not kept; readers recognise a token node by `NONCE`. The `?pin=` "2.18 warns" promise is removed: §8 says "not scheduled" | decision 3; §8 |
| 11 | §6 numbers disagree with cross-app-proposals and PlayMap's owner | **Part rejected:** cross-app-proposals already has senders at 512 and readers keeping 200 of `words` (`design/cross-app-proposals.md:180,441`), as this design does. **Accepted:** a reader that keeps commands keeps ≥ 256 (decision 18); M2 is a question to the owner (decision 17) | decisions 4, 7 |
| 12 | Limits cannot all be met; only items trim | **Accepted.** The byte limit binds first; §6 gives the trimming order and counts; measured: 38,142 B compact, about 47.5 KB at 256 commands | decision 4; §6 |
| 13 | Stagewatch's reader would break §6 at S9; tests check few numbers | **Accepted.** Each tree's test checks every §13 number it implements; S8 (final) waits for cross-app-proposals SW-1 and windows-for-peers SW-W1; names read at 64 in S2, A1; 128 peers in PT-90.5, A1, M1. **Corrected beyond the review:** items 200 and links 400, since Patterns writes 200 (`ShowContext.cs:68,180`) | decision 4; S8 |
| 14 | Certificates already issued stay | **Accepted.** Renewal also when the span is over 825 days, in all three; tests for 826 and 1,826 days | decision 8 |
| 15 | The hash fence is oversold | **Accepted.** The header and decision 10 say what each half catches; node-declared caps; body bytes defined exactly (no BOM, LF, one final LF) | decision 10 |
| 16 | P2's consequences in Stagewatch and Patterns' doctrines | **Accepted.** PT-90.2 waits for playout-links' `PlayoutLink` (amber "not paired", reads after pairing, `design/playout-links.md:101-112,311`); Patterns' Super Check Remote row and a log line are the closing evidence | decision 7; §9 |
| 17 | §12.3 against §12.4 | **Moot** (leases dropped). Where a pass may ride is rule 5's carrier exemption, windows-for-peers' D0 | §1 rule 5 |
| 18 | S7 and §12.6 over-built | **Accepted.** Dropped | decision 9 |
| 19 | `bus:hello` would stay lit with Wi-Fi off | **Accepted.** Counted per target; amber only when every target fails; the line says "this PC only" without promising who hears it | §3 |
| 20 | `presenting` against rule 4 | **Accepted.** Named in rule 4 and §2 as the one show fact a hello carries | decision 13 |
| 21 | Ownership table incomplete; round numbers collide | **Accepted.** The table lists windows-for-peers, agmessage, ecosystem-lane and patterns-playmap-side; Patterns' units are Round 90.2, 90.5 and 90.4 in patterns-playmap-side's numbering | ownership table; §6 |
| missing | Windows lab line; e2e on private ports | LB-1 (S6) and §5's port rules | §5 |
| missing | Duplicate ids | decision 6 | §2 |
| missing | Lease reconciliation, tombstones, separate counts | windows-for-peers' `grants` list; S5 | decision 9 |
| missing | Inventory of Patterns' cross-machine asks; NAV after pairing; WirePeer pairing | PT-90.2 | §4 |
| missing | Stagewatch's token-less link amber before P2/M2 | playout-links' unit, a stated dependency | decision 7 |
| missing | Renewal of over-long certificates | decision 8 | §10 |
| missing | 128 peers and 64-character names everywhere | S2, PT-90.5, A1, M1 | §6 |
| missing | Loopback copy only from reachable doors | decision 5 | §2 |
| missing | Byte trimming order; PlayMap measured | decision 4 | §6 |
| missing | `?pin=` warning unit | removed from §8's text instead ("not scheduled") | §8 |
| missing | ADR; question to PlayMap's owner | ADR-016 in PT-90.2; M2 as a question | decision 7 |
| missing | Lend transports, `all`, approval card | windows-for-peers' grant; the transport word asked of it | decision 9 |
| missing | Reconciliation with windows-for-peers and cross-app-proposals before S1 pins | S1's dependency "the siblings' words agreed"; S8 waits for their units | §6 |
