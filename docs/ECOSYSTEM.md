# The show network: Stagewatch, Patterns, PlayMap, AGMessage — and how it leads

**Short answer: yes.** The apps already share a stance no competitor combines. Each runs offline on the venue's own
network, each is built the way real-time games are built, and each keeps AI behind a human's approval. This page began
with three; PlayMap, a media player and projection-mapping node, joined as the fourth. Each speaks its own dialect:

| App | Protocol |
|---|---|
| Stagewatch | WebSocket JSON |
| Patterns | TCP line protocol |
| PlayMap | TCP line protocol in Patterns' grammar (9941), HTTP and a WebSocket line channel |
| AGMessage | Socket.io |

Each also has its own discovery and its own auth. Give them one small shared contract and one place where AI and
people decide together, and they become a show-control platform rather than four good apps.

## 1. What each node owns (one authority per domain)

| Node | Authority for | Already has |
|---|---|---|
| **Stagewatch** | Time: the show clock, running order, cues, curfews, the confidence monitor | Latest-wins snapshots, sequence + heartbeat, LTC/NTP, undo, recovery simulation, copilot with approvals, MCP, scoped API keys |
| **Patterns** | Picture: what's on air, video playout, outputs, the twin standby | libVLC playout, a TCP wire with pushed `STATE`, a beacon, a twin failover |
| **PlayMap** | Mapping: media on mapped surfaces, its playback and its outputs | A render node with FFmpeg decode, structured-light scanning, a line wire in Patterns' grammar, a hello and a node link to Stagewatch |
| **AGMessage** | People: the private channel between the stage and the backroom, documents, decisions | Per-room op log with seq + delta resume, signed identities, role invites, AI drafts |
| *Micro-apps* | One job each (presenter PC slides, cue lights, …) | Stagewatch's node conventions |

The rule, borrowed from game servers: **one authority per domain, everyone else subscribes.** No node keeps its own
copy of another's truth. AGMessage shows Stagewatch's clock; Stagewatch shows Patterns' clip time. None of them
re-time anything themselves.

## 2. Show Bus v1: the one contract every node speaks

*Now specified exactly in `docs/SHOWBUS.md` (Show Bus 1.3, final 2026-10-26, which names PlayMap as the `playmap` kind).
Each app carries it in a release of its own: Stagewatch 2.19 carries 1.3; Patterns, PlayMap and AGMessage each take their
copy of 1.3 in a release of theirs, and until then Patterns and AGMessage keep their copies of 1.2 and PlayMap implements 1.2
(its `docs/SHOWBUS-NODE.md`). The contract's header names the trees whose copies have landed. Show Bus 1.1 (2026-10-10) was
first implemented by Stagewatch 2.15, Patterns round 87 and AGMessage 0.3; 1.2 (2026-10-11) by Stagewatch 2.16, Patterns
round 88 and AGMessage 0.4. This section is the original plan.*

It's small enough to adopt in a day per node, and every piece already exists somewhere in the three apps:

1. **Discovery:** one hello on UDP 9928, `{"t":"<kind>","v":1,"id","name","machine","web","caps":[…]}`, every 2 s.
   - Stagewatch already sends it, and AGMessage now does (`t:"messenger"`).
   - Patterns keeps its own 9700 beacon for twin failover and adds a 9928 hello for the rest of the network.
2. **Manifest** *(superseded: no node serves one)*: `GET /.well-known/show-node.json`. It was to list what the node
   offers (state streams, commands, and the risk class of each command), its versions, and which key scope each route
   needs. Show Bus carries each of those elsewhere: what a node offers in its hello's `caps` and `ports` (§2), its commands
   and their risk in its context (§6), its scopes in 13.5.3 and its node row (§8), its version in its hello's `proto`.
3. **State:** whole-state frames, latest wins. Each frame carries `seq`, durations rather than wall-clock times, and
   a heartbeat at least every 2 s; `live:false` means "I'm up but my show has stopped". Silence means stale, never
   "fine". Stagewatch and AGMessage already follow this, and Patterns' `STATE` almost does (it needs `seq` and
   fractional seconds).
4. **Commands:** each node keeps its own grammar but publishes it in the manifest with a **risk class**:
   - **low:** nobody sees it.
   - **review:** changes timing or words.
   - **high:** changes what the room sees.

   The classes are decided by code, never by a model. Stagewatch's copilot already works this way.
5. **Auth:** one model everywhere. A scoped key (`read` / `control` / `mcp`, and `node` for another show app, which
   speaks for one node id) is sent as `Bearer`. Only a hash is stored, keys can be revoked, every reply carries
   rate-limit headers, and wrong PINs, viewing codes and window addresses count toward a lockout; unknown or revoked
   keys are refused and never counted (2.18).
   Stagewatch has this now; AGMessage and Patterns adopt the same shape.
6. **Journal:** every node appends what happened, and who decided, to its own log with the shared `seq`/time
   fields. One merged journal is then the show report, the evidence of what the AI suggested, and the training
   data.

## 3. The low-latency, can't-fall-over architecture (from games)

Already done in at least one node, and worth making universal:

