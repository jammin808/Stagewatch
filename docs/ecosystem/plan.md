# Show Bus 1.3 and PeerLink across four trees — the integrated plan

Design work only; nothing here is built. Sources: design/*.md and design/json/*.json (eight revised areas), design/peerlink.md (the PeerLink profile), design/eye.md (the Eye) and its critique, gaps.json. Unit ids are the anchors used by the plan's StructuredOutput.

## Overview

ONE PLAN FOR THE FOUR TREES. Show Bus is the contract (what nodes say); PeerLink is layer 2 (how two apps that proved who they are hold one bounded, live, measured link that carries it); views — Patterns' Eye first — read only the link record and the contexts. This plan integrates the eight revised area designs (design/*.md and design/json/*.json, each already answering its critic), the PeerLink profile (design/peerlink.md) and the Eye design (design/eye.md) with its critique's twenty issues and thirteen unmapped gaps folded into the units that touch them. 134 units in nine trains; each train lands and is validated before the next.

THE TRAINS. (1) Stop the five harms — the stuck Stagewatch unseen in Patterns (P89.1), the window pass in Patterns' STATE (P89.2–3), peers hit by screen commands (SW18.3, PM-1), tokens in clear (SW18.7, P89.4 incl. Patterns' Companion module, PM-2), a node key speaking for another node (SW18.1–2) — each unilateral and safe against today's peers. (2) One contract, one kit, one bench — Show Bus 1.3 final with §12 Windows, §13 PeerLink, §14 numbers and the Eye's P1–P8; the vectors and the scripted peer in every tree; hello and certificates; Patterns and PlayMap dial as nodes; Patterns' state only to paired connections; the interop lane (four real apps on a one-kernel LAN), which from then on is part of every train's validation. (3) Every link honest from its own end — PeerLink cores and connector hygiene in Stagewatch (PlayoutLink, with the playout-links design), Patterns (Round 92) and PlayMap (PM-6); speaker message and playlist hold (Round 93). (4) One desk for every app, one verdict for the room, the Eye's NETWORK band. (5) The hub proves itself (LINK on /ws), windows granted to verified links, release B, PlayMap known as a Patterns node device. (6) Patterns and PlayMap as acceptors (TLS on 9697, LINK, scoped app keys, receipts) and PlayoutLink by proof. (7) The Eye sees the room — one id one node, links as edges with their records, lights computed once for every reader, CONTEXT POST. (8) PlayMap after round 3's integration pass. (9) AGMessage on its 0.5.0 branch, last as asked.

DE-DUPLICATED. The 1.3 text is one edit (SW19.1) carrying spec-1-3, windows-for-peers, peerlink (§13/§14; spec-1-3's numbers block moves to §14), the Eye's §6 rules, and every design's 'words handed to S1'; no papers unit edits SHOWBUS.md, and the copies are P91.5, PM-4 and AG-4 under tools/showbus-copies.sh. PeerContexts' caps (commands 256, verbs/words 512, items 200, links 400, UTF-8 bytes) live only in SW21.1; the 64 KiB body cap only in SW20.9; NodeBindings only in SW18.1. Held addresses are one pair of units (P89.2–3 = patterns-playmap-side 89.2–3 = windows-for-peers PT-W1). The node link/verdict is one unit per repo (SW18.3, P91.1, PM-5, AG-5). Results retried with read-back once per repo (AG-3, P92.2, PM-6). STAGEWATCH verbs and the trust verbs are defined once (P92.3) and reused by the Eye's NetworkMenus (P98.3). The hello table cap (spec-1-3 and the Eye critique) is P91.4. The brief's 32 is P91.3. PlayMap's page-import refusal is one batch (PM-3 = PM-F1/F2 = PM-W1); every SHOWBUS-NODE.md edit of trains 1–3 is PM-4; the cid fix is one unit per app. Patterns' round numbers, which the designs set at 89, 90, 91, 92, 'N', 'NN', 'PL' and 'EYE', are now 89–99 consecutively (see numbering).

INTEGRATION DECISIONS. Round 89.1 adopts the profile's stuck lights (link live and amber, values red/no time, card amber) so the Eye never reverses it. The first-use confirm (TRUST, one press beside the short fingerprint) and the changed-certificate ACCEPT (≥ 8 hex typed, never pre-filled, never on a pad) are separate verbs. The Eye's band is a new Band field beside Plane; PlayMap's node device (Round 96) draws into that band rather than the VIDEO band. Only own things drive the rail, eye.worst and the exported headline. CONTEXT POST and EYE's link fields need pairing, which Round 91.2 (ADR-016) provides.

PROCESS PER REPO. Stagewatch: one release per train (2.18–2.24), tools/test.sh lanes green, WPF work named as the maintainer's PC work. Patterns: one round per request, one commit per unit 'Round NN.k: …', all seven suites and the module green, papers with the tag row for the round before; rounds that touch a platform path (89 WebView2/twin, 92 twin, 95 WebView2) stay 'unwalked' until the QUALIFICATION §22 rows are filled. PlayMap: nothing edits a file no round-3 wave owns without the owner's answer (round3/PLAN.md:2121-2125); the plan asks for named round-2 fix batches (PM-X) and two post-round-3 packages; any Mapper unit whose answer has not come moves to Train 8, its train is validated without it, and peers show PlayMap 'legacy' (amber) meanwhile. AGMessage: units on top of AGMessage's 0.5.0 branch (9d7f5c3), npm test and npm run check in app/; AG-1 (the operator token in clear) is a ship blocker the owner should take into 0.5.0 even though the train is last.

## Numbering

PATTERNS. Round 88 is the newest (CHANGELOG.md:13; PLAN §106; its last commit bcdbf2829cea302078acee7887841389e00d5178; tag-rounds.sh's table ends at 87). Rounds 89–99 are consecutive, PLAN §107–§117, units 'Round NN.k', each round's papers unit adding the tag-table row for the round before: 89 safety (§107; tag row 88) · 90 the bench host (§108) · 91 Show Bus 1.3 from Patterns' side (§109) · 92 PeerLink core and the operator's Stagewatch link (§110) · 93 speaker message and playlist hold (§111) · 94 the Eye's NETWORK band (§112) · 95 the Stagewatch link by proof and windows granted (§113) · 96 Show Bus node devices (§114) · 97 Patterns as an acceptor (§115) · 98 the Eye: one id one node (§116) · 99 CONTEXT POST (§117). The designs' labels map as: patterns-playmap-side 89.1/89.2/89.3/89.4 → 89.1/89.2/89.3/89.5 (89.4 is the new no-token-in-clear unit); windows-for-peers PT-W1 → 89.2–89.3; ecosystem-lane 'Round NN' → 90; peers-not-screens 'Round N.1/N.2' and patterns-playmap-side 90.1 → 91.1; spec-1-3/patterns-playmap-side 90.2 → 91.2; cross-app-proposals/patterns-playmap-side 90.3 → 91.3; spec-1-3 90.5 → 91.4; the 90.4 papers of four designs → 91.5; the profile's 'Round 90' core, link and twin units → 92.1, 92.2, 92.4; patterns-playmap-side 91.1 → 92.3, 91.2 → 93.1, 91.3 → 93.2; the Eye's EYE.1 → 94; the profile's 'Round PL' link=1/S → 95.1; windows-for-peers PT-W2/PT-W4 → 95.2/95.3 (PT-W3's Eye part → 98.1, its NODES ASK WINDOW → 95.2, its brief cap → 91.3); patterns-playmap-side 92.1 → 96.1; 'Round PL' acceptor → 97; EYE.2/3/5 → 98; EYE.4 → 99; each design's papers unit split into its round's papers. The Companion module takes its next version in each round that moves it (a patch for a fix such as 89.4, a minor for new actions or variables), named in that round's CHANGELOG line. STAGEWATCH. Version 2.16.0 in Timer.App.csproj with the 2.17 windows work on the branch; one release per train: 2.18 (Train 1), 2.19 (Train 2, Show Bus 1.3 final), 2.20 (3), 2.21 (4), 2.22 (5), 2.23 (6), 2.24 (7); units SWvv.k; interop-only commits in Trains 8–9 (SW-INT-PM, SW-INT-AG) need no release; every design's 'REMOTE 2.18' section is written as the release that lands it. The master is 'Show Bus 1.3' with §12 Windows, §13 PeerLink and §14 numbers (spec-1-3's §13 numbers renumbered). PLAYMAP. Named round-2 fix batches during round 3 (on PM-X): fix-showbus-identify (PM-1), fix-showbus-keys (PM-2), fix-media-page-import (PM-3), fix-showbus-1.3 (PM-4), fix-showbus-node-link (PM-5), fix-peerlink-core (PM-6), fix-stagewatch-timer-air (PM-7), fix-stagewatch-context-size (PM-8); 'Spine: the context names its peers and the windows it draws' (PM-11, the §5.7 lane); post-round-3 packages wp4-peerlink (PM-9, PM-10, PM-12) and wp4-showbus-windows (PM-14, PM-15); PM-13 inside round 3's integration pass; commit subjects in the tree's 'Area: sentence' form with CARRYOVER entries. AGMESSAGE. package.json says 0.5.0 (unreleased, CHANGELOG dated 2026-10-24): AG-1 inside 0.5.0 before it is tagged; AG-2…AG-6 (and the 1.3 copy) as 0.5.1; AG-7…AG-10 as 0.6.0; spec-1-3's planned '0.4.1' is superseded.

## Train 1 — Stop the five harms (Stagewatch 2.18, Patterns Round 89, PlayMap safety fix batches)

**Goal.** Each named harm is closed by the app that causes it, unilaterally and safe against today's peers, with no new shape between apps except Stagewatch's node-link verdict (older peers ignore it): a stuck Stagewatch shows no time on Patterns (G04); no window pass in Patterns' STATE, journal, bundle or twin (G09); screen commands never reach a show app (G06); no token or key crosses the network in clear from Stagewatch, Patterns (and its Companion module) or PlayMap (G08 class); one node key speaks for one node id on every Stagewatch route (G15). Validated when Stagewatch's lanes are green with nodes.e2e.cjs and stall.e2e.cjs (whose bytes Patterns' tests replay), Patterns Round 89 closes with all seven suites and the module, and PlayMap's batches pass both CI jobs. AGMessage 0.5.0's operator token in clear is Train 9's first unit and an owner question (hold the 0.5.0 tag).

### SW18.1 — Stagewatch — NodeBindings: one node key speaks for one node id (fail-closed); PeerContexts and ProposalDesk on it

peers-not-screens SW-1 as designed (peers-not-screens.md §1 D1–D4, §3). New src/Timer.Core/NodeBindings.cs: NodeProof {None, Operator, Key}, record struct NodeCredential and NodeVerdict; key ↔ (kind, id) under a lock, seeded from ApiKey.Node/NodeKind, Bound raised outside the lock. ApiKeys.cs appends node/nodeKind (kept only on node-scope keys). ShowBus.cs PeerContexts(NodeBindings).Accept = parse → Speak → stronger-proof rule → store; Entry.Proof; RiskFor is high with no entry or Proof None, so an unverified context never lowers a risk. Approvals.cs ProposalDesk(deskId, bindings): Submit/Collect/Result take a NodeCredential; a refused Submit is 403 'this key speaks for <kind> <id>'. Today a node key can post a context under any id and relabel another app's risks (gaps.json G15; the route at src/Timer.Remote/WebServer.cs:452). The PeerContexts caps are not set here (SW21.1 owns them). Tests: new tests/Stagewatch.Tests/NodeTests.cs (bind on first use, another id refused, rebind only explicit, operator vs key proof, the stronger proof wins, unverified never sets risk); ShowBusTests' BusControl fake and DeskRules move to the credential overloads.

- **Sources:** peers-not-screens SW-1
- **Gaps:** G15
- **Depends on:** —
- **Validate:** `cd /home/user/Stagewatch && tools/test.sh build unit`

### SW18.2 — Stagewatch — Every node route checks its credential before storing; unknown keys no longer feed the PIN lockout

peers-not-screens SW-2. WebServer gains CredentialOf on POST /api/context, /api/peers/context, /api/proposals* (from, collect, result) and the message routes; the check runs before anything is stored; 403 bodies carry words. Authorize stops counting an unknown or revoked key toward the per-address lockout wrong PINs use (today both go into _authFailures, src/Timer.Remote/WebServer.cs:1077-1082) — the profile's 13.5.6 at level 0 and the ecosystem lane's F1 fix; the lockout check before it stays. IShowContext/IShowApprovals signatures, the fakes and tests/Stagewatch.UiSmoke signatures follow. WebV2Tests against the real WebServer: a forged context for a bound id is 403 and stores nothing; a revoked key no longer locks its address; a key shared by two ids is refused for the second.

- **Sources:** peers-not-screens SW-2
- **Gaps:** G15
- **Depends on:** SW18.1
- **Validate:** `cd /home/user/Stagewatch && tools/test.sh build unit; on the maintainer's PC: pwsh tools\smoke.ps1`

### SW18.3 — Stagewatch — Show apps leave Screens: node links on /ws hear a verdict first; screen commands never reach a peer

peers-not-screens SW-3 (its §2.1 wire, D5–D9). New src/Timer.Core/NodeLinks.cs: Admit(query, authScheme, cred, viewNeeded) → screen | claim | node; /ws?node=<id>&t=<kind>[&cid=] with a node key or the operator's credential is a node link and hears {type:'auth', ok, node:{t,id,verified}, words} before any frame; a refused key gets the refusal line on every socket that sent one; CredentialsChanged re-checks open sockets on key and PIN changes. Screens.Match skips claims and nodes for 'all' and an empty target; new src/Timer.Remote/ScreenDirector.cs holds the rules moved from MainViewModel.Screens.cs with the fixed matching order ('all'/empty; screen id; operator-given name; then the refusal for a proven node). ListNodes/Push filters keep nodes out of screen pushes. node= with win or Authorization: Window is a 400 before the upgrade (window grants are SW22.4's). Node links grouped by id give two Patterns desks or two AGMessages two rows (G32). Today 'screen identify all' reaches PlayMap and 'disconnect all' drops every peer link (G06). Tests: NodeTests (the admission matrix), WebV2Tests (verdict first, claim flagged, 'all' skips nodes, refused-key line, CredentialsChanged).

- **Sources:** peers-not-screens SW-3
- **Gaps:** G06, G32, G43
- **Depends on:** SW18.1, SW18.2
- **Validate:** `cd /home/user/Stagewatch && tools/test.sh build unit`

### SW18.4 — Stagewatch — The desk, the context and the AI name show apps: link line, shape mark, right-click; Screens says who is not a screen

peers-not-screens SW-4. Timer.Show ShowContext peers rows append link and verified (§6); AiContext PeerFact gains Link/Verified and ScreenFact Claim; MainViewModel.ShowBus PeerRow gains LinkLine and a shape mark beside its colour (✓ node key, △ operator credential, ○ unverified); each Show network row's right-click offers Copy node id, Let a key speak for this node…, Revoke its key… (confirmed), in step with the buttons; the Screens subtitle adds '· N show apps follow this Stagewatch too (Producer → Show network); screen commands never reach them' and a claim row reads 'claims to be Patterns 9f8e… (unproven)'; the diagnostics web line counts browsers, window links, show-app links and claims apart. companion-module screen-action labels say 'show apps and unproven claims are never included'. The WPF pieces are the maintainer's PC work (UiSmoke), named as such.

- **Sources:** peers-not-screens SW-4
- **Gaps:** G06
- **Depends on:** SW18.3
- **Validate:** `cd /home/user/Stagewatch && tools/test.sh build unit module; on the maintainer's PC: pwsh tools\smoke.ps1`

### SW18.5 — Stagewatch — Bindings persisted; 'Let it speak for…' in Settings and the Show network, journalled

peers-not-screens SW-5. Bound is posted to the show thread, which persists Node/NodeKind and journals first use or the operator's rebind naming the previous id (key name and ids only, never a secret); new RebindApiKey(keyId, kind, id); Settings key rows say 'speaks for <kind> <id>', 'no node yet' or 'refused for … N s ago'; Settings buttons and right-click in step with the Show network menu.

- **Sources:** peers-not-screens SW-5
- **Gaps:** G15
- **Depends on:** SW18.4
- **Validate:** `cd /home/user/Stagewatch && tools/test.sh build unit; on the maintainer's PC: pwsh tools\smoke.ps1`

### SW18.6 — Stagewatch — The rig as a test peer: one control reader, --node-keys and stall <ms>; nodes.e2e.cjs and stall.e2e.cjs

peers-not-screens SW-6 plus patterns-playmap-side SW-ST, sharing one stdin control reader that SW19.8 later extends. tools/Stagewatch.Rig/Program.cs: --node-keys N (keys made with ApiKeys.Create on first start, hashes, ids and bindings in --state, this run's secrets in the ready line beside the PIN); Nodes, PeerContexts and AcceptPeerContext(cred); screen commands through ScreenDirector; the control command `stall <ms>` blocks the rig's owner thread so the real stuck beat goes out ({type:'beat',live:false} after StuckAfter 3 s: src/Timer.Remote/WebServer.cs:1265-1271, src/Timer.Core/ShowState.cs:217). tests/e2e/nodes.e2e.cjs (verdict first, 'all' skips nodes, claims, a forged context 403, a shared key) and tests/e2e/stall.e2e.cjs pinning the exact stuck-beat bytes Patterns 89.1's FakeStagewatch replays.

- **Sources:** peers-not-screens SW-6; patterns-playmap-side SW-ST
- **Gaps:** G04, G06, G15, G43
- **Depends on:** SW18.3
- **Validate:** `cd /home/user/Stagewatch && tools/test.sh build unit e2e`

### SW18.7 — Stagewatch — PatternsLink sends no token in clear: plain AUTH only to loopback, a proof only for a 32-hex NONCE, nothing taken before OK paired

The unilateral part of the PeerLink profile's 13.6.8 for today's link, ahead of PlayoutLink (SW20.4 replaces this class and keeps these rules). src/Timer.Integrations/PatternsLink.cs today sends plain `AUTH <token>` to any host when no NONCE came within 1.5 s (:280-284, G08), proves any nonce of 1–128 characters (:311) and takes STATE before pairing (:357-366). Change: the fallback AUTH only when the socket's remote address is loopback (a local-address predicate seam, never a host name), else the link says 'this desk asks for no proof — update Patterns; the token stays on this PC' (amber) and sends nothing secret; AUTH-PROOF only for a NONCE of exactly 32 lowercase hex; STATE, receipts and clock samples before `OK paired` are discarded and counted; an open desk (no NONCE, an OK) is shown amber 'asks for no key'. The token is never logged. Tests in tests/Stagewatch.Tests against the fake desk: a non-loopback desk with no NONCE never sees AUTH; a 31-hex and an upper-case nonce are not proved; a STATE line before OK paired moves nothing.

- **Sources:** PeerLink profile 13.6.8 / SW-PL0 (split); playout-links unit 4 (pairing rules)
- **Gaps:** G08
- **Depends on:** —
- **Validate:** `cd /home/user/Stagewatch && tools/test.sh build unit`

### SW18.8 — Stagewatch — Stagewatch 2.18 papers and release

peers-not-screens SW-7 and this train: REMOTE.md 2.18 'Show apps on /ws' (screens, claims and nodes; unknown keys not counted; revoking reaches open sockets; the PatternsLink token rule); NODES.md the /ws line per app; COMPANION.md the screen actions; FEATURES Show network; WHATS_NEXT 2.18 with what it did not do (no PeerLink yet; the Show Bus words wait for 1.3 in 2.19; the WPF walk on the PC); README; Version 2.18.0 in src/Timer.App/Timer.App.csproj. No SHOWBUS.md edit: the §6/§7/§7.1/§11 words go to SW19.1.

- **Sources:** peers-not-screens SW-7
- **Gaps:** G06, G15
- **Depends on:** SW18.5, SW18.6, SW18.7
- **Validate:** `cd /home/user/Stagewatch && tools/test.sh; on the maintainer's PC: pwsh tools\smoke.ps1; pwsh tools\release.ps1 -NoBump`

### P89.1 — Patterns — Round 89.1: a stuck Stagewatch shows no time in every reader; its link stays live and amber

patterns-playmap-side 89.1 (its §4.1, decisions 2–4) with the PeerLink profile's lights (13.10.5, 13.16.1), so no later round reverses them (the Eye critique's first issue). Today ReceiveAsync handles only pong and state and every message refreshes the link (src/Patterns.App/Services/StagewatchLinkService.cs:515-538): the stuck beat is ignored. Change: `beat` handled; {type:'beat',live:false} sets a volatile _stuck written only by the receive loop; only the next state frame clears it (a plain beat or a pong never does); the context moves to its own field written only by the beat task, so a context fetch cannot restore a live frame; the Sight getter folds Frame.Live=false. Lights: the link stays live, △ amber 'Stagewatch Hall A says its show is stuck'; every counting value (time, schedule) shows no time with ✕; the app card is the worse of its context light and amber, never red for stuck alone. Every reader in this unit: stage and timer pages, STATE bus.stagewatch appends `stuck` at the row's end, the twin relay to timer nodes, the Eye's app node, the Nodes card, the brief, the module's stagewatch_stuck, one log line per transition. A Stagewatch stuck before its first frame is said without taking the desk's own timer. Tests: Core (the Sight fold), App ShowBusAppTests with FakeStagewatch sending the stuck-beat bytes pinned by Stagewatch's stall.e2e.cjs (SW18.6); a context fetch during stuck leaves no time; module node test.

- **Sources:** patterns-playmap-side 89.1; PeerLink profile 13.10.5, 13.16.1; Eye critique issue 1
- **Gaps:** G04
- **Depends on:** —
- **Validate:** `cd /home/user/Patterns && dotnet build Patterns.sln -c Debug -v q && dotnet test tests/Patterns.Core.Tests -c Debug --no-build --filter "FullyQualifiedName~ShowBus" && dotnet test tests/Patterns.App.Tests -c Debug --no-build --filter "FullyQualifiedName~ShowBus" && (cd integrations/companion-module-patterns && npm test)`

### P89.2 — Patterns — Round 89.2: held addresses, Core — a window's pass becomes kept://host:port/<random id>

patterns-playmap-side 89.2 and windows-for-peers PT-W1's Core half, one unit (the same work). Patterns.Core: WindowAddresses (the private rule: a /w/ path or an address the operator marks secret); kept ids of 60 random bits from RandomNumberGenerator (never System.Random); the per-machine book; the show-file rewrite on load with its journal row; TwinWord.Kept; support-bundle redaction; ShortName; the InputKeys/MediaLocator fence so no key or label carries the pass; Withheld and black for an unresolved page. Today WebRow publishes the full address in STATE (src/Patterns.App/Services/CommandRouter.cs:294,618; G09). Tests: PrivateAddressFenceTests' Core part, the migration, the random id, a canary over STATE, CONTEXT, Eye, journal and bundle.

- **Sources:** patterns-playmap-side 89.2; windows-for-peers PT-W1
- **Gaps:** G09
- **Depends on:** —
- **Validate:** `cd /home/user/Patterns && dotnet build Patterns.sln -c Debug -v q && dotnet test tests/Patterns.Core.Tests -c Debug --no-build --filter "FullyQualifiedName~PrivateAddress|FullyQualifiedName~WindowAddresses"`

### P89.3 — Patterns — Round 89.3: held addresses, App — every entry point, the resolver, logs, STATE, the twin

patterns-playmap-side 89.3 and PT-W1's App half: the kept store in BaseDirectory on Secrets; Secure at every entry (wire, OSC, menus, Library, native player, load migration); the WebView2 resolver; CurrentUrl and logs scrubbed (approved lines too); STATE's public web.url; kept passes cross the twin only under SendSecrets (P92.4 turns that off by default); the WebView2 probe (OPEN L26); the shell observer drawing black unless assigned; the raw-address Super Check row; the full PrivateAddressFenceTests (STATE, CONTEXT, Eye, journal, log, bundle, module lines, InputLabels, approved lines, the twin wire). A platform path (WebView2; the twin's takeover): QUALIFICATION §22 walks and a new §28, unwalked until filled.

- **Sources:** patterns-playmap-side 89.3; windows-for-peers PT-W1
- **Gaps:** G09
- **Depends on:** P89.2
- **Validate:** `cd /home/user/Patterns && dotnet build Patterns.sln -c Debug -v q && for s in Core Rendering Devices Audio Assistant Audience App; do dotnet test tests/Patterns.$s.Tests -c Debug --no-build || exit 1; done && (cd integrations/companion-module-patterns && npm test)`

### P89.4 — Patterns — Round 89.4: no key or token in clear — the Stagewatch link by pin and probe, the module by proof

The profile's unilateral rules (13.3.2, 13.4.2, 13.6.8) for Patterns' two connectors. (a) StagewatchLinkService picks TLS from the hello (StagewatchLinkService.cs:241-242), a typed address only with a pin held (:235), and otherwise sends the bearer key on plain ws/http (:269): now TLS by the pin and a probe, never by the hello; a pinned id is dialled TLS whatever a hello says (a contradicting hello is a note on the link, never a refusal); with no TLS on offer off this machine the link reads the public view keyless and says 'Hall A offers no TLS — the key stays on this desk'; the key goes plain only to a loopback socket address; side calls follow the same rule. First-use pinning stays as Round 88 left it until P92.3's TRUST. (b) The Companion module sends plain `AUTH <token>` to any host when no NONCE came (integrations/companion-module-patterns/src/main.js:196-204): now only to a loopback desk, else 'the desk asks for no proof — update Patterns; the token stays here'; AUTH-PROOF only for a 32-hex NONCE. Tests: App test with FakeStagewatch behind the link's local-address seam — no Authorization header captured, the keyless view and its words; a pinned id with a hello lacking tls still dials TLS; module node tests for the nonce shape and the loopback rule. The module takes its next patch version.

- **Sources:** PeerLink profile 13.3.2, 13.4.2, 13.6.8 (Patterns' unilateral part of its Round 90 unit)
- **Gaps:** G08
- **Depends on:** —
- **Validate:** `cd /home/user/Patterns && dotnet build Patterns.sln -c Debug -v q && dotnet test tests/Patterns.App.Tests -c Debug --no-build --filter "FullyQualifiedName~StagewatchLink|FullyQualifiedName~ShowBus" && (cd integrations/companion-module-patterns && npm test)`

### P89.5 — Patterns — Round 89 papers (PLAN §107) and the tag row for round 88

PLAN §107: 'what the files said: none this round' (docs/field holds only README.md); the requester and evidence class (the maintainer's ask, answered from a handed-in critique: the cross-repo gap analysis and its reviews); design, proof and what the round did not do (no PeerLink handshake; the node link waits for Stagewatch 2.18 and lands in Round 91). REVIEW round 89 with 'Walk: unwalked' (WebView2 resolution and the twin's takeover: QUALIFICATION §22 rows and a new §28). CHANGELOG '## Round 89 — <date> — …' naming the evidence class (ChangelogTests). README (kept pages, the Nodes card's stuck words); REMOTE (bus.stagewatch stuck; web.url's public form); COMPANION (stagewatch_stuck; the module's AUTH rule and version). OPEN: L57/L58 added and closed, L26 closed, L36/L40 amended, L59 (the unpaired STATE greeting) and L60 (a standby without kept passes) added. .github/scripts/tag-rounds.sh gains `88 bcdbf2829cea302078acee7887841389e00d5178`. Closing sentence: the stall's two log lines and the migration's journal row, read beside the stage page and Super Check's raw-address row.

- **Sources:** patterns-playmap-side Round 89 papers; windows-for-peers PT-W5 (its 89 part)
- **Gaps:** G04, G09, G08
- **Depends on:** P89.1, P89.3, P89.4, owner:P-numbers
- **Validate:** `cd /home/user/Patterns && dotnet build Patterns.sln -c Debug -v q && for s in Core Rendering Devices Audio Assistant Audience App; do dotnet test tests/Patterns.$s.Tests -c Debug --no-build || exit 1; done && (cd integrations/companion-module-patterns && npm test)`

### PM-1 — Mapper — fix-showbus-identify: Stagewatch's identify is a journal row, never OUTPUT IDENTIFY ALL

peers-not-screens PM-1. NetworkHost.OnScreenCommand runs `OUTPUT IDENTIFY ALL 5` for any Stagewatch's identify (src/PlayMap.Engine/Host/NetworkHost.cs:783), flashing every projector on air (G06). New pure plan src/PlayMap.Net/Links/StagewatchScreenCommands.cs: identify → one journal row 'Stagewatch <name> asked to identify — PlayMap flashes no output for a screen command'; redirect, reload and disconnect keep today's words and hold. Ships alone; needs nothing from Stagewatch. A named round-2 fix batch (subject 'Engine: …', a docs/round2/CARRYOVER.md entry) on the owner's PM-X answer. Tests: PlayMap.Net.Tests (the plan); PlayMap.Engine.Tests (no OUTPUT IDENTIFY line reaches the control lane on an armed show).

- **Sources:** peers-not-screens PM-1
- **Gaps:** G06
- **Depends on:** owner:M-PMX
- **Validate:** `cd /home/user/Mapper && dotnet build PlayMap.sln -c Debug -v q && dotnet test tests/PlayMap.Net.Tests -c Debug --no-build --filter "FullyQualifiedName~StagewatchScreen" && dotnet test tests/PlayMap.Engine.Tests -c Debug --no-build --filter "FullyQualifiedName~NetworkHost"`

### PM-2 — Mapper — fix-showbus-keys: the Stagewatch key and the Patterns token never in clear; facts only after pairing

The safety subset of the profile's PM-PL0 (peerlink.md §6.3) and G31's TLS half. StagewatchLink uses TLS only when the hello names tls and https (src/PlayMap.Net/Links/StagewatchLink.cs:66), a typed address is always plain (src/PlayMap.Engine/Host/NetworkHost.cs:718-735), and the key rides as a bearer either way (:278-279). Now: TLS by the pin and a probe; a typed address tries TLS first (patterns-playmap-side D20); a pinned id is never dialled plain; with no TLS off this machine the link reads the public view keyless with words; the key never leaves on a plain socket off loopback. The key moves from plain playmap.node.json (src/PlayMap.Core/Bus/Pairing.cs:121-123) into NodeCredentialStore (src/PlayMap.Engine/Devices/NodeCredentialStore.cs:9-20) with a one-time migration. WireClient proves only a 32-hex NONCE (today any, WireClient.cs:314). PatternsLink takes no STATE and no blackout fact before `OK paired` (today before pairing, src/PlayMap.Net/Links/PatternsLink.cs:218-247: a forged answer at Patterns' address could black the air). Tests (PlayMap.Net.Tests on tests/Shared/ManualTime.cs): typed address TLS first; a pinned id with a hello lacking tls dials TLS; no Authorization on a plain non-loopback socket; nonce shapes; a pre-pairing STATE carrying a blackout is ignored. Fixtures unchanged.

- **Sources:** PeerLink profile PM-PL0 (safety subset); patterns-playmap-side PM-P1 / D20 (TLS half)
- **Gaps:** G08, G31
- **Depends on:** owner:M-PMX
- **Validate:** `cd /home/user/Mapper && dotnet build PlayMap.sln -c Debug -v q && dotnet test tests/PlayMap.Net.Tests -c Debug --no-build --filter "FullyQualifiedName~StagewatchLink|FullyQualifiedName~WireClient|FullyQualifiedName~PatternsLink|FullyQualifiedName~LinkTests" && dotnet test tests/PlayMap.Engine.Tests -c Debug --no-build --filter "FullyQualifiedName~NetworkHost|FullyQualifiedName~NodeCredential"`

### PM-3 — Mapper — fix-media-page-import: pages, embeds and window passes refused at import; kept passes scrubbed

patterns-playmap-side PM-F1+PM-F2, which is windows-for-peers PM-W1 (one unit). PlayMap cannot render a page yet imports one as 'added' and keeps a /w/ address in the project (G47). A private-address marker in VerbCatalog.Redacted wherever a line rides (journal, log, STATE echo) — a block-local edit of the spine's file; MEDIA IMPORT of a page, an embed, a private address or an https stream refused with the reason ('pages need a browser source — not built; use NDI from a browser'); kept passes in an existing project scrubbed on load with a journal row; LIST, readiness and FETCH stop calling Web items ready. src/PlayMap.Engine/EngineCore.Content.cs is desk-map's in W3 and canvas's in W4 (round3/PLAN.md §4): this batch lands before W3 starts, or waits for the integration pass.

- **Sources:** patterns-playmap-side PM-F1, PM-F2; windows-for-peers PM-W1
- **Gaps:** G47
- **Depends on:** owner:M-PMX, owner:M-content-before-W3
- **Validate:** `cd /home/user/Mapper && dotnet build PlayMap.sln -c Debug -v q && dotnet test tests/PlayMap.Engine.Tests -c Debug --no-build --filter "FullyQualifiedName~Media|FullyQualifiedName~Content" && dotnet test tests/PlayMap.Core.Tests -c Debug --no-build --filter "FullyQualifiedName~Redact|FullyQualifiedName~VerbCatalog"`

## Train 2 — One contract, one kit, one bench (Show Bus 1.3 final in Stagewatch 2.19; Patterns Rounds 90–91; PlayMap's 1.3 batches)

**Goal.** Show Bus 1.3 — with §12 Windows, §13 PeerLink, §14 numbers and the Eye's publishing rules — is final in Stagewatch's master and byte-identical in Patterns and PlayMap; the conformance kit (vectors inside the hashed body, one scripted peer) is in each tree; the hello and certificates follow 1.3; Patterns and PlayMap dial Stagewatch as nodes and read its verdict; Patterns shows its state only to a paired connection; the interop lane runs four real apps on a one-kernel LAN and from here on is part of every train's validation. No PeerLink behaviour yet (the profile's Phase 0). Validated by ShowBusSpecTests and tools/showbus-copies.sh agreeing across the trees, the PeerLinkVectors group, and `tools/test.sh interop` with the first rows.

### SW19.1 — Stagewatch — Show Bus 1.3 master draft: one edit carrying every design's words

spec-1-3 S1 (spec-1-3.md §2) as the single master edit of docs/SHOWBUS.md, carrying: windows-for-peers §1.5's two carriers and §12.4 onward (wording per D0); the PeerLink profile's §13 (peerlink.md §4, the ```peerlink block inside the hashed body) and §14 (spec-1-3's numbers block renumbered §13→§14 — the 23 references in the unlanded designs follow — with the profile's rows appended) and its 'words handed to the rest of the master' (§1 rule 3, §3's receipt fields final/repeat/lateMs/frame and the outcome table, §4, §5, §7, §7.2 LINK, §8 rows naming each node's scope map, show writer and stuckMs or 'stuck not offered: no measurement yet', the 13.18 doors, §10); the Eye's §6 rules (eye.md §5: P1–P8, @<id> alone, CONTEXT POST on line carriers within link.command.bytes, the informative kind table) amended by the Eye critique — P2 is one row per link with K the connector's primary use and the scope in the item's words, every acceptor publishes accepted links as P2 items, P1 names the twin's desk item a 13.18 exemption — and 13.16.5 gains the acceptor row's light (✓ verified, △ legacy/plain/keyless, never red); cross-app-proposals' §6/§8/§11 words, playout-links' §2.6, agmessage's §11 shape (top-level ok/words; SW-AG-1), peers-not-screens' §6/§7/§7.1/§11, the ecosystem lane's §6/§8. §8 gains the playmap kind and row (G38) and each node's exemptions with what ends them (G45). New ShowBusSpecTests: the draft's hash, ```showbus and ```peerlink parse, kinds; ShowBusKinds. No behaviour change.

- **Sources:** spec-1-3 S1; PeerLink profile SW-PL4 (text); Eye SB-EYE; cross-app-proposals SW-7 (words); windows-for-peers SW-W7 (words); playout-links unit 11 (words); peers-not-screens SW-7 (words); ecosystem-lane SW-P (words); agmessage SW-AG-1
- **Gaps:** G22, G38, G40, G42, G45, G17
- **Depends on:** SW18.8, owner:S-sections
- **Validate:** `cd /home/user/Stagewatch && tools/test.sh build unit`

### SW19.2 — Stagewatch — The PeerLink kit: the scripted peer, Stagewatch's pure core and the vectors group

PeerLink profile SW-PL1 and SW-PL4's kit (peerlink.md §6.1, §7). tools/showbus/peerlink-peer.mjs: one dependency-free Node 22 file (node:net, node:tls, node:crypto, node:http, a minimal WebSocket framer) that plays either end on line, ws and side calls, honest or as an attacker, driven by NDJSON on stdin/stdout (listen/dial, send, expect ≤ 2 s, silence, close), certificates and keys from 0600 files, never argv; scenarios S1–S22 as peerlink.md §7 lists. src/Timer.Core/PeerLink.cs, pure on injected time and j: canon (13.5.1), K, kid, the transcript T, client and server proofs, lid, S, enrolK; liveness 2/6/12 s inclusive and the self-gap rule; clock admission, errMs, fresh/fit/apart; redial with ±20 % jitter and the stable reset; the 13.12.3 answer→outcome table; the state machine with holds, floors, the closed state set and flags (13.16). A PeerLinkVectors group in tests/Stagewatch.Tests reads the ```peerlink block from docs/SHOWBUS.md and runs every crypto, parse, pure and machine row. Node 22 is already in ci.yml:36-38.

- **Sources:** PeerLink profile SW-PL1, SW-PL4 (kit)
- **Gaps:** G44
- **Depends on:** SW19.1
- **Validate:** `cd /home/user/Stagewatch && tools/test.sh build unit && node --check tools/showbus/peerlink-peer.mjs`

### SW19.3 — Stagewatch — Hello 1.3: built from the snapshot, silent on loopback, every address per id, sticky dial, conflicts amber

spec-1-3 S2 (its §3): ShowBusHello from the snapshot; no hello when the web server is bound to loopback; per-target sends with Status; ports list only doors receivers can open; proto 3, inst, the windows cap; the reader keeps 32 caps and 64-character names, every address per id, dials the same address while it is heard, shows one id from two running copies amber and never merges them; the Show network line, bus:hello and conflict rows; rig --bus/--bus-to; tests/e2e/bus.e2e.cjs.

- **Sources:** spec-1-3 S2
- **Gaps:** G27, G40
- **Depends on:** SW19.1
- **Validate:** `cd /home/user/Stagewatch && tools/test.sh build unit e2e`

### SW19.4 — Stagewatch — Stagewatch's own context within §6: at most 200 items, the byte-trimming order, context:trimmed

spec-1-3 S3: Timer.Show ShowContext stays within §14's context numbers (64 KiB, 200 items, 400 links); when over, trims in the fixed order and publishes a context:trimmed item saying how many. Tests in ShowBusTests.

- **Sources:** spec-1-3 S3
- **Gaps:** G40
- **Depends on:** SW19.1
- **Validate:** `cd /home/user/Stagewatch && tools/test.sh build unit`

### SW19.5 — Stagewatch — Certificates 825 days in all; an over-long one replaced once

spec-1-3 S4: CertificateLife and NodeCertificate count the whole span (825 days; Stagewatch's is 826 today); a certificate already issued longer is replaced once, so pinned peers see CERTIFICATE CHANGED once (said in the papers).

- **Sources:** spec-1-3 S4
- **Gaps:** G39
- **Depends on:** SW19.1
- **Validate:** `cd /home/user/Stagewatch && tools/test.sh build unit`

### SW19.6 — Stagewatch — View passes counted apart from PINs and keys

spec-1-3 S5 (§12.2): WebServer counts wrong view passes in their own table; a correct credential never clears them; REMOTE says which count each wrong attempt feeds.

- **Sources:** spec-1-3 S5
- **Gaps:** G22
- **Depends on:** SW19.1
- **Validate:** `cd /home/user/Stagewatch && tools/test.sh build unit`

### SW19.7 — Stagewatch — LB-1: the same-machine carrier probe (the Windows run is the maintainer's)

spec-1-3 S6: tools/BusCarrierProbe measuring 127.0.0.1, 127.255.255.255 and loopback multicast on the shared 9928 port, with its WHATS_NEXT row; the Linux run recorded, the Windows run named as the PC's work and reported not run.

- **Sources:** spec-1-3 S6
- **Gaps:** G27
- **Depends on:** —
- **Validate:** `cd /home/user/Stagewatch && tools/test.sh build && dotnet run --project tools/BusCarrierProbe -c Debug --no-build`

### SW19.8 — Stagewatch — The rig for the interop lane: a stable node id, a journal and report, secrets off stdout, the lockout table readable

ecosystem-lane SW-L1 (its §2.1–2.2): a stable node id, a journal and its report, secrets off stdout (--pin-file, --secrets-out), --exit-on-eof, the `auth` control command on SW18.6's reader and a public read WebServer.AuthFailures(), tests/e2e/rig-faults.e2e.cjs and a WebV2 test.

- **Sources:** ecosystem-lane SW-L1
- **Gaps:** G44
- **Depends on:** SW18.6
- **Validate:** `cd /home/user/Stagewatch && tools/test.sh build unit e2e`

### P90.1 — Patterns — Round 90.1: the bench host — the real desk headless off Windows for Stagewatch's interop lane

ecosystem-lane P-B1 (§2.3, §4 Patterns): HeadlessDesk Create/Show, TestApp, tools/Patterns.Bench (fenced like src/, its name not ending in .Tests), BenchClip (synthetic clips), BenchControl, added to Patterns.sln; BenchHostTests. No libVLC on Linux, no WebView2, no change to the exe's start; the lifecycle lane's census unchanged.

- **Sources:** ecosystem-lane P-B1
- **Gaps:** G44
- **Depends on:** P89.5
- **Validate:** `cd /home/user/Patterns && dotnet build Patterns.sln -c Debug -v q && dotnet test tests/Patterns.App.Tests -c Debug --no-build --filter "FullyQualifiedName~BenchHost|FullyQualifiedName~LifecycleTests"`

### SW19.9 — Stagewatch — The interop lane: four real apps on a one-kernel LAN; the first rows, the stuck rig and the laptop topology

ecosystem-lane SW-L2, SW-L3 and SW-L5 (its §2.6–2.8, §3): tools/interop (lan.py, a stdlib rtnetlink helper with namespaces on one bridge and a PID namespace; siblings.json pins; clones the lane owns), tests/interop, the sweep and matrix.json/md, a `tools/test.sh interop` lane and setup-dev.sh. Rows S0.1, S1.1–S1.3, S4.5, S6.1, S6.1b, S6.4, S6.7, S7.1, S7.3 (first three), S6.2a (the stuck rig through `stall`) and S1c.1/S1c.2 (the show laptop), each added only after it passes on the bench; 'not run' is never counted as passed. Siblings pinned at Patterns P90.1, PlayMap main and AGMessage's 0.5.0 branch head.

- **Sources:** ecosystem-lane SW-L2; ecosystem-lane SW-L3; ecosystem-lane SW-L5
- **Gaps:** G44
- **Depends on:** SW19.8, P90.1
- **Validate:** `cd /home/user/Stagewatch && tools/test.sh build unit interop`

### SW19.10 — Stagewatch — CI: the interop job at the siblings' pins, the nightly heads run, releases wait for it

ecosystem-lane SW-L4 (§2.12): a hardened, fork-guarded interop job in .github/workflows/ci.yml at siblings.json's pins that the release job needs; a nightly workflow against the siblings' heads that reports drift only.

- **Sources:** ecosystem-lane SW-L4
- **Gaps:** G44
- **Depends on:** SW19.9, owner:S-interop-gate
- **Validate:** `cd /home/user/Stagewatch && tools/test.sh interop && git push && gh run watch --exit-status $(gh run list --workflow ci.yml --branch $(git branch --show-current) --limit 1 --json databaseId -q '.[0].databaseId')`

### SW19.11 — Stagewatch — Stagewatch 2.19 papers: NODES.md from the code, ECOSYSTEM, REMOTE, the lane

spec-1-3 S7 and the lane's papers from ecosystem-lane SW-P: NODES.md rewritten from the code and held by a test against the hello builder (G41); ECOSYSTEM.md names PlayMap and 1.3, the manifest marked superseded; REMOTE 2.19 (hello, certificates, view passes, the rig's new flags); FEATURES; WHATS_NEXT (what 2.19 did not do: no PeerLink behaviour; LB-1's Windows run); the agent brief's loop line gains `tools/test.sh interop`. Version 2.19.0.

- **Sources:** spec-1-3 S7; ecosystem-lane SW-P (lane part)
- **Gaps:** G41, G38, G44
- **Depends on:** SW19.3, SW19.4, SW19.5, SW19.6, SW19.7, SW19.9
- **Validate:** `cd /home/user/Stagewatch && tools/test.sh build unit`

### SW19.12 — Stagewatch — Show Bus 1.3 final: the hash and the peer's hash pinned; tools/showbus-copies.sh

spec-1-3 S8: the header says final; BodySha256 pinned; §14's peerlink.peer.sha256 pinned to SW19.2's file; the other units' reader rows asserted; tools/showbus-copies.sh compares the body hash and the scripted peer's hash in all four trees and names each stale copy (G44's stale-copy risk). Release 2.19 with the papers.

- **Sources:** spec-1-3 S8; PeerLink profile SW-PL4 (hash)
- **Gaps:** G38, G39, G40, G41, G42
- **Depends on:** SW19.2, SW19.11, owner:S-D0, owner:S-exemptions
- **Validate:** `cd /home/user/Stagewatch && tools/test.sh && bash tools/showbus-copies.sh; on the maintainer's PC: pwsh tools\release.ps1 -NoBump`

### P90.2 — Patterns — Round 90 papers (PLAN §108)

ecosystem-lane P-B2: PLAN §108 opening with the requester and evidence class (the maintainer's list, quoting the ask), what the round did not do (no libVLC on Linux, no WebView2, no change to the exe's start); REVIEW 'Walk: none asked'; CHANGELOG; README names the bench; OPEN L40, L47, L50, L56 annotated, one row for synthetic clips; tag row 89. Closing sentence: no verb an output can show.

- **Sources:** ecosystem-lane P-B2
- **Gaps:** G44
- **Depends on:** P90.1, owner:P-numbers
- **Validate:** `cd /home/user/Patterns && dotnet build Patterns.sln -c Debug -v q && for s in Core Rendering Devices Audio Assistant Audience App; do dotnet test tests/Patterns.$s.Tests -c Debug --no-build || exit 1; done && (cd integrations/companion-module-patterns && npm test)`

### P91.1 — Patterns — Round 91.1: the Stagewatch link dials as a node and reads its verdict

peers-not-screens PT-1 and PT-2, one unit. StagewatchFrame.SocketQuery(selfId) gives '/ws?node=<ShowBusId>&t=patterns&cid=<id>&page=/patterns' (today a fixed cid=patterns, StagewatchLinkService.cs:276; G32); StagewatchFrame.Auth parses the verdict; a refusal (401, or auth ok:false) is recorded in _keyHeldUntil before any further I/O; keyed calls skip while it holds and every redial waits for it; 403 words shown verbatim; a refused collect said; an unwritable Show Bus id said. Every reader: STATE bus.stagewatch appends `key` (key|operator|none|refused|empty) after `stuck`; the Eye's, Nodes card's, context's and assistant's words; the module's stagewatch_key. Against an older Stagewatch with no verdict, HTTP evidence decides (peers-not-screens 'mixed versions'). Tests: Core (SocketQuery, Auth, Hold); App ShowBus12AppTests (the asked URL; the verdict reaching STATE key and the card words; the hold proven by counting keyed requests; 403 words; a refused collect).

- **Sources:** peers-not-screens PT-1; peers-not-screens PT-2; patterns-playmap-side 90.1
- **Gaps:** G32, G43
- **Depends on:** SW18.3, P90.2
- **Validate:** `cd /home/user/Patterns && dotnet build Patterns.sln -c Debug -v q && dotnet test tests/Patterns.Core.Tests -c Debug --no-build --filter "FullyQualifiedName~ShowBus" && dotnet test tests/Patterns.App.Tests -c Debug --no-build --filter "FullyQualifiedName~ShowBus" && (cd integrations/companion-module-patterns && npm test)`

### P91.2 — Patterns — Round 91.2: the wire shows its state only to a paired connection (ADR-016)

spec-1-3 PT-90.2 (spec-1-3.md §4 Patterns; decision 7). 9697 greets every connection with STATE before NONCE (src/Patterns.App/Services/ControlService.cs:356-357) and IsQuery lets every non-Action verb through unpaired (src/Patterns.Core/Services/ControlProtocol.cs:1454), EYE and CONTEXT included, while HTTP fences /api/state and /api/context (G45; G09's remainder). ADR-016 first, with the maintainer's scope. NONCE first; no STATE greeting or push before pairing; OpenBeforePairing(cmd) beside IsQuery: HELLO, NONCE, AUTH, AUTH-PROOF, PING, HELP, VERBS and, under scope A, NAV, MENU, CUE LIST, ARCADE …, ASSISTANT … (named in §8 until the desk's own cross-machine asks pair by proof); STATE, EYE, CONTEXT and SUBSCRIBE refused unpaired with words — which keeps the Eye's later link records and other apps' contexts off unpaired readers (the Eye critique's unpaired-topology issue); WirePeer re-checks pairing per push; the module pairs before it reads; the test helpers pair first; Super Check's Remote row; one log line per refused unpaired read. Every loopback test sets ControlService.TrustLoopback = false and restores it in finally. Compatible with Stagewatch after SW18.7 and PlayMap after PM-2 (both read STATE only after OK paired; older ones pair first and then read).

- **Sources:** spec-1-3 PT-90.2; patterns-playmap-side 90.2; Eye critique (unpaired readers)
- **Gaps:** G45, G09
- **Depends on:** SW18.7, owner:P-ADR016
- **Validate:** `cd /home/user/Patterns && dotnet build Patterns.sln -c Debug -v q && for s in Core Rendering Devices Audio Assistant Audience App; do dotnet test tests/Patterns.$s.Tests -c Debug --no-build || exit 1; done && (cd integrations/companion-module-patterns && npm test)`

### P91.3 — Patterns — Round 91.3: the context lists what peers may propose; APPLY at review; the reader keeps 256 commands

cross-app-proposals PT-90.3 and patterns-playmap-side 90.3 (cross-app-proposals.md §2.8): Patterns' context commands gain APPLY <id> at review and 15 rows appended in the design's order (VIDEO STOP high, the staged and per-screen take verbs, the web actions except WEB OPEN and WEB TYPE); APPLY runs only this desk's own proposal (fenced); the reader of other apps' contexts keeps 256 commands with verbs and words to 512 characters (today .Take(64), src/Patterns.Core/Services/ShowContext.cs:99 — the owner of PlayMap asked for at least 256, round3/PLAN.md §9 decision 18); the brief lists 32 (windows-for-peers' 12→32, folded here). Tests: the rows appended (raw substring), the APPLY fence, PlayMap's busy golden context read to 256.

- **Sources:** cross-app-proposals PT-90.3; patterns-playmap-side 90.3; windows-for-peers PT-W3 (brief cap)
- **Gaps:** G03, G33, G35
- **Depends on:** —
- **Validate:** `cd /home/user/Patterns && dotnet build Patterns.sln -c Debug -v q && dotnet test tests/Patterns.Core.Tests -c Debug --no-build --filter "FullyQualifiedName~ShowContext|FullyQualifiedName~ShowBus" && dotnet test tests/Patterns.App.Tests -c Debug --no-build --filter "FullyQualifiedName~Apply|FullyQualifiedName~Proposal"`

### P91.4 — Patterns — Round 91.4: certificates 825 days; the hello 1.3; the hello table bounded at 128

spec-1-3 PT-90.5 plus the Eye critique's hello-table issue: TlsIdentity.Life 825 days in all (1,826 today, G39) with the one renewal; hello proto 3 and inst; every address per id; sticky dial; one id from two running copies amber, never merged. ShowBusPeers.Hear stores every hello with no cap (src/Patterns.Core/Services/ShowBusNetwork.cs:111): cap 128, evicting the oldest app that is not needed, counting what was dropped (Nodes page now; the Eye's 'left' in Round 98). Tests: the renewal; a Core flood (thousands of forged ids keep ≤ 128, needed peers kept); conflicts.

- **Sources:** spec-1-3 PT-90.5; Eye critique (unbounded hello table)
- **Gaps:** G39, G40
- **Depends on:** —
- **Validate:** `cd /home/user/Patterns && dotnet build Patterns.sln -c Debug -v q && dotnet test tests/Patterns.Core.Tests -c Debug --no-build --filter "FullyQualifiedName~ShowBus|FullyQualifiedName~TlsIdentity" && dotnet test tests/Patterns.App.Tests -c Debug --no-build --filter "FullyQualifiedName~ShowBus"`

### P91.5 — Patterns — Round 91 papers (PLAN §109) with the Show Bus 1.3 copy and the kit

PLAN §109 (requester; evidence: a handed-in critique and the contract docs/SHOWBUS.md 1.3; what the round did not do: no PeerLink handshake, decks keep the legacy proof); REVIEW 'Walk: none asked'; CHANGELOG; README ledger; REMOTE (reads after pairing, the node link, the context rows, hello 1.3); COMPANION (pairing order, stagewatch_key, the module version); docs/SHOWBUS.md replaced by the byte-identical 1.3 master and tools/showbus/peerlink-peer.mjs copied; ShowBusSpecTests extracting ```showbus and ```peerlink and checking the peer's SHA-256 against §14; ADR-016; OPEN L59 closed, L47/L50/L55/L56 stay; QUALIFICATION §26 note; tag row 90. Closing sentence: the refused unpaired read, beside its log line and Super Check's Remote row.

- **Sources:** patterns-playmap-side 90.4; cross-app-proposals PT-90.4; spec-1-3 PT-90.4; peers-not-screens PT-3
- **Gaps:** G38, G40, G45
- **Depends on:** P91.1, P91.2, P91.3, P91.4, SW19.12, owner:P-numbers
- **Validate:** `cd /home/user/Patterns && dotnet build Patterns.sln -c Debug -v q && for s in Core Rendering Devices Audio Assistant Audience App; do dotnet test tests/Patterns.$s.Tests -c Debug --no-build || exit 1; done && (cd integrations/companion-module-patterns && npm test) && bash /home/user/Stagewatch/tools/showbus-copies.sh`

### PM-4 — Mapper — fix-showbus-1.3: the 1.3 copy and the kit, SHOWBUS-NODE corrected and answered, the hello reader's limits

spec-1-3 M1 with patterns-playmap-side PM-F5, peers-not-screens PM-3 and playout-links' Mapper doc rows (one batch; every docs/SHOWBUS-NODE.md edit of trains 1–3 in one place). docs/SHOWBUS.md (new here) and tools/showbus/peerlink-peer.mjs byte-identical with the master; ShowBusSpecTests in tests/PlayMap.Core.Tests/Bus (hash, ```showbus, ```peerlink, the peer's hash); the hello reader keeps 32 caps and 128 peers, proto 3, one id one peer with every address, conflicts; SHOWBUS-NODE.md: :37 'a node on Stagewatch's Show network, never a screen; the verdict read', the wrong claim that Patterns' device links pair by proof corrected (:40, :49-50, :72-73, G35), the Stagewatch rows and the trigger requirement (G30), 'do not paste a window address'; FLEET.md P1–P3; WIRE.md adds 'refused' to links.stagewatch.state; a CARRYOVER entry.

- **Sources:** spec-1-3 M1; patterns-playmap-side PM-F5; peers-not-screens PM-3; playout-links Mapper unit (doc rows)
- **Gaps:** G35, G38, G40, G30
- **Depends on:** SW19.12, owner:M-PMX
- **Validate:** `cd /home/user/Mapper && dotnet build PlayMap.sln -c Debug -v q && dotnet test tests/PlayMap.Core.Tests -c Debug --no-build --filter "FullyQualifiedName~ShowBus" && dotnet test tests/PlayMap.Net.Tests -c Debug --no-build --filter "FullyQualifiedName~Hello"`

### PM-5 — Mapper — fix-showbus-node-link: PlayMap dials Stagewatch as a node; 'paired' comes from the verdict

peers-not-screens PM-2: StagewatchLink dials 'node=<SelfId>&t=playmap&cid=<id>&page=/playmap&detail=1' (StagewatchLink.cs:283-284); a public Pairing (PairState) from the verdict or an authenticated HTTP 2xx, set before any I/O; paired = Paired or NotNeeded (G31's honest paired); a 'refused' state and a red preflight arm in PreflightBreadth.Row with Stagewatch's words and no fix line; holds recorded before I/O; 403 words; a refused collect said. The two lines in ContextBuilder.cs and Seams.cs' LinkView are spine files and go through the spine amendment lane (round3/PLAN.md §5.7). Fixtures unaffected (main.json's links.stagewatch is {state:off}). Tests: the verdict gives Pairing; keyless gives NotNeeded; ok:false gives Refused with one connection; a keyed 2xx gives Paired; 403 words; the preflight row.

- **Sources:** peers-not-screens PM-2
- **Gaps:** G06, G43, G31
- **Depends on:** SW18.3, PM-2, owner:M-PMX
- **Validate:** `cd /home/user/Mapper && dotnet build PlayMap.sln -c Debug -v q && dotnet test tests/PlayMap.Net.Tests -c Debug --no-build --filter "FullyQualifiedName~StagewatchLink|FullyQualifiedName~LinkTests" && dotnet test tests/PlayMap.Engine.Tests -c Debug --no-build --filter "FullyQualifiedName~Preflight"`

### SW19.13 — Stagewatch — Interop: keys from the rig, the rig on the bus, contexts both ways (Patterns and PlayMap rows)

ecosystem-lane SW-R1 for the Patterns and PlayMap halves: rows S1.4–S1.6 and S2.1–S2.7 that pass at the new pins (siblings.json bumped in the same commit). AGMessage's rows wait for SW-INT-AG.

- **Sources:** ecosystem-lane SW-R1
- **Gaps:** G44
- **Depends on:** SW19.9, P91.1, PM-5
- **Validate:** `cd /home/user/Stagewatch && tools/test.sh build interop`

## Train 3 — Every link honest from its own end (Stagewatch 2.20 playout links; Patterns Rounds 92–93; PlayMap PeerLink core batches)

**Goal.** Every connector keeps the profile's unilateral rules on today's carriers (level 0, the profile's Phase 1): matched pongs only as clock samples, five outcomes read by one table, a written command with no answer 'unconfirmed' never 'failed', request ids carrying the process's inst, redial with ±20 % jitter reset only after a verified link stayed up 10 s, persisted holds, link and show liveness apart, results retried, one journal row per command and per state change. Stagewatch runs a list of playout links to Patterns desks and PlayMap nodes with real receipts, running-order cues and timed GO on a fit clock; Patterns speaks for the operator through Stagewatch and holds playlist items. Validated by each tree's PeerLinkVectors and its connector's scripted-peer scenarios, and by the interop VT rows S4.1–S4.4.

### SW20.1 — Stagewatch — Playout model and grammar: settings v2, PlayoutCue, ShowCommandKind.Playout, JournalKind.Playout

playout-links unit 1 (playout-links.md §2.1, §2.4, §3): settings v2 with migration and the legacy mirror (one Patterns link becomes a list keyed by hello id and kind, G01), PlayoutCue and ShowFile.Sanitize, ShowCommandKind.Playout appended (parser verb, help line, Copilot.Classify High, ShowContext.Commands rows), JournalKind.Playout, ClockOffsetEstimator sample times, reserved names, licence words.

- **Sources:** playout-links unit 1
- **Gaps:** G01, G29
- **Depends on:** SW19.12
- **Validate:** `cd /home/user/Stagewatch && tools/test.sh build unit`

### SW20.2 — Stagewatch — Who may drive playout: PlayoutGate and the OSC/Companion/HTTP gates

playout-links unit 2: PlayoutGate, Remote.PlayoutFrom; `playout` and the existing `patterns` lines refused from another machine over OSC or Companion TCP (no PIN) unless the operator lists the address; an AI's line is always a proposal (routed to the target's desk when it collects approvals). The shared sender gate that SW21.4 extends.

- **Sources:** playout-links unit 2
- **Gaps:** G01
- **Depends on:** SW20.1
- **Validate:** `cd /home/user/Stagewatch && tools/test.sh build unit`

### SW20.3 — Stagewatch — Wire primitives: PlayoutWire (proof, quoting, a finder that hears playmap) and PlayoutStateReader

playout-links unit 3: PlayoutWire (the proof on Timer.Core/PeerLink.cs's legacy rows, quoting, ParseBeacon accepting t:'playmap' — today only 'patterns', src/Timer.Integrations/PatternsLink.cs:459, G01); PlayoutStateReader forward-only and prefix-capable, reading at least 256 KiB and an oversize STATE in part, shown amber with its size (G26, 13.14).

- **Sources:** playout-links unit 3
- **Gaps:** G01, G26
- **Depends on:** SW20.1, SW19.2
- **Validate:** `cd /home/user/Stagewatch && tools/test.sh build unit`

### SW20.4 — Stagewatch — PlayoutLink over TCP on the PeerLink core (replaces PatternsLink)

playout-links unit 4 with the profile's SW-PL0 (peerlink.md §6.1). SW18.7's pairing rules kept. Ids s<n>-<inst> unique per process (today s<n> per run, PatternsLink.cs:167); ≤ 32 in flight, the 33rd not-sent, nothing evicted (today evicted past 64, :169); answers read by the 13.12.3 table — a drop after a write is unconfirmed (today it fails every pending command, :225-226), a desk older than 1.1 unconfirmed; matched pongs only (today any OK PONG, :318-327); redial 500 ms→5 s × (0.8+0.4j), j from a CSPRNG-seeded generator, reset only after verified + 10 s (today 250 ms doubling, no jitter, reset on connect, :189,209,229-230); holds persisted with their end; a writer lane per link so the show thread never touches the socket (today it writes, :172-176, MainViewModel.Patterns.cs:69-85); a TimeProvider seam; one journal row per command when its outcome settles and per state change; kind profiles (patterns, playmap), local refusals (pausable/role, sync follower), WHOAMI/CONTEXT/INDEX/CUE LIST after pairing. PatternsLink's tests move. Scripted-peer line-connector scenarios at level 0: S1, S11, S13, S14, S19, S22.

- **Sources:** playout-links unit 4; PeerLink profile SW-PL0
- **Gaps:** G07, G08, G26, G48
- **Depends on:** SW20.3, SW18.7
- **Validate:** `cd /home/user/Stagewatch && tools/test.sh build unit`

### SW20.5 — Stagewatch — The link set, PlayoutDirector and receipts: Patterns' real outcome completes Stagewatch's receipt

playout-links unit 5: PlayoutLinks, PlayoutDirector (the executor, the journal on completion, STATE observation, next-on-end once per execution), PlayoutReceipts (the pending receipt keyed by the link's request id, completed applied or refused in the peer's words, G07), WS/Companion `final`, a link:<name> item and clock:<id> check per link, frame playout.link/node, config playoutLinks, AI facts, AI re-target via ProposalDesk, the licence gate; rig --playout; e2e. Refuses up front in Patterns' words until Patterns publishes video.pausable/stoppable (P93.2), which then decide (G48).

- **Sources:** playout-links unit 5
- **Gaps:** G01, G07, G48
- **Depends on:** SW20.4, SW20.2
- **Validate:** `cd /home/user/Stagewatch && tools/test.sh build unit e2e`

### SW20.6 — Stagewatch — PlayMap over pinned TLS; Trust a changed certificate by typing it

playout-links unit 6: WSS playmap.lines.v1 with the pin per node id, never plain once pinned, 429/4401 words; 'Trust new certificate' is a text-taking entry where the operator types ≥ 8 hex of the new fingerprint beside its short form (13.8.3; never pre-filled, never bound to a pad) — a ShowCommandKind appended with its parser verb, help line and Copilot.Classify High, never in ShowContext.Commands.

- **Sources:** playout-links unit 6; PeerLink profile 13.8.3; Eye critique issue 4
- **Gaps:** G08
- **Depends on:** SW20.5
- **Validate:** `cd /home/user/Stagewatch && tools/test.sh build unit`

### SW20.7 — Stagewatch — Running-order cues and timed GO on a fit clock

playout-links unit 7: the planner, opt-in standby (off by default — a PlayMap standby puts the item's first frame on air), hold-until for untimed members (T − rtt/2), one GO per sync group, 'scheduled for' and 'started late' rows; AT is used only when the link's clock is fit for the verb's tolerance (13.11.4–6: fresh ≤ 10 s, errMs ≤ tolerance) and was measured over TLS or a local link — PlayMap over pinned WSS can time a GO, Patterns over plain 9697 cannot until Round 97, and the row says so; rig --cue; e2e.

- **Sources:** playout-links unit 7; PeerLink profile 13.11.6
- **Gaps:** G28, G29
- **Depends on:** SW20.5
- **Validate:** `cd /home/user/Stagewatch && tools/test.sh build unit e2e`

### SW20.8 — Stagewatch — HTTP triggers authenticate: secrets only to loopback or a pinned HTTPS server

playout-links unit 8: per-trigger auth profiles (DPAPI at rest, never journalled), loopback-or-pinned-https only, no redirects, a fixed authority no running-order text can change, X-Request-Id, the reply's ok and stage read into the trigger status by 13.12.3, a query-secret warning.

- **Sources:** playout-links unit 8
- **Gaps:** G30
- **Depends on:** SW20.1
- **Validate:** `cd /home/user/Stagewatch && tools/test.sh build unit`

### SW20.9 — Stagewatch — 413 for a request body over 64 KiB, drained so the sender reads it

patterns-playmap-side SW-25: WebServer answers 413 for bodies over 64 KiB after draining them (deferred paths exempt); the path logs no body. The PeerContexts byte caps of SW21.1 rely on it.

- **Sources:** patterns-playmap-side SW-25
- **Gaps:** G25
- **Depends on:** —
- **Validate:** `cd /home/user/Stagewatch && tools/test.sh build unit`

### SW20.10 — Stagewatch — Rig message signals and message.e2e.cjs (the contract Patterns' speaker message is tested against)

patterns-playmap-side SW-RM: rig message signals, receipts with refs.messageId, frames with messageId/messageAck/messageAckBy, the ack path; tests/e2e/message.e2e.cjs with --node-keys.

- **Sources:** patterns-playmap-side SW-RM
- **Gaps:** G21
- **Depends on:** SW18.6
- **Validate:** `cd /home/user/Stagewatch && tools/test.sh build unit e2e`

### SW20.11 — Stagewatch — The desk for playout (PC work): Settings links, May drive playout, trigger auth, RundownEditor cues, VT chip and menus

playout-links unit 9: Settings playout links and 'May drive playout', trigger auth; RundownEditor cues with per-kind standby words; the VT chip and running-order right-click menus in step with buttons; Help. Built and walked on the maintainer's PC (UiSmoke), named as such.

- **Sources:** playout-links unit 9
- **Gaps:** G01, G29, G30
- **Depends on:** SW20.7, SW20.8, SW20.6
- **Validate:** `cd /home/user/Stagewatch && tools/test.sh build unit; on the maintainer's PC: pwsh tools\smoke.ps1`

### SW20.12 — Stagewatch — Companion module: playout actions and vt_link

playout-links unit 10: companion-module playout actions, the vt_link variable and feedback, tests.

- **Sources:** playout-links unit 10
- **Gaps:** G01, G29
- **Depends on:** SW20.5
- **Validate:** `cd /home/user/Stagewatch && tools/test.sh module`

### P92.1 — Patterns — Round 92.1: PeerLink core in Patterns.Core, the vectors, the token's canonical form, the clock's admission

The profile's Round 90 core unit (peerlink.md §6.2), renumbered. src/Patterns.Core/Services/PeerLink.cs (no package references): canon, K, kid, T, proofs, lid, S; liveness with the self-gap; clock quality; redial; the 13.12.3 table; the state machine; the LinkRecord type (13.16.3, never holding a key, code, S, proof or nonce). tests/Patterns.Core.Tests/PeerLinkVectorsTests.cs (xUnit theories over the ```peerlink block of docs/SHOWBUS.md). PairingToken.Normal strips five characters today (src/Patterns.Core/Services/PairingToken.cs:54-63) and the token is a free text box (Views/Sections/WebSection.axaml:38): canon by 13.5.1, refused at the Remote page and at NEW TOKEN unless [A-Z0-9]{1,64}. LinkClock clamps a negative rtt and Apart uses >= (LinkClock.cs:59-63,97): admission by 13.11.2, Apart as |offset| − err > 2 s. CI: the test job gains setup-node 22 (today only the module job has Node, .github/workflows/build.yml:76-78) and tests/Patterns.App.Tests/PeerLinkWireTests.cs gains the harness spawning tools/showbus/peerlink-peer.mjs.

- **Sources:** PeerLink profile Patterns 'Round 90' core unit
- **Gaps:** G08
- **Depends on:** P91.5
- **Validate:** `cd /home/user/Patterns && dotnet build Patterns.sln -c Debug -v q && dotnet test tests/Patterns.Core.Tests -c Debug --no-build --filter "FullyQualifiedName~PeerLink|FullyQualifiedName~PairingToken|FullyQualifiedName~LinkClock"`

### P92.2 — Patterns — Round 92.2: the Stagewatch link on the core, with its record in every reader

The profile's Round 90 link hygiene. StagewatchLinkService: the attempt count resets only after a link verified and up 10 s (today on any return, :184-185); 60 s holds persisted and recorded before I/O; a side-call lane with the beat and the drop check on their own timer (today the ping waits behind 4 s side calls, :324-355); matched pongs; results retried 3→30 s ±20 % for 30 min, recorded before they run, and a bounded desk-id LRU (today posted once and cleared at 1000, :460-465); a TimeProvider defaulting to ShowClock (:79-80); the legacy-bearer rule (13.6.8): first use sends the key only to a certificate already pinned or confirmed (P92.3's TRUST). Every reader: STATE bus.stagewatch appends `link` (the 13.16.3 record as one object) after `key`; the Eye's existing app node and its Follows edge carry the record's light, shape mark and words (the fold is Round 98); the Nodes card; the Run strip; Companion stagewatch_link_state and stagewatch_link_flags; the assistant's facts; a Super Check row 'Show network links'; one journal row and one log line per state change, never per beat. Scripted-peer ws-connector scenarios at level 0: S5, S6, S11, S12, S13, S14, S19.

- **Sources:** PeerLink profile Patterns 'Round 90' link unit
- **Gaps:** G16, G08
- **Depends on:** P92.1
- **Validate:** `cd /home/user/Patterns && dotnet build Patterns.sln -c Debug -v q && dotnet test tests/Patterns.App.Tests -c Debug --no-build --filter "FullyQualifiedName~PeerLinkWire|FullyQualifiedName~ShowBus|FullyQualifiedName~StagewatchLink" && (cd integrations/companion-module-patterns && npm test)`

### P92.3 — Patterns — Round 92.3: STAGEWATCH verbs — set, follow, trust and accept the link from the wire, the deck and the menus

patterns-playmap-side 91.1 (G34) with the profile's trust verbs and the Eye critique's fourth issue. Verbs: STAGEWATCH LINK <host[:port]>|AUTO|OFF, STAGEWATCH FOLLOW, STAGEWATCH STATUS, STAGEWATCH LINK AGAIN, STAGEWATCH TRUST (first use: one press beside the short fingerprint, on the desk and its menus only), STAGEWATCH ACCEPT <≥ 8 hex typed> (a changed certificate; text-taking, never pre-filled, not bindable to a deck or MIDI pad, DeskMenuTests.cs:178), STAGEWATCH FLOOR LOWER, STAGEWATCH RELEASE (a held fact, 13.17). KeyFor and its bind rules: the key is desk-typed only and goes only to the Stagewatch it is bound to, never to a target a wire line chose. The Nodes page's Accept today calls AcceptPendingCertificate directly (ViewModels/MainViewModel.Nodes.cs:46; StagewatchLinkService.cs:121-132): it goes through the same ShowAction with a journal row. Desk and right-click menus in step; module actions; REMOTE rows; catalogued, never proposed by the assistant. STATE appends `follow`, `pending`.

- **Sources:** patterns-playmap-side 91.1; PeerLink profile 13.16.6; Eye critique issue 4
- **Gaps:** G34
- **Depends on:** P92.2
- **Validate:** `cd /home/user/Patterns && dotnet build Patterns.sln -c Debug -v q && dotnet test tests/Patterns.Core.Tests -c Debug --no-build --filter "FullyQualifiedName~ControlProtocol|FullyQualifiedName~DeskMenu" && dotnet test tests/Patterns.App.Tests -c Debug --no-build --filter "FullyQualifiedName~DeskMenuAppTests|FullyQualifiedName~StagewatchLink" && (cd integrations/companion-module-patterns && npm test)`

### P92.4 — Patterns — Round 92.4: the twin honours the bind address and keeps device secrets home by default

The profile's Round 90 twin unit (13.18: the twin stays a named exemption). The twin listens on IPAddress.Any whatever the bind address (src/Patterns.App/Services/TwinService.cs:488,512): it binds as the wire does. SendSecrets defaults true (TwinConfig, src/Patterns.Core/Model/ShowState.cs:2080): default off, and refused on a plain twin link to another machine with words ('device passwords stay on this desk — type them on the standby'). A platform path (the twin's takeover): a QUALIFICATION §22 walk row, unwalked until filled. OPEN rows: L60's standby without kept passes amended; a new row for the takeover dropping accepted PeerLinks and posted contexts bound to the main's Show Bus id (the Eye critique's unmapped gap).

- **Sources:** PeerLink profile Patterns 'Round 90' twin unit
- **Gaps:** G09
- **Depends on:** owner:P-twin-secrets
- **Validate:** `cd /home/user/Patterns && dotnet build Patterns.sln -c Debug -v q && dotnet test tests/Patterns.App.Tests -c Debug --no-build --filter "FullyQualifiedName~Twin" && dotnet test tests/Patterns.Core.Tests -c Debug --no-build --filter "FullyQualifiedName~Twin"`

### P92.5 — Patterns — Round 92 papers (PLAN §110)

PLAN §110 (requester; evidence: a handed-in critique — the PeerLink profile; what the round did not do: no LINK handshake, no acceptor); REVIEW 'Walk: unwalked' (the twin); CHANGELOG; README; REMOTE (the STAGEWATCH verbs, bus.stagewatch link/follow/pending, the token's canonical form); COMPANION (variables and actions, module version); QUALIFICATION §22 row; OPEN; tag row 91. Closing sentence: STAGEWATCH LINK's journal rows and the Super Check's 'Show network links' row, read beside the Nodes card.

- **Sources:** PeerLink profile 6.2 process
- **Gaps:** G34
- **Depends on:** P92.2, P92.3, P92.4, owner:P-numbers
- **Validate:** `cd /home/user/Patterns && dotnet build Patterns.sln -c Debug -v q && for s in Core Rendering Devices Audio Assistant Audience App; do dotnet test tests/Patterns.$s.Tests -c Debug --no-build || exit 1; done && (cd integrations/companion-module-patterns && npm test)`

### P93.1 — Patterns — Round 93.1: one speaker message — through Stagewatch when the link can carry it, on this desk with words when not

patterns-playmap-side 91.2 (its §3.1, §4.3): while following with a node key, STAGE MESSAGE goes as POST /api/message/<text>/<urgency>/off[/ack] with X-Request-Id on P92.2's side-call lane; receipt stages and the speaker's ACK shown; latest wins on the page; speakerShown; the speaker lane; without the link it stays local and says so; STATE stage appends speakerFrom, speakerAck, speakerAckBy; the module. Tested against Stagewatch's message.e2e.cjs contract (SW20.10).

- **Sources:** patterns-playmap-side 91.2
- **Gaps:** G21
- **Depends on:** P92.2, SW20.10, P92.5
- **Validate:** `cd /home/user/Patterns && dotnet build Patterns.sln -c Debug -v q && dotnet test tests/Patterns.App.Tests -c Debug --no-build --filter "FullyQualifiedName~Speaker|FullyQualifiedName~StageMessage" && (cd integrations/companion-module-patterns && npm test)`

### P93.2 — Patterns — Round 93.2: hold a playlist item or a stinger's clip; video.pausable and video.stoppable

patterns-playmap-side 91.3 (OPEN L48, L49 in part): VIDEO PAUSE/STOP hold a playlist item's and a stinger's clip and its clock; a schedule entry held past its window is skipped and journalled, not played late; STATE video appends pausable, stoppable, which Stagewatch's PlayoutLink reads (G48).

- **Sources:** patterns-playmap-side 91.3
- **Gaps:** G48
- **Depends on:** P92.5
- **Validate:** `cd /home/user/Patterns && dotnet build Patterns.sln -c Debug -v q && dotnet test tests/Patterns.App.Tests -c Debug --no-build --filter "FullyQualifiedName~Video|FullyQualifiedName~Playlist" && dotnet test tests/Patterns.Core.Tests -c Debug --no-build --filter "FullyQualifiedName~Video"`

### SW20.13 — Stagewatch — Interop: the VT rows (S4.1–S4.4)

ecosystem-lane SW-R4: rows S4.1–S4.4 at bumped pins (PlayoutLink to the real Patterns bench and PlayMap engine; receipts; the first untested pairing proved).

- **Sources:** ecosystem-lane SW-R4
- **Gaps:** G44, G01, G07
- **Depends on:** SW20.5, SW19.9, P93.2
- **Validate:** `cd /home/user/Stagewatch && tools/test.sh build interop`

### SW20.14 — Stagewatch — Stagewatch 2.20 papers and release

playout-links unit 11: REMOTE 2.20 (playout grammar, receipts, the OSC/Companion change, trigger auth, the 413), COMPANION, FEATURES, WHATS_NEXT 2.20 (what it did not do, the rollback note), NODES §5, README. Version 2.20.0.

- **Sources:** playout-links unit 11
- **Gaps:** G01, G07, G26, G28, G29, G30
- **Depends on:** SW20.11, SW20.12, SW20.9, SW20.10, SW20.13
- **Validate:** `cd /home/user/Stagewatch && tools/test.sh && tools/test.sh interop; on the maintainer's PC: pwsh tools\release.ps1 -NoBump`

### P93.3 — Patterns — Round 93 papers (PLAN §111)

PLAN §111, REVIEW ('Walk: none asked'), CHANGELOG, README, REMOTE (stage and video fields), COMPANION; OPEN L48 closed and the assistant's reach recorded; tag row 92. Closing sentence: STAGE MESSAGE's journal row with the receipt's stage named, read beside the stage page.

- **Sources:** patterns-playmap-side 91.4
- **Gaps:** G21, G48
- **Depends on:** P93.1, P93.2, owner:P-numbers
- **Validate:** `cd /home/user/Patterns && dotnet build Patterns.sln -c Debug -v q && for s in Core Rendering Devices Audio Assistant Audience App; do dotnet test tests/Patterns.$s.Tests -c Debug --no-build || exit 1; done && (cd integrations/companion-module-patterns && npm test)`

### PM-6 — Mapper — fix-peerlink-core: PlayMap's PeerLink core and its links' hygiene

The profile's PM-PL1 and the rest of PM-PL0 (peerlink.md §6.3). src/PlayMap.Core/Bus/PeerLink.cs (the runtime only) and tests/PlayMap.Core.Tests/Bus/PeerLinkVectorsTests.cs over the ```peerlink block; Liveness.Of inclusive (today `<`, Core/Bus/ShowBus.cs:226-229); ClockOffsetEstimator refuses stamps ≤ 0 and rtt > 60 s; Pairing.Normalise by 13.5.1 with refusal at entry. WireClient: a written command with no answer is unconfirmed, never failed (today both are failures, WireClient.cs:158-165); ids pm<n>-<inst> (today per run, :153); reset only after verified + 10 s (today when the peer said anything, :247). StagewatchLink: only a state frame clears _stuck (today a plain beat, :381); the side-call lane out of the beat loop (:438-475); reset only after verified + 10 s (:236-237); results retried 3→30 s for 30 min (today posted once, _ran cleared at 1000, :538-552). The held blackout is shown with its age (13.17; NetworkHost.cs:696-714). The desk↔engine WireClient path keeps working at level 0 on loopback (13.18). Scripted-peer connector scenarios at level 0 (S11–S14, S19, S22) from PlayMap.Net.Tests on tests/Shared/ManualTime.cs.

- **Sources:** PeerLink profile PM-PL1; PeerLink profile PM-PL0 (rest)
- **Gaps:** G16, G08
- **Depends on:** PM-4, owner:M-PMX
- **Validate:** `cd /home/user/Mapper && dotnet build PlayMap.sln -c Debug -v q && dotnet test tests/PlayMap.Core.Tests -c Debug --no-build --filter "FullyQualifiedName~PeerLink|FullyQualifiedName~ShowBus|FullyQualifiedName~Pairing" && dotnet test tests/PlayMap.Net.Tests -c Debug --no-build`

### PM-7 — Mapper — fix-stagewatch-timer-air: blackout and watermark in STATE's timer and on the timer surface

patterns-playmap-side PM-F3 (G12's air half): the timer source carries Stagewatch's blackout and watermark into STATE's timer object and draws them (the timer's pixel test through its BackendFixture leg — the GL line holds); field order fixed for PM-14.

- **Sources:** patterns-playmap-side PM-F3; windows-for-peers PM-W2 (air half)
- **Gaps:** G12
- **Depends on:** owner:M-PMX
- **Validate:** `cd /home/user/Mapper && (Xvfb :99 &) && export DISPLAY=:99 && dotnet build PlayMap.sln -c Debug -v q && dotnet test tests/PlayMap.Net.Tests -c Debug --no-build --filter "FullyQualifiedName~StagewatchTimer" && dotnet test tests/PlayMap.Render.Tests -c Debug --no-build --filter "FullyQualifiedName~Timer"`

### PM-8 — Mapper — fix-stagewatch-context-size: the posted context fits 64 KiB by bytes; STATE's size fenced

patterns-playmap-side PM-F4 with playout-links' Mapper fence: the context posted to Stagewatch fits 64 KiB by UTF-8 bytes, trims in a fixed order (recent first, then command words, then green items) and says so (context:trimmed), never failing silently; reads Stagewatch's 413 (SW20.9). A fence test that a 'HELLO Stagewatch' session's STATE stays within the reader's bound and one clock (G26), asserting fields and sizes, not timings.

- **Sources:** patterns-playmap-side PM-F4; playout-links Mapper unit (fence)
- **Gaps:** G25, G26
- **Depends on:** owner:M-PMX
- **Validate:** `cd /home/user/Mapper && dotnet build PlayMap.sln -c Debug -v q && dotnet test tests/PlayMap.Engine.Tests -c Debug --no-build --filter "FullyQualifiedName~Context|FullyQualifiedName~Fleet" && dotnet test tests/PlayMap.Net.Tests -c Debug --no-build --filter "FullyQualifiedName~Wire"`

## Train 4 — One desk for every app, one verdict for the room, the Eye's network band (Stagewatch 2.21; Patterns Round 94)

**Goal.** The operator sends any app a line it lists, and decides what any AI proposes for any app, from Stagewatch's shared queue, with each app's own risk read correctly (cross-app release A); the room has one 'ready for doors' verdict and one merged report; Patterns' Eye draws other apps in a bounded NETWORK band whose labels stay readable at the fit view, at a cost that no longer serializes every context each second. Validated by the desk e2e, the preflight e2e, the interop desk/doors/lockout rows, and Patterns Round 94's layout fence.

### SW21.1 — Stagewatch — The verb table and PeerContexts' caps: one owner for every reader's numbers

cross-app-proposals SW-1 (§1, Appendix A; proto/verbsyntax2.py): VerbSyntax/VerbTable (fixed words stop at '[' and 'A|B' are alternatives, G02); PeerContexts keeps 256 commands with verbs and words to 512 characters, items ≤ 200, links ≤ 400 (windows-for-peers SW-W1 only reads these), sizes by UTF-8 bytes on top of SW20.9's 413; risk only from a verified context, the link's own context first; golden replay of PlayMap's busy context and probes. This is the single place the de-duplicated caps live (cross-app SW-1, windows SW-W1, spec-1-3 §14, peers-not-screens SW-1).

- **Sources:** cross-app-proposals SW-1; windows-for-peers SW-W1 (caps)
- **Gaps:** G02
- **Depends on:** SW18.1, SW20.9
- **Validate:** `cd /home/user/Stagewatch && tools/test.sh build unit`

### SW21.2 — Stagewatch — The desk's rules: run-tagged ids, LineFence, self-raise, last collect, §6-notation rows, peers' documented lines

cross-app-proposals SW-2: proposal ids p<n>-<run>; LineFence; refuse-not-cut at 2048; from.t and desk-id refusal; a target speaking for itself takes its own risk when its key is bound to that id (G03); last collect; ProposeApproved; JSON collected/form appended; context rows in the §6 notation; a DeskRules test that the lines peers document parse (AGMessage's `message "…" <urgency> off ack`, G05).

- **Sources:** cross-app-proposals SW-2
- **Gaps:** G02, G03, G05
- **Depends on:** SW21.1
- **Validate:** `cd /home/user/Stagewatch && tools/test.sh build unit`

### SW21.3 — Stagewatch — DeskService, LineRisk/CommandRisk.Of in Core, the shared-approve tick fix, the receipt-notes stack

cross-app-proposals SW-3: DeskService; LineRisk and CommandRisk.Of in Timer.Core (Copilot.Classify maps it); the shared-approve tick fix; the receipt-notes stack and RefIds; MainViewModel.Desk as a mirror.

- **Sources:** cross-app-proposals SW-3
- **Gaps:** G14
- **Depends on:** SW21.2
- **Validate:** `cd /home/user/Stagewatch && tools/test.sh build unit`

### SW21.4 — Stagewatch — Release A grammar: `peer <app> <line>` and `desk approve|reject <id>` on HTTP and WS, with receipts

cross-app-proposals SW-4 (§2.1–2.2): ShowCommandKind.Peer and .Desk appended with parser verbs and PersonHelp, kept out of ShowContext.Commands and the AI's Help; the shared sender gate (extending SW20.2's); POST /api/peer; executors that report sent → collected → done or refused; `next` approves only a non-high item; the rig hosts the desk; tests/e2e/desk.e2e.cjs.

- **Sources:** cross-app-proposals SW-4
- **Gaps:** G14, G20
- **Depends on:** SW21.3, SW18.2, SW20.2
- **Validate:** `cd /home/user/Stagewatch && tools/test.sh build unit e2e`

### SW21.5 — Stagewatch — Release A readers: the operator frame's desk block, config peers, context collects, the producer page panel

cross-app-proposals SW-5: the operator frame's `desk` block (field order agreed before SW21.8's `network`), config peers, context collects, the producer page's desk panel; pages e2e.

- **Sources:** cross-app-proposals SW-5
- **Gaps:** G14, G20
- **Depends on:** SW21.4
- **Validate:** `cd /home/user/Stagewatch && tools/test.sh build unit e2e`

### SW21.6 — Stagewatch — The desk (PC work): Show network row menu, the send dialog with live risk, approval greys, help

cross-app-proposals SW-6: the Show network row's right-click 'Propose to <app>…' and '…', the send dialog with the line's risk as typed, greyed approvals, help; UiSmoke on the maintainer's PC.

- **Sources:** cross-app-proposals SW-6
- **Gaps:** G14, G20
- **Depends on:** SW21.4
- **Validate:** `cd /home/user/Stagewatch && tools/test.sh build unit; on the maintainer's PC: pwsh tools\smoke.ps1`

### SW21.7 — Stagewatch — One report: verified peers' lines and every desk decision, rejections included

ecosystem-lane SW-G49a (§2.10): Journal, ShowBus PeerLines (posted road and link-read road, each line with its node, its own time and how it came), DeskService rows for every decision including rejections, ShowContext, WebServer network.csv; culture-invariant times tested under th-TH; the report says it is a sample and each node's own journal is the record.

- **Sources:** ecosystem-lane SW-G49a
- **Gaps:** G49
- **Depends on:** SW18.2, SW21.3
- **Validate:** `cd /home/user/Stagewatch && tools/test.sh build unit`

### SW21.8 — Stagewatch — One preflight: 'ready for doors' with a reason per node, on every surface; the rig's preflight

ecosystem-lane SW-G49b and SW-G49c (§2.9): NetworkPreflight (pure, injected time), /api/preflight with the stuck rule (a stuck Stagewatch is never ready), the operator frame's `network` after `desk`, MCP get_preflight, the AI facts, Companion network_ready (yes/no/unknown), the producer page, the desk header and right-click (PC), help; amber never blocks; a configured node gone is red, a seen-only one amber; kept out of Stagewatch's own context; rig preflight and tests/e2e/preflight.e2e.cjs.

- **Sources:** ecosystem-lane SW-G49b; ecosystem-lane SW-G49c
- **Gaps:** G49
- **Depends on:** SW21.5, SW19.3
- **Validate:** `cd /home/user/Stagewatch && tools/test.sh build unit module e2e`

### SW21.9 — Stagewatch — Interop: the desk rows, Patterns' desk and stuck rows, the doors, nobody locks anybody out

ecosystem-lane SW-R2 (S3.1, S3.2, S3.5, S3.7, S3.8, S7.3's last word), SW-R3 (S3.3, S3.4, S6.2b), SW-R5 (S6.3, S8.1–S8.4) and SW-R7 (S7.4, S1c.3, S1b.1) for Stagewatch, Patterns and PlayMap; each row only once it passes at the bumped pins. AGMessage's rows wait for SW-INT-AG.

- **Sources:** ecosystem-lane SW-R2; ecosystem-lane SW-R3; ecosystem-lane SW-R5; ecosystem-lane SW-R7
- **Gaps:** G44, G49
- **Depends on:** SW21.4, SW21.7, SW21.8, SW19.13, P91.3, P89.1
- **Validate:** `cd /home/user/Stagewatch && tools/test.sh build interop`

### SW21.10 — Stagewatch — Stagewatch 2.21 papers and release

cross-app-proposals SW-7 and ecosystem-lane SW-P's report/preflight part: REMOTE 2.21 (peer and desk verbs; preflight; the report; each route's key scope), COMPANION, FEATURES, WHATS_NEXT 2.21 (release B's items and why they wait), ECOSYSTEM (manifest superseded), README. Version 2.21.0.

- **Sources:** cross-app-proposals SW-7; ecosystem-lane SW-P
- **Gaps:** G14, G20, G49
- **Depends on:** SW21.6, SW21.9
- **Validate:** `cd /home/user/Stagewatch && tools/test.sh && tools/test.sh interop; on the maintainer's PC: pwsh tools\release.ps1 -NoBump`

### P94.1 — Patterns — Round 94.1: the NETWORK band — per-app clusters on a band of their own, bounded by the canvas

eye.md EYE.1 with the Eye critique's fixes. Today every app thing stacks in one CONTROL column (src/Patterns.Core/Services/EyeLayout.cs:83-89,120-141): 48 things make 3,304 units, so no label shows at the fit view under 1,236 px (labels at scale ≥ 0.35, Views/Controls/EyeCanvas.cs:217; fit floored at 0.12, EyeCamera.cs:15,50-55). EyeNode gains an appended Band (layout) beside Plane (lens), so another app's things sit in its cluster yet keep their plane for the lenses; EYE JSON appends `band`; BandLabel gains NETWORK (EyeLayout.cs:93-99; the side card, MainViewModel.Eye.cs:72). A cluster is the app card then up to 12 things in a 3-column grid by the kind table and the context's order; ⌈√expanded⌉ clusters per row; collapsed cards in a strip; '+N more'; sticky slots. The expansion set is explicit view state reported in EYE JSON and STATE `eye`, never changed by focus; a change rebuilds only the layout. The expanded count is derived from the Eye page's real canvas (minus the 300-px side card, Views/Sections/EyeSection.axaml:53) so the fit scale stays ≥ 0.35; at the bound the fit view shows marks only and says so. The rail, eye.worst, the problems queue and FromEye's headline count this desk's own nodes, its link edges and needed apps' cards; borrowed problems order after own ones (Eye.cs:290-303,488-494; ShowContext.cs:187) — ShowBusTests.cs:264 rewritten with the reason. Tests: EyeLayoutTests (the reference room fits the real canvas at ≥ 0.35; the comment records today's 3,304-unit column as the measured cause); SyntheticNetworkTests (round 84's fixed-seed xorshift, 0–160 apps: no overlap, stable ids and slots, 'left' exact).

- **Sources:** Eye EYE.1; Eye critique issues 5, 6, 9, 14
- **Gaps:** G24
- **Depends on:** P93.3
- **Validate:** `cd /home/user/Patterns && dotnet build Patterns.sln -c Debug -v q && dotnet test tests/Patterns.Core.Tests -c Debug --no-build --filter "FullyQualifiedName~Eye|FullyQualifiedName~SyntheticNetwork|FullyQualifiedName~ShowBus" && dotnet test tests/Patterns.App.Tests -c Debug --no-build --filter "FullyQualifiedName~Eye"`

### P94.2 — Patterns — Round 94.2: the Eye's cost and marks — revisions not contexts, the walk cached, parallel edges, shapes at every scale

Today the facts hash serializes every other app's whole context each second (src/Patterns.App/Services/EyeService.cs:66-70; Eye.cs:133-134,142-145) and the focus walk runs every render (EyeCanvas.cs:168,306). The hash takes a steady projection: per-source context revisions, never contexts, and no volatile link fields (heardMs, nextMs, attempt, clock age; offset quantised) — a Core test that a record whose heardMs alone moves causes no rebuild; Hops cached by (Rev, focus) with a counter seam; edges off view culled. Parallel edges: EdgesBetween and distinct Neighbours (Eye.cs:282-288,350), one contact row and one menu id per edge (EyeMenus.cs:51-60, DeskMenuTests.cs:130), offset segments with their own midpoint marks. Marks drawn in the dot's geometry at every scale (✓ circle, △ triangle, ✕ cross, ■ square, ○ ring, i), derived from Light with i an override so the replay's relighting (EyeReplay.cs:171,295) cannot leave a stale mark; side-card and contact rows (EyeSection.axaml:65,87) and the Nodes cards carry the shape. The rebuild's duration and node count go to patterns.metrics.csv; no governor (nothing measured).

- **Sources:** Eye EYE.1 (cost); Eye critique issues 7, 10, 11
- **Gaps:** G24
- **Depends on:** P94.1
- **Validate:** `cd /home/user/Patterns && dotnet build Patterns.sln -c Debug -v q && dotnet test tests/Patterns.Core.Tests -c Debug --no-build --filter "FullyQualifiedName~Eye" && dotnet test tests/Patterns.App.Tests -c Debug --no-build --filter "FullyQualifiedName~Eye|FullyQualifiedName~DeskMenuAppTests"`

### P94.3 — Patterns — Round 94 papers (PLAN §112)

PLAN §112 (evidence: a handed-in critique — the Eye design and its review; what it did not do: no fold, no link edges yet); REVIEW 'Walk: none asked'; CHANGELOG; README; REMOTE (EYE JSON band, the expansion state; REMOTE.md:107's lens words); the lens and band words in Help.cs:225, HelpBodies.cs:272 and EyeService.cs:119; docs/EYE.md; OPEN; tag row 93. Closing sentence: no verb an output can show; the metrics row named as the first measurement.

- **Sources:** Eye EYE.6 (its EYE.1 part); Eye critique issue 20
- **Gaps:** G24
- **Depends on:** P94.2, owner:P-numbers
- **Validate:** `cd /home/user/Patterns && dotnet build Patterns.sln -c Debug -v q && for s in Core Rendering Devices Audio Assistant Audience App; do dotnet test tests/Patterns.$s.Tests -c Debug --no-build || exit 1; done && (cd integrations/companion-module-patterns && npm test)`

## Train 5 — The hub proves itself, windows travel to peers, PlayMap known on Patterns (Stagewatch 2.22; Patterns Rounds 95–96)

**Goal.** Stagewatch's /ws speaks LINK (mutual proofs bound to the certificate, the session credential S on side calls, a wrong-proof table apart from the PIN's, seen-in-clear keys, live revocation, features final/receipts/stuck) — the profile's Phase 2; Patterns' Stagewatch link proves itself and records a floor (Phase 3); a Stagewatch window is granted to a named app's verified link and shown on Patterns, never as a copied address; Stagewatch's release B reaches Companion, OSC, the copilot and MCP; Patterns knows PlayMap as a Show Bus node device on the PeerLink core. Validated by Stagewatch's acceptor scenarios (S1–S3, S8–S10, S12, S14–S20), grants.e2e.cjs, Patterns' ws-connector scenarios at level 1, and the interop windows rows for Patterns.

### SW22.1 — Stagewatch — /ws speaks LINK: proofs first, the session credential on side calls, its own wrong-proof table, receipts kept

The profile's SW-PL2 on peers-not-screens SW-3's node links (peerlink.md §4 13.6.5, 13.7; §6.1). A /ws?node=&t=&link=1 socket hears the link message first and gets a verdict carrying proof, lid, level, can, inst and beatMs; after ok:false it stays a public viewer. Authorization: ShowBus lid, s on /api/context, /api/peers/context, /api/proposals*, /api/message/* and the result routes (401 relink); a wrong-proof table per (address, kid) apart from the PIN's, unknown kids uncounted; Host and Origin checked before any loopback trust; a bearer key arriving plain from the LAN marked seen in clear (key row and context, amber) and given no window grant; revocation ends S at once; features final, receipts (the store, repeat:true, {cmd:'receipts'}, @rq RECEIPTS on Companion TCP) and stuck (the existing beat; stuckMs 3000 = StuckAfter, src/Timer.Core/ShowState.cs:217, named in its §8 row); lateMs in receipts (late commands run silently today, MainViewModel.Receipts.cs:21-27); a line of words with 429/Retry-After before shedding past 64 connections (closed silently today, WebServer.cs:276-281); Settings says that with HTTPS off other apps cannot link with keys. Scripted-peer acceptor scenarios S1–S3, S8–S10, S12 (emitting), S14–S20 on ws and side calls.

- **Sources:** PeerLink profile SW-PL2
- **Gaps:** G43, G15
- **Depends on:** SW19.2, SW18.3
- **Validate:** `cd /home/user/Stagewatch && tools/test.sh build unit e2e`

### SW22.2 — Stagewatch — Context: windows feed only while showing a page; health items unlinked; window rows; placements; the windows cap

windows-for-peers SW-W1 (§2.6): links per its D11 (timer -feeds-> window only while it shows a page, G23), health items unlinked, 13 rows in the §6 notation, peers' links parsed within SW21.1's caps, WindowPlacements with the holder and the verified rule (pure), the hello cap `windows`, AiContext windows facts and PeerFact.Can.

- **Sources:** windows-for-peers SW-W1
- **Gaps:** G22, G23, G24
- **Depends on:** SW21.1, SW18.1
- **Validate:** `cd /home/user/Stagewatch && tools/test.sh build unit`

### SW22.3 — Stagewatch — Grants in Core: `window grant <window> <app>`

windows-for-peers SW-W2: the grant model, ShowApp, the parser (explicit all), Apply, revoke and delete, Find/Via, settings sanitising (remote.windows[], never a show file), copilot rows (High), help, the FindApp seam, WindowDirector.Run(findApp), the proposal fence.

- **Sources:** windows-for-peers SW-W2
- **Gaps:** G10
- **Depends on:** —
- **Validate:** `cd /home/user/Stagewatch && tools/test.sh build unit`

### SW22.4 — Stagewatch — WebServer delivers grants only on the app's verified link; every grant-token miss ends black

windows-for-peers SW-W3 (§2.3–2.5) under D0: per-grant tokens and Via, black on every grant-token miss, IsLocal, clearSeen (a key seen in clear gets no grant, 13.6.9), can=, grants and the window-* messages on the verified node link (LINK-verified or key-verified over TLS or loopback), grants-seen, ViewerStatus bounded per key beside WindowStatus and Drawn, identify and reload rules, /api/windows grants, window.js's black end.

- **Sources:** windows-for-peers SW-W3
- **Gaps:** G10, G11
- **Depends on:** SW22.3, SW22.1, owner:S-D0
- **Validate:** `cd /home/user/Stagewatch && tools/test.sh build unit e2e`

### SW22.5 — Stagewatch — Window lights and words: viewers and claims, the detail frame's windows block, the desk

windows-for-peers SW-W4: ContextItems with viewers and claims; the detail frame's windows block (last, after rooms); the view model's FindApp, journal rows, the desk headline; the Screens column and menus; the Show network menu (WPF on the PC).

- **Sources:** windows-for-peers SW-W4
- **Gaps:** G22, G24
- **Depends on:** SW22.2, SW22.4
- **Validate:** `cd /home/user/Stagewatch && tools/test.sh build unit; on the maintainer's PC: pwsh tools\smoke.ps1`

### SW22.6 — Stagewatch — The rig's grants and grants.e2e.cjs

windows-for-peers SW-W5: the rig's FindApp, Frame().Windows, placements and --state; the ws devDependency; tests/e2e/grants.e2e.cjs.

- **Sources:** windows-for-peers SW-W5
- **Gaps:** G10, G11, G24
- **Depends on:** SW22.5, SW18.5
- **Validate:** `cd /home/user/Stagewatch && tools/test.sh build unit e2e`

### SW22.7 — Stagewatch — Companion module: window_grant, window_state and window_holding

windows-for-peers SW-W6: the window_grant action, window_state and window_holding feedbacks, variables, presets.

- **Sources:** windows-for-peers SW-W6
- **Gaps:** G22
- **Depends on:** SW22.5
- **Validate:** `cd /home/user/Stagewatch && tools/test.sh module`

### SW22.8 — Stagewatch — Release B: the Companion and OSC doors through the gate; the module's desk actions

cross-app-proposals SW-8: Companion TCP and OSC reach peer and desk through the shared gate, the ACCESS line, the module's actions, variables and feedback, tests.

- **Sources:** cross-app-proposals SW-8
- **Gaps:** G14, G20
- **Depends on:** SW21.4
- **Validate:** `cd /home/user/Stagewatch && tools/test.sh build unit module`

### SW22.9 — Stagewatch — Release B: the copilot plans steps for other apps; a person decides

cross-app-proposals SW-9: CopilotStep Target, two-pass verbs, ParsePlan with peers' verb tables (risk from the target's list), PlanRunner in order, the AI Help split; every peer step goes to the local ProposalDesk.

- **Sources:** cross-app-proposals SW-9
- **Gaps:** G14
- **Depends on:** SW21.4
- **Validate:** `cd /home/user/Stagewatch && tools/test.sh build unit`

### SW22.10 — Stagewatch — Release B: MCP run_command target, string get_proposal, get_context peer

cross-app-proposals SW-10.

- **Sources:** cross-app-proposals SW-10
- **Gaps:** G14
- **Depends on:** SW21.4
- **Validate:** `cd /home/user/Stagewatch && tools/test.sh build unit`

### P95.1 — Patterns — Round 95.1: the Stagewatch link proves itself (LINK on ws), pins after the proof and records its floor

The profile's 'StagewatchLinkService speaks link=1 and S' (peerlink.md §6.2, Phase 3): /ws?node=<id>&t=patterns&link=1 with no Authorization header; the client proof first; nothing taken before the acceptor's proof verifies (13.6.3); pinned only after that proof (13.8.2); floors {tls, level, proof} per node id recorded, a later lesser offer refused red with one journalled press to lower it (P92.3's FLOOR LOWER); side calls carry Authorization: ShowBus lid, s only to the pinned certificate or loopback; a 401 relink. The level-0 path stays for an older Stagewatch. Scripted-peer ws-connector scenarios S1, S2, S4–S7, S10, S11–S14, S17–S19.

- **Sources:** PeerLink profile Patterns Round PL (link=1 and S)
- **Gaps:** G08, G43
- **Depends on:** SW22.1, P92.3, P94.3
- **Validate:** `cd /home/user/Patterns && dotnet build Patterns.sln -c Debug -v q && dotnet test tests/Patterns.Core.Tests -c Debug --no-build --filter "FullyQualifiedName~PeerLink" && dotnet test tests/Patterns.App.Tests -c Debug --no-build --filter "FullyQualifiedName~PeerLinkWire|FullyQualifiedName~StagewatchLink"`

### P95.2 — Patterns — Round 95.2: a Stagewatch window granted to this desk, placed ready-gated, shown only from the air

windows-for-peers PT-W2 (§2.10, §3 Patterns) plus PT-W3's NODES ASK WINDOW: can=pages and the pages cap; a loopback dial when the hello comes from this machine; per-link rev; persisted grants (local Control section, never the show); the certificate decision; Library tiles; SCREEN n WINDOW (only when ready) / SCREEN n PVW WINDOW then SCREEN n TAKE, so air never goes black or white while a browser starts; STATE bus.stagewatch appends `windows`; the Run strip chip; menus; the Nodes page; the module. Whether a window is 'shown' is derived from the air (AirState, EyeScreen.OnAir/Staged, OutputsLive): on air, 'in the preview — not on air', or 'outputs closed' (the Eye critique's air issue).

- **Sources:** windows-for-peers PT-W2; windows-for-peers PT-W3 (NODES ASK WINDOW); Eye critique (window chain and the air)
- **Gaps:** G10
- **Depends on:** P89.3, P91.1, SW22.4
- **Validate:** `cd /home/user/Patterns && dotnet build Patterns.sln -c Debug -v q && for s in Core App; do dotnet test tests/Patterns.$s.Tests -c Debug --no-build --filter "FullyQualifiedName~Window|FullyQualifiedName~Grant" || exit 1; done && (cd integrations/companion-module-patterns && npm test)`

### SW22.11 — Stagewatch — Interop: windows for peers, the Patterns rows (S5.1); one row per real PeerLink pair to the hub

ecosystem-lane SW-R6 for Patterns (S5.1; Xvfb, Mesa and Playwright join the job) and the profile's cross-app rows for the ws pairs that now prove themselves ('Patterns links to Stagewatch over ws: verified both ways, pinned, floor set'); a stuck rig: every follower shows no time within stuckMs + 4 s and every link stays live, amber.

- **Sources:** ecosystem-lane SW-R6; PeerLink profile conformance (cross-app rows)
- **Gaps:** G44, G10
- **Depends on:** SW22.6, P95.2, P95.1
- **Validate:** `cd /home/user/Stagewatch && tools/test.sh build interop`

### SW22.12 — Stagewatch — Stagewatch 2.22 papers and release

windows-for-peers SW-W7 and cross-app-proposals SW-11: REMOTE 2.22 with D0's answer (LINK on /ws, S, the wrong-proof table, seen in clear, grants), COMPANION, FEATURES, NODES, WHATS_NEXT 2.22, README. Version 2.22.0.

- **Sources:** windows-for-peers SW-W7; cross-app-proposals SW-11
- **Gaps:** G22, G14
- **Depends on:** SW22.7, SW22.8, SW22.9, SW22.10, SW22.11
- **Validate:** `cd /home/user/Stagewatch && tools/test.sh && tools/test.sh interop; on the maintainer's PC: pwsh tools\release.ps1 -NoBump`

### P95.3 — Patterns — Round 95.3: the twin's WINDOWS word, the standby's Super Check row, DRILL scenario 23

windows-for-peers PT-W4: the twin tells the standby which grants it lacks (grant paths kept off the twin); a standby Super Check row; DRILL scenario 23.

- **Sources:** windows-for-peers PT-W4
- **Gaps:** G10
- **Depends on:** P95.2
- **Validate:** `cd /home/user/Patterns && dotnet build Patterns.sln -c Debug -v q && dotnet test tests/Patterns.App.Tests -c Debug --no-build --filter "FullyQualifiedName~Twin"`

### P95.4 — Patterns — Round 95 papers (PLAN §113)

PLAN §113; REVIEW 'Walk: unwalked' (WebView2 showing a granted window: QUALIFICATION §22 and §27 rows); CHANGELOG; README; REMOTE (SCREEN n WINDOW, NODES ASK WINDOW, bus.stagewatch windows, the link's floor); COMPANION; DRILL; OPEN; tag row 94. Closing sentence: SCREEN n WINDOW's journal row, read beside the screen's tile and the Run strip chip.

- **Sources:** windows-for-peers PT-W5
- **Gaps:** G10
- **Depends on:** P95.1, P95.3, owner:P-numbers
- **Validate:** `cd /home/user/Patterns && dotnet build Patterns.sln -c Debug -v q && for s in Core Rendering Devices Audio Assistant Audience App; do dotnet test tests/Patterns.$s.Tests -c Debug --no-build || exit 1; done && (cd integrations/companion-module-patterns && npm test)`

### P96.1 — Patterns — Round 96.1: Show Bus node devices — PlayMap known, paired by proof and driven, drawn in the NETWORK band

patterns-playmap-side 92.1 (decision 19), amended by the Eye critique. Lines devices send raw lines with no proof today, so Patterns drives PlayMap only when it is open or trusts the address (G35). A node device on the Patterns.Core PeerLink core from its first commit: HELLO; LINK when PlayMap offers it, else the legacy AUTH-PROOF on a 32-hex NONCE; SUBSCRIBE OFF and CONTEXT; untagged facts only after the acceptor's proof or OK paired; ids d<n>-<inst> with outcomes by 13.12.3. PlayMap's context is drawn as a cluster in Round 94's NETWORK band (not the VIDEO band) with its things keeping their planes for the lenses, folded with its hello only when the hello address and the context id agree (flagged legacy at level 0); KindWords gains playmap → PlayMap (src/Patterns.Core/Services/ShowBusNetwork.cs:168-175); the Nodes page and 'Add as a device'; the brief. Tests: App tests against a fake PlayMap speaking WireSession's grammar and the scripted peer as a level-0 line acceptor.

- **Sources:** patterns-playmap-side 92.1; Eye critique issue 17 (92.1 adopts the band)
- **Gaps:** G35
- **Depends on:** P92.1, P94.1, P95.4
- **Validate:** `cd /home/user/Patterns && dotnet build Patterns.sln -c Debug -v q && dotnet test tests/Patterns.Devices.Tests -c Debug --no-build && dotnet test tests/Patterns.App.Tests -c Debug --no-build --filter "FullyQualifiedName~Device|FullyQualifiedName~ShowBus|FullyQualifiedName~PeerLinkWire"`

### P96.2 — Patterns — Round 96 papers (PLAN §114)

PLAN §114; REVIEW 'Walk: none asked'; CHANGELOG; README; REMOTE (the node device); OPEN; tag row 95. Closing sentence: the DEVICE lines' receipts, read beside the Nodes page.

- **Sources:** patterns-playmap-side Round 92 papers
- **Gaps:** G35
- **Depends on:** P96.1, owner:P-numbers
- **Validate:** `cd /home/user/Patterns && dotnet build Patterns.sln -c Debug -v q && for s in Core Rendering Devices Audio Assistant Audience App; do dotnet test tests/Patterns.$s.Tests -c Debug --no-build || exit 1; done && (cd integrations/companion-module-patterns && npm test)`

## Train 6 — Acceptors, and every line connector by proof (Patterns Round 97; Stagewatch 2.23; PlayMap's wp4-peerlink)

**Goal.** Patterns' wire 9697 offers TLS and LINK, issues scoped app keys bound to one connector and revokes them live, keeps receipts and says what it accepted; PlayMap's 9941 and playmap.lines.v1 do the same when its owner opens the lane; Stagewatch's PlayoutLink then proves an issued key to both, over TLS, with floors, and times a GO only on a fit clock measured over TLS (the profile's Phase 4). Validated by the acceptor scenarios on each line acceptor and the connector scenarios on PlayoutLink; PlayMap's rows wait for its owner and peers show PlayMap 'legacy' (amber) until then.

### P97.1 — Patterns — Round 97.1: TLS on 9697, told from plain by the first byte

The profile's Round PL (peerlink.md §6.2; 13.8.5): 9697 sniffs 0x16 and serves TLS with the desk's certificate (Round 88's TlsIdentity, 825 days from P91.4); plain stays for decks with their legacy proof; closes OPEN L55's wire half. Tests: App test dialling 9697 over TLS and plain on one listener; the hello's tls names 9697.

- **Sources:** PeerLink profile Patterns Round PL (TLS)
- **Gaps:** G08
- **Depends on:** P96.2
- **Validate:** `cd /home/user/Patterns && dotnet build Patterns.sln -c Debug -v q && dotnet test tests/Patterns.App.Tests -c Debug --no-build --filter "FullyQualifiedName~ControlService|FullyQualifiedName~Tls"`

### P97.2 — Patterns — Round 97.2: LINK and LINK PROOF on 9697; constant-shape refusals; a wrong-proof table of its own; app links within the wire's limits

13.6.1–13.6.4 on line: LINK joins the pre-pairing words (P91.2's OpenBeforePairing); one proof per connection, a nonce per proof; an unknown kid answered exactly like a known one, compared in constant time, refused with one set of words; handshake within 10 s; ≤ 4 unfinished per address; wrong proofs counted per (address, kid) apart from the PIN, web and deck tables (today five wrong legacy proofs close, ControlService.cs:541 — kept for legacy proofs). App links get a sub-cap inside WireLimits (64 clients, 16 per address, src/Patterns.Core/Services/WireLimits.cs:14-15) so decks keep reserved slots (the Eye critique's capacity issue). Host and Origin checked before loopback trust on HTTP. Scripted-peer line-acceptor scenarios S1–S3, S8, S9, S15, S16, S19, S20; every loopback test sets ControlService.TrustLoopback = false and restores it in finally.

- **Sources:** PeerLink profile Patterns Round PL (LINK); Eye critique issue 13 (capacity)
- **Gaps:** G08
- **Depends on:** P97.1
- **Validate:** `cd /home/user/Patterns && dotnet build Patterns.sln -c Debug -v q && dotnet test tests/Patterns.Core.Tests -c Debug --no-build --filter "FullyQualifiedName~PeerLink|FullyQualifiedName~ControlProtocol" && dotnet test tests/Patterns.App.Tests -c Debug --no-build --filter "FullyQualifiedName~PeerLinkWire|FullyQualifiedName~ControlService"`

### P97.3 — Patterns — Round 97.3: the App links table — ptk_ keys bound to one connector, scoped, revocable live; the acceptor's list

13.5 and 13.16.5: a Nodes-page App links table with ptk_ keys (160 bits from the CSPRNG, shown once) bound to one connector t:id, scope follow/post/drive/operate mapped onto Patterns' verbs (the maintainer's map), last use, issued or enrolled, seen in clear, Revoke; kept DPAPI-protected beside the TLS key, never in the show, a backup or the twin; KEY MAKE, KEY BIND, KEY REVOKE catalogued, journalled, never proposed; enrolment from the pairing token over TLS only (13.5.7), NEW TOKEN revoking enrolled keys and ending their sessions; X-Patterns-Token arriving plain from another machine marked seen in clear (amber). The acceptor's list on the Nodes page and in STATE decks rows appended level, t, id, verified, scope; every decks reader drops or marks connections whose PeerId is set (CommandRouter.cs:558, ViewModels/MainViewModel.Poll.cs:388, NodeHost.cs:362, ShowActions.Nav.cs:190) so a proved app is never also a deck. Scenarios S17, S18, S21.

- **Sources:** PeerLink profile Patterns Round PL (keys); Eye critique issue 8b (decks readers)
- **Gaps:** G08
- **Depends on:** P97.2, owner:P-scopes
- **Validate:** `cd /home/user/Patterns && dotnet build Patterns.sln -c Debug -v q && dotnet test tests/Patterns.Core.Tests -c Debug --no-build --filter "FullyQualifiedName~PeerLink|FullyQualifiedName~Key" && dotnet test tests/Patterns.App.Tests -c Debug --no-build --filter "FullyQualifiedName~PeerLinkWire|FullyQualifiedName~Nodes|FullyQualifiedName~DeskMenuAppTests" && (cd integrations/companion-module-patterns && npm test)`

### P97.4 — Patterns — Round 97.4: receipts kept, a final answer for CUE GO, lateMs; stuck not offered; node devices speak LINK

13.12.4–6: features final (CUE GO's 'Requested' followed by `OK @id final {…}` once Done) and receipts (per verified connector id, the last 256 ids for 10 minutes; a repeated id answered with `repeat:true`, never run twice; `@rq RECEIPTS <id>…`); lateMs and its journal row. stuck is not offered: 'no measurement this round' — the Super Check records the desk thread's stall distribution so a later round sets stuckMs (lab line LP-1). P96.1's node devices speak LINK to PlayMap once PM-9 has landed, else stay at level 0 and say so. Scenarios S14 (acceptor side) and S22 against a level-0 acceptor.

- **Sources:** PeerLink profile Patterns Round PL (features)
- **Gaps:** G07, G35
- **Depends on:** P97.3, P96.1
- **Validate:** `cd /home/user/Patterns && dotnet build Patterns.sln -c Debug -v q && dotnet test tests/Patterns.App.Tests -c Debug --no-build --filter "FullyQualifiedName~PeerLinkWire|FullyQualifiedName~Receipt|FullyQualifiedName~Cue"`

### P97.5 — Patterns — Round 97.5: a proof grants nothing to an address — the 12-hour OSC carry-over ends (ADR)

13.18: today a wire or web proof trusts the address for OSC for 12 h (ControlService.cs:556-560, PairedAddressKept; :439, :978-981). On the maintainer's ADR, OSC on a paired desk is accepted only from the OSC ALLOW list (the operator's) and loopback; a LINK or legacy proof grants nothing to an address. Tests: an address that proved on 9697 sends OSC → refused unless listed.

- **Sources:** PeerLink profile 13.18 (for the maintainer's ADR)
- **Gaps:** G08
- **Depends on:** P97.2, owner:P-OSC12h
- **Validate:** `cd /home/user/Patterns && dotnet build Patterns.sln -c Debug -v q && dotnet test tests/Patterns.App.Tests -c Debug --no-build --filter "FullyQualifiedName~Osc|FullyQualifiedName~ControlService"`

### P97.6 — Patterns — Round 97 papers (PLAN §115)

PLAN §115; REVIEW 'Walk: none asked'; CHANGELOG; README; REMOTE (TLS on 9697, LINK and LINK PROOF, KEY verbs, RECEIPTS, decks fields, Trust); COMPANION (decks keep the legacy proof; the module's TLS dial deferred to its own round); the ADR; OPEN (L55's wire half closed; rows for what is not done); tag row 96. Closing sentence: KEY MAKE's and LINK's journal rows, read beside the App links table and the Super Check's 'Show network links' row.

- **Sources:** PeerLink profile 6.2 process
- **Gaps:** G08
- **Depends on:** P97.4, P97.5, owner:P-numbers
- **Validate:** `cd /home/user/Patterns && dotnet build Patterns.sln -c Debug -v q && for s in Core Rendering Devices Audio Assistant Audience App; do dotnet test tests/Patterns.$s.Tests -c Debug --no-build || exit 1; done && (cd integrations/companion-module-patterns && npm test)`

### SW23.1 — Stagewatch — PlayoutLink speaks LINK to Patterns and PlayMap: an issued key, TLS, floors, AT only on a fit clock

The profile's SW-PL3: LINK when the acceptor offers it (Patterns 9697 over TLS from P97.1; PlayMap playmap.lines.v1 or 9941 once PM-9 lands, else legacy amber); an issued ptk_ or pmk_ key, or enrolment over TLS where offered; floors per node id; AT only on a clock fit for the verb's tolerance and measured over TLS or a local link; the record into the link:<name> items, the frame's playout row and the copilot's facts; the trust verbs as ShowCommandKind appended (parser verb, help line, Copilot.Classify High, never in ShowContext.Commands). Scripted-peer line-connector scenarios S1, S2, S4–S7, S11–S14, S17–S19, S21, S22. Interop row: 'Stagewatch links to Patterns over line: verified both ways, pinned, floor set'; a pulled cable with a GO in flight: unconfirmed, then RECEIPTS, then applied, run once.

- **Sources:** PeerLink profile SW-PL3
- **Gaps:** G08, G28
- **Depends on:** SW20.4, P97.4
- **Validate:** `cd /home/user/Stagewatch && tools/test.sh build unit && tools/test.sh interop`

### SW23.2 — Stagewatch — Stagewatch 2.23 papers and release

REMOTE 2.23 (PlayoutLink by proof, the trust verbs, timed GO's TLS rule), COMPANION, FEATURES, WHATS_NEXT 2.23 (PlayMap at level 0 until its acceptor lands), README. Version 2.23.0.

- **Sources:** PeerLink profile 6.1
- **Gaps:** G08
- **Depends on:** SW23.1
- **Validate:** `cd /home/user/Stagewatch && tools/test.sh; on the maintainer's PC: pwsh tools\release.ps1 -NoBump`

### PM-9 — Mapper — wp4-peerlink: PlayMap as an acceptor — LINK on 9941 and playmap.lines.v1, pmk_ keys, receipts

The profile's PM-PL2 (peerlink.md §6.3): LINK on TCP 9941 and the /ws playmap.lines.v1 channel (BeforePairing gains LINK; WireSession.cs:27,229-233); TLS sniffed on 9941; a pmk_ key table in NodeCredentialStore with scopes mapped to ShowPolicy roles; TrustedAddresses (which pair without proof, Core/Bus/Pairing.cs:113-115) kept only for legacy Lines devices, amber 'trusted by address — not a proof', never for an app link; features final (verbs whose first answer is stage:accepted) and receipts; stuck not offered until the engine control lane's stall is measured (a lab line, reported not run); VerbCatalog rows LINK, RECEIPTS, KEY …; STATE clients rows append level, verified, scope, t, id with fixtures regenerated (PLAYMAP_WRITE_FIXTURES=1) and card descriptors; menus and right-click in step. Acceptor scenarios S1–S3, S8, S9, S14–S21.

- **Sources:** PeerLink profile PM-PL2
- **Gaps:** G08, G35
- **Depends on:** PM-6, owner:M-wp4-peerlink, owner:M-trusted-addresses
- **Validate:** `cd /home/user/Mapper && dotnet build PlayMap.sln -c Debug -v q && dotnet test tests/PlayMap.Net.Tests -c Debug --no-build && PLAYMAP_WRITE_FIXTURES=1 dotnet test tests/PlayMap.Engine.Tests -c Debug --no-build --filter "FullyQualifiedName~State" && dotnet test tests/PlayMap.Engine.Tests -c Debug --no-build && (cd integrations/companion-module-playmap && npm test)`

### PM-10 — Mapper — wp4-peerlink: PlayMap's links speak LINK; their records in STATE, preflight and the desk

The profile's PM-PL3: StagewatchLink with link=1 and S; PatternsLink with LINK when Patterns offers it (P97.2) and an issued ptk_ key; the fifth PONG word read; STATE links.stagewatch and links.patterns append the 13.16.3 record, with the clock's offsetMs, errMs and ageMs (G28); fixtures and card descriptors; preflight rows and the desk's lights with StatusMark shapes; the trust verbs catalogued and on the menu bar and right-click menus, never typed-only. Connector scenarios S1, S2, S4–S7, S10–S14, S17–S19, S22.

- **Sources:** PeerLink profile PM-PL3
- **Gaps:** G28, G08
- **Depends on:** PM-9, SW22.1, P97.4
- **Validate:** `cd /home/user/Mapper && dotnet build PlayMap.sln -c Debug -v q && dotnet test tests/PlayMap.Net.Tests -c Debug --no-build && PLAYMAP_WRITE_FIXTURES=1 dotnet test tests/PlayMap.Engine.Tests -c Debug --no-build --filter "FullyQualifiedName~State" && dotnet test tests/PlayMap.Engine.Tests -c Debug --no-build && dotnet test tests/PlayMap.App.Tests -c Debug --no-build`

## Train 7 — The Eye sees the room: one id one node, links as edges, contexts over PeerLinks (Patterns Rounds 98–99; Stagewatch 2.24; PlayMap's context rows)

**Goal.** Patterns' Eye draws every app, node and device once — by proof of identity — with each PeerLink as an edge carrying its 13.16.3 record verbatim, every node and edge saying where it came from (own, link, told, relayed, heard), claims drawn as claims, the window chains across apps, and lights computed once and read by every reader (Eye, Nodes card, STATE bus.apps[], exported peers rows, the brief, the module); other apps give Patterns their contexts with CONTEXT POST over a verified PeerLink; Stagewatch and PlayMap publish by P1–P8. Validated by EyeNetworkTests, SyntheticNetworkTests, the one-projection fence across five readers, CONTEXT POST's flood test, and the ecosystem rows: one node per app id, no deck duplicate for a proved link, the window chain with real apps, a stuck rig's cards amber with every link live and amber.

### P98.1 — Patterns — Round 98.1: EyeNetwork — the fold by proof, one projection for lights, precedence not 'worst', link edges from records, @ ends

eye.md EYE.2 with the critique's fixes (pure, Patterns.Core). New Services/EyeNetwork.cs: Fold — a LINK verdict id folds hello, held link, accepted link, context, mentions and node device into app:<id>; at level 0 a dialled device folds only when its hello address and context id agree (flagged legacy); an accepted level-0 deck never folds; another Patterns desk's hello and its twin-fed context fold into node:<inst> (TwinService.NodeContexts returns the instance, today dropped, Services/TwinService.Followers.cs:213-217; StagewatchFollow.cs:233-239); a conflict is one amber node — and only an i note when a verified link exists for that id. ShowBusView.Network computes Light, Mark, Source and words once as EyeApp fields, read by Eye.Build, ShowBusView.Peers (Gone is red today, StagewatchFollow.cs:247-253), ShowAppCard.Of (StagewatchFollow.cs:303-308), BriefLines and ShowBusRow — the one-projection fence (ShowBusTests.cs:329) extended to assert the same light and mark in all five for gone heard-only, gone needed, stuck, held and CERTIFICATE CHANGED. The highest-precedence source alone sets an edge's light and mark; a lower one adds an i note ('Stagewatch Hall A says: down'); only the two ends of one link show two words. Link edges carry the record verbatim (a stuck show: amber △ 'says its show is stuck'); a context's link items with an @<peer> row become edges and their other rows re-anchor; @<id> and @<id>/<item> resolve to item, else card, else count unplaced; counting kinds show '? — reason'. Bounds: relayed ≤ 256, cross-app ≤ 256, mentions ≤ 32 per context and ≤ 128 in all, 12 things plus ≤ 8 forced chain ends. NodeContextBuilder (ShowBusNodes.cs:108-160) stops mirroring Stagewatch into the node's own light: the relay becomes a link: item 'relayed by the desk'. FromEye exports this desk's nodes with short stable ids (no path or URL; Eye.cs:553, InputKeys.cs:6-17), link:<peer id> items with P2 rows and its own @ edges, never a borrowed item; the canary (swk_/ptk_/'/w/'/a path) runs over Patterns' own export too. The brief is bounded: own lines first, then needed apps, then counts, told and relayed lines grouped under 'X says'. The window chain's 'shown' comes from the air (P95.2's rule). Tests: EyeNetworkTests, SyntheticNetworkTests extended, ShowBusTests rewritten not skipped (:264, :306-325 incl. :311, :323, :324, :329-363), each named in the papers with 13.16 as the reason.

- **Sources:** Eye EYE.2; windows-for-peers PT-W3 (Eye chain, FromEye @ links); Eye critique issues 1, 2, 3, 8a, 9, 18, 19, 20
- **Gaps:** G24, G23, G35, G37
- **Depends on:** P97.6, P92.2, P95.2, P96.1, SW19.12
- **Validate:** `cd /home/user/Patterns && dotnet build Patterns.sln -c Debug -v q && dotnet test tests/Patterns.Core.Tests -c Debug --no-build --filter "FullyQualifiedName~Eye|FullyQualifiedName~SyntheticNetwork|FullyQualifiedName~ShowBus|FullyQualifiedName~ShowContext"`

### P98.2 — Patterns — Round 98.2: the feed — Apps from the hello, the Stagewatch link, node devices, accepted links and the twin

eye.md EYE.3: AppServices.Apps(now) composes the hello table (P91.4's bounded table), StagewatchLinkService (record, context, revision), DeviceService's node devices (record, context), ControlService's accepted links (P97.3's list) and the twin's node contexts; any kind may be linked (today only a `main` by typed host and port, StagewatchFollow.cs:212,256-262); EyeService.Gather builds Apps with context revisions; ContextJson uses the new export.

- **Sources:** Eye EYE.3
- **Gaps:** G35, G37, G24
- **Depends on:** P98.1
- **Validate:** `cd /home/user/Patterns && dotnet build Patterns.sln -c Debug -v q && dotnet test tests/Patterns.App.Tests -c Debug --no-build --filter "FullyQualifiedName~Eye|FullyQualifiedName~ShowBus"`

### P98.3 — Patterns — Round 98.3: every reader — canvas, side card, NetworkMenus in step on Eye, Nodes and deck, lens NETWORK, STATE, EYE JSON, the module

eye.md EYE.5 with the critique's fixes. Canvas: marks in geometry (P94.2), claims with a dash pattern of their own and a 'says' glyph (grey edges are already dashed, EyeCanvas.cs:196), hollow heard dots, edge hit-test on click and right-click. The side card's LINKS list (record words, flags, clock — volatile fields read live, never hashed). Core/Menus/NetworkMenus.cs: one LINK group shared by the Eye's app card and edges, the Nodes card and the deck bank — Link again now, Trust, Accept certificate (text-taking, ≥ 8 hex typed, never pre-filled, never bindable to a pad), Lower floor, Accept new id, Release held fact, Make key for…, Let this key speak for, Revoke key — each carrying P92.3's or P97.3's wire line and its Because when it does not apply; never proposed by the assistant. EYE LENS NETWORK; STATE eye appends apps, linksRed, linksAmber, network; bus.apps[] appends mark and link; EYE JSON appends node app, source, mark, collapsed, band and edge source, mark, claim, link (the record, null in replay JSON), top level apps, linkLights (not 'links', which clashes with counts.links), left, unplaced; EYE's link and record fields and eye.network served only to paired connections (P91.2). Companion eye_network, eye_links_worst, the eye_links feedback, the eye_lens choice 'network', an EYE page preset; test/lines.txt holds EYE LENS NETWORK. The beacon node's light by the same needed rule (Eye.cs:763). Tests: EyeNetworkAppTests with FakeStagewatch or the scripted peer (CERTIFICATE CHANGED → red edge and an Accept entry whose wire line equals the Nodes button's; the stuck beat → amber card, live amber edge, timer '?'; a forged hello for a pinned id → i note, no light change); DeskMenuAppTests (identical wire lines; every trust entry takes text where 13.8.3 asks); module tests.

- **Sources:** Eye EYE.5; Eye critique issues 4, 10, 12, 20
- **Gaps:** G24, G35
- **Depends on:** P98.2
- **Validate:** `cd /home/user/Patterns && dotnet build Patterns.sln -c Debug -v q && dotnet test tests/Patterns.Core.Tests -c Debug --no-build --filter "FullyQualifiedName~Eye|FullyQualifiedName~Menu" && dotnet test tests/Patterns.App.Tests -c Debug --no-build --filter "FullyQualifiedName~Eye|FullyQualifiedName~DeskMenuAppTests" && (cd integrations/companion-module-patterns && npm test)`

### P98.4 — Patterns — Round 98 papers (PLAN §116)

PLAN §116 (evidence: a handed-in critique — the Eye design and its review; the reversals named: a gone heard-only app grey, the stuck card amber, PlayMap in the NETWORK band); REVIEW 'Walk: none asked'; CHANGELOG; README; REMOTE (EYE LENS NETWORK, the EYE and STATE fields); COMPANION; docs/EYE.md §7 'The network: apps, links and claims'; OPEN rows (replay of link edges, L44, NDI across apps, AGMessage until it posts, the twin takeover); tag row 97. Closing sentence: the Super Check's 'Show network links' row read beside the NETWORK lens.

- **Sources:** Eye EYE.6 (its EYE.2–5 part)
- **Gaps:** G24
- **Depends on:** P98.3, owner:P-numbers
- **Validate:** `cd /home/user/Patterns && dotnet build Patterns.sln -c Debug -v q && for s in Core Rendering Devices Audio Assistant Audience App; do dotnet test tests/Patterns.$s.Tests -c Debug --no-build || exit 1; done && (cd integrations/companion-module-patterns && npm test)`

### P99.1 — Patterns — Round 99.1: CONTEXT POST — another app gives its context over a verified PeerLink

eye.md EYE.4 with the critique's fixes: `CONTEXT POST <json>` (RemoteCommandKind.ContextPost, catalogued, low, never in the context's commands) gated explicitly before the router (IsQuery's default-allow, ControlProtocol.cs:1454, would otherwise pass it unpaired): only on a LINK-verified connection with scope ≥ post, node.id equal to the verified id and node.t equal to the verified kind ('ERR this key speaks for …', never counted); level 0 refused ('posting a context needs PeerLink'); parsed and de-duplicated on the connection's reader task, the UI thread handed a reference only; PostedContexts latest wins, ≤ 128, kept 30 s against §14's posting cadence; the line within link.command.bytes (64 KiB); one journal row at first keep and one refusal summary a minute (13.19.4); one log line per state change; the Super Check's 'Show network links' names posted contexts with their age. Tests: kept for a verified post link; another id or kind refused and uncounted; level 0 refused; latest wins; expiry at 30 s; a flood at the WireLimits cap; ControlService.TrustLoopback = false restored in finally.

- **Sources:** Eye EYE.4; Eye critique issue 13
- **Gaps:** G37
- **Depends on:** P98.4, P97.3
- **Validate:** `cd /home/user/Patterns && dotnet build Patterns.sln -c Debug -v q && dotnet test tests/Patterns.Core.Tests -c Debug --no-build --filter "FullyQualifiedName~ControlProtocol" && dotnet test tests/Patterns.App.Tests -c Debug --no-build --filter "FullyQualifiedName~ContextPost|FullyQualifiedName~PeerLinkWire"`

### P99.2 — Patterns — Round 99 papers (PLAN §117)

PLAN §117; REVIEW 'Walk: none asked'; CHANGELOG; README; REMOTE (CONTEXT POST); COMPANION; OPEN; tag row 98. Closing sentence: CONTEXT POST's journal row and the Super Check's 'Show network links' row, read beside the NETWORK lens.

- **Sources:** Eye EYE.6 (its EYE.4 part)
- **Gaps:** G37
- **Depends on:** P99.1, owner:P-numbers
- **Validate:** `cd /home/user/Patterns && dotnet build Patterns.sln -c Debug -v q && for s in Core Rendering Devices Audio Assistant Audience App; do dotnet test tests/Patterns.$s.Tests -c Debug --no-build || exit 1; done && (cd integrations/companion-module-patterns && npm test)`

### SW24.1 — Stagewatch — Stagewatch publishes by P1–P8: one row per link to @<peer>, windows serve their holders; 2.24

eye.md §9.2 SW-EYE: src/Timer.Show/ShowContext.cs drops timer → every link and window (today 'follows'/'hears' rows, :126; G23); timer -feeds-> window:<name> only while it shows a page; window:<name> -serves-> @<holder id> per grant; each link item has one row -K-> @<peer id> (K the connector's primary use; playout: controls) with the scope in its words; accepted node links published as P2 items; clock:<peer id> per link with errMs; window items name their holder (a claim) or the browser device; no item restates a peer's thing (P1). ShowContextTests. Papers: REMOTE 2.24, WHATS_NEXT; Version 2.24.0.

- **Sources:** Eye SW-EYE
- **Gaps:** G23, G24
- **Depends on:** SW23.1, SW22.2, SW18.4
- **Validate:** `cd /home/user/Stagewatch && tools/test.sh && tools/test.sh interop`

### PM-11 — Mapper — Spine: the context names its peers and the windows it draws (P2/P3 rows)

eye.md PM-EYE.1 through the spine amendment lane for src/PlayMap.Engine/ContextBuilder.cs (a spine file): link:patterns -follows-> @<patterns id>; link:stagewatch -follows-> @<sw id> (one row per link, the scope in words); @<sw>/timer -feeds-> output:<id> for timer sources; @<sw>/window:<name> -feeds-> output:<id> for drawn grants (once PM-15 lands); playlist:<id> -feeds-> output:<id>; the air says a held blackout with its age (13.17). ContextShape.cs and EyeContextTests are not edited this round (not spine files, round3/PLAN.md:307-311): Patterns forces link items and chain ends whatever their position (P98.1). Tests in the Engine suite for the new rows.

- **Sources:** Eye PM-EYE.1; Eye critique issue 18
- **Gaps:** G24
- **Depends on:** SW19.12, owner:M-eye-spine
- **Validate:** `cd /home/user/Mapper && dotnet build PlayMap.sln -c Debug -v q && dotnet test tests/PlayMap.Engine.Tests -c Debug --no-build --filter "FullyQualifiedName~Context"`

### PM-12 — Mapper — wp4-peerlink: CONTEXT POST to Patterns, and its outcome in PlayMap's own STATE and context

eye.md PM-EYE.2 plus the critique: once the Patterns link is verified with scope post (PM-10), PlayMap posts its context to Patterns every 5 s; its own STATE links.patterns and its link:patterns item say 'Patterns keeps this context (N s ago)' or the refusal's words (attempts are not facts). SHOWBUS-NODE says what PlayMap publishes for the Eye.

- **Sources:** Eye PM-EYE.2; Eye critique (poster's outcome)
- **Gaps:** G37
- **Depends on:** PM-10, P99.1
- **Validate:** `cd /home/user/Mapper && dotnet build PlayMap.sln -c Debug -v q && dotnet test tests/PlayMap.Net.Tests -c Debug --no-build --filter "FullyQualifiedName~PatternsLink" && PLAYMAP_WRITE_FIXTURES=1 dotnet test tests/PlayMap.Engine.Tests -c Debug --no-build --filter "FullyQualifiedName~State" && dotnet test tests/PlayMap.Engine.Tests -c Debug --no-build`

## Train 8 — PlayMap after round 3's integration pass (the owner-gated remainder; and any earlier Mapper unit the owner did not let in)

**Goal.** What round 3's ownership keeps out of the waves lands in PlayMap's own process once the integration pass opens the files: the context's risk rows at the pass; a post-round-3 package for native windows and the timer's remaining fields; the typed address's desk probe; the assistant on Stagewatch's desk, the Patterns link's drive and the wire's reads after pairing — each only on the owner's answer. Any Mapper unit of Trains 1–7 whose owner answer did not come joins this train and its earlier train is validated without it (peers then see PlayMap at level 0, amber 'legacy', and the papers say so). Validated by both Mapper CI jobs and the interop rows for PlayMap's windows and PeerLink pairs.

### PM-13 — Mapper — At the integration pass: derived risk rows, the secret field, risk-examples.json

cross-app-proposals PM-0 inside round3/PLAN.md §5.6 item 7 (Core/Bus/ShowContext.cs is the integration pass's file): derived RiskWhen/HighFor rows, the `secret` field, risk-examples.json, ContextRiskRowsTests; ContextOrderTests keep the air's verbs first within 256.

- **Sources:** cross-app-proposals PM-0
- **Gaps:** G02
- **Depends on:** SW21.1
- **Validate:** `cd /home/user/Mapper && dotnet build PlayMap.sln -c Debug -v q && dotnet test tests/PlayMap.Core.Tests -c Debug --no-build --filter "FullyQualifiedName~ShowContext|FullyQualifiedName~ContextRisk|FullyQualifiedName~ContextOrder"`

### PM-14 — Mapper — wp4-showbus-windows: the timer source draws everything Stagewatch sends; the desk and the assistant read it

windows-for-peers PM-W2 (= patterns-playmap-side PM-P2), after PM-7's air half: the G12 fields carried and drawn — the watermark always, holding (text or image, fetched), flash at Stagewatch's rates from leader time, urgency, next, show{}; the desk's StateSnapshot and the assistant's StateExplainer readers (wave-owned files until the pass); fixtures; redirect words retired (the view choice is a window grant).

- **Sources:** windows-for-peers PM-W2; patterns-playmap-side PM-P2
- **Gaps:** G12
- **Depends on:** PM-7, PM-5, owner:M-windows-package
- **Validate:** `cd /home/user/Mapper && (Xvfb :99 &) && export DISPLAY=:99 && dotnet build PlayMap.sln -c Debug -v q && dotnet test tests/PlayMap.Net.Tests -c Debug --no-build --filter "FullyQualifiedName~StagewatchTimer" && dotnet test tests/PlayMap.Render.Tests -c Debug --no-build --filter "FullyQualifiedName~Timer" && PLAYMAP_WRITE_FIXTURES=1 dotnet test tests/PlayMap.Engine.Tests -c Debug --no-build --filter "FullyQualifiedName~State" && dotnet test tests/PlayMap.App.Tests -c Debug --no-build`

### PM-15 — Mapper — wp4-showbus-windows: native windows — PlayMap draws a granted Stagewatch window, black when unknown

windows-for-peers PM-W3: spine commits (SourceKind.Window, catalogue words, StateWriter, ContextBuilder); the window-draw cap and can=; the link messages on the verified link; the loopback dial; NativeWindows (fail-closed, the loss state when the link is down, persisted HeldWindows, flag honesty); the window-identify overlay; the STATE block and its cards; the @ context link; desk menus.

- **Sources:** windows-for-peers PM-W3
- **Gaps:** G11
- **Depends on:** PM-14, SW22.4, owner:M-windows-package, owner:S-D0
- **Validate:** `cd /home/user/Mapper && (Xvfb :99 &) && export DISPLAY=:99 && dotnet build PlayMap.sln -c Debug -v q && dotnet test tests/PlayMap.Net.Tests -c Debug --no-build && PLAYMAP_WRITE_FIXTURES=1 dotnet test tests/PlayMap.Engine.Tests -c Debug --no-build --filter "FullyQualifiedName~State" && dotnet test tests/PlayMap.Engine.Tests -c Debug --no-build && dotnet test tests/PlayMap.Render.Tests -c Debug --no-build --filter "FullyQualifiedName~Window"`

### PM-16 — Mapper — The typed Stagewatch address: re-resolved, the desk probed once a minute, a 403 named

patterns-playmap-side PM-P1's remainder (PM-2 did TLS first, PM-5 the honest paired): a typed address re-resolved to node:<id> after the first verified link (13.4.3); the approval desk probed once a minute as AGMessage does; a 403 named 'requires wss'.

- **Sources:** patterns-playmap-side PM-P1
- **Gaps:** G31
- **Depends on:** PM-5
- **Validate:** `cd /home/user/Mapper && dotnet build PlayMap.sln -c Debug -v q && dotnet test tests/PlayMap.Net.Tests -c Debug --no-build --filter "FullyQualifiedName~StagewatchLink" && dotnet test tests/PlayMap.Engine.Tests -c Debug --no-build --filter "FullyQualifiedName~NetworkHost"`

### PM-17 — Mapper — PlayMap's assistant on Stagewatch's approval desk

cross-app-proposals PM-1 (= patterns-playmap-side PM-P3), after W4 (EngineCore.Assistant.cs is workflows') and the owner's answer to round 2 decision 7: NetworkHost publishes the desk, the sink collects with CollectApproved=false so StagewatchLink stays the one collector, the link's pull pauses, orphan words, ASSISTANT SET desk, STATE assistant.desk with its fixture and card, the NetworkDrawer door, verbs.json regenerated.

- **Sources:** cross-app-proposals PM-1; patterns-playmap-side PM-P3
- **Gaps:** G13
- **Depends on:** SW21.2, PM-13, owner:M-decision7
- **Validate:** `cd /home/user/Mapper && dotnet build PlayMap.sln -c Debug -v q && dotnet test tests/PlayMap.Assistant.Tests -c Debug --no-build && PLAYMAP_WRITE_FIXTURES=1 dotnet test tests/PlayMap.Engine.Tests -c Debug --no-build --filter "FullyQualifiedName~State" && dotnet test tests/PlayMap.Engine.Tests -c Debug --no-build && (cd integrations/companion-module-playmap && npm test)`

### PM-18 — Mapper — The Patterns link drives: a Patterns chase source and LINK PATTERNS SEND, with receipts

patterns-playmap-side PM-P4 (designed minimal): an option to slave a playlist to Patterns' video row (seq, positionMs); LINK PATTERNS SEND for operator-sent verbs with receipts read by 13.12.3; the Show Bus node contract test. Frame-accurate following (ANCHOR) is not built.

- **Sources:** patterns-playmap-side PM-P4
- **Gaps:** G36
- **Depends on:** PM-10, owner:M-G36
- **Validate:** `cd /home/user/Mapper && dotnet build PlayMap.sln -c Debug -v q && dotnet test tests/PlayMap.Net.Tests -c Debug --no-build --filter "FullyQualifiedName~PatternsLink" && dotnet test tests/PlayMap.Engine.Tests -c Debug --no-build`

### PM-19 — Mapper — The wire's reads after pairing on a token node (9941)

spec-1-3 M2, only on the owner's yes against decision 17: TCP 9941 pushes STATE on HELLO before pairing today (src/PlayMap.Net/Wire/WireSession.cs:263-276); on a token node, STATE and CONTEXT wait for pairing, as Patterns does after P91.2 — after Patterns' node devices pair by proof (P96.1).

- **Sources:** spec-1-3 M2
- **Gaps:** G45
- **Depends on:** PM-4, P96.1, owner:M-decision17
- **Validate:** `cd /home/user/Mapper && dotnet build PlayMap.sln -c Debug -v q && dotnet test tests/PlayMap.Net.Tests -c Debug --no-build --filter "FullyQualifiedName~Wire"`

### PM-20 — Mapper — PlayMap's papers for the post-round-3 work

windows-for-peers PM-W4 and cross-app-proposals PM-2: SHOWBUS-NODE §2/§3/§5 (windows, the PeerLink acceptor, what PlayMap publishes for the Eye), WIRE, ROADMAP (L10, the FFmpeg TLS probe), CARRYOVER (G13's corrected reason), README, UX.md, the Companion module's fixtures regenerated.

- **Sources:** windows-for-peers PM-W4; cross-app-proposals PM-2
- **Gaps:** G11, G12, G13
- **Depends on:** PM-14, PM-15
- **Validate:** `cd /home/user/Mapper && dotnet build PlayMap.sln -c Debug -v q && (cd integrations/companion-module-playmap && npm test)`

### SW-INT-PM — Stagewatch — Interop: PlayMap's windows rows and PlayMap's PeerLink pairs

ecosystem-lane SW-R6's PlayMap half (S5.2, S5.3) and the profile's cross-app rows for PlayMap's pairs ('Stagewatch links to PlayMap over line: verified both ways, pinned, floor set'; PlayMap's links to Stagewatch and Patterns), at bumped pins; only rows that pass.

- **Sources:** ecosystem-lane SW-R6; PeerLink profile conformance (cross-app rows)
- **Gaps:** G44, G11
- **Depends on:** PM-15, PM-10, SW22.11
- **Validate:** `cd /home/user/Stagewatch && tools/test.sh build interop`

## Train 9 — AGMessage on its 0.5.0 branch (only what AGMessage's 0.5.0 branch at 9d7f5c3 has not done)

**Goal.** AGMessage's links keep the same profile: Patterns' and PlayMap's operator token never in clear and never driving them; results that run once and arrive; Stagewatch's proposals read in their real shape; an honest Stagewatch link (node id, verdict, holds, stuck, pins only on an authenticated answer); Show Bus 1.3; then LINK to Stagewatch, a line connector with a scoped key to Patterns and PlayMap, and its context in Patterns' Eye by CONTEXT POST. Built on the branch's head, which already fixed the stage line (`… <urgency> off ack`, app/lib/stagelines.js:8-19), hears and names PlayMap (app/lib/nodes.js:40), bounds the hello table (nodes.js:36-37), keys runs by desk, id and created and retries reports each tick (app/server.js:2012-2024), never speaks plain to a pinned node (app/lib/peerlinks.js:259-261), and reads Patterns' {ok,msg,id} answer with a fake that answers it (peerlinks.js:201-213; test/helpers.js:152-178). Validated by npm test and npm run check in app/, the peerlink vectors and the scripted peer in-process, and the interop rows for AGMessage at its new pin.

### AG-1 — AGMessage — Patterns' and PlayMap's operator token never in clear and never driving them (before 0.5.0 ships)

The profile's AG-PL0 on the branch head. The token header (X-Patterns-Token / X-PlayMap-Token, app/lib/peerlinks.js:292) still goes over plain HTTP to a node that is neither pinned nor announcing a certificate (_wantsTls, :259-261; poll, :294). Now: only to a loopback socket address, or over TLS to a certificate already pinned or confirmed by a person (the admin types ≥ 8 hex beside the short fingerprint, 13.6.8); otherwise the link is 'refused' with 'this node offers no TLS — AGMessage will not send its pairing token in clear'. The direct command path (send(), :346-356; app/server.js:1183-1204) is off while the only credential is the operator token: a line goes to the hall's Stagewatch desk as a §11 proposal, or with no desk is refused 'Patterns gives AGMessage no key of its own yet' (agmessage decision 10). A written command with no answer is unconfirmed, never 'not sent' (send()'s catch, :355). The admin page says, amber, that AGMessage holds an operator token until an app key replaces it. Tests (node:test): a fake Patterns behind an injected local-address predicate never sees the token on plain; a timeout after the write is unconfirmed; the send path proposes instead; secrets scan clean.

- **Sources:** PeerLink profile AG-PL0
- **Gaps:** G08
- **Depends on:** owner:A-hold-050, owner:A-direct-path
- **Validate:** `cd /home/user/AGMessage && git switch -c peerlink-hygiene AGMessage's 0.5.0 branch && cd app && npm ci --no-audit --no-fund && npm test && npm run check`

### AG-2 — AGMessage — The desk's proposals read as Stagewatch sends them; the shared fake in ToJson's shape

agmessage AG-2's remainder: readProposal takes `ok` only from a result object (app/lib/nodes.js:206), so Stagewatch's top-level ok with a string result reads null (G17): read the top-level ok and words as Stagewatch's ToJson sends them, plus created and collected (cross-app SW-2 appends collected); the shared fake Stagewatch in test/helpers.js answers that shape with a key-matched §6 context; the stage proposal shows the risk the desk stored (G05's remainder, if not already shown).

- **Sources:** agmessage AG-2; agmessage AG-1 (remainder)
- **Gaps:** G17, G05
- **Depends on:** AG-1
- **Validate:** `cd /home/user/AGMessage/app && npm ci --no-audit --no-fund && npm test && npm run check`

### AG-3 — AGMessage — Each approved line runs at most once across restarts, and its result arrives

agmessage AG-3's remainder: runs are keyed in memory today and dropped past 200 (app/server.js:2012-2015) and reports retry each tick only while that link lives (:2020-2024). Now: each run recorded on disk before it runs (desk-runs.json through WriteLane, read with readJsonWithBak) and refused if the record does not land; tombstones; reports retried 3→30 s ±20 % for 30 minutes after reading the proposal back and posting only onto the one it ran; a 409 'settled' counts as delivered (13.12.9); intake stops at 50; a lost collect reply recovered from GET /api/proposals. Tests with a restarting fake desk.

- **Sources:** agmessage AG-3
- **Gaps:** G16
- **Depends on:** AG-2
- **Validate:** `cd /home/user/AGMessage/app && npm ci --no-audit --no-fund && npm test && npm run check`

### AG-4 — AGMessage — Show Bus 1.3: the copy and the kit, certificates 825 days, the hello and its reader

spec-1-3 A1, agmessage AG-4's rest and windows-for-peers AG-W1: docs/SHOWBUS.md replaced by the byte-identical 1.3 master and tools/showbus/peerlink-peer.mjs copied, held by showbus.test.js (body hash, ```showbus, ```peerlink, the peer's hash); the certificate 825 days in all (825 days plus 1 hour today) with the one renewal; proto 3, inst; caps read to 32 (20 today, nodes.js:58) and names to 64; any kind matching [A-Za-z0-9_-]{1,16} heard (a fixed set today, nodes.js:40; G18); the table at §14's peer bound with 8 per address kept; the hello sent from HOST; every address per id, sticky dial, conflict; find on any address; NODES.md's ping wording (G42).

- **Sources:** spec-1-3 A1; agmessage AG-4; windows-for-peers AG-W1
- **Gaps:** G18, G38, G39, G40, G42, G22
- **Depends on:** AG-1, SW19.12
- **Validate:** `cd /home/user/AGMessage/app && npm ci --no-audit --no-fund && npm test && npm run check && bash /home/user/Stagewatch/tools/showbus-copies.sh`

### AG-5 — AGMessage — An honest Stagewatch link on a pure PeerLink core: node id, verdict, holds, stuck, pins only on an authenticated answer

agmessage AG-5, peers-not-screens AG-1/AG-2 and the profile's AG-PL1 in one unit. New app/lib/peerlink.js (pure) and app/test/peerlink.test.js reading the ```peerlink block from docs/SHOWBUS.md (after AG-4's copy; the scripted peer imported in process). StagewatchLink dials '/ws?cid=agmessage' (nodes.js:386; G32): now node=<id>&t=messenger&cid=<id>; 'connected' is set on open and the pin confirmed on any state frame (:392, :399; G43): now only on the verdict or a key-matched /api/context 2xx; one door for 401/429 with a persisted hold, Link again now, disconnect held, a closed set of statuses, refusal words to admins only; TLS by the pin and a probe, never by the hello; no key plain off this machine; the legacy-bearer confirm; the delay reset only after verified + 10 s (today on open, :392); a pong never clears stuck (every non-beat message refreshes today, :397) and the halls board shows '?' and 'Stagewatch's show is stuck'; performance.now() for liveness; ids ag<n>-<inst>; unconfirmed after the write; matched pongs; injected time and j in tests.

- **Sources:** agmessage AG-5; peers-not-screens AG-1; peers-not-screens AG-2; PeerLink profile AG-PL1
- **Gaps:** G32, G43
- **Depends on:** AG-2, AG-4
- **Validate:** `cd /home/user/AGMessage/app && npm ci --no-audit --no-fund && npm test && npm run check && npm run smoke`

### AG-6 — AGMessage — Stagewatch's attention items, windows and commands in the brief and the admin page

agmessage AG-6: showBrief and the admin page show Stagewatch's red and amber items and every window with a shape mark beside each colour; window actions from Stagewatch's listed rows; the admin's fenced desk proposals with Stagewatch's risk shown before and after, followed with one ?from= and persisted.

- **Sources:** agmessage AG-6
- **Gaps:** G19
- **Depends on:** AG-3, AG-5
- **Validate:** `cd /home/user/AGMessage/app && npm ci --no-audit --no-fund && npm test && npm run check && npm run smoke`

### AG-7 — AGMessage — LINK to Stagewatch: proofs, the session credential on side calls, floors

The profile's AG-PL2: link=1 LINK with a swk_ key on Stagewatch's /ws (SW22.1), nothing taken before the acceptor's proof, pinned after it, floors per id, Authorization: ShowBus lid, s on side calls only to the pinned certificate; ws-connector scenarios S1, S2, S4–S7, S10–S14, S17–S19.

- **Sources:** PeerLink profile AG-PL2
- **Gaps:** G43, G08
- **Depends on:** AG-5, SW22.12
- **Validate:** `cd /home/user/AGMessage/app && npm ci --no-audit --no-fund && npm test && npm run check`

### AG-8 — AGMessage — A line connector to Patterns and PlayMap with a scoped key; no token anywhere

The profile's AG-PL3: PeerLink becomes a line connector — node:net/tls to 9697 or 9941, or playmap.lines.v1 over pinned WSS — proving an issued key (scope follow, or drive where the node's operator grants it); no token in any header and none stored (the stored operator token dropped); low and review verbs the node lists and the scope allows go with receipts read by 13.12.3, high verbs only proposed; CONTEXT every 5 s after LINK; STATE lines counted and dropped unparsed. Line-connector scenarios S1, S2, S4–S7, S11–S14, S17–S19, S22.

- **Sources:** PeerLink profile AG-PL3
- **Gaps:** G08, G37
- **Depends on:** AG-7, P97.6
- **Validate:** `cd /home/user/AGMessage/app && npm ci --no-audit --no-fund && npm test && npm run check`

### AG-9 — AGMessage — AGMessage publishes for the Eye: displays, peer links, one row per link, chain ends

eye.md AG-EYE.1 (the context today has no displays although Displays.status exists, app/lib/displays.js:165-173): display:<id> items (kind screen, the display's name ≤ 60 per the owner's answer, kind words only — never message text, file names, hall names or codes; green shown, amber sending/error/offline-after-shown, grey never opened); link:patterns:<id> and link:playmap:<id> items; rows link:stagewatch[:n] -follows-> @<sw id>, @<sw id>/timer -feeds-> display:<id> while it shows that hall's countdown, one row per peer link; the comment at app/lib/context.js:3 corrected (G37). showbus.test.js with canary texts; resilience.test.js's toaster assertion changed.

- **Sources:** Eye AG-EYE.1
- **Gaps:** G37, G24
- **Depends on:** AG-4, owner:A-display-names
- **Validate:** `cd /home/user/AGMessage/app && npm ci --no-audit --no-fund && npm test && npm run check`

### AG-10 — AGMessage — CONTEXT POST to Patterns and PlayMap, and the outcome shown on AGMessage's own side

eye.md AG-EYE.2 plus the critique: over AG-8's verified line link with scope post, AGMessage posts its context; the halls board, the admin page and its own context say 'Patterns keeps this context (N s ago)' or the refusal's words.

- **Sources:** Eye AG-EYE.2; Eye critique (poster's outcome)
- **Gaps:** G37
- **Depends on:** AG-8, AG-9, P99.2
- **Validate:** `cd /home/user/AGMessage/app && npm ci --no-audit --no-fund && npm test && npm run check`

### AG-11 — AGMessage — Papers and releases

agmessage AG-7: CHANGELOG entries with 'Tests: N (was M)' (AG-1 inside 0.5.0 if it is not yet tagged; AG-2…AG-6 as 0.5.1; AG-7…AG-10 as 0.6.0), app/package.json and the version scheme (app/lib/version.js), README, docs/NODES.md rows, the context-token text and comments corrected (what Show Bus §8 delivers).

- **Sources:** agmessage AG-7; peers-not-screens AG-3
- **Gaps:** G37, G42
- **Depends on:** AG-6, AG-10, owner:A-versions
- **Validate:** `cd /home/user/AGMessage/app && npm ci --no-audit --no-fund && npm test && npm run check && npm run smoke`

### SW-INT-AG — Stagewatch — Interop: AGMessage's rows

agmessage SW-AG-2 and ecosystem-lane SW-R8, plus SW-R1's and SW-R2's AGMessage rows: S1.3b (AGMessage hears PlayMap), S2.8, S2.9, S3.9, S6.2c (AGMessage sees a stuck Stagewatch), S6.5 reworded (no re-offer), S7.4 (no app's address counted), the desk rows for AGMessage, and the PeerLink pair rows for AGMessage's ws and line links, at AGMessage's new pin.

- **Sources:** agmessage SW-AG-2; ecosystem-lane SW-R8; ecosystem-lane SW-R1/SW-R2 (AGMessage rows)
- **Gaps:** G44, G16, G18
- **Depends on:** AG-5, AG-3, AG-8, SW21.9
- **Validate:** `cd /home/user/Stagewatch && tools/test.sh build interop`

## Owner questions

- **Patterns** — [P-numbers] Take rounds 89–99 and PLAN §107–§117 as this plan numbers them, with the bench host as Round 90?
  - Why: Five designs numbered the same work 89, 90, 91, 92, 'N', 'NN', 'PL' and 'EYE'; the CHANGELOG headers, ChangelogTests, the PLAN sections and the tag table need one consecutive sequence, and the maintainer's numbering wins.
  - Recommended: Yes, as in the numbering field; renumber freely later only by shifting whole rounds.
  - Blocks: P89.5, P90.2, P91.5, P92.5, P93.3, P94.3, P95.4, P96.2, P97.6, P98.4, P99.2
- **Patterns** — [P-ADR016] ADR-016: the wire shows its state only to a paired connection — scope A (NAV, MENU, CUE LIST, ARCADE …, ASSISTANT … stay open and named in §8) or scope B (every query pairs first)?
  - Why: 9697 greets every connection with STATE before NONCE (ControlService.cs:356-357) and IsQuery lets EYE, CONTEXT and every non-Action verb through unpaired (ControlProtocol.cs:1454), so the venue network reads the desk — and, once the Eye carries link records and other apps' contexts, the room's topology. Scope B breaks Patterns' own cross-machine asks (arcade nodes, a node's moderation ask) until they pair by proof.
  - Recommended: Scope A: STATE, EYE, CONTEXT and SUBSCRIBE pair first; the five named queries stay open and are listed in §8 with what ends them.
  - Blocks: P91.2, P91.5, P98.3
- **Patterns** — [P-twin-secrets] Turn the twin's SendSecrets off by default and refuse it on a plain twin link to another machine?
  - Why: Device passwords and kept window passes ride the plain-TCP twin by default (TwinConfig _sendSecrets = true, Patterns.Core/Model/ShowState.cs:2080) and the twin listens on every interface whatever the bind address (TwinService.cs:488,512). The cost: after a takeover a standby lacks device passwords and draws kept pages black until typed there (OPEN L60).
  - Recommended: Yes, with words on both desks and a QUALIFICATION §22 walk row; twin TLS later.
  - Blocks: P92.4
- **Patterns** — [P-scopes] Which Patterns verbs does each app-key scope reach (follow, post, drive, operate)?
  - Why: Round 97 issues ptk_ keys; the acceptor enforces the scope and §8's row names the map (13.5.3). A wrong 'drive' set would let a playout desk change outputs or settings.
  - Recommended: follow = STATE, CONTEXT, SUBSCRIBE, EYE; post = follow + CONTEXT POST and its own proposals and results; drive = post + VIDEO PLAY/PAUSE/SEEK/STOP/END, CUE GO/STANDBY, PLAYLIST transport, STAGE MESSAGE; operate = every verb (the pairing token). Never OUTPUTS, TAKE/CUT, SCREEN or settings below operate. 1.3's §8 says 'keys from Round 97; the map in REMOTE.md' so the spec does not wait.
  - Blocks: P97.3
- **Patterns** — [P-OSC12h] End the 12-hour OSC trust an address earns from a wire or web proof (ADR)?
  - Why: A proof today trusts the address for OSC for 12 h (ControlService.cs:556-560, PairedAddressKept; :439, :978-981); 13.18 says a proof grants nothing to an address, and on a shared network an address is not an identity.
  - Recommended: Yes: OSC on a paired desk only from the OSC ALLOW list and loopback.
  - Blocks: P97.5
- **Stagewatch** — [S-sections] Put PeerLink at §13 and the numbers at §14 of the 1.3 master?
  - Why: spec-1-3 put the numbers at §13; the PeerLink profile is written as §13. Either works; the master's owner chooses before SW19.1 is written (23 references in unlanded designs follow).
  - Recommended: §13 PeerLink, §14 numbers.
  - Blocks: SW19.1
- **Stagewatch** — [S-D0] D0: may a window grant's page address ride exactly two carriers — a browser's address bar and the one verified link to the grant's own app (TLS or local) — with the doctrine line in CLAUDE.md:38-39 reworded by you?
  - Why: §1 rule 5 and the doctrine say a window address rides in no frame; delivering a grant to Patterns or PlayMap needs a named exemption written before the wire does it (windows-for-peers D0).
  - Recommended: Yes, as worded in windows-for-peers D0 with 'verified PeerLink (or key-verified node link)'; the CLAUDE.md line written by the maintainer.
  - Blocks: SW19.12, SW22.4, PM-15
- **Stagewatch** — [S-interop-gate] Let Stagewatch's release job wait for the interop job at the siblings' pinned commits?
  - Why: The interop lane is the only cross-app proof (G44); a release that ignores it can ship a contract break. It pins Patterns, PlayMap and AGMessage commits, so a sibling's red head never blocks — only a pinned row that fails.
  - Recommended: Yes; the nightly heads run reports drift only.
  - Blocks: SW19.10
- **Stagewatch** — [S-exemptions] Keep ?pin= in browser addresses and Companion TCP 9923's unauthenticated door (when bound to the network) as named §8 exemptions in 1.3?
  - Why: spec-1-3 lists both as 'not scheduled'; 1.3 final needs the wording, and the health lights must say what they leave open.
  - Recommended: Keep them named in 1.3; schedule 9923's authentication with Show Bus 1.4's default flip.
  - Blocks: SW19.12
- **Mapper** — [M-PMX] PM-X: may the Show Bus safety and PeerLink fixes to files no round-3 wave owns land during round 3 as named round-2 fix batches (src/PlayMap.Net/Links/**, src/PlayMap.Net/Wire/**, src/PlayMap.Engine/Host/NetworkHost.cs, src/PlayMap.Core/Bus/{Pairing,ShowBus}.cs, docs/SHOWBUS-NODE.md, a new docs/SHOWBUS.md and tools/showbus/), with spine lines only through §5.7?
  - Why: round3/PLAN.md §4 says 'a folder not named for a wave keeps no owner in it, and nothing in that wave edits it' (:2121-2125). Without leave, PlayMap keeps flashing projectors on any Stagewatch's identify (NetworkHost.cs:783), sends its Stagewatch key plain to typed addresses, follows Patterns' blackout from whoever answers first, and stays 'legacy' to every peer until the integration pass. Fix batches already merge during round 3 (the fix-*-win batches).
  - Recommended: Grant it for PM-1, PM-2, PM-4–PM-8, each a named batch with a CARRYOVER entry, merged between wave merges.
  - Blocks: PM-1, PM-2, PM-3, PM-4, PM-5, PM-6, PM-7, PM-8
- **Mapper** — [M-content-before-W3] May fix-media-page-import edit src/PlayMap.Engine/EngineCore.Content.cs before W3 starts (desk-map owns it in W3, canvas in W4)?
  - Why: PlayMap imports a page it cannot draw and keeps a window pass in the project (G47); the refusal lives in EngineCore.Content.cs.
  - Recommended: Yes, before W3 begins; otherwise at the integration pass.
  - Blocks: PM-3
- **Mapper** — [M-wp4-peerlink] Open a post-round-3 package wp4-peerlink for PlayMap as a PeerLink acceptor and connector (VerbCatalog rows LINK/RECEIPTS/KEY…, STATE clients and links records with fixtures and card descriptors, a pmk_ table in NodeCredentialStore with scopes mapped to ShowPolicy roles, TLS sniffed on 9941)?
  - Why: These touch spine-owned VerbCatalog, ShowPolicy and STATE fixtures; mid-round they would need the amendment lane for every row. Until it lands, peers see PlayMap at level 0 and Stagewatch cannot time a GO to PlayMap over a proved link.
  - Recommended: Yes, first after the integration pass.
  - Blocks: PM-9, PM-10, PM-12
- **Mapper** — [M-trusted-addresses] Keep TrustedAddresses only for legacy Lines devices (amber 'trusted by address — not a proof') and never let an app link pair by address?
  - Why: Trusted addresses pair without proof today (Core/Bus/Pairing.cs:113-115); on the venue network an address is not an identity, and a PeerLink proof grants nothing to an address (13.18).
  - Recommended: Yes.
  - Blocks: PM-9
- **Mapper** — [M-eye-spine] Route the Eye's context rows through the spine amendment lane ('Spine: the context names its peers and the windows it draws' in ContextBuilder.cs) and leave ContextShape.cs and EyeContextTests untouched this round?
  - Why: ContextBuilder.cs is a spine file; ContextShape.cs and EyeContextTests are not (round3/PLAN.md:307-311), so the lane cannot carry them; Patterns forces link items and chain ends whatever their position instead.
  - Recommended: Yes.
  - Blocks: PM-11
- **Mapper** — [M-decision7] Round 2 decision 7: may PlayMap's assistant use Stagewatch's approval desk (forwarding on, the link staying the one collector)?
  - Why: G13 is designed (cross-app-proposals PM-1) but parked on this decision (docs/round2/CARRYOVER.md:99) and on W4's EngineCore.Assistant.cs.
  - Recommended: Yes after W4, with CollectApproved=false so StagewatchLink stays the one collector.
  - Blocks: PM-17
- **Mapper** — [M-decision17] Revisit decision 17: on a node with a token, may TCP 9941's STATE greeting and CONTEXT wait for pairing once Patterns' node devices pair by proof (Round 96)?
  - Why: 9941 pushes STATE on HELLO before pairing (WireSession.cs:263-276); the owner kept Patterns' read 'as today' (round3/PLAN.md §9 decision 17). Show Bus 1.3 §1 rule 4 says show state travels only after pairing, listing this as PlayMap's exemption until then.
  - Recommended: Yes, after Round 96.
  - Blocks: PM-19
- **Mapper** — [M-G36] May PlayMap's Patterns link drive (a Patterns chase source, LINK PATTERNS SEND)?
  - Why: G36 is designed minimal; no ask from the room yet, and frame-accurate following needs an ANCHOR line nobody builds.
  - Recommended: Defer until an ask from the room; keep the design.
  - Blocks: PM-18
- **Mapper** — [M-windows-package] Open a post-round-3 package wp4-showbus-windows for native windows and the timer's remaining fields?
  - Why: G11 and G12's rest need SourceKind.Window, catalogue words, StateWriter and ContextBuilder (spine) plus wave-owned desk and assistant readers.
  - Recommended: Yes, after wp4-peerlink or beside it.
  - Blocks: PM-14, PM-15
- **AGMessage** — [A-hold-050] Hold the 0.5.0 tag until AG-1 lands on the branch?
  - Why: The 0.5.0 branch sends Patterns' and PlayMap's full-control pairing token in a header over plain HTTP to any node not pinned and not announcing a certificate (app/lib/peerlinks.js:259-261,292,294), and drives them with it — a ship-blocking leak, although AGMessage's train is last in this plan.
  - Recommended: Yes: AG-1 goes into 0.5.0; the rest of Train 9 follows as 0.5.1 and 0.6.0.
  - Blocks: AG-1
- **AGMessage** — [A-direct-path] Accept losing the direct send path to Patterns and PlayMap until they issue AGMessage a scoped key (Rounds 97 / wp4-peerlink)?
  - Why: agmessage decision 10: the pairing token is too strong for a messenger. Meanwhile lines go to Stagewatch's desk as proposals.
  - Recommended: Yes.
  - Blocks: AG-1, AG-8
- **AGMessage** — [A-display-names] May display names appear in AGMessage's Show Bus context?
  - Why: The Eye draws AGMessage's displays (AG-9); a display name may name a place; the context's privacy rule keeps message text, file names, hall names and codes out.
  - Recommended: Publish the admin-set display name (≤ 60) with a per-display 'keep private' switch that falls back to 'Screen N'.
  - Blocks: AG-9
- **AGMessage** — [A-versions] Version the remaining work as 0.5.1 (AG-2…AG-6 and the 1.3 copy) and 0.6.0 (LINK, the line connector, the Eye)?
  - Why: spec-1-3 planned a 0.4.1 that 0.5.0 overtook; the CHANGELOG and app/lib/version.js need the numbers.
  - Recommended: Yes.
  - Blocks: AG-11

## Gap map

| gap | units | left, and why |
|---|---|---|
| G01 | SW20.1, SW20.2, SW20.3, SW20.4, SW20.5, SW20.11, SW20.12, SW20.13, SW20.14 |  |
| G02 | SW21.1, SW21.2, PM-13 |  |
| G03 | SW21.2, P91.3 |  |
| G04 | P89.1, SW18.6, SW19.9, P89.5 |  |
| G05 | SW21.2, AG-2 | The line itself is already fixed on AGMessage's 0.5.0 branch (app/lib/stagelines.js:8-19; its CHANGELOG 'Fixed: 0.4 proposed …'); what remains is Stagewatch's fence on peers' documented lines and the stored risk shown. |
| G06 | SW18.3, SW18.4, SW18.6, PM-1, PM-5, SW18.8 |  |
| G07 | SW20.4, SW20.5, P97.4, SW20.13, SW20.14 |  |
| G08 | SW18.7, P89.4, PM-2, SW20.4, SW20.6, P92.1, P95.1, P97.1, P97.2, P97.3, SW23.1, PM-9, PM-10, AG-1, AG-7, AG-8, P89.5, P92.2, PM-6, P97.5, P97.6, SW23.2 | Decks on Patterns' and PlayMap's wires keep the legacy proof on plain TCP until their modules adopt LINK (13.18); a pairing-code proof over a plain carrier stays an offline guessing target until each acceptor offers TLS (flag code-plain). |
| G09 | P89.2, P89.3, P91.2, P92.4, P89.5 | Kept passes rest in per-machine JSON like a box's password (OPEN L40) until DPAPI; the twin is plain TCP (L36). |
| G10 | SW22.3, SW22.4, SW22.6, P95.2, P95.3, SW22.11, P95.4 |  |
| G11 | SW22.4, PM-15, SW-INT-PM, SW22.6, PM-20 |  |
| G12 | PM-7, PM-14, PM-20 |  |
| G13 | PM-17, PM-20 | Waits for the owner's round 2 decision 7 and W4. |
| G14 | SW21.3, SW21.4, SW21.5, SW21.6, SW22.8, SW22.9, SW22.10, SW22.12, SW21.10 |  |
| G15 | SW18.1, SW18.2, SW18.5, SW22.1, SW18.6, SW18.8 | A Stagewatch with no PIN still accepts a context for an unbound id from anyone on the LAN — labelled unverified, never setting risk; tying an id to an address was rejected (DHCP, two-NIC laptops). |
| G16 | AG-3, P92.2, PM-6, SW-INT-AG | AGMessage 0.5.0 already keys runs by desk, id and created and retries reports each tick (app/server.js:2012-2024); persistence, read-back and the recovery read remain (AG-3). |
| G17 | AG-2, SW19.1 |  |
| G18 | AG-4, SW-INT-AG | PlayMap's hello is already heard and named on the 0.5.0 branch (app/lib/nodes.js:40); hearing any well-formed kind remains. |
| G19 | AG-6 | No picker beyond the stage message and no AGMessage AI proposals: no ask from the room. |
| G20 | SW21.4, SW21.5, SW21.6, SW22.8, SW21.10 |  |
| G21 | P93.1, SW20.10, P93.3 | A forwarded speaker ACK can be refused under Stagewatch's crew-key policy and Patterns shows the refusal; Patterns' messages carry no urgency. |
| G22 | SW19.1, SW19.6, SW22.2, SW22.5, SW22.7, SW22.12, P91.5, PM-4, AG-4 |  |
| G23 | SW22.2, SW24.1, P98.1 |  |
| G24 | SW22.2, SW22.5, P94.1, P98.1, P98.3, SW24.1, PM-11, AG-9, P94.2, P94.3, SW22.6, P98.2, P98.4 |  |
| G25 | SW20.9, PM-8 |  |
| G26 | SW20.3, SW20.4, PM-8, SW20.14 |  |
| G27 | SW19.3, SW19.7 | The same-machine carrier with no network waits for LB-1's Windows run on the maintainer's PC and is named for Show Bus 1.4. |
| G28 | SW20.7, SW23.1, PM-10, SW20.14 | A Stagewatch-fed timecode source in PlayMap (ROADMAP R3.7) is not planned. |
| G29 | SW20.1, SW20.7, SW20.11, SW20.12, SW20.14 |  |
| G30 | SW20.8, SW20.11, PM-4, SW20.14 |  |
| G31 | PM-2, PM-5, PM-16 |  |
| G32 | SW18.3, P91.1, AG-5 |  |
| G33 | P91.3 | WEB OPEN and WEB TYPE stay unadvertised: a window pass or a password would ride a proposal line and the journal. |
| G34 | P92.3, P92.5 | An assistant-proposable STAGEWATCH LINK is declined: repointing a key-bearing link is a person's trust decision. |
| G35 | P91.3, P96.1, PM-4, P97.4, PM-9, P96.2, P98.1, P98.2, P98.3 |  |
| G36 | PM-18 | On the owner's answer; frame-accurate following (ANCHOR) is not built. |
| G37 | P98.1, P98.2, P99.1, AG-8, AG-9, AG-10, PM-12, P99.2, AG-11 | By decision AGMessage does not read Patterns' context; its context reaches Patterns by CONTEXT POST once AG-8 and Round 99 have both landed. |
| G38 | SW19.1, SW19.11, SW19.12, P91.5, PM-4, AG-4 |  |
| G39 | SW19.5, P91.4, AG-4, SW19.12 | Key-continuity rollover is recorded for 1.4; pinned peers see CERTIFICATE CHANGED once after the renewal. |
| G40 | SW19.1, SW19.3, SW19.4, SW19.12, P91.4, PM-4, AG-4, P91.5 |  |
| G41 | SW19.11, SW19.12 | Dropping 9700 from Find waits for Patterns' 9928 hello walk (OPEN L47). |
| G42 | SW19.1, AG-4, AG-11, SW19.12 |  |
| G43 | SW18.3, P91.1, PM-5, AG-5, SW22.1, P95.1, AG-7, SW18.6 |  |
| G44 | P90.1, SW19.8, SW19.9, SW19.10, SW19.13, SW20.13, SW21.9, SW22.11, SW-INT-PM, SW-INT-AG, SW19.2, SW19.11, P90.2 | Two machines and a real network, Windows' UDP stack and firewall, WebView2, DPAPI, Direct3D 12, a TLS rig, real decoders and AI models stay walk rows (Patterns QUALIFICATION §26–§27; OPEN L47, L50, L56). |
| G45 | SW19.1, P91.2, PM-19, P91.5 | Stagewatch's ?pin= in browser addresses and Companion TCP 9923 stay named exemptions (owner question); PlayMap's 9941 waits for its owner (decision 17). |
| G46 | — | No measured ask; a cross-origin page would break the window's CSP, its window.SW check and pinning, so a Stagewatch window shows only its own Stagewatch's pages (windows-for-peers). |
| G47 | PM-3 | https HLS/DASH as a Stream needs a probe that PlayMap's LGPL FFmpeg has TLS; refused with the reason until then. |
| G48 | P93.2, SW20.4, SW20.5, P93.3 |  |
| G49 | SW21.7, SW21.8, SW21.10, SW21.9 | 'Link all' (a key minted per heard node) needs a security design of its own; a clock-corrected merged journal is not planned. |

## Deferred

- Show Bus 1.4's default flip: bearer keys arriving plain from the LAN refused, legacy proofs from other machines refused for app links, plain line app links where TLS is offered refused, a node link without LINK served as a screen; key-continuity certificate rollover; the same-machine carrier (LB-1).
- Measured features: `stuck` on line acceptors (the fifth PONG word) and `expire`, each only after its show writer's stall is measured (lab line LP-1); the handshake cost, the side-call burst after a hub restart against Stagewatch's 64 slots (LP-2), sleep and resume against the self-gap rule (LP-3). No governor before its measurement.
- Companion decks on Patterns' and PlayMap's wires adopting LINK (each module's own round); until then the legacy proof, never plain AUTH off this machine.
- Twin TLS; the twin adopting 13.12's outcomes (ACT/ACTED) and a background keep-alive beat; a standby taking over accepted PeerLinks and posted contexts bound to the main's Show Bus id (an OPEN row from P92.4).
- G46 (a Stagewatch window showing a peer's page) and G49 piece 1 ('Link all').
- The Eye: replay relighting link edges from the journal's link rows; the problems queue's memory (OPEN L44); NDI chains across apps (needs a field naming NDI sends); PlayMap's fleet board and Stagewatch's Show network drawing the same edges; the Eye in a browser.
- G36's frame-accurate following (an ANCHOR line), G19's research-team picker and AGMessage AI proposals, G33's WEB OPEN/WEB TYPE, G34's assistant-proposable link, G21's crew-key ACK policy.
- PlayMap rendering real web pages (ROADMAP L10) and https streams behind an FFmpeg TLS probe.
- Patterns' kept passes and grants at rest under DPAPI and a sealed twin link (OPEN L36, L40).
- G15's remainder (a no-PIN Stagewatch accepting contexts for unbound ids) and the viewing code reaching screens already open.
- Interop rows that need two machines, Windows (UiSmoke, WebView2 showing a grant, DPAPI), Direct3D 12, a TLS rig, real decoders or real AI models.
- Declined by the profile and not planned: a per-request HTTP MAC, http and twin carriers, resend by id, a default command expiry, `observed` as a wire outcome, a PAKE, TLS-exporter channel binding (.NET does not expose one).
