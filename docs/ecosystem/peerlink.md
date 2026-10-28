# PeerLink — one profile for layer 2 of the Show Bus (the synthesis)

Area: peerlink, synthesis of the three drafts (security first `design/peerlink-security.md`, reliability first
`design/peerlink-profile.md`, adoption first `design/peerlink-adoption.md`) under the two judges' scores. Design only: no repo was
edited or built. Line numbers are from the trees as read on 2026-10-25 (Stagewatch `855e8b6`, Patterns `bcdbf28`, Mapper `f647094`);
`AGMessage@0.5.0` is `AGMessage's 0.5.0 branch` at `7b97cca`, read with `git show`. Every vector below was computed with
Python's stdlib and cross-checked in Node 22 in this session; the security draft's vectors were recomputed and match.

---

## 0. The answer to the question

**Are Show Bus and PeerLink the same thing?** No. The Show Bus is the *contract*: what nodes say to each other — the hello, commands
and receipts, state frames, context, the approval desk, windows. A PeerLink is *one authenticated link between two apps* that carries
that contract. The Show Bus says "a command carries an id and gets a receipt"; PeerLink says how the two ends proved who they are, kept
the link alive, measured the clock, bounded the queues and redialled — the same way in every app.

**Are both needed?** Yes. Without the contract, two linked apps have nothing in common to say. Without one link standard, every app
pair reinvents layer 2, and the code shows it: eight implementations, each with its own faults (section 2). The pairing token goes plain
off the machine from Stagewatch. A key rides plain `ws://` from Patterns, PlayMap and AGMessage whenever a hello names no TLS. A pong
hides a stuck show. A drop reports a command that may have run as failed.

**Different things, on separate layers?** Yes, four layers, each with one owner:

| layer | what it owns | what it never does |
|---|---|---|
| 1. Discovery (§2) | the hello on UDP 9928; hearing, staleness, one id one peer, conflicts | trust: a hello may raise a link's guard, never lower it |
| 2. PeerLink (§13, this design) | carriers, identity, keys, the LINK handshake, TLS and pins, floors, liveness of the link and the show, the clock and its quality, request ids, the receipt envelope and outcomes, order, bounds, redial, holds, the link's state and its record | interpret a message (it treats layer 3 as opaque except its size, ordering class, id and the receipt envelope) |
| 3. Show Bus messages (§3–§6, §11, §12) | verbs and their risk, receipt `effect`/`refs`, frames and their counters, contexts, proposals, windows | authenticate, deliver or bound anything itself; it never sees a secret, because layer 2 carried none |
| 4. Views | Patterns' Eye, the desks' node pages, the Run strip, right-click menus, STATE, Companion variables, the assistant's facts, preflight, the journal | make a trust decision or invent a light: they read layer 2's link record and layer 3's content |

The Eye is layer 4's chief reader. It draws every app as a node and every link as an edge carrying the link record: green ✓ verified,
amber △ degraded, stale or legacy, red ✕ refused or down when the show needs it. It also draws the links Patterns does not hold, one hop
out, from the `link:` items and `links` rows in the other apps' contexts.

---

## 1. How the synthesis was made

The two judges disagreed on the base. Judge 1 picked reliability first; judge 2 picked adoption first. By total score adoption is
highest (5/7/9/8 and 6/8/9/8 = 60), reliability next (55), security last (52), though security is strongest on its own axis by both.

So this design **starts from the adoption draft's frame**:

- it keeps today's bytes (HELLO, NONCE, AUTH-PROOF, PING/PONG, `OK/ERR @id`) untouched at level 0;
- unilateral rules apply first, with no ordering between apps;
- one answer→outcome mapping table;
- a timing number is offered only once measured;
- each repo adopts in its own process.

Its identity section is **replaced wholesale** by the security draft's LINK transcript. Both judges ask for that:

- judge 1: "replace that wholesale";
- judge 2: "one transcript for every new proof… AUTH-PROOF stays byte-identical as level 0".

The reliability draft's liveness, self-gap, clock quality, receipt store and safety-on-loss rules are grafted in. Each graft and each
rejection was checked against the code; the table names where.

