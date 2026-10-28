# Owner decisions for the Show Bus / PeerLink plan

Each question has the plan's recommended answer. Answer in chat: "all recommended", or name the ones you want differently (by the tag in brackets).
The full plan is 173 units in 9 trains; Train 1 (the safety fixes) is being built now with the recommended answers assumed, committed locally and not pushed.

## All four

**[X-branches] Which branch does each tree's work commit to? Stagewatch, Patterns and Mapper sit on the working branch; Stagewatch's has five commits that are not on main.**

- Why: Patterns' brief: 'never push to a branch you were not given'. The trains need one named branch per tree, and pins for the interop lane.
- Recommended: One branch per tree that you name; Stagewatch's after S-217 is settled. The plan pushes nowhere else.
- Holds back: SW18.1, P89.0, PM-1

**[X-declines] Confirm the fixes this plan declines or defers without building them: G46 (a peer's page in a Stagewatch window), G49's 'Link all', G19's proposal picker, G33's WEB OPEN/TYPE, G34's assistant-proposable link, and G21's crew-key ACK policy.**

- Why: Each gap's fix is designed, but nobody in the room has asked, or the fix would carry a secret on a proposal line. The papers must record a decision, not a silence.
- Recommended: Confirm all six as declined for now. Stagewatch 2.22's WHATS_NEXT records G46 and G49, AGMessage 0.5.1's CHANGELOG records G19, and Patterns' OPEN keeps G33 and G34.
- Holds back: SW22.12, AG-11a

## Stagewatch

**[S-217] 2.17 (windows) is five commits on the branch, unreleased (csproj 2.16.0). Release it before 2.18, or fold it into 2.18?**

- Why: Every later release and SW22's grants build on it. Its WHATS_NEXT section and release date need one answer.
- Recommended: Release 2.17 first through release.ps1 -Version 2.17.0 -ModuleVersion 2.17.0. Folding it into 2.18 is the fallback, keeping its section.
- Holds back: SW18.8

**[S-releases] Seven releases (2.18–2.24) need the maintainer's PC seven times: smoke, the WPF work and release.ps1. Keep one per train, or merge two adjacent trains into one release?**

- Why: The PC is the only place UiSmoke and the installer run. Merging releases saves sessions but ships two trains' harms fixes later.
- Recommended: One per train for 2.18 (the harms) and 2.19 (the contract). From 2.20 on, you may merge two adjacent trains into the later number; that release then validates both trains.
- Holds back: SW18.8, SW19.11

**[S-sections] Put PeerLink at §13 and the numbers at §14 of the 1.3 master?**

- Why: spec-1-3 put the numbers at §13, while the PeerLink profile is written as §13. 23 references in unlanded designs follow the choice.
- Recommended: §13 PeerLink, §14 numbers.
- Holds back: SW19.1a

**[S-D0] May a window grant's page address ride exactly two carriers: a browser's address bar, and the one verified link to the grant's own app (TLS or local)? The doctrine line at CLAUDE.md:37-38 would change, and that line is yours to write.**

- Why: §1 rule 5 and the doctrine keep window addresses out of every frame. Delivering a grant to Patterns or PlayMap needs a named exemption written before the wire does it (windows-for-peers D0).
- Recommended: Yes, as D0 words it, saying 'verified PeerLink (or key-verified node link)'.
- Holds back: SW19.1c, SW22.4, PM-15b

**[S-exemptions] Keep ?pin= in browser addresses, and Companion TCP 9923's unauthenticated door when bound to the network, as named §8 exemptions in 1.3?**

- Why: spec-1-3 lists both as 'not scheduled'. 1.3's text needs the wording, and the health lights must say what these doors leave open.
- Recommended: Keep both named in 1.3, and schedule 9923's authentication with 1.4's default flip.
- Holds back: SW19.1e

**[S-interop-gate] Make Stagewatch's release job wait for the interop job at the siblings' pinned commits?**

- Why: The lane is the only cross-app proof (G44). Pins mean only a failing pinned row blocks, never a sibling's red head.
- Recommended: Yes. The nightly heads run reports drift only.
- Holds back: SW19.10

**[S-interop-ci] Create a read-only SIBLINGS_READ_TOKEN for Patterns, Mapper and AGMessage, and allow the interop job to relax Ubuntu's unprivileged-userns AppArmor restriction on its ephemeral runner?**

- Why: The job checks out the siblings at their pins (ecosystem-lane.md:794-798), which needs the token if they are private. The namespaced LAN needs `sysctl kernel.apparmor_restrict_unprivileged_userns=0` (:808). Both are yours to grant.
- Recommended: Yes: a fine-grained token with contents:read on the three repos. It never persists in a checkout, and forks skip the job with words.
- Holds back: SW19.10

**[S-shared-keys] When 2.18 binds each node key to one app, what happens to a key two apps share today?**

- Why: SW18.1 binds a key on its first use, and SW18.2 refuses it for a second id. A room that gave Patterns and PlayMap one key would see one of them refused after the upgrade.
- Recommended: Refuse the second app with words ('this key speaks for <kind> <id> — make a key for <app>'), offer Make a key for <app> in one press on its Show network row, and say so in 2.18's upgrade notes. Never let one key speak for two ids.
- Holds back: SW18.1

**[S-brief] The plan needs two lines in Stagewatch's CLAUDE.md: `tools/test.sh interop` in the loop, and the doctrine's D0 wording. Will you write them?**

- Why: No agent edits an agent brief. The lane's papers list the line as your action.
- Recommended: Yes, when SW19.11L lands, and D0's line with S-D0's answer.
- Holds back: SW19.11L

## Patterns

**[P-numbers] Take rounds 89–99 and PLAN §107–§117 as numbered here, with the bench host as Round 90?**

- Why: Five designs numbered the same work differently. Commit subjects carry 'Round NN.k', and pushed history is never rewritten, so the answer must come before Round 89's first commit, not before its papers.
- Recommended: Yes. A later renumbering shifts only rounds not yet opened.
- Holds back: P89.0

**[P-ADR016] ADR-016: the wire shows its state only to a paired connection. Scope A (NAV, MENU, CUE LIST, ARCADE and ASSISTANT stay open and named in §8) or scope B (every query pairs first)?**

- Why: 9697 greets with STATE before NONCE (ControlService.cs:356-357), and IsQuery lets EYE and CONTEXT through unpaired (ControlProtocol.cs:1454). Once the Eye carries link records, the room's topology would reach anyone. Scope B breaks Patterns' own cross-machine asks.
- Recommended: Scope A. STATE, EYE, CONTEXT and SUBSCRIBE pair first; the five named queries stay open and listed.
- Holds back: P91.2

**[P-twin-secrets] Turn the twin's SendSecrets off by default and refuse it on a plain twin link to another machine, in Round 89?**

- Why: Device passwords and, from 89.3, kept window passes ride the plain twin by default (ShowState.cs:2080), and the twin listens on every interface (TwinService.cs:488,512). The cost: after a takeover the standby lacks passwords and draws kept pages black (OPEN L60).
- Recommended: Yes, in Round 89, with words on both desks and a §22 walk row. Twin TLS comes later.
- Holds back: P89.6

**[P-OSC12h] End the 12-hour OSC trust an address earns from a wire or web proof (an ADR), in Round 89?**

- Why: A proof trusts the address for OSC for 12 h today (ControlService.cs:439, 557-569, 979-981). 13.18 says a proof grants nothing to an address, and on venue Wi-Fi an address is not an identity. The change is unilateral.
- Recommended: Yes. On a paired desk, OSC comes only from the OSC ALLOW list and loopback. Desks that relied on the carry-over add their controller to OSC ALLOW, which the papers say.
- Holds back: P89.8

**[P-legacy] The standing brief says 'No legacy or previous version is considered'. May Patterns keep level-0 paths for other apps' older versions: HTTP evidence for an older Stagewatch (91.1), level 0 for a Stagewatch without LINK (95.1), PlayMap at level 0 (96.1, 98.1)?**

- Why: Other apps' older versions are in the room until each upgrades, and dropping them strands a show mid-upgrade. Keeping them goes against the brief's words.
- Recommended: Keep them as labelled, amber fallbacks for other apps only: never for Patterns' own past versions. Each is named in OPEN and removed at Show Bus 1.4's default flip.
- Holds back: P91.1

**[P-scopes] Which Patterns verbs does each app-key scope reach (follow, post, drive, operate)?**

- Why: Round 97 issues ptk_ keys, and the acceptor enforces the scope (13.5.3). A loose 'drive' would let a playout desk change outputs.
- Recommended: - follow: STATE, CONTEXT, SUBSCRIBE, EYE.
- post: follow, plus CONTEXT POST and its own proposals and results.
- drive: post, plus VIDEO transport, CUE GO/STANDBY, PLAYLIST transport and STAGE MESSAGE.
- operate: everything.

No OUTPUTS, TAKE/CUT, SCREEN or settings below operate. §8 points to REMOTE.md.
- Holds back: P97.0

## PlayMap (Mapper)

**[M-PMX] May the Show Bus safety and PeerLink fixes to unowned round-2 files land during round 3 as named round-2 fix batches, spine lines only through §5.7? The files, per batch:
- PM-1: Engine/Host/NetworkHost.cs, Net/Links/StagewatchScreenCommands.cs (new).
- PM-2: Net/Links/{StagewatchLink,PatternsLink}.cs, Net/Wire/{WireClient,WireSession}.cs, Net/Http/HttpParts.cs, Engine/Host/NetworkHost.cs, Core/Bus/Pairing.cs, Engine/Devices/NodeCredentialStore.cs, Engine/Show/PreflightBreadth.cs.
- PM-3: Engine/EngineCore.Content.cs, plus VerbCatalog.Redacted through §5.7.
- PM-4: docs/SHOWBUS.md and tools/showbus/ (new), Net/Bus/HelloService.cs, docs/SHOWBUS-NODE.md, docs/FLEET.md.
- PM-5: Net/Links/StagewatchLink.cs, Engine/Show/PreflightBreadth.cs.
- PM-6: Core/Bus/{PeerLink,ShowBus,Pairing}.cs, Core/Time/ClockOffsetEstimator.cs, Net/Wire/WireClient.cs, Net/Links/StagewatchLink.cs, Engine/Host/NetworkHost.cs.
- PM-7: Net/Links/StagewatchTimer.cs, Engine/Rendering/TimerView.cs.
- PM-8: Net/Links/StagewatchLink.cs, Net/Wire/WireSession.cs.

Each batch also adds its tests and a docs/round2/CARRYOVER.md entry.**

- Why: round3/PLAN.md §4 (:2121-2125) forbids edits to unnamed folders during a wave. Engine/Rendering/** was wp3-rhi's in W1 (:578), and VerbCatalog outside its blocks is a spine file (:307-311). Without leave, PlayMap, until the integration pass:
- flashes projectors on any identify (NetworkHost.cs:783);
- sends its key plain to typed addresses;
- follows a blackout from whoever answers;
- stays legacy to every peer.

Fix batches already merge during round 3 (the fix-*-win batches).
- Recommended: Grant it for PM-1 to PM-8, each a named batch with a CARRYOVER entry, merged between wave merges. WIRE, README and ROADMAP stay the integrator's, through papers notes.
- Holds back: PM-1, PM-2, PM-3, PM-4, PM-5, PM-6, PM-7, PM-8

**[M-content-before-W3] May fix-media-page-import edit EngineCore.Content.cs before W3 starts? Desk-map owns it in W3 and canvas in W4.**

- Why: PlayMap imports a page it cannot draw and keeps a window pass in the project (G47). The refusal lives in that file.
- Recommended: Yes, before W3; otherwise at the integration pass.
- Holds back: PM-3

**[M-wp4-peerlink] Open a post-round-3 package, wp4-peerlink, for PlayMap as a PeerLink acceptor and connector?

It brings VerbCatalog LINK, RECEIPTS and KEY rows; STATE clients and links records, with fixtures and cards; a pmk_ table with scopes mapped to ShowPolicy; and TLS on 9941.**

- Why: These touch the spine-owned VerbCatalog, ShowPolicy and fixtures. Until it lands, peers see PlayMap at level 0, and GO is never timed over a proved link.
- Recommended: Yes, first after the integration pass.
- Holds back: PM-9a, PM-10, PM-12

**[M-trusted-addresses] Keep TrustedAddresses only for legacy Lines devices (amber 'trusted by address — not a proof'), never for an app link?**

- Why: Trusted addresses pair without proof today (Pairing.cs:113-115). An address is not an identity, and a proof grants nothing to one (13.18).
- Recommended: Yes.
- Holds back: PM-9a

**[M-eye-spine] Carry the Eye's context rows through the spine lane in ContextBuilder.cs, and leave ContextShape.cs and EyeContextTests untouched this round?**

- Why: ContextBuilder.cs is a spine file. ContextShape.cs and EyeContextTests are not (round3/PLAN.md:307-311), so the lane cannot carry them; Patterns forces link items and chain ends instead.
- Recommended: Yes.
- Holds back: PM-11

**[M-decision7] Round 2 decision 7: may PlayMap's assistant use Stagewatch's approval desk?**

- Why: G13 is designed (cross-app PM-1) but parked on this decision (CARRYOVER.md:99) and on W4's EngineCore.Assistant.cs.
- Recommended: Yes, after W4, with CollectApproved=false so that StagewatchLink stays the one collector.
- Holds back: PM-17

**[M-decision17] Revisit decision 17: on a node with a token, may TCP 9941's STATE greeting and CONTEXT wait for pairing, once Patterns' node devices pair by proof (Round 96)?**

- Why: 9941 sends STATE on HELLO before pairing (WireSession.cs:263-276). 1.3 §8 lists this as PlayMap's exemption until then.
- Recommended: Yes, after Round 96.
- Holds back: PM-19

**[M-G36] May PlayMap's Patterns link drive Patterns, through a chase source and LINK PATTERNS SEND?**

- Why: G36 is designed minimal, nobody in the room has asked for it, and frame accuracy would need an ANCHOR line.
- Recommended: Defer until someone asks; keep the design.
- Holds back: PM-18

**[M-windows-package] Open a post-round-3 package, wp4-showbus-windows, for native windows and the timer's remaining fields (UX.md included)?**

- Why: G11 and the rest of G12 need SourceKind.Window, catalogue words, StateWriter and ContextBuilder, which are spine files, plus wave-owned readers.
- Recommended: Yes, beside or after wp4-peerlink.
- Holds back: PM-14, PM-15a

## AGMessage

**[A-branch] Who owns AGMessage's 0.5.0 branch, and may this plan's AGMessage units land there as 'AGMessage 0.5.0, unit 21' onward? If not, may they be handed to that branch's session?**

- Why: The branch is another session's. It moved 8 commits (9d7f5c3 → 6146ee9, the last at 07:09) while this plan was written, and its newest commit is 'the papers for the last review round'. The plan must not push to a branch it was not given.
- Recommended: Hand AG-1's design to the session that owns the branch, for it to land as unit 21 before tagging 0.5.0. Later units follow the same way, or on a branch you name.
- Holds back: AG-1

**[A-hold-050] Hold the 0.5.0 tag until AG-1 lands on the branch?**

- Why: At 6146ee9 the branch still sends Patterns' and PlayMap's full-control token in a header over plain HTTP to any node that is neither pinned nor announcing TLS (peerlinks.js:259-261, :292), and drives them with it (:346-356). That leak should block the ship.
- Recommended: Yes. AG-1 goes into 0.5.0; the rest follows as 0.5.1 and 0.6.0.
- Holds back: AG-1

**[A-direct-path] Accept losing the direct send path to Patterns and PlayMap until they issue AGMessage a scoped key?**

- Why: agmessage decision 10: the pairing token is too strong for a messenger. Meanwhile, lines become desk proposals.
- Recommended: Yes. AG-8 and AG-8m restore the path with scoped keys.
- Holds back: AG-1

**[A-display-names] May display names appear in AGMessage's Show Bus context?**

- Why: The Eye draws AGMessage's displays (AG-9), and a display name may name a place. The privacy rule already keeps text, files, halls and codes out.
- Recommended: Publish the admin-set name (60 characters at most), with a per-display 'keep private' switch that falls back to 'Screen N'.
- Holds back: AG-9

**[A-versions] Version the remaining work as 0.5.1 (AG-2 to AG-6 and the 1.3 copy) and 0.6.0 (LINK, the line connector, the Eye)?**

- Why: spec-1-3's planned 0.4.1 was overtaken by 0.5.0. The CHANGELOG, version.js and commit subjects need the numbers.
- Recommended: Yes.
- Holds back: AG-11a, AG-11b