| Pattern | What it does |
|---|---|
| Single-writer loop with immutable snapshots | No locks on the hot path; readers never block the show (Stagewatch). |
| Sequence numbers, deltas while they cover the gap, a full snapshot otherwise | A phone that drops for a second catches up in one packet (AGMessage op log, Stagewatch frames). |
| Client-side prediction | Every screen counts down locally from a duration and corrects on the next frame, so clocks never depend on the device's time. |
| Interest management | A screen gets only the frame detail its role may see (Stagewatch's public, detail and operator frames). |
| Append-only logs and replay | Crash-proof persistence; the same log replays a show for the report or a what-if. |
| Supervisors and hot standby | AGMessage's launcher restarts it within 2 s, Patterns has its twin, Stagewatch has main plus backup licences. |

**Next:**
- **One heartbeat dashboard.** Every node on the bus shows its health on one Stagewatch screen, and a red node says why.
- **Failover across the ecosystem.** The backup Stagewatch takes over the clock when the main one goes silent, as
  Patterns' twin does for the picture.
- **Chaos rehearsal.** A "rehearse failure" mode drops links, kills a node and skews a clock during the tech check,
  so the crew sees each fail-safe work before the doors open.

## 4. The show brain: AI everywhere, depended on nowhere

The principle is that **everything works without AI; AI makes it faster.** It never holds the authority; it
proposes into one approval queue.

- **One approval queue across nodes.** Say "we're 6 minutes late, protect the keynote". The answer becomes a
  cross-node plan, and the operator ticks each step:
  - Stagewatch: trim the panel by 4 minutes.
  - Patterns: skip the sizzle reel's last minute.
  - AGMessage: tell the board.

  The steps run in order with one undo, and each shows its simulated consequence (Stagewatch's recovery simulator)
  before anything runs.
- **Context, not commands.** Each node publishes what the AI may read through its manifest and MCP. The brain
  reasons over the merged state and journals, never over a screen-scrape.
- **Explain the problem across the network.** "Why is the VT countdown blank?" can be answered by checking every
  link: whether Patterns is up, whether the clip loops, whether a key was revoked.
- **Learn from your own shows.** Overrun history (Stagewatch), what the board asked (AGMessage) and what played late
  (Patterns) feed the plan review before the next event.
- **Voice and drafts at the edges.** These already exist:
  - push-to-talk copilot (Stagewatch);
  - draft answers from the open document (AGMessage);
  - layouts from a sentence (Stagewatch designer);
  - message ideas (Stagewatch).
- **Not dependent:**
  - Offline, every AI button greys out with a reason and the show runs exactly as before.
  - A local model can take the advisor role for venues with no internet.
  - Risk classes and approvals are code paths, so a model can never bypass them.

## 5. Workflow: one show, not three set-ups

- **One show file** that references each node's part, opened together. Patterns' cues, Stagewatch's running order
  and AGMessage's rooms are created from one agenda (Stagewatch already drafts running orders from any agenda text).
- **One setup pass:**
  1. Discovery lists every node.
  2. "Link all" exchanges scoped keys with one approval per node.
  3. A **preflight** checks clocks, links, keys and screens across the network, and turns green only when the whole
     system is ready.
- **One control surface:** a single Companion module and Stream Deck profile drives all nodes through the bus.
  Stagewatch's module is the template.
- **One report:** the merged journal becomes the client's show report: timings, what played, what the board asked,
  what the AI suggested and who approved it.

## 6. Why this leads

| | Stagetimer / Ontime | QLab / Disguise | This network |
|---|---|---|---|
| Works offline on the venue LAN | partly | yes | **yes, every node** |
| Time, picture and people in one system | time only | picture/cues | **all three** |
| AI that proposes, simulates and waits for approval | basic | no | **yes, across nodes** |
| Open protocol, scoped keys, MCP | partial | proprietary | **yes** |
| Game-grade resilience (snapshots, deltas, replay, standby) | partial | partial | **yes** |

## 7. Roadmap

1. **Now (done this round)**
   - Stagewatch 2.11 reads Patterns' clip and can skip to the last 10 s.
   - AGMessage 0.2 is hardened, has an op log with resume and AI drafts, and links to Stagewatch.
2. **Next**
   - ~~Show Bus v1 manifest on all three apps~~ (superseded: Show Bus 1.1 to 1.3 carry what it was for, §2 above).
   - Patterns adds `seq`, fractional seconds, `VIDEO PLAY/PAUSE/SEEK` and a 9928 hello.
   - AGMessage moves to scoped API keys for its own integrations.
3. **Then**
   - The cross-node approval queue in Stagewatch's copilot (MCP clients for Patterns and AGMessage).
   - Merged journals and report.
   - One preflight.
4. **Later**
   - Ecosystem failover.
   - Chaos rehearsal.
   - Local-model advisor.
   - One Companion module for the whole network.

## Honest risks

- **Three stacks across four apps** (.NET/WPF for Stagewatch, .NET/Avalonia for Patterns and PlayMap, Node for
  AGMessage). Keep the bus spec and its conformance tests independent of all of them, and give each node a tiny adapter:
  Show Bus 1.3 does this with vectors in its own text and one scripted peer that every tree runs against itself (§13.20).
- **Security drift.** Auth is only as strong as the weakest node. Use the same key model and lockouts everywhere,
  and send nothing secret on discovery.
- **Code signing** is still deferred. SmartScreen warnings will cost trust at venues, so sign before a wider
  release.
- **AI cost and latency** on venue Wi-Fi. Every AI path needs a timeout, a cancel and a non-AI fallback; they
  already do.