| item | from | judge | kept, changed or dropped, and the code that decided it |
|---|---|---|---|
| bytes of level 0 unchanged; unilateral rules first | adoption | 2 best; 1 (migration) | kept. Readers HMAC everything after `NONCE ` (`Stagewatch/src/Timer.Integrations/PatternsLink.cs:309`, `Mapper/src/PlayMap.Net/Links/WireClient.cs:314`), and pong parsers need exactly four words (`Patterns/src/Patterns.Core/Services/ShowBusNetwork.cs:302`, `WireClient.cs:384`) |
| LINK: one transcript, mutual proof bound to the certificate each end saw, offer/speaks inside it, connector first | security | 1 and 2 graft | kept, on `line` and `ws` only. It is the one new proof, so PROVE and PEERLINK are dropped. It closes PlayMap following a blackout from whoever answers: Patterns greets with STATE before NONCE (`Patterns/src/Patterns.App/Services/ControlService.cs:356-363`) and PlayMap acts on it unpaired (`Mapper/src/PlayMap.Net/Links/PatternsLink.cs:218-247`) |
| per-request HTTP MAC, 64-wide counter window | security | 1 and 2 reject | dropped. Side calls carry a session credential S derived in the handshake, sent only to the pinned certificate (judge 1's alternative); no key travels |
| `http` carrier (`/api/link`, `hid`) | security | 2 reject | dropped. AGMessage moves onto the `line` carrier its acceptors already serve |
| `twin` carrier | security | 1 and 2 reject | dropped. The twin is a named exemption; only its bind address and secrets switch are fixed now |
| resend the same id after a drop | security | 1 reject | dropped. A `RECEIPTS` ask reads the acceptor's receipt store and never runs anything. Judge 2's "a second mechanism" objection is met: dedupe and RECEIPTS are two doors to one store |
| enrolment code→link key | security | 1 graft, 2 "not now" | kept as a MAY, only over TLS (feature `enrol`). Issued scoped keys are the MUST, because a 160-bit key can be proved over plain 9697 today with no guessable target, while a code proof cannot |
| scoped, id-bound, revocable keys; wrong proofs in their own table | security | 1 and 2 graft | kept. Stagewatch counts unknown keys in the PIN's lockout today (`Stagewatch/src/Timer.Remote/WebServer.cs:1077-1082`) |
| floors per node id | security | 1 and 2 graft | kept. A floor refusal keeps the public view (judge 1); floors come only from verified links, never from a hello (judge 1's "all three" reject) |
| a hello can refuse or freeze a link | all three | 1 reject | dropped. For a pinned id the pin alone decides; a contradicting hello is a note |
| two livenesses, a pong never clears stuck | reliability | 1 graft; adoption agrees | kept. Patterns refreshes on any message and ignores the beat (`StagewatchLinkService.cs:521-538`); PlayMap clears stuck on a plain beat (`Mapper/src/PlayMap.Net/Links/StagewatchLink.cs:381`) |
| self-gap, clock errMs/fresh/fit, `\|offset\| − err > 2 s`, safety facts held with their age, lateMs, reports owed retried | reliability | 1 and 2 graft | kept |
| expireMs enforced by default | reliability | 1 and 2 reject | dropped as a default. Feature `expire` is offered only once measured; `lateMs` is always reported |
| placeholder stuckMs for Patterns | reliability | 2 reject | dropped. `stuck` is offered on the line wire only once measured (adoption) |
| `observed` as a link outcome; ping `live\|stuck` both ways; a new integer redial formula | reliability | 2 reject | dropped. `observed` is a reader's "seen" row. Redial keeps the three apps' `base × (0.8 + 0.4j)` (`ShowBusNetwork.cs:31-37`, `Mapper/src/PlayMap.Core/Bus/ShowBus.cs:232-236`, `AGMessage@0.5.0 app/lib/nodes.js:203`) |
| answer→outcome mapping table; the AGMessage/Patterns receipt defect | adoption | 1 and 2 graft | kept. The defect is confirmed: `AGMessage@0.5.0 app/lib/peerlinks.js:62-71` reads Patterns' `{ok,msg}` (`ControlService.cs:1365-1368`) as accepted |
| AG-PL0: no token header off the machine without pinned TLS | adoption | 1 and 2 graft | kept, tightened: the token goes only to a certificate already pinned or confirmed by a person, and the direct command path stays off while the only credential is the operator's token (agmessage D10; judge 1) |
| one token canon, refused where typed if not `[A-Z0-9]`; prove only 32-hex nonces | adoption | 1 graft | kept. Patterns strips five characters (`Patterns/src/Patterns.Core/Services/PairingToken.cs:54-63`), PlayMap any `char.IsWhiteSpace` (`Mapper/src/PlayMap.Core/Bus/Pairing.cs:39-48`); Stagewatch proves any nonce of 1–128 characters (`PatternsLink.cs:311`) |
| level 0 never refused; downgrade only degrades | adoption | 1 reject | changed. Level 0 stays for never-verified ids; once an id has verified, floors refuse less |
| plain off-machine line link is grey information | adoption | 2 reject | changed to amber `plain`; grey belongs to loopback |
| CLOCKS APART without an error bound; samples up to 20 s old | adoption | 1 reject | changed to reliability's |
| two interpreters (C# and JS) | adoption, reliability | 1 and 2 reject | dropped. Pure and machine rows sit in the spec's block, read by each tree's own tests, with one Node scripted peer for socket-level scenarios |
| vectors inside the hashed spec body | security | 2 graft | kept |
| section number | reliability, adoption, security note | 2: "use §15" | **not taken, with a reason**: the task names §13. No landed text refers to Show Bus §12–§14 (`Stagewatch/docs/SHOWBUS.md` ends at §11; a grep of the four trees finds none), so moving spec-1-3's unlanded numbers block to §14 costs 23 references in five unlanded designs, which spec-1-3's single master edit owns. If the master's owner prefers no rename, this text drops in as §14 unchanged except its number |

---

## 2. What the code shows (evidence)

### 2.1 The links today

| # | link | carrier | proof | in clear on the wire | liveness / redial | receipts | evidence |
|---|---|---|---|---|---|---|---|
| 1 | Stagewatch `PatternsLink` → Patterns 9697 / PlayMap 9941 | plain TCP | 1.1 AUTH-PROOF, one-sided | plain `AUTH <token>` to any host 1.5 s after no NONCE; STATE taken before pairing | ping 2 s, no stale, drop 12 s; 250 ms doubling, **no jitter**, reset on every connect | ids `s<n>` per run; pending evicted past 64; a drop fails all | `Stagewatch/src/Timer.Integrations/PatternsLink.cs:43-47,167-169,189-230,280-284,311,318-327,357-364` |
| 2 | Stagewatch `/ws` + HTTP (acceptor) | TLS or plain on 9924, sniffed | bearer `swk_` or `?pin=` | the bearer key on plain from the LAN unless Require HTTPS (off by default) | beat 2 s; stuck beat after 3 s; pong off the show thread, t2 = t3; 64 slots, an extra closed without a word | a late command runs whenever the dispatcher reaches it | `Stagewatch/src/Timer.Remote/WebServer.cs:26-30,278-281,300-331,1059,1150-1164,1262-1297,1349-1355`; `Stagewatch/src/Timer.Core/AppSettings.cs:259-261`; `Stagewatch/src/Timer.Core/ShowState.cs:217`; `Stagewatch/src/Timer.App/ViewModels/MainViewModel.Receipts.cs:21-27` |
| 3 | Patterns `StagewatchLinkService` → Stagewatch | `wss` only when the hello names TLS; a typed address only when a pin is held | bearer node key | the key on plain `ws`/`http` when the hello is missing, forged or without `tls` | 2/6/12 s, ±20 %; any message (a pong too) refreshes; stuck ignored; attempt reset on any normal end; ping waits behind 4 s side calls | result POST once; `_ranDeskIds` cleared at 1000 | `Patterns/src/Patterns.App/Services/StagewatchLinkService.cs:184-185,225-243,269-276,284,289,324-355,460-465,521-538` |
| 4 | Patterns wire 9697 + HTTP (acceptor) | plain TCP; HTTP sniffs TLS | AUTH-PROOF; HTTP takes `X-Patterns-Token` (the token itself) | STATE greeting before NONCE; queries answered unpaired | pong on the connection task | ids echoed, no dedupe; a proof trusts the address for OSC 12 h | `ControlService.cs:356-363,407-411,428,439,443-457,501,548-560,917-945,978-981,1333-1368`; `Patterns/docs/OPEN.md` L55 |
| 5 | Patterns twin and node relays | plain TCP on every interface | mutual HMAC over both nonces | the show; device passwords by default (`SendSecrets` true) | beat 1 s, silent 5 s, dial every 1 s | a forwarded ACT answers "Requested … sent" and never hears back | `Patterns/src/Patterns.App/Services/TwinService.cs:488,512`; `Patterns/src/Patterns.Core/Services/Twin.cs:162-194,483-509`; `Patterns/src/Patterns.Core/Model/ShowState.cs:2080,2123`; `Patterns/src/Patterns.App/Services/TwinService.Followers.cs:47-55` |
| 6 | PlayMap `StagewatchLink` → Stagewatch | `wss` only when the hello names `tls` and `https`; a typed address always plain | bearer key | the key on plain | injected clock; stale/drop; ±20 %; a plain beat clears stuck; attempt reset on normal return; side calls inside the beat loop | `_ran` cleared at 1000; result POST once | `Mapper/src/PlayMap.Net/Links/StagewatchLink.cs:63-80,236-237,278-302,380-382,420,438-475,538-552`; `Mapper/src/PlayMap.Engine/Host/NetworkHost.cs:718-735` |
| 7 | PlayMap `PatternsLink`/`WireClient` → Patterns | plain TCP | AUTH-PROOF; plain AUTH only to loopback | STATE followed (blackout) before pairing | 2/6/12 s injected; reset when the peer "said anything" | ids `pm<n>` per run; no answer in 10 s or a drop = failure | `Mapper/src/PlayMap.Net/Links/WireClient.cs:145-170,247,256-320,381-391`; `Mapper/src/PlayMap.Net/Links/PatternsLink.cs:218-247`; `NetworkHost.cs:696-714` (a blackout held across a drop, released on stop) |
| 8 | PlayMap 9941 and `/ws` `playmap.lines.v1` (acceptor) | TCP plain; WS over the web port's TLS | AUTH-PROOF; trusted addresses need none | TCP pushes STATE on HELLO before pairing (the owner's decision 17) | `LineWriter` ≤ 256 replies, 10 s writes, latest-wins pushes, t3 at write | stage accepted/applied | `Mapper/src/PlayMap.Net/Wire/WireSession.cs:27,180-181,229-233,263-276,306-315,337-369`; `Mapper/src/PlayMap.Net/Common/ConnectionLedger.cs:58-66`; `Mapper/src/PlayMap.Core/Bus/Pairing.cs:110-123`; `Mapper/docs/wire/companion.md:6-23` |
| 9 | AGMessage `StagewatchLink` → Stagewatch | `wss` when the URL, the hello or a typed fingerprint says so; else plain | bearer key | the key on plain | 1 s ping, 6/12 s on `Date.now()`; delay reset on open; a pong refreshes while stuck | ids `ag<12 hex>`; a timeout after the POST reads "not sent"; pin confirmed on any state frame | `AGMessage@0.5.0 app/lib/nodes.js:30-32,58,201-203,227,251-268,306,323-336,356-365,383-389` |
| 10 | AGMessage `PeerLink` → Patterns / PlayMap (new in 0.5.0) | HTTP polling every 5 s | none — the token itself | Patterns'/PlayMap's **full-control pairing token** in `X-Patterns-Token` / `X-PlayMap-Token` over plain HTTP when no TLS is named | stale 12 s, no ping, no clock | misreads every Patterns answer | `AGMessage@0.5.0 app/lib/peerlinks.js:19-23,62-71,108-153` |

What already works and is kept as the model:

- PlayMap's `WireClient`: plain AUTH only to loopback, one proof per connection, nothing queued (`WireClient.cs:149-152,280-316`).
- PlayMap's line channel: pairing words only until paired (`WireSession.cs:27,229-233`).
- The twin's mutual proof (`Twin.cs:162-194`).
- Stagewatch's keys: 160 random bits, stored as SHA-256 (`Stagewatch/src/Timer.Core/ApiKeys.cs:49-60,108`).
- The stuck beat (`WebServer.cs:1265-1271,1297`) and Companion's `PONG … STUCK` (`Stagewatch/src/Timer.Companion/CompanionServer.cs:389-399`).
- Every node's TLS sniff on its HTTP port (`WebServer.cs:300-318`; `ControlService.cs:917-945`).

### 2.2 Defects, each now a rule and a kit row

| D | defect | evidence | rule |
|---|---|---|---|
| D1 | The hello chooses TLS or plain, so a missing or forged hello sends the key in clear | `StagewatchLinkService.cs:238-242,269-276`; `StagewatchLink.cs:66`; `nodes.js:255,326`; `peerlinks.js:111` | 13.3.2, 13.4.2 |
| D2 | Secrets in clear: plain `AUTH` off the machine; the pairing token in an HTTP header; twin secrets | `PatternsLink.cs:280-284`; `peerlinks.js:23,121`; `ShowState.cs:2080` | 13.6.8, 13.6.9, 13.18, 13.19 |
| D3 | The 1.1 proof is one-sided and unbound: relayable, harvestable, and the acceptor never proves itself | `ShowBusNetwork.cs:317-339`; `Pairing.cs:54-59`; `PatternsLink.cs:379-384` | 13.6 |
| D4 | Facts followed from whoever answers: STATE taken before pairing | `PatternsLink.cs (Mapper):218-247`; `PatternsLink.cs (Stagewatch):357-364`; `ControlService.cs:356-363` | 13.6.3 |
| D5 | One token opens every verb; no per-peer key, scope or revocation; a 59-bit token in a free text box, saved with the show | `PairingToken.cs:11-22`; `Patterns/src/Patterns.App/Views/Sections/WebSection.axaml:38` | 13.5 |
| D6 | An address used as a credential: 12 h OSC trust after a wire proof; trusted addresses pair without proof | `ControlService.cs:439,556-560,978-981`; `Pairing.cs:113-115` | 13.18 |
| D7 | Unknown keys counted in the PIN's lockout | `WebServer.cs:1077-1082` | 13.5.6 |
| D8 | A pong keeps a stuck show green | `StagewatchLinkService.cs:521-522`; `nodes.js:333-334`; `StagewatchLink.cs:381` | 13.10.5 |
| D9 | Backoff resets on a bare connect; no jitter at Stagewatch | `PatternsLink.cs:189,209,229-230`; `StagewatchLinkService.cs:184-185`; `StagewatchLink.cs:236-237`; `nodes.js:329`; `WireClient.cs:247` | 13.15 |
| D10 | "No answer" reported as a failure; pending evicted; ids restart every run | `WireClient.cs:160-165`; `PatternsLink.cs:167-169,225-226`; `nodes.js:387-389`; `peerlinks.js:151-152` | 13.12 |
| D11 | Any `OK PONG` line is a clock sample; four admission rules; `>=` against `>` at 2 s | `PatternsLink.cs:318-327`; `Stagewatch/src/Timer.Core/Resilience.cs:255-256`; `Patterns/src/Patterns.Core/Services/LinkClock.cs:59-63,97`; `AGMessage@0.5.0 app/lib/resilience.js:165-167` | 13.10.2, 13.11 |
| D12 | The beat waits behind HTTP side calls (up to 3 × 4 s); Stagewatch writes Patterns commands from its show thread | `StagewatchLinkService.cs:324-355`; `StagewatchLink.cs:438-475`; `PatternsLink.cs:172-176` via `Stagewatch/src/Timer.App/ViewModels/MainViewModel.Patterns.cs:69-85` | 13.7.2, 13.14 |
| D13 | Liveness boundary: 6000 ms live in two apps, stale in two | `ShowBusNetwork.cs:40`; `Stagewatch/src/Timer.Core/ShowBus.cs:33` (`<=`); `ShowBus.cs (Mapper):226-229`; `nodes.js:58` (`<`) | 13.10.3 |
| D14 | Token canon differs (a pasted NBSP pairs on PlayMap and fails on Patterns); any NONCE proved | `PairingToken.cs:54-63`; `Pairing.cs:39-48`; `PatternsLink.cs:311,381` | 13.5.1, 13.6.8 |
| D15 | Reports owed sent once; "ran" sets cleared whole | `StagewatchLinkService.cs:460-465`; `StagewatchLink.cs:538-552` | 13.12.9 |
| D16 | A late command runs silently | `MainViewModel.Receipts.cs:21-27` | 13.12.5 |
| D17 | AGMessage misreads Patterns' `/api/cmd` answers, and its fake Patterns hides it | `peerlinks.js:62-71,145-153`; `ControlService.cs:1337,1355,1365-1368`; `AGMessage@0.5.0 app/test/helpers.js:122,137-138` (as the adoption draft read it) | 13.12.3 |
| D18 | Every app's tests use their own fakes of the peer | `Stagewatch/tests/Stagewatch.Tests/Program.cs:1276-1318`; `Mapper/tests/PlayMap.Net.Tests/LinkTests.cs:17`; `StagewatchLinkTests.cs:20` | 13.20 |

Measured in the reliability area (`design/peerlink-stampede.py`, output beside it): 32 links redialling one acceptor that is back after
6 s.

- Today's ±20 % puts at most 9 connects (median; p95 12) in any 250 ms.
- Stagewatch's link, with no jitter, puts all 32 in one window.
- Wider jitter brings every link back about 2 s later.

So ±20 % stays, and jitter is added where it is missing.

---

## 3. Decisions

1. **One handshake, LINK, on two carriers; level 0 is today's bytes.**
   - LINK runs on the `line` carrier (Patterns 9697, PlayMap 9941, PlayMap's `/ws` `playmap.lines.v1`) and on the `ws` carrier
     (Stagewatch's node link).
   - It carries one transcript — version, carrier, both `t:id`, the key id, both nonces, and the certificate each end saw — with mutual
     proofs, the connector first.
   - An acceptor that does not speak LINK gets 1.1's AUTH-PROOF unchanged (`line`), or a bearer key only to a confirmed certificate
     (`ws`).
   - *Why:* one mechanism replaces three:
     - the one-sided 1.1 proof;
     - the adoption draft's PROVE (proof after pairing);
     - its PEERLINK verb (negotiation after pairing, outside any proof).
   - Offer and speaks are inside the proved transcript, so a middle machine cannot downgrade the version silently.
   - *Alternatives:* PROVE + PEERLINK, which leave the negotiation unauthenticated (judges 1 and 2); mutual TLS (browsers share these
     ports, and the certificates carry `serverAuth` only); a PAKE (neither .NET nor Node ships one, and hand-rolled curve code in two
     languages is the worse risk).
2. **No key or code is ever sent between apps; side calls carry a derived session credential.**
   - S = HMAC(K, "session\n" ‖ T) rides `Authorization: ShowBus lid=…, s=…`, only to the pinned certificate or to loopback, and dies
     with the link.
   - *Why:* judges 1 and 2 both reject the per-request MAC (it breaks when a client re-escapes a URL — AGMessage puts message text in its
     path, `nodes.js:380` — and a mismatch holds every side call for 60 s).
   - Judge 2's alternative, the long-term key inside pinned TLS, still puts the key in every request header.
   - S costs one HMAC per link and a lid table at the acceptor, and keeps the rule simple: no key on any wire.
3. **Issued scoped keys are the base credential; the pairing code is the operator's, and enrolment is a MAY over TLS.**
   - Every node issues app keys the way Stagewatch already does: `<prefix>_` + 32 symbols, 160 bits, stored as SHA-256
     (`ApiKeys.cs:49-60,108`). Each key is bound to one connector id, carries one scope from follow/post/drive/operate, and is revocable
     live.
   - *Why:* a 160-bit key can be proved over plain 9697 today with no offline target, while a 59-bit code proof cannot. So issued keys
     work before Patterns' L55 (TLS on the wire) closes, and enrolment waits for it.
   - Enrolment (type the code once over TLS, derive a link key) is the better show-setup experience once TLS exists, so it is defined
     and optional: judge 1 grafts it, judge 2 defers it.
4. **The hello may raise a guard and never lower it, and never refuses.** For a pinned id the pin alone decides. Floors come only from
   verified links. *Why:* D1. Judge 1 also rejects every wording that lets a forged UDP packet freeze a link.
5. **Floors per node id: `{tls, level, proof}`.** An id that once verified is refused when it offers less. The refusal keeps the public
   view, and the operator lowers the floor with one press, journalled. *Why:* without floors a middle machine strips LINK or TLS for
   good.
6. **Two livenesses.** The link is alive when messages arrive; the show is alive when the sender's single writer turns over. A pong
   never clears stuck.
   - On `ws` the existing stuck beat says so.
   - On `line`, a fifth PONG word `STUCK` says so (Companion's precedent, `CompanionServer.cs:389-399`), only on a link that negotiated
     `stuck`.
   - A node offers `stuck` only once its stuckMs is measured.
7. **The alarm system watches itself (self-gap).** A node that did not run for over 2 s declares no peer dead on that run.
8. **Clock with quality.** Pongs are matched to the last 8 pings. Each link publishes errMs, ageMs and samples. CLOCKS APART only when
   |offset| − err > 2 s. `AT` only from a fresh, fit clock measured over TLS or a local link.
9. **Five outcomes and one reading table.**
   - The outcomes are not-sent, refused, accepted, applied, unconfirmed.
   - "Seen" (observed) is a reader's row, not a wire stage.
   - `lateMs` is always reported; expiry only once measured (feature `expire`).
10. **Run-unique ids, a receipt store, RECEIPTS, no blind resends.**
    - Ids are `<prefix><n>-<inst>`.
    - With feature `receipts`, the acceptor keeps the final receipts of each verified connector's last 256 ids for 10 minutes. A repeat
      is answered from the store, never run.
    - After a relink the connector may ask what became of an id. `unknown` from a restarted acceptor stays unconfirmed: running it again
      is a person's act.
11. **Bounds everywhere, nothing in the beat's path, nothing on the show writer.**
12. **One closed set of link states and one link record that every reader takes**, the Eye's edges included.
13. **Safety facts are held with their age**, released only by the peer's live word or a person (PlayMap already holds a blackout,
    `NetworkHost.cs:696-714`).
14. **Doors that are not PeerLinks are named, and a proof grants nothing to an address.**
15. **Conformance is data in the hashed spec body plus one Node scripted peer;** each tree reads the data with its own tests.
16. **Each repo adopts in its own process;** unilateral hygiene first, the hub second, the line acceptors last. The order is section 8.

---

## 4. The master text: §13 PeerLink (normative, for spec-1-3's single master edit)

*Placement note for the master's owner:*

- This text is §13. spec-1-3's machine-read numbers block becomes §14, with this section's rows appended; its existing rows keep their
  names and values.
- Nothing landed refers to §12–§14.
- If §13 is to stay the numbers, this text is §14 with only its number changed.
- The handed-over words for the rest of the master follow §14's rows.

---

## 13. PeerLink — one authenticated link between two nodes (1.3)

Discovery (§2) finds a node and says where its doors are. A **PeerLink** is one connection between two nodes that have proved to each
other who they are, together with the HTTP calls that ride beside it to the same node.

- Commands, receipts, state, context, proposals and windows (§3–§6, §11, §12) travel on it unchanged.
- This section owns only *how* they travel: identity, keys, TLS, liveness, the clock, request ids and the envelope of a receipt,
  order, bounds, redial, and the link's state, which every reader takes from one record (13.16).
- Every app-to-app link in this contract is a PeerLink:
  - Stagewatch's playout links and the node links on its `/ws`;
  - Patterns' Stagewatch link and its Show Bus node devices;
  - PlayMap's Stagewatch and Patterns links;
  - AGMessage's Stagewatch link and its links to Patterns and PlayMap.
- Doors that are not PeerLinks keep their own rules and are named in 13.18.

MUST, MUST NOT, SHOULD and MAY are used as in RFC 2119. The *connector* dials; the *acceptor* answers. Names in `code` are §14 rows.
Times are on the node's monotonic clock unless called wall time.

### 13.1 Words

- **Carrier.**
  - `line`: §3's line grammar — Patterns' wire (TCP 9697), PlayMap's wire (TCP 9941), and PlayMap's `/ws` with subprotocol
    `playmap.lines.v1`, one text frame per line.
  - `ws`: a node link on Stagewatch's `/ws` (§7.1).
  - A **side call** is an HTTP request to the acceptor of a live `ws` link, on that link's scheme, host, port and pin.
- **Local**: the socket's remote address is a loopback address — never because of a host name, the hello's `host`, or an address the
  node owns.
- **Verified**: both proofs of 13.6 checked, each by the other end. A link that is up without them carries the flag `legacy`, `open`
  or `keyless` (13.16.2).
- **Level**: 1 on a link that ran LINK; 0 on a link to an acceptor that does not speak it (13.6.8).
- **Feature**: a word in the verdict's `can` list: `final`, `receipts`, `stuck`, `expire`, `enrol`. An unknown word is ignored.
- **Show writer**: a node's single writer of the show. These are Stagewatch's show thread, Patterns' desk thread, PlayMap's engine
  control lane and AGMessage's event loop.
  - Its **stuckMs** is how long it may go without turning over before the node says its show is stuck.
  - Each node's §8 row names both, with the measurement behind the number.
- **The show needs a link** when the link is configured to be followed, is a playout link, or carries a grant (§12). A link only heard
  never turns red.
- **Link record**: what a connector publishes about each link (13.16.3).

### 13.2 What the venue network can and cannot do

Anything on the show network — the venue's Wi-Fi included, where every client may read every other's traffic — is assumed hostile.

| anyone on the show network can | under this section they cannot |
|---|---|
| hear every hello: names, ids, hosts, ports, caps, fingerprints (public by design) | learn a key, code, PIN or view pass from the wire: none is sent off this machine. The one derived credential, S, rides only inside pinned TLS (13.6, 13.7, 13.19) |
| forge hellos for any id, address or fingerprint | make a link plain, change or confirm a pin, choose the key that is proved, mark a link verified, or refuse a link (13.4.2) |
| answer at a node's address (ARP, a stolen IP, a forged hello) | complete LINK without the key, or relay a proof between two nodes, because proofs are mutual and bound to the certificate each end saw; nor be followed for a fact by a connector that holds a key for that node (13.6.3, 13.6.8) |
| intercept a first contact with its own certificate | have that certificate pinned on a keyed link: the proofs fail and nothing secret was sent (13.8.2) |
| replay anything it recorded | replay a proof (fresh nonces), use S off TLS (refused), or run a command twice (the receipt store, 13.12.6) |
| strip TLS or LINK from a link it sits in | downgrade a node that proved itself before: floors (13.9.3) |
| read and alter a plain `line` link off this machine | use it to carry a secret, enrol or schedule a timed command; the link says it is degraded (13.3.3, 13.11.6) |
| record a code's proof made on a plain carrier | — a recorded code proof is an offline guessing target (about 2^59 for a made Patterns token, trivial for a typed short one); an issued key's proof is not; the link says so (13.5.1, 13.16.2) |
| send wrong guesses from its own or a spoofed address | lock out a PIN, a browser or another key: wrong proofs count per (address, kid) in a table of their own, and unknown kids count nowhere (13.5.6) |
| drop, delay or flood traffic; jam the Wi-Fi; see who talks to whom | — not prevented; every link says what it lost (13.16) |

### 13.3 Carriers

1. Where each node is dialled, and what it dials:

| node | dialled at (acceptor) | dials (connector) |
|---|---|---|
| Stagewatch (`main`) | `ws` and its side calls on the web port (9924), TLS sniffed on the same port | Patterns 9697 (`line`); PlayMap 9941, or `playmap.lines.v1` over TLS on PlayMap's `https` door (`line`) |
| Patterns (`patterns`) | `line` on 9697, TLS sniffed on the same port (13.8.5) | Stagewatch (`ws`); PlayMap 9941 or `playmap.lines.v1` (`line`), as a Show Bus node device |
| PlayMap (`playmap`) | `line` on 9941 (TLS sniffed) and `playmap.lines.v1` on its `https` door | Stagewatch (`ws`); Patterns 9697 (`line`) |
| AGMessage (`messenger`) | — (no node links to it with a key in 1.3) | Stagewatch (`ws`); Patterns and PlayMap (`line`) |

2. **Off this machine a `ws` link and its side calls MUST be TLS.**
   - A connector MUST NOT send a credential on a plain `ws` link or side call to another machine, whatever a hello, a typed `http://`
     address or a setting says.
   - With no TLS on offer, it reads the acceptor's public view keyless and says why: "<name> offers no TLS — the key stays on this
     machine".
3. A `line` link MUST be TLS when the acceptor offers TLS on that port. Where it offers none, a `line` link MAY run plain off this
   machine only as `degraded`:
   - verified by LINK;
   - carrying no secret;
   - shown amber;
   - its clock never scheduling a timed command (13.11.6);
   - its lines understood to be readable and alterable on the path.
4. A local link MAY be plain.
   - A node that forwards outside traffic to one of its ports through loopback (a tunnel, a reverse proxy) MUST tell that traffic apart
     and MUST NOT treat it as local.
   - Before any listener grants anything to loopback, it MUST check that the request names the node by one of its own hosts (`Host`)
     and comes from no foreign page (`Origin` absent or the node's own).
5. Every listener a PeerLink uses MUST honour the node's bind address.
6. **HTTP polling alone is not a PeerLink.** A node that only polls another's context is a *reader*: it shows the context's age, grey
   at best, never a green link, and sends no command through it.

### 13.4 Identity and the hello

1. A node is its hello `id` and `t`, together with its certificate (§10) and the key it proves.
   - A connector keeps one link record per node id: the pin, the floor (13.9.3), the key held for that node, the clock and the state.
   - An address is where to dial, never who.
2. **A hello may raise a link's guard and MUST NOT lower it.**
   - It may add a peer to dial, an address to try, and the hints `tls` and cap `peerlink` (try TLS first, try LINK first).
   - It MUST NOT make a link plain, change or confirm a pin, choose which key is proved or where a key goes, mark anything verified, or
     refuse a link.
   - **For an id with a pin, the pin alone decides.** The connector dials TLS whatever the hello says.
   - A hello that contradicts the pin (no `tls`, another fingerprint, another address only) is shown as a note on the link ("the hello
     heard for Hall A names no TLS — a stale or forged hello; dialling TLS as before"), or as §2's conflict, never as a refusal.
   - A typed address follows the same rule.
3. A typed address is recorded as `addr:<host>:<port>` until the first verified link names the acceptor's id.
   - The record then becomes `node:<id>`, with that pin, and the key typed for it is bound to that id.
   - A key bound to one id MUST NOT be proved to, or sent toward, another.
   - An acceptor that answers LINK with an id other than the one the connector meant is refused, naming both ids ("10.0.0.5:9697 now
     answers as patterns 7a7a… — not Patterns FOH 9f8e…; accept it on the Nodes page"). Nothing it says is followed until the operator
     accepts the new id.
4. An id in conflict (§2) gets no new link; a link it already has keeps running.

### 13.5 Keys

1. **Classes.**
   - *key* — machine-made, with at least `peerlink.key.bits.min` random bits from the platform's CSPRNG, shown once: `<prefix>_` and 32
     symbols of `abcdefghijkmnpqrstuvwxyz23456789` (160 bits).
     - Stagewatch's `swk_…` are such keys; Patterns issues `ptk_…` and PlayMap `pmk_…` for app links.
     - `K = SHA-256(UTF-8(key))`, which is what Stagewatch already stores for a key.
   - *code* — a pairing code a person reads and types: Patterns' and PlayMap's pairing token.
     - Its canonical form removes `-` and every one of U+0009–U+000D, U+0020, U+0085, U+00A0, U+1680, U+2000–U+200A, U+2028, U+2029,
       U+202F, U+205F, U+3000 and U+FEFF, and upper-cases `a`–`z`.
     - The result MUST be `[A-Z0-9]{1,64}`. A code that is not is refused where it is typed ("a pairing token is letters and digits")
       and never used.
     - `K = SHA-256(UTF-8("showbus-peerlink-1 code\n" + canonical))`.
     - A code is the operator's credential: scope `operate`. Proved over a plain carrier off this machine, it is an offline guessing
       target, and the link says so (flag `code-plain`).
   - *link* — a key both ends derive at enrolment (13.5.7) and never send. `K` is its 32 bytes.
   - A key's `kid` is the lowercase hex of the first `peerlink.kid.bytes` bytes of SHA-256(UTF-8("showbus-peerlink-1 kid\n") ‖ K). A
     code's kid is the word `code`. A kid is not a secret.
   - A PIN, a viewing code and a view pass (§12.2) are never PeerLink credentials.
2. **One key, one pair.**
   - An acceptor binds each key to the one connector `t:id` it was made for — named when the operator makes it, or at its first
     verified use — and refuses it for any other: "this key speaks for <kind> <id>". That refusal is never counted.
   - A connector holds each key for one node id and its pin (13.4.3).
   - A code belongs to its node and may be proved by several connectors, each named by its own id.
3. **Scope.** Every key has one scope on this ladder, which each node's §8 row maps onto its own verbs:
   - `follow`: state, context, subscriptions;
   - `post`: the above, plus its own context, proposals and their results — Stagewatch's `node` scope;
   - `drive`: the above, plus the verbs the node lists for a playout desk — clip and playlist transport, cue GO and STANDBY;
   - `operate`: every verb, as the operator — a code, or a control key.

   The acceptor enforces the scope. A refusal names it: "this link's key is scope follow — Patterns FOH's operator can raise it on its
   Nodes page". A connector's own fence is extra.
4. **At rest.**
   - An acceptor keeps only what verifies: `K`, or the hash it already keeps.
   - A connector keeps its keys and codes protected — Windows DPAPI for the user, an owner-only file elsewhere — and never in a show
     file, a project, a backup, an export or a twin's traffic (13.18).
5. **Revocation reaches live links at once.**
   - Revoking or re-binding a key, or a new code, MUST refuse every verified link that used it (13.16) and end its session
     credentials (13.7).
   - A new code (Patterns' NEW TOKEN) also revokes every key enrolled from the old one.
6. **Counting.**
   - Unknown kids and binding refusals are never counted toward any lockout.
   - Wrong proofs are counted in a table of their own: per (address, kid), and per address for `code`.
   - `peerlink.wrong.proofs.per.minute` in a minute lock that pair for the minute; a locked pair is refused before its proof is checked.
   - That table never locks out a PIN, a viewing code, a view pass, another key or a browser.
7. **Enrolment** (feature `enrol`; an acceptor MAY offer it). Over TLS, a link proved with a code enrols:
   - both ends derive `K_link = HMAC-SHA256(K_code, "enrol\n" ‖ T)` (T of 13.6.1);
   - the acceptor stores it bound to the connector's id, with the scope its operator set for that kind (`follow` unless set), journals
     it, and names it in the verdict (`"enrolled":"<kid>"`);
   - the connector proves with that kid from then on.

   A code proved over a plain carrier MUST NOT enrol. An acceptor lists each enrolled key with the connector's name, id, scope and when,
   so an enrolment nobody expected is seen.

### 13.6 The handshake: LINK

1. **The transcript.** T is these lines in UTF-8, joined by LF, with no final LF:

```
showbus-peerlink-1
carrier=<line|ws>
offer=<the connector's highest PeerLink version>
speaks=<the version the acceptor chose>
server=<acceptor t>:<acceptor id>
client=<connector t>:<connector id>
kid=<kid>
snonce=<32 lowercase hex: 16 bytes from the acceptor's CSPRNG>
cnonce=<32 lowercase hex: 16 bytes from the connector's CSPRNG>
bind=<tls:sha256:<64 lowercase hex> | plain>
```

   - `bind` is the certificate this connection showed (on the connector's side, the one it was shown; on the acceptor's side, its own),
     or `plain`.
   - Each field is checked against its shape before T is built, so no LF can enter it: `t` `[a-z0-9_-]{1,16}`, `id`
     `[A-Za-z0-9_-]{6,40}` (§2), kid 16 lowercase hex or `code`.
2. From T and K:
   - client proof = lowercase hex of HMAC-SHA256(K, "client\n" ‖ T);
   - server proof = lowercase hex of HMAC-SHA256(K, "server\n" ‖ T);
   - `lid`, the link's id (not a secret) = lowercase hex of the first `peerlink.lid.bytes` bytes of
     SHA-256("showbus-peerlink-1 lid\n" ‖ T);
   - on `ws`, the session credential S = lowercase hex of HMAC-SHA256(K, "session\n" ‖ T) (13.7).
3. **Order.**
   - The connector proves first. The acceptor verifies, then answers with its own proof.
   - The connector MUST NOT take state, context, a receipt, a clock sample, a pin or a fact from the connection before the acceptor's
     proof verifies; anything that arrives earlier is discarded and counted.
   - Each nonce answers one proof, right or wrong. One proof per connection: a wrong one ends the connection on `line`; on `ws` it ends
     the socket's standing as a node, and the socket stays a public viewer (§7.1).
   - An acceptor MUST treat an unknown kid exactly like a known one until the proof, and compare in constant time. It refuses both with
     the same words: "the proof did not verify: a wrong key, or another machine between us" (its own journal MAY name the kid).
   - A connection that has not finished LINK within `peerlink.handshake.ms` is closed. An acceptor holds at most
     `peerlink.unpaired.per.address` unfinished connections per address.
4. **`line`.** Two verbs join the line grammar. Before LINK completes, an acceptor answers only what finding and pairing need (§7.2's
   words, plus `LINK`) and nothing of the show:

```
← NONCE 7c1e…                                   a 1.2 greeting; a LINK connector ignores it
→ HELLO Stagewatch
← OK
→ @l1 LINK 1 main 3f9c2a1b7d0e ffeeddccbbaa99887766554433221100 code
← OK @l1 LINK 1 patterns 9f8e7d6c 00112233445566778899aabbccddeeff tls:sha256:03e9b152ca42518c28128f36def699be8aea5eec11bb9d5afc18a314f8c3eb04
→ @l2 LINK PROOF a0622441e11e24be7577abcff6599b5b8c5031a06739a2774e94307d70b5c09f
← OK @l2 paired {"v":1,"verified":"code","scope":"operate","proof":"a628a0aa5691349ced0d2e1d8b9b13203b55bbd3d994a9e45925b74fc1369e02","lid":"c1bb791379aa366c","level":1,"can":["final","receipts","enrol"],"inst":"a41f09c2","beatMs":2000,"enrolled":"936086ee6bc93cef"}
← STATE {…}                                     the greeting, now; then on every change
```

   - The forms: `LINK <offer> <t> <id> <cnonce> <kid>`; its answer `OK LINK <speaks> <t> <id> <snonce> <bind>`; `LINK PROOF <64 hex>`.
   - The verdict is `OK paired <json>`, or `ERR LINK <class> — <words>`, with class `malformed`, `version`, `refused`, `locked`,
     `binding`, `busy` or `open`.
   - A word on a LINK line that the acceptor does not know refuses the line. A malformed proof is a wrong proof.
5. **`ws`.**
   - The connector opens `wss://<acceptor>/ws?node=<its id>&t=<its kind>&link=<offer>` with no `Authorization` header.
   - The acceptor's first message: `{"type":"link","v":1,"speaks":1,"server":{"t","id"},"snonce","bind"}`.
   - The connector's: `{"type":"link","client":{"t","id"},"cnonce","kid","proof"}`, whose `client` MUST equal the query's `node` and
     `t`.
   - The verdict is §7.1's, with fields appended: `{"type":"auth","ok":true,"scope","node":{"t","id","verified":"key"},"proof","lid","level":1,"can":[…],"inst","beatMs"}`,
     or `{"type":"auth","ok":false,"error","class","retry":60}`.
   - No frame is sent to a `link=` socket before its verdict. After `ok:false` the socket stays a public viewer.
6. **The verdict** carries:
   - `verified` (`key`, `code` or `link`) and `scope`;
   - the acceptor's `proof` and the `lid`;
   - `level`;
   - the features the acceptor will use on this link (`can`);
   - its `inst` (§2) and `beatMs`;
   - `stuckMs` when it lists `stuck`, `expireMs` when it lists `expire`, `enrolled` when it enrolled.
7. **An open acceptor** (no code, no keys) answers `ERR @l1 LINK open — this node asks for no key`. The connector links unverified,
   with the flag `open` (13.16.2).
8. **Level 0: an acceptor that does not speak LINK.** The connector knows it this way:
   - on `line`: any `ERR` to `LINK` whose words do not begin with `LINK` (Patterns' `unknown command`, PlayMap's `not paired` on its
     WebSocket channel), or no answer in `receipt.deadline.ms`;
   - on `ws`: a first message that is not `link`.

   Then:
   - **Floors first.** A node id with a floor (13.9.3) is refused, red; nothing more is sent.
   - **`line`, the legacy proof.**
     - The connector MAY answer §7's `NONCE` with 1.1's `AUTH-PROOF` (HMAC over the nonce and the HELLO name), and only a `NONCE` of
       exactly 32 lowercase hex.
     - It MUST NOT send plain `AUTH <secret>` to another machine, nor on a connection that heard a `NONCE` or an `OK` to its `NONCE` ask.
     - The link is `legacy`, amber: "<node> cannot prove itself yet — an older build; anything at its address could answer".
     - Facts it publishes are followed as in 1.2, and the words say so.
   - **`ws`, the legacy bearer.**
     - The connector sends its key in `Authorization: Bearer` only over TLS, to a certificate already pinned for that id or one a
       person typed or confirmed on this desk (one press, beside the short fingerprint).
     - Otherwise it reads the public view keyless: "Hall A cannot prove itself yet (an older Stagewatch) — compare its fingerprint 03e9
       b152 ca42 518c and press Trust to send the key".
     - Side calls carry the bearer key on the same terms. The link is `legacy`, amber.
9. **Bearer keys** remain for tools and scripts.
   - A connector that speaks LINK MUST NOT send a key, code or token to an acceptor that speaks LINK.
   - An acceptor that receives a key or token in clear from another machine MUST mark it *seen in clear* — on its key row and in its
     context, amber, with when and from where: "replace this key".
   - It gives that key no window grant (§12), and from 1.4 refuses it.

### 13.7 Side calls

1. On a verified `ws` link, every side call carries `Authorization: ShowBus lid=<16 hex>, s=<64 hex>` (S of 13.6.2).
   - It goes only over TLS, to the certificate that link pinned, or to loopback; never in a URL.
   - S is valid while its link's socket is open and verified. The socket's end, a revocation or a refused verdict ends it, answered
     `401 {"ok":false,"error":…,"relink":true}`.
   - An acceptor that receives an S in clear from another machine refuses it and ends that link.
2. Side calls run on a lane of their own:
   - never in the beat's path (13.10.3);
   - at most `link.sidecall.inflight` per kind at a time, each within `link.sidecall.timeout.ms`;
   - a newer post replaces one not yet sent;
   - their failures are counted on their own item and never move the link's liveness.
3. **One door for refusals.**
   - A 401 starts the key's hold (13.15.3).
   - A 429 waits its `Retry-After`.
   - A 403 says its words and holds nothing.
   - A timeout or a 5xx is retried on the call's own schedule for a read, a context post or an owed report (13.12.9), and never for a
     command (13.12.7).
4. A side call that carries a command carries its id — `X-Request-Id` on Stagewatch and PlayMap, the line's `@id` in the body on
   Patterns' `/api/cmd` — and its answer is read by 13.12.3.

### 13.8 TLS and pinning

1. TLS 1.2 or 1.3, with ephemeral key exchange; the certificate is §10's. A connector checks a certificate by its fingerprint
   (`sha256:` + lowercase hex of SHA-256 of the DER) and by nothing else — never a name, a chain or an authority.
2. **First use.**
   - With no pin, any certificate is accepted for the handshake.
   - It is **pinned only after the acceptor's proof verifies over it**.
   - On a link that reads only the public view, it is pinned after the first frame and shown `first`: "pinned 03e9 b152 ca42 518c at
     20:01 — compare it on Hall A's screen".
   - A certificate that differs from the hello's `tls` is a note, never a refusal; on a keyed link the proof decides.
3. **Changed.**
   - A pinned node showing another certificate gets nothing of the connector's — no key, code, proof, S or command — until the operator
     accepts the new one, typing at least 8 hex of it: CERTIFICATE CHANGED, red.
   - Meanwhile the connector MAY read the public view keyless over that certificate, and MAY probe every `peerlink.cert.probe.ms` to
     show the current fingerprint.
4. A pin store that cannot be read is said, amber: "pins lost — every node is new here; compare fingerprints". Floors are lost with it.
5. An acceptor SHOULD serve TLS on every port a PeerLink uses, telling TLS from plain by the first byte (0x16), as §10 does for HTTP.

### 13.9 Versions, features and floors

1. The PeerLink version is chosen in LINK (`offer`, `speaks`). Both are inside T, so a changed offer fails the proof. The hello's
   `proto` decides nothing here.
2. **Features.** The verdict lists the features the acceptor will use on this link. A version-1 connector reads every version-1 shape —
   a fifth PONG word, an `OK @id final …` line, a `repeat` field — whether or not it uses the feature, and ignores what it does not use.
3. **Floors.**
   - After a verified link, a connector records for that node id what it reached: `{tls, level, proof}`.
   - A later link to that id that offers less MUST be refused, red: plain where TLS was, LINK refused as an unknown word, a lower
     `speaks`, or an `open` answer where a key verified.
   - The words: "<node> used to prove itself with PeerLink 1 over TLS; it now offers less — not linking". The connector keeps only what
     the acceptor shows any reader (§8's public view), where the carrier has one.
   - The operator lowers a floor with one press, journalled.
   - Floors come only from verified links, never from a hello.

### 13.10 Liveness — the link and the show

1. **Beats.**
   - The connector pings every `ping.min.ms`–`ping.max.ms` (§5).
   - The acceptor answers each ping at once, and never through its show writer.
   - A `ws` acceptor also sends a frame or `{"type":"beat"}` at least every `link.beat.ms`.
2. **Matched pongs.** Any complete message from the verified acceptor counts as heard. A pong is a clock sample only when it answers one
   of the connector's last `peerlink.pings.matched` pings (its `t1`).
3. **The link.** With s the silence since the last complete message:
   - **live** while s ≤ `link.stale.ms`;
   - **stale** while s ≤ `link.drop.ms`;
   - then **down**: the connector closes and redials (13.15), and the acceptor closes.

   So 6000 ms is live, 6001 stale, 12000 stale, 12001 down. The state is evaluated at least every `link.check.max.ms`, on a timer of its
   own that never waits behind a side call or a write.
4. **Its own gap.** When the evaluator's consecutive runs are more than `link.selfgap.ms` apart — this node was paused, asleep or
   starved — it MUST NOT move a link to down on that run (a link past `link.drop.ms` shows stale). It pings every link at once, decides
   again `link.selfgap.ms` later, and logs one line naming its own pause.
5. **The show.** Show liveness — `live`, `stuck` or `unknown` — is kept apart from link liveness.
   - `ws`: `{"type":"beat","live":false}` makes it `stuck`. Only the next state frame makes it `live`; a plain beat or a pong never
     clears it.
   - `line`, feature `stuck`:
     - the acceptor answers a ping `OK [@id] PONG <t1> <t2> <t3> STUCK` while its show writer has not turned over within its `stuckMs`;
     - on such a link, a pong without the word makes show liveness `live`;
     - the flag is read from a stamp the writer sets each turn, and written off the writer;
     - an acceptor lists `stuck` only once its `stuckMs` (between `show.stuck.min.ms` and `show.stuck.max.ms`) comes from a measured
       honest stall of its own show writer, named in its §8 row.
   - Otherwise show liveness is `unknown`, and the link claims nothing.
6. While a peer's show is stuck, or its link is stale or down, no counting value taken from it is shown as current: it shows `?` with
   the reason. The link stays live while only its show is stuck.

### 13.11 Clock offset and its quality

1. §5's exchange runs on every link. t1 and t4 come from the connector's wall clock; t2 is the acceptor's as it read the ping; t3 is as
   late as its writer allows, and never before t2.
2. A sample is refused when any stamp is ≤ 0, t4 < t1, t3 < t2, rtt < 0 or rtt > `clock.rtt.max.ms`, or when its pong is not matched
   (13.10.2).
3. The last `clock.window` samples are kept, and the one with the smallest rtt is believed. A link publishes:
   - `offsetMs`;
   - `errMs` = ⌊(rtt + 1) / 2⌋ + 1 of that sample;
   - `ageMs` since its t4;
   - `samples`.
4. **Quality.**
   - `unknown` with no sample;
   - **fresh** while `ageMs` ≤ `clock.fresh.ms`;
   - **fit for** a tolerance while fresh and `errMs` ≤ that tolerance.
5. **CLOCKS APART** when |`offsetMs`| − `errMs` > `clock.apart.ms`.
6. A timed command (`AT`) uses an offset only when it is fit for that verb's tolerance and was measured over TLS or a local link;
   otherwise the command falls back and says so. A clock measured over a plain link off this machine MAY be shown and MUST NOT schedule.
7. The window is emptied when the peer's `inst` changes, and when this node sees its own wall clock step.

### 13.12 Commands — request ids, receipts and outcomes

1. **Ids.** Every command a connector sends on a PeerLink carries a request id `<prefix><n>-<inst>`:
   - a prefix of 1–4 letters, a counter from 1, and its hello's `inst`;
   - unique for the process and never repeated by a later one;
   - at most `request.id.chars` characters (§3).

   An acceptor treats an id as opaque.
2. **Outcomes.** Each command ends, at the connector, in one of:
   - `not-sent`: never written; it did not run;
   - `refused`: the acceptor said no; it did not run;
   - `accepted`: the acceptor took it and has not run it yet;
   - `applied`: it ran; `effect` says what changed;
   - `unconfirmed`: written, and no final answer came; it may have run ("unknown — <node> did not answer; look at its state").

   An outcome only moves forward: `accepted` → `applied` or `refused` by a final receipt; `unconfirmed` → `applied` or `refused` by a
   final receipt or a `RECEIPTS` answer (13.12.6). Whether an effect was later seen in the peer's state is a reader's (layer 3–4) fact,
   journalled as *seen*. A receipt MAY carry `frame`, the counter (§4) of the first state frame published after it ran.
3. **Reading answers.** Every connector reads by this table:

| the answer | outcome | words |
|---|---|---|
| line `OK @id`, or `OK @id <words>` that is not JSON | applied | the words |
| line `OK @id {…}` with `"stage":"accepted"` (PlayMap) | accepted | `effect` |
| line `OK @id {…}` with `"outcome":"Requested"` (Patterns `CUE GO`) | accepted | Patterns' words |
| line `OK @id {…}` with `"outcome":"Confirm"` | accepted | Patterns' words + "asks for a second GO — a person's" |
| line `OK @id {…}` with `"stage":"applied"`, `"outcome":"Done"`, or neither | applied | `effect`, or the words |
| line `ERR @id <words>` | refused | the words |
| line `OK @id final {…}` / `ERR @id final <words>` | as the rows above, final | |
| JSON receipt `"ok":true,"stage":"applied"` | applied | `effect` |
| JSON receipt `"ok":true,"stage":"accepted"` | accepted (a `final` may follow) | `effect` |
| JSON receipt `"ok":false` | refused | `error` |
| any receipt with `"repeat":true` | as its other fields | + "answered from its record — not run again" |
| HTTP 2xx `{"ok":true}` with neither `stage` nor `msg` | accepted | "took it — this node does not say whether it ran" |
| HTTP 2xx `{"ok":true,"msg":"OK …"}` (Patterns `/api/cmd`) | applied | `msg` after `OK` and any `@id` |
| HTTP 2xx or 4xx `{"ok":false,"msg":"ERR …"}` or `{"ok":false,"error":"…"}` | refused | `msg` after `ERR` and any `@id`, else `error` |
| HTTP 401 | refused, and the key's hold (13.15.3) | the body's `error` |
| HTTP 403, 404, 409, 413 | refused | the body's words |
| HTTP 429 | refused | "rate limited — again in N s" (`Retry-After`) |
| HTTP 408, a 5xx, or no response after the request was written | unconfirmed | |
| no connection, a hold, `link.inflight.max` reached, a link that is not live, or a peer whose show is stuck | not-sent | why |
| written, then no final answer within `receipt.final.ms`, or the link dropped first | unconfirmed | "no answer in 30 s — it may have run" / "the link dropped before <node> answered" |
| an acceptor older than 1.1, which echoes no id | unconfirmed | "sent — this node gives no receipts; look at its state" |

4. **Deadlines.**
   - An acceptor answers within `receipt.deadline.ms` with applied, refused or accepted.
   - With feature `final`, it sends one follow-up within `receipt.final.ms` once it knows: `OK @<id> final <json>` or
     `ERR @<id> final <words>` on `line`, `{"type":"receipt",…,"final":true}` on `ws`.
   - A connector shows "waiting for <node>'s answer" after `receipt.deadline.ms`. A command written with no final answer within
     `receipt.final.ms`, or whose link dropped first, is `unconfirmed`.
5. **Late commands.**
   - An acceptor that runs a command more than `receipt.late.ms` after it read it says so: `"lateMs"` in the receipt, and a journal row.
   - Feature `expire` is offered only by an acceptor whose §8 row names an `expireMs` (≤ `cmd.expire.max.ms`) set by a measured stall
     of its show writer. With it, the acceptor does not run a command later than its `expireMs`, and answers refused "expired — read at
     20:14:03.120, not run".
6. **Receipts kept** (feature `receipts`).
   - The acceptor keeps, per verified connector id, the final receipts of its last `receipts.kept` ids for `receipts.kept.ms`.
   - A command whose id it holds MUST NOT run again: it is answered with the receipt it had and `"repeat":true` appended (or `accepted`
     while it still runs).
   - After a relink, the connector MAY ask `@rq RECEIPTS <id> …` (at most `receipts.ask.max` ids) or
     `{"cmd":"receipts","args":[…],"id":"rq"}`, for its own ids only. The answer:
     `OK @rq {"inst":"a41f09c2","receipts":{"s17-a41f09c2":{…},"s18-a41f09c2":"unknown"}}`.
   - "unknown" from an acceptor whose `inst` is not the one the command was sent to leaves it `unconfirmed`.
7. **No blind resends.** A connector MUST NOT send a command again once any byte of it may have reached the acceptor. Sending it again
   is a person's act, with the outcome in view and a new id.
8. **In flight.** At most `link.inflight.max` commands wait for answers on one link; the next is `not-sent` ("32 commands are waiting on
   <node>'s answers"). Nothing waiting is ever evicted, and nothing is queued for a link that is not live (§4).
9. **Reports owed** (a §11 result, an acknowledgement):
   - are idempotent at their receiver: a second one is answered 409 "settled", which counts as delivered;
   - are retried by their sender from `report.retry.first.ms`, doubling to `report.retry.max.ms`, ±20 %, for `report.retry.for.ms`;
   - are recorded before the act they report.
10. One journal row per command that reached the socket, when its outcome first settles; one more when an `unconfirmed` one is resolved
    or its effect is seen.

### 13.13 Order and latest wins

1. One writer per connection. Replies leave in the order their lines came, and receipts are matched by id, never by order. Commands,
   replies and receipts are never coalesced or dropped.
2. State is latest-wins.
   - A sender keeps one pending push per kind and replaces it.
   - A reader keeps the newest frame per stream, and on one connection takes a frame only when its counter (§4) is above the last it
     took.
   - The first frame on a new connection is taken whatever its counter.
   - A counter lower than the last connection's, or a new `inst`, means the peer restarted: every anchor and held value taken from it is
     dropped, save 13.17's held facts.
3. An event stream (every item matters) is ordered and counted. A reader that falls behind is cut, with words saying where to resume.

### 13.14 Bounds and back-pressure

| bound | value |
|---|---|
| a command line an acceptor reads | ≤ `link.command.bytes`; longer: `ERR … — closed` |
| a message a connector reads | at least `link.read.min.bytes`. A longer one is counted and shown amber with its size (`oversize`), read only for the fields it can reach, and never taken as whole; the link stays up |
| a message a connector sends to Stagewatch's `/ws` | ≤ `link.ws.in.bytes` |
| a posted context | ≤ 64 KiB (§6) |
| replies waiting at an acceptor, per connection | ≤ `link.replies.max`, plus one pending push per kind; past that the connection is cut: "not reading its answers" |
| one write | within `link.write.deadline.max.ms`, or the connection is cut |
| commands in flight per link | ≤ `link.inflight.max` (13.12.8) |
| side calls | 13.7.2 |
| connections an acceptor takes | its own ceiling, said in words before it closes an extra one (a line of words, or `429` with `Retry-After`); a connector takes `Retry-After` as a hold |

The show writer never writes to, reads from or waits on a socket.

### 13.15 Reconnect and holds

1. **The wait before redial n.** n counts failed dials since the link was last stable, from 0.
   - base = min(`link.redial.max.ms`, `link.redial.first.ms` × 2ⁿ);
   - wait = round(base × (0.8 + 0.4 j)), with j uniform in [0, 1) from a generator seeded once per process from the CSPRNG (a test
     injects j).

   A first redial waits 400–600 ms, and none waits more than 6000 ms.
2. **n returns to 0 only after a link that was verified and stayed up — never down — for `peerlink.stable.ms`.** A connect, an upgrade,
   a reply or a refusal alone resets nothing, so an acceptor that accepts and closes is backed off.
3. **Holds replace the wait.**
   - A refused key — unknown, revoked, bound to another, a wrong proof, locked out, out of scope — is held for
     max(`peerlink.hold.refused.ms`, the acceptor's `retry` or `Retry-After`).
     - The hold is recorded before any further I/O, and persisted with its end across restarts.
     - The connector proves that key again only when the hold ends, the key changes, or the operator presses *Link again now*.
     - A `ws` link keeps its public frames meanwhile.
   - The acceptor operator's `disconnect` holds `peerlink.hold.disconnect.ms`.
   - CERTIFICATE CHANGED, a floor, another id and an id conflict link nothing keyed until the operator acts.
4. **Where.** The connector dials the address its link uses while the hello still names it (§2), and a peer on its own machine at
   127.0.0.1 first.
5. **Nothing crosses a drop** (§4). A command not yet written becomes `not-sent`; a written one becomes `unconfirmed`.

### 13.16 States, and what each side shows

1. A connector keeps one state per link, from this closed set. Every light carries its shape beside its colour.

| state | when | light | the connector says (example) | commands | counting values |
|---|---|---|---|---|---|
| `off` | not configured | ○ grey | "link off" | not-sent | none |
| `finding` | no address, and no live hello of the kind | ○ grey | "listening for a Stagewatch on the show network" | not-sent | none |
| `connecting` | dialling, TLS, LINK | ○ grey; △ amber after 10 s | "dialling Hall A at 10.0.0.5:9924 over TLS (attempt 3)" | not-sent | `?` |
| `live` | heard within 6 s; verified, or up with `legacy`, `open` or `keyless` | ✓ green; △ amber with an amber flag | "Hall A · pinned 03e9 b152 · key 52f6… · scope post · PeerLink 1 · +12 ms ±3" | sent | shown while its show is live |
| `stale` | silent 6–12 s | △ amber | "Hall A quiet for 8 s" | sent; may end unconfirmed | `?` |
| `down` | silent past 12 s, dropped or unreachable; redialling | ✕ red when the show needs it, else △ amber | "Hall A not answering — dialling again in 2.4 s; nothing is queued" | not-sent | none |
| `held` | a key refused; its hold runs | ✕ red when the show needs it, else △ amber | "Hall A refused this desk's key (revoked) — next try 20:15:05; make a node key on Hall A" | not-sent | public view only |
| `refused` | CERTIFICATE CHANGED, a floor, another id, an id conflict: a person must act | ✕ red | "CERTIFICATE CHANGED: Hall A now shows ab12 … — nothing sent; accept it on the Nodes page" | not-sent | public view only, where the carrier has one |

   Beside the state is the **show**:
   - `stuck`: the link △ amber "Hall A says its show is stuck"; every value from it ✕ "no time shown"; commands not-sent;
   - `unknown`: an i note, no claim.
2. **Flags.** Each flag has its words; it is amber △ unless marked i.
   - `plain`: a `line` link off this machine with no TLS on offer — "proved, not private — its wire offers no TLS yet; timed GO off".
     It is i on a local link.
   - `keyless`: a key is set but held back. It is i "public view" when no key is set.
   - `legacy`: level 0 (13.6.8).
   - `open`: the acceptor asks for no key. Amber when this connector holds a key for it, i otherwise.
   - `code-plain`: proved with a pairing code over plain — "a recording of this proof can be guessed offline — make an app key on
     <node>".
   - `scope`: the key's scope is below what the link is used for.
   - `first` (i): pinned at first use; compare the fingerprint.
   - `note` (i): a hello contradicts the record.
   - `apart`: CLOCKS APART.
   - `unfit` (i): the clock is not fit for timed commands.
   - `oversize`, and `malformed` (i, a count).
   - A link's light is the worse of its state's and its flags', and red only when the show needs the link.
3. **The link record.** Every reader takes this one record. It is appended at the end of each row that carries a link, and never
   carries a key, code, `K`, S, proof or nonce:

```
{"peer":{"t","id","name"},"carrier":"line|ws","level":0|1,"tls":"pinned|first|none|local","pin":"<16 hex>",
 "proof":"mutual|legacy|open|none","kid":"<16 hex>|code","scope":"follow|post|drive|operate|none",
 "state":"…","refusal":"key|binding|scope|locked|certificate|floor|other-id|conflict|busy|malformed|","show":"live|stuck|unknown",
 "flags":[…],"light":"green|amber|red|grey","mark":"✓|△|✕|○|i","words":"…","since":<wall ms>,"heardMs":…,"attempt":n,"nextMs":…,
 "clock":{"offsetMs","errMs","ageMs","samples","apart"},"counts":{"unconfirmed","late","replayed","oversize","malformed","unmatched"},
 "floor":{"tls","level","proof"}}
```

4. **Every reader.** The connector publishes the record in:
   - its STATE;
   - its context item `link:<name>` (kind `link`, the light and the state's words), with a `links` row to the peer node, and the
     `peers` row's `link` and `verified` (§6);
   - the Eye's edge, its desk's node card and Run strip, and its right-click menus;
   - its Companion variables and feedbacks;
   - its AI facts, its preflight and its report.

   No reader derives a link fact of its own. The Eye draws the links a node does not hold one hop out, from other nodes' contexts: an
   app's `link:` items and `links` rows become edges between that app and its peer, and a node known only from a context's `peers` is
   grey "heard, not linked".
5. **The acceptor's side.** An acceptor lists, on its operator surface and in its context:
   - each verified peer: its id, kind and name, carrier, level, TLS, kid or key name, scope, and since when;
   - each refusal with its words and its count this minute ("PlayMap Stage left (10.0.0.12): key 52f6… refused (revoked) ×3 this
     minute");
   - each key with its bound connector, scope, last use, how it was made (issued or enrolled) and whether it was *seen in clear*.
6. **Operator actions.** These are on every surface, in step: the desk's buttons, right-click menus, wire verbs and Companion.
   - Connector: *Link again now*; *Accept certificate* (at least 8 hex of the new one); *Lower floor*; *Accept new id*; *Release held
     fact* (13.17).
   - Acceptor: *Make key for…*; *Let this key speak for <node>*; *Revoke key*.

   Each is journalled. None is proposed by an assistant: a trust decision is a person's.
7. One journal row and one log line per state change — from, to, reason, attempt — never per beat (13.19).

### 13.17 Safety on loss

- A fact a node follows for safety — a blackout, a hold — is **held** when its link goes stale or down, or its show stuck.
  - It is shown with its age: "holding Patterns' blackout since 20:14:03 — link lost".
  - It is released only by the peer's live word or by a person, never by silence.
- A counting value is never held (13.10.6).
- What a link feeds to air goes to that output's own loss state — a window's `onLoss`, black for a drawn window — never to the last
  picture of a live count.
- No command is sent to a peer whose show is stuck (13.12.3).

### 13.18 Doors that are not PeerLinks

These doors keep their own rules:

- OSC;
- Stagewatch's Companion TCP (9923);
- Companion decks on the line wires: they keep §7's legacy proof until their modules adopt LINK, and never send plain `AUTH` off this
  machine;
- browser pages and `?pin=`;
- window links (§12);
- Slides agents;
- PlayMap's sync (9942) and fleet traffic;
- PlayMap's desk↔engine link on one machine;
- Patterns' twin and its node relays. These keep failover's own carrier, beat (1 s), silence (5 s) and dialling, and:
  - MUST honour the bind address;
  - send device secrets only under their own switch, which is off by default and refused on a plain link to another machine;
  - adopt 13.12's outcomes and 13.19 in a later round of their node.

Each door is named in its node's §8 row with what it does without a proof, and each node's health lights say what these doors leave
open. **A PeerLink proof grants nothing to an address:** it proves a key on one connection, and no OSC, trusted-address or loopback trust
follows from it.

### 13.19 Secrets and logging

1. These are never sent off this machine — only proved: a key, a code, `K`, `K_link`, a PIN, a view pass. The one exception is S,
   inside pinned TLS. None of them, S included, rides in:
   - a URL, a hello, a frame, STATE, a context or a receipt;
   - a journal row, a log line, a report, a CSV or a support bundle;
   - an error or exception message;
   - a Companion variable, an AI brief or a proposal.
2. Proofs, nonces and transcripts are not logged either: a proof made with a code is a guessing target. A log names:
   - a key by its kid, or by its name and last four characters;
   - a certificate by its short fingerprint;
   - a peer by kind and id.
3. A line that may carry a secret — a legacy `AUTH`, a verb with a token argument, an `Authorization` header — is redacted where it is
   read, before any logger, journal or fault path sees it.
   - A fault answer never echoes the line.
   - Kept lines read `AUTH (token)`, `AUTH-PROOF (proof)`, `LINK PROOF (proof)`.
4. One log line per change of state: the link, the peer's kind and id, the address, from → to, the reason and the attempt. Never per
   beat.
   - While a state repeats, one summary a minute: "still dialling Stage left: 11 attempts, last: refused".
   - Counters are published (13.16.3), not only logged.
5. Each tree runs 13.20's rows and its scripted-peer scenarios with canary secrets. It fails when a canary — or a proof or credential
   made with one — appears in anything it captured.

### 13.20 Conformance

A node sends cap `peerlink`, and offers LINK, only when its tree passes the block below and the scripted peer's scenarios for every
carrier it uses, in each role it plays. Its §8 row lists anything it does not keep yet, and what ends it.

- **The scripted peer** is `tools/showbus/peerlink-peer.mjs`: one dependency-free Node 22 file, byte-identical in every tree. Its SHA-256
  is §14's `peerlink.peer.sha256`.
- **The block** holds four kinds of rows:
  - crypto rows: `canon`, `key`, `proof`, `refuse`, `legacy`;
  - parse rows;
  - pure rows: `liveness`, `redial`, `errms`, `clock`, `outcome`;
  - machine rows: events on injected time → state, light and hold. A machine row is evaluated at each `expect` time, after every event
    at or before it.
- `refuse` rows reuse the first `proof` row, with the one named field changed. Hex is lowercase. `K` is the 32-byte key as hex.

```peerlink
{"case":"canon","code":"k7qm-3xwd p9ra","canon":"K7QM3XWDP9RA","K":"5155f2dfc471eeab6498c03b18e3e3df5f8c9f7a2e5f85c8ed4526aaad49e5c8","kid":"code"}
{"case":"canon","code":"k7qm 3xwd p9ra","canon":"K7QM3XWDP9RA"}
{"case":"canon","code":"﻿K7QM-3XWD-P9RA\u0085","canon":"K7QM3XWDP9RA"}
{"case":"canon","code":"k7qm‑3xwd","canon":null}
{"case":"canon","code":"K7QM-3XWD-P9RÄ","canon":null}
{"case":"canon","code":"","canon":null}
{"case":"key","key":"swk_abcdefghijkmnpqrstuvwxyz234567","K":"98bfa2c5a8399cb4c76056ad0f56f2fdc325b81ea7b6f3c1323edf367f754fdb","kid":"52f6be8e5259dfef"}
{"case":"key","key":"ptk_3vq8xk2m9tz7w4hc6r5n2b8d7f4g9j3k","K":"9ea1b4fe90fa985d2b50fc444ef8941d6db25d55e41b09b8eea99a8efc84a039","kid":"bb3f97d01c274e15"}
{"case":"proof","carrier":"line","offer":1,"speaks":1,"server":"patterns:9f8e7d6c","client":"main:3f9c2a1b7d0e","kid":"code","snonce":"00112233445566778899aabbccddeeff","cnonce":"ffeeddccbbaa99887766554433221100","bind":"tls:sha256:03e9b152ca42518c28128f36def699be8aea5eec11bb9d5afc18a314f8c3eb04","K":"5155f2dfc471eeab6498c03b18e3e3df5f8c9f7a2e5f85c8ed4526aaad49e5c8","Tsha256":"0a7706734a78a999335af163a788f9ace1ac831a53d999bc45b46c72ce7986c9","clientProof":"a0622441e11e24be7577abcff6599b5b8c5031a06739a2774e94307d70b5c09f","serverProof":"a628a0aa5691349ced0d2e1d8b9b13203b55bbd3d994a9e45925b74fc1369e02","lid":"c1bb791379aa366c","enrolK":"fb7d7f2a95b74ba83c4dd61e4382fad70056f92f7267d5921471df4905c5dd44","enrolKid":"936086ee6bc93cef"}
{"case":"refuse","why":"bind","bind":"tls:sha256:8e8bc45bb14f9c7413604ef78eb1c42b465437cf700ae04a8f9553ffb0286edc","clientProof":"b1117233650616c95cb2d776c67e24aacd286f08c8a7361bbaa31c5b0fe628b6","expect":"refused"}
{"case":"refuse","why":"offer","offer":0,"clientProof":"4ac3e7c904c4e6ada67f1e283bf8eac7f21c7a797b992e43e6143fac5b262571","expect":"refused"}
{"case":"refuse","why":"role","clientProof":"a628a0aa5691349ced0d2e1d8b9b13203b55bbd3d994a9e45925b74fc1369e02","expect":"refused"}
{"case":"proof","carrier":"ws","offer":1,"speaks":1,"server":"main:3f9c2a1b7d0e","client":"patterns:9f8e7d6c","kid":"52f6be8e5259dfef","snonce":"00112233445566778899aabbccddeeff","cnonce":"ffeeddccbbaa99887766554433221100","bind":"tls:sha256:03e9b152ca42518c28128f36def699be8aea5eec11bb9d5afc18a314f8c3eb04","K":"98bfa2c5a8399cb4c76056ad0f56f2fdc325b81ea7b6f3c1323edf367f754fdb","clientProof":"a462d48f46a2cabe1d99d6ef1a96f3169621a2190beaec047a27686f8f1162e5","serverProof":"834bb29b51465d4d8cc839836a78188b25600016eb4407bd978ba6f2fd7d09d3","lid":"b4da667a1f3077c9","session":"fbba9238fff6768a7de911312c792bfe5c2f9c120df31fa0afd263f8bca2c797"}
{"case":"proof","carrier":"line","offer":1,"speaks":1,"server":"patterns:9f8e7d6c","client":"main:3f9c2a1b7d0e","kid":"bb3f97d01c274e15","snonce":"00112233445566778899aabbccddeeff","cnonce":"ffeeddccbbaa99887766554433221100","bind":"plain","K":"9ea1b4fe90fa985d2b50fc444ef8941d6db25d55e41b09b8eea99a8efc84a039","clientProof":"d64f0760b67d30c1ae6e98e337a6990767a5382a851950063e3735ad88797ba6","serverProof":"386af6ffb6c7e13e94481111eb56fde13df8993bdeadcd96411900a82ffd012d","lid":"e7bb3c6328d0766c","flags":["plain"]}
{"case":"proof","carrier":"line","offer":1,"speaks":1,"server":"playmap:0a1b2c3d","client":"patterns:9f8e7d6c","kid":"code","code":"ABCD-EFGH-JKMN","snonce":"00112233445566778899aabbccddeeff","cnonce":"ffeeddccbbaa99887766554433221100","bind":"plain","K":"2ffce7e6490a662038d6fa71cd307b0ba41f4aec0068ffd9621ac8930a08e9a4","clientProof":"228994337068a5a5c3ca67af80275b72d2480c6b7d393d9cbe94806f06b377f4","serverProof":"4bff96d7f1602241b677249e2457aefc4b1b0a9b3e9049c11725ea4cccc587f2","lid":"a512c1c5f3a901cd","enrol":false,"flags":["plain","code-plain"]}
{"case":"legacy","code":"k7qm-3xwd p9ra","nonce":"3f9a0c5e7b2d4f6a8c1e3b5d7f9a0c2e","name":"Stagewatch","proof":"f4196515d05ce2e593ac57af719ef450f4390826f1c20cce7079ac8b7ba75a13"}
{"case":"legacy","code":"abcd-efgh-ijkl","nonce":"3f9a0c1d2e4b5a69788796a5b4c3d2e1","name":"Stagewatch","proof":"7490a3de9065f4040b38fb07c1ffc489947348137fe81dca8d47f2fabd4f7f38"}
{"case":"legacy","nonce":"3F9A0C5E7B2D4F6A8C1E3B5D7F9A0C2E","prove":false}
{"case":"legacy","nonce":"3f9a0c5e7b2d4f6a8c1e3b5d7f9a0c2","prove":false}
{"case":"parse","carrier":"line","line":"LINK 1 main 3f9c2a1b7d0e FFEEDDCCBBAA99887766554433221100 code","expect":"ERR LINK malformed"}
{"case":"parse","carrier":"line","line":"LINK 1 main 3f9c2a1b7d0e ffeeddccbbaa9988776655443322110 code","expect":"ERR LINK malformed"}
{"case":"parse","carrier":"line","line":"LINK 1 main 3f9c2a1b7d0e ffeeddccbbaa99887766554433221100 52f6be8e5259dfe","expect":"ERR LINK malformed"}
{"case":"parse","carrier":"line","line":"LINK 1 Main 3f9c2a1b7d0e ffeeddccbbaa99887766554433221100 code","expect":"ERR LINK malformed"}
{"case":"parse","carrier":"line","line":"LINK 1 main 3f9c2a1b7d0e ffeeddccbbaa99887766554433221100 code extra","expect":"ERR LINK malformed"}
{"case":"parse","carrier":"line","line":"LINK 0 main 3f9c2a1b7d0e ffeeddccbbaa99887766554433221100 code","expect":"ERR LINK version"}
{"case":"parse","carrier":"line","line":"LINK PROOF a0622441e11e24be7577abcff6599b5b8c5031a06739a2774e94307d70b5c09","expect":"ERR LINK refused"}
{"case":"parse","carrier":"ws","query":"node=7a7a7a7a&t=patterns&link=1","message":{"type":"link","client":{"t":"patterns","id":"9f8e7d6c"}},"expect":{"ok":false,"class":"malformed"}}
{"case":"parse","carrier":"http","authorization":"ShowBus lid=b4da667a1f3077c9","expect":{"status":401,"relink":true}}
{"case":"parse","carrier":"http","authorization":"ShowBus lid=B4DA667A1F3077C9, s=fbba9238fff6768a7de911312c792bfe5c2f9c120df31fa0afd263f8bca2c797","expect":{"status":401,"relink":true}}
{"case":"liveness","silenceMs":6000,"state":"live"}
{"case":"liveness","silenceMs":6001,"state":"stale"}
{"case":"liveness","silenceMs":12000,"state":"stale"}
{"case":"liveness","silenceMs":12001,"state":"down"}
{"case":"redial","n":0,"j":0,"ms":400,"tol":1}
{"case":"redial","n":0,"j":0.5,"ms":500,"tol":1}
{"case":"redial","n":0,"j":0.999,"ms":600,"tol":1}
{"case":"redial","n":1,"j":0.25,"ms":900,"tol":1}
{"case":"redial","n":3,"j":0.25,"ms":3600,"tol":1}
{"case":"redial","n":4,"j":0.25,"ms":4500,"tol":1}
{"case":"redial","n":9,"j":0.999,"ms":5998,"tol":1}
{"case":"errms","rttMs":0,"errMs":1}
{"case":"errms","rttMs":1,"errMs":2}
{"case":"errms","rttMs":11,"errMs":7}
{"case":"errms","rttMs":29,"errMs":16}
{"case":"errms","rttMs":250,"errMs":126}
{"case":"clock","samples":[[1000,1505,1506,1012]],"offsetMs":499.5,"rttMs":11,"errMs":7,"apart":false,"tol":1}
{"case":"clock","samples":[[1000,1520,1521,1050],[3000,3530,3531,3100],[5000,5512,5513,5030],[7000,7600,7601,7300]],"offsetMs":497.5,"rttMs":29,"errMs":16,"apart":false,"tol":1}
{"case":"clock","samples":[[1000,1505,1504,1012]],"refused":1}
{"case":"clock","samples":[[1000,1505,1506,999]],"refused":1}
{"case":"clock","samples":[[1000,1505,1506,62000]],"refused":1}
{"case":"clock","samples":[[0,1505,1506,1012]],"refused":1}
{"case":"clock","best":{"offsetMs":2020,"errMs":16},"apart":true}
{"case":"clock","best":{"offsetMs":2010,"errMs":16},"apart":false}
{"case":"clock","best":{"offsetMs":-2017,"errMs":16},"apart":true}
{"case":"clock","best":{"offsetMs":499,"errMs":16,"ageMs":10001},"fresh":false,"fitFor300":false}
{"case":"outcome","answer":{"line":"OK @s1-a41f09c2"},"outcome":"applied"}
{"case":"outcome","answer":{"line":"OK @s1-a41f09c2 {\"stage\":\"accepted\",\"effect\":\"queued\"}"},"outcome":"accepted","words":"queued"}
{"case":"outcome","answer":{"line":"OK @s1-a41f09c2 {\"outcome\":\"Requested\"}"},"outcome":"accepted"}
{"case":"outcome","answer":{"line":"OK @s1-a41f09c2 {\"outcome\":\"Done\"}"},"outcome":"applied"}
{"case":"outcome","answer":{"line":"ERR @s1-a41f09c2 standby moved"},"outcome":"refused","words":"standby moved"}
{"case":"outcome","answer":{"http":200,"body":{"ok":true,"msg":"OK @a1-5e6f7a8b clip ends in 10 s","id":"a1-5e6f7a8b"}},"outcome":"applied","words":"clip ends in 10 s"}
{"case":"outcome","answer":{"http":403,"body":{"ok":false,"msg":"ERR not paired — this desk asks for its pairing token first"}},"outcome":"refused","words":"not paired — this desk asks for its pairing token first"}
{"case":"outcome","answer":{"http":200,"body":{"ok":true}},"outcome":"accepted"}
{"case":"outcome","answer":{"http":502},"outcome":"unconfirmed"}
{"case":"outcome","answer":{"ws":{"type":"receipt","id":"p1-0a1b2c3d","ok":true,"stage":"applied","repeat":true}},"outcome":"applied"}
{"case":"outcome","answer":{"written":true,"silenceMs":30000},"outcome":"unconfirmed"}
{"case":"outcome","answer":{"written":false,"link":"down"},"outcome":"not-sent"}
{"case":"machine","name":"silence","events":[[0,"verified"]],"expect":[[6000,{"state":"live"}],[6001,{"state":"stale","light":"amber"}],[12000,{"state":"stale"}],[12001,{"state":"down"}]]}
{"case":"machine","name":"self-gap","events":[[0,"verified"],[5000,"evaluate"],[14000,"evaluate"],[16000,"evaluate"]],"expect":[[14000,{"state":"stale","pinged":true}],[16000,{"state":"down"}]]}
{"case":"machine","name":"no reset on a bare connect","events":[[0,"connected"],[100,"closed"],[700,"connected"],[800,"closed"]],"expect":[[100,{"state":"down","waitMs":[400,600]}],[800,{"waitMs":[800,1200]}]]}
{"case":"machine","name":"reset only when stable","events":[[0,"connected"],[100,"closed"],[600,"verified"],[9000,"closed"],[10000,"verified"],[12000,"heard"],[16000,"heard"],[20000,"heard"],[20001,"closed"]],"expect":[[100,{"waitMs":[400,600]}],[9000,{"waitMs":[800,1200]}],[20001,{"waitMs":[400,600]}]]}
{"case":"machine","name":"hold survives a restart","events":[[0,"refused",{"class":"key","retry":30}],[1000,"restart"]],"expect":[[1000,{"state":"held","heldUntil":60000}],[59999,{"dial":null}],[60000,{"dial":"tls"}]]}
{"case":"machine","name":"ws stuck","events":[[0,"verified"],[1000,"frame"],[2000,"beat-stuck"],[2010,"pong"],[4000,"beat"],[9000,"frame"]],"expect":[[2010,{"state":"live","show":"stuck"}],[4000,{"show":"stuck"}],[9000,{"show":"live"}]]}
{"case":"machine","name":"line stuck","config":{"can":["stuck"]},"events":[[0,"verified"],[2000,"pong-stuck"],[4000,"pong"]],"expect":[[2000,{"show":"stuck"}],[4000,{"show":"live"}]]}
{"case":"machine","name":"line without stuck","config":{"can":[]},"events":[[0,"verified"],[2000,"pong"]],"expect":[[2000,{"show":"unknown"}]]}
{"case":"machine","name":"the pin decides","config":{"pin":"node:9f8e7d6c"},"events":[[0,"hello",{"id":"9f8e7d6c","tls":null}],[0,"dial"]],"expect":[[0,{"dial":"tls","flags":["note"],"state":"connecting"}]]}
{"case":"machine","name":"floor","config":{"floor":{"tls":true,"level":1,"proof":"mutual"}},"events":[[0,"answer",{"tls":false}]],"expect":[[0,{"state":"refused","refusal":"floor","sent":[]}]]}
{"case":"machine","name":"floor on a stripped LINK","config":{"floor":{"tls":false,"level":1,"proof":"mutual"}},"events":[[0,"answer",{"line":"ERR @l1 unknown command 'LINK'"}]],"expect":[[0,{"state":"refused","refusal":"floor","sent":["HELLO","LINK"]}]]}
{"case":"machine","name":"33rd command","events":[[0,"verified"],[1,"send",{"count":33}]],"expect":[[1,{"inFlight":32,"outcome":{"33":"not-sent"}}]]}
{"case":"machine","name":"drop after write, same acceptor","events":[[0,"verified",{"inst":"a41f09c2"}],[100,"written",{"id":"s1-11aa22bb"}],[200,"closed"],[800,"verified",{"inst":"a41f09c2"}],[810,"receipts",{"s1-11aa22bb":{"ok":true,"stage":"applied"}}]],"expect":[[200,{"outcome":{"s1-11aa22bb":"unconfirmed"}}],[810,{"outcome":{"s1-11aa22bb":"applied"}}]]}
{"case":"machine","name":"drop after write, acceptor restarted","events":[[0,"verified",{"inst":"a41f09c2"}],[100,"written",{"id":"s1-11aa22bb"}],[200,"closed"],[800,"verified",{"inst":"b7c3d9e1"}],[810,"receipts",{"s1-11aa22bb":"unknown"}]],"expect":[[810,{"outcome":{"s1-11aa22bb":"unconfirmed"},"sent":["@rq RECEIPTS s1-11aa22bb"]}]]}
{"case":"machine","name":"repeated id at the acceptor","role":"acceptor","config":{"can":["receipts"]},"events":[[0,"verified"],[10,"command",{"id":"s1-11aa22bb"}],[20,"command",{"id":"s1-11aa22bb"}]],"expect":[[20,{"runs":1,"answer":{"repeat":true}}]]}
{"case":"machine","name":"unmatched pong","events":[[0,"verified"],[0,"ping",{"t1":1000}],[50,"pong",{"t1":999,"t2":1505,"t3":1506}]],"expect":[[50,{"state":"live","samples":0,"counts":{"unmatched":1}}]]}
{"case":"machine","name":"stuck show refuses commands","events":[[0,"verified"],[1000,"frame"],[2000,"beat-stuck"],[2100,"send",{"count":1}]],"expect":[[2100,{"outcome":{"1":"not-sent"}}]]}
```

---

## 14. The numbers — rows this section appends

Kept from spec-1-3's block as written: `link.beat.ms 2000`, `link.stale.ms 6000`, `link.drop.ms 12000`, `link.redial.first.ms 500`,
`link.redial.max.ms 5000`, `ping.min.ms 1000`, `ping.max.ms 2000`, `clock.apart.ms 2000`, `receipt.deadline.ms 3000`,
`request.id.chars 32`.

```showbus
peerlink.version 1
peerlink.label showbus-peerlink-1
peerlink.feature final
peerlink.feature receipts
peerlink.feature stuck
peerlink.feature expire
peerlink.feature enrol
peerlink.can.words 8
peerlink.nonce.bytes 16
peerlink.kid.bytes 8
peerlink.lid.bytes 8
peerlink.key.bits.min 128
peerlink.handshake.ms 10000
peerlink.unpaired.per.address 4
peerlink.wrong.proofs.per.minute 5
peerlink.hold.refused.ms 60000
peerlink.hold.disconnect.ms 30000
peerlink.cert.probe.ms 60000
peerlink.stable.ms 10000
peerlink.redial.jitter.pct 20
peerlink.pings.matched 8
peerlink.peer.sha256 <64 hex>
link.check.max.ms 1000
link.selfgap.ms 2000
link.connect.timeout.ms 5000
link.inflight.max 32
link.replies.max 256
link.write.deadline.max.ms 10000
link.command.bytes 65536
link.read.min.bytes 262144
link.ws.in.bytes 4096
link.sidecall.inflight 1
link.sidecall.timeout.ms 4000
clock.window 8
clock.rtt.max.ms 60000
clock.fresh.ms 10000
show.stuck.min.ms 1000
show.stuck.max.ms 10000
receipt.final.ms 30000
receipt.late.ms 1000
receipts.kept 256
receipts.kept.ms 600000
receipts.ask.max 32
cmd.expire.max.ms 10000
report.retry.first.ms 3000
report.retry.max.ms 30000
report.retry.for.ms 1800000
log.repeat.summary.ms 60000
```

Where each number comes from:

- 2/6/12 s, 0.5→5 s and ±20 %: spec-1-3 and three apps today (`ShowBusNetwork.cs:16-37`).
- 32 in flight and 30 s final: playout-links D5.
- 256 replies and 10 s writes: `Patterns/src/Patterns.App/Services/WirePeer.cs`, `ConnectionLedger.cs:58-66`.
- 64 KiB command lines: `ControlService.cs:731`.
- 256 KiB reads: playout-links D7 (`PatternsLink.cs:40`).
- 4 KiB into `/ws`: `WebServer.cs:28`.
- 4 s side calls: `StagewatchLinkService.cs:289`.
- 60 s rtt cap: `Resilience.cs:256`.
- 60 s and 30 s holds: peers-not-screens D12; agmessage D8 (`StagewatchLink.cs:59`).
- Five wrong proofs a minute: today's five (`ControlService.cs:541`; `ConnectionLedger.cs:65`).
- 10 s handshake and stable reset: security and adoption.
- 10 s fresh, errMs, self-gap: reliability.
- 10 minutes of receipts: covers a laptop lid closed and a redial.

**Words handed to the rest of the master** (spec-1-3's single edit):

- §1 rule 3 gains "and a command is never applied twice (§13.12.6)".
- §3 gains `final`, `repeat`, `lateMs` and `frame` as the last fields of a receipt, and the outcome table's reference.
- §4's liveness paragraph becomes "A link follows §13.10; nothing is queued while a link is down (§13.15.5)".
- §5 gains "its quality: §13.11".
- §7 gains "app links prove keys by §13.6; a PIN, viewing code or view pass is never an app link's credential".
- §7.2's pre-pairing words gain `LINK`.
- §8's rows gain, per node:
  - its scope map;
  - its show writer, `stuckMs` and the measurement (or "`stuck` not offered: no measurement yet");
  - `expire` (not offered until measured);
  - the doors of 13.18 it has, with what each leaves open.
- §10's "Line protocols" bullet is replaced by 13.8.5.

---

## 5. The Eye and every reader (layer 4, Patterns first)

Today the Eye draws another show app as a node, grey when heard, amber when stale, red when gone, with its context's items one tier
out (`Patterns/src/Patterns.Core/Services/Eye.cs:771-820`). The linked one has a `Follows` edge, the others a `Hears` edge (`:794-795`).
STATE's `bus.stagewatch` carries state, light, clock and words (`Patterns/src/Patterns.App/Services/CommandRouter.cs:755-778`).

With PeerLink:

- **The edges Patterns holds** (its Stagewatch link; its Show Bus node devices to PlayMap) carry the link record.
  - The light and mark come from 13.16; the words are the record's.
  - The edge shows the carrier, TLS, scope and level ("PeerLink 1 · TLS · scope post").
  - Show liveness is drawn on the app node: amber link, red values, "its show is stuck — no time shown".
- **The connections Patterns accepts** on 9697 become named app nodes, not just "Wire clients" (`Eye.cs:702-703`). Each comes from the
  acceptor's list (13.16.5): verified id and kind, scope, level, seen-in-clear.
- **The links Patterns does not hold** are drawn one hop out from each linked app's context.
  - Stagewatch's `link:patterns` and `link:stage-left` items and their `links` rows draw "Stagewatch → PlayMap Stage left: legacy,
    plain", with that app's own light.
  - A node seen only in a context's `peers` is grey "heard, not linked".
  - Nothing is inferred: a link no context names is not drawn.
- **Right-click on an edge** gives *Link again now*, *Accept certificate*, *Lower floor*, *Accept new id*, and on an acceptor-side node
  *Revoke key* and *Let this key speak for…*.
  - These are the same verbs as the desk's buttons, the wire and the Companion module, journalled.
  - The assistant reads the record in its facts and never proposes these verbs (patterns-playmap-side D16).
- **Super Check** gains one row, "Show network links": each link's state and flags, and any key seen in clear.

---

## 6. Per app, per link: today and the changes, in each repo's own process

### 6.1 Stagewatch (its release train with playout-links, peers-not-screens and spec-1-3)

**Its links.** It has a `line` connector (`PatternsLink` → `PlayoutLink`) and a `ws` acceptor (`/ws` and HTTP).

**Today:**

- **Connector** (`Stagewatch/src/Timer.Integrations/PatternsLink.cs`):
  - plain TCP only (`:192-199`);
  - after 1.5 s with no NONCE it sends plain `AUTH <token>` to any host (`:280-284`; G08), and proves any nonce of 1–128 characters
    (`:311`);
  - STATE is taken before pairing (`:357-364`);
  - any `OK PONG` is a sample (`:318-327`);
  - ids `s<n>` per run (`:167`); pending receipts evicted past 64 (`:169`); a drop fails every pending command (`:225-226`);
  - redial 250 ms doubling, no jitter, reset on connect (`:189,209,229-230`);
  - commands written from the show thread (`:172-176`).
- **Acceptor** (`Stagewatch/src/Timer.Remote/WebServer.cs`):
  - TLS sniffed (`:300-318`); plain allowed from the LAN unless Require HTTPS, which is off by default (`:325-331`;
    `AppSettings.cs:261`);
  - bearer or `?pin=` (`:1059,1150-1151`);
  - unknown keys counted with wrong PINs (`:1077-1082`);
  - stuck beat (`:1265-1271`); pong with t2 = t3 (`:1349-1355`);
  - late commands run silently (`MainViewModel.Receipts.cs:21-27`).

**Changes (units numbered by its maintainer):**

- **SW-PL0, with playout-links' `PlayoutLink` (its D2–D5, D7 kept).** Hygiene, no wire change:
  - never plain AUTH off loopback; prove only a 32-hex NONCE; no STATE or fact before `OK paired`;
  - matched pongs; ids `s<n>-<inst>`; five outcomes by 13.12.3 (a desk older than 1.1 is unconfirmed, the reliability correction);
  - 32 in flight; the show thread never touches the socket;
  - ±20 % jitter; reset only after verified + 10 s; persisted holds;
  - one journal row per command and one per state change;
  - a `TimeProvider` seam.
- **SW-PL1.** `Timer.Core/PeerLink.cs`, pure:
  - canon, K, kid, the transcript, proofs, lid and S;
  - liveness with self-gap, clock quality, redial, the outcome table, the state machine on injected time and j.
  - A `PeerLinkVectors` group in `tests/Stagewatch.Tests` reads the ```peerlink block from `docs/SHOWBUS.md`.
- **SW-PL2, with peers-not-screens SW-3.** The acceptor speaks LINK on `/ws?…&link=1`:
  - the verdict carries proof, lid, level, can and inst;
  - `Authorization: ShowBus lid, s` on `/api/context`, `/api/peers/context`, `/api/proposals*`, `/api/message/*` and result routes;
  - a wrong-proof table per (address, kid), apart from the PIN's; unknown kids uncounted (peers-not-screens D5);
  - Host/Origin checked before loopback trust;
  - seen-in-clear on a bearer key arriving plain from the LAN (windows-for-peers D4 generalised: no grants);
  - live revocation ends sessions;
  - features `final` (playout-links D5), `receipts` (store, repeat, `{"cmd":"receipts"}`, `@rq RECEIPTS` on Companion TCP) and
    `stuck` (the existing beat; `stuckMs` 3000 is Stagewatch's shipped `StuckAfter`, `ShowState.cs:217`, named in its §8 row);
  - `lateMs` in receipts;
  - a line of words before shedding a connection (today closed silently, `:278-281`).
  - Settings says, when HTTPS is off, that other apps cannot link with keys.
- **SW-PL3.** `PlayoutLink` speaks LINK to Patterns and PlayMap when they offer it:
  - TLS to PlayMap's `playmap.lines.v1` (playout-links D4) and to 9697 once Patterns sniffs it; floors;
  - an issued `ptk_`/`pmk_` key, or enrolment over TLS where offered;
  - `AT` only on a fit clock over TLS or local (playout-links D8 narrowed);
  - the record into its context items `link:<name>`, the frame's playout row and the copilot's facts;
  - trust verbs as `ShowCommandKind` appended, parser verb, help line, `Copilot.Classify` High, never in `ShowContext.Commands`.
- **SW-PL4, with spec-1-3 S1/S8.** The master:
  - §13 and §14's rows in `docs/SHOWBUS.md`;
  - `tools/showbus/peerlink-peer.mjs` and its hash;
  - `tools/showbus-copies.sh` compares the peer's hash across the four trees.

### 6.2 Patterns (rounds; one commit per unit, `Round NN.k`; the maintainer's numbers win; patterns-playmap-side's map is used)

**Its links.** It has a `ws` connector (`StagewatchLinkService`), a `line` acceptor (9697) and HTTP, a `line` connector (the Show Bus
node device, Round 92), and the twin (exemption).

**Today:**

- **Connector**:
  - TLS only when the hello names it (`StagewatchLinkService.cs:238-242`); a typed address dials TLS only with a pin held (`:235`);
  - bearer key on plain (`:269-276`); fixed `cid=patterns` (`:276`);
  - keyless pin on the answer (`:284`);
  - attempt reset on any return (`:184-185`);
  - beat behind side calls (`:324-355`); stuck ignored (`:521-538`);
  - results once, ran-ids cleared at 1000 (`:460-465`);
  - static `ShowClock` (`:79-80`).
- **Acceptor**:
  - STATE before NONCE (`ControlService.cs:356-363`); queries unpaired (`:501`);
  - 12 h OSC trust from a proof (`:439,556-560`);
  - `X-Patterns-Token` is the token (`:978-981`);
  - `/api/cmd` id only from the body's `@id`, answer `{ok,msg,id}`, cue verbs need `X-Patterns-Client` (`:1337,1355,1365-1368`).
- **Core**:
  - `PairingToken.Normal` strips five characters (`PairingToken.cs:54-63`);
  - `LinkClock.Sample` clamps a negative rtt (`LinkClock.cs:59-63`) and `Apart` uses `>=` (`:97`).
- **Twin**:
  - on `IPAddress.Any` (`TwinService.cs:488,512`); `SendSecrets` true (`ShowState.cs:2080`).

**Changes:**

- **Round 89 (as designed by patterns-playmap-side, decisions 2–4).** The stuck flag with one writer; a pong never clears it. This is
  13.10.5 on `ws`, reaching every reader in the round.
- **Round 90 (its 1.3 contract round) gains a unit "PeerLink core and the Stagewatch link's hygiene":**
  - `Patterns.Core/Services/PeerLink.cs` (no package references) and `PeerLinkVectorsTests` in Core.Tests, reading the block from its
    `docs/SHOWBUS.md` copy;
  - `PairingToken` canon by 13.5.1, refused at the Remote page and at NEW TOKEN when not `[A-Z0-9]`;
  - `LinkClock` admission by 13.11.2, and `Apart` as |offset| − err > 2 s;
  - in `StagewatchLinkService`:
    - TLS by pin and probe, never by the hello; no key plain off the machine ("Hall A offers no TLS — the key stays here");
    - `/ws?node=<ShowBusId>&t=patterns` (peers-not-screens PT-1);
    - the legacy-bearer confirm rule (13.6.8);
    - reset only after verified + 10 s; persisted 60 s holds;
    - the side-call lane (beat and drop on their own timer);
    - matched pongs; results retried 3→30 s for 30 min, written ahead; a bounded LRU of desk ids;
    - a `TimeProvider` with `ShowClock` as its default.
  - Every reader in the same unit:
    - STATE `bus.stagewatch` appends the record's fields at the end of its row;
    - the Eye's edge (section 5), the Nodes card, the Run strip;
    - right-click and wire verbs `STAGEWATCH LINK AGAIN`, `STAGEWATCH ACCEPT <8 hex>` (patterns-playmap-side D15) and
      `STAGEWATCH FLOOR LOWER`;
    - Companion variables `stagewatch_link_state`, `stagewatch_link_flags`, `stagewatch_stuck`;
    - the assistant's facts; the Super Check row "Show network links"; one journal row per transition.
- **Round 90's twin unit.** Twin unilateral fixes (L55's twin half stays open):
  - the twin honours the bind address;
  - `SendSecrets` defaults off, and is refused on a plain twin link to another machine, with words ("device passwords stay on this
    desk — type them on the standby");
  - a QUALIFICATION §22 walk row, unwalked until filled.
- **Round PL (after 92; its number is the maintainer's): Patterns as an acceptor.**
  - TLS sniffed on 9697 (closes OPEN L55's wire half; decks stay plain with their legacy proof).
  - `LINK` / `LINK PROOF` (REMOTE.md; `LINK` added to the pre-pairing words with Round 90.2's reads-after-pairing).
  - An *App links* table on the Nodes page:
    - `ptk_` keys bound to a connector id, with scope follow/drive/operate (its §8 map), last use, issued or enrolled, seen in clear,
      Revoke;
    - kept DPAPI-protected beside the TLS key, never in the show;
    - `KEY MAKE`, `KEY REVOKE`, `KEY BIND` verbs, catalogued, never proposed.
  - Enrolment from the pairing token over TLS; NEW TOKEN revokes enrolled keys.
  - Features `final` (when `CUE GO`'s Requested gains a later Done) and `receipts`.
  - `stuck` **not** offered: "no measurement this round". The Super Check records the desk thread's stall distribution, so a later
    round sets `stuckMs` (LP-1).
  - `StagewatchLinkService` speaks `link=1` and S.
  - The Show Bus node device (Round 92, decision 19) is built on the same core: LINK to PlayMap when offered, legacy AUTH-PROOF
    otherwise; ids `d<n>-<inst>`.
  - The Companion module: never plain AUTH off loopback (`integrations/companion-module-patterns/src/main.js:196-204`), in its own
    module round.
  - **For the maintainer's ADR:** end the 12 h OSC carry-over from a wire proof (13.18); OSC ALLOW stays the operator's list.
- **Process.**
  - Every loopback test sets `ControlService.TrustLoopback = false` and restores it in `finally`.
  - New STATE fields go at the end of their rows.
  - The papers: PLAN §, REVIEW, CHANGELOG, README, REMOTE.md, COMPANION.md, QUALIFICATION, OPEN (close L55's wire half; add rows for
    what is not done).
  - Each round closes with a Super Check row for its verb.
  - All seven suites and the module's tests are green before a push.

### 6.3 PlayMap (Mapper; round-3 ownership)

**The process gate.**

- `src/PlayMap.Net/Links/**`, `src/PlayMap.Net/Wire/**`, `Net/Http/HttpHost.cs` and `Core/Bus/Pairing.cs` are named for no wave: "nothing
  in that wave edits it" (`Mapper/docs/round3/PLAN.md:2123-2125`).
- So PlayMap's half is **one exception request** quoting §4 (the patterns-playmap-side decision 6 precedent). If declined, it lands at
  round 3's integration pass, and until then every peer sees PlayMap at level 0 (amber `legacy`).
- Every new verb is a `VerbCatalog` row, every new STATE block has its fixture written by a test and a card descriptor, and every desk
  light carries its `StatusMark` shape.

**Today:**

- **`StagewatchLink`**:
  - TLS only when the hello names `tls` and `https` (`StagewatchLink.cs:66`); a typed address always plain (`NetworkHost.cs:718-735`);
  - bearer (`:278-279`); keyless pin on the upgrade (`:296-302`);
  - a plain beat clears stuck (`:381`);
  - side calls in the beat loop (`:438-475`); reset on return (`:236-237`);
  - `_ran` cleared at 1000 and results once (`:538-552`);
  - the key in plain `playmap.node.json` (`Pairing.cs:121-123`).
- **`WireClient`/`PatternsLink`**:
  - no answer or a drop = failure (`WireClient.cs:158-165`); any NONCE proved (`:314`); ids `pm<n>` per run (`:153`); reset when the
    peer "said anything" (`:247`); a 5-word PONG dropped (`:384`);
  - facts taken before pairing (`PatternsLink.cs:218-247`).
- **Acceptor**:
  - NONCE first (`WireSession.cs:180-181`); the strict WS channel (`:27,229-233`); TCP STATE on HELLO before pairing (`:263-276`);
  - trusted addresses pair without proof (`Pairing.cs:113-115`).
- **Core**:
  - `Liveness.Of` uses `<` (`ShowBus.cs:226-229`).

**Changes:**

- **PM-PL0 (the exception request; the smallest safety set):**
  - `StagewatchLink`:
    - TLS by pin and probe, a typed address trying TLS first (G31, patterns-playmap-side D20); no key plain off the machine; the
      legacy-bearer confirm rule;
    - the key moved to `NodeCredentialStore` (`Mapper/src/PlayMap.Engine/Devices/NodeCredentialStore.cs:9-20`);
    - `paired` from the verdict (peers-not-screens PM-1);
    - only a frame clears `_stuck`; reset only after verified + 10 s.
  - `WireClient`:
    - unconfirmed, never failed, for a written command; prove only a 32-hex NONCE; ids `pm<n>-<inst>`; reset only after verified +
      10 s;
    - `PatternsLink` takes no STATE or fact before `OK paired`.
- **PM-PL1:**
  - `PlayMap.Core/Bus/PeerLink.cs` (runtime only) and `PlayMap.Core.Tests/Bus/PeerLinkVectorsTests.cs` reading the block;
  - `Liveness.Of` inclusive;
  - `ClockOffsetEstimator` refuses stamps ≤ 0 and rtt > 60 s;
  - `Pairing.Normalise` by 13.5.1, with entry refusal.
- **PM-PL2: the acceptor.**
  - LINK on 9941 and `playmap.lines.v1` (`BeforePairing` gains `LINK`); TLS sniffed on 9941.
  - A `pmk_` key table with scopes mapped to `ShowPolicy` roles, in `NodeCredentialStore`.
  - `TrustedAddresses` kept only for legacy Lines devices, amber "trusted by address — not a proof", never for app links.
  - Features `final` (verbs whose first answer is `stage:accepted`) and `receipts`; `stuck` not offered until the engine control lane's
    stall is measured (a lab line, "not run").
  - `VerbCatalog` rows for `LINK`, `RECEIPTS`, `KEY …`.
  - STATE `clients` rows append `level`, `verified`, `scope`, `t`, `id`; fixtures regenerated.
  - Reads after pairing on TCP 9941 go to the owner again (spec-1-3 decision 7).
- **PM-PL3: the links speak LINK.**
  - `StagewatchLink` with `link=1` and S; `PatternsLink` with LINK when Patterns offers it.
  - STATE `links.stagewatch` / `links.patterns` append the record (G28's clock with errMs and ageMs).
  - Preflight and the desk's lights with shapes.
  - The trust verbs are catalogued and on the menu bar and right-click, never typed-only.
- The desk↔engine `WireClient` path keeps working at level 0 on loopback (13.18).

### 6.4 AGMessage (the 0.5.0 branch's owner; node:test)

**Its links.** It has a `ws` connector (`StagewatchLink`), and a reader today, a `line` connector later (`PeerLink`).

**Today:**

- **`StagewatchLink`**:
  - TLS by URL, hello or a typed fingerprint, else plain with the bearer key (`nodes.js:255,306,326`);
  - fixed `cid=agmessage` (`:323`);
  - "connected" on open and the pin confirmed on any state frame (`:328-335`);
  - delay reset on open (`:329`);
  - a pong refreshes while stuck (`:333-334`);
  - `Date.now()`;
  - "not sent" after a POST timeout (`:383-389`).
- **`PeerLink`**:
  - polls `/api/context` with Patterns'/PlayMap's **operator token** in a header, plain when no TLS is named (`peerlinks.js:19-23,108-121`);
  - sends low/review verbs to `/api/cmd` with that token (`:145-153`);
  - `readReceipt` misreads Patterns (`:62-71`);
  - this contradicts agmessage D10 ("too strong for a messenger").

**Changes:**

- **AG-PL0, before 0.5.0 ships:**
  - `PeerLink` sends the token header only to loopback, or over TLS to a certificate already pinned or confirmed by a person (13.6.8);
    otherwise `refused` "this node offers no TLS — AGMessage will not send its pairing token in clear".
  - The direct command path is off while the only credential is the operator's token: a line goes to the desk as a proposal (§11), or,
    with no desk, is refused "Patterns gives AGMessage no key of its own yet".
  - `readReceipt` by 13.12.3; the fake Patterns in `test/helpers.js` answers Patterns' real `{ok,msg,id}`.
  - The admin page says AGMessage holds Patterns' operator token, amber, until an app key replaces it.
- **AG-PL1:**
  - `app/lib/peerlink.js`, pure, with `app/test/peerlink.test.js` reading the block from its `docs/SHOWBUS.md` copy (spec-1-3's copy
    chain);
  - in `StagewatchLink`:
    - TLS by pin and probe; no key plain off the machine; the legacy-bearer confirm rule;
    - `node=<id>&t=messenger` (agmessage D7, D8);
    - reset only after verified + 10 s;
    - a pong never clears stuck, and the board shows "?" and "Stagewatch's show is stuck";
    - `performance.now()` for liveness, with t1/t4 from `timeOrigin + performance.now()`;
    - ids `ag<n>-<inst>`; unconfirmed after the write; matched pongs;
    - injected now, timers and j in tests.
- **AG-PL2:** `link=1` LINK and S to Stagewatch, when it offers them.
- **AG-PL3, after Patterns' and PlayMap's acceptor units:**
  - `PeerLink` becomes a `line` connector: Node `net`/`tls` to 9697 or 9941, or `playmap.lines.v1` over pinned WSS;
  - it proves an issued key (scope `follow`, or `drive` where the node's operator grants it);
  - no token anywhere; its settings drop the stored operator token;
  - low/review verbs listed by the node and allowed by the scope go with receipts read by 13.12.3; high verbs are still only proposed;
  - `CONTEXT` every 5 s after LINK; STATE lines counted and dropped unparsed (Patterns has no `SUBSCRIBE OFF`; bandwidth measured in the
    interop lane first).

---

## 7. The conformance kit

**The vectors live in the spec.**

- The ```peerlink block in §13.20 sits inside the hashed body, so spec-1-3 decision 10's copy fence holds it byte-identical in every tree
  with no new mechanism.
- Its rows are crypto, parse, pure (liveness, redial, errMs, clock, outcome) and machine rows (events on injected time).
- The crypto rows were computed with Python's stdlib and cross-checked in Node 22. The security draft's rows were recomputed by both
  judges and again here.
- **The machine rows are normative data, not code.** Each tree drives its own pure link machine with them, with no shared interpreter
  (judges 1 and 2 reject two).

**How each repo consumes them:**

| tree | pure core | vectors and machine rows | socket scenarios (the scripted peer) | CI |
|---|---|---|---|---|
| Stagewatch | `src/Timer.Core/PeerLink.cs` | a `PeerLinkVectors` group in `tests/Stagewatch.Tests`, reading `docs/SHOWBUS.md`'s block; `tools/test.sh unit` | `node tools/showbus/peerlink-peer.mjs` spawned by the suite against `PlayoutLink` and `WebServer`, on an injected `TimeProvider` | Node 22 is already in the job (`.github/workflows/ci.yml:36-38`) |
| Patterns | `src/Patterns.Core/Services/PeerLink.cs` | `tests/Patterns.Core.Tests/PeerLinkVectorsTests.cs` (xUnit theories over the block) | `tests/Patterns.App.Tests/PeerLinkWireTests.cs` spawns the peer against `StagewatchLinkService`, 9697 and the node device; `TrustLoopback = false`, restored in `finally` | Node is only in the module job (`.github/workflows/build.yml:76-78`); the `test` job gains `setup-node` |
| PlayMap | `src/PlayMap.Core/Bus/PeerLink.cs` | `tests/PlayMap.Core.Tests/Bus/PeerLinkVectorsTests.cs` | `PlayMap.Net.Tests` spawns the peer against `StagewatchLink`, `WireClient` and `WireSession`, on `tests/Shared/ManualTime.cs` | Node 22.20.0 in the same job (`.github/workflows/build.yml:149-151`) |
| AGMessage | `app/lib/peerlink.js` | `app/test/peerlink.test.js` (node:test) | the peer imported in process | Node |

Each tree's ShowBusSpec test (spec-1-3 decision 10) already extracts ```showbus. It extracts ```peerlink the same way, and checks the
peer's SHA-256 against `peerlink.peer.sha256`.

**The scripted peer** is `tools/showbus/peerlink-peer.mjs`: one dependency-free Node 22 file (`node:net`, `node:tls`, `node:crypto`,
`node:http`, a minimal WebSocket framer), byte-identical in all four trees.

- **What it plays.** Either end, on `line` (TCP, TLS, or `playmap.lines.v1` over WS), on `ws` (Stagewatch's node link) and on side calls,
  honestly or as an attacker.
- **How it is driven.** Over newline-delimited JSON on stdin/stdout by the host test: `listen` / `dial`, `send`, `expect` (≤ 2 s real
  time), `silence`, `close`.
- **Secrets.** Certificates and keys come from 0600 files, never argv.
- **Time.** The link under test keeps its injected clock, which the host advances. The peer has no clock of its own.

**Scenarios:**

| id | scenario |
|---|---|
| S1 | honest link, each carrier and role |
| S2 | wrong key → constant words; `line` closed, `ws` a public viewer; held 60 s; attempt not reset |
| S3 | unknown kid → the same words as S2, not counted |
| S4 | another certificate at first use (MITM) → the proof fails, nothing pinned, no key, code or S on the wire |
| S5 | a pinned id and a forged hello without `tls` or at another address → TLS dialled anyway; a note; no refusal |
| S6 | CERTIFICATE CHANGED → nothing of the connector's sent; public view only; a probe at most every 60 s |
| S7 | LINK stripped (`unknown command`) on a floored id → floor-refused; never AUTH-PROOF or AUTH sent |
| S8 | a replayed client proof or reused nonce → refused |
| S9 | a server proof offered as a client proof → refused |
| S10 | S replayed in clear from another machine → refused and the link ended; S after the link closed → 401 relink; a wrong lid → 401 |
| S11 | silence: stale at 6001, down at 12001; redials within ±20 %; the self-gap |
| S12 | stuck: the `ws` beat; the `line` `PONG … STUCK` with the feature; a pong never clears it on `ws` |
| S13 | a forged or unmatched pong → no sample; a clock over a plain link never schedules `AT` |
| S14 | receipts: applied, accepted→final, refused, drop→unconfirmed, 30 s→unconfirmed, the 33rd not-sent; RECEIPTS with the same inst and a new one; a repeated id answered `repeat` and run once |
| S15 | an oversize message → counted, the link up; a slow reader → one push per kind, replies cut at 256 |
| S16 | an unpaired flood → ≤ 4 per address, a 10 s close |
| S17 | a key revoked while live → refused at once, session ended |
| S18 | a key bound to another connector → a binding refusal, not counted |
| S19 | canary secrets: none of the key, code, K, S, proofs or nonces in any captured log, journal, STATE, context, report or error |
| S20 | loopback trust through a local forwarder or a foreign `Origin` → not trusted |
| S21 | enrolment over TLS where offered; a code over plain → no enrolment; NEW TOKEN revokes enrolled keys |
| S22 | a level-0 acceptor with Patterns 88.1's real grammar (STATE, then NONCE) → `legacy` amber, AUTH-PROOF only on a 32-hex nonce, no plain AUTH off the machine, nothing taken before `OK paired` |

**What each link must pass:**

- **Every connector** passes S1, S2, S4–S7, S11–S14, S17–S19 and S22, on each carrier it uses, plus S21 where it enrols. The connectors:
  - Stagewatch's `PlayoutLink`;
  - Patterns' Stagewatch link and Show Bus node device;
  - PlayMap's Stagewatch and Patterns links;
  - AGMessage's Stagewatch link and, from AG-PL3, its `PeerLink`.
- **Every acceptor** passes S1–S3, S8, S9, S12 (emitting), S14 (for the features it offers), S15–S20, S10 (Stagewatch) and S21 (where it
  offers `enrol`). The acceptors:
  - Stagewatch `/ws` and its side calls;
  - Patterns 9697;
  - PlayMap 9941 and `playmap.lines.v1`.
- **The Patterns twin** runs only the `clock` rows and S19 (a named exemption).

**Cross-app rows** are handed to the ecosystem lane (`design/ecosystem-lane.md`). They run the real apps on its one-kernel LAN, under
its deadline rule (contract number × 1.5 + 2 s), in the LAN, laptop and offline topologies.

- S7.1 "no secret anywhere" and S7.4 "no app's address counted" become PeerLink rows.
- Per real pair: "<connector> links to <acceptor> over <carrier>: verified both ways, pinned, floor set".
- A stuck rig: every follower shows no time within stuckMs + 4 s, and every link stays live.
- The rig killed: first redials spread over 400–600 ms; every link live within 6 s of the restart; no address in a lockout table.
- A pulled cable with a GO in flight: unconfirmed → RECEIPTS → applied; the GO ran once.
- Mixed levels: one tree pinned at a level-0 commit, so its links show `legacy` and nothing breaks.

---

## 8. Migration

**Older peers keep working.**

- Through 1.3 every node keeps its 1.2 doors: the line wires' NONCE / AUTH-PROOF, bearer keys over TLS or loopback, Companion decks'
  legacy proof.
- **A 1.3 connector and a 1.2 acceptor:** the connector tries LINK and gets `ERR … unknown command` / `not paired`, or a frame first on
  `ws`.
  - With no floor for that id, it falls back: `legacy` amber, every unilateral rule still on.
  - The bearer key goes only to a pinned or confirmed certificate.
- **A 1.2 connector and a 1.3 acceptor:** it works as before, except:
  - its bearer key arriving plain from the LAN is marked *seen in clear* (amber; replace it);
  - Patterns' and PlayMap's reads wait for pairing once their reads-after-pairing units land (spec-1-3 decision 7, a sibling's change).
- **Bytes never change at level 0:** HELLO, NONCE, AUTH-PROOF, four-word PONG, `OK/ERR @id`, receipt JSON, and the `/ws` query keys old
  acceptors read. The fifth PONG word, `final` lines and `repeat` reach only a connector that ran LINK.

**Order.** Each step stands alone, and nothing waits for another app except where named.

| phase | what | who, in their own process | refused from then on |
|---|---|---|---|
| 0 | the words and the kit, no behaviour change | spec-1-3's master edit (S1/S8) with SW-PL4; each tree's copy by its own chain | nothing |
| 1 | unilateral connector hygiene | AGMessage **AG-PL0 before 0.5.0 ships** (most urgent: the operator token in clear); Stagewatch SW-PL0 with `PlayoutLink`; Patterns Rounds 89 and 90; PlayMap PM-PL0 by exception (or the integration pass); AG-PL1 | by each connector, from its release: plain AUTH off this machine; a key or token over plain off this machine; a secret to a certificate neither pinned nor confirmed; a plain dial to a pinned id; a pin set before an authenticated answer; a NONCE that is not 32 hex proved; a token with a non-`[A-Z0-9]` character at entry |
| 2 | the hub speaks LINK | Stagewatch SW-PL1 and SW-PL2 | nothing new (seen-in-clear flagged) |
| 3 | connectors link to the hub by proof | AG-PL2; Patterns Round PL's link unit; PlayMap PM-PL3 | per id, once verified: floors refuse legacy or plain to that id (the public view kept) |
| 4 | Patterns and PlayMap as acceptors | Patterns Round PL (TLS on 9697, LINK, keys, enrolment); PlayMap PM-PL2; then Stagewatch SW-PL3, PlayMap PM-PL3's Patterns link, Patterns' node device and AG-PL3 link by proof | enrolment over plain; Patterns' twin secrets over a plain link; AGMessage holds no operator token |
| 5 | measured features | `stuck` (LP-1) and `expire` on line acceptors once each §8 row names a measured number | — |
| 6 | Show Bus 1.4 flips the defaults | every node | bearer keys arriving plain from the LAN; legacy 1.1 proofs from other machines for app links (decks excepted until their modules adopt LINK); plain `line` app links off this machine where TLS is offered; a node link without LINK is served as a screen |

The fail-closed costs, said in each app's papers:

- A Stagewatch with HTTPS off cannot hold keyed links from other machines. HTTPS is on by default (`AppSettings.cs:259`), and the words
  say how to turn it on.
- AGMessage 0.5.0 drives Patterns and PlayMap only through the desk until they issue it a key.
- A legacy bearer at first use needs one confirm press.
- A deliberate rollback of a peer meets its floor until one press.

---

## 9. Risks

1. **Trust on first use remains** for keyless links, and for a legacy bearer confirmed carelessly. The short fingerprint is shown on
   both screens, but nothing forces a person to compare it.
2. **Code proofs on plain carriers are an offline target** until 9697 and 9941 offer TLS (OPEN L55): about 2^59 for a made Patterns
   token, trivial for a typed short one, which Patterns' free text box allows (`WebSection.axaml:38`). Issued keys are not a target;
   the flag `code-plain` says so.
3. **Four implementations in two languages.** Mitigated:
   - only HMAC-SHA256, SHA-256 and platform TLS;
   - vectors in the spec, where Python and Node agree;
   - one Node peer in every tree.

   A PAKE was declined: neither .NET nor Node ships one.
4. **The binding is tls-server-end-point, not a TLS exporter** (.NET exposes none). It is weaker than session binding, and sufficient,
   because a middle machine never holds the acceptor's private key.
5. **S is a bearer inside pinned TLS.** A leak lasts as long as the link; it is never logged (the canary fence).
6. **Address spoofing on a shared L2 segment** can lock an (address, kid) pair for a minute. Denial of service and traffic analysis are
   not addressed.
7. **Receipt-store dedupe makes id uniqueness a safety property.** A connector that reused ids across restarts would have a command
   answered from an old record. Ids carry `inst`, and machine rows hold it.
8. **`unknown` after an acceptor restart leaves a command unconfirmed.** That is honest, but a person must look at the peer's state;
   an alarm-fatigue risk is answered by "seen" rows when a reader sees the effect.
9. **The twin's secrets refusal can leave a standby without device passwords after a takeover.** It touches takeover, a platform path:
   unwalked until its QUALIFICATION row is filled. Twin TLS is later.
10. **Mapper's round-3 ownership** may hold PlayMap at level 0 until the integration pass. Typed Stagewatch links stay plain-keyed (G31)
    unless the exception is granted.
11. **New shapes on Patterns' and PlayMap's grammar** (`LINK`, `RECEIPTS`, the fifth PONG word, `final` lines) reach only LINK
    connectors.
    - The Companion modules' handling of an unknown untagged push is checked in each module's tests.
    - PlayMap's shared `WireClient` also carries its desk↔engine link, which must keep working.
12. **The peer and the block are copy-fenced artefacts.** A stale copy is seen only by `tools/showbus-copies.sh` in the interop lane,
    which has no runs yet (G44).
13. **Unmeasured:**
    - the handshake cost (expected negligible);
    - the side-call burst after a hub restart against Stagewatch's 64 slots (LP-2);
    - each show writer's honest stall (LP-1: `stuckMs`, `expireMs`);
    - sleep and resume against the self-gap rule (LP-3).

    No governor lands before its measurement.
14. **More amber during migration** (`legacy`, `plain`, `keyless`, `first`) risks alarm fatigue. Views group flags per peer, and only
    links the show needs can turn red.
15. **Patterns' CI `test` job has no Node.** Adding `setup-node` is a CI change in its round.
16. **Numbering.** This is §13 as the task asks; moving spec-1-3's numbers to §14 renames 23 references in five unlanded designs. If its
    owner keeps §13, this text drops in as §14.
17. **Patterns' 12 h OSC carry-over** (`ControlService.cs:556-560`) stays until the maintainer's ADR ends it.

---

## 10. What this design does not do

- No `http` carrier, no per-request MAC, no `twin` carrier, no PROVE or PEERLINK verbs.
- No `stuckMs` or `expireMs` for Patterns or PlayMap: no measurement this round.
- No TLS on the twin, and no STARTTLS on Stagewatch's Companion port.
- No change to discovery, reads after pairing (spec-1-3), node-link admission (peers-not-screens), grants (windows-for-peers) or
  proposals (cross-app-proposals), beyond the words handed to the master.
- No scoped key for a deck: the Companion modules keep their legacy proof.

Gaps it bears on: G04 (13.10.5), G07 (13.12), G08 (13.6.8), G15 (13.5.2), G16/G17 (13.12.9), G28 (13.11, 13.16.3), G31 (13.3.2, 13.4.3),
G32 (13.6.5), G35 (the node device), G37 (AG-PL3), G43 (13.8.2), G44 (13.20), G45 (13.6.3–4, with spec-1-3's §7.2). It closes none alone:
it is the standard the sibling designs' link units meet, plus the units in section 6.
