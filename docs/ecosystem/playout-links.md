# Playout links — Stagewatch drives Patterns and PlayMap, both at once, with real receipts (revised after review)

Area: playout-links. Gaps: G01, G07, G08, G26, G28, G29, G30, G48. Design only: nothing in any repo was edited or built.

All Stagewatch paths below are under `Stagewatch/src/` unless written in full. Line numbers are from the trees as read on 2026-10-25.

## R. The review, answered

Each issue was checked against the code. Twenty issues: eighteen accepted, one accepted with a corrected mechanism, one partly
rejected.

| # | Issue | Verdict | Evidence checked | What changed |
|---|---|---|---|---|
| 1 | `playout` over OSC / Companion TCP is an unauthenticated relay into the peers' paired sessions | **Accepted** | OSC dispatches any parsed verb with no check (`Timer.Remote/OscServer.cs:110-130`), binds every interface when on (`:39`; `Timer.Core/AppSettings.cs:271` `oscBindAll` true, OSC off by default `:269`); Companion runs any shared verb (`Timer.Companion/CompanionServer.cs:294-305,404-411`), loopback unless the operator binds all (`AppSettings.cs:315`, `CompanionServer.cs:44`); `docs/REMOTE.md:29-30` says "none". The `patterns` verb has the same exposure today. | D12: a verb that drives another node's air (`playout`, `patterns`) is refused from a non-loopback sender on every transport that carries no credential (OSC, Companion TCP, and HTTP/WS/MCP when no PIN is set), unless the operator lists the address in `Remote.PlayoutFrom`. Tests. |
| 2 | Trigger secrets ride plain `http://`, the target URL is expanded with show text, redirects keep custom headers | **Accepted** | `Timer.Integrations/Triggers.cs:27` (default `HttpClient`: redirects followed), `:86-92` (`{TITLE}`, `{DETAIL}`…), `:133` (`Expand(r.Target, ctx)`). Patterns closed the same defect as L53 with TLS (`Patterns/docs/OPEN.md:51`); Patterns and PlayMap serve HTTPS on the same HTTP port (`Patterns/docs/REMOTE.md:602-606`; `Mapper/src/PlayMap.Net/Bus/HelloService.cs:95-113`). | D10: a secret goes only to a loopback host, or over `https://` to a pinned certificate; no placeholder in scheme, host or port when auth is set; no redirects on the authenticated client; a rule that would send a secret to plain HTTP off this PC cannot be saved or fired. |
| 3 | PlayMap standby is on the air: a cued item shows its first frame, and CUE replaces a playing lane | **Accepted** | A cued lane is on the air at the frame's own instant (`Mapper/src/PlayMap.Engine/Rendering/EngineSources.cs:236-257`; only an idle lane returns null, `:240-242`); `Cue` overwrites the lane with no playing check (`Mapper/src/PlayMap.Engine/Playback/PlaybackLanes.cs:135-146`). Review is pre-ticked (`Timer.Integrations/Copilot.cs:15-19`). | D9: standby is opt-in per cue and off by default, labelled "shows its first frame on air at load"; the idle guard applies to the manual `playout … cue` too; every `playout` action is High. |
| 4 | The old fake-desk tests cannot pass unchanged; a peer that sent a greeting NONCE must never get plain AUTH; an unanswered NONCE must not wait forever | **Accepted** | `tests/Stagewatch.Tests/ShowBusTests.cs:239-263` (greeting NONCE, then `ERR <id>unknown word` for anything else; asserts no `AUTH `); `tests/Stagewatch.Tests/Program.cs:1283-1318` (answers only `PING`; asserts `AUTH s3cret`). | D3's rule: after a greeting NONCE or an `OK @pn`, never plain AUTH; a refused NONCE verb proves on the greeting nonce; no answer within 5 s fails closed (loopback: plain AUTH). The ShowBusTests fake now passes unchanged through that rule; the Program.cs fake gains one line (answer an unknown verb with `ERR`, as a real pre-1.1 desk does). The papers say so. |
| 5 | An AI escapes approval through the link: with agents acting directly, a `playout go` reaches PlayMap as Stagewatch's Remote role, where PlayMap would hold an assistant's line | **Accepted** | `Timer.App/ViewModels/MainViewModel.Copilot.cs:207-209` (`return null` when `!AgentsNeedApproval`), `Timer.Remote/McpEndpoint.cs:133-149`; `Mapper/src/PlayMap.Core/Show/ShowPolicy.cs:203-210` (assistant → Propose while armed); Show Bus §11 (targets pull approved lines and run them under their own checks, `docs/SHOWBUS.md` §11). | D12: AI-origin `playout`/`patterns` lines are always proposals; when the link's node collects §11 approvals the proposal targets that node, in its own grammar, so it runs there as Approved under its own policy with the proposer's name. |
| 6 | The outbound queue and journal-on-answer can leave a command with no row, and queue across a drop | **Accepted** | `docs/SHOWBUS.md:79-80` (§4: nothing queued while a link is down); `Timer.Integrations/PatternsLink.cs:165-171` (ids only on 1.1 desks; silent eviction over 64), `:224-226` (only pending entries are told at a drop). | D5: in-flight bounded at 32 with no eviction (the 33rd is refused, not sent); the queue is emptied at a drop as "not sent"; 30 s silence = "unconfirmed"; an id-less old desk completes at once "sent — no receipt"; exactly one journal row per command that reached the socket. |
| 7 | Timed GO: mixed timed/untimed sets, sync groups, stale best sample | **Accepted, one mechanism corrected** | `GuardOf` is the timecode chase guard (`PlaybackLanes.cs:302-316`, set only by `Timecode/EngineCore.Timecode.cs:208`), not a sync-follower guard; PlayMap does not refuse a GO on a follower. A follower takes its leader's anchors (`EngineCore.Sync.cs:46-80,193-197`; `PlaybackLanes.cs:173-178`), so a GO there is replaced by the leader's next anchor — worse than a refusal. `ClockOffsetEstimator` keeps 8 samples with no time (`Timer.Core/Resilience.cs:244-274`); pings every 2 s (`PatternsLink.cs:43`). | D8: untimed members are held on their link until `T − rtt/2`; with no rtt at all, every member goes untimed; one GO to a sync group's leader; a follower is refused locally; the best sample among those ≤ 10 s old. |
| 8 | "Observed start" only echoes AT, and `video` is the focused playlist only | **Accepted** | `Mapper/src/PlayMap.Engine/StateWriter.cs:263-270` (`video` = the focused lane), `:353` (`positionMs` from the anchor), `EngineCore.Router.cs:265-271` (a past AT clamped to now). `Mapper/tests/fixtures/state/observe.json` is `{}`. | D8: read `playlists[<id>]`; report "scheduled for T (± rtt/2)", and "started late by X ms" only when clamped; the real picture observation is named as waiting on PlayMap's round-3 observer. |
| 9 | With no link named, a high-risk target follows moving state; grammar depends on an unknown kind | **Accepted** | `Timer.App/ViewModels/MainViewModel.Patterns.cs:15-22` (`PickPlayout` prefers whichever source is running). | D11: only clip transport falls back to the VT link; `go`/`cue` need a name when more than one link is enabled; refused while the link's kind is unknown. |
| 10 | Patterns standby moves the Patterns operator's standby; STANDBY+GO defeats the fence; Confirm left open | **Accepted** | `Patterns/src/Patterns.App/Services/ShowActions.Cues.cs:37-53`, `CueStackService.cs:174-198` (`GoGate` fence on the seen standby; Confirm), `CommandRouter.cs:149-155`; `Patterns/docs/REMOTE.md:196-197`. | D9: Patterns standby on load is opt-in and says it moves the desk's standby; at start only `CUE GO <id>` (the fence decides); Confirm gets its own receipt and no second GO is ever sent; Help and the menu say it. |
| 11 | Two clip ends can call `Next()` twice | **Accepted** | `MainViewModel.Patterns.cs:42-62`; `Timer.Show/ShowController.cs:127,225` (`Execution`). | D13: only the link feeding the VT countdown moves the running order, at most once per item execution. |
| 12 | Migration breaks rollback; show files are an input boundary | **Accepted** | `AppSettings.cs:11,145-152`; `Timer.Core/SettingsStore.cs:169-177` (no upgraders yet); `Timer.Core/ShowFile.cs:99-122` (`Sanitize`). | D1: the legacy `patterns*` keys keep being written as a mirror of link `patterns` for one release, with a deterministic merge rule; `ShowFile.Sanitize` checks playout cues. |
| 13 | Refusing `?pin=`/`key=` in trigger URLs breaks shows | **Accepted** | `docs/REMOTE.md:107` documents `?pin=` for Stagewatch's own HTTP API. | D10: warn at save and offer to move the secret into the profile (new kind `stagewatch`); refuse only a rule that also carries an auth profile; never refuse at fire a rule that worked before. |
| 14 | SHOWBUS copies drift; Patterns' copy is a Patterns round; a new receipt shape is a version question | **Accepted (alignment)** | `docs/SHOWBUS.md:3-6`; the spec-1-3 area already owns the one master edit and the copy rule and lists the copies as units (`design/spec-1-3.md:8-16`, decision 10, units S1/S9/P3/A1/M1). | D15: this area supplies words to spec-1-3's master edit (Show Bus 1.3); the copies follow through spec-1-3's units; "Patterns: none" is corrected. |
| 15 | The TLS choice comes from an unauthenticated hello and can be downgraded | **Accepted** | `docs/SHOWBUS.md` §10 ("the pin — not the hello — is the trust"). | D4: once a node id has a pin, its link dials `wss` only; a missing TLS hello is amber, never a fallback. |
| 16 | Context ids can collide or change | **Accepted** | `MainViewModel.ShowBus.cs:55-61` (ids from integration names), `MainViewModel.Integrations.cs:101-116`. | D14: playout items are built from the link name only, excluded from the integration loop; the other integrations' slugs are reserved. |
| 17 | No licence gate named | **Accepted** | `MainViewModel.Integrations.cs:41,106` (`FeatureGate.AllowsPlayout`, Production). | D1: the links, the verb, running-order cues and the module's actions take the same gate, with the tier named in the refusal. |
| 18 | Over-building on legacy paths: the clear-token tick, the 9700 listen, the local G48 rule | **Partly accepted** | The tick: dropped (fail closed). The 9700 listen is existing code (`PatternsLink.cs:420-445`), and the sibling spec-1-3 area keeps it until Patterns' L47 walk row (`design/spec-1-3.md` decision 11; `Patterns/docs/OPEN.md:37`) — so this design adds no 9700 code and leaves its removal to spec-1-3 (**rejected** as this area's change). The G48 rule: Patterns' STATE has no version (`Patterns/.../CommandRouter.cs:228-260`), so it is tied to the `pausable`/`stoppable` fields that Patterns adds with L48 (`design/patterns-playmap-side.md` decision 12): present, they decide; absent, the role rule holds. | D3, D6. |
| 19 | PlayMap's applied OK carries no words; the STATE size understated | **Accepted** | `Mapper/src/PlayMap.Net/Commands/CommandRunner.cs:96-115` (`Ok(id, reply.Payload)`), `Mapper/src/PlayMap.Core/Wire/Endpoints.cs:22` (`Success(effect: …)` leaves `Payload` null); the 30 non-card STATE fixtures total 75,861 bytes, with round-3 placeholders `{}` (`Mapper/tests/fixtures/state/README.md`). | D5 wording ("PlayMap Stage left took PLAYLIST GO …"); D7 cap rationale on the whole STATE; the Mapper fence measures it. |
| 20 | No supervisor API to retire a removed name; the rig never runs the desk's executor | **Accepted** | `Timer.Integrations/Supervisor.cs:41-61` (Reconcile only); `tools/Stagewatch.Rig/Program.cs:184-234` (its own item model). | No supervisor change: the view model keeps last pass's names and reconciles a removed one with a `"False"` signature, which `Reconcile` already retires. The executor, receipts and planner hooks move into `PlayoutDirector` (Timer.Integrations), used by the desk and the rig. |

