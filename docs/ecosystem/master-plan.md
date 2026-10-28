# Master plan — Show Bus and PeerLink across Stagewatch, Patterns, PlayMap and AGMessage (revised)

Design only: no repo is edited and nothing is built. The JSON form of this plan is `design/master/master-plan.json`, and `design/master/build.py` builds both from `b1.py`–`b7.py` after checking them for cycles, unknown dependencies, later-train dependencies and gap and owner bookkeeping.

## Overview

One plan for the four trees.
- Show Bus is the contract: what nodes say.
- PeerLink is layer 2: one proved, bounded, live link between two apps that carries the contract.
- Views, with Patterns' Eye first, read only the link record and the contexts.

This is the master plan revised against its critique (design/plan-critique.md). Each issue was checked against the trees on 2026-10-25. The table at the end of this overview gives each issue's verdict; the rules R1–R7 say how the trains run. Each unit's longer text, with files, evidence, tests and papers, is in design/plan.md under the unit's source design. A split unit carries its parent's text in parts.

**What it integrates:**
- the eight revised area designs;
- the PeerLink profile (design/peerlink.md);
- the Eye design (design/eye.md), with its critique folded in.

**The trains** (each closes before the next):
1. **Stop the harms.** Stagewatch 2.18; Patterns Round 89; PlayMap's fix batches; AGMessage 0.5.0 unit 21.
2. **One contract, one kit.** Show Bus 1.3 final; Rounds 90–91; the interop lane as a track.
3. **Every link honest from its own end.** Stagewatch 2.20 playout links; Rounds 92–93.
4. **One desk, one verdict for the room, the Eye's network band.** Stagewatch 2.21; Round 94.
5. **The hub proves itself; windows travel to peers.** Stagewatch 2.22; Rounds 95–96.
6. **Acceptors.** Round 97; Stagewatch 2.23; PlayMap's wp4-peerlink.
7. **The Eye sees the room.** Rounds 98–99; Stagewatch 2.24; PlayMap's context rows.
8. **PlayMap after round 3's integration pass.**
9. **AGMessage 0.5.1 and 0.6.0.**

**Rules the trains run by:**
- **R1 (order).** A unit may start once its dependencies have landed. A train's releases and round papers close only after the previous train has closed. A repo's own process may be stricter: a Patterns round opens only after the previous round's papers.
- **R2 (external windows).** An external window overrides train order: round 3's integration pass (PM-13), W3's start (PM-3), and AGMessage's 0.5.0 tag (AG-1).
- **R3 (slips).** A unit waiting for an owner's answer, or for another repo's unit that has not landed, moves to the first train (or, for Patterns, round) after its wait ends. Every unit whose only unmet dependency is a moved unit moves with it. A train or round is validated without moved units, and its papers say which moved and why. A moved Patterns unit not yet committed takes its new round's number. No Stagewatch release depends on a PlayMap-only or AGMessage-only interop unit.
  - A round's papers depend only on its units that wait on nothing outside the round. The others land before the papers, or move.
  - A round whose every unit needs another repo's work opens only once that work has landed (Round 95), and a round whose core is an owner's answer opens only after the answer (Round 97).
- **R4 (validates prove something).**
  - Patterns code units run the whole Core, Rendering and App suites, plus any suite the unit names. ControlProtocolTests live in Rendering.Tests. Every unit that moves the module runs CI's four module steps.
  - Papers units run all seven suites and the module.
  - Mapper filters name a class that exists, or that the unit creates, and carry `-- RunConfiguration.TreatNoTestsAsError=true`. Fixture tests run without PLAYMAP_WRITE_FIXTURES. Fixtures are regenerated as part of the change and committed with it.
  - AGMessage runs `npm ci`, `npm run check` and the browser smoke.
  - No validate pushes, creates a branch or writes a tracked file. Interop steps in release validates run once SW19.10 has landed.
