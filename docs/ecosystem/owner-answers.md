# Owner answers (given 2026-10-25 by the maintainer, the owner of all four repos)

Every owner question in master-plan.md is answered with the plan's RECOMMENDED answer. In particular:

- X-branches: each repo's work commits to the working branch (Stagewatch, Patterns, Mapper). AGMessage's AG-1 is
  handed to the session that owns AGMessage's 0.5.0 branch (a note was given to the user); later AGMessage
  units land after 0.5.0 on a branch named then.
- P-numbers: Patterns rounds 89-99, PLAN §107-§117, as numbered in master-plan.md.
- P-ADR016: scope A (STATE, EYE, CONTEXT and SUBSCRIBE pair first; NAV, MENU, CUE LIST, ARCADE and ASSISTANT stay open and named).
- P-twin-secrets: yes, in Round 89 (SendSecrets off by default, refused on a plain twin link to another machine, words on both desks, a §22 walk row).
- P-OSC12h: yes (the 12-hour OSC carry-over ends; OSC ALLOW and loopback only on a paired desk; an ADR).
- P-legacy: keep labelled amber fallbacks for OTHER apps' older versions only, each in OPEN, removed at 1.4's default flip.
- P-scopes: follow / post / drive / operate as recommended (no OUTPUTS, TAKE/CUT, SCREEN or settings below operate).
- S-217: release 2.17 first if the maintainer runs release.ps1 before 2.18; otherwise 2.18 carries 2.17's section.
- S-releases: one release per train for 2.18 and 2.19; from 2.20 two adjacent trains may merge.
- S-sections: §13 PeerLink, §14 numbers.
- S-D0: yes — a window grant's page address rides exactly two carriers: a browser's address bar and the one verified
  PeerLink (or key-verified node link) to the grant's own app.
- S-exemptions: keep ?pin= in browser addresses and Companion TCP 9923's door as named §8 exemptions in 1.3; schedule
  9923's authentication with 1.4's default flip.
- S-interop-gate: yes (release waits for the interop job at the siblings' pins; the nightly heads run reports drift only).
- S-interop-ci: yes (a read-only SIBLINGS_READ_TOKEN, contents:read on the three repos; relax the userns AppArmor
  restriction on the ephemeral runner; forks skip with words). The maintainer creates the token.
- S-shared-keys: refuse the second app with words, a one-press "Make a key for <app>" on its Show network row, and the
  upgrade note in 2.18.
- S-brief: the maintainer writes the two CLAUDE.md lines when SW19.11L lands (or accepts them drafted for review).
- X-declines: all six declined for now (G46, G49 'Link all', G19's proposal picker, G33's WEB OPEN/TYPE, G34's
  assistant-proposable link, G21's crew-key ACK policy), recorded in the papers named in the plan.
- M-PMX: yes — PM-1 to PM-8 land now as named round-2 fix batches with CARRYOVER entries, spine lines through §5.7,
  merged between wave merges; WIRE, README and ROADMAP stay the integrator's through papers notes.
- M-content-before-W3: yes, before W3 starts; otherwise at the integration pass.
- M-wp4-peerlink: yes, first after the integration pass.
- M-trusted-addresses: yes (legacy Lines devices only, amber "trusted by address — not a proof").
- M-eye-spine: yes.
- M-decision7: yes, after W4, with CollectApproved=false.
- M-decision17: yes, after Round 96.
- M-G36: defer until someone asks; keep the design.
- M-windows-package: yes, beside or after wp4-peerlink.
- A-branch / A-hold-050 / A-direct-path: AG-1 goes to the 0.5.0 session (hold the tag until it lands); the direct path
  is lost until scoped keys exist.
- A-display-names: publish the admin-set name (60 characters at most) with a per-display "keep private" switch.
- A-versions: 0.5.1 and 0.6.0 as recommended.
- M-PMX extension (2026-10-25): the leave for fix-showbus-keys (PM-2) is extended to src/PlayMap.Net/Wire/WireServer.cs and
  src/PlayMap.Net/Http/HttpHost.cs (one nullable option each). fix-media-page-import's (PM-3) ask for the new
  src/PlayMap.Core/Media/PrivateAddress.cs is extended too (same day).

- M-PMX extension (2026-10-26): the leave for fix-showbus-node-link (PM-5) is extended to src/PlayMap.Engine/Host/NetworkHost.cs
  (the change already made) and to src/PlayMap.App/ViewModels/MainViewModel.cs's LinkLight arm only (held draws red; a live link
  that is not paired draws amber, as preflight and the context do).

- M-PMX extension (2026-10-26, later): the leave for fix-showbus-1.3 (PM-4) is extended to src/PlayMap.Core/Bus/ShowBus.cs,
  src/PlayMap.Net/Bus/PeerTable.cs and src/PlayMap.Engine/Host/NetworkHost.cs (Peers()'s conflict arm and OnPeer's Changed).
- S-brief (2026-10-26): the maintainer accepts all four drafted Stagewatch CLAUDE.md lines, with 2-D0 (scratchpad/brief-lines-draft.md).

- M-PMX (2026-10-27): a new round-2 fix batch, fix-proposal-sink-deadline, is allowed with the leave
  src/PlayMap.Engine/Assistant/StagewatchProposalSink.cs and tests/PlayMap.Engine.Tests/StagewatchSinkTests.cs: the sink's 10 s
  real-time HttpClient limit becomes a deadline on an injectable clock (a running node behaves as today), and a late or missing
  answer is said as "no answer within 10 s; the desk may hold it", never "did not take".

- M-PMX extension (2026-10-27): the leave for fix-stagewatch-timer-air (PM-7) is extended to src/PlayMap.Engine/Host/NetworkHost.cs
  (the timer JSON by its writer; the held blackout's hold and words), tests/PlayMap.Render.Tests/PlayMap.Render.Tests.csproj (the
  item group compiling TimerView.cs and SourceLeases.cs) and the new tests/PlayMap.Engine.Tests/Host/StagewatchTimerAirTests.cs.

- M-PMX (2026-10-27): a new round-2 fix batch, fix-desk-unconfirmed, is allowed: the reply carries its outcome
  (src/PlayMap.Core/Wire/Endpoints.cs, WireReply) and the desk draws an unconfirmed command amber with "it may have run — look at
  its state", never a refusal (EngineLinkClient and its readers, DeskSession's toast, the GO guard's Replied), with their tests and a
  CARRYOVER entry; the desk's 30 s reply wait stays. Desk files are the wave's desk package's: the leave covers the lines this needs.

# Commit identity (all repos, from 2026-10-25)
The committer is always the session's configured committer identity (each repo's git config says so; never override it with -c user.name/
user.email). The AUTHOR follows the repo: Stagewatch --author="jammin808 <b.rohleder@gmail.com>"; Patterns
--author="Patterns <b.rohleder@gmail.com>"; Mapper the default (Claude); AGMessage as its branch does.