The review's "missing" list maps to: D12 (gating, AI routing), D8 (sync groups), D1/D9 (`ShowFile.Sanitize`, unknown links), D1
(licensing, rollback), D15 (copies, version), D5 (journal rows for silence, eviction, old desks, drops), D4 (no downgrade, 429), D14
(AI facts, echo identity), D6 (RemoteViewOnly words via WHOAMI), D9 (Patterns standby warning).

## 0. What exists (evidence)

- One link, one host/port/token: `PatternsLink` (`Timer.Integrations/PatternsLink.cs`), started from three settings
  (`Timer.Core/AppSettings.cs:145-152`) by `MainViewModel.Integrations.cs:40-42`, gated by `FeatureGate.AllowsPlayout` (`:41,106`). Its
  name is the constant "Patterns" (`PatternsLink.cs:84`), so the context item is `link:patterns`, kind `playout`, and the clock check
  `clock:patterns` (`MainViewModel.ShowBus.cs:55-66`). The Show network labels `playmap` raw (`MainViewModel.ShowBus.cs:100`).
- The finder accepts only `t:"patterns"` (`PatternsLink.cs:458-460`) and listens on 9700 and 9928 (`:420-445`), although Stagewatch keeps a
  live hello table with `ports` and `tls` (`Timer.Integrations/RemoteSlides.cs:107-129`).
- Pairing today: `HELLO Stagewatch`, an `@hi PING` probe, a greeting `NONCE` proved with `AUTH-PROOF`; with no NONCE within 1.5 s plain
  `AUTH <token>` goes to any host (`PatternsLink.cs:197-207,279-284`), and a late NONCE is ignored (`:311`). Both peers answer a `NONCE`
  verb at any time with the request id and replace the connection's nonce (`Patterns/src/Patterns.App/Services/ControlService.cs:413-418`;
  `Mapper/src/PlayMap.Net/Wire/WireSession.cs:259-262`); Patterns greets with STATE built before the NONCE (`ControlService.cs:356-363`)
  and greets a loopback connection with no NONCE; both refuse plain AUTH off loopback (`ControlService.cs:428`; `WireSession.cs:341-344`).
- Receipts: ids only on 1.1 desks; over 64 pending, the oldest are dropped silently (`PatternsLink.cs:165-171`); the desk only sets
  `StatusLine` on the answer (`MainViewModel.Patterns.cs:64-66`), journals on send (`:86-88`) and gives the sender `accepted`
  (`MainViewModel.Receipts.cs:38,48-52`). HTTP/WS/Companion wait up to 3 s (`Timer.Remote/WebServer.cs:842,851,1370`;
  `CompanionServer.cs:294-313`). PlayMap's applied OK carries no words (`CommandRunner.cs:96-115`); an accepted one carries
  `{"stage":"accepted","effect"}`. Patterns' `CUE GO` OK carries `{"outcome":"Done"|"Requested"|"Confirm",…}` (`CommandRouter.cs:149-156`).
- Line cap 256 KiB; an over-long line is dropped into `Malformed`, which nothing reads (`PatternsLink.cs:40,274,88-89`). PlayMap's
  `main.json` is 13,240 bytes with `video` at byte 769, `sync` at 2,891 and `project` at 3,930; the whole STATE adds module sections
  (75,861 bytes across the non-card fixtures, round-3 ones still `{}`). Keys are in a fixed order: `v,seq,id,t,…,at`, `air`, `video`,
  `playlists`, `outputs`, `media`, `stage`, `scan`, `sync`, `links`, `peers`, `machine`, `project` (`StateWriter.cs:73-214`).
- Roles: Patterns refuses VIDEO PAUSE/STOP on a playlist item or a stinger's clip (`Patterns/.../ShowActions.Content.cs:481-505`) and
  publishes `video.role` (`CommandRouter.cs:237`) but no version. PlayMap's video row is always `role:"playlist"` for the focused lane
  (`StateWriter.cs:263-282`) and PAUSE/STOP work on it (`Mapper/src/PlayMap.Core/Wire/VerbCatalog.cs:155-161`).
- Clocks: PlayMap's PONG and `AT <unix ms>` read one `ShowWallClock` (`Mapper/src/PlayMap.Engine/Host/EngineHost.cs:121`;
  `EngineCore.Router.cs:253-276`); a past AT within 60 s is clamped to now (`:268-271`). Stagewatch measures the offset
  (`PatternsLink.cs:318-327`; `Resilience.cs:244-274`, no sample time kept).