- **R5 (owner timing).** Each owner question names the units it gates directly. It is answered before the first of them starts, and P-numbers before Round 89's first commit.
- **R6 (re-baseline).** At its train's start, each unit re-checks the file:line it cites. AGMessage's units re-check at each unit, because the branch is moving.
- **R7 (briefs).** No unit edits a CLAUDE.md. The lines the plan needs there (Stagewatch's loop line and its D0 doctrine line) are owner actions (S-brief, S-D0).

**Integration decisions** (unchanged unless the critique moved them):
- **Stuck lights.** Round 89.1 takes the profile's stuck lights now: the link live and amber, values with no time, the card amber.
- **Trust verbs.** First-use TRUST is one press beside the short fingerprint. ACCEPT after a changed certificate needs at least 8 hex typed. Both are on the desk and its menus only: the wire, decks and the assistant are refused with words.
- **The Eye's band** is a Band field beside Plane, and PlayMap's node device draws there.
- **Only this desk's own things** drive the rail, eye.worst and the exported headline.
- **Pairing before link fields.** The EYE link fields and CONTEXT POST need pairing (91.2, ADR-016).
- **One home for each number.** PeerContexts' caps in code live only in SW21.1; the 64 KiB body only in SW20.9; NodeBindings only in SW18.1; the hello table's 128 in 91.4; the brief's 32 in 91.3. §14 states them all.

**Process per repo:**
- **Stagewatch:**
  - one release per train, 2.18–2.24, each through `release.ps1 -Version 2.x.0 -ModuleVersion 2.x.0`;
  - tools/test.sh lanes green;
  - WPF work is named as the maintainer's PC work;
  - 2.17's fate is S-217.
- **Patterns:**
  - each round opens with a unit NN.0 (docs/field, OPEN, the requester, the evidence class) and closes with its papers and the tag row for the round before;
  - one commit per unit;
  - pushes happen at round close, so other repos depend on a round's papers unit, never on its code units;
  - rounds 89 and 95 touch platform paths (WebView2, the twin) and stay 'unwalked' until their QUALIFICATION §22 rows are filled.
- **PlayMap:**
  - nothing edits a file no round-3 wave owns without the owner's answer (round3/PLAN.md:2121-2125);
  - spine files go through §5.7;
  - the integrator's papers (WIRE, README, ROADMAP) get papers notes, never edits (:2198).
- **AGMessage:**
  - units go on the branch A-branch names, as 'AGMessage X.Y.Z, unit N';
  - AG-1 is 0.5.0's unit 21.

**The critique, checked:**

| # | Issue | Verdict | What changed |
|---|---|---|---|
| 1 | Round 89 cycle; P-numbers timing | Accepted | P89.2 and P89.4 drop the papers dependency (the source plan/t1.json never had it). Every round gets an NN.0 opening unit. P-numbers gates P89.0. |
| 2 | AG-1 last | Accepted | AG-1 moves to Train 1, re-baselined at 6146ee9. peerlinks.js:259-261, :292 and :346-356 still leak. A-hold-050, A-direct-path and the new A-branch are asked now. Receiver backstop: P89.5 and PM-2 mark a token arriving in clear from another machine, refusing nothing. |
| 3 | Twin secrets | Accepted | P92.4 becomes P89.6 (ShowState.cs:2080; TwinService.cs:488,512), with a §22 walk row in Round 89. |
| 4 | Release date | Accepted | -NoBump keeps ReleaseDate (release.ps1:83-88), and `-Bump minor` would yield 2.17.0 from today's 2.16.0, so every release states -Version and -ModuleVersion. |
| 5 | Slip rule | Accepted, narrowed | Rule R3. Only SW19.13 needed a PlayMap split (SW19.13m): SW20.13's S4.2/S4.3 and S6.3 test PlayMap at its pin and need no Mapper unit. Found while checking: SW21.9 held S3.5/S3.7, which need SW22.10 (Train 5), and S3.1/S3.2/S3.8, which need AGMessage. They move to SW22.11 and SW-INT-AG. |
| 6 | Module validate | Accepted | npm ci, test, lines diff, check (build.yml:81-88). |
| 7 | Empty filters | Accepted | Rule R4. ControlProtocolTests is in Rendering.Tests (RemotePresenterWarpTests.cs:9). Mapper has no NetworkHost, NodeCredential or Render Timer class. |
| 8 | Copies check | Accepted | Each copy is held by its own tree's ShowBusSpecTests; showbus-copies.sh checks only §14's `landed` trees; S7.2 joins in SW-INT-AG. |
| 9 | SW19.1 | Accepted | Split into five commits, each after the answer it needs. |
| 10 | Critical path | Accepted | SW19.12 depends only on the text and the kit. The lane is a track. |
| 11 | Mapper ownership | Accepted | Full file lists in M-PMX. Redacted goes through §5.7. Papers notes for the integrator. LinksCheck.Row is at PreflightBreadth.cs:115: the file exists, the type is LinksCheck. TimerView.cs falls under Engine/Rendering/** (round3/PLAN.md:578). |
| 12 | PM-13 window | Accepted | Event-triggered (R2); no SW21.1 dependency. |
| 13 | Work scheduled later | Accepted, one part narrowed | PM-11 sheds drawn windows to PM-15c. AG-8m waits for PM-9c. AG-10 drops PlayMap. P95.2 depends on P95.1. P89.1 needs no SW18.6: Stagewatch sends the stuck beat today (WebServer.cs:1265-1271,1297). |
| 14 | 9696 cross-origin | Rejected as stated; narrower fix accepted | Round 83 already refuses a cross-site POST without X-Patterns-Client, whatever the pairing (ControlService.cs:1004-1012; HttpHead.cs:50-63; OPEN L42). DNS rebinding remains, because Host is never checked (HttpHead.cs:148). New P89.7. |
| 15 | SW19.10 push; CLAUDE.md | Accepted | The validate pushes nothing and polls the run for HEAD's commit. Rule R7. |
| 16 | SW19.7 | Accepted | The probe joins test.sh's build lane (:27-34). |
| 17 | Fixture validates | Accepted | R4. For PM-7: no fixture holds links.stagewatch.timer today, so it adds StagewatchTimerTests. |
| 18 | AGMessage process | Accepted | CLAUDE.md:100-104 on the branch: smoke and subjects. |
| 19 | Patterns process | Accepted | Opening units. Round 90's evidence is a handed-in critique. Legacy paths are P-legacy. |
| low | Bookkeeping; P97.5 earlier | Accepted | Gap lists and owner blocks are now computed from the units. The blackout age lives in PM-6 only. P92.4's surfaces are stated. The OSC change is P89.8. |
| — | Units too big | Accepted | SW19.1, SW19.2, SW19.9, SW20.4, SW22.1, P89.3, P92.2, P97.3, P98.1, P98.3, PM-9, PM-15 and AG-5 are split along their seams. |
| — | Unmapped gaps | Accepted | G48's L49 is P93.3, with SW20.5 reading role and key. G46, G49's 'Link all' and G19's picker go to X-declines. G35, G37, G41 and G26 get their leftWhy. |
| — | Owner questions missing | Accepted | S-217, S-releases, S-interop-ci, S-shared-keys, S-brief, X-branches, A-branch, P-legacy and X-declines are added. P-numbers' timing is fixed. |

## The trains at a glance

| Train | Units | Stagewatch | Patterns | PlayMap | AGMessage |
|---|---|---|---|---|---|
| 1 | 22 | 8 | 10 | 3 | 1 |
| 2 | 34 | 22 | 10 | 2 | 0 |
| 3 | 29 | 15 | 11 | 3 | 0 |
| 4 | 14 | 10 | 4 | 0 | 0 |
| 5 | 22 | 14 | 8 | 0 | 0 |
| 6 | 13 | 2 | 7 | 4 | 0 |
| 7 | 14 | 2 | 10 | 2 | 0 |
| 8 | 11 | 1 | 0 | 10 | 0 |
| 9 | 14 | 1 | 0 | 0 | 13 |

## Train 1 — Stop the harms (Stagewatch 2.18; Patterns Round 89; PlayMap fix batches; AGMessage 0.5.0 unit 21)

**Goal.** Each harm is closed by the app that causes it, unilaterally and safe against today's peers:
- G04: a stuck Stagewatch unseen in Patterns;
- G09: a window's pass in Patterns' STATE;
- G06: screen commands hitting peers;
- the G08 class: tokens in clear from Stagewatch, Patterns and its module, PlayMap and AGMessage, with the receivers marking what they saw in clear;
- G15: one node key speaking for another;
- found in review: the twin's secrets on by default on every interface (OPEN L36), the web remote's loopback trust open to a rebound name, and OSC trust earned by an address.

Validated by Stagewatch's lanes, Round 89 closing with seven suites and the module, PlayMap's batches green on both CI jobs, and AGMessage's check and browser smoke on the branch its owner names.

| Unit | Repo | Title | Depends on |
|---|---|---|---|
| SW18.1 | Stagewatch | NodeBindings: one node key speaks for one node id (fail-closed); PeerContexts and ProposalDesk on it | owner:S-shared-keys, owner:X-branches |
| SW18.2 | Stagewatch | Every node route checks its credential before storing; unknown keys stop feeding the PIN lockout | SW18.1 |
| SW18.3 | Stagewatch | Show apps leave Screens: node links on /ws hear a verdict first; screen commands never reach a peer | SW18.1, SW18.2 |
| SW18.4 | Stagewatch | Desk, context and AI name show apps: link line, shape mark, right-click; Screens says who is not a screen | SW18.3 |
| SW18.5 | Stagewatch | Bindings persisted; 'Let it speak for…' in Settings and the Show network, journalled | SW18.4 |
| SW18.6 | Stagewatch | The rig as a test peer: one control reader, --node-keys, stall <ms>; nodes.e2e.cjs and stall.e2e.cjs | SW18.3 |
| SW18.7 | Stagewatch | PatternsLink sends no token in clear: plain AUTH only to loopback, a proof only for a 32-hex NONCE, nothing before OK paired | — |
| SW18.8 | Stagewatch | Stagewatch 2.18 papers and release | SW18.5, SW18.6, SW18.7, owner:S-217, owner:S-releases |
| P89.0 | Patterns | Round 89.0: the round opens — what the files said, OPEN, the requester and the evidence | owner:P-numbers, owner:X-branches |
| P89.1 | Patterns | Round 89.1: a stuck Stagewatch shows no time in every reader; its link stays live and amber | P89.0 |
| P89.2 | Patterns | Round 89.2: held addresses, Core — a window's pass becomes kept://host:port/<random id> | P89.0 |
| P89.3 | Patterns | Round 89.3: held addresses, App (1 of 2) — the kept store, every entry point, the resolver, black unless assigned | P89.2 |
| P89.4 | Patterns | Round 89.4: held addresses, App (2 of 2) — logs, STATE, the twin, the probe, the fence | P89.3 |
| P89.5 | Patterns | Round 89.5: no key or token in clear — out by pin and probe, the module by proof, and what arrives in clear is marked | P89.0 |
| P89.6 | Patterns | Round 89.6: the twin keeps device secrets home by default and listens on the bind address | P89.0, owner:P-twin-secrets |
| P89.7 | Patterns | Round 89.7: the web remote's loopback trust needs a Host that names this desk | P89.0 |
| P89.8 | Patterns | Round 89.8: a proof grants nothing to an address — the 12-hour OSC carry-over ends (ADR) | P89.0, owner:P-OSC12h |
| P89.9 | Patterns | Round 89 papers (PLAN §107) and the tag row for round 88 | P89.1, P89.4, P89.5, P89.7 |
| PM-1 | Mapper | fix-showbus-identify: Stagewatch's identify is a journal row, never OUTPUT IDENTIFY ALL | owner:M-PMX, owner:X-branches |
| PM-2 | Mapper | fix-showbus-keys: the Stagewatch key and the Patterns token never in clear; facts only after pairing; what arrives in clear is marked | owner:M-PMX |
| PM-3 | Mapper | fix-media-page-import: pages, embeds and window passes refused at import; kept passes scrubbed | owner:M-PMX, owner:M-content-before-W3 |
| AG-1 | AGMessage | AGMessage 0.5.0, unit 21: Patterns' and PlayMap's operator token never in clear and never driving them | owner:A-branch, owner:A-hold-050, owner:A-direct-path |

### SW18.1 — NodeBindings: one node key speaks for one node id (fail-closed); PeerContexts and ProposalDesk on it

*Stagewatch.* Sources: peers-not-screens SW-1. Gaps: G15. Depends on: owner:S-shared-keys, owner:X-branches.

peers-not-screens SW-1 (§1 D1–D4, §3).
- New src/Timer.Core/NodeBindings.cs: NodeProof, NodeCredential, key↔(kind,id) under a lock, Bound raised outside it.
- ApiKey.node/nodeKind appended.
- PeerContexts.Accept runs parse → Speak → stronger-proof → store. RiskFor is high without a verified entry.
- ProposalDesk on the bindings, answering 403 'this key speaks for <kind> <id>'.
- Today any node key posts a context under any id (WebServer.cs:452).
- Upgrade (S-shared-keys): a key bound to nothing is bound by its first use; a second app using it gets 403 'this key speaks for <kind> <id> — make a key for <app>', and its Show network row offers Make a key for <app> in one press. 2.18's WHATS_NEXT says so.
- The caps are SW21.1's.
- Tests: NodeTests.cs; ShowBusTests' fakes move to the credential overloads.

Validate:

```
cd /home/user/Stagewatch && tools/test.sh build unit
```

### SW18.2 — Every node route checks its credential before storing; unknown keys stop feeding the PIN lockout

*Stagewatch.* Sources: peers-not-screens SW-2. Gaps: G15. Depends on: SW18.1.

peers-not-screens SW-2.
- CredentialOf on /api/context, /api/peers/context, /api/proposals* and the message routes. The check runs before storing; 403 answers carry words.
- Unknown or revoked keys no longer count toward the per-address lockout they share with wrong PINs today (WebServer.cs:1077-1082): the ecosystem lane's F1 fix.
- Signatures, fakes and UiSmoke follow.
- WebV2Tests: a forged context answers 403 and stores nothing; a revoked key locks no address; a shared key is refused for a second id.

Validate:

```
cd /home/user/Stagewatch && tools/test.sh build unit; on the maintainer's PC: pwsh tools\smoke.ps1
```

### SW18.3 — Show apps leave Screens: node links on /ws hear a verdict first; screen commands never reach a peer

*Stagewatch.* Sources: peers-not-screens SW-3. Gaps: G06, G32, G43. Depends on: SW18.1, SW18.2.

peers-not-screens SW-3 (§2.1, D5–D9).
- Timer.Core/NodeLinks.cs: Admit sorts each socket into screen, claim or node.
- /ws?node=&t= with a node key or operator credential hears {type:'auth',ok,node,words} first; a refused key gets a refusal line; CredentialsChanged re-checks open sockets.
- Screens.Match and the new Timer.Remote/ScreenDirector.cs keep nodes and claims out of 'all' and empty targets, with a fixed matching order.
- Node links are grouped by id (G32); node= with win answers 400.
- Tests: NodeTests (the admission matrix), WebV2Tests.

Validate:

```
cd /home/user/Stagewatch && tools/test.sh build unit
```

### SW18.4 — Desk, context and AI name show apps: link line, shape mark, right-click; Screens says who is not a screen

*Stagewatch.* Sources: peers-not-screens SW-4. Gaps: G06. Depends on: SW18.3.

peers-not-screens SW-4.
- ShowContext peers rows append link and verified; AiContext facts follow.
- Each Show network row gets a link line and a shape mark: ✓ key, △ operator, ○ unverified.
- Its right-click menu (Copy node id, Let a key speak for…, Revoke…) stays in step with the buttons.
- The Screens subtitle and claim rows; diagnostics; companion-module screen labels.
- The WPF work is the maintainer's PC work.

Validate:

```
cd /home/user/Stagewatch && tools/test.sh build unit module; on the maintainer's PC: pwsh tools\smoke.ps1
```

### SW18.5 — Bindings persisted; 'Let it speak for…' in Settings and the Show network, journalled

*Stagewatch.* Sources: peers-not-screens SW-5. Gaps: G15. Depends on: SW18.4.

peers-not-screens SW-5.
- Bindings are persisted from the show thread.
- First uses and rebinds are journalled, with names and ids, never a secret.
- RebindApiKey.
- Settings rows say 'speaks for <kind> <id>'. Buttons and right-click stay in step.

Validate:

```
cd /home/user/Stagewatch && tools/test.sh build unit; on the maintainer's PC: pwsh tools\smoke.ps1
```

### SW18.6 — The rig as a test peer: one control reader, --node-keys, stall <ms>; nodes.e2e.cjs and stall.e2e.cjs

*Stagewatch.* Sources: peers-not-screens SW-6; patterns-playmap-side SW-ST. Gaps: G04, G06, G15, G43. Depends on: SW18.3.

peers-not-screens SW-6 plus patterns-playmap-side SW-ST, on one stdin control reader in tools/Stagewatch.Rig:
- --node-keys N;
- screen commands through ScreenDirector;
- peer contexts;
- `stall <ms>`, which blocks the owner thread so the real stuck beat goes out (WebServer.cs:1265-1271,1297; ShowState.cs:217).

tests/e2e/nodes.e2e.cjs and stall.e2e.cjs pin the bytes; interop rows S6.2a–c replay them against the real apps.

Validate:

```
cd /home/user/Stagewatch && tools/test.sh build unit e2e
```

### SW18.7 — PatternsLink sends no token in clear: plain AUTH only to loopback, a proof only for a 32-hex NONCE, nothing before OK paired

*Stagewatch.* Sources: PeerLink profile 13.6.8 / SW-PL0 (split); playout-links unit 4 (pairing rules). Gaps: G08. Depends on: nothing.

The unilateral part of 13.6.8, landed ahead of PlayoutLink.

Today PatternsLink.cs:
- sends plain AUTH <token> to any host when no NONCE arrives in 1.5 s (:280-284);
- proves a nonce of 1–128 characters (:311);
- takes STATE before pairing (:357-366).

Now:
- plain AUTH only to a loopback socket, through a local-address seam; otherwise amber words, and nothing secret is sent;
- AUTH-PROOF only for 32 lowercase hex;
- STATE, receipts and samples before OK paired are discarded and counted.

Suite tests run against the fake desk.

Validate:

```
cd /home/user/Stagewatch && tools/test.sh build unit
```

### SW18.8 — Stagewatch 2.18 papers and release

*Stagewatch.* Sources: peers-not-screens SW-7. Gaps: G06, G15. Depends on: SW18.5, SW18.6, SW18.7, owner:S-217, owner:S-releases.

peers-not-screens SW-7:
- REMOTE 2.18 'Show apps on /ws', with the PatternsLink token rule;
- NODES /ws lines; COMPANION; FEATURES; README;
- WHATS_NEXT 2.18, with what it did not do and the shared-key upgrade note;
- if 2.17 ships first (S-217), its section stays; if it folds in, 2.18 carries it.

The release goes through release.ps1 -Version 2.18.0 -ModuleVersion 2.18.0, never a hand edit: app, module, installer and AppInfo.ReleaseDate move together (release.ps1:83-88 keeps the old date only under -NoBump, which stays for a failed run's rebuild).

No SHOWBUS.md edit; those words go to SW19.1a–e.

Validate:

```
cd /home/user/Stagewatch && tools/test.sh; on the maintainer's PC: pwsh tools\smoke.ps1; pwsh tools\release.ps1 -Version 2.18.0 -ModuleVersion 2.18.0
```

### P89.0 — Round 89.0: the round opens — what the files said, OPEN, the requester and the evidence

*Patterns.* Sources: Patterns CLAUDE.md 'How a round opens'; plan critique issues 1, 19. Gaps: none. Depends on: owner:P-numbers, owner:X-branches.

Patterns' 'How a round opens', steps 1–4, as the round's first commit. Answered before it: P-numbers and X-branches.

PLAN §107's first lines:
- what the files said: none (docs/field holds only README.md);
- OPEN: closes L26, L57, L58; adds L59, L60; amends L36, L40, L42;
- the requester: the maintainer, through the Stagewatch session's ask;
- the evidence class: a handed-in critique (the cross-repo gap analysis and its designs);
- each item read against the standing brief, with 'no measurement this round' where there is none.

Validate:

```
cd /home/user/Patterns && grep -n '^## 107\. Round 89' docs/PLAN.md
```

### P89.1 — Round 89.1: a stuck Stagewatch shows no time in every reader; its link stays live and amber

*Patterns.* Sources: patterns-playmap-side 89.1; PeerLink profile 13.10.5, 13.16.1; Eye critique issue 1. Gaps: G04. Depends on: P89.0.

patterns-playmap-side 89.1, with the profile's lights (13.10.5, 13.16.1).

Today ReceiveAsync ignores the beat, and every message refreshes the link (StagewatchLinkService.cs:515-538).

Now:
- A single-writer _stuck is set from {type:'beat',live:false} and cleared only by a state frame.
- The context moves to its own field; Sight folds in Frame.Live=false.
- The link stays live, △ amber, 'says its show is stuck'; counting values show ✕ no time; the card takes the worse of its light and amber.

Readers: pages, STATE bus.stagewatch (`stuck` appended), the twin relay, the Eye, the Nodes card, the brief, the module's stagewatch_stuck, and one log line per transition.

Tests carry the stuck beat's bytes as Stagewatch sends them today (WebServer.cs:1297, sent at :1265-1271), so nothing waits for SW18.6; interop row S6.2b replays the live rig.

Validate:

```
cd /home/user/Patterns && dotnet build Patterns.sln -c Debug -v q && for s in Core Rendering App Assistant; do dotnet test tests/Patterns.$s.Tests -c Debug --no-build || exit 1; done && cd integrations/companion-module-patterns && npm ci && npm test && node test/emit-lines.mjs | diff - test/lines.txt && npm run check
```

### P89.2 — Round 89.2: held addresses, Core — a window's pass becomes kept://host:port/<random id>

*Patterns.* Sources: patterns-playmap-side 89.2; windows-for-peers PT-W1. Gaps: G09. Depends on: P89.0.

patterns-playmap-side 89.2, which is windows-for-peers PT-W1 Core.
- WindowAddresses; kept ids of 60 random bits from RandomNumberGenerator; the per-machine book.
- A load migration with its journal row (a scrub of secrets already in show files, not a compatibility path).
- TwinWord.Kept; bundle redaction; the InputKeys/MediaLocator fence; black for an unresolved page.

Today WebRow publishes the pass (CommandRouter.cs:294,618).

Tests: PrivateAddressFenceTests (Core).

Validate:

```
cd /home/user/Patterns && dotnet build Patterns.sln -c Debug -v q && for s in Core Rendering App; do dotnet test tests/Patterns.$s.Tests -c Debug --no-build || exit 1; done
```

### P89.3 — Round 89.3: held addresses, App (1 of 2) — the kept store, every entry point, the resolver, black unless assigned

*Patterns.* Sources: patterns-playmap-side 89.3; windows-for-peers PT-W1. Gaps: G09. Depends on: P89.2.

patterns-playmap-side 89.3 / PT-W1 App, its first half:
- the kept store on Secrets;
- secure at every entry: wire, OSC, menus, Library, native player, load;
- the WebView2 resolver;
- the shell observer draws black unless assigned.

Platform path: WebView2 (QUALIFICATION §22/§28 walk rows).

Validate:

```
cd /home/user/Patterns && dotnet build Patterns.sln -c Debug -v q && for s in Core Rendering App; do dotnet test tests/Patterns.$s.Tests -c Debug --no-build || exit 1; done
```

### P89.4 — Round 89.4: held addresses, App (2 of 2) — logs, STATE, the twin, the probe, the fence

*Patterns.* Sources: patterns-playmap-side 89.3; windows-for-peers PT-W1. Gaps: G09. Depends on: P89.3.

The second half of 89.3 / PT-W1 App:
- CurrentUrl and the logs scrubbed; a public web.url in STATE;
- kept passes cross the twin only under SendSecrets (off by default from 89.6);
- the WebView2 probe (OPEN L26 closed);
- a raw-address Super Check row;
- the full PrivateAddressFenceTests: a canary over STATE, CONTEXT, the Eye, the journal and the bundle.

Validate:

```
cd /home/user/Patterns && dotnet build Patterns.sln -c Debug -v q && for s in Core Rendering App; do dotnet test tests/Patterns.$s.Tests -c Debug --no-build || exit 1; done && cd integrations/companion-module-patterns && npm ci && npm test && node test/emit-lines.mjs | diff - test/lines.txt && npm run check
```

### P89.5 — Round 89.5: no key or token in clear — out by pin and probe, the module by proof, and what arrives in clear is marked

*Patterns.* Sources: PeerLink profile 13.3.2, 13.4.2, 13.6.8; plan critique issue 2 (receiver backstop). Gaps: G08. Depends on: P89.0.

Rules 13.3.2, 13.4.2 and 13.6.8.

StagewatchLinkService (out):
- Today it picks TLS from the hello (:241-242) and otherwise sends the bearer key plain (:269).
- Now TLS is chosen by pin and probe; a pinned id is dialled over TLS whatever a hello says (a contradicting hello is a note, never a refusal); with no TLS off this machine it shows the keyless public view and says 'Hall A offers no TLS — the key stays on this desk'; plain only to loopback.

The Companion module: today plain AUTH to any host when no NONCE arrives (src/main.js:196-204); now loopback only, AUTH-PROOF only for 32 hex; its next patch version.

In, refusing nothing: a request on 9696's plain listener, or a plain AUTH on 9697, that presents the token from another machine marks it 'seen in clear from <address> — rotate it: NEW TOKEN' (amber) on the Super Check's REMOTE row, the Nodes page and STATE control (appended), with one journal row per address. This is the receiver's backstop for AGMessage 0.5.0's header (peerlinks.js:292) until AG-1 lands.

Validate:

```
cd /home/user/Patterns && dotnet build Patterns.sln -c Debug -v q && for s in Core Rendering App; do dotnet test tests/Patterns.$s.Tests -c Debug --no-build || exit 1; done && cd integrations/companion-module-patterns && npm ci && npm test && node test/emit-lines.mjs | diff - test/lines.txt && npm run check
```

### P89.6 — Round 89.6: the twin keeps device secrets home by default and listens on the bind address

*Patterns.* Sources: PeerLink profile Patterns 'Round 90' twin unit; plan critique issue 3. Gaps: G09. Depends on: P89.0, owner:P-twin-secrets.

Was 92.4; moved here because 89.3–89.4 make window passes secrets that ride the twin (OPEN L36).
- SendSecrets defaults off (TwinConfig, ShowState.cs:2080); refused with words on a plain twin link to another machine.
- The twin listens on the desk's bind address, not IPAddress.Any (TwinService.cs:488,512).
- A standby without passwords or kept passes says so on both desks (OPEN L60).
- The twin stays a 13.18 exemption.

Platform path: the twin (a QUALIFICATION §22 walk row). OPEN: L36 amended; a takeover row (accepted links and posted contexts bound to the main's id) added for Round 98.

Validate:

```
cd /home/user/Patterns && dotnet build Patterns.sln -c Debug -v q && for s in Core Rendering App; do dotnet test tests/Patterns.$s.Tests -c Debug --no-build || exit 1; done
```

### P89.7 — Round 89.7: the web remote's loopback trust needs a Host that names this desk

*Patterns.* Sources: plan critique issue 14 (narrowed); PeerLink profile 13.6 (Host and Origin before loopback trust). Gaps: G08. Depends on: P89.0.

What the critique asked, checked:
- Since Round 83, a POST that is cross-site by Sec-Fetch-Site, or by an Origin that is not its Host, without X-Patterns-Client, is refused whatever the pairing (ControlService.cs:1004-1012; HttpHead.cs:50-63; HttpHeadTests.cs:68-78; OPEN L42). A simple cross-origin BLACKOUT is already refused.
- What remains is a rebound name. Host is read (HttpHead.cs:148) and never checked. A page whose name is rebound to 127.0.0.1 is same-origin to itself and is trusted as loopback (ControlService.cs:594, 977-978), so it can POST /api/cmd or GET /api/state.

Now loopback trust, and the pages' cookie, need a Host naming this desk: localhost, 127.0.0.1, [::1], the machine's name or one of its addresses, with the port. Any other Host is answered as an unpaired machine, with words and one log line per address.

Tests: HttpHeadTests and RemoteControlTests; TrustLoopback restored in finally.

Validate:

```
cd /home/user/Patterns && dotnet build Patterns.sln -c Debug -v q && for s in Core Rendering App; do dotnet test tests/Patterns.$s.Tests -c Debug --no-build || exit 1; done
```

### P89.8 — Round 89.8: a proof grants nothing to an address — the 12-hour OSC carry-over ends (ADR)

*Patterns.* Sources: PeerLink profile 13.18; plan critique (low: P97.5 earlier). Gaps: G08. Depends on: P89.0, owner:P-OSC12h.

13.18.
- Today a wire or web proof trusts its address for OSC for 12 h (ControlService.cs:439, 557-569, 979-981).
- Now OSC on a paired desk is accepted only from the OSC ALLOW list and loopback.
- An ADR in docs/ADR.md; a Super Check words change; OscAppTests.

Unilateral. It lands here if P-OSC12h is answered before Round 89's papers; otherwise it moves to the first round after the answer (rule R3), and the papers do not wait for it.

Validate:

```
cd /home/user/Patterns && dotnet build Patterns.sln -c Debug -v q && for s in Core Rendering App Devices; do dotnet test tests/Patterns.$s.Tests -c Debug --no-build || exit 1; done
```

### P89.9 — Round 89 papers (PLAN §107) and the tag row for round 88

*Patterns.* Sources: patterns-playmap-side Round 89 papers; windows-for-peers PT-W5 (89 part). Gaps: G04, G09, G08. Depends on: P89.1, P89.4, P89.5, P89.7.

- PLAN §107 completed: the design, the proof, and what it did not do (no PeerLink handshake; the node link waits for Stagewatch 2.18 and lands in Round 91).
- REVIEW 'Walk: unwalked': WebView2 resolution; the twin's bind address and takeover (§22 rows and a new §28).
- CHANGELOG naming the evidence class; README; REMOTE (bus.stagewatch stuck, web.url's public form, control's seen-in-clear field, the Host rule, the OSC rule if 89.8 landed); COMPANION (stagewatch_stuck; the module's AUTH rule and patch version).
- OPEN: L57/L58 added and closed, L26 closed, L36/L40/L42 amended, L59 and L60 added.
- Tag row `88 bcdbf2829cea302078acee7887841389e00d5178`.
- Closing: the stall's log lines and the migration's journal row, beside the stage page and the raw-address row.

89.6 and 89.8 are owner-gated: each lands before this unit or moves to a later round (rule R3), so the papers never wait for an answer.

Validate:

```
cd /home/user/Patterns && dotnet build Patterns.sln -c Debug -v q && for s in Core Rendering Devices Audio Assistant Audience App; do dotnet test tests/Patterns.$s.Tests -c Debug --no-build || exit 1; done && cd integrations/companion-module-patterns && npm ci && npm test && node test/emit-lines.mjs | diff - test/lines.txt && npm run check
```

### PM-1 — fix-showbus-identify: Stagewatch's identify is a journal row, never OUTPUT IDENTIFY ALL

*Mapper.* Sources: peers-not-screens PM-1. Gaps: G06. Depends on: owner:M-PMX, owner:X-branches.

peers-not-screens PM-1.
- Today OnScreenCommand runs OUTPUT IDENTIFY ALL 5 for any Stagewatch (Engine/Host/NetworkHost.cs:783).
- New pure Net/Links/StagewatchScreenCommands.cs: identify becomes one journal row; redirect, reload and disconnect are unchanged.
- Files (M-PMX): Engine/Host/NetworkHost.cs, Net/Links/StagewatchScreenCommands.cs (new), docs/round2/CARRYOVER.md.
- A named fix batch with a CARRYOVER entry.
- Tests: StagewatchScreenCommandsTests (Net), StagewatchIdentifyTests (Engine), both new.

Validate:

```
cd /home/user/Mapper && dotnet build PlayMap.sln -c Debug -v q && dotnet test tests/PlayMap.Net.Tests -c Debug --no-build --filter "FullyQualifiedName~StagewatchScreenCommandsTests" -- RunConfiguration.TreatNoTestsAsError=true && dotnet test tests/PlayMap.Engine.Tests -c Debug --no-build --filter "FullyQualifiedName~StagewatchIdentifyTests" -- RunConfiguration.TreatNoTestsAsError=true
```

### PM-2 — fix-showbus-keys: the Stagewatch key and the Patterns token never in clear; facts only after pairing; what arrives in clear is marked

*Mapper.* Sources: PeerLink profile PM-PL0 (safety subset); patterns-playmap-side PM-P1 / D20 (TLS half); plan critique issue 2 (receiver backstop). Gaps: G08, G31. Depends on: owner:M-PMX.

The safety subset of PM-PL0, plus G31's TLS half.

StagewatchLink today: TLS only by the hello (Net/Links/StagewatchLink.cs:66); a typed address always dialled plain (NetworkHost.cs:718-735); the key sent as a bearer either way (:278-279).

Now:
- TLS by pin and probe; a typed address tries TLS first; no key plain off loopback; the keyless view otherwise;
- the key moves from plain playmap.node.json (Core/Bus/Pairing.cs:121-123) into NodeCredentialStore;
- WireClient proves only a 32-hex NONCE (:314);
- PatternsLink takes no STATE or blackout before OK paired (PatternsLink.cs:218-247);
- in, refusing nothing: the token presented in clear from another machine (X-PlayMap-Token on plain HTTP, Net/Http/HttpParts.cs; plain AUTH on 9941, Net/Wire/WireSession.cs) is marked amber in LinksCheck's row and journalled, with no STATE field (StateWriter is a spine file).

Files (M-PMX): Net/Links/{StagewatchLink,PatternsLink}.cs, Net/Wire/{WireClient,WireSession}.cs, Net/Http/HttpParts.cs, Engine/Host/NetworkHost.cs, Core/Bus/Pairing.cs, Engine/Devices/NodeCredentialStore.cs, Engine/Show/PreflightBreadth.cs.

Tests on tests/Shared/ManualTime.cs: StagewatchLinkTests, LinkTests, WireServerTests (Net); NodeCredentialStoreTests (Engine, new); PreflightBreadthTests.

Validate:

```
cd /home/user/Mapper && dotnet build PlayMap.sln -c Debug -v q && dotnet test tests/PlayMap.Net.Tests -c Debug --no-build --filter "FullyQualifiedName~StagewatchLinkTests|FullyQualifiedName~LinkTests|FullyQualifiedName~WireServerTests|FullyQualifiedName~TlsTests" -- RunConfiguration.TreatNoTestsAsError=true && dotnet test tests/PlayMap.Engine.Tests -c Debug --no-build --filter "FullyQualifiedName~NodeCredentialStoreTests|FullyQualifiedName~PreflightBreadthTests" -- RunConfiguration.TreatNoTestsAsError=true
```

### PM-3 — fix-media-page-import: pages, embeds and window passes refused at import; kept passes scrubbed

*Mapper.* Sources: patterns-playmap-side PM-F1, PM-F2; windows-for-peers PM-W1; plan critique issue 11. Gaps: G47. Depends on: owner:M-PMX, owner:M-content-before-W3.

PM-F1+F2, which is PM-W1.
- MEDIA IMPORT of a page, an embed, a private address or an https stream is refused with the reason.
- Kept passes are scrubbed on load, with a journal row.
- LIST, readiness and FETCH say honestly what is ready.
- The private-address marker in VerbCatalog.Redacted goes through the §5.7 spine lane: Redacted (VerbCatalog.cs:617-641) lies outside every block, so it is a spine file (round3/PLAN.md:307-311), not a block-local edit.

EngineCore.Content.cs is desk-map's in W3, so this lands before W3 starts or at the integration pass (rule R2).

Tests: MediaImportRefusalTests (Engine, new); ShownLineTests (Core).

Validate:

```
cd /home/user/Mapper && dotnet build PlayMap.sln -c Debug -v q && dotnet test tests/PlayMap.Engine.Tests -c Debug --no-build --filter "FullyQualifiedName~MediaImportRefusalTests|FullyQualifiedName~ContentHostTests" -- RunConfiguration.TreatNoTestsAsError=true && dotnet test tests/PlayMap.Core.Tests -c Debug --no-build --filter "FullyQualifiedName~ShownLineTests" -- RunConfiguration.TreatNoTestsAsError=true
```

### AG-1 — AGMessage 0.5.0, unit 21: Patterns' and PlayMap's operator token never in clear and never driving them

*AGMessage.* Sources: PeerLink profile AG-PL0; plan critique issue 2. Gaps: G08. Depends on: owner:A-branch, owner:A-hold-050, owner:A-direct-path.

AG-PL0, moved from Train 9: a safety fix in a version that is closing now.

Re-baselined at 6146ee9, which is 8 commits past 9d7f5c3; app/lib/peerlinks.js and nodes.js are unchanged between the two:
- the token header (peerlinks.js:292) goes plain to a node that is neither pinned nor announcing a certificate (:259-261, :267);
- send() (:346-356, called at server.js:1260) drives Patterns and PlayMap with it.

Now:
- the token goes only to loopback, or over TLS to a pinned or person-confirmed certificate (at least 8 hex typed); otherwise the link is 'refused', with words;
- send() is off while the only credential is the operator token: lines become §11 proposals, or are refused with words;
- a timeout after a write is unconfirmed (:355); the admin page shows an amber note.

Tests use an injected local-address predicate. Commit subject 'AGMessage 0.5.0, unit 21: …', on the branch A-branch names; the tag stays its owner's.

Validate:

```
cd /home/user/AGMessage/app && npm ci --no-audit --no-fund && npm run check && AGM_BROWSER="$(command -v google-chrome || command -v chromium)" npm run smoke
```

## Train 2 — One contract, one kit (Show Bus 1.3 final in Stagewatch 2.19; Patterns Rounds 90–91; PlayMap fix-showbus-1.3 and node-link; the interop lane as a track)

**Goal.** Show Bus 1.3 is final in Stagewatch, with §12 Windows, §13 PeerLink, §14 numbers and the Eye's P1–P8, and its copy is in Patterns and, if M-PMX is granted, PlayMap. Each copy is held by its own tree's ShowBusSpecTests against the hashes §14 pins. The kit (the vectors in the hashed body, one scripted peer) is in each copying tree, and hello and certificates follow 1.3. Patterns and PlayMap dial Stagewatch as nodes, and Patterns shows its state only to paired connections. No PeerLink behaviour yet (Phase 0).

The interop lane (SW19.8, SW19.9a–b, SW19.10, SW19.11L, SW19.13, SW19.13m) is a track inside this train. 1.3 final and Stagewatch 2.19 do not wait for it. It gates releases from the first release after SW19.10 lands (if S-interop-gate is answered yes).

Validated by ShowBusSpecTests in each tree, tools/showbus-copies.sh over the trees §14 lists as landed, PeerLinkVectors, and `tools/test.sh interop` once the track lands.

| Unit | Repo | Title | Depends on |
|---|---|---|---|
| SW19.1a | Stagewatch | Show Bus 1.3 text (1 of 5): §13 PeerLink with the hashed ```peerlink vectors block | SW18.8, owner:S-sections |
| SW19.1b | Stagewatch | Show Bus 1.3 text (2 of 5): §14 numbers | SW19.1a |
| SW19.1c | Stagewatch | Show Bus 1.3 text (3 of 5): §12 Windows and §1.5, worded per D0 | SW19.1b, owner:S-D0 |
| SW19.1d | Stagewatch | Show Bus 1.3 text (4 of 5): §6 the Eye's P1–P8, @<id>, CONTEXT POST and the kind table | SW19.1b |
| SW19.1e | Stagewatch | Show Bus 1.3 text (5 of 5): the words for §1–§11, the §8 playmap row and the named exemptions | SW19.1b, owner:S-exemptions |
| SW19.2a | Stagewatch | The PeerLink kit (1 of 3): Stagewatch's pure core and the vectors group | SW19.1a |
| SW19.2b | Stagewatch | The PeerLink kit (2 of 3): the scripted peer's honest ends | SW19.1a |
| SW19.2c | Stagewatch | The PeerLink kit (3 of 3): the peer as an attacker; scenarios S8–S22 | SW19.2a, SW19.2b |
| SW19.3 | Stagewatch | Hello 1.3: from the snapshot, silent on loopback, every address per id, sticky dial, conflicts amber | SW19.1e |
| SW19.4 | Stagewatch | Stagewatch's own context within §14: 200 items, the trimming order, context:trimmed | SW19.1b |
| SW19.5 | Stagewatch | Certificates 825 days in all; an over-long one replaced once | SW19.1b |
| SW19.6 | Stagewatch | View passes counted apart from PINs and keys | SW19.1c |
| SW19.7 | Stagewatch | LB-1: the same-machine carrier probe | — |
| SW19.12 | Stagewatch | Show Bus 1.3 final: the body hash and the peer's hash pinned; tools/showbus-copies.sh over the landed trees | SW19.1a, SW19.1b, SW19.1c, SW19.1d, SW19.1e, SW19.2a, SW19.2b, SW19.2c |
| SW19.11 | Stagewatch | Stagewatch 2.19 papers and release: NODES.md from the code, ECOSYSTEM, REMOTE | SW19.3, SW19.4, SW19.5, SW19.6, SW19.7, SW19.12, owner:S-releases |
| SW19.8 | Stagewatch | Lane track: the rig for the interop lane — stable id, journal and report, secrets off stdout, the lockout readable | SW18.6 |
| P90.0 | Patterns | Round 90.0: the round opens | P89.9 |
| P90.1 | Patterns | Round 90.1: the bench host — the real desk headless off Windows for the interop lane | P90.0 |
| P90.2 | Patterns | Round 90 papers (PLAN §108) | P90.1 |
| SW19.9a | Stagewatch | Lane track: the interop lane (1 of 2) — four real apps on a one-kernel LAN | SW19.8, P90.2 |
| SW19.9b | Stagewatch | Lane track: the interop lane (2 of 2) — the first rows | SW19.9a, SW18.6 |
| SW19.10 | Stagewatch | Lane track: CI's interop job at the siblings' pins; the nightly heads run | SW19.9b, owner:S-interop-gate, owner:S-interop-ci |
| SW19.11L | Stagewatch | Lane track: the lane's papers | SW19.9b, owner:S-brief |
| P91.0 | Patterns | Round 91.0: the round opens | P90.2 |
| P91.1 | Patterns | Round 91.1: the Stagewatch link dials as a node and reads its verdict | SW18.3, P91.0, owner:P-legacy |
| P91.2 | Patterns | Round 91.2: the wire shows its state only to a paired connection (ADR-016) | P91.0, SW18.7, owner:P-ADR016 |
| P91.3 | Patterns | Round 91.3: the context lists what peers may propose; APPLY at review; the reader keeps 256 | P91.0 |
| P91.4 | Patterns | Round 91.4: certificates 825 days; the hello 1.3; the hello table bounded at 128 | P91.0 |
| P91.5 | Patterns | Round 91.5: the Show Bus 1.3 copy and the kit, held by Patterns' own tests | P91.0, SW19.12 |
| P91.6 | Patterns | Round 91 papers (PLAN §109) | P91.3, P91.4 |
| PM-4 | Mapper | fix-showbus-1.3: the 1.3 copy and the kit; SHOWBUS-NODE corrected; the hello reader's limits | SW19.12, owner:M-PMX |
| PM-5 | Mapper | fix-showbus-node-link: PlayMap dials Stagewatch as a node; 'paired' from the verdict | SW18.3, PM-2, owner:M-PMX |
| SW19.13 | Stagewatch | Lane track: keys from the rig, the rig on the bus, contexts both ways — the rows Stagewatch and Patterns units carry | SW19.9b, P91.6 |
| SW19.13m | Stagewatch | Lane track: PlayMap's node-link and 1.3-reader legs | SW19.9b, PM-4, PM-5 |

### SW19.1a — Show Bus 1.3 text (1 of 5): §13 PeerLink with the hashed ```peerlink vectors block

*Stagewatch.* Sources: spec-1-3 S1; PeerLink profile SW-PL4 (text). Gaps: G38. Depends on: SW18.8, owner:S-sections.

spec-1-3 S1, split into ordered commits so that no text waits for an answer it does not need.
- The profile's §13 (13.1–13.18), with the ```peerlink block inside the hashed body.
- ShowBusSpecTests gains the §13 rows and reads the block.

Validate:

```
cd /home/user/Stagewatch && tools/test.sh build unit
```

### SW19.1b — Show Bus 1.3 text (2 of 5): §14 numbers

*Stagewatch.* Sources: spec-1-3 S1 (§13→§14); PeerLink profile SW-PL4 (numbers). Gaps: G38, G40. Depends on: SW19.1a.

spec-1-3's numbers block, renumbered §13→§14, with the profile's rows appended:
- 256 commands; 512 characters for verbs and words; 200 items; 400 links; a 64 KiB body;
- 32 caps; 64-character names; 128 peers; 825 days.

A `landed` list names the trees whose copies the hashes hold (Stagewatch at first). ShowBusSpecTests rows.

Validate:

```
cd /home/user/Stagewatch && tools/test.sh build unit
```

### SW19.1c — Show Bus 1.3 text (3 of 5): §12 Windows and §1.5, worded per D0

*Stagewatch.* Sources: windows-for-peers SW-W7 (words); spec-1-3 S1. Gaps: G22. Depends on: SW19.1b, owner:S-D0.

windows-for-peers §1.5 and §12.4+, written after S-D0's answer so that nothing is written twice. ShowBusSpecTests rows.

Validate:

```
cd /home/user/Stagewatch && tools/test.sh build unit
```

### SW19.1d — Show Bus 1.3 text (4 of 5): §6 the Eye's P1–P8, @<id>, CONTEXT POST and the kind table

*Stagewatch.* Sources: Eye SB-EYE; spec-1-3 S1. Gaps: G38. Depends on: SW19.1b.

The Eye's §6, as amended by its critique:
- one P2 row per link;
- acceptors publish P2 items;
- the twin item is a 13.18 exemption;
- the 13.16.5 acceptor light.

New ShowBusKinds. ShowBusSpecTests rows.

Validate:

```
cd /home/user/Stagewatch && tools/test.sh build unit
```

### SW19.1e — Show Bus 1.3 text (5 of 5): the words for §1–§11, the §8 playmap row and the named exemptions

*Stagewatch.* Sources: spec-1-3 S1; cross-app-proposals SW-7 (words); playout-links unit 11 (words); peers-not-screens SW-7 (words); ecosystem-lane SW-P (words); agmessage SW-AG-1. Gaps: G17, G42, G45. Depends on: SW19.1b, owner:S-exemptions.

- The profile's words for §1, §3, §4, §5, §7, §7.2, §8 and §10.
- The words of cross-app, playout-links, agmessage (top-level ok/words), peers-not-screens and the ecosystem lane.
- §8's playmap row, and the exemptions per S-exemptions: ?pin=, 9923, and PlayMap's 9941 greeting until decision 17.
- ShowBusSpecTests rows.

Validate:

```
cd /home/user/Stagewatch && tools/test.sh build unit
```

### SW19.2a — The PeerLink kit (1 of 3): Stagewatch's pure core and the vectors group

*Stagewatch.* Sources: PeerLink profile SW-PL4 (kit). Gaps: G44. Depends on: SW19.1a.

SW-PL4's core. src/Timer.Core/PeerLink.cs, pure on injected time and j:
- canon, K, kid, T, proofs, lid, S;
- liveness and the self-gap;
- clock quality and redial;
- the 13.12.3 table and the 13.16 machine.

The PeerLinkVectors group reads the ```peerlink block from docs/SHOWBUS.md.

Validate:

```
cd /home/user/Stagewatch && tools/test.sh build unit
```

### SW19.2b — The PeerLink kit (2 of 3): the scripted peer's honest ends

*Stagewatch.* Sources: PeerLink profile SW-PL1. Gaps: G44. Depends on: SW19.1a.

SW-PL1, first half. tools/showbus/peerlink-peer.mjs:
- dependency-free Node 22;
- plays either end on line, ws and side calls;
- NDJSON control; keys come from 0600 files.

Scenarios S1–S7 run against Stagewatch's core in the unit lane.

Validate:

```
cd /home/user/Stagewatch && node --check tools/showbus/peerlink-peer.mjs && tools/test.sh build unit
```

### SW19.2c — The PeerLink kit (3 of 3): the peer as an attacker; scenarios S8–S22

*Stagewatch.* Sources: PeerLink profile SW-PL1. Gaps: G44. Depends on: SW19.2a, SW19.2b.

SW-PL1, second half:
- the peer's attacker modes (a replayed proof, a forged kid, a flood, a lying clock);
- scenarios S8–S22 against Stagewatch's core;
- §14's peerlink.peer.sha256 is computed here and pinned in SW19.12.

Validate:

```
cd /home/user/Stagewatch && node --check tools/showbus/peerlink-peer.mjs && tools/test.sh build unit
```

### SW19.3 — Hello 1.3: from the snapshot, silent on loopback, every address per id, sticky dial, conflicts amber

*Stagewatch.* Sources: spec-1-3 S2. Gaps: G27, G40. Depends on: SW19.1e.

spec-1-3 S2.

The hello:
- built from the snapshot; silent when bound to loopback;
- per-target sends with Status; real doors only;
- proto 3, inst and the windows cap.

The reader:
- keeps 32 caps and 64-character names;
- keeps every address per id and dials the same one while it is heard;
- shows a conflict amber.

Also the Show network line, bus:hello, rig --bus/--bus-to and bus.e2e.cjs.

Validate:

```
cd /home/user/Stagewatch && tools/test.sh build unit e2e
```

### SW19.4 — Stagewatch's own context within §14: 200 items, the trimming order, context:trimmed

*Stagewatch.* Sources: spec-1-3 S3. Gaps: G40. Depends on: SW19.1b.

spec-1-3 S3:
- ShowContext stays within 64 KiB, 200 items and 400 links;
- it trims in a fixed order;
- a context:trimmed item says how many were cut.

Validate:

```
cd /home/user/Stagewatch && tools/test.sh build unit
```

### SW19.5 — Certificates 825 days in all; an over-long one replaced once

*Stagewatch.* Sources: spec-1-3 S4. Gaps: G39. Depends on: SW19.1b.

spec-1-3 S4:
- CertificateLife and NodeCertificate allow 825 days in all (826 today);
- an over-long certificate is replaced once, so peers see CERTIFICATE CHANGED once, and the papers say so.

Validate:

```
cd /home/user/Stagewatch && tools/test.sh build unit
```

### SW19.6 — View passes counted apart from PINs and keys

*Stagewatch.* Sources: spec-1-3 S5. Gaps: G22. Depends on: SW19.1c.

spec-1-3 S5 (§12.2):
- wrong view passes go into their own table;
- a correct credential never clears them;
- REMOTE names each count.

Validate:

```
cd /home/user/Stagewatch && tools/test.sh build unit
```

### SW19.7 — LB-1: the same-machine carrier probe

*Stagewatch.* Sources: spec-1-3 S6; plan critique issue 16. Gaps: G27. Depends on: nothing.

spec-1-3 S6:
- tools/BusCarrierProbe tests 127.0.0.1, 127.255.255.255 and loopback multicast on 9928;
- tools/test.sh's build lane gains the probe (today it builds only Timer.slnf, PageShot and the Rig, tools/test.sh:27-34), so the --no-build run below finds it;
- the Linux run is recorded;
- the Windows run is the maintainer's PC work, reported as not run.

Validate:

```
cd /home/user/Stagewatch && tools/test.sh build && dotnet run --project tools/BusCarrierProbe -c Debug --no-build
```

### SW19.12 — Show Bus 1.3 final: the body hash and the peer's hash pinned; tools/showbus-copies.sh over the landed trees

*Stagewatch.* Sources: spec-1-3 S8; PeerLink profile SW-PL4 (hash); plan critique issues 8, 10. Gaps: G38, G39, G40, G41, G42. Depends on: SW19.1a, SW19.1b, SW19.1c, SW19.1d, SW19.1e, SW19.2a, SW19.2b, SW19.2c.

spec-1-3 S8:
- the header says final;
- BodySha256 and §14's peerlink.peer.sha256 are pinned;
- the readers' rows are asserted.

tools/showbus-copies.sh compares the body and peer hashes only in the trees §14's `landed` list names, and names each stale copy. At first that list is Stagewatch alone. Each later copy (P91.5, PM-4, AG-4) proves itself in its own tree's ShowBusSpecTests against the pinned numbers, and the master's `landed` list grows in that train's Stagewatch release. Interop row S7.2 runs the script over all four clones once the last copy lands.

No dependency on the lane or on 2.19's code units: Train 3 waits only for this text.

Validate:

```
cd /home/user/Stagewatch && tools/test.sh build unit && bash tools/showbus-copies.sh
```

### SW19.11 — Stagewatch 2.19 papers and release: NODES.md from the code, ECOSYSTEM, REMOTE

*Stagewatch.* Sources: spec-1-3 S7. Gaps: G41, G38. Depends on: SW19.3, SW19.4, SW19.5, SW19.6, SW19.7, SW19.12, owner:S-releases.

spec-1-3 S7:
- NODES.md written from the code and held by a test (G41), with a WHATS_NEXT row 'drop UDP 9700 from Find once Patterns' 9928 walk (OPEN L47) is filled';
- ECOSYSTEM: PlayMap, 1.3, the manifest marked superseded;
- REMOTE 2.19, FEATURES, WHATS_NEXT.

The release goes through release.ps1 -Version 2.19.0 -ModuleVersion 2.19.0. The lane's papers are SW19.11L.

Validate:

```
cd /home/user/Stagewatch && tools/test.sh; on the maintainer's PC: pwsh tools\smoke.ps1; pwsh tools\release.ps1 -Version 2.19.0 -ModuleVersion 2.19.0
```

### SW19.8 — Lane track: the rig for the interop lane — stable id, journal and report, secrets off stdout, the lockout readable

*Stagewatch.* Sources: ecosystem-lane SW-L1. Gaps: G44. Depends on: SW18.6.

ecosystem-lane SW-L1:
- a stable node id; a journal and its report;
- --pin-file and --secrets-out; --exit-on-eof;
- the `auth` command on SW18.6's reader, and WebServer.AuthFailures();
- rig-faults.e2e.cjs.

Validate:

```
cd /home/user/Stagewatch && tools/test.sh build unit e2e
```

### P90.0 — Round 90.0: the round opens

*Patterns.* Sources: Patterns CLAUDE.md 'How a round opens'; plan critique issue 19. Gaps: none. Depends on: P89.9.

PLAN §108's first lines:
- what the files said;
- OPEN: L40, L47, L50 and L56 to annotate; a row for synthetic clips to add;
- the requester: the maintainer;
- the evidence class: a handed-in critique, the Stagewatch session's ecosystem-lane design. This is not 'the maintainer's list'.

Validate:

```
cd /home/user/Patterns && grep -n '^## 108\. Round 90' docs/PLAN.md
```

### P90.1 — Round 90.1: the bench host — the real desk headless off Windows for the interop lane

*Patterns.* Sources: ecosystem-lane P-B1. Gaps: G44. Depends on: P90.0.

ecosystem-lane P-B1:
- HeadlessDesk Create/Show and TestApp;
- tools/Patterns.Bench, fenced like src/;
- BenchClip and BenchControl;
- added to Patterns.sln, with BenchHostTests.

No libVLC or WebView2 off Windows. The exe's start and the lifecycle census are unchanged.

Validate:

```
cd /home/user/Patterns && dotnet build Patterns.sln -c Debug -v q && for s in Core Rendering App; do dotnet test tests/Patterns.$s.Tests -c Debug --no-build || exit 1; done
```

### P90.2 — Round 90 papers (PLAN §108)

*Patterns.* Sources: ecosystem-lane P-B2. Gaps: G44. Depends on: P90.1.

ecosystem-lane P-B2:
- PLAN §108 completed;
- REVIEW 'Walk: none asked';
- CHANGELOG naming the evidence class;
- README names the bench;
- OPEN: L40, L47, L50 and L56 annotated, plus a row for synthetic clips;
- tag row 89.

Closing: no verb an output can show.

Validate:

```
cd /home/user/Patterns && dotnet build Patterns.sln -c Debug -v q && for s in Core Rendering Devices Audio Assistant Audience App; do dotnet test tests/Patterns.$s.Tests -c Debug --no-build || exit 1; done && cd integrations/companion-module-patterns && npm ci && npm test && node test/emit-lines.mjs | diff - test/lines.txt && npm run check
```

### SW19.9a — Lane track: the interop lane (1 of 2) — four real apps on a one-kernel LAN

*Stagewatch.* Sources: ecosystem-lane SW-L2. Gaps: G44. Depends on: SW19.8, P90.2.

ecosystem-lane SW-L2's infrastructure:
- tools/interop/lan.py: rtnetlink namespaces on one bridge, plus a PID namespace;
- siblings.json pins;
- the lane's own clones (git clone --shared, never the working trees);
- the matrix;
- a `tools/test.sh interop` lane;
- row S0.1 (each app ready in its own namespace).

Validate:

```
cd /home/user/Stagewatch && tools/test.sh build interop
```

### SW19.9b — Lane track: the interop lane (2 of 2) — the first rows

*Stagewatch.* Sources: ecosystem-lane SW-L3; ecosystem-lane SW-L5. Gaps: G44, G04. Depends on: SW19.9a, SW18.6.

ecosystem-lane SW-L3 and SW-L5. Each of these rows needs only today's apps or Stagewatch's own units:
- S1.1–S1.3;
- S4.5;
- S6.1, S6.1b, S6.4, S6.7;
- S7.1, S7.3 (first three);
- S6.2a (the stall: PlayMap shows no time);
- S1c.1, S1c.2.

Each row is added only once it passes.

Validate:

```
cd /home/user/Stagewatch && tools/test.sh build interop
```

### SW19.10 — Lane track: CI's interop job at the siblings' pins; the nightly heads run

*Stagewatch.* Sources: ecosystem-lane SW-L4; plan critique issue 15. Gaps: G44. Depends on: SW19.9b, owner:S-interop-gate, owner:S-interop-ci.

ecosystem-lane SW-L4:
- a hardened, fork-guarded interop job at the pins; the release job needs it (S-interop-gate);
- its credential and runner change come from S-interop-ci: SIBLINGS_READ_TOKEN, and the ephemeral runner's userns sysctl;
- a nightly run against the siblings' heads reports drift only.

The validate pushes nothing. The maintainer pushes, and the run checked is the one for this exact commit.

Validate:

```
cd /home/user/Stagewatch && tools/test.sh build interop; after the maintainer pushes: gh run watch --exit-status $(gh run list --workflow ci.yml --commit $(git rev-parse HEAD) --limit 1 --json databaseId -q '.[0].databaseId')
```

### SW19.11L — Lane track: the lane's papers

*Stagewatch.* Sources: ecosystem-lane SW-P (lane part); plan critique issue 15. Gaps: G44. Depends on: SW19.9b, owner:S-brief.

The lane's part of SW-P:
- docs/BUILD.md: the lane, its topologies, its clones and pins, what it proves and what it does not;
- test.sh's help text.

The brief's loop line `tools/test.sh interop` is the maintainer's to write (S-brief). WHATS_NEXT lists it as an owner action, and no unit edits CLAUDE.md.

Validate:

```
cd /home/user/Stagewatch && tools/test.sh build unit
```

### P91.0 — Round 91.0: the round opens

*Patterns.* Sources: Patterns CLAUDE.md 'How a round opens'. Gaps: none. Depends on: P90.2.

PLAN §109's first lines:
- what the files said;
- OPEN: L59 to close; QUALIFICATION §26 to note;
- the requester and the evidence class: a handed-in critique (spec-1-3, peers-not-screens, cross-app and the 1.3 contract).

Validate:

```
cd /home/user/Patterns && grep -n '^## 109\. Round 91' docs/PLAN.md
```

### P91.1 — Round 91.1: the Stagewatch link dials as a node and reads its verdict

*Patterns.* Sources: peers-not-screens PT-1; peers-not-screens PT-2; patterns-playmap-side 90.1. Gaps: G32, G43. Depends on: SW18.3, P91.0, owner:P-legacy.

peers-not-screens PT-1 and PT-2.
- SocketQuery gives '/ws?node=<ShowBusId>&t=patterns&cid=<id>'. Today cid=patterns is fixed (StagewatchLinkService.cs:276).
- The verdict is parsed. A refusal is held before any I/O: keyed calls are skipped, and redials wait.
- 403 words are shown, and a refused collect is said.
- Readers: STATE bus.stagewatch `key`, after `stuck`; the words on the Eye, the Nodes card, the context and the assistant; the module's stagewatch_key.
- Against an older Stagewatch, HTTP evidence is used. This branch exists only as P-legacy answers.

Validate:

```
cd /home/user/Patterns && dotnet build Patterns.sln -c Debug -v q && for s in Core Rendering App Assistant; do dotnet test tests/Patterns.$s.Tests -c Debug --no-build || exit 1; done && cd integrations/companion-module-patterns && npm ci && npm test && node test/emit-lines.mjs | diff - test/lines.txt && npm run check
```

### P91.2 — Round 91.2: the wire shows its state only to a paired connection (ADR-016)

*Patterns.* Sources: spec-1-3 PT-90.2; patterns-playmap-side 90.2; Eye critique (unpaired readers). Gaps: G45, G09. Depends on: P91.0, SW18.7, owner:P-ADR016.

spec-1-3 PT-90.2. ADR-016 is written in this commit, before the code.

Today:
- 9697 greets with STATE before NONCE (ControlService.cs:356-357);
- IsQuery lets EYE, CONTEXT and every non-Action verb through unpaired (ControlProtocol.cs:1454).

Now:
- NONCE first; no STATE greeting or push while unpaired.
- OpenBeforePairing allows HELLO, NONCE, AUTH, AUTH-PROOF, PING, HELP and VERBS, plus scope A's named queries.
- STATE, EYE, CONTEXT and SUBSCRIBE are refused unpaired, with words. WirePeer re-checks pairing on each push.
- The module and the test helpers pair first.
- A Super Check Remote row, and a log line per refusal.
- Loopback tests set TrustLoopback=false and restore it in finally.

Validate:

```
cd /home/user/Patterns && dotnet build Patterns.sln -c Debug -v q && for s in Core Rendering Devices Audio Assistant Audience App; do dotnet test tests/Patterns.$s.Tests -c Debug --no-build || exit 1; done && cd integrations/companion-module-patterns && npm ci && npm test && node test/emit-lines.mjs | diff - test/lines.txt && npm run check
```

### P91.3 — Round 91.3: the context lists what peers may propose; APPLY at review; the reader keeps 256

*Patterns.* Sources: cross-app-proposals PT-90.3; patterns-playmap-side 90.3; windows-for-peers PT-W3 (brief cap). Gaps: G03, G33, G35. Depends on: P91.0.

cross-app PT-90.3 and pps 90.3.
- The context gains APPLY <id> at review, plus 15 appended rows:
  - VIDEO STOP, at high;
  - the staged and per-screen take verbs;
  - the web actions except WEB OPEN and WEB TYPE.
- APPLY runs only this desk's own proposal.
- The reader keeps 256 commands, with verbs and words up to 512 characters. Today it takes 64 (Core/Services/ShowContext.cs:99).
- The brief lists 32.

Validate:

```
cd /home/user/Patterns && dotnet build Patterns.sln -c Debug -v q && for s in Core Rendering App Assistant; do dotnet test tests/Patterns.$s.Tests -c Debug --no-build || exit 1; done
```

### P91.4 — Round 91.4: certificates 825 days; the hello 1.3; the hello table bounded at 128

*Patterns.* Sources: spec-1-3 PT-90.5; Eye critique (unbounded hello table). Gaps: G39, G40. Depends on: P91.0.

spec-1-3 PT-90.5 and the Eye critique.
- TlsIdentity: 825 days in all (1,826 today), with one renewal.
- The hello: proto 3, inst, every address per id, sticky dial, conflicts amber.
- ShowBusPeers.Hear is unbounded today (ShowBusNetwork.cs:111). Cap it at 128, evicting the oldest app that is not needed, and count what was dropped.
- Tests include a forged-hello flood.

Validate:

```
cd /home/user/Patterns && dotnet build Patterns.sln -c Debug -v q && for s in Core Rendering App; do dotnet test tests/Patterns.$s.Tests -c Debug --no-build || exit 1; done
```

### P91.5 — Round 91.5: the Show Bus 1.3 copy and the kit, held by Patterns' own tests

*Patterns.* Sources: patterns-playmap-side 90.4; cross-app-proposals PT-90.4; spec-1-3 PT-90.4; peers-not-screens PT-3; plan critique issue 8. Gaps: G38, G40, G22. Depends on: P91.0, SW19.12.

- docs/SHOWBUS.md becomes a copy of the 1.3 master, and tools/showbus/peerlink-peer.mjs is copied in, byte-identical.
- ShowBusSpecTests check ```showbus and ```peerlink, and both hashes against the numbers pinned in the master's §14. No validate calls into another tree.
- If 1.3 is not final when 91.1–91.4 have landed, this unit moves to the next round (rule R3).

Validate:

```
cd /home/user/Patterns && dotnet build Patterns.sln -c Debug -v q && for s in Core Rendering App; do dotnet test tests/Patterns.$s.Tests -c Debug --no-build || exit 1; done
```

### P91.6 — Round 91 papers (PLAN §109)

*Patterns.* Sources: patterns-playmap-side 90.4; spec-1-3 PT-90.4. Gaps: G45. Depends on: P91.3, P91.4.

- PLAN §109 completed;
- REVIEW 'Walk: none asked';
- CHANGELOG, README, REMOTE, COMPANION (the module's minor version for stagewatch_key);
- OPEN L59 closed; a QUALIFICATION §26 note;
- tag row 90.

Closing: the refused unpaired read, beside its log line.

91.1, 91.2 and 91.5 wait on an answer or another repo. Each lands before this unit or moves to a later round (rule R3).

Validate:

```
cd /home/user/Patterns && dotnet build Patterns.sln -c Debug -v q && for s in Core Rendering Devices Audio Assistant Audience App; do dotnet test tests/Patterns.$s.Tests -c Debug --no-build || exit 1; done && cd integrations/companion-module-patterns && npm ci && npm test && node test/emit-lines.mjs | diff - test/lines.txt && npm run check
```

### PM-4 — fix-showbus-1.3: the 1.3 copy and the kit; SHOWBUS-NODE corrected; the hello reader's limits

*Mapper.* Sources: spec-1-3 M1; patterns-playmap-side PM-F5; peers-not-screens PM-3; playout-links Mapper unit (doc rows); plan critique issue 11. Gaps: G35, G38, G40, G30, G22. Depends on: SW19.12, owner:M-PMX.

spec-1-3 M1, PM-F5, pns PM-3 and the playout-links doc rows.
- docs/SHOWBUS.md (new here) and tools/showbus/peerlink-peer.mjs, byte-identical with the master. ShowBusSpecTests (Core.Tests/Bus, new) hold them to §14's pinned hashes.
- The reader (Net/Bus/HelloService.cs) keeps 32 caps and 128 peers, speaks proto 3, follows the address rule and shows conflicts.
- SHOWBUS-NODE.md :37, :40, :49-50 and :72-73 corrected: Patterns' devices do not pair by proof today (G35). Also the trigger requirement (G30) and 'never paste a window address'.
- FLEET P1–P3.
- WIRE's 'refused' row goes to the integrator as a papers note: WIRE, README and ROADMAP are the integrator's (round3/PLAN.md:2198).
- A CARRYOVER entry.

Files (M-PMX): docs/SHOWBUS.md, tools/showbus/, Core.Tests/Bus/ShowBusSpecTests.cs, Net/Bus/HelloService.cs, docs/SHOWBUS-NODE.md, docs/FLEET.md, docs/round2/CARRYOVER.md.

Validate:

```
cd /home/user/Mapper && dotnet build PlayMap.sln -c Debug -v q && dotnet test tests/PlayMap.Core.Tests -c Debug --no-build --filter "FullyQualifiedName~ShowBusSpecTests" -- RunConfiguration.TreatNoTestsAsError=true && dotnet test tests/PlayMap.Net.Tests -c Debug --no-build --filter "FullyQualifiedName~HelloTests" -- RunConfiguration.TreatNoTestsAsError=true
```

### PM-5 — fix-showbus-node-link: PlayMap dials Stagewatch as a node; 'paired' from the verdict

*Mapper.* Sources: peers-not-screens PM-2; plan critique issue 11. Gaps: G06, G43, G31. Depends on: SW18.3, PM-2, owner:M-PMX.

pns PM-2.
- StagewatchLink dials node=<SelfId>&t=playmap&cid=<id> (:283-284).
- Pairing comes from the verdict or a keyed 2xx, set before any I/O. paired = Paired or NotNeeded.
- A 'refused' state, with a red arm in LinksCheck.Row (src/PlayMap.Engine/Show/PreflightBreadth.cs:115). The file exists; the type is LinksCheck, not PreflightBreadth.
- Holds recorded before I/O.
- 403 words, and a refused collect said.
- The ContextBuilder and Seams lines go through the §5.7 spine lane.

Files (M-PMX): Net/Links/StagewatchLink.cs, Engine/Show/PreflightBreadth.cs.

Validate:

```
cd /home/user/Mapper && dotnet build PlayMap.sln -c Debug -v q && dotnet test tests/PlayMap.Net.Tests -c Debug --no-build --filter "FullyQualifiedName~StagewatchLinkTests" -- RunConfiguration.TreatNoTestsAsError=true && dotnet test tests/PlayMap.Engine.Tests -c Debug --no-build --filter "FullyQualifiedName~PreflightBreadthTests" -- RunConfiguration.TreatNoTestsAsError=true
```

### SW19.13 — Lane track: keys from the rig, the rig on the bus, contexts both ways — the rows Stagewatch and Patterns units carry

*Stagewatch.* Sources: ecosystem-lane SW-R1; plan critique issue 5. Gaps: G44. Depends on: SW19.9b, P91.6.

ecosystem-lane SW-R1, the rows whose needs are Stagewatch's or Patterns' units, at bumped pins:
- S1.4, S1.5;
- S2.3, S2.4, S2.7;
- the Patterns and today's-PlayMap legs of S1.6, S2.1 and S2.6.

AGMessage's rows (S2.2, S2.5) are in SW-INT-AG.

Validate:

```
cd /home/user/Stagewatch && tools/test.sh build interop
```

### SW19.13m — Lane track: PlayMap's node-link and 1.3-reader legs

*Stagewatch.* Sources: ecosystem-lane SW-R1 (PlayMap legs); plan critique issue 5. Gaps: G44. Depends on: SW19.9b, PM-4, PM-5.

S1.6's PlayMap reader leg, and a row for PlayMap's node-link verdict, at PlayMap's bumped pin.

This unit slips with PM-4 and PM-5 (rule R3). No Stagewatch release depends on it.

Validate:

```
cd /home/user/Stagewatch && tools/test.sh build interop
```

## Train 3 — Every link honest from its own end (Stagewatch 2.20 playout links; Patterns Rounds 92–93; PlayMap PeerLink core batches)

**Goal.** Every connector keeps the profile's unilateral rules at level 0 (Phase 1):
- matched pongs;
- five outcomes from one table, and unconfirmed is never 'failed';
- ids with inst;
- a jittered redial, reset only after a verified 10 s;
- persisted holds;
- link and show liveness kept apart;
- results retried;
- one journal row per command and per state change.

Stagewatch runs a list of playout links to Patterns and PlayMap, with real receipts and timed GO on a fit clock. It reads Patterns' clip role and key, so a playlist item or a stinger is never 'paused' by a receipt that lies. Patterns holds a playlist item and drives a clip by its key (L48, L49).

Validated by PeerLinkVectors, the connector scenarios and the VT interop rows.

| Unit | Repo | Title | Depends on |
|---|---|---|---|
| SW20.1 | Stagewatch | Playout model and grammar: settings v2, PlayoutCue, ShowCommandKind.Playout, JournalKind.Playout | SW19.12 |
| SW20.2 | Stagewatch | Who may drive playout: PlayoutGate and the OSC/Companion/HTTP gates | SW20.1 |
| SW20.3 | Stagewatch | Wire primitives: PlayoutWire (a finder that hears playmap) and PlayoutStateReader | SW20.1, SW19.2a |
| SW20.4a | Stagewatch | PlayoutLink (1 of 2): the connection on the PeerLink core | SW20.3, SW18.7 |
| SW20.4b | Stagewatch | PlayoutLink (2 of 2): the writer lane, persisted holds and the journal; PatternsLink retired | SW20.4a |
| SW20.5 | Stagewatch | The link set, PlayoutDirector and receipts: the peer's real outcome completes the receipt | SW20.4b, SW20.2 |
| SW20.6 | Stagewatch | PlayMap over pinned TLS; Trust a changed certificate by typing it | SW20.5 |
| SW20.7 | Stagewatch | Running-order cues and timed GO on a fit clock | SW20.5 |
| SW20.8 | Stagewatch | HTTP triggers authenticate: secrets only to loopback or a pinned HTTPS server | SW20.1 |
| SW20.9 | Stagewatch | 413 for a request body over 64 KiB, drained so the sender reads it | — |
| SW20.10 | Stagewatch | Rig message signals and message.e2e.cjs | SW18.6 |
| SW20.11 | Stagewatch | The desk for playout (PC work): Settings links, May drive playout, trigger auth, RundownEditor cues, menus | SW20.7, SW20.8, SW20.6 |
| SW20.12 | Stagewatch | Companion module: playout actions and vt_link | SW20.5 |
| P92.0 | Patterns | Round 92.0: the round opens | P91.6 |
| P92.1 | Patterns | Round 92.1: PeerLink core in Patterns.Core, the vectors, the token's canonical form, the clock's admission | P92.0, P91.5 |
| P92.2 | Patterns | Round 92.2: the Stagewatch link on the PeerLink core | P92.1 |
| P92.3 | Patterns | Round 92.3: the link's record in every reader | P92.2 |
| P92.4 | Patterns | Round 92.4: STAGEWATCH verbs — set and follow the link from the wire, the deck and the menus; trust and accept only on the desk | P92.3 |
| P92.5 | Patterns | Round 92 papers (PLAN §110) | P92.3, P92.4 |
| P93.0 | Patterns | Round 93.0: the round opens | P92.5 |
| P93.1 | Patterns | Round 93.1: one speaker message — through Stagewatch when the link can carry it, here with words when not | P93.0, P92.3, SW20.10 |
| P93.2 | Patterns | Round 93.2: hold a playlist item or a stinger's clip; video.pausable and video.stoppable | P93.0 |
| P93.3 | Patterns | Round 93.3: VIDEO … ON <key> drives a layer's or a screen's own clip (L49) | P93.0 |
| P93.4 | Patterns | Round 93 papers (PLAN §111) | P93.2, P93.3 |
| SW20.13 | Stagewatch | Interop: the VT rows (S4.1–S4.4) | SW20.5, SW20.6, SW19.9b, P93.4 |
| SW20.14 | Stagewatch | Stagewatch 2.20 papers and release | SW20.11, SW20.12, SW20.9, SW20.10, SW20.13 |
| PM-6 | Mapper | fix-peerlink-core: PlayMap's PeerLink core and its links' hygiene | PM-4, owner:M-PMX |
| PM-7 | Mapper | fix-stagewatch-timer-air: blackout and watermark in STATE's timer and on the timer surface | owner:M-PMX |
| PM-8 | Mapper | fix-stagewatch-context-size: the posted context fits 64 KiB by bytes; STATE's size fenced | owner:M-PMX |

### SW20.1 — Playout model and grammar: settings v2, PlayoutCue, ShowCommandKind.Playout, JournalKind.Playout

*Stagewatch.* Sources: playout-links unit 1. Gaps: G01, G29. Depends on: SW19.12.

playout-links unit 1.
- Settings v2 with migration: the one Patterns link becomes a list keyed by hello id and kind (G01).
- PlayoutCue.
- ShowCommandKind.Playout appended, with parser, help, High risk and context rows.
- JournalKind.Playout.
- Sample times and reserved names.

Validate:

```
cd /home/user/Stagewatch && tools/test.sh build unit
```

### SW20.2 — Who may drive playout: PlayoutGate and the OSC/Companion/HTTP gates

*Stagewatch.* Sources: playout-links unit 2. Gaps: G01. Depends on: SW20.1.

playout-links unit 2.
- PlayoutGate: the shared sender gate.
- `playout` and `patterns` lines from other machines over OSC or Companion TCP are refused unless listed.
- An AI's line is always a proposal.

Validate:

```
cd /home/user/Stagewatch && tools/test.sh build unit
```

### SW20.3 — Wire primitives: PlayoutWire (a finder that hears playmap) and PlayoutStateReader

*Stagewatch.* Sources: playout-links unit 3. Gaps: G01, G26. Depends on: SW20.1, SW19.2a.

playout-links unit 3.
- PlayoutWire: legacy proof rows from the core, quoting, and ParseBeacon accepting playmap. Today it accepts only 'patterns' (PatternsLink.cs:459).
- PlayoutStateReader reads at least 256 KiB. An oversize STATE is read in part and shown amber (G26).

Validate:

```
cd /home/user/Stagewatch && tools/test.sh build unit
```

### SW20.4a — PlayoutLink (1 of 2): the connection on the PeerLink core

*Stagewatch.* Sources: playout-links unit 4; PeerLink profile SW-PL0. Gaps: G01, G07, G08, G26. Depends on: SW20.3, SW18.7.

playout-links unit 4 plus SW-PL0, keeping SW18.7's rules. Each line is today's behaviour, then the change:
- ids per run (PatternsLink.cs:167) → s<n>-<inst>;
- evicted past 64 (:169) → at most 32 in flight, none evicted;
- a drop fails every pending command (:225-226) → 13.12.3 outcomes, with a drop after a write unconfirmed;
- any OK PONG counts as a sample (:318-327) → matched pongs only;
- 250 ms doubling, no jitter, reset on connect (:189,209,229-230) → 0.5 → 5 s × (0.8+0.4j), reset only after verified + 10 s.

Scenarios S1, S11, S13, S14.

Validate:

```
cd /home/user/Stagewatch && tools/test.sh build unit
```

### SW20.4b — PlayoutLink (2 of 2): the writer lane, persisted holds and the journal; PatternsLink retired

*Stagewatch.* Sources: playout-links unit 4; PeerLink profile SW-PL0. Gaps: G01, G48. Depends on: SW20.4a.

- Writes move from the show thread (PatternsLink.cs:172-176) to a writer lane.
- Persisted holds.
- A TimeProvider.
- A journal row per command and per state change.
- PatternsLink.cs is removed once every reader has moved.

Scenarios S19, S22.

Validate:

```
cd /home/user/Stagewatch && tools/test.sh build unit
```

### SW20.5 — The link set, PlayoutDirector and receipts: the peer's real outcome completes the receipt

*Stagewatch.* Sources: playout-links unit 5; gap G48 (Stagewatch half). Gaps: G01, G07, G48. Depends on: SW20.4b, SW20.2.

playout-links unit 5.
- PlayoutLinks and PlayoutDirector.
- PlayoutReceipts, keyed by request id and completed in the peer's own words (G07).
- final on WS and Companion.
- A link:<name> and a clock:<id> item per link.
- Frame and config fields, AI facts.
- rig --playout and e2e.

G48's Stagewatch half:
- it parses STATE.video role, key, pausable and stoppable (CommandRouter.cs:237-252 publish them);
- it refuses pause and stop for a playlist item or a stinger's clip, in Patterns' words, until pausable/stoppable say otherwise (P93.2);
- it sends VIDEO … ON <key> only when Patterns' context lists that form (P93.3), else it drives the clip on air.

Validate:

```
cd /home/user/Stagewatch && tools/test.sh build unit e2e
```

### SW20.6 — PlayMap over pinned TLS; Trust a changed certificate by typing it

*Stagewatch.* Sources: playout-links unit 6; PeerLink profile 13.8.3; Eye critique issue 4. Gaps: G08. Depends on: SW20.5.

playout-links unit 6.
- WSS to playmap.lines.v1, pinned per node id, never plain once pinned.
- 429 and 4401 words.
- Trust a changed certificate by typing at least 8 hex (13.8.3). It is never pre-filled and never on a pad.
- A High-risk ShowCommandKind, not in ShowContext.Commands.

Validate:

```
cd /home/user/Stagewatch && tools/test.sh build unit
```

### SW20.7 — Running-order cues and timed GO on a fit clock

*Stagewatch.* Sources: playout-links unit 7; PeerLink profile 13.11.6. Gaps: G28, G29. Depends on: SW20.5.

playout-links unit 7.
- The planner. Standby is opt-in and off by default. Hold-until.
- One GO per sync group. 'Scheduled' and 'late' rows.
- AT only on a clock fit for the verb (fresh ≤ 10 s, errMs within tolerance), measured over TLS or locally. Not to Patterns' plain 9697 until Round 97, and the row says so.
- rig --cue and e2e.

Validate:

```
cd /home/user/Stagewatch && tools/test.sh build unit e2e
```

### SW20.8 — HTTP triggers authenticate: secrets only to loopback or a pinned HTTPS server

*Stagewatch.* Sources: playout-links unit 8. Gaps: G30. Depends on: SW20.1.

playout-links unit 8.
- Auth profiles, kept with DPAPI and never journalled.
- Loopback or pinned HTTPS only, with no redirects and a fixed authority.
- X-Request-Id.
- Replies read by 13.12.3.

Validate:

```
cd /home/user/Stagewatch && tools/test.sh build unit
```

### SW20.9 — 413 for a request body over 64 KiB, drained so the sender reads it

*Stagewatch.* Sources: patterns-playmap-side SW-25. Gaps: G25. Depends on: nothing.

patterns-playmap-side SW-25.
- 413 for a body over 64 KiB, after draining it.
- Deferred paths are exempt.
- No body is logged.
- SW21.1's byte caps rely on it.

Validate:

```
cd /home/user/Stagewatch && tools/test.sh build unit
```

### SW20.10 — Rig message signals and message.e2e.cjs

*Stagewatch.* Sources: patterns-playmap-side SW-RM. Gaps: G21. Depends on: SW18.6.

patterns-playmap-side SW-RM.
- Rig message signals.
- Receipts carry refs.messageId.
- Frames carry messageId, messageAck and messageAckBy.
- The ack path.
- message.e2e.cjs.

Validate:

```
cd /home/user/Stagewatch && tools/test.sh build unit e2e
```

### SW20.11 — The desk for playout (PC work): Settings links, May drive playout, trigger auth, RundownEditor cues, menus

*Stagewatch.* Sources: playout-links unit 9. Gaps: G01, G29, G30. Depends on: SW20.7, SW20.8, SW20.6.

playout-links unit 9, the maintainer's PC work:
- Settings playout links; 'May drive playout'; trigger auth;
- RundownEditor cues;
- the VT chip and running-order menus, in step with the buttons;
- Help; UiSmoke.

Validate:

```
cd /home/user/Stagewatch && tools/test.sh build unit; on the maintainer's PC: pwsh tools\smoke.ps1
```

### SW20.12 — Companion module: playout actions and vt_link

*Stagewatch.* Sources: playout-links unit 10. Gaps: G01, G29. Depends on: SW20.5.

playout-links unit 10:
- companion-module playout actions;
- the vt_link variable and feedback;
- tests.

Validate:

```
cd /home/user/Stagewatch && tools/test.sh module
```

### P92.0 — Round 92.0: the round opens

*Patterns.* Sources: Patterns CLAUDE.md 'How a round opens'. Gaps: none. Depends on: P91.6.

PLAN §110's first lines:
- what the files said;
- OPEN rows to touch;
- the requester;
- the evidence class: a handed-in critique, the PeerLink profile.

Validate:

```
cd /home/user/Patterns && grep -n '^## 110\. Round 92' docs/PLAN.md
```

### P92.1 — Round 92.1: PeerLink core in Patterns.Core, the vectors, the token's canonical form, the clock's admission

*Patterns.* Sources: PeerLink profile Patterns 'Round 90' core unit. Gaps: G08. Depends on: P92.0, P91.5.

- Patterns.Core/Services/PeerLink.cs (no package references), with PeerLinkVectorsTests reading the vectors block from the 1.3 copy.
- The LinkRecord type (13.16.3), with no secret fields.
- PairingToken. Today it strips only five characters (PairingToken.cs:54-63) and is typed in a free text box (WebSection.axaml:38). Now canon follows 13.5.1, and a token that is not [A-Z0-9]{1,64} is refused at entry.
- LinkClock: admission by 13.11.2, and Apart as |offset| − err > 2 s (LinkClock.cs:59-63,97).
- CI: the test job gains setup-node 22. Today only the module job has Node (build.yml:76-78).
- PeerLinkWireTests spawns the scripted peer.

Validate:

```
cd /home/user/Patterns && dotnet build Patterns.sln -c Debug -v q && for s in Core Rendering App; do dotnet test tests/Patterns.$s.Tests -c Debug --no-build || exit 1; done
```

### P92.2 — Round 92.2: the Stagewatch link on the PeerLink core

*Patterns.* Sources: PeerLink profile Patterns 'Round 90' link unit. Gaps: G16, G08. Depends on: P92.1.

StagewatchLinkService moves onto the core:
- the attempt count resets only after verified + 10 s (:184-185);
- persisted holds are recorded before any I/O;
- side calls get their own lane, and the beat and drop check their own timer (:324-355);
- matched pongs;
- results are retried 3→30 s for 30 minutes, written ahead, with a bounded LRU (:460-465);
- a TimeProvider over ShowClock (:79-80);
- the legacy-bearer rule.

Level-0 scenarios S5, S6, S11–S14 and S19.

Validate:

```
cd /home/user/Patterns && dotnet build Patterns.sln -c Debug -v q && for s in Core Rendering App; do dotnet test tests/Patterns.$s.Tests -c Debug --no-build || exit 1; done
```

### P92.3 — Round 92.3: the link's record in every reader

*Patterns.* Sources: PeerLink profile Patterns 'Round 90' link unit (readers). Gaps: G16. Depends on: P92.2.

The 13.16.3 record reaches:
- STATE bus.stagewatch `link`, after `key`;
- the Eye's app node and edge: the record's light, mark and words;
- the Nodes card and the Run strip;
- Companion stagewatch_link_state and stagewatch_link_flags, with lines.txt regenerated and the module's minor version;
- the assistant's facts;
- a Super Check row, 'Show network links';
- a journal row and a log line per change.

Validate:

```
cd /home/user/Patterns && dotnet build Patterns.sln -c Debug -v q && for s in Core Rendering App Assistant; do dotnet test tests/Patterns.$s.Tests -c Debug --no-build || exit 1; done && cd integrations/companion-module-patterns && npm ci && npm test && node test/emit-lines.mjs | diff - test/lines.txt && npm run check
```

### P92.4 — Round 92.4: STAGEWATCH verbs — set and follow the link from the wire, the deck and the menus; trust and accept only on the desk

*Patterns.* Sources: patterns-playmap-side 91.1; PeerLink profile 13.16.6; Eye critique issue 4; plan critique (low: P92.3 surfaces). Gaps: G34. Depends on: P92.3.

patterns-playmap-side 91.1 (G34), plus the trust verbs.

On the wire, the deck, the menus and the module:
- STAGEWATCH LINK <host[:port]>|AUTO|OFF, FOLLOW, STATUS, LINK AGAIN;
- FLOOR LOWER and RELEASE.

On the desk and its right-click menus only:
- TRUST: first use, one press beside the short fingerprint;
- ACCEPT <≥8 hex typed>: never pre-filled.
- The wire, a deck and the assistant are refused with 'TRUST and ACCEPT are a person's choice on the desk, beside the fingerprint'. The module has no action for them.

Also:
- KeyFor bind rules: the key is typed on the desk and sent only to its bound Stagewatch.
- The Nodes Accept button calls the service directly today (MainViewModel.Nodes.cs:46). It moves to the same ShowAction, with a journal row.
- Menus in step; never proposed.
- STATE appends follow and pending.
- ControlProtocolTests live in Rendering.Tests (RemotePresenterWarpTests.cs:9), which this validate runs.

Validate:

```
cd /home/user/Patterns && dotnet build Patterns.sln -c Debug -v q && for s in Core Rendering App; do dotnet test tests/Patterns.$s.Tests -c Debug --no-build || exit 1; done && cd integrations/companion-module-patterns && npm ci && npm test && node test/emit-lines.mjs | diff - test/lines.txt && npm run check
```

### P92.5 — Round 92 papers (PLAN §110)

*Patterns.* Sources: PeerLink profile 6.2 process. Gaps: G34. Depends on: P92.3, P92.4.

- PLAN §110 completed.
- REVIEW 'Walk: none asked'. The twin moved to 89.6, so this round touches no platform path.
- CHANGELOG, README.
- REMOTE: the STAGEWATCH verbs and their refusals, the link/follow/pending fields, the token's canon.
- COMPANION; OPEN.
- Tag row 91.

Closing: STAGEWATCH LINK's journal rows and 'Show network links', beside the Nodes card.

Validate:

```
cd /home/user/Patterns && dotnet build Patterns.sln -c Debug -v q && for s in Core Rendering Devices Audio Assistant Audience App; do dotnet test tests/Patterns.$s.Tests -c Debug --no-build || exit 1; done && cd integrations/companion-module-patterns && npm ci && npm test && node test/emit-lines.mjs | diff - test/lines.txt && npm run check
```

### P93.0 — Round 93.0: the round opens

*Patterns.* Sources: Patterns CLAUDE.md 'How a round opens'. Gaps: none. Depends on: P92.5.

PLAN §111's first lines:
- what the files said;
- OPEN: L48 and L49 to close;
- the requester;
- the evidence class: a handed-in critique (patterns-playmap-side, and gap G48).

Validate:

```
cd /home/user/Patterns && grep -n '^## 111\. Round 93' docs/PLAN.md
```

### P93.1 — Round 93.1: one speaker message — through Stagewatch when the link can carry it, here with words when not

*Patterns.* Sources: patterns-playmap-side 91.2. Gaps: G21. Depends on: P93.0, P92.3, SW20.10.

pps 91.2.
- While following with a node key, STAGE MESSAGE goes as POST /api/message/<text>/<urgency>/off[/ack], with X-Request-Id, on the side-call lane.
- Receipt stages and the ACK are shown; the latest message wins.
- Otherwise it stays local, with words.
- STATE stage appends speakerFrom, speakerAck and speakerAckBy.
- The module.
- Tested against the bytes message.e2e.cjs pins, checked into Patterns' tests.

Validate:

```
cd /home/user/Patterns && dotnet build Patterns.sln -c Debug -v q && for s in Core Rendering App; do dotnet test tests/Patterns.$s.Tests -c Debug --no-build || exit 1; done && cd integrations/companion-module-patterns && npm ci && npm test && node test/emit-lines.mjs | diff - test/lines.txt && npm run check
```

### P93.2 — Round 93.2: hold a playlist item or a stinger's clip; video.pausable and video.stoppable

*Patterns.* Sources: patterns-playmap-side 91.3. Gaps: G48. Depends on: P93.0.

pps 91.3 (OPEN L48).
- VIDEO PAUSE and STOP hold a playlist item's or a stinger's clip, and its clock. Today they are refused (ShowActions.Content.cs:481,486,501).
- An entry held past its window is skipped and journalled.
- STATE video appends pausable and stoppable.

Validate:

```
cd /home/user/Patterns && dotnet build Patterns.sln -c Debug -v q && for s in Core Rendering App; do dotnet test tests/Patterns.$s.Tests -c Debug --no-build || exit 1; done
```

### P93.3 — Round 93.3: VIDEO … ON <key> drives a layer's or a screen's own clip (L49)

*Patterns.* Sources: gap G48 (L49); plan critique unmapped G48. Gaps: G48. Depends on: P93.0.

G48's remaining half (OPEN L49).
- The transport takes ON <key>, with the key from STATE videos[]. It is refused for a key that is not mounted.
- The context lists the form, so Stagewatch sends it only when Patterns offers it.
- REMOTE grammar; the module's action and lines.txt.
- ControlProtocolTests (Rendering.Tests) and VideoTransportAppTests.

Validate:

```
cd /home/user/Patterns && dotnet build Patterns.sln -c Debug -v q && for s in Core Rendering App; do dotnet test tests/Patterns.$s.Tests -c Debug --no-build || exit 1; done && cd integrations/companion-module-patterns && npm ci && npm test && node test/emit-lines.mjs | diff - test/lines.txt && npm run check
```

### P93.4 — Round 93 papers (PLAN §111)

*Patterns.* Sources: patterns-playmap-side 91.4. Gaps: G21, G48. Depends on: P93.2, P93.3.

- PLAN §111 completed.
- REVIEW 'Walk: none asked'.
- CHANGELOG, README, REMOTE, COMPANION.
- OPEN L48 and L49 closed.
- Tag row 92.

Closing: STAGE MESSAGE's journal row with the receipt's stage, beside the stage page.

93.1 waits on Stagewatch's SW20.10. It lands before this unit or moves (rule R3).

Validate:

```
cd /home/user/Patterns && dotnet build Patterns.sln -c Debug -v q && for s in Core Rendering Devices Audio Assistant Audience App; do dotnet test tests/Patterns.$s.Tests -c Debug --no-build || exit 1; done && cd integrations/companion-module-patterns && npm ci && npm test && node test/emit-lines.mjs | diff - test/lines.txt && npm run check
```

### SW20.13 — Interop: the VT rows (S4.1–S4.4)

*Stagewatch.* Sources: ecosystem-lane SW-R4. Gaps: G44, G01, G07. Depends on: SW20.5, SW20.6, SW19.9b, P93.4.

ecosystem-lane SW-R4: rows S4.1–S4.4 at bumped pins, against the Patterns bench and the PlayMap engine.

S4.2 and S4.3 test PlayMap at its pin and need no Mapper unit (the lane's row table: 'playout-links 1–5'), so nothing here slips with PlayMap.

Validate:

```
cd /home/user/Stagewatch && tools/test.sh build interop
```

### SW20.14 — Stagewatch 2.20 papers and release

*Stagewatch.* Sources: playout-links unit 11. Gaps: G01, G07, G26, G28, G29, G30. Depends on: SW20.11, SW20.12, SW20.9, SW20.10, SW20.13.

playout-links unit 11:
- REMOTE 2.20; COMPANION; FEATURES; NODES §5; README;
- WHATS_NEXT 2.20, with the rollback note and the OSC/Companion change;
- §14's `landed` list gains each copy that has landed (Patterns from P91.5; PlayMap from PM-4).

The release goes through release.ps1 -Version 2.20.0 -ModuleVersion 2.20.0.

Validate:

```
cd /home/user/Stagewatch && tools/test.sh && tools/test.sh interop; on the maintainer's PC: pwsh tools\smoke.ps1; pwsh tools\release.ps1 -Version 2.20.0 -ModuleVersion 2.20.0
```

### PM-6 — fix-peerlink-core: PlayMap's PeerLink core and its links' hygiene

*Mapper.* Sources: PeerLink profile PM-PL1; PeerLink profile PM-PL0 (rest); plan critique issue 11. Gaps: G16, G08. Depends on: PM-4, owner:M-PMX.

PM-PL1 and the rest of PM-PL0.

Core:
- Core/Bus/PeerLink.cs (new) and PeerLinkVectorsTests;
- Liveness.Of inclusive (ShowBus.cs:226-229);
- ClockOffsetEstimator admission (Core/Time/ClockOffsetEstimator.cs);
- Pairing.Normalise by 13.5.1.

WireClient:
- an unanswered command is unconfirmed, not failed (:158-165);
- ids pm<n>-<inst> (:153);
- reset after verified + 10 s (:247).

StagewatchLink:
- only a state frame clears _stuck (:381);
- side calls move off the beat loop (:438-475);
- the reset rule (:236-237);
- results retried (:538-552).

Also:
- a held blackout shows its age (NetworkHost.cs:696-714). This is its one home; PM-11 no longer carries it.
- The desk↔engine link stays level 0 on loopback.

Files (M-PMX): Core/Bus/{PeerLink,ShowBus,Pairing}.cs, Core/Time/ClockOffsetEstimator.cs, Net/Wire/WireClient.cs, Net/Links/StagewatchLink.cs, Engine/Host/NetworkHost.cs.

Validate:

```
cd /home/user/Mapper && dotnet build PlayMap.sln -c Debug -v q && dotnet test tests/PlayMap.Core.Tests -c Debug --no-build --filter "FullyQualifiedName~PeerLinkVectorsTests|FullyQualifiedName~WireAndBusTests|FullyQualifiedName~NetworkClockTests" -- RunConfiguration.TreatNoTestsAsError=true && dotnet test tests/PlayMap.Net.Tests -c Debug --no-build
```

### PM-7 — fix-stagewatch-timer-air: blackout and watermark in STATE's timer and on the timer surface

*Mapper.* Sources: patterns-playmap-side PM-F3; windows-for-peers PM-W2 (air half); plan critique issues 11, 17. Gaps: G12. Depends on: owner:M-PMX.

pps PM-F3, G12's air half.
- Blackout and watermark go into the timer JSON (Net/Links/StagewatchTimer.cs), which StateWriter writes as given (StateWriter.cs:185,503). No spine edit.
- They are drawn on the timer surface (Engine/Rendering/TimerView.cs). Engine/Rendering/** was wp3-rhi's in W1 (round3/PLAN.md:578), so this lands outside W1 under M-PMX.
- The field order is fixed for PM-14.
- Tests: StagewatchTimerTests (Net, new); TimerViewRenderTests (Render, new), through its BackendFixture leg.
- No STATE fixture holds links.stagewatch.timer today, so the fixture tests run to show nothing else moved.

Validate:

```
cd /home/user/Mapper && (Xvfb :99 &) && export DISPLAY=:99 && dotnet build PlayMap.sln -c Debug -v q && dotnet test tests/PlayMap.Net.Tests -c Debug --no-build --filter "FullyQualifiedName~StagewatchTimerTests" -- RunConfiguration.TreatNoTestsAsError=true && dotnet test tests/PlayMap.Render.Tests -c Debug --no-build --filter "FullyQualifiedName~TimerViewRenderTests" -- RunConfiguration.TreatNoTestsAsError=true && dotnet test tests/PlayMap.Engine.Tests -c Debug --no-build --filter "FullyQualifiedName~FixtureTest" -- RunConfiguration.TreatNoTestsAsError=true
```

### PM-8 — fix-stagewatch-context-size: the posted context fits 64 KiB by bytes; STATE's size fenced

*Mapper.* Sources: patterns-playmap-side PM-F4; playout-links Mapper unit (fence). Gaps: G25, G26. Depends on: owner:M-PMX.

pps PM-F4 and the playout-links fence.
- The posted context stays within 64 KiB by bytes. It is trimmed at the poster (Net/Links/StagewatchLink.cs), never in ContextBuilder, in a fixed order, and it publishes context:trimmed.
- It reads Stagewatch's 413.
- A fence (WireServerTests) holds a HELLO Stagewatch session's STATE within the reader's bound and one clock.
- If the fence finds a real show past the bound, a video-shaped STATE for that session (G26's fix) becomes a follow-up batch. Measured, then governed.

Validate:

```
cd /home/user/Mapper && dotnet build PlayMap.sln -c Debug -v q && dotnet test tests/PlayMap.Net.Tests -c Debug --no-build --filter "FullyQualifiedName~StagewatchLinkTests|FullyQualifiedName~WireServerTests" -- RunConfiguration.TreatNoTestsAsError=true && dotnet test tests/PlayMap.Engine.Tests -c Debug --no-build --filter "FullyQualifiedName~ContextLinkTests" -- RunConfiguration.TreatNoTestsAsError=true
```

## Train 4 — One desk for every app, one verdict for the room, the Eye's network band (Stagewatch 2.21; Patterns Round 94)

**Goal.** From Stagewatch's shared queue, the operator sends any app a line it lists, and decides any AI proposal for any app, with each app's own risk read right (release A). The room gets one 'ready for doors' verdict and one merged report. Patterns' Eye draws other apps in a bounded NETWORK band whose labels stay readable at fit, without serialising every context each second.

Validated by the desk and preflight e2e, the interop desk, doors and lockout rows, and Round 94's layout fence.

| Unit | Repo | Title | Depends on |
|---|---|---|---|
| SW21.1 | Stagewatch | The verb table and PeerContexts' caps: one owner for every reader's numbers | SW18.1, SW20.9 |
| SW21.2 | Stagewatch | The desk's rules: run-tagged ids, LineFence, self-raise, last collect, §6-notation rows, peers' documented lines | SW21.1 |
| SW21.3 | Stagewatch | DeskService, LineRisk/CommandRisk.Of in Core, the shared-approve tick fix, the receipt-notes stack | SW21.2 |
| SW21.4 | Stagewatch | Release A grammar: `peer <app> <line>` and `desk approve\|reject <id>` on HTTP and WS, with receipts | SW21.3, SW18.2, SW20.2 |
| SW21.5 | Stagewatch | Release A readers: the operator frame's desk block, config peers, context collects, the producer page | SW21.4 |
| SW21.6 | Stagewatch | The desk (PC work): 'Propose to <app>…', the send dialog with live risk, approval greys | SW21.4 |
| SW21.7 | Stagewatch | One report: verified peers' lines and every desk decision, rejections included | SW18.2, SW21.3 |
| SW21.8 | Stagewatch | One preflight: 'ready for doors' with a reason per node, on every surface; the rig's preflight | SW21.5, SW19.3 |
| SW21.9 | Stagewatch | Interop: the desk rows, Patterns' desk and stuck rows, the doors, nobody locks anybody out | SW21.4, SW21.7, SW21.8, SW19.13, P91.6, P89.9 |
| SW21.10 | Stagewatch | Stagewatch 2.21 papers and release | SW21.6, SW21.9 |
| P94.0 | Patterns | Round 94.0: the round opens | P93.4 |
| P94.1 | Patterns | Round 94.1: the NETWORK band — per-app clusters on a band of their own, bounded by the canvas | P94.0 |
| P94.2 | Patterns | Round 94.2: the Eye's cost and marks — revisions not contexts, the walk cached, parallel edges, shapes at every scale | P94.1 |
| P94.3 | Patterns | Round 94 papers (PLAN §112) | P94.2 |

### SW21.1 — The verb table and PeerContexts' caps: one owner for every reader's numbers

*Stagewatch.* Sources: cross-app-proposals SW-1; windows-for-peers SW-W1 (caps). Gaps: G02. Depends on: SW18.1, SW20.9.

cross-app SW-1.
- VerbSyntax/VerbTable: '[' ends the fixed words, and 'A|B' are alternatives.
- PeerContexts keeps 256 commands, verbs and words to 512 characters, 200 items and 400 links, by UTF-8 bytes, on top of SW20.9's 413. This is the only home of these caps in code; §14 states them.
- Risk comes only from a verified context.
- A golden replay of PlayMap's busy context.

Validate:

```
cd /home/user/Stagewatch && tools/test.sh build unit
```

### SW21.2 — The desk's rules: run-tagged ids, LineFence, self-raise, last collect, §6-notation rows, peers' documented lines

*Stagewatch.* Sources: cross-app-proposals SW-2. Gaps: G02, G03, G05. Depends on: SW21.1.

cross-app SW-2.
- Ids p<n>-<run>.
- LineFence: refuse rather than cut at 2048.
- from.t and desk-id refusal.
- A target's own risk counts when its key is bound to it (G03).
- Last collect; ProposeApproved; JSON collected/form.
- Rows in §6 notation.
- A DeskRules test that peers' documented lines parse (G05).

Validate:

```
cd /home/user/Stagewatch && tools/test.sh build unit
```

### SW21.3 — DeskService, LineRisk/CommandRisk.Of in Core, the shared-approve tick fix, the receipt-notes stack

*Stagewatch.* Sources: cross-app-proposals SW-3. Gaps: G14. Depends on: SW21.2.

cross-app SW-3:
- DeskService;
- LineRisk and CommandRisk.Of in Core;
- the tick fix;
- RefIds;
- the desk as a mirror.

Validate:

```
cd /home/user/Stagewatch && tools/test.sh build unit
```

### SW21.4 — Release A grammar: `peer <app> <line>` and `desk approve|reject <id>` on HTTP and WS, with receipts

*Stagewatch.* Sources: cross-app-proposals SW-4. Gaps: G14, G20. Depends on: SW21.3, SW18.2, SW20.2.

cross-app SW-4.
- Peer and Desk kinds appended, with parser verbs and PersonHelp. They stay out of ShowContext.Commands and the AI's Help.
- The shared gate.
- POST /api/peer.
- Executors report sent → collected → done, or refused.
- `next` never approves a high item.
- The rig hosts the desk; desk.e2e.cjs.

Validate:

```
cd /home/user/Stagewatch && tools/test.sh build unit e2e
```

### SW21.5 — Release A readers: the operator frame's desk block, config peers, context collects, the producer page

*Stagewatch.* Sources: cross-app-proposals SW-5. Gaps: G14, G20. Depends on: SW21.4.

cross-app SW-5:
- the frame's `desk` block;
- config peers;
- context collects;
- the producer page's panel;
- pages e2e.

Validate:

```
cd /home/user/Stagewatch && tools/test.sh build unit e2e
```

### SW21.6 — The desk (PC work): 'Propose to <app>…', the send dialog with live risk, approval greys

*Stagewatch.* Sources: cross-app-proposals SW-6. Gaps: G14, G20. Depends on: SW21.4.

cross-app SW-6:
- Show network row menus;
- a send dialog that shows the risk as it is typed;
- greyed approvals;
- help;
- UiSmoke on the maintainer's PC.

Validate:

```
cd /home/user/Stagewatch && tools/test.sh build unit; on the maintainer's PC: pwsh tools\smoke.ps1
```

### SW21.7 — One report: verified peers' lines and every desk decision, rejections included

*Stagewatch.* Sources: ecosystem-lane SW-G49a. Gaps: G49. Depends on: SW18.2, SW21.3.

ecosystem SW-G49a.
- Peers' lines, each with its node, own time and road.
- Every desk decision, rejections included.
- network.csv.
- Culture-invariant times, tested under th-TH.
- The report says it is a sample, not the record.

Validate:

```
cd /home/user/Stagewatch && tools/test.sh build unit
```

### SW21.8 — One preflight: 'ready for doors' with a reason per node, on every surface; the rig's preflight

*Stagewatch.* Sources: ecosystem-lane SW-G49b; ecosystem-lane SW-G49c. Gaps: G49. Depends on: SW21.5, SW19.3.

ecosystem SW-G49b and SW-G49c.
- NetworkPreflight and /api/preflight. A stuck Stagewatch is never ready.
- The frame's `network`, after `desk`.
- MCP get_preflight, AI facts, Companion network_ready.
- The producer page; the desk header (PC); help.
- rig preflight; preflight.e2e.cjs.

Validate:

```
cd /home/user/Stagewatch && tools/test.sh build unit module e2e
```

### SW21.9 — Interop: the desk rows, Patterns' desk and stuck rows, the doors, nobody locks anybody out

*Stagewatch.* Sources: ecosystem-lane SW-R2; ecosystem-lane SW-R3; ecosystem-lane SW-R5; ecosystem-lane SW-R7. Gaps: G44, G49. Depends on: SW21.4, SW21.7, SW21.8, SW19.13, P91.6, P89.9.

ecosystem SW-R2, R3, R5 and R7. These are the rows whose needs exist by this train:
- S3.3, S3.4, S7.3;
- S6.2b, S6.3;
- S8.1–S8.4;
- S7.4, S1c.3, S1b.1.

Found while checking the slip rule:
- S3.5 and S3.7 need release B's MCP run_command target (SW22.10, Train 5), so they move to SW22.11.
- S3.1, S3.2 and S3.8 need AGMessage, so they move to SW-INT-AG.
- S6.3's PlayMap is PlayMap at its pin, which needs no Mapper unit.

Each row is added only once it passes.

Validate:

```
cd /home/user/Stagewatch && tools/test.sh build interop
```

### SW21.10 — Stagewatch 2.21 papers and release

*Stagewatch.* Sources: cross-app-proposals SW-7; ecosystem-lane SW-P. Gaps: G14, G20, G49. Depends on: SW21.6, SW21.9.

cross-app SW-7 and SW-P:
- REMOTE 2.21: peer, desk, preflight, report, and each route's key scope;
- COMPANION, FEATURES;
- WHATS_NEXT 2.21: why release B waits;
- ECOSYSTEM, README.

The release goes through release.ps1 -Version 2.21.0 -ModuleVersion 2.21.0.

Validate:

```
cd /home/user/Stagewatch && tools/test.sh && tools/test.sh interop; on the maintainer's PC: pwsh tools\smoke.ps1; pwsh tools\release.ps1 -Version 2.21.0 -ModuleVersion 2.21.0
```

### P94.0 — Round 94.0: the round opens

*Patterns.* Sources: Patterns CLAUDE.md 'How a round opens'. Gaps: none. Depends on: P93.4.

PLAN §112's first lines:
- what the files said;
- OPEN rows to touch;
- the requester;
- the evidence class: a handed-in critique (the Eye design and its critique).

The layout's measurement is named here: the fit scale at 48 things.

Validate:

```
cd /home/user/Patterns && grep -n '^## 112\. Round 94' docs/PLAN.md
```

### P94.1 — Round 94.1: the NETWORK band — per-app clusters on a band of their own, bounded by the canvas

*Patterns.* Sources: Eye EYE.1; Eye critique issues 5, 6, 9, 14. Gaps: G24. Depends on: P94.0.

EYE.1, with critique issues 5, 6, 9 and 14.

Today:
- one CONTROL column (EyeLayout.cs:83-89,120-141);
- 48 things make a 3,304-unit column, with no labels at fit under 1,236 px (EyeCanvas.cs:217; EyeCamera.cs:15,50-55).

Now:
- EyeNode.Band is appended beside Plane, which stays for the lens.
- Each app is a cluster on a NETWORK band: its card plus 12 things in a 3-column grid.
- ⌈√n⌉ clusters per row, a collapsed strip, '+N more', sticky slots.
- The expansion set is explicit view state.
- The expanded count is derived from the real canvas, minus the 300-px side card, to keep the fit scale ≥ 0.35.
- The rail, worst and headline count only own nodes, links and needed cards (Eye.cs:290-303,488-494; ShowContext.cs:187). ShowBusTests.cs:264 is rewritten, never skipped.

Validate:

```
cd /home/user/Patterns && dotnet build Patterns.sln -c Debug -v q && for s in Core Rendering App; do dotnet test tests/Patterns.$s.Tests -c Debug --no-build || exit 1; done
```

### P94.2 — Round 94.2: the Eye's cost and marks — revisions not contexts, the walk cached, parallel edges, shapes at every scale

*Patterns.* Sources: Eye EYE.1 (cost); Eye critique issues 7, 10, 11. Gaps: G24. Depends on: P94.1.

Critique issues 7, 10 and 11.
- Today the hash serialises every context each second (EyeService.cs:66-70). It becomes per-source revisions plus a steady record projection; heardMs alone never rebuilds.
- Hops are cached by (Rev, focus) (EyeCanvas.cs:168,306), and edges are culled.
- EdgesBetween, distinct neighbours, and one row and one menu id per edge (Eye.cs:282-288,350; EyeMenus.cs:51-60).
- Marks are drawn as geometry at every scale and derived from Light, so they are replay-safe (EyeReplay.cs:171,295).
- The rebuild's time goes to patterns.metrics.csv.

Validate:

```
cd /home/user/Patterns && dotnet build Patterns.sln -c Debug -v q && for s in Core Rendering App; do dotnet test tests/Patterns.$s.Tests -c Debug --no-build || exit 1; done
```

### P94.3 — Round 94 papers (PLAN §112)

*Patterns.* Sources: Eye EYE.6 (its EYE.1 part); Eye critique issue 20. Gaps: G24. Depends on: P94.2.

- PLAN §112 completed; REVIEW 'Walk: none asked'.
- CHANGELOG, README.
- REMOTE: band and expansion (REMOTE.md:107).
- Lens words in Help.cs:225, HelpBodies.cs:272 and EyeService.cs:119.
- EYE.md.
- Tag row 93.

Closing: no verb an output shows; the metrics row is named.

Validate:

```
cd /home/user/Patterns && dotnet build Patterns.sln -c Debug -v q && for s in Core Rendering Devices Audio Assistant Audience App; do dotnet test tests/Patterns.$s.Tests -c Debug --no-build || exit 1; done && cd integrations/companion-module-patterns && npm ci && npm test && node test/emit-lines.mjs | diff - test/lines.txt && npm run check
```

## Train 5 — The hub proves itself, windows travel to peers, PlayMap known on Patterns (Stagewatch 2.22; Patterns Rounds 95–96)

**Goal.** Stagewatch's /ws speaks LINK (Phase 2):
- mutual proofs bound to the certificate;
- S on side calls;
- its own wrong-proof table;
- keys seen in clear;
- live revocation;
- final, receipts and stuck.

Patterns' Stagewatch link proves itself and records a floor (Phase 3). A Stagewatch window is granted to a named app's verified link and shown on Patterns. Release B reaches Companion, OSC, the copilot and MCP. Patterns knows PlayMap as a node device.

Validated by the acceptor scenarios, grants.e2e.cjs, the Patterns windows rows and the MCP rows.

| Unit | Repo | Title | Depends on |
|---|---|---|---|
| SW22.1a | Stagewatch | /ws speaks LINK (1 of 3): proofs first, the verdict, its own wrong-proof table, Host and Origin | SW19.2a, SW18.3 |
| SW22.1b | Stagewatch | /ws speaks LINK (2 of 3): the session credential on side calls; keys seen in clear; live revocation | SW22.1a |
| SW22.1c | Stagewatch | /ws speaks LINK (3 of 3): final, receipts, stuck, lateMs, 429 words | SW22.1b |
| SW22.2 | Stagewatch | Context: windows feed only while showing a page; window rows; placements; the windows cap | SW21.1, SW18.1 |
| SW22.3 | Stagewatch | Grants in Core: `window grant <window> <app>` | — |
| SW22.4 | Stagewatch | WebServer delivers grants only on the app's verified link; every grant-token miss ends black | SW22.3, SW22.1b, owner:S-D0 |
| SW22.5 | Stagewatch | Window lights and words: viewers and claims, the detail frame's windows block, the desk | SW22.2, SW22.4 |
| SW22.6 | Stagewatch | The rig's grants and grants.e2e.cjs | SW22.5, SW18.5 |
| SW22.7 | Stagewatch | Companion module: window_grant, window_state, window_holding | SW22.5 |
| SW22.8 | Stagewatch | Release B: the Companion and OSC doors through the gate | SW21.4 |
| SW22.9 | Stagewatch | Release B: the copilot plans steps for other apps; a person decides | SW21.4 |
| SW22.10 | Stagewatch | Release B: MCP run_command target, string get_proposal, get_context peer | SW21.4 |
| P95.0 | Patterns | Round 95.0: the round opens | P94.3, SW22.1b, SW22.4 |
| P95.1 | Patterns | Round 95.1: the Stagewatch link proves itself (LINK on ws), pins after the proof, records its floor | SW22.1b, P92.4, P95.0 |
| P95.2 | Patterns | Round 95.2: a Stagewatch window granted to this desk, placed ready-gated, 'shown' only from the air | P89.4, P91.1, SW22.4, P95.1 |
| P95.3 | Patterns | Round 95.3: the twin's WINDOWS word, the standby's Super Check row, DRILL scenario 23 | P95.2 |
| P95.4 | Patterns | Round 95 papers (PLAN §113) | P95.1, P95.3 |
| SW22.11 | Stagewatch | Interop: windows on Patterns (S5.1), the proved ws pairs, the stuck rig, the MCP rows | SW22.6, P95.4, SW22.10 |
| SW22.12 | Stagewatch | Stagewatch 2.22 papers and release | SW22.7, SW22.8, SW22.9, SW22.10, SW22.11, SW22.1c, owner:X-declines |
| P96.0 | Patterns | Round 96.0: the round opens | P95.4 |
| P96.1 | Patterns | Round 96.1: Show Bus node devices — PlayMap known, paired by proof, driven, drawn in the NETWORK band | P92.1, P94.1, P96.0 |
| P96.2 | Patterns | Round 96 papers (PLAN §114) | P96.1 |

### SW22.1a — /ws speaks LINK (1 of 3): proofs first, the verdict, its own wrong-proof table, Host and Origin

*Stagewatch.* Sources: PeerLink profile SW-PL2. Gaps: G43, G15. Depends on: SW19.2a, SW18.3.

SW-PL2 on SW18.3's links, first part.
- A link=1 socket hears the link message first.
- The verdict carries proof, lid, level, can, inst and beatMs.
- A wrong-proof table per (address, kid).
- Host and Origin are checked before loopback is trusted.

Scenarios S1–S3, S8–S10, S15 and S16.

Validate:

```
cd /home/user/Stagewatch && tools/test.sh build unit e2e
```

### SW22.1b — /ws speaks LINK (2 of 3): the session credential on side calls; keys seen in clear; live revocation

*Stagewatch.* Sources: PeerLink profile SW-PL2. Gaps: G43, G15. Depends on: SW22.1a.

SW-PL2, second part.
- ShowBus lid authorizes the side calls: S on the context, peers, proposals, message and result routes.
- A key seen in clear gets no grant.
- Revocation ends S.

Scenarios S12, S14, S17 and S18.

Validate:

```
cd /home/user/Stagewatch && tools/test.sh build unit e2e
```

### SW22.1c — /ws speaks LINK (3 of 3): final, receipts, stuck, lateMs, 429 words

*Stagewatch.* Sources: PeerLink profile SW-PL2. Gaps: G43. Depends on: SW22.1b.

SW-PL2's features.
- final.
- receipts: the store, repeat, {cmd:'receipts'}, and @rq RECEIPTS on Companion TCP.
- stuck: StuckAfter is 3000 (ShowState.cs:217).
- lateMs. Late commands run silently today (MainViewModel.Receipts.cs:21-27).
- 429 words before shedding past 64 (WebServer.cs:276-281).

Scenarios S19 and S20.

Validate:

```
cd /home/user/Stagewatch && tools/test.sh build unit e2e
```

### SW22.2 — Context: windows feed only while showing a page; window rows; placements; the windows cap

*Stagewatch.* Sources: windows-for-peers SW-W1. Gaps: G22, G23, G24. Depends on: SW21.1, SW18.1.

windows-for-peers SW-W1.
- The timer feeds a window only while that window shows a page (G23).
- Health items get no link.
- 13 rows in §6 notation.
- Peers' links are kept within SW21.1's caps.
- WindowPlacements.
- The windows cap.
- AiContext facts and PeerFact.Can.

Validate:

```
cd /home/user/Stagewatch && tools/test.sh build unit
```

### SW22.3 — Grants in Core: `window grant <window> <app>`

*Stagewatch.* Sources: windows-for-peers SW-W2. Gaps: G10. Depends on: nothing.

windows-for-peers SW-W2:
- the grant model and ShowApp;
- the parser, with an explicit `all`;
- Apply, revoke and delete;
- Find and Via;
- kept in settings, never in a show file;
- copilot High and help;
- FindApp and WindowDirector.Run(findApp);
- the proposal fence.

Validate:

```
cd /home/user/Stagewatch && tools/test.sh build unit
```

### SW22.4 — WebServer delivers grants only on the app's verified link; every grant-token miss ends black

*Stagewatch.* Sources: windows-for-peers SW-W3. Gaps: G10, G11. Depends on: SW22.3, SW22.1b, owner:S-D0.

windows-for-peers SW-W3, under D0.
- Grant tokens and Via.
- A black end on every grant-token miss.
- IsLocal.
- clearSeen: a key seen in clear gets no grant.
- can=.
- Grants and window-* messages go only on the app's verified link.
- grants-seen; a bounded ViewerStatus.
- identify and reload rules.
- /api/windows grants.
- window.js ends black.

Validate:

```
cd /home/user/Stagewatch && tools/test.sh build unit e2e
```

### SW22.5 — Window lights and words: viewers and claims, the detail frame's windows block, the desk

*Stagewatch.* Sources: windows-for-peers SW-W4. Gaps: G22, G24. Depends on: SW22.2, SW22.4.

windows-for-peers SW-W4:
- ContextItems viewers and claims;
- the detail frame's windows block, placed last;
- journal rows and the headline;
- the Screens column and menus;
- the Show network menu (PC).

Validate:

```
cd /home/user/Stagewatch && tools/test.sh build unit; on the maintainer's PC: pwsh tools\smoke.ps1
```

### SW22.6 — The rig's grants and grants.e2e.cjs

*Stagewatch.* Sources: windows-for-peers SW-W5. Gaps: G10, G11, G24. Depends on: SW22.5, SW18.5.

windows-for-peers SW-W5:
- the rig's FindApp, Frame().Windows, placements and --state;
- the ws devDependency;
- grants.e2e.cjs.

Validate:

```
cd /home/user/Stagewatch && tools/test.sh build unit e2e
```

### SW22.7 — Companion module: window_grant, window_state, window_holding

*Stagewatch.* Sources: windows-for-peers SW-W6. Gaps: G22. Depends on: SW22.5.

windows-for-peers SW-W6:
- the window_grant action;
- window_state and window_holding feedbacks;
- variables;
- presets.

Validate:

```
cd /home/user/Stagewatch && tools/test.sh module
```

### SW22.8 — Release B: the Companion and OSC doors through the gate

*Stagewatch.* Sources: cross-app-proposals SW-8. Gaps: G14, G20. Depends on: SW21.4.

cross-app SW-8:
- Companion TCP and OSC go through the shared gate;
- the ACCESS line;
- the module's actions, variables and feedback.

Validate:

```
cd /home/user/Stagewatch && tools/test.sh build unit module
```

### SW22.9 — Release B: the copilot plans steps for other apps; a person decides

*Stagewatch.* Sources: cross-app-proposals SW-9. Gaps: G14. Depends on: SW21.4.

cross-app SW-9:
- step targets;
- two-pass verbs;
- ParsePlan with peers' tables;
- PlanRunner;
- the AI Help split.

Every peer step goes to the local desk.

Validate:

```
cd /home/user/Stagewatch && tools/test.sh build unit
```

### SW22.10 — Release B: MCP run_command target, string get_proposal, get_context peer

*Stagewatch.* Sources: cross-app-proposals SW-10. Gaps: G14. Depends on: SW21.4.

cross-app SW-10:
- an MCP run_command target;
- a string get_proposal;
- get_context for a peer.

Validate:

```
cd /home/user/Stagewatch && tools/test.sh build unit
```

### P95.0 — Round 95.0: the round opens

*Patterns.* Sources: Patterns CLAUDE.md 'How a round opens'. Gaps: none. Depends on: P94.3, SW22.1b, SW22.4.

PLAN §113's first lines:
- what the files said;
- OPEN rows to touch;
- the requester;
- the evidence class: a handed-in critique (the PeerLink profile and windows-for-peers).

It names the round's platform path: WebView2 showing a grant.

The round opens only once Stagewatch's LINK and grants have landed (SW22.1b, SW22.4), because every unit in it needs them.

Validate:

```
cd /home/user/Patterns && grep -n '^## 113\. Round 95' docs/PLAN.md
```

### P95.1 — Round 95.1: the Stagewatch link proves itself (LINK on ws), pins after the proof, records its floor

*Patterns.* Sources: PeerLink profile Patterns Round PL (link=1 and S). Gaps: G08, G43. Depends on: SW22.1b, P92.4, P95.0.

Round PL's link=1 and S.
- Dial /ws?…&link=1 with no Authorization header.
- The client proves first, and nothing is taken before the acceptor's proof (13.6.3).
- The certificate is pinned only after that proof (13.8.2).
- Floors {tls, level, proof} are kept per id. A lower offer is refused red, and FLOOR LOWER lowers it.
- Side calls carry S only to the pin or loopback.
- Level 0 is kept for older Stagewatches only as P-legacy answers.

Scenarios S1, S2, S4–S7, S10–S14, S17–S19.

Validate:

```
cd /home/user/Patterns && dotnet build Patterns.sln -c Debug -v q && for s in Core Rendering App; do dotnet test tests/Patterns.$s.Tests -c Debug --no-build || exit 1; done
```

### P95.2 — Round 95.2: a Stagewatch window granted to this desk, placed ready-gated, 'shown' only from the air

*Patterns.* Sources: windows-for-peers PT-W2; windows-for-peers PT-W3 (NODES ASK WINDOW); Eye critique (window chain and the air); plan critique issue 13. Gaps: G10. Depends on: P89.4, P91.1, SW22.4, P95.1.

windows PT-W2, plus PT-W3's NODES ASK WINDOW.
- can=pages and the pages cap.
- A loopback dial. Per-link rev.
- Grants persisted in the Control section, never the show.
- The certificate decision. Library tiles.
- SCREEN n WINDOW only when the page is ready; otherwise PVW WINDOW, then TAKE.
- STATE bus.stagewatch `windows`.
- The Run strip chip, menus, the Nodes page and the module.
- 'Shown' is derived from the air: on air, in the preview, or outputs closed.

It depends on 95.1, so the grant arrives on a proved link whatever D0's wording.

Validate:

```
cd /home/user/Patterns && dotnet build Patterns.sln -c Debug -v q && for s in Core Rendering App; do dotnet test tests/Patterns.$s.Tests -c Debug --no-build || exit 1; done && cd integrations/companion-module-patterns && npm ci && npm test && node test/emit-lines.mjs | diff - test/lines.txt && npm run check
```

### P95.3 — Round 95.3: the twin's WINDOWS word, the standby's Super Check row, DRILL scenario 23

*Patterns.* Sources: windows-for-peers PT-W4. Gaps: G10. Depends on: P95.2.

windows PT-W4:
- the WINDOWS word tells the standby which grants it lacks; grant paths stay off the twin;
- a standby Super Check row;
- DRILL scenario 23.

Platform path: the twin.

Validate:

```
cd /home/user/Patterns && dotnet build Patterns.sln -c Debug -v q && for s in Core Rendering App; do dotnet test tests/Patterns.$s.Tests -c Debug --no-build || exit 1; done
```

### P95.4 — Round 95 papers (PLAN §113)

*Patterns.* Sources: windows-for-peers PT-W5. Gaps: G10. Depends on: P95.1, P95.3.

- PLAN §113 completed.
- REVIEW 'Walk: unwalked': WebView2 showing a grant, and the twin's WINDOWS word (§22/§27).
- CHANGELOG, README.
- REMOTE: SCREEN n WINDOW, NODES ASK WINDOW, windows, floors.
- COMPANION, DRILL, OPEN.
- Tag row 94.

Closing: SCREEN n WINDOW's journal row, beside the tile.

Validate:

```
cd /home/user/Patterns && dotnet build Patterns.sln -c Debug -v q && for s in Core Rendering Devices Audio Assistant Audience App; do dotnet test tests/Patterns.$s.Tests -c Debug --no-build || exit 1; done && cd integrations/companion-module-patterns && npm ci && npm test && node test/emit-lines.mjs | diff - test/lines.txt && npm run check
```

### SW22.11 — Interop: windows on Patterns (S5.1), the proved ws pairs, the stuck rig, the MCP rows

*Stagewatch.* Sources: ecosystem-lane SW-R6; ecosystem-lane SW-R2 (S3.5, S3.7); PeerLink profile conformance (cross-app rows). Gaps: G44, G10. Depends on: SW22.6, P95.4, SW22.10.

ecosystem SW-R6, the Patterns half:
- S5.1, with Xvfb, Mesa and Playwright in the job;
- pair rows for the proved ws links;
- a stuck rig: followers show no time within stuckMs + 4 s, and links stay live and amber;
- S3.5 and S3.7 (MCP → PlayMap at its pin, a rejection never runs), moved here from SW21.9 because they need SW22.10.

Validate:

```
cd /home/user/Stagewatch && tools/test.sh build interop
```

### SW22.12 — Stagewatch 2.22 papers and release

*Stagewatch.* Sources: windows-for-peers SW-W7; cross-app-proposals SW-11; plan critique unmapped G46, G49. Gaps: G22, G14, G46, G49. Depends on: SW22.7, SW22.8, SW22.9, SW22.10, SW22.11, SW22.1c, owner:X-declines.

SW-W7 and cross-app SW-11:
- REMOTE 2.22, with D0's answer, LINK, S and grants;
- COMPANION, FEATURES, NODES, README;
- WHATS_NEXT 2.22, which records the declines X-declines confirms: G46 (a peer's page in a window) and G49's 'Link all'.

The release goes through release.ps1 -Version 2.22.0 -ModuleVersion 2.22.0.

Validate:

```
cd /home/user/Stagewatch && tools/test.sh && tools/test.sh interop; on the maintainer's PC: pwsh tools\smoke.ps1; pwsh tools\release.ps1 -Version 2.22.0 -ModuleVersion 2.22.0
```

### P96.0 — Round 96.0: the round opens

*Patterns.* Sources: Patterns CLAUDE.md 'How a round opens'. Gaps: none. Depends on: P95.4.

PLAN §114's first lines:
- what the files said;
- OPEN rows to touch;
- the requester;
- the evidence class: a handed-in critique (patterns-playmap-side and gap G35).

Validate:

```
cd /home/user/Patterns && grep -n '^## 114\. Round 96' docs/PLAN.md
```

### P96.1 — Round 96.1: Show Bus node devices — PlayMap known, paired by proof, driven, drawn in the NETWORK band

*Patterns.* Sources: patterns-playmap-side 92.1; Eye critique issue 17 (92.1 adopts the band). Gaps: G35. Depends on: P92.1, P94.1, P96.0.

pps 92.1, amended by the Eye critique. Today Lines devices send raw lines with no proof (G35; DeviceProfiles.cs:167-190).

A node device on the core:
- HELLO;
- LINK when PlayMap offers it, else AUTH-PROOF on a 32-hex NONCE;
- SUBSCRIBE OFF and CONTEXT;
- facts only after the proof;
- ids d<n>-<inst>, with outcomes by 13.12.3.

On the Eye:
- PlayMap is a cluster in the NETWORK band, and its things keep their planes.
- It folds only when the address and the context id agree. At level 0 it is marked legacy (per P-legacy).
- KindWords maps playmap to PlayMap (ShowBusNetwork.cs:168-175).

Also the Nodes page, Add as a device, and the brief. Generic Lines devices (third-party gear) keep their own protocols.

Validate:

```
cd /home/user/Patterns && dotnet build Patterns.sln -c Debug -v q && for s in Core Rendering Devices App Assistant; do dotnet test tests/Patterns.$s.Tests -c Debug --no-build || exit 1; done
```

### P96.2 — Round 96 papers (PLAN §114)

*Patterns.* Sources: patterns-playmap-side Round 92 papers. Gaps: G35. Depends on: P96.1.

- PLAN §114 completed; REVIEW 'Walk: none asked'.
- CHANGELOG, README, REMOTE, OPEN.
- Tag row 95.

Closing: the DEVICE lines' receipts, beside the Nodes page.

Validate:

```
cd /home/user/Patterns && dotnet build Patterns.sln -c Debug -v q && for s in Core Rendering Devices Audio Assistant Audience App; do dotnet test tests/Patterns.$s.Tests -c Debug --no-build || exit 1; done && cd integrations/companion-module-patterns && npm ci && npm test && node test/emit-lines.mjs | diff - test/lines.txt && npm run check
```

## Train 6 — Acceptors, and every line connector by proof (Patterns Round 97; Stagewatch 2.23; PlayMap wp4-peerlink)

**Goal.** Patterns' 9697:
- offers TLS and LINK;
- issues scoped app keys, each bound to one connector, and revokes them live;
- keeps receipts and lists what it accepted.

PlayMap's 9941 and playmap.lines.v1 do the same once its owner opens the package. Stagewatch's PlayoutLink then proves an issued key to both over TLS, with floors, and times a GO only on a fit clock (Phase 4).

Validated by the acceptor and connector scenarios. PlayMap's rows wait for its owner (rule R3); peers show it as legacy, amber, meanwhile.

| Unit | Repo | Title | Depends on |
|---|---|---|---|
| P97.0 | Patterns | Round 97.0: the round opens | P96.2, owner:P-scopes |
| P97.1 | Patterns | Round 97.1: TLS on 9697, told from plain by the first byte | P97.0 |
| P97.2 | Patterns | Round 97.2: LINK and LINK PROOF on 9697; constant-shape refusals; its own wrong-proof table; app links within the wire's limits | P97.1 |
| P97.3 | Patterns | Round 97.3: app keys — ptk_ keys bound to one connector, scoped, revocable live, enrolled over TLS | P97.2 |
| P97.4 | Patterns | Round 97.4: the acceptor's list — the App links table, STATE decks rows, decks readers drop proved apps | P97.3 |
| P97.5 | Patterns | Round 97.5: receipts kept, a final answer for CUE GO, lateMs; stuck not offered; node devices speak LINK | P97.4, P96.1 |
| P97.6 | Patterns | Round 97 papers (PLAN §115) | P97.5 |
| SW23.1 | Stagewatch | PlayoutLink speaks LINK to Patterns and PlayMap: an issued key, TLS, floors, AT only on a fit clock | SW20.4b, P97.6 |
| SW23.2 | Stagewatch | Stagewatch 2.23 papers and release | SW23.1 |
| PM-9a | Mapper | wp4-peerlink (1 of 3): the pmk_ table, scopes mapped to ShowPolicy, the catalogue's KEY rows | PM-6, owner:M-wp4-peerlink, owner:M-trusted-addresses |
| PM-9b | Mapper | wp4-peerlink (2 of 3): LINK on 9941, TLS sniffed, final and receipts | PM-9a |
| PM-9c | Mapper | wp4-peerlink (3 of 3): LINK on playmap.lines.v1; STATE clients rows, fixtures and cards | PM-9b |
| PM-10 | Mapper | wp4-peerlink: PlayMap's links speak LINK; their records in STATE, preflight and the desk | PM-9c, SW22.1b, P97.6, owner:M-wp4-peerlink |

### P97.0 — Round 97.0: the round opens

*Patterns.* Sources: Patterns CLAUDE.md 'How a round opens'. Gaps: none. Depends on: P96.2, owner:P-scopes.

PLAN §115's first lines:
- what the files said;
- OPEN: L55's wire half to close;
- the requester;
- the evidence class: a handed-in critique (the PeerLink profile, Round PL).

The round opens once P-scopes is answered, because its keys are the round.

Validate:

```
cd /home/user/Patterns && grep -n '^## 115\. Round 97' docs/PLAN.md
```

### P97.1 — Round 97.1: TLS on 9697, told from plain by the first byte

*Patterns.* Sources: PeerLink profile Patterns Round PL (TLS). Gaps: G08. Depends on: P97.0.

Round PL, 13.8.5.
- 9697 sniffs 0x16 and serves TLS with the desk's certificate.
- Decks stay plain, with the legacy proof.
- OPEN L55's wire half closes.
- The hello's tls names 9697.

Validate:

```
cd /home/user/Patterns && dotnet build Patterns.sln -c Debug -v q && for s in Core Rendering App; do dotnet test tests/Patterns.$s.Tests -c Debug --no-build || exit 1; done
```

### P97.2 — Round 97.2: LINK and LINK PROOF on 9697; constant-shape refusals; its own wrong-proof table; app links within the wire's limits

*Patterns.* Sources: PeerLink profile Patterns Round PL (LINK); Eye critique issue 13 (capacity). Gaps: G08. Depends on: P97.1.

13.6.1–4 on line.
- LINK joins the pre-pairing words; one proof per connection.
- Unknown kids are answered exactly like known ones, in constant time.
- A 10 s handshake; at most 4 unfinished per address.
- Wrong proofs count per (address, kid), apart from the legacy five-and-close (ControlService.cs:541).
- App links get a sub-cap inside WireLimits (64 in all, 16 per address; WireLimits.cs:14-15), so decks keep their slots.
- Host and Origin are checked before loopback is trusted (9696 has had this since 89.7).

Scenarios S1–S3, S8, S9, S15, S16, S19, S20. TrustLoopback=false is restored in finally.

Validate:

```
cd /home/user/Patterns && dotnet build Patterns.sln -c Debug -v q && for s in Core Rendering App; do dotnet test tests/Patterns.$s.Tests -c Debug --no-build || exit 1; done
```

### P97.3 — Round 97.3: app keys — ptk_ keys bound to one connector, scoped, revocable live, enrolled over TLS

*Patterns.* Sources: PeerLink profile Patterns Round PL (keys). Gaps: G08. Depends on: P97.2.

13.5.
- ptk_ keys, each bound to one connector and scoped by P-scopes' map.
- Kept with DPAPI, never in the show or the twin.
- KEY MAKE, KEY BIND and KEY REVOKE: catalogued, journalled, never proposed.
- Enrolment over TLS only. NEW TOKEN revokes enrolled keys.
- A token seen in clear is marked (89.5's mark, now also on keys).

Scenarios S17, S18, S21.

Validate:

```
cd /home/user/Patterns && dotnet build Patterns.sln -c Debug -v q && for s in Core Rendering App; do dotnet test tests/Patterns.$s.Tests -c Debug --no-build || exit 1; done && cd integrations/companion-module-patterns && npm ci && npm test && node test/emit-lines.mjs | diff - test/lines.txt && npm run check
```

### P97.4 — Round 97.4: the acceptor's list — the App links table, STATE decks rows, decks readers drop proved apps

*Patterns.* Sources: PeerLink profile Patterns Round PL (keys); Eye critique issue 8b (decks readers). Gaps: G08. Depends on: P97.3.

13.16.5.
- The App links table on the Nodes page.
- The acceptor's list in STATE decks rows, with level, t, id, verified and scope appended.
- The decks readers drop proved apps: CommandRouter.cs:558, MainViewModel.Poll.cs:388, NodeHost.cs:362 and ShowActions.Nav.cs:190.

Validate:

```
cd /home/user/Patterns && dotnet build Patterns.sln -c Debug -v q && for s in Core Rendering App; do dotnet test tests/Patterns.$s.Tests -c Debug --no-build || exit 1; done && cd integrations/companion-module-patterns && npm ci && npm test && node test/emit-lines.mjs | diff - test/lines.txt && npm run check
```

### P97.5 — Round 97.5: receipts kept, a final answer for CUE GO, lateMs; stuck not offered; node devices speak LINK

*Patterns.* Sources: PeerLink profile Patterns Round PL (features). Gaps: G07, G35. Depends on: P97.4, P96.1.

13.12.4–6.
- final: CUE GO's 'Requested' is followed by an `OK @id final` line once Done.
- receipts: 256 ids for 10 minutes, repeat:true, and @rq RECEIPTS.
- lateMs.
- stuck is not offered: no measurement this round. The Super Check records the desk thread's stall distribution (LP-1).
- Node devices speak LINK once PM-9b lands; otherwise they stay at level 0 and say so.

Validate:

```
cd /home/user/Patterns && dotnet build Patterns.sln -c Debug -v q && for s in Core Rendering App Devices; do dotnet test tests/Patterns.$s.Tests -c Debug --no-build || exit 1; done
```

### P97.6 — Round 97 papers (PLAN §115)

*Patterns.* Sources: PeerLink profile 6.2 process. Gaps: G08. Depends on: P97.5.

- PLAN §115 completed; REVIEW 'Walk: none asked'.
- CHANGELOG, README.
- REMOTE: TLS, LINK, KEY, RECEIPTS, decks fields.
- COMPANION.
- OPEN: L55's wire half closed.
- Tag row 96.

Closing: KEY MAKE's and LINK's journal rows, beside App links.

Validate:

```
cd /home/user/Patterns && dotnet build Patterns.sln -c Debug -v q && for s in Core Rendering Devices Audio Assistant Audience App; do dotnet test tests/Patterns.$s.Tests -c Debug --no-build || exit 1; done && cd integrations/companion-module-patterns && npm ci && npm test && node test/emit-lines.mjs | diff - test/lines.txt && npm run check
```

### SW23.1 — PlayoutLink speaks LINK to Patterns and PlayMap: an issued key, TLS, floors, AT only on a fit clock

*Stagewatch.* Sources: PeerLink profile SW-PL3. Gaps: G08, G28. Depends on: SW20.4b, P97.6.

SW-PL3.
- LINK to Patterns over TLS on 9697.
- LINK to PlayMap once PM-9b lands; legacy amber until then.
- An issued key, or enrolment over TLS. Floors.
- AT only on a fit clock measured over TLS.
- The record goes into the link items, the playout row and the copilot's facts.
- Trust verbs are High and stay outside ShowContext.Commands.

Scenarios S1, S2, S4–S7, S11–S14, S17–S19, S21, S22. Interop: the pair row, and a GO caught by a pulled cable runs once.

Validate:

```
cd /home/user/Stagewatch && tools/test.sh build unit && tools/test.sh interop
```

### SW23.2 — Stagewatch 2.23 papers and release

*Stagewatch.* Sources: PeerLink profile 6.1. Gaps: G08. Depends on: SW23.1.

- REMOTE 2.23; COMPANION, FEATURES; README.
- WHATS_NEXT 2.23: PlayMap is level 0 until its acceptor lands.

The release goes through release.ps1 -Version 2.23.0 -ModuleVersion 2.23.0.

Validate:

```
cd /home/user/Stagewatch && tools/test.sh && tools/test.sh interop; on the maintainer's PC: pwsh tools\smoke.ps1; pwsh tools\release.ps1 -Version 2.23.0 -ModuleVersion 2.23.0
```

### PM-9a — wp4-peerlink (1 of 3): the pmk_ table, scopes mapped to ShowPolicy, the catalogue's KEY rows

*Mapper.* Sources: PeerLink profile PM-PL2. Gaps: G08. Depends on: PM-6, owner:M-wp4-peerlink, owner:M-trusted-addresses.

PM-PL2, first part.
- A pmk_ table in NodeCredentialStore, with scopes mapped to ShowPolicy roles.
- VerbCatalog KEY rows (spine, through the package's hunk).
- TrustedAddresses (Pairing.cs:113-115) only for legacy Lines devices, shown amber: 'trusted by address — not a proof'.

Validate:

```
cd /home/user/Mapper && dotnet build PlayMap.sln -c Debug -v q && dotnet test tests/PlayMap.Engine.Tests -c Debug --no-build --filter "FullyQualifiedName~NodeCredentialStoreTests" -- RunConfiguration.TreatNoTestsAsError=true && dotnet test tests/PlayMap.Core.Tests -c Debug --no-build --filter "FullyQualifiedName~WireAndBusTests" -- RunConfiguration.TreatNoTestsAsError=true
```

### PM-9b — wp4-peerlink (2 of 3): LINK on 9941, TLS sniffed, final and receipts

*Mapper.* Sources: PeerLink profile PM-PL2. Gaps: G08, G35. Depends on: PM-9a.

PM-PL2, second part.
- LINK on 9941 (WireSession.cs:27,229-233), with TLS sniffed on the first byte.
- final and receipts; VerbCatalog LINK and RECEIPTS rows.
- stuck is not offered: its lab line is not run.

Scenarios S1–S3, S8, S9, S14–S16, S19–S21.

Validate:

```
cd /home/user/Mapper && dotnet build PlayMap.sln -c Debug -v q && dotnet test tests/PlayMap.Net.Tests -c Debug --no-build
```

### PM-9c — wp4-peerlink (3 of 3): LINK on playmap.lines.v1; STATE clients rows, fixtures and cards

*Mapper.* Sources: PeerLink profile PM-PL2. Gaps: G08. Depends on: PM-9b.

PM-PL2, third part.
- LINK on playmap.lines.v1 (ws).
- STATE clients rows appended, with their fixtures and cards. The fixtures are regenerated as part of the change; the validate checks them without the write flag.

Scenarios S17, S18.

Validate:

```
cd /home/user/Mapper && (Xvfb :99 &) && export DISPLAY=:99 && dotnet build PlayMap.sln -c Debug -v q && dotnet test tests/PlayMap.Net.Tests -c Debug --no-build && dotnet test tests/PlayMap.Engine.Tests -c Debug --no-build --filter "FullyQualifiedName~FixtureTest" -- RunConfiguration.TreatNoTestsAsError=true && cd integrations/companion-module-playmap && npm ci && npm test && npm run check
```

### PM-10 — wp4-peerlink: PlayMap's links speak LINK; their records in STATE, preflight and the desk

*Mapper.* Sources: PeerLink profile PM-PL3. Gaps: G28, G08. Depends on: PM-9c, SW22.1b, P97.6, owner:M-wp4-peerlink.

PM-PL3.
- StagewatchLink: link=1 and S.
- PatternsLink: LINK with a ptk_ key.
- The fifth PONG word is read; today it is dropped (WireClient.cs:384).
- STATE links.* appends the record, with offsetMs, errMs and ageMs (G28). Fixtures; preflight; StatusMark lights.
- Trust verbs catalogued and on the menus.

Scenarios S1, S2, S4–S7, S10–S14, S17–S19, S22.

Validate:

```
cd /home/user/Mapper && (Xvfb :99 &) && export DISPLAY=:99 && dotnet build PlayMap.sln -c Debug -v q && dotnet test tests/PlayMap.Net.Tests -c Debug --no-build && dotnet test tests/PlayMap.Engine.Tests -c Debug --no-build --filter "FullyQualifiedName~FixtureTest" -- RunConfiguration.TreatNoTestsAsError=true && dotnet test tests/PlayMap.Engine.Tests -c Debug --no-build --filter "FullyQualifiedName~PreflightBreadthTests" -- RunConfiguration.TreatNoTestsAsError=true && dotnet test tests/PlayMap.App.Tests -c Debug --no-build
```

## Train 7 — The Eye sees the room: one id one node, links as edges, contexts over PeerLinks (Patterns Rounds 98–99; Stagewatch 2.24; PlayMap context rows)

**Goal.** The Eye draws every app, node and device once, folded by proof of identity. Each PeerLink is an edge carrying its 13.16.3 record, every node and edge says its source, and claims are drawn as claims. Window chains cross apps. Lights are computed once, for every reader. Other apps post their contexts over verified PeerLinks, and Stagewatch and PlayMap publish by P1–P8.

Validated by EyeNetworkTests, SyntheticNetworkTests, the five-reader projection fence, the CONTEXT POST flood test, and interop rows: one node per app, no duplicate deck, chains with real apps.

| Unit | Repo | Title | Depends on |
|---|---|---|---|
| P98.0 | Patterns | Round 98.0: the round opens | P97.6 |
| P98.1 | Patterns | Round 98.1: EyeNetwork — fold by proof, records verbatim, @ ends, bounds | P98.0, P92.3, P95.2, P96.1, SW19.12 |
| P98.2 | Patterns | Round 98.2: one projection for lights — precedence not 'worst', five readers, one fence | P98.1 |
| P98.3 | Patterns | Round 98.3: the feed — Apps from the hello, the Stagewatch link, node devices, accepted links and the twin | P98.2, P97.4 |
| P98.4 | Patterns | Round 98.4: the desk's readers — canvas, side card, NetworkMenus in step on Eye, Nodes and deck, lens NETWORK | P98.3 |
| P98.5 | Patterns | Round 98.5: the wire's readers — STATE, EYE JSON, the module | P98.4 |
| P98.6 | Patterns | Round 98 papers (PLAN §116) | P98.5 |
| P99.0 | Patterns | Round 99.0: the round opens | P98.6 |
| P99.1 | Patterns | Round 99.1: CONTEXT POST — another app gives its context over a verified PeerLink | P99.0, P97.3 |
| P99.2 | Patterns | Round 99 papers (PLAN §117) | P99.1 |
| SW24.1 | Stagewatch | Stagewatch publishes by P1–P8 | SW23.1, SW22.2, SW18.4 |
| SW24.2 | Stagewatch | Stagewatch 2.24 papers and release | SW24.1 |
| PM-11 | Mapper | Spine: the context names its peers and the timer it draws (P2/P3 rows) | SW19.12, owner:M-eye-spine |
| PM-12 | Mapper | wp4-peerlink: CONTEXT POST to Patterns, and its outcome in PlayMap's own STATE and context | PM-10, P99.2, owner:M-wp4-peerlink |

### P98.0 — Round 98.0: the round opens

*Patterns.* Sources: Patterns CLAUDE.md 'How a round opens'. Gaps: none. Depends on: P97.6.

PLAN §116's first lines:
- what the files said;
- OPEN: the takeover row 89.6 added, and the rows the round adds;
- the requester;
- the evidence class: a handed-in critique (the Eye design and its critique).

It names the reversals the round makes: a gone heard-only app is grey; a stuck card is amber; PlayMap sits in the NETWORK band.

Validate:

```
cd /home/user/Patterns && grep -n '^## 116\. Round 98' docs/PLAN.md
```

### P98.1 — Round 98.1: EyeNetwork — fold by proof, records verbatim, @ ends, bounds

*Patterns.* Sources: Eye EYE.2; windows-for-peers PT-W3 (Eye chain, FromEye @ links); Eye critique issues 1, 2, 3, 18, 19. Gaps: G24, G23, G35, G37. Depends on: P98.0, P92.3, P95.2, P96.1, SW19.12.

EYE.2, PT-W3's chain, and critique issues 1–3, 18 and 19.

Fold:
- by LINK id;
- a level-0 device folds only when its address and context id agree;
- a level-0 deck never folds;
- a Patterns hello and its twin context fold into node:<inst> (TwinService.Followers.cs:213-217).

Also:
- records verbatim;
- re-anchored rows; @ ends; counting shows '?'; bounds;
- NodeContextBuilder's relay becomes a link item (ShowBusNodes.cs:114-134);
- FromEye with short ids, plus a canary.

Validate:

```
cd /home/user/Patterns && dotnet build Patterns.sln -c Debug -v q && for s in Core Rendering App; do dotnet test tests/Patterns.$s.Tests -c Debug --no-build || exit 1; done
```

### P98.2 — Round 98.2: one projection for lights — precedence not 'worst', five readers, one fence

*Patterns.* Sources: Eye EYE.2; Eye critique issues 8a, 9, 20. Gaps: G24. Depends on: P98.1.

EYE.2's lights, with critique issues 8a, 9 and 20.
- ShowBusView.Network computes the lights once, for Eye.Build, Peers (StagewatchFollow.cs:247-253), ShowAppCard.Of (:303-308), BriefLines and ShowBusRow.
- The fence at ShowBusTests.cs:329 covers all five.
- The highest source sets a light; lower sources add i notes.
- A bounded brief.
- Tests are rewritten, never skipped (ShowBusTests.cs:264,306-325,329-363).

Validate:

```
cd /home/user/Patterns && dotnet build Patterns.sln -c Debug -v q && for s in Core Rendering App Assistant; do dotnet test tests/Patterns.$s.Tests -c Debug --no-build || exit 1; done
```

### P98.3 — Round 98.3: the feed — Apps from the hello, the Stagewatch link, node devices, accepted links and the twin

*Patterns.* Sources: Eye EYE.3. Gaps: G35, G37, G24. Depends on: P98.2, P97.4.

EYE.3.
- AppServices.Apps composes:
  - the bounded hello table;
  - the Stagewatch link's record, context and revision;
  - node devices;
  - accepted links (P97.4);
  - the twin.
- Any kind can be linked; today only `main` can (StagewatchFollow.cs:212,256-262).
- Gather works by revisions.

Validate:

```
cd /home/user/Patterns && dotnet build Patterns.sln -c Debug -v q && for s in Core Rendering App; do dotnet test tests/Patterns.$s.Tests -c Debug --no-build || exit 1; done
```

### P98.4 — Round 98.4: the desk's readers — canvas, side card, NetworkMenus in step on Eye, Nodes and deck, lens NETWORK

*Patterns.* Sources: Eye EYE.5; Eye critique issues 4, 10, 12. Gaps: G24, G35. Depends on: P98.3.

EYE.5, first part, with critique issues 4, 10 and 12.
- Canvas: marks; claims in their own dash with a 'says' glyph; edge hit-test.
- The side card's LINKS.
- NetworkMenus, shared by the Eye's cards and edges, the Nodes card and the deck:
  - Link again; Trust and Accept (typed hex) on the desk only;
  - Lower floor; Accept new id;
  - Release held fact;
  - Make key; Let speak for; Revoke.
  - They use 92.4's and 97.3's lines and are never proposed.
- EYE LENS NETWORK.
- The beacon node follows the needed rule (Eye.cs:763).

Validate:

```
cd /home/user/Patterns && dotnet build Patterns.sln -c Debug -v q && for s in Core Rendering App; do dotnet test tests/Patterns.$s.Tests -c Debug --no-build || exit 1; done && cd integrations/companion-module-patterns && npm ci && npm test && node test/emit-lines.mjs | diff - test/lines.txt && npm run check
```

### P98.5 — Round 98.5: the wire's readers — STATE, EYE JSON, the module

*Patterns.* Sources: Eye EYE.5; Eye critique issue 20. Gaps: G24, G35. Depends on: P98.4.

EYE.5, second part, with critique issue 20.
- STATE: eye and bus.apps[] appended.
- EYE JSON appends band, app, source, mark, collapsed and the link record (null in replay), plus apps, linkLights, left and unplaced.
- Link fields go only to paired connections (ADR-016).
- Companion: eye_network, eye_links_worst, eye_links; test/lines.txt regenerated; the module's minor version.

Validate:

```
cd /home/user/Patterns && dotnet build Patterns.sln -c Debug -v q && for s in Core Rendering App; do dotnet test tests/Patterns.$s.Tests -c Debug --no-build || exit 1; done && cd integrations/companion-module-patterns && npm ci && npm test && node test/emit-lines.mjs | diff - test/lines.txt && npm run check
```

### P98.6 — Round 98 papers (PLAN §116)

*Patterns.* Sources: Eye EYE.6 (its EYE.2–5 part). Gaps: G24. Depends on: P98.5.

- PLAN §116 completed, naming the reversals.
- REVIEW 'Walk: none asked'.
- CHANGELOG, README, REMOTE, COMPANION.
- EYE.md §7.
- OPEN rows: replay of link edges, L44, NDI across apps, AGMessage until it posts, twin takeover.
- Tag row 97.

Validate:

```
cd /home/user/Patterns && dotnet build Patterns.sln -c Debug -v q && for s in Core Rendering Devices Audio Assistant Audience App; do dotnet test tests/Patterns.$s.Tests -c Debug --no-build || exit 1; done && cd integrations/companion-module-patterns && npm ci && npm test && node test/emit-lines.mjs | diff - test/lines.txt && npm run check
```

### P99.0 — Round 99.0: the round opens

*Patterns.* Sources: Patterns CLAUDE.md 'How a round opens'. Gaps: none. Depends on: P98.6.

PLAN §117's first lines:
- what the files said;
- OPEN rows to touch;
- the requester;
- the evidence class: a handed-in critique (the Eye design, EYE.4).

The flood test's bound is named here.

Validate:

```
cd /home/user/Patterns && grep -n '^## 117\. Round 99' docs/PLAN.md
```

### P99.1 — Round 99.1: CONTEXT POST — another app gives its context over a verified PeerLink

*Patterns.* Sources: Eye EYE.4; Eye critique issue 13. Gaps: G37. Depends on: P99.0, P97.3.

EYE.4, with critique issue 13.
- CONTEXT POST is gated before the router. Today IsQuery allows it by default (ControlProtocol.cs:1454).
- It needs:
  - a LINK-verified connection with scope at least post;
  - an id and kind equal to the verified ones; otherwise an uncounted 'ERR this key speaks for…'.
- Level 0 is refused.
- Parsing happens on the reader task.
- Latest wins; at most 128 contexts, kept 30 s, each at most 64 KiB.
- A journal row on the first keep, one refusal summary a minute, and Super Check words.
- A flood test at the WireLimits cap.

Validate:

```
cd /home/user/Patterns && dotnet build Patterns.sln -c Debug -v q && for s in Core Rendering App; do dotnet test tests/Patterns.$s.Tests -c Debug --no-build || exit 1; done
```

### P99.2 — Round 99 papers (PLAN §117)

*Patterns.* Sources: Eye EYE.6 (its EYE.4 part). Gaps: G37. Depends on: P99.1.

- PLAN §117 completed; REVIEW 'Walk: none asked'.
- CHANGELOG, README.
- REMOTE: CONTEXT POST.
- COMPANION, OPEN.
- Tag row 98.

Closing: CONTEXT POST's journal row, beside the NETWORK lens.

Validate:

```
cd /home/user/Patterns && dotnet build Patterns.sln -c Debug -v q && for s in Core Rendering Devices Audio Assistant Audience App; do dotnet test tests/Patterns.$s.Tests -c Debug --no-build || exit 1; done && cd integrations/companion-module-patterns && npm ci && npm test && node test/emit-lines.mjs | diff - test/lines.txt && npm run check
```

### SW24.1 — Stagewatch publishes by P1–P8

*Stagewatch.* Sources: Eye SW-EYE. Gaps: G23, G24. Depends on: SW23.1, SW22.2, SW18.4.

SW-EYE.
- ShowContext.cs drops the timer → every link/window rows (:126; G23).
- The timer feeds a window only while it shows a page.
- A window serves @<holder>.
- One row per link to @<peer>, with the scope in words.
- Accepted links as P2 items.
- clock:<peer> with errMs.
- Window items say their holder.
- Tests.

Validate:

```
cd /home/user/Stagewatch && tools/test.sh build unit && tools/test.sh interop
```

### SW24.2 — Stagewatch 2.24 papers and release

*Stagewatch.* Sources: Eye SW-EYE (papers). Gaps: G24. Depends on: SW24.1.

- REMOTE 2.24 and WHATS_NEXT 2.24.
- §14's `landed` list as it stands.

The release goes through release.ps1 -Version 2.24.0 -ModuleVersion 2.24.0.

Validate:

```
cd /home/user/Stagewatch && tools/test.sh && tools/test.sh interop; on the maintainer's PC: pwsh tools\smoke.ps1; pwsh tools\release.ps1 -Version 2.24.0 -ModuleVersion 2.24.0
```

### PM-11 — Spine: the context names its peers and the timer it draws (P2/P3 rows)

*Mapper.* Sources: Eye PM-EYE.1; Eye critique issue 18; plan critique issue 13. Gaps: G24. Depends on: SW19.12, owner:M-eye-spine.

PM-EYE.1, through the §5.7 lane in ContextBuilder.cs.
- Link rows to @<id>.
- @<sw>/timer feeds the outputs that draw the timer.
- Playlists feed outputs.

Taken out:
- drawn windows' rows: PlayMap draws windows only from PM-15, so they go to PM-15c;
- the held blackout's age: its one home is PM-6.

ContextShape.cs and EyeContextTests are untouched. They are not spine files (round3/PLAN.md:307-311), and Patterns forces the chain ends.

Validate:

```
cd /home/user/Mapper && dotnet build PlayMap.sln -c Debug -v q && dotnet test tests/PlayMap.Engine.Tests -c Debug --no-build --filter "FullyQualifiedName~ContextLinkTests" -- RunConfiguration.TreatNoTestsAsError=true
```

### PM-12 — wp4-peerlink: CONTEXT POST to Patterns, and its outcome in PlayMap's own STATE and context

*Mapper.* Sources: Eye PM-EYE.2; Eye critique (poster's outcome). Gaps: G37. Depends on: PM-10, P99.2, owner:M-wp4-peerlink.

PM-EYE.2.
- Post every 5 s over the verified scope-post link.
- STATE links.patterns and link:patterns say 'Patterns keeps this context (N s ago)', or give the refusal.
- SHOWBUS-NODE says what PlayMap publishes.

Validate:

```
cd /home/user/Mapper && (Xvfb :99 &) && export DISPLAY=:99 && dotnet build PlayMap.sln -c Debug -v q && dotnet test tests/PlayMap.Net.Tests -c Debug --no-build --filter "FullyQualifiedName~LinkTests" -- RunConfiguration.TreatNoTestsAsError=true && dotnet test tests/PlayMap.Engine.Tests -c Debug --no-build --filter "FullyQualifiedName~FixtureTest" -- RunConfiguration.TreatNoTestsAsError=true
```

## Train 8 — PlayMap after round 3's integration pass (the owner-gated remainder, and any earlier Mapper unit not yet let in)

**Goal.** What round 3's ownership keeps out of the waves lands in PlayMap's own process once the files open:
- native windows and the timer's remaining fields as a package;
- the typed address's probe;
- the assistant on Stagewatch's desk, the Patterns link's drive, and the wire's reads after pairing, each on the owner's answer.

PM-13 is listed here but is event-triggered (rule R2): it lands inside the integration pass, whatever train is running then. Any Mapper unit from Trains 1–7 still waiting, and every unit that waits only on it, joins here; its train is validated without it (rule R3).

Validated by both Mapper CI jobs and the interop rows.

| Unit | Repo | Title | Depends on |
|---|---|---|---|
| PM-13 | Mapper | At the integration pass (event-triggered): derived risk rows, the secret field, risk-examples.json | SW19.1b |
| PM-14 | Mapper | wp4-showbus-windows: the timer source draws everything Stagewatch sends; the desk and assistant read it | PM-7, PM-5, owner:M-windows-package |
| PM-15a | Mapper | wp4-showbus-windows (1 of 3): the spine commits through §5.7 | PM-14, owner:M-windows-package |
| PM-15b | Mapper | wp4-showbus-windows (2 of 3): NativeWindows — PlayMap draws a granted Stagewatch window, black when unknown | PM-15a, SW22.4, owner:S-D0 |
| PM-15c | Mapper | wp4-showbus-windows (3 of 3): the STATE block and cards, the @ link, menus, and the context's drawn-window rows | PM-15b |
| PM-16 | Mapper | The typed Stagewatch address: re-resolved, the desk probed once a minute, a 403 named | PM-5 |
| PM-17 | Mapper | PlayMap's assistant on Stagewatch's approval desk | SW21.2, PM-13, owner:M-decision7 |
| PM-18 | Mapper | The Patterns link drives: a Patterns chase source and LINK PATTERNS SEND, with receipts | PM-10, owner:M-G36 |
| PM-19 | Mapper | The wire's reads after pairing on a token node (9941) | PM-4, P96.2, owner:M-decision17 |
| PM-20 | Mapper | PlayMap's papers for the post-round-3 work | PM-14, PM-15c |
| SW-INT-PM | Stagewatch | Interop: PlayMap's windows rows and PeerLink pairs | PM-15c, PM-10, SW22.11 |

### PM-13 — At the integration pass (event-triggered): derived risk rows, the secret field, risk-examples.json

*Mapper.* Sources: cross-app-proposals PM-0; plan critique issue 12. Gaps: G02. Depends on: SW19.1b.

cross-app PM-0, inside §5.6 item 7. Core/Bus/ShowContext.cs is the pass's file (round3/PLAN.md:2135).
- RiskWhen/HighFor rows; the secret field; risk-examples.json.
- ContextRiskRowsTests, and ContextOrderTests keeping the air first within 256. Both are new.

It needs no Stagewatch code: the numbers are §14's (SW19.1b) and the golden is PlayMap's own. It lands when the pass runs (rule R2), even if that is during Train 3 or 4. If the pass has already gone by, it goes to the owner as the next fix batch.

Validate:

```
cd /home/user/Mapper && dotnet build PlayMap.sln -c Debug -v q && dotnet test tests/PlayMap.Core.Tests -c Debug --no-build --filter "FullyQualifiedName~ContextRiskRowsTests|FullyQualifiedName~ContextOrderTests" -- RunConfiguration.TreatNoTestsAsError=true
```

### PM-14 — wp4-showbus-windows: the timer source draws everything Stagewatch sends; the desk and assistant read it

*Mapper.* Sources: windows-for-peers PM-W2; patterns-playmap-side PM-P2. Gaps: G12. Depends on: PM-7, PM-5, owner:M-windows-package.

windows PM-W2, which is pps PM-P2.
- The watermark, always.
- Holding, fetched.
- Flash from leader time.
- Urgency, next and show{}.
- The desk's StateSnapshot and the assistant's StateExplainer.
- Fixtures, regenerated with the change and checked without the flag.
- Redirect words retired.

Validate:

```
cd /home/user/Mapper && (Xvfb :99 &) && export DISPLAY=:99 && dotnet build PlayMap.sln -c Debug -v q && dotnet test tests/PlayMap.Net.Tests -c Debug --no-build --filter "FullyQualifiedName~StagewatchTimerTests" -- RunConfiguration.TreatNoTestsAsError=true && dotnet test tests/PlayMap.Render.Tests -c Debug --no-build --filter "FullyQualifiedName~TimerViewRenderTests" -- RunConfiguration.TreatNoTestsAsError=true && dotnet test tests/PlayMap.Engine.Tests -c Debug --no-build --filter "FullyQualifiedName~FixtureTest" -- RunConfiguration.TreatNoTestsAsError=true && dotnet test tests/PlayMap.App.Tests -c Debug --no-build
```

### PM-15a — wp4-showbus-windows (1 of 3): the spine commits through §5.7

*Mapper.* Sources: windows-for-peers PM-W3. Gaps: G11. Depends on: PM-14, owner:M-windows-package.

windows PM-W3, first part. Spine commits:
- SourceKind.Window;
- catalogue words;
- StateWriter's windows block (empty until 15b);
- ContextBuilder's window rows.

Validate:

```
cd /home/user/Mapper && dotnet build PlayMap.sln -c Debug -v q && dotnet test tests/PlayMap.Core.Tests -c Debug --no-build --filter "FullyQualifiedName~WireAndBusTests" -- RunConfiguration.TreatNoTestsAsError=true && dotnet test tests/PlayMap.Engine.Tests -c Debug --no-build --filter "FullyQualifiedName~StateOrderTests" -- RunConfiguration.TreatNoTestsAsError=true
```

### PM-15b — wp4-showbus-windows (2 of 3): NativeWindows — PlayMap draws a granted Stagewatch window, black when unknown

*Mapper.* Sources: windows-for-peers PM-W3. Gaps: G11. Depends on: PM-15a, SW22.4, owner:S-D0.

windows PM-W3, second part.
- window-draw and can=; link messages; a loopback dial.
- NativeWindows fails closed, with the loss state and persisted HeldWindows.
- The identify overlay.

Validate:

```
cd /home/user/Mapper && (Xvfb :99 &) && export DISPLAY=:99 && dotnet build PlayMap.sln -c Debug -v q && dotnet test tests/PlayMap.Net.Tests -c Debug --no-build && dotnet test tests/PlayMap.Render.Tests -c Debug --no-build --filter "FullyQualifiedName~NativeWindowRenderTests" -- RunConfiguration.TreatNoTestsAsError=true
```

### PM-15c — wp4-showbus-windows (3 of 3): the STATE block and cards, the @ link, menus, and the context's drawn-window rows

*Mapper.* Sources: windows-for-peers PM-W3; plan critique issue 13 (PM-11's drawn windows). Gaps: G11, G24. Depends on: PM-15b.

windows PM-W3, third part.
- The STATE windows block and its cards, with fixtures regenerated with the change.
- The @ link.
- Menus, in step with the desk's buttons.
- The context's drawn-window rows (@<sw>/<window> feeds outputs), moved here from PM-11.

Validate:

```
cd /home/user/Mapper && (Xvfb :99 &) && export DISPLAY=:99 && dotnet build PlayMap.sln -c Debug -v q && dotnet test tests/PlayMap.Engine.Tests -c Debug --no-build --filter "FullyQualifiedName~FixtureTest" -- RunConfiguration.TreatNoTestsAsError=true && dotnet test tests/PlayMap.Engine.Tests -c Debug --no-build --filter "FullyQualifiedName~ContextLinkTests" -- RunConfiguration.TreatNoTestsAsError=true && dotnet test tests/PlayMap.App.Tests -c Debug --no-build
```

### PM-16 — The typed Stagewatch address: re-resolved, the desk probed once a minute, a 403 named

*Mapper.* Sources: patterns-playmap-side PM-P1. Gaps: G31. Depends on: PM-5.

The rest of pps PM-P1.
- A typed address becomes node:<id> after its first verified link (13.4.3).
- The desk is probed once a minute.
- A 403 is named 'requires wss'.

Validate:

```
cd /home/user/Mapper && dotnet build PlayMap.sln -c Debug -v q && dotnet test tests/PlayMap.Net.Tests -c Debug --no-build --filter "FullyQualifiedName~StagewatchLinkTests" -- RunConfiguration.TreatNoTestsAsError=true && dotnet test tests/PlayMap.Engine.Tests -c Debug --no-build --filter "FullyQualifiedName~PreflightBreadthTests" -- RunConfiguration.TreatNoTestsAsError=true
```

### PM-17 — PlayMap's assistant on Stagewatch's approval desk

*Mapper.* Sources: cross-app-proposals PM-1; patterns-playmap-side PM-P3. Gaps: G13. Depends on: SW21.2, PM-13, owner:M-decision7.

cross-app PM-1, which is pps PM-P3. It comes after W4 and decision 7.
- NetworkHost publishes the desk.
- The sink collects with CollectApproved=false; the pull pauses.
- Orphan words.
- ASSISTANT SET desk.
- STATE assistant.desk, with its fixture and card.
- The NetworkDrawer door.
- verbs.json.

Validate:

```
cd /home/user/Mapper && (Xvfb :99 &) && export DISPLAY=:99 && dotnet build PlayMap.sln -c Debug -v q && dotnet test tests/PlayMap.Assistant.Tests -c Debug --no-build && dotnet test tests/PlayMap.Engine.Tests -c Debug --no-build --filter "FullyQualifiedName~FixtureTest" -- RunConfiguration.TreatNoTestsAsError=true && dotnet test tests/PlayMap.Engine.Tests -c Debug --no-build --filter "FullyQualifiedName~AssistantShowTests" -- RunConfiguration.TreatNoTestsAsError=true && cd integrations/companion-module-playmap && npm ci && npm test && npm run check
```

### PM-18 — The Patterns link drives: a Patterns chase source and LINK PATTERNS SEND, with receipts

*Mapper.* Sources: patterns-playmap-side PM-P4. Gaps: G36. Depends on: PM-10, owner:M-G36.

pps PM-P4.
- A chase source following seq and positionMs.
- LINK PATTERNS SEND, with 13.12.3 receipts.
- The contract test.
- No ANCHOR line.

Validate:

```
cd /home/user/Mapper && dotnet build PlayMap.sln -c Debug -v q && dotnet test tests/PlayMap.Net.Tests -c Debug --no-build --filter "FullyQualifiedName~LinkTests" -- RunConfiguration.TreatNoTestsAsError=true && dotnet test tests/PlayMap.Engine.Tests -c Debug --no-build --filter "FullyQualifiedName~PatternsChaseTests" -- RunConfiguration.TreatNoTestsAsError=true
```

### PM-19 — The wire's reads after pairing on a token node (9941)

*Mapper.* Sources: spec-1-3 M2. Gaps: G45. Depends on: PM-4, P96.2, owner:M-decision17.

spec-1-3 M2, only on the owner's yes.
- Today 9941 sends STATE on HELLO before pairing (WireSession.cs:263-276).
- STATE and CONTEXT will wait for pairing on a token node.
- This lands after Patterns' devices pair by proof (Round 96).

Validate:

```
cd /home/user/Mapper && dotnet build PlayMap.sln -c Debug -v q && dotnet test tests/PlayMap.Net.Tests -c Debug --no-build --filter "FullyQualifiedName~WireServerTests|FullyQualifiedName~LineWebSocketTests" -- RunConfiguration.TreatNoTestsAsError=true
```

### PM-20 — PlayMap's papers for the post-round-3 work

*Mapper.* Sources: windows-for-peers PM-W4; cross-app-proposals PM-2; plan critique issue 11. Gaps: G11, G12, G13. Depends on: PM-14, PM-15c.

windows PM-W4 and cross-app PM-2:
- SHOWBUS-NODE §2, §3 and §5;
- CARRYOVER, with G13's reason;
- module fixtures;
- UX.md, under M-windows-package's leave.

Papers notes go to the integrator for WIRE, README and ROADMAP (L10 and the FFmpeg TLS probe). Those files are the integrator's (round3/PLAN.md:2198), so no package edits them.

Validate:

```
cd /home/user/Mapper && dotnet build PlayMap.sln -c Debug -v q && cd integrations/companion-module-playmap && npm ci && npm test && npm run check
```

### SW-INT-PM — Interop: PlayMap's windows rows and PeerLink pairs

*Stagewatch.* Sources: ecosystem-lane SW-R6; PeerLink profile conformance (cross-app rows). Gaps: G44, G11. Depends on: PM-15c, PM-10, SW22.11.

ecosystem SW-R6, PlayMap's half, at bumped pins:
- S5.2 and S5.3;
- PlayMap's line pair rows: verified, pinned, floor set.

No Stagewatch release depends on it.

Validate:

```
cd /home/user/Stagewatch && tools/test.sh build interop
```

## Train 9 — AGMessage after 0.5.0 (0.5.1 and 0.6.0, on the branch A-branch names)

**Goal.** AGMessage's links keep the same profile:
- results that run once and arrive;
- proposals read in their real shape;
- an honest Stagewatch link;
- Show Bus 1.3;
- then LINK, a scoped line connector, and CONTEXT POST to Patterns.

AG-1, the token fix, is in Train 1. These units are last for one reason only: the branch is another session's and is still moving (6146ee9, 8 commits in a day). Under rule R1, AG-2 and AG-3 may land as soon as that branch's owner takes them.

Re-baselined at 6146ee9:
- app/lib/peerlinks.js and nodes.js are unchanged since 9d7f5c3;
- server.js's run loop is now :2129-2143;
- send() is called at :1260;
- the hello reader's caps are at nodes.js:57;
- Displays.status is at displays.js:188.

Each unit re-checks its lines at its start (rule R6). Validated by npm ci, npm run check and the browser smoke in app/, with subjects 'AGMessage X.Y.Z, unit N: …'.

| Unit | Repo | Title | Depends on |
|---|---|---|---|
| AG-2 | AGMessage | AGMessage 0.5.1, unit 1: the desk's proposals read as Stagewatch sends them; the shared fake in ToJson's shape | AG-1 |
| AG-3 | AGMessage | AGMessage 0.5.1, unit 2: each approved line runs at most once across restarts, and its result arrives | AG-2 |
| AG-4 | AGMessage | AGMessage 0.5.1, unit 3: Show Bus 1.3 — the copy and the kit, certificates 825 days, the hello and its reader | AG-1, SW19.12 |
| AG-5a | AGMessage | AGMessage 0.5.1, unit 4: a pure PeerLink core over the vectors | AG-4 |
| AG-5b | AGMessage | AGMessage 0.5.1, unit 5: an honest Stagewatch link — node id, verdict, holds, stuck, pins only on an authenticated answer | AG-5a, AG-2 |
| AG-6 | AGMessage | AGMessage 0.5.1, unit 6: Stagewatch's attention items, windows and commands in the brief and the admin page | AG-3, AG-5b |
| AG-11a | AGMessage | AGMessage 0.5.1: papers and the version | AG-6, AG-4, owner:A-versions, owner:X-declines |
| AG-7 | AGMessage | AGMessage 0.6.0, unit 1: LINK to Stagewatch — proofs, the session credential on side calls, floors | AG-11a, SW22.1b |
| AG-8 | AGMessage | AGMessage 0.6.0, unit 2: a line connector to Patterns with a scoped key; no token anywhere | AG-7, P97.6 |
| AG-8m | AGMessage | AGMessage 0.6.0, unit 3: the line connector's PlayMap leg | AG-8, PM-9c |
| AG-9 | AGMessage | AGMessage 0.6.0, unit 4: AGMessage publishes for the Eye — displays, peer links, one row per link, chain ends | AG-4, owner:A-display-names |
| AG-10 | AGMessage | AGMessage 0.6.0, unit 5: CONTEXT POST to Patterns, and the outcome shown on AGMessage's side | AG-8, AG-9, P99.2 |
| AG-11b | AGMessage | AGMessage 0.6.0: papers and the version | AG-7, AG-8, AG-9, AG-10, owner:A-versions |
| SW-INT-AG | Stagewatch | Interop: AGMessage's rows, and the one-contract row | AG-5b, AG-3, AG-8, SW21.9, SW19.10 |

### AG-2 — AGMessage 0.5.1, unit 1: the desk's proposals read as Stagewatch sends them; the shared fake in ToJson's shape

*AGMessage.* Sources: agmessage AG-2; agmessage AG-1 (remainder). Gaps: G17, G05. Depends on: AG-1.

The rest of agmessage AG-2.
- Today readProposal reads ok only from a result object (nodes.js:206). Now it reads the top-level ok and words, plus created and collected.
- The fake Stagewatch answers in that shape.
- The stage proposal's stored risk is shown, if it is still missing (the rest of G05).

Validate:

```
cd /home/user/AGMessage/app && npm ci --no-audit --no-fund && npm run check && AGM_BROWSER="$(command -v google-chrome || command -v chromium)" npm run smoke
```

### AG-3 — AGMessage 0.5.1, unit 2: each approved line runs at most once across restarts, and its result arrives

*AGMessage.* Sources: agmessage AG-3. Gaps: G16. Depends on: AG-2.

The rest of agmessage AG-3.

Today runs are kept in memory, cut at 200, and retried only while the link lives (server.js:2129-2143 at 6146ee9).

Now:
- each run is recorded on disk before it runs (desk-runs.json via WriteLane and readJsonWithBak), and refused if the record fails;
- tombstones;
- reports retried 3→30 s for 30 minutes after a read-back;
- 409 counts as delivered;
- intake stops at 50;
- a lost collect is recovered from GET /api/proposals.

Validate:

```
cd /home/user/AGMessage/app && npm ci --no-audit --no-fund && npm run check && AGM_BROWSER="$(command -v google-chrome || command -v chromium)" npm run smoke
```

### AG-4 — AGMessage 0.5.1, unit 3: Show Bus 1.3 — the copy and the kit, certificates 825 days, the hello and its reader

*AGMessage.* Sources: spec-1-3 A1; agmessage AG-4; windows-for-peers AG-W1. Gaps: G18, G38, G39, G40, G42, G22. Depends on: AG-1, SW19.12.

spec-1-3 A1, the rest of agmessage AG-4, and AG-W1.
- docs/SHOWBUS.md becomes 1.3, and peerlink-peer.mjs is copied in. showbus.test.js holds both to the hashes pinned in the master's §14.
- The certificate lasts 825 days, with one renewal.
- proto 3 and inst.
- Caps to 32 (20 today, nodes.js:57) and names to 64.
- Any kind matching [A-Za-z0-9_-]{1,16} (nodes.js:40; G18).
- The §14 peer bound, keeping 8 per address.
- The hello is sent from HOST.
- Per-id addresses, sticky dial, conflicts.
- NODES ping wording (G42).

Validate:

```
cd /home/user/AGMessage/app && npm ci --no-audit --no-fund && npm run check && AGM_BROWSER="$(command -v google-chrome || command -v chromium)" npm run smoke
```

### AG-5a — AGMessage 0.5.1, unit 4: a pure PeerLink core over the vectors

*AGMessage.* Sources: agmessage AG-5; PeerLink profile AG-PL1. Gaps: G43. Depends on: AG-4.

agmessage AG-5 and AG-PL1, the core.
- app/lib/peerlink.js, pure, on injected time.
- ids ag<n>-<inst>; unconfirmed after a write; matched pongs; the redial rule.
- peerlink.test.js reads the vectors block from the 1.3 copy.

Validate:

```
cd /home/user/AGMessage/app && npm ci --no-audit --no-fund && npm run check && AGM_BROWSER="$(command -v google-chrome || command -v chromium)" npm run smoke
```

### AG-5b — AGMessage 0.5.1, unit 5: an honest Stagewatch link — node id, verdict, holds, stuck, pins only on an authenticated answer

*AGMessage.* Sources: agmessage AG-5; peers-not-screens AG-1; peers-not-screens AG-2; PeerLink profile AG-PL1. Gaps: G32, G43. Depends on: AG-5a, AG-2.

agmessage AG-5, pns AG-1 and AG-2, AG-PL1, on the core.
- The dial moves from cid=agmessage (nodes.js:386) to node=<id>&t=messenger&cid=<id>.
- 'connected' and the pin come only from the verdict or a keyed 2xx. Today they come on open and on any frame (:392, :399).
- One door for 401 and 429; a persisted hold; Link again now; refusal words for admins only.
- TLS by pin and probe, and no key sent plain.
- The delay resets only after verified + 10 s (:392).
- A pong never clears stuck (:397).
- performance.now().

Validate:

```
cd /home/user/AGMessage/app && npm ci --no-audit --no-fund && npm run check && AGM_BROWSER="$(command -v google-chrome || command -v chromium)" npm run smoke
```

### AG-6 — AGMessage 0.5.1, unit 6: Stagewatch's attention items, windows and commands in the brief and the admin page

*AGMessage.* Sources: agmessage AG-6. Gaps: G19. Depends on: AG-3, AG-5b.

agmessage AG-6.
- The brief and the admin page show Stagewatch's red and amber items and its windows, each with a mark.
- Window actions come from the listed rows.
- Desk proposals are fenced, show the risk before and after, use one ?from=, and are persisted.

G19's picker is declined, recorded by X-declines.

Validate:

```
cd /home/user/AGMessage/app && npm ci --no-audit --no-fund && npm run check && AGM_BROWSER="$(command -v google-chrome || command -v chromium)" npm run smoke
```

### AG-11a — AGMessage 0.5.1: papers and the version

*AGMessage.* Sources: agmessage AG-7; peers-not-screens AG-3. Gaps: G42, G19. Depends on: AG-6, AG-4, owner:A-versions, owner:X-declines.

agmessage AG-7, its 0.5.1 part.
- CHANGELOG '## 0.5.1', with 'Tests: N (was M)' and the declines X-declines confirms (G19's picker).
- package.json and version.js.
- README and NODES rows.
- The context-token text corrected.

The tag `v0.5.1` is the branch owner's.

Validate:

```
cd /home/user/AGMessage/app && npm ci --no-audit --no-fund && npm run check && AGM_BROWSER="$(command -v google-chrome || command -v chromium)" npm run smoke
```

### AG-7 — AGMessage 0.6.0, unit 1: LINK to Stagewatch — proofs, the session credential on side calls, floors

*AGMessage.* Sources: PeerLink profile AG-PL2. Gaps: G43, G08. Depends on: AG-11a, SW22.1b.

AG-PL2.
- link=1 LINK with a swk_ key.
- Nothing is taken before the acceptor's proof.
- The pin is set after that proof.
- Floors.
- S goes only to the pin.

Scenarios S1, S2, S4–S7, S10–S14, S17–S19.

Validate:

```
cd /home/user/AGMessage/app && npm ci --no-audit --no-fund && npm run check && AGM_BROWSER="$(command -v google-chrome || command -v chromium)" npm run smoke
```

### AG-8 — AGMessage 0.6.0, unit 2: a line connector to Patterns with a scoped key; no token anywhere

*AGMessage.* Sources: PeerLink profile AG-PL3. Gaps: G08, G37. Depends on: AG-7, P97.6.

AG-PL3, Patterns' leg.
- A line connector over net/tls to 9697.
- It proves an issued ptk_ key: follow, or drive where granted.
- No token is stored or sent.
- Listed low and review verbs within scope go with 13.12.3 receipts. High verbs are only proposed.
- CONTEXT every 5 s.

Scenarios S1, S2, S4–S7, S11–S14, S17–S19, S22.

Validate:

```
cd /home/user/AGMessage/app && npm ci --no-audit --no-fund && npm run check && AGM_BROWSER="$(command -v google-chrome || command -v chromium)" npm run smoke
```

### AG-8m — AGMessage 0.6.0, unit 3: the line connector's PlayMap leg

*AGMessage.* Sources: PeerLink profile AG-PL3; plan critique issue 13. Gaps: G08. Depends on: AG-8, PM-9c.

AG-PL3, PlayMap's leg: 9941 over TLS, or playmap.lines.v1 over pinned WSS, proving an issued pmk_ key.

It needs PM-9 and slips with it (rule R3). Until then PlayMap is a legacy amber row with no direct path.

Validate:

```
cd /home/user/AGMessage/app && npm ci --no-audit --no-fund && npm run check && AGM_BROWSER="$(command -v google-chrome || command -v chromium)" npm run smoke
```

### AG-9 — AGMessage 0.6.0, unit 4: AGMessage publishes for the Eye — displays, peer links, one row per link, chain ends

*AGMessage.* Sources: Eye AG-EYE.1. Gaps: G37, G24. Depends on: AG-4, owner:A-display-names.

AG-EYE.1.
- display:<id> items, their names per A-display-names. No text, files, halls or codes. Displays.status (displays.js:188) is unpublished today.
- Link items, one row per link to @<peer>.
- @<sw>/timer feeds a display while it shows that hall's countdown.
- The comment at context.js:3 is fixed (G37).
- Canary tests.

Validate:

```
cd /home/user/AGMessage/app && npm ci --no-audit --no-fund && npm run check && AGM_BROWSER="$(command -v google-chrome || command -v chromium)" npm run smoke
```

### AG-10 — AGMessage 0.6.0, unit 5: CONTEXT POST to Patterns, and the outcome shown on AGMessage's side

*AGMessage.* Sources: Eye AG-EYE.2; Eye critique (poster's outcome); plan critique issue 13. Gaps: G37. Depends on: AG-8, AG-9, P99.2.

AG-EYE.2, Patterns only.
- Post over AG-8's link with scope post.
- The board, the admin page and its own context say 'Patterns keeps this context (N s ago)', or give the refusal.

The PlayMap half is dropped: no unit makes PlayMap accept CONTEXT POST, and no PlayMap reader needs AGMessage's context. The Eye lives in Patterns.

Validate:

```
cd /home/user/AGMessage/app && npm ci --no-audit --no-fund && npm run check && AGM_BROWSER="$(command -v google-chrome || command -v chromium)" npm run smoke
```

### AG-11b — AGMessage 0.6.0: papers and the version

*AGMessage.* Sources: agmessage AG-7. Gaps: G37, G42. Depends on: AG-7, AG-8, AG-9, AG-10, owner:A-versions.

agmessage AG-7, its 0.6.0 part.
- CHANGELOG '## 0.6.0', with 'Tests: N (was M)'.
- package.json and version.js; README and NODES rows.

AG-8m is named as landed or waiting. The tag `v0.6.0` is the branch owner's.

Validate:

```
cd /home/user/AGMessage/app && npm ci --no-audit --no-fund && npm run check && AGM_BROWSER="$(command -v google-chrome || command -v chromium)" npm run smoke
```

### SW-INT-AG — Interop: AGMessage's rows, and the one-contract row

*Stagewatch.* Sources: agmessage SW-AG-2; ecosystem-lane SW-R8; ecosystem-lane SW-R1/SW-R2 (AGMessage rows); plan critique issue 8. Gaps: G44, G16, G18, G38. Depends on: AG-5b, AG-3, AG-8, SW21.9, SW19.10.

SW-AG-2, SW-R8, and the AGMessage rows of SW-R1 and SW-R2, at AGMessage's new pin:
- S1.3b;
- S2.2, S2.5, S2.8, S2.9;
- S3.1, S3.2, S3.8, S3.9;
- S6.2c; S6.5, reworded; S7.4;
- the desk rows; the ws and line pair rows.
- S7.2 (one contract): tools/showbus-copies.sh over all four clones, added now that the last copy (AG-4) has landed.

No Stagewatch release depends on it.

Validate:

```
cd /home/user/Stagewatch && tools/test.sh build interop
```

## Numbering

**PATTERNS**
- Round 88 is the newest: CHANGELOG.md:13, PLAN §106 (docs/PLAN.md:11929), last commit bcdbf2829cea302078acee7887841389e00d5178. tag-rounds.sh's table ends at 87 (:100).
- Rounds 89–99 are consecutive, in PLAN §107–§117. Unit ids are P<round>.<k>, and commit subjects are 'Round NN.k: …'.
- k=0 opens the round; the last k is its papers, with the tag row for the round before.

| Round | PLAN | Units | Content |
|---|---|---|---|
| 89 | §107 | 89.0–89.9 | the harms; tag row 88 |
| 90 | §108 | 90.0–90.2 | the bench host |
| 91 | §109 | 91.0–91.6 | Show Bus 1.3 from Patterns' side |
| 92 | §110 | 92.0–92.5 | the PeerLink core and the Stagewatch link |
| 93 | §111 | 93.0–93.4 | speaker message, playlist hold, VIDEO ON <key> |
| 94 | §112 | 94.0–94.3 | the Eye's NETWORK band |
| 95 | §113 | 95.0–95.4 | the link by proof; windows granted |
| 96 | §114 | 96.0–96.2 | node devices |
| 97 | §115 | 97.0–97.6 | the acceptor |
| 98 | §116 | 98.0–98.6 | the Eye: one id, one node |
| 99 | §117 | 99.0–99.2 | CONTEXT POST |

A unit moved by R3 takes its new round's next k.

**How the designs' labels map:**
- patterns-playmap-side 89.1–89.3 → 89.1–89.4 (89.3 split), and its papers → 89.9.
- The profile's twin unit → 89.6.
- The profile's 13.18 OSC ADR → 89.8.
- 89.5 is out by pin and probe, and in, the mark. 89.7 is the Host rule.
- ecosystem 'Round NN' → 90.
- peers-not-screens N.1/N.2 and pps 90.1 → 91.1.
- spec-1-3 and pps 90.2 → 91.2.
- cross-app and pps 90.3 → 91.3.
- spec-1-3 90.5 → 91.4.
- The 1.3 copy → 91.5; the papers → 91.6.
- The profile's 'Round 90' core and link → 92.1–92.3.
- pps 91.1 → 92.4.
- pps 91.2 and 91.3 → 93.1 and 93.2; G48's L49 → 93.3.
- EYE.1 → 94.
- 'Round PL' link=1 → 95.1.
- PT-W2 and PT-W4 → 95.2 and 95.3.
- pps 92.1 → 96.1.
- 'Round PL' acceptor → 97.1–97.5.
- EYE.2 → 98.1–98.2; EYE.3 → 98.3; EYE.5 → 98.4–98.5; EYE.4 → 99.1.

The Companion module takes its next version in each round that moves it: a patch for a fix, a minor for new actions or variables. lines.txt is regenerated in the same unit.

**STAGEWATCH**
- csproj says 2.16.0 (Timer.App.csproj:18), and ReleaseDate is 2026-10-11 (AppInfo.cs:16).
- 2.17 (windows) is five commits on the working branch, not on main; S-217 decides.
- One release per train: 2.18–2.24 for Trains 1–7, each through `pwsh tools\release.ps1 -Version 2.x.0 -ModuleVersion 2.x.0`, never a hand edit. 2.19 carries Show Bus 1.3 final.
- Units are SWvv.k, with a letter for the ordered commits of a split unit (SW19.1a–e).
- Lane-track and interop-only units need no release: SW19.8–SW19.10, SW19.11L, SW19.13, SW19.13m, SW-INT-PM and SW-INT-AG.
- The master is Show Bus 1.3, with §12 Windows, §13 PeerLink and §14 numbers, and a `landed` list of copies.

**PLAYMAP**
- Named round-2 fix batches, each under M-PMX with its own file list and a CARRYOVER entry, merged between wave merges:

| Batch | Unit |
|---|---|
| fix-showbus-identify | PM-1 |
| fix-showbus-keys | PM-2 |
| fix-media-page-import | PM-3 |
| fix-showbus-1.3 | PM-4 |
| fix-showbus-node-link | PM-5 |
| fix-peerlink-core | PM-6 |
| fix-stagewatch-timer-air | PM-7 |
| fix-stagewatch-context-size | PM-8 |

- PM-11 goes through the §5.7 spine lane.
- Packages:
  - wp4-peerlink: PM-9a–c, PM-10, PM-12;
  - wp4-showbus-windows: PM-14, PM-15a–c.
- PM-13 lands inside the integration pass (R2).
- Commits are 'Area: sentence'.
- WIRE, README and ROADMAP get papers notes for the integrator.

**AGMESSAGE**
- package.json says 0.5.0, untagged; the branch head is 6146ee9, whose newest commit is unit 20.
- AG-1 is 'AGMessage 0.5.0, unit 21', before the tag.
- AG-2–AG-6 are 0.5.1 units 1–6, with AG-5 split as 5a/5b. AG-11a closes 0.5.1.
- AG-7–AG-10 are 0.6.0 units 1–5, with AG-8m as unit 3. AG-11b closes 0.6.0.
- Tags are the branch owner's. spec-1-3's planned 0.4.1 is superseded.

## Owner questions

### [X-branches] Which branch does each tree's work commit to? Stagewatch, Patterns and Mapper sit on the working branch; Stagewatch's has five commits that are not on main.

*All four.* Gates: SW18.1, P89.0, PM-1.

**Why.** Patterns' brief: 'never push to a branch you were not given'. The trains need one named branch per tree, and pins for the interop lane.

**Recommended.** One branch per tree that you name; Stagewatch's after S-217 is settled. The plan pushes nowhere else.

### [P-numbers] Take rounds 89–99 and PLAN §107–§117 as numbered here, with the bench host as Round 90?

*Patterns.* Gates: P89.0.

**Why.** Five designs numbered the same work differently. Commit subjects carry 'Round NN.k', and pushed history is never rewritten, so the answer must come before Round 89's first commit, not before its papers.

**Recommended.** Yes. A later renumbering shifts only rounds not yet opened.

### [P-ADR016] ADR-016: the wire shows its state only to a paired connection. Scope A (NAV, MENU, CUE LIST, ARCADE and ASSISTANT stay open and named in §8) or scope B (every query pairs first)?

*Patterns.* Gates: P91.2.

**Why.** 9697 greets with STATE before NONCE (ControlService.cs:356-357), and IsQuery lets EYE and CONTEXT through unpaired (ControlProtocol.cs:1454). Once the Eye carries link records, the room's topology would reach anyone. Scope B breaks Patterns' own cross-machine asks.

**Recommended.** Scope A. STATE, EYE, CONTEXT and SUBSCRIBE pair first; the five named queries stay open and listed.

### [P-twin-secrets] Turn the twin's SendSecrets off by default and refuse it on a plain twin link to another machine, in Round 89?

*Patterns.* Gates: P89.6.

**Why.** Device passwords and, from 89.3, kept window passes ride the plain twin by default (ShowState.cs:2080), and the twin listens on every interface (TwinService.cs:488,512). The cost: after a takeover the standby lacks passwords and draws kept pages black (OPEN L60).

**Recommended.** Yes, in Round 89, with words on both desks and a §22 walk row. Twin TLS comes later.

### [P-OSC12h] End the 12-hour OSC trust an address earns from a wire or web proof (an ADR), in Round 89?

*Patterns.* Gates: P89.8.

**Why.** A proof trusts the address for OSC for 12 h today (ControlService.cs:439, 557-569, 979-981). 13.18 says a proof grants nothing to an address, and on venue Wi-Fi an address is not an identity. The change is unilateral.

**Recommended.** Yes. On a paired desk, OSC comes only from the OSC ALLOW list and loopback. Desks that relied on the carry-over add their controller to OSC ALLOW, which the papers say.

### [P-legacy] The standing brief says 'No legacy or previous version is considered'. May Patterns keep level-0 paths for other apps' older versions: HTTP evidence for an older Stagewatch (91.1), level 0 for a Stagewatch without LINK (95.1), PlayMap at level 0 (96.1, 98.1)?

*Patterns.* Gates: P91.1.

**Why.** Other apps' older versions are in the room until each upgrades, and dropping them strands a show mid-upgrade. Keeping them goes against the brief's words.

**Recommended.** Keep them as labelled, amber fallbacks for other apps only: never for Patterns' own past versions. Each is named in OPEN and removed at Show Bus 1.4's default flip.

### [P-scopes] Which Patterns verbs does each app-key scope reach (follow, post, drive, operate)?

*Patterns.* Gates: P97.0.

**Why.** Round 97 issues ptk_ keys, and the acceptor enforces the scope (13.5.3). A loose 'drive' would let a playout desk change outputs.

**Recommended.** - follow: STATE, CONTEXT, SUBSCRIBE, EYE.
- post: follow, plus CONTEXT POST and its own proposals and results.
- drive: post, plus VIDEO transport, CUE GO/STANDBY, PLAYLIST transport and STAGE MESSAGE.
- operate: everything.

No OUTPUTS, TAKE/CUT, SCREEN or settings below operate. §8 points to REMOTE.md.

### [S-217] 2.17 (windows) is five commits on the branch, unreleased (csproj 2.16.0). Release it before 2.18, or fold it into 2.18?

*Stagewatch.* Gates: SW18.8.

**Why.** Every later release and SW22's grants build on it. Its WHATS_NEXT section and release date need one answer.

**Recommended.** Release 2.17 first through release.ps1 -Version 2.17.0 -ModuleVersion 2.17.0. Folding it into 2.18 is the fallback, keeping its section.

### [S-releases] Seven releases (2.18–2.24) need the maintainer's PC seven times: smoke, the WPF work and release.ps1. Keep one per train, or merge two adjacent trains into one release?

*Stagewatch.* Gates: SW18.8, SW19.11.

**Why.** The PC is the only place UiSmoke and the installer run. Merging releases saves sessions but ships two trains' harms fixes later.

**Recommended.** One per train for 2.18 (the harms) and 2.19 (the contract). From 2.20 on, you may merge two adjacent trains into the later number; that release then validates both trains.

### [S-sections] Put PeerLink at §13 and the numbers at §14 of the 1.3 master?

*Stagewatch.* Gates: SW19.1a.

**Why.** spec-1-3 put the numbers at §13, while the PeerLink profile is written as §13. 23 references in unlanded designs follow the choice.

**Recommended.** §13 PeerLink, §14 numbers.

### [S-D0] May a window grant's page address ride exactly two carriers: a browser's address bar, and the one verified link to the grant's own app (TLS or local)? The doctrine line at CLAUDE.md:37-38 would change, and that line is yours to write.

*Stagewatch.* Gates: SW19.1c, SW22.4, PM-15b.

**Why.** §1 rule 5 and the doctrine keep window addresses out of every frame. Delivering a grant to Patterns or PlayMap needs a named exemption written before the wire does it (windows-for-peers D0).

**Recommended.** Yes, as D0 words it, saying 'verified PeerLink (or key-verified node link)'.

### [S-exemptions] Keep ?pin= in browser addresses, and Companion TCP 9923's unauthenticated door when bound to the network, as named §8 exemptions in 1.3?

*Stagewatch.* Gates: SW19.1e.

**Why.** spec-1-3 lists both as 'not scheduled'. 1.3's text needs the wording, and the health lights must say what these doors leave open.

**Recommended.** Keep both named in 1.3, and schedule 9923's authentication with 1.4's default flip.

### [S-interop-gate] Make Stagewatch's release job wait for the interop job at the siblings' pinned commits?

*Stagewatch.* Gates: SW19.10.

**Why.** The lane is the only cross-app proof (G44). Pins mean only a failing pinned row blocks, never a sibling's red head.

**Recommended.** Yes. The nightly heads run reports drift only.

### [S-interop-ci] Create a read-only SIBLINGS_READ_TOKEN for Patterns, Mapper and AGMessage, and allow the interop job to relax Ubuntu's unprivileged-userns AppArmor restriction on its ephemeral runner?

*Stagewatch.* Gates: SW19.10.

**Why.** The job checks out the siblings at their pins (ecosystem-lane.md:794-798), which needs the token if they are private. The namespaced LAN needs `sysctl kernel.apparmor_restrict_unprivileged_userns=0` (:808). Both are yours to grant.

**Recommended.** Yes: a fine-grained token with contents:read on the three repos. It never persists in a checkout, and forks skip the job with words.

### [S-shared-keys] When 2.18 binds each node key to one app, what happens to a key two apps share today?

*Stagewatch.* Gates: SW18.1.

**Why.** SW18.1 binds a key on its first use, and SW18.2 refuses it for a second id. A room that gave Patterns and PlayMap one key would see one of them refused after the upgrade.

**Recommended.** Refuse the second app with words ('this key speaks for <kind> <id> — make a key for <app>'), offer Make a key for <app> in one press on its Show network row, and say so in 2.18's upgrade notes. Never let one key speak for two ids.

### [S-brief] The plan needs two lines in Stagewatch's CLAUDE.md: `tools/test.sh interop` in the loop, and the doctrine's D0 wording. Will you write them?

*Stagewatch.* Gates: SW19.11L.

**Why.** No agent edits an agent brief. The lane's papers list the line as your action.

**Recommended.** Yes, when SW19.11L lands, and D0's line with S-D0's answer.

### [X-declines] Confirm the fixes this plan declines or defers without building them: G46 (a peer's page in a Stagewatch window), G49's 'Link all', G19's proposal picker, G33's WEB OPEN/TYPE, G34's assistant-proposable link, and G21's crew-key ACK policy.

*All four.* Gates: SW22.12, AG-11a.

**Why.** Each gap's fix is designed, but nobody in the room has asked, or the fix would carry a secret on a proposal line. The papers must record a decision, not a silence.

**Recommended.** Confirm all six as declined for now. Stagewatch 2.22's WHATS_NEXT records G46 and G49, AGMessage 0.5.1's CHANGELOG records G19, and Patterns' OPEN keeps G33 and G34.

### [M-PMX] May the Show Bus safety and PeerLink fixes to unowned round-2 files land during round 3 as named round-2 fix batches, spine lines only through §5.7? The files, per batch:

- PM-1: Engine/Host/NetworkHost.cs, Net/Links/StagewatchScreenCommands.cs (new).
- PM-2: Net/Links/{StagewatchLink,PatternsLink}.cs, Net/Wire/{WireClient,WireSession}.cs, Net/Http/HttpParts.cs, Engine/Host/NetworkHost.cs, Core/Bus/Pairing.cs, Engine/Devices/NodeCredentialStore.cs, Engine/Show/PreflightBreadth.cs.
- PM-3: Engine/EngineCore.Content.cs, plus VerbCatalog.Redacted through §5.7.
- PM-4: docs/SHOWBUS.md and tools/showbus/ (new), Net/Bus/HelloService.cs, docs/SHOWBUS-NODE.md, docs/FLEET.md.
- PM-5: Net/Links/StagewatchLink.cs, Engine/Show/PreflightBreadth.cs.
- PM-6: Core/Bus/{PeerLink,ShowBus,Pairing}.cs, Core/Time/ClockOffsetEstimator.cs, Net/Wire/WireClient.cs, Net/Links/StagewatchLink.cs, Engine/Host/NetworkHost.cs.
- PM-7: Net/Links/StagewatchTimer.cs, Engine/Rendering/TimerView.cs.
- PM-8: Net/Links/StagewatchLink.cs, Net/Wire/WireSession.cs.

Each batch also adds its tests and a docs/round2/CARRYOVER.md entry.

*Mapper.* Gates: PM-1, PM-2, PM-3, PM-4, PM-5, PM-6, PM-7, PM-8.

**Why.** round3/PLAN.md §4 (:2121-2125) forbids edits to unnamed folders during a wave. Engine/Rendering/** was wp3-rhi's in W1 (:578), and VerbCatalog outside its blocks is a spine file (:307-311). Without leave, PlayMap, until the integration pass:
- flashes projectors on any identify (NetworkHost.cs:783);
- sends its key plain to typed addresses;
- follows a blackout from whoever answers;
- stays legacy to every peer.

Fix batches already merge during round 3 (the fix-*-win batches).

**Recommended.** Grant it for PM-1 to PM-8, each a named batch with a CARRYOVER entry, merged between wave merges. WIRE, README and ROADMAP stay the integrator's, through papers notes.

### [M-content-before-W3] May fix-media-page-import edit EngineCore.Content.cs before W3 starts? Desk-map owns it in W3 and canvas in W4.

*Mapper.* Gates: PM-3.

**Why.** PlayMap imports a page it cannot draw and keeps a window pass in the project (G47). The refusal lives in that file.

**Recommended.** Yes, before W3; otherwise at the integration pass.

### [M-wp4-peerlink] Open a post-round-3 package, wp4-peerlink, for PlayMap as a PeerLink acceptor and connector?

It brings VerbCatalog LINK, RECEIPTS and KEY rows; STATE clients and links records, with fixtures and cards; a pmk_ table with scopes mapped to ShowPolicy; and TLS on 9941.

*Mapper.* Gates: PM-9a, PM-10, PM-12.

**Why.** These touch the spine-owned VerbCatalog, ShowPolicy and fixtures. Until it lands, peers see PlayMap at level 0, and GO is never timed over a proved link.

**Recommended.** Yes, first after the integration pass.

### [M-trusted-addresses] Keep TrustedAddresses only for legacy Lines devices (amber 'trusted by address — not a proof'), never for an app link?

*Mapper.* Gates: PM-9a.

**Why.** Trusted addresses pair without proof today (Pairing.cs:113-115). An address is not an identity, and a proof grants nothing to one (13.18).

**Recommended.** Yes.

### [M-eye-spine] Carry the Eye's context rows through the spine lane in ContextBuilder.cs, and leave ContextShape.cs and EyeContextTests untouched this round?

*Mapper.* Gates: PM-11.

**Why.** ContextBuilder.cs is a spine file. ContextShape.cs and EyeContextTests are not (round3/PLAN.md:307-311), so the lane cannot carry them; Patterns forces link items and chain ends instead.

**Recommended.** Yes.

### [M-decision7] Round 2 decision 7: may PlayMap's assistant use Stagewatch's approval desk?

*Mapper.* Gates: PM-17.

**Why.** G13 is designed (cross-app PM-1) but parked on this decision (CARRYOVER.md:99) and on W4's EngineCore.Assistant.cs.

**Recommended.** Yes, after W4, with CollectApproved=false so that StagewatchLink stays the one collector.

### [M-decision17] Revisit decision 17: on a node with a token, may TCP 9941's STATE greeting and CONTEXT wait for pairing, once Patterns' node devices pair by proof (Round 96)?

*Mapper.* Gates: PM-19.

**Why.** 9941 sends STATE on HELLO before pairing (WireSession.cs:263-276). 1.3 §8 lists this as PlayMap's exemption until then.

**Recommended.** Yes, after Round 96.

### [M-G36] May PlayMap's Patterns link drive Patterns, through a chase source and LINK PATTERNS SEND?

*Mapper.* Gates: PM-18.

**Why.** G36 is designed minimal, nobody in the room has asked for it, and frame accuracy would need an ANCHOR line.

**Recommended.** Defer until someone asks; keep the design.

### [M-windows-package] Open a post-round-3 package, wp4-showbus-windows, for native windows and the timer's remaining fields (UX.md included)?

*Mapper.* Gates: PM-14, PM-15a.

**Why.** G11 and the rest of G12 need SourceKind.Window, catalogue words, StateWriter and ContextBuilder, which are spine files, plus wave-owned readers.

**Recommended.** Yes, beside or after wp4-peerlink.

### [A-branch] Who owns AGMessage's 0.5.0 branch, and may this plan's AGMessage units land there as 'AGMessage 0.5.0, unit 21' onward? If not, may they be handed to that branch's session?

*AGMessage.* Gates: AG-1.

**Why.** The branch is another session's. It moved 8 commits (9d7f5c3 → 6146ee9, the last at 07:09) while this plan was written, and its newest commit is 'the papers for the last review round'. The plan must not push to a branch it was not given.

**Recommended.** Hand AG-1's design to the session that owns the branch, for it to land as unit 21 before tagging 0.5.0. Later units follow the same way, or on a branch you name.

### [A-hold-050] Hold the 0.5.0 tag until AG-1 lands on the branch?

*AGMessage.* Gates: AG-1.

**Why.** At 6146ee9 the branch still sends Patterns' and PlayMap's full-control token in a header over plain HTTP to any node that is neither pinned nor announcing TLS (peerlinks.js:259-261, :292), and drives them with it (:346-356). That leak should block the ship.

**Recommended.** Yes. AG-1 goes into 0.5.0; the rest follows as 0.5.1 and 0.6.0.

### [A-direct-path] Accept losing the direct send path to Patterns and PlayMap until they issue AGMessage a scoped key?

*AGMessage.* Gates: AG-1.

**Why.** agmessage decision 10: the pairing token is too strong for a messenger. Meanwhile, lines become desk proposals.

**Recommended.** Yes. AG-8 and AG-8m restore the path with scoped keys.

### [A-display-names] May display names appear in AGMessage's Show Bus context?

*AGMessage.* Gates: AG-9.

**Why.** The Eye draws AGMessage's displays (AG-9), and a display name may name a place. The privacy rule already keeps text, files, halls and codes out.

**Recommended.** Publish the admin-set name (60 characters at most), with a per-display 'keep private' switch that falls back to 'Screen N'.

### [A-versions] Version the remaining work as 0.5.1 (AG-2 to AG-6 and the 1.3 copy) and 0.6.0 (LINK, the line connector, the Eye)?

*AGMessage.* Gates: AG-11a, AG-11b.

**Why.** spec-1-3's planned 0.4.1 was overtaken by 0.5.0. The CHANGELOG, version.js and commit subjects need the numbers.

**Recommended.** Yes.

## Gap map

| Gap | Units | Left, and why |
|---|---|---|
| G01 | SW20.1, SW20.2, SW20.3, SW20.4a, SW20.4b, SW20.5, SW20.11, SW20.12, SW20.13, SW20.14 |  |
| G02 | SW21.1, SW21.2, PM-13 |  |
| G03 | P91.3, SW21.2 |  |
| G04 | SW18.6, P89.1, P89.9, SW19.9b |  |
| G05 | SW21.2, AG-2 | The line is already fixed on AGMessage's 0.5.0 branch (stagelines.js:8-19). What remains is Stagewatch's fence on peers' documented lines and the stored risk shown. |
| G06 | SW18.3, SW18.4, SW18.6, SW18.8, PM-1, PM-5 |  |
| G07 | SW20.4a, SW20.5, SW20.13, SW20.14, P97.5 |  |
| G08 | SW18.7, P89.5, P89.7, P89.8, P89.9, PM-2, AG-1, SW20.4a, SW20.6, P92.1, P92.2, PM-6, P95.1, P97.1, P97.2, P97.3, P97.4, P97.6, SW23.1, SW23.2, PM-9a, PM-9b, PM-9c, PM-10, AG-7, AG-8, AG-8m | Companion decks on the line wires keep the legacy proof until their modules adopt LINK (13.18). A pairing-code proof over plain stays a guessing target until each acceptor offers TLS; the code-plain flag says so. |
| G09 | P89.2, P89.3, P89.4, P89.6, P89.9, P91.2 | Kept passes sit in per-machine JSON (OPEN L40) until DPAPI lands; the twin is plain TCP (L36) until twin TLS. From 89.6, secrets stay off it by default. |
| G10 | SW22.3, SW22.4, SW22.6, P95.2, P95.3, P95.4, SW22.11 |  |
| G11 | SW22.4, SW22.6, PM-15a, PM-15b, PM-15c, PM-20, SW-INT-PM |  |
| G12 | PM-7, PM-14, PM-20 |  |
| G13 | PM-17, PM-20 | Waits for the owner's round-2 decision 7 and for W4. |
| G14 | SW21.3, SW21.4, SW21.5, SW21.6, SW21.10, SW22.8, SW22.9, SW22.10, SW22.12 |  |
| G15 | SW18.1, SW18.2, SW18.5, SW18.6, SW18.8, SW22.1a, SW22.1b | A Stagewatch with no PIN still accepts a context for an unbound id. It is labelled unverified and never sets risk. Tying an id to an address was rejected. |
| G16 | P92.2, P92.3, PM-6, AG-3, SW-INT-AG | AGMessage 0.5.0 already keys runs and retries each tick (server.js:2129-2143 at 6146ee9). Persistence, the read-back and the recovery read remain (AG-3). |
| G17 | SW19.1e, AG-2 |  |
| G18 | AG-4, SW-INT-AG | PlayMap's hello is already heard and named on 0.5.0 (nodes.js:40). Hearing any well-formed kind remains. |
| G19 | AG-6, AG-11a | The proposal picker and AGMessage's own AI proposals are declined for now (X-declines): nobody in the room has asked. |
| G20 | SW21.4, SW21.5, SW21.6, SW21.10, SW22.8 |  |
| G21 | SW20.10, P93.1, P93.4 | A forwarded ACK can be refused under Stagewatch's crew-key policy, and Patterns shows the refusal. Patterns' messages carry no urgency. The crew-key policy is in X-declines. |
| G22 | SW19.1c, SW19.6, P91.5, PM-4, SW22.2, SW22.5, SW22.7, SW22.12, AG-4 |  |
| G23 | SW22.2, P98.1, SW24.1 |  |
| G24 | P94.1, P94.2, P94.3, SW22.2, SW22.5, SW22.6, P98.1, P98.2, P98.3, P98.4, P98.5, P98.6, SW24.1, SW24.2, PM-11, PM-15c, AG-9 |  |
| G25 | SW20.9, PM-8 |  |
| G26 | SW20.3, SW20.4a, SW20.14, PM-8 | PM-8's fence measures. A video-shaped STATE for a HELLO Stagewatch session (the gap's PlayMap fix) lands only if the fence finds a real show past the bound. Measured, then governed. |
| G27 | SW19.3, SW19.7 | The same-machine carrier with no network waits for LB-1's Windows run on the maintainer's PC, and goes in 1.4. |
| G28 | SW20.7, SW20.14, SW23.1, PM-10 | A Stagewatch-fed timecode source in PlayMap (ROADMAP R3.7) is not planned. |
| G29 | SW20.1, SW20.7, SW20.11, SW20.12, SW20.14 |  |
| G30 | PM-4, SW20.8, SW20.11, SW20.14 |  |
| G31 | PM-2, PM-5, PM-16 |  |
| G32 | SW18.3, P91.1, AG-5b |  |
| G33 | P91.3 | WEB OPEN and WEB TYPE stay unadvertised: a window pass or a password would ride a proposal line (X-declines). |
| G34 | P92.4, P92.5 | An assistant-proposable STAGEWATCH LINK is declined: repointing a key-bearing link is a person's decision (X-declines). |
| G35 | P91.3, PM-4, P96.1, P96.2, P97.5, PM-9b, P98.1, P98.3, P98.4, P98.5 | Generic Lines devices (third-party projectors and players) keep their own protocols. Proof applies only where the far end is a Show Bus node, as PlayMap is from 96.1. |
| G36 | PM-18 | Waits for the owner's answer (M-G36). Frame-accurate following (ANCHOR) is not built. |
| G37 | P98.1, P98.3, P99.1, P99.2, PM-12, AG-8, AG-9, AG-10, AG-11b | By decision, AGMessage does not read Patterns' context; its own context reaches Patterns by CONTEXT POST. PlayMap accepts no CONTEXT POST: no PlayMap reader needs it, and AG-10 posts to Patterns only. |
| G38 | SW19.1a, SW19.1b, SW19.1d, SW19.12, SW19.11, P91.5, PM-4, AG-4, SW-INT-AG |  |
| G39 | SW19.5, SW19.12, P91.4, AG-4 | Key-continuity rollover is recorded for 1.4. Pinned peers see CERTIFICATE CHANGED once. |
| G40 | SW19.1b, SW19.3, SW19.4, SW19.12, P91.4, P91.5, PM-4, AG-4 |  |
| G41 | SW19.12, SW19.11 | Dropping UDP 9700 from Find waits for Patterns' 9928 walk (OPEN L47). SW19.11's WHATS_NEXT row names it, and the release after that walk row is filled takes it. |
| G42 | SW19.1e, SW19.12, AG-4, AG-11a, AG-11b |  |
| G43 | SW18.3, SW18.6, P91.1, PM-5, SW22.1a, SW22.1b, SW22.1c, P95.1, AG-5a, AG-5b, AG-7 |  |
| G44 | SW19.2a, SW19.2b, SW19.2c, SW19.8, SW19.9a, SW19.9b, SW19.10, SW19.11L, P90.1, P90.2, SW19.13, SW19.13m, SW20.13, SW21.9, SW22.11, SW-INT-PM, SW-INT-AG | Two machines, Windows UDP and firewall, WebView2, DPAPI, D3D12, a TLS rig, and real decoders and models stay walk rows (QUALIFICATION §26–27; OPEN L47, L50, L56). |
| G45 | SW19.1e, P91.2, P91.6, PM-19 | Stagewatch's ?pin= and 9923 stay named exemptions (S-exemptions). PlayMap's 9941 waits for its owner (decision 17). |
| G46 | SW22.12 | Declined for now (X-declines), recorded in Stagewatch 2.22's WHATS_NEXT. A cross-origin page would break the window's CSP (WebServer.cs:621), window.SW and pinning, so a window shows only its own Stagewatch's pages. |
| G47 | PM-3 | https HLS/DASH as a Stream needs an FFmpeg TLS probe; until then it is refused with the reason. |
| G48 | SW20.4b, SW20.5, P93.2, P93.3, P93.4 |  |
| G49 | SW21.7, SW21.8, SW21.9, SW21.10, SW22.12 | 'Link all' (a key minted per heard node over the pinned TLS path) needs its own security design, and is declined for now (X-declines). A clock-corrected merged journal is not planned. |

## Deferred

- Show Bus 1.4 flips the defaults. It refuses:
  - bearer keys sent plain from the LAN;
  - legacy proofs from other machines;
  - plain line app links where TLS is offered;
  - node links without LINK.

  It also carries certificate key-continuity rollover and the same-machine carrier (LB-1), and removes the level-0 fallbacks P-legacy keeps.

- Measured features, each only once its show writer's stall is measured (LP-1): stuck on line acceptors (the fifth PONG word), and expire. Also the handshake cost, the side-call burst on 64 slots (LP-2), and sleep/resume against the self-gap (LP-3). No governor lands before its measurement.

- Companion decks on Patterns' and PlayMap's wires adopting LINK, each in its own module round. Until then they keep the legacy proof and never send plain AUTH off the machine.

- Twin TLS; twin ACT/ACTED outcomes and a keep-alive beat; a standby inheriting accepted PeerLinks and posted contexts after takeover (an OPEN row from P89.6).

- Declined for now, pending X-declines: G46 (a peer's page in a Stagewatch window); G49's 'Link all'; G19's picker and AGMessage AI proposals; G33's WEB OPEN/TYPE; G34's assistant-proposable link; G21's crew-key ACK policy. G36's ANCHOR line waits for M-G36.

- G26's video-shaped STATE for a HELLO Stagewatch session: only if PM-8's fence finds a real show past the reader's bound.

- Dropping UDP 9700 from Stagewatch's Find, after Patterns' 9928 walk (OPEN L47).

- AGMessage posting its context to PlayMap: no PlayMap reader needs it.

- Eye: replay relighting link edges from the journal; the problems queue's memory (OPEN L44); NDI chains across apps; the same edges on PlayMap's fleet board and Stagewatch's Show network; the Eye in a browser.

- PlayMap rendering web pages (ROADMAP L10), and https streams behind an FFmpeg TLS probe.

- Patterns' kept passes and grants under DPAPI, and a sealed twin link (OPEN L36, L40).

- G15's remainder (a no-PIN Stagewatch), and a new viewing code reaching screens already open.

- Interop rows that need two machines, Windows (UiSmoke, WebView2, DPAPI), D3D12, a TLS rig, real decoders or real AI models.

- Declined by the profile: a per-request HTTP MAC; http and twin carriers; resend by id; a default command expiry; 'observed' as a wire outcome; a PAKE; TLS-exporter binding.

## Renumbering note (orchestrator, 2026-10-26)
Patterns Round 92 became a repair round (L64: CI's red test job, both halves; L65: the twin key out of the log, round 91's 91.7;
the 1.3 copy, round 91's 91.5, if SW19.12 has landed). The plan's P92.x (the PeerLink round) is Patterns Round 93, and every later
Patterns round of this plan moves up one (P93.x -> 94, P94.x -> 95, ...). PlayMap PM-4 moved under R3 (SW19.12 not landed at its
turn); PM-5 committed 616922a with the owner's leave extended to NetworkHost.cs and MainViewModel.LinkLight.