- PlayMap playlists: `PLAYLIST CUE` loads and holds the first frame on the air (`EngineCore.Transport.cs:227-238`; `PlaybackLanes.cs:135-146`;
  `EngineSources.cs:236-257`); `PLAYLIST GO <pl> [<item>] [AT …] [EXPECT …]` with double-GO protection (`EngineCore.Transport.cs:188-221,280-302`).
  STATE `playlists[]` carries `id,name,state,index,item{id,label},positionMs,…,syncGroup,seq,next{id,label}` (`StateWriter.cs:323-384`);
  `sync{role,leader,…}` (`:463-480`; roles `leader|follower|solo`, `Host/NetworkHost.cs:351-355`); `project.index` (`EngineCore.Companion.cs:47`).
  A chase guards a playlist's transport with words (`PlaybackLanes.cs:302-316`). A sync-group follower takes the leader's anchors
  (`EngineCore.Sync.cs:46-80,193-197`). Catalogue risks: PLAYLIST GO high, PLAYLIST CUE review (`VerbCatalog.cs:173-174`).
- PlayMap session: `HELLO Stagewatch` is transport `stagewatch` (`WireSession.cs:94-98`), role Remote (`ShowPolicy.cs:23-50`), which may run
  Transport while armed unless the show is remote-view-only (`ShowPolicy.cs:161-163,238-252`). `WHOAMI` answers `paired, role, armed, may`
  (`EngineCore.Companion.cs:82-111`). TLS: `wss://<node>:<https>/ws`, subprotocol `playmap.lines.v1`, proofs only, the greeting STATE after
  `OK paired`, 429 for a locked-out address, close 4401 (`Mapper/docs/wire/companion.md:7-23`); https is the HTTP port (`HelloService.cs:95-113`).
- Patterns' cue stack: `CUE STANDBY` moves the stack's standby (`ShowActions.Cues.cs:37-53`); `CUE GO <id>` fences a stale standby by id
  and may answer Confirm (`CueStackService.cs:174-198`; `REMOTE.md:196`); STATE `cuestack{armed,hold,listRev,standby{id,number,name,requireConfirm}}`
  (`CommandRouter.cs:975-996`).
- Who may send: OSC and Companion TCP have no credential (`OscServer.cs:110-130`; `CompanionServer.cs:404-411`; `docs/REMOTE.md:29-30`);
  MCP agents act directly when approval is off (`MainViewModel.Copilot.cs:207-209`; `McpEndpoint.cs:133-149`); §11 lets a target pull its
  approved lines (`Timer.Core/Approvals.cs:81-108,135`).
- Triggers: HTTP sends no header, follows redirects and trusts any 2xx; the target URL is expanded with show text (`Triggers.cs:27,86-92,131-139`).

## 1. Decisions

1. **A list of playout links, named, licensed, reversible.** `IntegrationSettings.PlayoutLinks` (≤ 8): name, enabled, kind
   (`auto|patterns|playmap`), host, port, token (DPAPI), node id, transport (`auto|tcp|wss`), follow VT, next on end.
   - Names: `[a-z0-9-]{1,24}`, never an action word (`end restart play pause stop seek cue go`), never another integration's context slug
     (`powerpoint presenter-pcs vmix speech time-server ltc ltc-in cue-lights triggers rooms`, and the assistant's), unique.
   - Schema 1 → 2: the 2.17 Patterns keys become `playoutLinks[0]` named `patterns` (kind `auto`), so `link:patterns` and `clock:patterns`
     keep their ids. For one release the legacy `patterns*` keys are still written, mirroring link `patterns`. Load rule: no
     `playoutLinks` → built from the legacy keys; legacy keys that differ from the mirror → an older Stagewatch edited them, and they win for
     link `patterns`. 2.17 drops the unknown `playoutLinks` when it saves, so after a rollback only link `patterns` survives (said in the
     papers).
   - Licence: links start, the verb runs, running-order cues fire and the module's actions appear only under `FeatureGate.AllowsPlayout`
     (Production, as today); the refusal names the tier (`FeatureGate.Needs`).
2. **One link class; the peer's kind is observed.** `PlayoutLink` replaces `PatternsLink`. The hello gives the kind at Find; STATE confirms
   it (`"t":"playmap"` is PlayMap; a STATE with no `t` is Patterns). A kind profile decides the verbs, local refusals, AT and TLS. While the
   kind is unknown (no STATE yet), `go` and `cue` are refused "Stage left's kind is not known yet — no STATE heard".
3. **Pairing asks for the nonce and fails closed.** After `HELLO Stagewatch` the link sends `@pn NONCE` (this replaces the `@hi PING`
   probe: an `OK @pn` proves a 1.1 peer).
   - `OK @pn <hex>` → `@pa AUTH-PROOF` on that nonce (the latest one: the peer reads our lines in order).
   - A greeting `NONCE` or an `OK @pn` marks the connection 1.1: **plain AUTH is never sent on it**.
   - `ERR @pn …` (or an untagged ERR while the ask is open): a 1.1 connection proves on the greeting nonce (the refused verb did not
     replace it); otherwise the desk is older than Show Bus 1.1 → plain `AUTH` only when the socket's remote address is loopback; off this
     PC nothing is sent and the link is amber: "this Patterns desk is older than Show Bus 1.1 and pairs only by sending its token in clear —
     update Patterns". No clear-token tick.
   - No answer within 5 s: loopback → plain AUTH (as today on this PC); otherwise drop and redial with the words "no answer to NONCE in 5 s".
   - `ERR … wrong token` (or close 4401): no more proofs until the token setting changes (no lockout loop).
   - CONTEXT, WHOAMI, INDEX and CUE LIST are asked only after `OK paired` / `OK open`.
   - Redial backoff 0.5 s doubling to 5 s with jitter (§4; the spec-1-3 area hands this change to this unit).
4. **TLS where PlayMap offers it, and no downgrade.** A PlayMap link whose hello has `ports.https` and `tls` dials
   `wss://<address>:<https>/ws`, subprotocol `playmap.lines.v1`; the certificate is checked against `TlsPins` by node id (first use = the
   hello's fingerprint; a change is "CERTIFICATE CHANGED" until the operator trusts it, as `SlidesAgent` does, `SlidesAgent.cs:186-230`).
   **Once a node id is pinned its link dials `wss` only**: a hello without TLS, or none, leaves the link amber ("Stage left offers no TLS
   now — not dialling plain"), never plain. 429 at the upgrade is red "PlayMap Stage left has locked this PC out for a minute after wrong
   tokens"; 4401 is red "wrong token". Patterns stays proof-paired plain TCP and its item says so (no TLS on its wire, Patterns L55).
5. **Receipts complete from the peer's answer, and every attempt leaves one row.**
   - Each send returns a ticket `{id, Task<PeerAnswer>}`. At most 32 tickets are in flight per link; the 33rd is refused at once ("32
     commands are waiting on Stage left's answers — not sent"), never evicted.
   - The link writes from its own thread (TCP) or its send loop (WSS); the show thread never touches the socket. At a drop every ticket not
     yet written completes as refused "not sent — the link dropped" (§4: nothing queued across a drop); every written one as
     "unconfirmed — the link dropped before it answered".
   - Answers: `OK @id` → applied; `OK @id {"stage":"accepted",…}` (PlayMap) → accepted; Patterns `CUE GO` `outcome:"Done"` → applied,
     `"Requested"` → accepted, `"Confirm"` → accepted with "Patterns asks for a second GO within 4 s — a person's" (Stagewatch never sends
     the second GO); `ERR @id words` → refused with the peer's words. No answer in 30 s → "unconfirmed — no answer in 30 s". A pre-1.1 desk
     gets no ids, so its ticket completes at once: accepted, "sent — this desk gives no receipts (older than Show Bus 1.1)".
   - `ExecuteWithReceiptAsync` waits for the ticket within the same 3 s; later answers reach WS and Companion as one follow-up receipt with
     the same id and `"final":true`, once, within 30 s.
   - Journal (`JournalKind.Playout`, appended): exactly one row per command that reached the socket, when its ticket completes (applied,
     refused, accepted-final, unconfirmed, no-receipt). A command not sent leaves a refused receipt and the status line, as `Refuse` does today.
   - Words are Stagewatch's where the peer gives none: "PlayMap Stage left took PLAYLIST GO Show · Intro (at 20:00:01.250)". Then the link
     looks for the effect in STATE for lead + 3 s and journals "seen: Show plays Intro on Stage left" or amber "not seen in Stage left's STATE".
6. **Local refusals only where the peer's own facts say so.**
   - Patterns, clip pause/stop: when `video.pausable`/`video.stoppable` are present (Patterns adds them with L48, patterns-playmap-side
     decision 12) they decide; absent, role `playlist` or `stinger` is refused in Patterns' own words, pointing at end/restart/seek. PlayMap
     is never refused by role.
   - PlayMap sync groups: a GO or CUE on a playlist with a `syncGroup` on a node whose STATE `sync.role` is `follower` is refused: "Stage
     right's Show follows sync group main (leader <name>) — GO its leader".
   - WHOAMI's `may` (a list not holding the verb) greys the desk's entries and turns the link amber ("Stage left is armed and lets this PC
     only read — its desk can SHOW SET REMOTE-VIEW-ONLY OFF"), but does not refuse locally: PlayMap's ERR is the refusal, with its words.
7. **Line cap kept; an oversize STATE is read in part and shown.** 256 KiB stays: the whole PlayMap STATE is about 76 KB with round-3
   sections still empty (3.4x margin, measured again by Mapper's fence). A STATE line over the cap keeps its first 256 KiB and is read
   forward-only (`Utf8JsonReader`) for `t,id,seq,at,video,playlists,sync,project.index,cuestack`; `video` and `playlists` come right after
   `air`, so the VT keeps working; `sync` and `project.index` come after `outputs`/`media`/`stage` and may be cut, and then together/AT and
   INDEX refresh are off for that link, said in words. The link is amber with the size ("STATE 312 KB — over this link's 256 KB") and
   counts `oversize`.
8. **Timed GO by measured offset, honest about what it knows.**
   - `T = now + playoutLeadMs` (300, 100–2000). A PlayMap member with a clock sample ≤ 10 s old whose rtt ≤ lead gets `AT <T + offset>`, the
     offset being the lowest-rtt sample among those ≤ 10 s old (the estimator keeps each sample's time).
   - A member without such a sample but with an rtt (Patterns, or a stale PlayMap clock) is held on its link and sent untimed at
     `T − rtt/2` (a `TimeProvider` timer on the link; a drop cancels it as "not sent").
   - If any member has no rtt at all, every member is sent untimed at once and the receipt says "not together: <link> has no clock yet".
   - Members on one PlayMap sync group collapse to one GO to the group's leader (the link whose STATE says `sync.role` `leader`); a leader
     that is not one of the links refuses the GO naming it.
   - The result is read from `playlists[<id>]`, not `video`: after AT the item must be `playing` with the cued id. PlayMap's `positionMs`
     comes from the anchor, so `at − positionMs` repeats the AT instant unless AT arrived after its instant and was clamped. The journal says
     "Stage left: scheduled for 20:00:01.250 (± 3 ms)", and "started late by 140 ms (AT arrived after its instant)" only when the gap exceeds
     rtt/2 + 20 ms. The picture actually reaching the outputs is PlayMap's round-3 observer's to report; not claimed here.
9. **The running order cues playout, without touching anyone's air or stack by surprise.** `RundownItem.Playout` (≤ 4 cues):
   `{link, list, item, label, standby, together}`, `standby` false by default.
   - On load (`IShowHost.ItemLoaded`), for a cue with `standby` on, at the end of the show thread's turn unless the item also started in it:
     PlayMap `PLAYLIST CUE <pl> <item>` only when that playlist is observed `idle` or `ended` in STATE ≤ 3 s old (else "not cued — Show is
     playing on Stage left; GO will play it"); the option reads "shows the item's first frame on air at load". Patterns `CUE STANDBY <id>`;
     the option reads "moves the Patterns desk's own standby".
   - On first start (`ItemStarted`): PlayMap `PLAYLIST GO <pl> <item> [AT …]`. Patterns `CUE GO <id>` only: Patterns' fence decides, so if
     its standby is not that cue (its operator moved it, or standby was off) Patterns refuses "standby moved" and the receipt and journal say
     so. Stagewatch never sends STANDBY and GO together.
   - The manual `playout <link> cue …` has the same idle guard and words.
   - Ids come from PlayMap's `INDEX` and Patterns' `CUE LIST`, cached and refreshed when `project.index` / `cuestack.listRev` moves. A cue
     naming something the peer does not list, or a link this PC does not have, is amber advice before the item and is skipped with a
     journal row.
   - The planner and the executor live in `PlayoutDirector` (Timer.Integrations), used by the desk and the rig alike.
10. **HTTP triggers authenticate only over a channel that keeps the secret.** `TriggerRule.Auth {kind: none|playmap|patterns|stagewatch|bearer, secret}`.
    - Headers: playmap `X-PlayMap-Token` + `X-Request-Id: tr<rule>-<n>`; patterns `X-Patterns-Token` + `X-Patterns-Client: Stagewatch` +
      body `@tr<rule>-<n> <line>`; stagewatch `X-Stagewatch-Pin` + `X-Request-Id`; bearer `Authorization: Bearer`.
    - The secret is attached only when the URL's host is loopback, or the scheme is `https` and the certificate is pinned (by the node id of
      the hello heard at that host:port, else `addr:<host>:<port>`, first use shown in Settings; a changed one is refused until trusted). A
      rule with a secret and `http://` to another machine cannot be saved, and is refused at fire.
    - With auth set, the scheme, host and port are fixed text: a placeholder there is refused at save; `Expand` runs only on the path and
      query. The authenticated client never follows redirects (`AllowAutoRedirect = false`).
    - The reply's JSON `ok`/`stage`/`error|msg` decides the outcome. The secret is DPAPI-protected at rest, resolved by the app, and in no
      status, journal row, log line, frame or context.
    - A query naming `pin|token|key|secret`: a warning at save, offering to move it into the profile; refused only on a rule that also has
      an auth profile; never refused at fire for a rule that ran before.
11. **One verb, safe target rules.** `playout [<link>] <action> …` (`ShowCommandKind.Playout`, appended after `Window`). `patterns …` is
    unchanged and means link `patterns` (else the only link). With no link named: `end|restart|play|pause|stop|seek` go to the only enabled
    link, else the link feeding the VT, else refused naming the links; `go` and `cue` go to the only enabled link, else refused "two playout
    links — name one: playout stage-left go …". Every `playout` action is risk High (Classify cannot see the peer, and a PlayMap CUE shows
    on air).
12. **Who may drive another node's air.** `playout` and `patterns` lines:
    - OSC and Companion TCP: from loopback, or from an address in `Remote.PlayoutFrom` (≤ 16 addresses, empty by default); otherwise
      refused with "playout from another machine over OSC/Companion TCP needs its address in Settings → Remote → May drive playout (these
      carry no PIN)". HTTP, WS and MCP: the PIN or a control/MCP key as today; with no PIN set, the same address rule applies.
    - AI origin (MCP `run_command`, a §11 proposal aimed at Stagewatch): always a proposal, even with agents acting directly. When the
      link's node has collected §11 approvals in the last 60 s, the proposal targets that node with the line the link would send (e.g.
      `PLAYLIST GO pl1a2b3c4 it9f8e7d6`), so the node runs it as Approved, with the proposer's name, under its own policy (risk from its
      own `commands`); otherwise it is a desk proposal that runs through the link once a person approves, journalled "approved by <who> for
      <proposer>".
13. **One VT, one move per item.** Each link may feed the VT; `PickPlayout` takes the running one in link order, then vMix. A clip end moves
    the running order only when that link fed the VT at that moment, its `nextOnEnd` is on, and no clip end has moved this item execution
    (`ShowController.Execution`).
14. **Every reader, named.** Context items `link:<name>` and `clock:<name>` are built from the link name only (not from `IntegrationStatus`
    names, whose loop skips playout links). The frame's `playout` row gains `link` and `node` at its end, so PlayMap and Patterns, which read
    Stagewatch's playout row (`Mapper/src/PlayMap.Net/Links/StagewatchTimer.cs:81`), can tell their own clip. `/api/config` (and the Companion
    config line) gains `playoutLinks:[{name,kind,label}]`. The copilot's facts gain each link's name, kind, label, paired, timed (can take
    AT), its playlists and items (ids and labels, ≤ 50) or cues (id, number, name, ≤ 50).
15. **The spec, once.** The words for §2 (kind `playmap`), §3 (the final follow-up receipt) and §8 (PlayMap's row) go to the spec-1-3 area's
    single master edit (Show Bus 1.3, its version decided there); the Patterns, AGMessage and PlayMap copies follow through that area's units
    S9, P3, A1 and M1. This area edits no copy.

## 2. Wire shapes

### 2.1 Stagewatch grammar (HTTP `/api/…` with `X-Request-Id`, WS `{cmd,args,id}`, OSC `/stagewatch/playout`, Companion TCP `@id PLAYOUT …`, MCP `run_command`)

```
playout [<link>] end [<1-3600>]             -> VIDEO END n
playout [<link>] restart                    -> VIDEO RESTART
playout [<link>] play | pause | stop        -> VIDEO PLAY | VIDEO PAUSE | VIDEO STOP   (never bare STOP)
playout [<link>] seek <s|m:ss|h:mm:ss>      -> VIDEO SEEK <seconds>
playout [<link>] cue <playlist> <item>      -> PlayMap: PLAYLIST CUE <pl> <item>      (idle/ended playlists only)
playout [<link>] cue <cue>                  -> Patterns: CUE STANDBY <cue>
playout [<link>] go <playlist> [<item>] [together]
                                            -> PlayMap: PLAYLIST GO <pl> <item> [AT <peer unix ms>]
                                               (no item: PLAYLIST GO <pl> EXPECT <observed next id>)
playout [<link>] go <cue>                   -> Patterns: CUE GO <cue id>      (Patterns' standby fence decides)
patterns <end|restart|play|pause|stop|seek …>  -> as today, on link patterns (else the only link)
```
Auth: HTTP/WS/MCP — the PIN, a control key (MCP: an MCP key); a node key may not. OSC/Companion TCP — loopback or `Remote.PlayoutFrom`.
AI origin — always a proposal (D12). Licence — Production.
Parse error: "playout takes end, restart, play, pause, stop, seek, cue or go". Execution refusals: "no playout link called x — links:
patterns, stage-left"; "two playout links — name one: playout stage-left go …"; "Stage left's kind is not known yet — no STATE heard";
"not cued — Show is playing on Stage left; GO will play it"; "Stage right's Show follows sync group main (leader Stage left) — GO its leader";
"playout from another machine over OSC needs its address in Settings → Remote → May drive playout"; "playout links need Stagewatch
Production". Risk: High for every action.

### 2.2 Receipts (Show Bus §3)
HTTP with `X-Request-Id: a1`:
```
200 {"type":"receipt","id":"a1","ok":true,"stage":"applied","effect":"PlayMap Stage left took PLAYLIST GO Show · Intro","error":null,"refs":{"peerRequest":17}}
409 {"type":"receipt","id":"a1","ok":false,"stage":"applied","effect":"","error":"PlayMap Stage left refused PLAYLIST GO: Show has no item #9","refs":{"peerRequest":17}}
409 {"type":"receipt","id":"a1","ok":false,"stage":"applied","effect":"","error":"Patterns: 'opener.mp4' is a playlist item — its own clock keeps running, so it is not paused (patterns end, restart or seek move it)","refs":{}}
409 {"type":"receipt","id":"a1","ok":false,"stage":"applied","effect":"","error":"not sent — the link to Stage left dropped","refs":{}}
200 {"type":"receipt","id":"a1","ok":true,"stage":"accepted","effect":"sent to PlayMap Stage left — waiting for its answer","error":null,"refs":{"peerRequest":17}}
200 {"type":"receipt","id":"a1","ok":true,"stage":"accepted","effect":"Patterns asks for a second GO within 4 s — a person's","error":null,"refs":{"peerRequest":4}}
```
WS follow-up, once, within 30 s, only after an `accepted` that waits on a peer:
`{"type":"receipt","id":"a1","ok":true,"stage":"applied","effect":"…","error":null,"refs":{"peerRequest":17},"final":true}` (`final` is a
new last field, written only on follow-ups). Unconfirmed: `{"…","ok":true,"stage":"accepted","effect":"unconfirmed — no answer in 30 s","final":true}`.
Companion TCP: `OK @c1 accepted sent to PlayMap Stage left — waiting for its answer`, then `OK @c1 applied PlayMap Stage left took …` or
`ERROR @c1 PlayMap Stage left refused PLAYLIST GO: …`.

### 2.3 Stagewatch → peer (line wire, one link)
```
HELLO Stagewatch                            <- (PlayMap) OK
@pn NONCE                                   <- OK @pn <hex>  (1.1)  |  ERR …  (older desk)  |  nothing in 5 s (fail closed)
@pa AUTH-PROOF <hex(HMAC-SHA256(TOKEN upper-cased without dashes/spaces, nonce + "Stagewatch"))>
                                            <- OK @pa paired | OK @pa open — … | ERR @pa wrong token (no more proofs)
AUTH <token>                                only a pre-1.1 desk, only when the socket's remote address is loopback
PING <unix ms>                              <- OK PONG t1 t2 t3            every 2 s
@ctx CONTEXT                                <- OK @ctx {§6 json}           every 5 s, after pairing
@wh WHOAMI                                  <- OK @wh {"paired","role","armed","may",…}   PlayMap, every 5 s and when show.armed moves
@ix INDEX                                   <- OK @ix {INDEX json}         PlayMap, after pairing and when project.index moves
@cl CUE LIST                                <- OK @cl {cues json}          Patterns, when cuestack.listRev moves and a cue or the editor needs it
@s<n> VIDEO END <s> | VIDEO RESTART | VIDEO PLAY | VIDEO PAUSE | VIDEO STOP | VIDEO SEEK <s>
@s<n> PLAYLIST CUE <pl-id> <item-id>
@s<n> PLAYLIST GO <pl-id> <item-id> [AT <T_local + offset_ms>]
@s<n> PLAYLIST GO <pl-id> EXPECT <next item id>
@s<n> CUE STANDBY <cue id>                  (manual cue, or standby on load when opted in)
@s<n> CUE GO <cue id>
```
Ids are bare; a typed name for PlayMap is quoted by the Show Bus line rule; a Patterns `CUE STANDBY` word with a control character or line
break is refused locally. Read from the peer, forward-only: `STATE` (`t, id, seq, at, video{file,role,tag,key,positionMs,lengthMs,paused,
rate,seq,pausable?,stoppable?}, playlists[{id,name,state,item{id,label},positionMs,syncGroup,next{id}}], sync{role,leader},
project.index, cuestack{armed,hold,listRev,standby{id,requireConfirm}}`), receipts, `NONCE <hex>`, `OK PONG …`.

TLS transport (PlayMap, `ports.https` + `tls` in its hello, or a pinned node id): `GET wss://<address>:<https>/ws`,
`Sec-WebSocket-Protocol: playmap.lines.v1`, no Origin, Host = the address; one text frame per line each way; inbound frames capped at
256 KiB; the greeting STATE after `OK paired`; 429 at the upgrade = locked out (red); close 4401 = wrong proofs (red, no redial with
proofs until the token changes). A pinned node id is never dialled plain.

### 2.4 Settings, running order, triggers (JSON)
```json
"integrations": {
  "playoutLinks": [
    {"name":"patterns","enabled":true,"kind":"auto","host":"10.0.0.5","port":9697,"token":"<DPAPI b64>","node":"",
     "transport":"auto","followVt":true,"nextOnEnd":false},
    {"name":"stage-left","enabled":true,"kind":"playmap","host":"10.0.0.12","port":9941,"token":"<DPAPI b64>","node":"a1b2c3d4",
     "transport":"auto","followVt":true,"nextOnEnd":false}],
  "playoutLeadMs": 300,
  "patterns": true, "patternsHost": "10.0.0.5", "patternsPort": 9697, "patternsToken": "<DPAPI b64>", "patternsNextOnEnd": false },
"remote": { …, "playoutFrom": ["10.0.0.40"] }
"rundown": { "items": [ { …, "playout": [
    {"link":"stage-left","list":"pl1a2b3c4","item":"it9f8e7d6","label":"Show · Intro","standby":false,"together":true},
    {"link":"patterns","list":"","item":"c0ffee12","label":"Cue 12 Walk-in","standby":false,"together":false}] } ] }
"triggers": [ {"enabled":true,"name":"GO VT","event":"ItemStart","filter":"4","action":"Http",
    "target":"https://10.0.0.12:9940/api/cmd","payload":"PLAYLIST GO show #4","auth":{"kind":"playmap","secret":"<DPAPI b64>"}} ]
```
`patterns*` are the legacy mirror of link `patterns` (written for one release, D1). `ShowFile.Sanitize`: ≤ 4 cues; link names by the name
rule; `list`/`item` `[A-Za-z0-9_-]{1,64}` or a Patterns cue id; `label` ≤ 80 characters, no control characters; anything else dropped.

### 2.5 Context (§6), frames, config
```
{"id":"link:stage-left","kind":"playout","label":"PlayMap Stage left","light":"green",
 "state":"paired by proof over TLS (pinned) · VT Intro 2:28 left · role remote, armed"}
{"id":"link:patterns","kind":"playout","label":"Patterns FOH","light":"green",
 "state":"paired by proof · plain TCP — Patterns offers no TLS on its wire · VT opener.mp4 (playlist item)"}
{"id":"clock:stage-left","kind":"health","label":"Clock vs PlayMap Stage left","light":"amber","state":"CLOCKS APART: 2.4 s ahead"}
```
Amber: not paired; an older desk off this PC; no NONCE answer; STATE over the cap; a different node id at the address; a pinned node with
no TLS hello; WHOAMI says read-only; a cue naming what the peer does not list or a link this PC lacks; "not seen in its STATE". Red:
unreachable, wrong token, locked out (429), CERTIFICATE CHANGED. Commands rows: `playout <link> go <playlist> <item>`, `playout <link> cue
<playlist> <item>`, `playout <link> pause|play|stop|end|restart|seek` — all high. Recent: one row per completed command, plus the
"seen"/"scheduled"/"started late" rows. Frame `playout{…,source,link,node}` (`link` and `node` new, last). `/api/config` and the Companion
config line: `playoutLinks:[{name,kind,label}]` last.

### 2.6 Show Bus words handed to spec-1-3's master edit
§2 kind table: `playmap` (PlayMap). §3: "a receipt with stage `accepted` that waits on another node may be followed, on WS and line
transports, by that command's final receipt with the same id and `"final":true`, once, within 30 s". §8: a PlayMap row; Stagewatch's
row consumes PlayMap's STATE, context and receipts over its playout links. No hello cap is added; the finder reads `ports.wire`,
`ports.https` and `tls`.

## 3. Changes, file by file

Stagewatch
- `src/Timer.Core/AppSettings.cs`: `PlayoutLinkSettings`; `IntegrationSettings.PlayoutLinks`, `PlayoutLeadMs`; legacy Patterns keys kept
  as the mirror; `RemoteSettings.PlayoutFrom`; `TriggerRule.Auth` (`TriggerAuth{Kind,SecretProtected}`); `CurrentSchemaVersion = 2`.
- `src/Timer.Core/SettingsStore.cs` (`Migrate`): the 1→2 upgrader, the mirror merge rule, sanitising links (≤ 8, names, reserved slugs,
  ports, kinds) and `PlayoutFrom` (≤ 16 parsed addresses); `Save` writes the mirror.
- `src/Timer.Core/Rundown.cs`: `PlayoutCue`, `RundownItem.Playout` (≤ 4), copied in `Clone()`.
- `src/Timer.Core/ShowFile.cs` (`Sanitize`): playout cues checked (§2.4).
- `src/Timer.Core/ShowState.cs`: `ShowCommandKind.Playout` after `Window`; `PlayoutArgs : ShowArgs`; parser verb; help line;
  `CommandReceipt.Final` (a Task, not serialised); `ToJson` writes `"final":true` last, only on follow-ups; `PlayoutView` gains `Link`, `Node`.
- `src/Timer.Core/SnapshotJson.cs`: `playout.link`, `playout.node` last in the row; `playoutLinks` last in the config.
- `src/Timer.Core/Journal.cs`: `JournalKind.Playout` appended.
- `src/Timer.Core/Resilience.cs`: `ClockOffsetEstimator` keeps each sample's time; `Best(maxAge)` → offset, rtt, age.
- `src/Timer.Core/PlayoutGate.cs` (new, pure): may this sender drive playout (`kind`, `sender`, `credentialed`, `PlayoutFrom`) → null or words.
- `src/Timer.Core/Approvals.cs`: `ProposalDesk.CollectedRecently(nodeId, now)` (last `Collect` per target) and `SubmitLocal(from, target,
  line, reason, risk)` for the re-targeted AI line (shared seam with cross-app-proposals).
- `src/Timer.Integrations/PatternsLink.cs` → `PlayoutLink.cs` (+ `PlayoutWire.cs`: proof, quoting, hello finder accepting `t:"playmap"` via
  `ports.wire`/`wire`, `ports.https`, `tls`; `PlayoutStateReader.cs`: forward-only, prefix-capable; `PlayoutTransport.cs`: TCP and WSS;
  `PlayoutTicket.cs`: ticket, `PeerAnswer`, the in-flight bound). The existing 9700 Find code stays as it is (spec-1-3 retires it).
- `src/Timer.Integrations/PlayoutLinks.cs` (new): the set, target rules, VT pick, sync-group collapse, Find over the live hello table.
- `src/Timer.Integrations/PlayoutDirector.cs` (new): the executor for `Patterns` and `Playout` commands (local refusals, ticket, journal on
  completion, observation), the cue planner (load, start, end of turn), timed GO (hold-until for untimed members), next-on-end once per
  execution, and the AI re-target helper. Used by the desk and the rig.
- `src/Timer.Integrations/PlayoutReceipts.cs` (new): a `CommandReceipt` from a ticket within the remaining deadline, and the `Final` one.
- `src/Timer.Integrations/Triggers.cs`: auth profiles and headers; the loopback-or-pinned-https rule (a second `HttpClient` on a
  `SocketsHttpHandler` with `AllowAutoRedirect = false` and the pin check); `Expand` on path and query only when auth is set; request ids;
  reply parsing; `Outcome` event; injected secret resolver and pins.
- `src/Timer.Integrations/Copilot.cs`: `Playout` → High.
- `src/Timer.Show/ShowContext.cs`: commands rows; `PlayoutItems(links)` builds `link:<name>`/`clock:<name>`.
- `src/Timer.Show/AiContext.cs`: `CopilotFacts.Playout` (bounded as in D14).
- `src/Timer.Remote/OscServer.cs`: `Dispatch(msg, sender)` and the gate for `Playout`/`Patterns`.
- `src/Timer.Companion/CompanionServer.cs`: the gate in `HandleWithReceipt` and `Handle`'s shared-grammar branch (the client's remote
  address); after an `accepted` with `Final`, a second line `OK @id applied …` / `ERROR @id …`.
- `src/Timer.Remote/WebServer.cs`: the gate when no PIN is set; `SendReceiptAsync` sends the follow-up when `Final` is set; config field.
- `src/Timer.Remote/McpEndpoint.cs`: a `playout`/`patterns` line is always handed to `ProposeFromAgent(…, mustPropose: true)`.
- `src/Timer.App/ViewModels/MainViewModel.Patterns.cs` → `MainViewModel.Playout.cs`: thin glue to `PlayoutDirector`; `PickPlayout` over links.
- `src/Timer.App/ViewModels/MainViewModel.Receipts.cs`: `ReceiptNotes.Peer` (a ticket) replaces `HandedOn`; `ExecuteWithReceiptAsync` awaits
  it within the same timeout via `PlayoutReceipts`.
- `src/Timer.App/ViewModels/MainViewModel.Copilot.cs`: `ProposeFromAgent` never returns null for a playout line; re-target via the director.
- `src/Timer.App/ViewModels/MainViewModel.Integrations.cs`: reconcile `playout:<name>` per link; names gone since the last pass get a
  `"False"` signature (retired by the existing `Reconcile`); `IntegrationStatus` lists each link for Settings.
- `src/Timer.App/ViewModels/MainViewModel.ShowBus.cs`: the integration loop skips playout links; `PlayoutItems`; `playmap => "PlayMap"`;
  contexts from every link merge into peer summaries, deduplicated by node id.
- `src/Timer.App/ViewModels/MainViewModel.Host.cs`: `ItemLoaded`/`ItemStarted` call the director; the turn-end flush is posted.
- `src/Timer.App/SettingsDialog.xaml(.cs)`: Playout links (Find, Add, Edit, Remove, Trust new certificate); Remote → May drive playout;
  trigger auth fields (PasswordBox) with the save-time rules. (PC work.)
- `src/Timer.App/RundownEditor.xaml(.cs)`: per-item cues with pickers from INDEX/CUE LIST; the standby option with its per-kind words;
  together. (PC work.)
- `src/Timer.App/MainWindow.xaml(.cs)`, `Help/HelpContent.cs`: VT chip right-click (play, pause, stop, restart, last 10 s, seek), greyed with
  the local refusal's or WHOAMI's words; running-order row right-click "Cue playout now", "GO playout now"; Help says a Patterns standby moves
  the Patterns desk's own standby and a PlayMap standby shows on air. (PC work.)
- `tools/Stagewatch.Rig/Program.cs`: `--playout name=host:port[,kind]` (token from `STAGEWATCH_RIG_PLAYOUT_TOKEN`), `--cue
  item=link:list:item[:standby][:together]`; its `Item()` calls `PlayoutDirector.ItemLoaded/ItemStarted`; receipts through `PlayoutReceipts`.
- `companion-module/src/{actions,variables,state}.ts`, `test/`: `playout_go`, `playout_cue`, `playout_transport` with a link dropdown from the
  config's `playoutLinks`; `vt_link`; the gate's refusal shown on the button's log.
- `tests/Stagewatch.Tests/PlayoutTests.cs` (new); `ShowBusTests.cs` and `Program.cs` renamed to the new types, the Program.cs fake answering
  an unknown verb with `ERR` (assertions unchanged).
- `tests/e2e/fakepeers.cjs`, `tests/e2e/playout.e2e.cjs` (new; `fakepeers.cjs` shared with cross-app-proposals). The real-app interop rows
  are the ecosystem-lane area's S4.1–S4.4 and S6.3, fed by `--playout`.
- Papers: `docs/REMOTE.md` (the verb, May drive playout, receipts and `final`, triggers' auth), `docs/COMPANION.md`, `docs/FEATURES.md`,
  `docs/WHATS_NEXT.md` (2.18, what it did not do, the rollback note, the OSC/Companion change for `patterns`), `docs/NODES.md` §5 (the link
  as rebuilt), `README.md`. `docs/SHOWBUS.md` through spec-1-3 (D15).

Mapper (docs and one test; no code), after round 3's integration pass
- `docs/SHOWBUS-NODE.md`: §2 Stagewatch rows (Find lists PlayMap; the link pairs by proof, over `/ws` with TLS when served, never plain
  once pinned; triggers need `X-PlayMap-Token` over https or a trusted address); §5 item 5 done.
- `tests/PlayMap.Net.Tests/…/StagewatchSessionTests.cs` (or Engine tests): a `HELLO Stagewatch` greeting STATE for a stress project keeps
  `video` and `playlists` within the first 16 KiB and records the whole STATE's size; PONG's t3 and `AT` read one wall clock.

Patterns: no code in this area. Its `SHOWBUS.md` copy follows through spec-1-3's P3 round; `video.pausable`/`stoppable` are
patterns-playmap-side's Round 91 (L48).

## 4. Tests

Stagewatch suite (`tests/Stagewatch.Tests/PlayoutTests.cs`, Linux and Windows; time through `TimeProvider`, so a manual clock):
1. Finder: `t:"playmap"` (top-level `wire` and `ports.wire`) and `t:"patterns"` hellos found with kind, id, wire, https, tls; `t:"main"` not.
2. Pairing, 1.1 fakes on loopback: `@pn NONCE` asked; the proof uses the answered nonce; the token on no line; a greeting NONCE delayed 3 s
   changes nothing; CONTEXT/WHOAMI/INDEX only after `OK paired`.
3. Pairing rules: the ShowBusTests fake (greeting NONCE, `ERR` to NONCE) pairs by the greeting nonce and never sees `AUTH `; an older
   desk at an injected "remote" address gets no `AUTH` and the link says why; on loopback it gets `AUTH`; no NONCE answer in 5 s off this PC
   → redial with words, no `AUTH`; `ERR wrong token` → no second proof until the token changes.
4. Tickets: `OK @s1`, `ERR @s1 …`, `OK @s1 {"stage":"accepted"…}`, Patterns `{"outcome":"Confirm"}`, silence 30 s, a drop with one line
   written and one queued, the 33rd send, an id-less old desk → applied, refused, accepted, accepted (no second GO written), unconfirmed,
   unconfirmed + "not sent", refused "not sent", accepted "no receipts". One journal row per written command, none for the unsent.
5. Roles and peer facts: Patterns `role:"playlist"` → pause/stop refused locally, no bytes; Patterns `pausable:true` → sent; PlayMap
   (`t:"playmap"`, `role:"playlist"`) → sent; PlayMap follower (`sync.role:"follower"`, `syncGroup:"main"`) → GO refused with the leader's
   name; WHOAMI `may:["WHOAMI"]` → sent (ERR is the refusal), link amber.
6. Oversize: a 300 KB STATE with `video` first → VT read, `oversize` 1, amber with the size; `sync` past 256 KiB → together refused for that
   link with words; `video` past 256 KiB → no VT, amber.
7. Clock and AT: fake PONG +5000 ms → `go … together` sends `AT` = local T + 5000 ± rtt; the best sample among those ≤ 10 s old; a Patterns
   member is written at T − rtt/2 on the manual clock; a member with no rtt → all untimed with the words; two links of one sync group → one
   GO to the leader; a STATE whose `at − positionMs` is 140 ms past T_peer → "started late by 140 ms"; within rtt/2 + 20 ms → "scheduled for".
8. Planner (pure): load+start in one turn → GO only; load with standby off → nothing; standby on and playlist idle → CUE at turn end;
   playlist playing → no CUE with the words; Patterns cue at start → `CUE GO <id>` only, never STANDBY+GO; Patterns `ERR standby moved`
   reaches the receipt and journal; an unknown link → advice and a skip row.
9. Grammar and risk: the forms parse; unknown action refused; `patterns pause` stays `ShowCommandKind.Patterns`; every `playout` action
   High; target rules (two links: transport goes to the VT link, `go` refused "name one"; unknown kind refused).
10. Gate: `PlayoutGate` table (OSC/Companion × loopback/LAN/listed; HTTP with and without a PIN); OSC `/stagewatch/playout stage-left go
    show intro` from an injected LAN sender is refused and the fake peer hears nothing; MCP `run_command playout …` with agents acting
    directly → a proposal, re-targeted to the node when it collected approvals (target id, the PlayMap line, risk from its commands).
11. Settings: a 2.17 file → one link `patterns` with the same host, port, token, next-on-end; saving writes the mirror; a file whose
    legacy keys differ from the mirror → link `patterns` takes them; a file with no `playoutLinks` → rebuilt; reserved and action-word names
    refused. `ShowFile.Sanitize` drops a fifth cue, a bad id, a long label.
12. Next on end: two links ending 100 ms apart in one item execution → one `Next()`; the link not feeding the VT never moves the order.
13. Context: `link:<name>`/`clock:<name>` from link names; a link named like an integration refused; the kind label `PlayMap`; frame
    `playout.link`/`node` last; config `playoutLinks` last; AI facts bounded.
14. Triggers against loopback fake HTTP and HTTPS servers: headers and request id present; PlayMap 409 → refused with its error; Patterns
    200 `{"ok":false}` → failed; `stage:"accepted"` → accepted; a secret rule to `http://<LAN>` refused at save and at fire; a 302 from the
    authenticated target is not followed; a placeholder in the host refused; a first-use pin then another certificate → refused; `?pin=` in
    an existing rule fires as before with a warning; the secret is in no status, journal or log line.
15. TLS (WSS): an in-process TLS WebSocket server (self-signed P-256): first use pins the hello's fingerprint; another certificate →
    CERTIFICATE CHANGED, nothing sent; a pinned node whose hello loses `tls` → no plain dial, amber; 429 at the upgrade → red words; no
    subprotocol → refused.
16. DPAPI round trip of a trigger secret: `Bench.OnWindows("trigger secret protected at rest")`.

Rig e2e (`tests/e2e/playout.e2e.cjs` with `fakepeers.cjs`): rig with a fake PlayMap and a fake Patterns over HTTP, WS and Companion: PlayMap
answers reach receipts (applied with Stagewatch's "took" words, refused with PlayMap's, accepted then `final`); `patterns pause` on a
playlist item → 409 and the fake hears nothing; Companion from loopback works; context shows both links and the journal rows; running order
`next` with standby off → GO only, with standby on → CUE then GO, `together` → `AT`; a Patterns cue → `CUE GO <id>` only; neither fake
sees the token. Real apps: ecosystem-lane S4.1–S4.4 and S6.3.

Mapper: the STATE/clock fence (Linux and Windows jobs). Patterns: none in this area.

## 5. Units, in order

1. Stagewatch — model and grammar: settings v2, migration and mirror, `PlayoutCue` + `ShowFile.Sanitize`, `ShowCommandKind.Playout`
   (parser, help, High risk, context rows), `JournalKind.Playout`, `ClockOffsetEstimator` sample times, reserved names, licence words.
   (G01, G29 model)
2. Stagewatch — who may drive: `PlayoutGate`, `Remote.PlayoutFrom`, OSC/Companion/HTTP gates for `playout` and `patterns`. (security, G01)
3. Stagewatch — wire primitives: `PlayoutWire` (proof, quoting, finder accepting playmap), `PlayoutStateReader` (prefix). (G01, G26)
4. Stagewatch — `PlayoutLink` over TCP: the pairing rules, backoff with jitter, tickets with the in-flight bound and drop rules, kind
   profiles, local refusals (pausable/role, sync follower), WHOAMI/CONTEXT/INDEX/CUE LIST after pairing, oversize; old tests moved, the
   Program.cs fake's one line. (G08, G07 link side, G48, G26)
5. Stagewatch — the set, the director and receipts: `PlayoutLinks`, `PlayoutDirector` (executor, journal on completion, observation,
   next-on-end once), `PlayoutReceipts`, WS/Companion `final`, context per link, frame `link`/`node`, config, AI facts, AI routing
   (`ProposalDesk` additions), licence gate; rig `--playout`; e2e. (G01, G07, G48)
6. Stagewatch — PlayMap over TLS: WSS with pins, no downgrade, 429/4401 words, Trust new certificate. (G08)
7. Stagewatch — running-order cues and timed GO: planner, opt-in standby with guards, hold-until, sync-group collapse, observation words;
   rig `--cue`; e2e. (G29, G28)
8. Stagewatch — HTTP trigger auth: profiles, loopback-or-pinned-https, no redirects, fixed authority, request ids, reply parsing,
   query-secret warning. (G30)
9. Stagewatch — the desk (PC work): Settings links, May drive playout, trigger auth; RundownEditor cues; VT chip and row menus; Help.
   (G01, G29, G30)
10. Stagewatch — Companion module: playout actions, `vt_link`, tests. (G01, G29)
11. Stagewatch — papers: REMOTE, COMPANION, FEATURES, WHATS_NEXT 2.18, NODES §5, README; the words to spec-1-3's S1. (all)
12. Mapper — SHOWBUS-NODE.md rows and the STATE/clock fence, after round 3's integration pass. (G30 doc, G26 fence, G28 assumption)

Outside this area, named so nobody waits on them silently: spec-1-3 S1/S9 (the master) and P3/A1/M1 (the copies); patterns-playmap-side
Round 91 (`pausable`/`stoppable`); ecosystem-lane S4 rows.

## 6. Gaps

Closed: G01; G07; G29; G30 (Stagewatch side; PlayMap's doc in unit 12); G28 (Stagewatch side: timed GO by measured offset, held untimed
members, sync-group leader, late starts reported); G26 (Stagewatch side; PlayMap's fence in unit 12); G48 (refused up front in Patterns'
words until Patterns publishes `pausable`, which then decides); G08 (no token in clear off this PC, ever; proofs on the asked nonce; TLS
for PlayMap with no downgrade).

Left:
- G08 — TLS on Patterns' wire 9697 is Patterns' (L55, low); its link stays proof-paired plain TCP and says so.
- G48 — the cure (pause that holds a playlist item's clock) is Patterns' L48, designed by patterns-playmap-side for Round 91.
- G28 — PlayMap-side items (publishing `links.stagewatch.clock`, its own offset for AT, a Stagewatch-fed timecode source, R3.7) are Mapper
  code outside round 3's ownership; not needed for a timed GO. The picture actually reaching the outputs waits on PlayMap's round-3 observer.
- G26 — the Mapper fence and the SHOWBUS-NODE.md rows land only after round 3's integration pass.

## 7. Risks

- Rewriting the Patterns link can regress the VT countdown: the old fake-desk tests move first, the ShowBusTests fake unchanged, the
  Program.cs fake changed by one line to behave like a real pre-1.1 desk; ids `link:patterns`/`clock:patterns` kept.
- A pre-1.1 Patterns desk on another PC no longer pairs (fail closed, said in words: update Patterns). Intended.
- `patterns` and `playout` from another machine over OSC or Companion TCP are refused until the address is listed: an existing OSC setup
  that drives Patterns from a lighting desk stops on upgrade, with the refusal's words on the OSC status line and in WHATS_NEXT.
- A rollback to 2.17 keeps link `patterns` (the mirror) and drops the others; said in WHATS_NEXT.
- A follow-up receipt with the same id may surprise a client resolving on the first; marked `"final":true`, once, documented in §3.
- Timed GO assumes PlayMap reads PONG and AT from one wall clock (`EngineHost.cs:121`), held by Mapper's fence; a broken assumption shows
  as "started late" rows.
- The prefix reader relies on `video` and `playlists` coming early (Mapper's rule: new keys at the end); if that fails the link is amber.
- Wrong proofs over `/ws` count toward PlayMap's per-address lockout: one wrong token stops proofs; 429 is shown red.
- The AI re-target relies on the node collecting approvals; a node that stops collecting leaves its proposal `undelivered` after 60 s, shown.
- WPF parts (Settings, RundownEditor, menus, Help) are built and walked on the maintainer's PC; a cloud session names them and leaves them.
- `SlidesAgent` also chooses https from the hello (`SlidesAgent.cs:186-199`), the same downgrade shape; not changed here (named in what
  the round did not do).

## 8. What this design does not do

- No TLS on Patterns' wire; no change to Patterns or PlayMap code.
- No real observation of the picture on PlayMap's outputs (round-3 observer); "seen" means seen in STATE.
- No removal of the 9700 Find (spec-1-3, after Patterns L47's walk).
- No change to `SlidesAgent`'s scheme choice (same hello-decides shape as issue 15).
- No raw line passthrough: the link only sends lines it builds.

## 9. Process notes

Stagewatch (`Stagewatch/CLAUDE.md`):
- A new command is a `ShowCommandKind` appended at the end, a parser verb, a help line, a `Copilot.Classify` risk and `ShowContext.Commands` rows.
- New frame, receipt and config fields go at the end of their objects (tests match substrings).
- Comments state invariants; no model or tool identifiers in anything pushed.
- No `Thread.Sleep`, `Environment.TickCount`, `GC.Collect` or unbounded `Task.Wait` in `src/` (BannedSymbols.txt); the link waits on its
  cancellation handle; new code reads time through `TimeProvider`; jitter from `RandomNumberGenerator`, not `System.Random`.
- Culture-invariant parsing and formatting; one `dotnet build` at a time.
- Windows-only checks (DPAPI, WPF) through `Bench.OnWindows`, never counted as passed elsewhere.
- `tools/test.sh` lanes build, unit, module, holo and e2e green; real-app interop is ecosystem-lane's lane.
- Secrets (link tokens, trigger secrets, the PIN) DPAPI-protected at rest; in no log line, journal row, frame, context or status.
- WPF work (Settings, RundownEditor, menus, the UI smoke via `tools/smoke.ps1`) is PC work, named and left by a cloud session.
- Papers: REMOTE.md, COMPANION.md, FEATURES.md, WHATS_NEXT.md (2.18 with what it did not do), NODES.md §5, README; SHOWBUS.md through spec-1-3.
- `.sh` files stay LF; `docs/BUILD.md` is not touched.

Mapper (`Mapper/CLAUDE.md`, `docs/round3/PLAN.md`):
- No code change. Neither `docs/SHOWBUS-NODE.md` nor a test beside `src/PlayMap.Net/Links` has a round-3 owner, and a folder not named for a
  wave is not edited in it (§4), so unit 12 lands after round 3's integration pass, or its doc rows reach the integrator as a papers note.
- The fence passes on both CI jobs with the analyzers on and asserts fields and sizes, not timings.

Patterns (`Patterns/CLAUDE.md`): no change in this area. Its `SHOWBUS.md` copy is spec-1-3's P3 round (PLAN §NN, REVIEW, CHANGELOG naming
the evidence class, OPEN rows, `Round NN.k` commits, all seven suites and the module's tests green, no walk: no platform path). `pausable`/
`stoppable` with L48 are patterns-playmap-side's Round 91 under the same rules.

AGMessage: untouched here; its `SHOWBUS.md` copy is spec-1-3's A1.
