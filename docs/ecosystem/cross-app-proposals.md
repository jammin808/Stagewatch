# Cross-app proposals: one desk sends to every app, and reads each app's risk correctly (revised after review)

Area: cross-app-proposals. Gaps: G02, G03, G05, G13, G14, G20, G33.

Paths are repo-relative. A Stagewatch path without a prefix is under `Stagewatch/src/`. Line numbers are from the trees as read on
2026-10-25. Design only: nothing in any repo was edited or built. The matcher prototype is outside every repo:
`<scratch>/proto/verbsyntax2.py` (the first draft's
`verbsyntax.py` is kept beside it for the comparison).

---

## Review answered

All 21 items were checked against the code. 19 are accepted. Items 7 and 16 are accepted in part, and the rejected part of each is given with
its reason.

| # | Item | Verdict | Checked | What changed |
|---|---|---|---|---|
| 1 | `peer`/`desk` open over HTTP and WS when no PIN is set | **Accepted** | `Timer.Remote/WebServer.cs:870` (`CheckPin` is null when the PIN is empty), `:1149,1191` (WS `role=control` controls with no PIN), `:803-806` | D11: the sender gate playout-links already designs (D12 there: `PlayoutGate`, `Remote.PlayoutFrom`) covers `peer` and `desk`. With no PIN they come only from this PC or a listed address. Tests on HTTP and WS with an empty PIN. |
| 2 | Unlisted lines make Stagewatch's PIN a relay for a peer's full wire | **Accepted** | Patterns runs an approved line as a wire client (`Patterns/src/Patterns.App/Services/AppServices.cs:682-686`, `OriginKind.Tcp`); `Patterns/docs/REMOTE.md:87-88` (`RESTART`/`UPDATE APPLY <passcode>`) | D11: `peer` sends only a line that matches a listed form of a verified table. Anything else is refused with words. |
| 3 | Peer-native secrets pass the fence | **Accepted** | `Mapper/src/PlayMap.Core/Wire/VerbCatalog.cs:249-250` (`[<token>]`, `[<key>]`, `SecretFrom = 3`), `:941-945`; context rows 72-73; proposals readable with a read key (`WebServer.cs:604-605,616-617`); `RunOperatorLine` records to Companion (`Timer.App/ViewModels/MainViewModel.cs:694-698`, `Timer.Companion/CompanionServer.cs:118-122`) | D4: §6 secret placeholders (by name) and an optional row field `secret`. A line that fills a secret with anything but `‹secret›` is refused at every door. Peer lines never reach the recorder. |
| 4 | Re-joining tokens corrupts the line | **Accepted** | `Timer.Remote/McpEndpoint.cs:173-185` and `Timer.Integrations/CloudAdvisor.cs:163-179` drop `"`; `Timer.Companion/CompanionProtocol.cs:73,77` (escapes, `\n` → newline); `WebServer.cs:792` (split on `/`, then unescape); `Timer.App/ViewModels/MainViewModel.Systems.cs:753` | D11: the line is exactly one argument. More tokens after `<app>` are refused; control characters are refused. The dialog and the copilot pass the raw string. `POST /api/peer` takes a JSON body. Round-trip tests on every door. |
| 5 | Proposal ids restart at p1; targets skip a reused id | **Accepted** | `Timer.Core/Approvals.cs:52,101`; `Mapper/src/PlayMap.Net/Links/StagewatchLink.cs:103,538-541`; `Patterns/src/Patterns.App/Services/StagewatchLinkService.cs:460`; `AGMessage/app/server.js:1098-1100` | D5: ids carry a run tag (`p17-k3m9`). `refs.sharedProposal` is a string. A restart test against fakes of all three collectors. |
| 6 | G15 must block, not sit in the risks | **Accepted** | `Timer.App/ViewModels/MainViewModel.Desk.cs:65-70` (the bind's `false` ignored), `:81-82` (a posted context beats the Patterns link's) | D6: SW-1 depends on peers-not-screens SW-1 (`NodeBindings`, decision 8). Only verified tables set risk, feed the model or allow a `peer`. The link-read Patterns context wins. Peers' `words` go to the model marked as that app's data. |
| 7 | "0 wrong" holds only on single-word examples; RiskWhen is invisible | **Accepted, in part** | RiskWhen/HighFor/carried lines: `VerbCatalog.cs:47-48,284,350,389-390,541,794-834`; only `v.Risk` is emitted (`Mapper/src/PlayMap.Core/Bus/ShowContext.cs:95`). Re-run with prototype 2. | D1 and D3: a value is one word except a form's last element or a `words`/`line` value. Carried lines raise the risk. Specificity breaks ties. PlayMap writes RiskWhen and HighFor as rows of their own (PM-0), and a fixture generated from `RiskOf` fences "never lower". **Rejected part:** "trailing words absorbed" is not a mis-rating when the absorbing value is the form's last. All three targets name the verb by its leading words, and PlayMap's own `RiskOf` rates `MOVE it1 3 BLACKOUT ON` as MOVE (`VerbCatalog.cs:808-813`). The probe now says so. |
| 8 | `desk approve` from triggers and automation; a blind `next` | **Accepted** | `Timer.Integrations/Triggers.cs:56,81`, `Timer.App/ViewModels/MainViewModel.Integrations.cs:59` | D12: `desk` is refused from triggers, the advisor, the copilot, MCP and a proposal. `next` approves only a non-high item. A high item needs its id and the word `high`. The deck approves the id it shows. |
| 9 | Wrong reason for the sink collecting | **Accepted** | `Mapper/src/PlayMap.Engine/EngineCore.cs:330` (a shown copy never runs), `VerbCatalog.cs:950-958` | D19: the corrected reason is that PlayMap's own secret-bearing proposals would be *refused as shown copies*. A test for orphaned forwarded proposals; one `ran` counter that names its collector. |
| 10 | `from.t` main/mcp spoofable | **Accepted** | `Approvals.cs:86-92,204-211` | D8: POST refuses `from.t` `main`/`mcp` and `from.id` equal to the desk's id. |
| 11 | APPLY re-proposed by another node | **Accepted** | `StagewatchLinkService.cs:482-495`; `Patterns/src/Patterns.Core/Services/ShowBusNodes.cs:233-240` (`LocalId` ignores `from`) | D20: Patterns runs `APPLY` only for a proposal it made itself, under the same desk id. Fenced in Round 90.3. |
| 12 | decidedBy leaks addresses and key names to meetings | **Accepted** | `AGMessage/app/server.js:1073` | D11: targets get a role word. Who and from where stay in the desk's journal. |
| 13 | `stagewatch`/`main` as an app word | **Accepted** | `state=approved` is only in `WebServer.cs:575,600` (serve side) | Dropped from `<app>`. |
| 14 | Nested receipts lose the outer one | **Accepted** | `Timer.App/ViewModels/MainViewModel.Receipts.cs:18,29-38`; `MainViewModel.Desk.cs:157` | D10: `RunWithReceipt` saves and restores the outer notes. `Decide` returns the inner outcome. |
| 15 | Copilot: silent 400 cut, order across apps, Stagewatch-only words, Help advertises `peer`/`desk` | **Accepted** | `Timer.Integrations/Copilot.cs:74,189`; `McpEndpoint.cs:87,139`; `MainViewModel.Copilot.cs:137,154` | D14: refused not cut; steps run in order and wait for an app step's result; separate words for app steps; `Help` for AIs leaves `peer`/`desk` out. |
| 16 | A >64 KB context is refused whole and nobody is told; chars, not bytes | **Accepted, in part** | `Timer.Core/ShowBus.cs:43,60` (UTF-16 length), `WebServer.cs:457,1523,1613` | D2: count UTF-8 bytes; answer 413 with the size; the row says so. **Rejected part:** "trim rows rather than refuse" cannot be the reader's job, because the body is never read past `MaxBody` (`WebServer.cs:1613`), which protects the server. Trimming is the sender's duty under spec-1-3's §6 sizes. Measured: PlayMap's busy context is 38,142 bytes compact and about 47.5 KB at 256 rows. |
| 17 | Copilot cost not governed by need; over-building | **Accepted** | measured: 199 PlayMap rows = 32,843 bytes compact; verb heads = 2,840 bytes | D14: verb heads first; full tables only when the request names the app or the model asks (one second pass). D18: two releases. A is the matcher, the fences, `peer`/`desk` on HTTP/WS, the desk and pages. B is Companion/OSC, copilot targets and MCP targets. |
| 18 | `refs.proposal` long vs MCP integers; `words` clash | **Accepted** | `Timer.Core/ShowState.cs:1201` (`Refs` is `IReadOnlyDictionary<string,long>`); `Approvals.cs:31`; `McpEndpoint.cs:153` | `refs.sharedProposal` is a string (new `RefIds` beside `Refs`). The POST reply's field is `formWords`. |
| 19 | §3 `final`, copy rule, version | **Accepted** | `design/spec-1-3.md:13-15,240-252`; `design/playout-links.md:130,249-250` | No §3 words here: `peer` uses playout-links' `final` follow-up as spec-1-3 carries it. The copies are byte-identical under spec-1-3's body hash. 1.3 is named. |
| 20 | Weak or breaking tests | **Accepted** | `tests/Stagewatch.Tests/ShowBusTests.cs:292-322` (4-arg `Submit`); `Copilot.cs:11-20` (`CopilotRisk` in Integrations) | Rig options fix the freshness window and poll interval, so each e2e asserts one outcome. Fakes collect with node keys. The old `Submit` overload is kept. A Core `LineRisk` enum; `Classify` maps it. |
| 21 | Deck shut out when Companion is on another PC; 2048-char line over the header cap | **Accepted** | `Timer.Companion/CompanionServer.cs:40-44`; `WebServer.cs:27` | The gate's address list (shared with playout-links) lets a Companion PC in. Companion gets a per-connection `ACCESS` line, so the module greys its actions. `POST /api/peer` takes a JSON body. |

The "missing" list:
- **G15 dependency:** item 6.
- **Restart-unique ids:** item 5.
- **No-PIN policy:** item 1.
- **Per-argument risk and adversarial lines:** item 7.
- **Peer-native secrets:** item 3.
- **The approval gate:** item 8.
- **Pages:** the producer page gets a desk panel (D17).
- **PlayMap chains:** the NetworkDrawer and right-click door (`DeskVerbRuleTests`), the Companion `verbs.json` regenerated, and `EngineCore.Assistant.cs` belonging to workflows in W4 (`Mapper/docs/round3/PLAN.md:2185`). The critique's "MenuCoverageTests" does not exist in Mapper; the real fences are `tests/PlayMap.App.Tests/DeskVerbRuleTests.cs` and `GeneratedMenuTests.cs`.
- **Freshness on approve:** D9.
- **Patterns' readers:** the CONTEXT reply (`Patterns/src/Patterns.App/Services/CommandRouter.cs:96`), `/api/context` (`ControlService.cs:1086-1090`) and the posted context (`AppServices.cs:681`). Node mode is a separate list (`ShowBusNodes.cs:168-176`) and is unchanged; the papers say so.
- **WEB TYPE/CLICK:** WEB CLICK is listed; WEB TYPE is not, and the papers say why.
- **§3 and the copy hash:** item 19.

---

## 0. What exists (evidence)

**The desk (Show Bus §11; this Stagewatch keeps the queue)**
- `ProposalDesk` (`Timer.Core/Approvals.cs:40-238`) holds the queue.
  - `Submit` takes any alphanumeric `from.t`, including `main` and `mcp` (`:204-211`).
  - It binds a node key to `from.id` (`:92`).
  - It cuts `line` to 400 characters **without saying so** (`:42,88`).
  - It rates the line with `risk(target, line)` (`:102`).
- Ids are `p` plus an in-memory counter (`:52,101`), so every restart begins again at `p1`.
- Every target remembers the ids it has run, and skips one it has seen:
  - PlayMap: `_ran`, kept for the link's life (`Mapper/src/PlayMap.Net/Links/StagewatchLink.cs:103,538-541`);
  - Patterns: `_ranDeskIds` (`Patterns/src/Patterns.App/Services/StagewatchLinkService.cs:460`);
  - AGMessage: `ranProposals`, with the cached `reported` flag, so it never reports again (`AGMessage/app/server.js:1098-1100`).
- `RiskFromCommands` (`:225-237`) is a prefix matcher.
- `PeerContexts.Parse` keeps 64 commands, cuts each verb to 80 characters and drops `words` (`Timer.Core/ShowBus.cs:101-102,48-49`).
- `Accept` measures UTF-16 length (`:60`). The HTTP route measures bytes (`WebServer.cs:457`), and the request reader stops at `MaxBody` 64 KiB
  (`:1523,1613`).
- The risk lookup (`Timer.App/ViewModels/MainViewModel.Desk.cs:73-84`):
  - uses `Copilot.Classify` for the desk itself;
  - for a peer, takes a *posted* context in preference to the Patterns link's (`:81-82`);
  - ignores a failed key bind (`:65-70`; G15).
- Approving a shared item ignores the tick: `ApproveProposal` sends any shared item to `ApproveShared` (`MainViewModel.Copilot.cs:165`), which approves
  it (`MainViewModel.Desk.cs:135-159`). A high shared step is never pre-ticked (`MainViewModel.Copilot.cs:25`).
- `ApproveShared` runs a desk-targeted line through `RunWithReceipt` (`MainViewModel.Desk.cs:157`). `_receipt` and `_executingRemote` are single
  fields, reset in `finally` (`MainViewModel.Receipts.cs:18,29-38`).
- `CommandReceipt.Refs` holds only longs (`Timer.Core/ShowState.cs:1201,1213`).

**Doors and tokenizers**
- No PIN means no gate:
  - `CheckPin` returns null when the PIN is empty (`WebServer.cs:870`);
  - a `/ws?role=control` socket then controls (`:1149,1191`);
  - the generic `/api/<verb>` path asks only for Control scope (`:798-806`).
- Every tokenizer drops quote marks: MCP (`McpEndpoint.cs:173-185`), the advisor's, which triggers use through `RunCommandLine`
  (`CloudAdvisor.cs:163-179`, `MainViewModel.Systems.cs:751-756`), and Companion's. Companion's honours `\"` and turns `\n` into a newline
  (`CompanionProtocol.cs:64-90`).
- The HTTP path splits on `/` before unescaping (`WebServer.cs:792`). WS `args` keep each string exact (`:1342-1345`). A WS message is at most
  4096 bytes (`:28,1301-1313`). A request header is at most 8192 bytes (`:27`).
- Command triggers run lines with no transport gate (`Timer.Integrations/Triggers.cs:56,81`, `MainViewModel.Integrations.cs:59`). The advisor's
  verbs are an allow-list without `peer`/`desk` (`CloudAdvisor.cs:22-23,158`).
- `RunOperatorLine` offers its line to Companion's recorder, which sends `RECORD <line>` to every subscribed client
  (`MainViewModel.cs:694-698,113-117`, `CompanionServer.cs:118-122`).
- Companion and OSC bind loopback unless the operator binds all (`CompanionServer.cs:40-44`, `Timer.Remote/OscServer.cs:36-39`). Neither
  authenticates.
- playout-links already designs one sender gate for verbs that drive another node (`design/playout-links.md` D12, units 2 and 5: `PlayoutGate`,
  `Remote.PlayoutFrom`).

**Who can put a line in the queue today**
- Only `POST /api/proposals` (`WebServer.cs:589-595`). Any proposal is readable with a read key (`:604-605,616-617`).
- The copilot cannot:
  - `CopilotStep` has no target (`Timer.Integrations/Copilot.cs:23`);
  - it keeps only Stagewatch grammar (`:205-211`) and cuts commands at 400 (`:189`);
  - its prompt and MCP's tool text carry `ShowCommandParser.Help` (`:74`, `McpEndpoint.cs:87,139`).
- MCP `get_proposal` takes only integers, which are local copilot ids (`McpEndpoint.cs:151-155`).
- The Show network panel is read-only (`Timer.App/MainWindow.xaml:691-722`). No page shows the queue (`Timer.Remote/wwwroot/producer.js`
  reads the operator frame).

**How each target runs an approved line**
- **PlayMap** collects every 2 s (`StagewatchLink.cs:471`) and runs the desk's copy as transport `approval`
  (`StagewatchLink.cs:560-569`, `Mapper/src/PlayMap.Core/Show/ShowPolicy.cs:35-36`). The engine refuses any *shown copy* (secret words replaced by
  `‹secret›`) before running it (`Mapper/src/PlayMap.Engine/EngineCore.cs:330`, `VerbCatalog.cs:950-958`).
- **Patterns** runs a line through its full router as a wire client (`AppServices.cs:682-686`). It runs `APPLY <localId>` from its own record,
  whoever proposed it (`StagewatchLinkService.cs:482-495`, `ShowBusNodes.cs:233-240`).
- **AGMessage** takes only `notice <text>`, every 3 s. It shows `decidedBy` to every open meeting (`server.js:1061-1101`, `:1073`).

**Risk the desk cannot see** (PlayMap's `RiskOf`, `VerbCatalog.cs:794-834`)
- A per-word risk (`RiskWhen`, `:47-48`), applied upward or downward:
  - `DECODE POLICY … FORCE` → high (`:350`);
  - `MACHINE SET GPU-PRIORITY REALTIME` → high (`:541`);
  - `SEND ENABLE <send> ON` → review (`:371`);
  - `MEDIA OPTIMISE CANCEL|PAUSE|RESUME` → low (`:353`);
  - `BIND LEARN OFF` and `TIMECODE SOURCE RESET` → low (`:390,398`).
- `HighFor`: `ASSISTANT SET enabled|provider` → high (`:284`).
- A carried line: `BIND ADD … TO <target line>` and `BIND LEARN <target line>` are at least the carried line's risk (`:389-390,826-834`).
- The context emits only `v.Risk` (`Mapper/src/PlayMap.Core/Bus/ShowContext.cs:85-95`). That file is the integration pass's
  (`Mapper/docs/round3/PLAN.md:2135`, §5.6 item 7).
- Secret words: PlayMap knows them per verb (`SecretFrom`; `LINK PATTERNS … [<token>]`, `LINK STAGEWATCH … [<key>]`, `VerbCatalog.cs:249-250`).
  The context does not say. Rows 72-73 of the golden context carry them, at review.

**Risk tables**
- **PlayMap** lists 199 commands (`Mapper/tests/PlayMap.Engine.Tests/Fleet/Golden/context-busy.json`):
  - 38,142 bytes compact, 32,843 of them commands;
  - longest verb 307 characters, longest `words` 403;
  - verb heads alone are 2,840 bytes.
- **Patterns** lists 14 (`Patterns/src/Patterns.Core/Services/ShowContext.cs:157-173`): no `APPLY`, no `VIDEO STOP`, no staged or WEB verbs. Its
  proposals are `APPLY <localId>: <Kind> "<title>"` at `review` (`ShowBusNodes.cs:229-230`).
- **AGMessage** lists `notice <text>` at review (`AGMessage/app/lib/context.js:96-97`). Its stage line is invalid Stagewatch grammar today (G05).
  The agmessage area owns the fix (`design/agmessage.md` decision 1, AG-1).

**PlayMap's assistant and the desk (G13)**
- `AssistantService.UseApprovalDeskAsync` is the only setter of the queue's sink. Production calls it only with null
  (`Mapper/src/PlayMap.Engine/Assistant/AssistantService.cs:141-157,273`).
- The sink posts the shown copy. Its collector runs the node's own record for its own proposals and the desk's copy for others
  (`StagewatchProposalSink.cs:106,165-166`).
- Neither the link's `ApprovalsRun` (`StagewatchLink.cs:144`) nor the sink's `Ran` (`StagewatchProposalSink.cs:85`) reaches STATE.
- Ownership: `NetworkHost.cs`, `StagewatchLink.cs` and `OpsParts.cs` belong to no round-3 package. `EngineCore.Assistant.cs` is workflows' in W4
  (`PLAN.md:2185`).

---

## 1. Decisions

1. **Rate a line by whole forms of the target's verified table.** A pure `VerbSyntax`/`VerbTable` in Core reads §6's notation:
   - Literal words are case-blind. `A|B` is one word, either of those. `[…]` is optional and nests. A bare `…` is any further words.
   - ` | ` at bracket depth 0 separates whole forms. A form that does not start with the verb's first word carries on from the first form's
     leading literals, all but the last. That is PlayMap's `VerbSyntax.Alternatives` re-prefix
     (`Mapper/src/PlayMap.Core/Wire/VerbSyntax.cs:21-62`), and the replay holds it for every golden row.
   - A line's words are read as Patterns' and PlayMap's `WireGrammar` reads them: a quoted phrase with `\"` escapes is one word, and a word that
     opens with `{` or `[` runs to the line's end (`Mapper/src/PlayMap.Core/Wire/WireLine.cs:199-260`).
   - **A `<…>` value is exactly one word**, with two exceptions that take one or more words:
     - the form's last element;
     - a value named `words`, `text`, `line`, or ending in ` words` or ` line`.
   - **Specificity:** most literal words wins, then most single-literal words. An exact tie gives the higher risk. A line no form matches, or one
     over 64 words, is high.
   - **Carried lines:** a value named `line` or `… line` holds a line of the same node.
     - The risk is at least that line's when it names a listed form. Words that name no verb add nothing: PlayMap's rule, `VerbCatalog.cs:850-853`.
     - Depth is at most 4; deeper is high.
     - A JSON value in a row that carries lines is high, because the desk does not read JSON.
   - Measured with prototype 2 (table in Appendix A):
     - on PlayMap's 504 form examples, 502 are rated as their row, 2 higher (`BIND ADD <json>`) and **0 lower**;
     - 35 probe lines, adversarial ones included, all hold.
   - *Alternatives:* the prefix patch (G02's own proposal), which still leaves about 21 forms high; any value taking any number of words
     (`SCREEN 2 TAKE PVW LOOK x` would read as the review form).
2. **Caps, stated and counted.**
   - At most 256 commands. A verb over 512 characters is dropped (never cut) and counted. `words` are kept to 200 of the 512 spec-1-3 allows a
     sender.
   - A context is at most 64 KiB *as sent*, counted in UTF-8 bytes (spec-1-3 §6 sizes).
   - A context over that gets **413** `{"ok":false,"error":"a context is at most 65536 bytes (this one is 70211) — Show Bus §13"}`. The Show
     network row says "posted a context too big to read — its verbs read high", named by the key's bound node or else by the address.
   - The compiled table is cached on the entry.
   - *Why:* a cut verb is a different syntax, and the reader cannot trim what it never reads (`WebServer.cs:1613`). Measured: PlayMap's busy
     context is about 47.5 KB at 256 rows.
3. **A row whose risk depends on a word lists that form again, with the word written in and its own risk.** This is the §6 rule.
   - PlayMap's `CommandsFromCatalog` writes one extra row per `RiskWhen` and `HighFor` (unit PM-0, in the integration pass's file). The rows are
     in Appendix A.
   - Mapper generates `tests/fixtures/bus/risk-examples.json` from `RiskOf`: each row's form examples, every `RiskWhen`/`HighFor` word, and carried
     lines.
   - Stagewatch's replay asserts on that fixture that **the desk never rates a PlayMap line lower than PlayMap does**.
   - Until PM-0 lands, four kinds of PlayMap line rate review where PlayMap says high (`ASSISTANT SET enabled|provider`, `DECODE POLICY … FORCE`,
     `MACHINE SET GPU-PRIORITY REALTIME`). This is named as a risk.
   - *Why:* no new notation; a reader that ignores the extra rows still reads them as rows; ties go higher, so raising always works.
4. **No secret rides in any line on the desk.**
   - Refused at every door (POST, `peer`, copilot, MCP), with 400 and words: a line that fills a secret placeholder with anything but the
     marker `‹secret›`.
     - A secret placeholder is one whose whole name is `token`, `key`, `password`, `passcode` or `secret`, case-blind. `pin` is not one: it is
       PlayMap's calibration pin.
     - Or a row's optional field `"secret": n`, PlayMap's `SecretFrom`, emitted by PM-0. It marks words from n on as secret.
   - Also refused anywhere in a line:
     - a word shaped `swk_…` or `agn_…`;
     - a current window token, or `/w/` followed by a 26-character token;
     - any control character.
   - A proposer that keeps the secret itself sends `‹secret›` and runs its own record. PlayMap's sink already does (`StagewatchProposalSink.cs:166`).
   - Peer lines never go to Companion's recorder.
   - *Why:* proposals are readable with a read key, and ride the operator frame, MCP and the journal.
5. **Proposal ids are unique across desk runs:** `p<n>-<run>`.
   - `<run>` is 4 base-32 characters drawn once per desk start (`RandomNumberGenerator`).
   - `p17` alone resolves within the current run.
   - `GET /api/proposals/<id of another run>` → 404 "no such proposal — this desk restarted since (run k3m9)".
   - *Why:* all three targets remember ids across desk restarts and skip a reused one silently. §11 ids were always opaque.
6. **Only verified tables count.** This depends on peers-not-screens SW-1/SW-2: `NodeBindings` checked before storing; decision 8, unverified
   contexts never set risk.
   - Only a table that is verified may decide risk, a `peer` send, quick verbs or the copilot's verbs.
     - Verified means posted under a key bound to that id, or read by Stagewatch's own authenticated link.
     - Patterns' link-read context wins over a posted one.
   - An unverified context's commands rate everything high and never reach the model.
   - Peers' `words` go to the model inside `other_apps`, which the prompt names as that app's data.
7. **A target may raise the risk of its own proposal, never lower it.** With `from.id == target.id` and a verified binding, the stored risk is
   max(table, stated).
8. **Refused, never cut.**
   - A `line` is at most 2048 characters.
   - POST refuses `from.t` `main` or `mcp`, and `from.id` equal to the desk's id ("those are this desk's own").
   - A `peer` or `desk` line aimed at the desk is refused.
   - The copilot's 400-character cut becomes a refusal ("a step is at most 2048 characters").
9. **The desk observes collection.**
   - `ProposalDesk` records each target's last collect. Stagewatch's own sends, and approvals of pending items for that target, are refused when
     it has not asked in **15 s**: "PlayMap Hall has not asked this desk for approved lines in 40 s — it would not collect this".
   - The window greys Approve with that reason. The item stays pending.
   - The JSON shows `collected`.
   - Rig options set the window and the fakes' poll, so tests are deterministic.
10. **One pure desk service.** `Timer.Show/DeskService.cs` owns the queue, the peer contexts, the tables, app resolution, the send and decide
    rules, the freshness check and the AI's view of apps.
    - `MainViewModel.Desk.cs` mirrors it, and the rig hosts it.
    - `CommandRisk.Of(ShowCommand) → LineRisk` lives in Core (a new enum). `Copilot.Classify` maps it to `CopilotRisk`.
    - `RunWithReceipt` saves and restores the outer receipt notes and `_executingRemote` (a stack). `Decide` returns the inner run's outcome.
11. **`peer <app> <line>` — a person sends a listed line to another app, born approved.**
    - **One argument.** Exactly one token follows `<app>`, or it is refused: "quote the line as one argument".
      - HTTP has a GET form with one URL-encoded segment, and `POST /api/peer` with `{"app","line"}` for long lines.
      - WS takes `args` of 2. Companion and OSC take two strings.
    - **Listed only.** The line must match a form of the app's verified table, carry no secret, and be at most 2048 characters with no control
      characters. Otherwise it is refused: "PlayMap Hall does not list 'OUTPUTS OFF' — see its verbs".
    - **`<app>`** is a node id, a unique name, or `playmap`, `patterns` or `agmessage`/`messenger` when exactly one live app of that kind is heard.
    - **Born approved.** `decidedBy` is a role word: `operator`, `operator (copilot)`, `wire`, `deck` or `trigger`. The key's name or the
      sender's address goes only to the desk's journal.
    - **The sender gate** is playout-links' `PlayoutGate` with `Remote.PlayoutFrom`, extended to `Peer` and `Desk`; whichever area lands first
      adds it.
      - HTTP and WS: a PIN that is set and given, or a control key.
      - With no PIN: only this PC or a listed address.
      - OSC and Companion: this PC or a listed address.
      - Never a node, read or MCP key. Never as a proposal.
      - A Command trigger may send `peer` (origin: this PC).
    - The receipt waits for the target's result inside the 3 s budget, through playout-links' `ReceiptNotes.Peer` ticket. WS and Companion get
      playout-links' `final` follow-up.
12. **`desk approve|reject` — a person decides from anywhere they control the show.**
    - Forms: `desk approve <id> [high]`, `desk approve next`, `desk reject <id>|next [<why…>]`.
    - `next` approves only when the oldest pending item is not high. A high item needs its id and the word `high`.
    - Refused when the target is not collecting (decision 9).
    - The same sender gate as `peer`.
    - Refused from MCP, triggers, the advisor, a copilot step and a proposal: "a person approves".
13. **Approving honours the tick.** An unticked shared step is not approved ("Nothing was ticked — nothing ran").
14. **The copilot plans for other apps (release B).**
    - A step gains `target`.
    - `other_apps[]` gives id, collects, verified and `verb_heads` (about 2.8 KB for PlayMap).
      - The full `verbs` are included only when the request names the app (its name or kind word), or when the plan asks `need_verbs:[id]`.
        That second pass happens at most once, within 32 KB per app and 64 KB in total, with `more_verbs` as a count.
    - A target step is kept only when it is listed and carries no secret. Its risk is the table's.
    - "Run ticked steps" runs in order. An app step goes through `peer`'s path and waits up to 10 s for its result. A refusal or no result stops
      the run, and the rest say "not run — the step before it did not report done".
    - Each run of Stagewatch steps is its own undo batch.
    - App steps that cannot be proposed get their own words ("not a verb PlayMap Hall lists").
    - `ShowCommandParser.Help`, which the AIs read, leaves `peer`/`desk` out. `PersonHelp` lists them.
15. **MCP proposes to apps; a person always decides (release B).**
    - `run_command.target` makes a pending shared proposal from `{"t":"mcp","id":"mcp","name":"AI agent (MCP)"}`. This holds even when agents act
      directly.
    - `get_proposal` takes a string id (`p17` or `p17-k3m9`).
    - `get_context.peer` returns a verified context.
    - `peer`/`desk` lines are isError.
16. **The desk's door** (WPF, the maintainer's PC). The Show network row's right-click and its "…" button offer the same items:
    - "Send a line to <name>…";
    - up to 6 quick verbs: low or review forms whose only value is trailing free text;
    - "Its verbs…";
    - "Copy its headline".

    The send dialog calls `DeskService.Send` with the raw string. It shows the matched form, its risk and words, and the collect state. Items are
    greyed with reasons (not collecting, unverified table).
17. **Every reader.**
    - Operator frame `desk{pending,next,apps}`; `next.line` is at most 120 characters and is fenced at entry.
    - `/api/config` and Companion CONFIG `peers`.
    - Companion `ACCESS` per connection (release B).
    - The producer page's desk panel: approve and reject by id; a high item asks to confirm and sends `high`.
    - Context `peers[].collects`.
    - The AI facts and the papers.
18. **Two releases, each one complete in its papers.**
    - **A:** decisions 1–13, 16, and 17 without `ACCESS`.
    - **B:** Companion and OSC doors with the module, decisions 14 and 15.
    - Release A's papers say what B adds and why it waits: the fences and the approve-by-id flow are proven on HTTP/WS first.
19. **PlayMap.**
    - **PM-0, in the integration pass** (`Core/Bus/ShowContext.cs`, §5.6 item 7): derived risk rows, the `secret` field, the `risk-examples.json`
      fixture.
    - **PM-1, after W4, the integration pass and the owner's answer to round 2 decision 7:**
      - `NetworkHost` publishes the desk. The sink collects (`CollectApproved = true`) and the link's pull pauses.
      - The reason, corrected: with the link collecting, PlayMap's own secret-bearing proposals would be **refused as shown copies**
        (`EngineCore.cs:330`).
      - A proposal forwarded before a switch-off or restart fails with words: "this node no longer holds that proposal — ask again".
      - STATE gets one `ran` counter with its `collector`.
      - `ASSISTANT SET desk auto|off` comes with the NetworkDrawer toggle and its right-click, and the Companion `verbs.json` is regenerated.
20. **Patterns Round 90.3** (the round patterns-playmap-side places it in):
    - It appends 16 rows (§2.8). `APPLY` is review, and its words say only this desk's own.
    - `WEB KEY <key name>` is named so it reads as no secret.
    - WEB OPEN and WEB TYPE are not listed. An address can be a window's pass; typed text can be a password.
    - `StagewatchLinkService` runs `APPLY` only when `p.FromId == SelfId` and `Approvals.ByDesk(p.Id)` is the record whose local id the line names.
21. **AGMessage:** no unit here. agmessage AG-1 owns `message "<text>" <urgency> off ack` (`design/agmessage.md:120-121`). This area's SW-2 test
    proves the line parses. Role-word `decidedBy` reaches its meetings unchanged.

---

## 2. Wire shapes

### 2.1 Stagewatch grammar (`ShowCommandKind.Peer`, `ShowCommandKind.Desk`, appended after the last kind)

```
peer <app> <line>                           send one listed line to another show app, approved by you
desk approve <id> [high] | desk approve next
desk reject <id>|next [<why…>]
```

Transports (release A unless marked):

| Door | Shape |
|---|---|
| HTTP | `GET|POST /api/peer/<app>/<one URL-encoded line>`. `POST /api/peer` with body `{"app":"pm-a1b2","line":"SET out1 name \"Left wall\""}` (≤ 16 KB). `/api/desk/approve/p17`. `/api/desk/approve/p18-k3m9/high`. `/api/desk/reject/next/<why>`. |
| WS | `{"cmd":"peer","args":["pm-a1b2","PLAYLIST CUE \"Main show\" #3"],"id":"a1"}`. A message is at most 4096 bytes; longer lines go by HTTP POST. |
| Companion TCP (B) | `@c1 PEER "PlayMap Hall" "SET out1 name \"Left wall\""`, `@c2 DESK APPROVE p18-k3m9 HIGH` |
| OSC (B) | `/stagewatch/peer ,ss pm-a1b2 "…"`, `/stagewatch/desk/approve ,s p17` |
| Trigger | `peer agmessage "notice Q&A in 2 minutes"` (a line with inner quotes is refused there, never altered) |
| MCP | never: `run_command` with a `peer`/`desk` line is isError |

Auth (the shared sender gate): see decision 11. A no-PIN refusal is 403
`{"ok":false,"error":"peer and desk act on other apps with a person's authority — set a PIN, use a control key, or add this address under Remote → May drive other apps"}`.

Refusals:

| Kind | Words |
|---|---|
| parse | "peer takes an app and one line — quote the line as one argument" |
| parse | "desk takes approve or reject" |
| parse | "desk approve takes a proposal id (p17) or next" |
| run | "no app called 'pm' on the show network — heard: PlayMap Hall (playmap), Patterns FOH (patterns), AGMessage (messenger)" |
| run | "two apps answer 'playmap' — name one: PlayMap Hall, PlayMap Foyer" |
| run | "PlayMap Hall does not list 'OUTPUTS OFF' — see its verbs" |
| run | "PlayMap Hall's verbs are not verified (posted without its key) — Stagewatch sends only lines it can read" |
| run | "PlayMap Hall has not asked this desk for approved lines in 40 s — it would not collect this" |
| run | "a secret rides in no line — type it on PlayMap Hall itself" |
| run | "a key rides in no line" |
| run | "a window's address rides in no line" |
| run | "a line is at most 2048 characters" |
| run | "a line holds no control characters" |
| run | "p18 is high — approve it by its id: desk approve p18-k3m9 high" |
| run | "a person approves — not a trigger, the copilot or an agent" |

Risk: `CommandRisk.Of` rates both kinds `High`. Neither is in `ShowContext.Commands`, nor in `ShowCommandParser.Help` (AI-facing).

Receipts (Show Bus §3), HTTP with `X-Request-Id: a1`:
```
200 {"type":"receipt","id":"a1","ok":true,"stage":"applied","effect":"PlayMap Hall ran PLAYLIST CUE show #3: cued Intro (p17)","error":null,"refs":{"sharedProposal":"p17-k3m9"}}
200 {"type":"receipt","id":"a1","ok":true,"stage":"accepted","effect":"sent to PlayMap Hall as p17 — it collects every 2 s and reports back","error":null,"refs":{"sharedProposal":"p17-k3m9"}}
409 {"type":"receipt","id":"a1","ok":false,"stage":"applied","effect":"","error":"PlayMap Hall refused PLAYLIST CUE show #9: Show has no item #9 (p17)","refs":{"sharedProposal":"p17-k3m9"}}
200 {"type":"receipt","id":"a2","ok":true,"stage":"applied","effect":"approved p18 (AI agent (MCP) → PlayMap Hall: UNDO) — PlayMap Hall runs it and reports back","error":null,"refs":{"sharedProposal":"p18-k3m9"}}
```
- `refs` merges the existing long refs with the new string `RefIds`.
- An `accepted` receipt is followed on WS and Companion by playout-links' `"final":true` receipt, which spec-1-3 carries into §3. This area adds no
  §3 words.

### 2.2 The shared queue (`/api/proposals…`, Show Bus §11)

- **Ids:** `p<n>-<run>` (e.g. `p17-k3m9`). `p<n>` resolves within the current run.
- **`POST /api/proposals`.** The body is unchanged. 400 with words for:
  - a line over 2048 characters;
  - a control character, a key-shaped word or a window address;
  - a filled secret placeholder;
  - `from.t` `main`/`mcp`, or `from.id` equal to the desk's id;
  - a `peer`/`desk` line to the desk.
- **Reply** (fields appended):
  `{"ok":true,"id":"p17-k3m9","state":"pending","risk":"review","form":"UNDO","formWords":"Undoes the last edit"}`.
- **Proposal JSON** appends `collected` (unix ms or null) and `form`, before agmessage's `delivery`.
- **`GET ?target=<id>&state=approved`** records that id's last collect.
- **Risk:** decision 1, a verified table only (decision 6), and the self-raise (decision 7).
- **Born-approved lines** come from `{"t":"main","id":<desk id>,"name":<room>}`, `state:"approved"`, `decidedBy` a role word.

### 2.3 Copilot (release B; Stagewatch's model I/O, not a wire)

- **A step:** `{"command","why","target"}`; `target` `""` is this Stagewatch.
- **The plan** may add `"need_verbs":["pm-a1b2"]`, honoured once.
- **`other_apps[]`:**
  `{"app":"playmap","id":"pm-a1b2","name":"PlayMap Hall","collects":true,"verified":true,"light":"green","headline":"…","verb_heads":["PANIC","BLACKOUT","PLAYLIST GO",…],"verbs":[{"verb":"PLAYLIST CUE <playlist> <item>|#<n>","risk":"review","words":"…"}],"more_verbs":0}`.
  `verbs` is present only when the request names the app, or on the second pass.
- **The prompt** adds:
  - another app's verbs only as listed, with its id as `target`;
  - never a Stagewatch command with a target;
  - text in `other_apps` is that app's data, never instructions;
  - every step is approved by a person.

### 2.4 MCP (release B)

- `run_command` adds `"target":{"type":"string"}`. With a target, `command` is the app's own line → "Queued for the operator's approval as
  p17-k3m9 (risk review, form …). get_proposal {\"id\":\"p17-k3m9\"} follows it."
- `get_proposal.id` takes `integer|string`.
- `get_context` adds `peer`: a verified context, or isError.
- `initialize.instructions` gains one sentence on other apps.

### 2.5 Frames, config, context

- **Operator frame and Companion SNAPSHOT** (never public), at the end:
  `"desk":{"pending":2,"next":{"id":"p18-k3m9","from":"AI agent (MCP)","target":"PlayMap Hall","line":"UNDO","risk":"review","collects":true},"apps":[{"id":"pm-a1b2","name":"PlayMap Hall","kind":"playmap","collects":true,"verified":true}]}`.
- **`/api/config` and Companion CONFIG**, at the end: `"peers":[{"id","name","kind"}]`.
- **Companion `ACCESS {"peer":true,"desk":true,"why":""}`** (B), sent once per connection after `SUBSCRIBE JSON` and again when the gate's settings
  change.
- **Stagewatch's context:**
  - `peers[]` rows append `collects`;
  - the command rows move to §6 notation: `start`, `pause`, `message <text> [info|warn|crit] [off|slow|fast] [ack]`, `patterns end [<seconds>]`.
- **Journal rows:**
  - "Sent to PlayMap Hall (wire, key 'FOH tablet', 10.0.0.7): PLAYLIST CUE show #3 — p17";
  - "PlayMap Hall: … — done (cued Intro)";
  - "Approved p18 (deck)";
  - "Refused to send to PlayMap Hall: not collecting for 40 s".

### 2.6 Show Bus words handed to spec-1-3's single master edit (1.3; copies byte-identical under its body hash)

- **§6, the notation:**
  - literals and `A|B`;
  - `<…>` one word, except a form's last element or a `words`/`text`/`line` value;
  - `[…]`, `…`, ` | ` with the carried literals;
  - a quoted phrase and a JSON run as one word.
- **§6, the rules:**
  - a word-dependent risk is its own row;
  - secret placeholders `token|key|password|passcode|secret`, and the optional `"secret": n`;
  - caps of 256 commands, verbs ≤ 512 (dropped, not cut), `words` ≥ 200 kept.
- **§11:**
  - risk is the most specific full form of a verified table, an exact tie higher, carried lines raising it, unknown high;
  - a bound self-proposal raises only;
  - lines ≤ 2048, refused not cut, with no key, window address, filled secret or control character; a proposer keeping a secret sends `‹secret›`;
  - ids are opaque and unique across a desk's runs;
  - `from.t` `main`/`mcp` are the desk's own;
  - the desk records the last collect and does not send or approve for a target silent 15 s;
  - an operator-sent line is born approved, with a role word in `decidedBy`.
- **§8:** the desk flows.

### 2.7 AGMessage (owned by agmessage AG-1)

The line is `message "<text, " → '>" <info|warn|crit> off ack`. Stagewatch's SW-2 holds it in `fixtures/peer-lines.json`.

### 2.8 Patterns' context commands (Round 90.3; appended after the 14, in this order)

| verb | risk | words |
|---|---|---|
| `APPLY <proposal>` | review | one of this desk's own assistant proposals, by its id — only the one this desk proposed (any other is refused); it lands in the preview, never on air |
| `VIDEO STOP` | high | the clip on air back to its top and held |
| `EYE NEXT\|PREV\|RESET` | low | move the operator's Eye (the desk's view only) |
| `EYE LENS <name>` | low | change the Eye's lens (the desk's view only) |
| `CUE STANDBY NEXT\|PREV\|<cue>` | review | put a cue on standby — nothing changes on air |
| `PVW LOOK\|PRESET\|PATTERN\|LIBRARY <name>` | review | stage a picture in the preview — nothing reaches the air until a TAKE |
| `PVW PROGRAM\|RESET` | review | the preview from the air, or the look's own picture back |
| `SCREEN <n> PVW LOOK\|PRESET\|PATTERN\|LIBRARY <name>` | review | stage a picture on one screen's preview |
| `SCREEN <n> PVW PROGRAM\|RESET` | review | one screen's preview from its air, or back |
| `LIBRARY <name>` | review | a Library tile onto the editing target's preview |
| `SCREEN <n> TAKE\|CUT` | high | one screen's preview to its air |
| `WEB NEXT\|PREV\|FIRST\|LAST\|PRESENT\|EXIT\|PLAY\|PAUSE\|MUTE\|RESTART\|FORWARD\|REWIND\|BLACK\|WHITE\|CAPTIONS\|FULLSCREEN [ON <page>]` | high | a page action on the web page on air |
| `WEB KEY <key name> [ON <page>]` | high | a key to the web page on air |
| `WEB CLICK <x> <y> [ON <page>]` | high | a click at a point of the page on air |
| `WEB RELOAD [<page>]` | high | reload the page on air |

Not listed:
- `WEB OPEN`: an address can be a window's pass (G09/G10).
- `WEB TYPE`: typed text can be a password.
- The admin verbs (`RESTART`, `UPDATE APPLY`).

### 2.9 PlayMap

**PM-0** (integration pass). Rows appended after the catalogue's own, so the panic/air/transport order is kept (`ContextOrderTests`) and the total
stays ≤ 256 (§5.6 item 7):
```
{"verb":"ASSISTANT SET enabled|provider <value>","risk":"high","words":"…"}
{"verb":"DECODE POLICY AUTO|DIRECT|GPU-COPY|COPY-BACK|SOFTWARE FORCE","risk":"high",…}
{"verb":"MACHINE SET GPU-PRIORITY REALTIME","risk":"high",…}
{"verb":"SEND ENABLE <send> ON","risk":"review",…}
{"verb":"MEDIA OPTIMISE CANCEL [<job>|ALL]","risk":"low",…}   (and PAUSE, RESUME)
{"verb":"BIND LEARN OFF","risk":"low",…}  {"verb":"TIMECODE SOURCE RESET <source>","risk":"low",…}
```
The `LINK PATTERNS` and `LINK STAGEWATCH` rows gain `"secret":3` at their end.

**PM-1:**
- `ASSISTANT SET desk auto|off`, from this machine or the paired desk only.
- STATE `assistant` appends `"desk":{"name":"Stagewatch at 10.0.0.6:9924","on":true,"problem":"","forwarded":2,"ran":5,"collector":"assistant"}`.
- Nothing new on the wire to Stagewatch: the §11 POST with `from = target = {t:"playmap",…}`, and the line's own risk, which can only raise it.

---

## 3. Changes, file by file

### Stagewatch — release A

**Core**
- **`Timer.Core/VerbSyntax.cs`** (new, pure):
  - `VerbSyntax.Words(line)`, `WireGrammar`-compatible;
  - `VerbForm`;
  - `VerbTable.Compile(rows)`: caps 256/512, dropped counts, carried-line and secret slots;
  - `VerbTable.Rate(line) → RiskVerdict(Risk, Form, Words, Listed, Secret)`, a memoised match over (node, word) with depth ≤ 4.
- **`Timer.Core/ShowBus.cs`:**
  - `Entry.Commands` is `(Verb, Risk, Words, Secret?)`, plus `Entry.Verbs` (a cached `VerbTable`), `CommandsDropped` and `Verified`;
  - `Parse` keeps 256, drops verbs over 512 and keeps `words` ≤ 200;
  - `Accept` counts UTF-8 bytes;
  - `PeerSummary` gains `Collects` (appended, defaulted).
- **`Timer.Core/Approvals.cs`:**
  - `MaxLine` 2048, refused;
  - ids `p<n>-<run>`, with `Resolve(idOrShort)`;
  - `Submit(json, credential, Func<NodeRef,string,RiskVerdict>, fence, now)`, keeping the 4-argument overload for the existing callers;
  - `from.t` main/mcp refused; self-raise;
  - `ProposeApproved(from, target, line, reason, verdict, by, now)` raises one Approved `Changed`;
  - `LastCollect(id)`, recorded in `Collect`; `NextPending()`;
  - JSON appends `collected` and `form`;
  - `RiskFromCommands` wraps `VerbTable`.
- **`Timer.Core/CommandRisk.cs`** (new): `enum LineRisk {Low, Review, High}`; `CommandRisk.Of(ShowCommand)` is `Classify`'s switch, plus
  `Peer` and `Desk` → High.
- **`Timer.Core/LineFence.cs`** (new, pure): control characters, `swk_`/`agn_`, window tokens and shape, and filled secrets → words.
- **`Timer.Core/ShowState.cs`:**
  - `Peer` and `Desk` kinds appended; `PeerArgs(App, Line)` and `DeskArgs(Action, Id, High, Why)`;
  - parser verbs (exactly one line token);
  - `Help` (AI-facing, unchanged) and `PersonHelp` (+ `peer`/`desk`);
  - `CommandReceipt` gains `RefIds` (appended), merged into `refs`;
  - `IShowContext.PeerContextJson(app)` (defaults);
  - `RemoteConfig.Peers`.
- **`Timer.Core/SnapshotJson.cs` and `ShowSnapshot`:** `DeskSummary` at the end of the operator frame only; config `peers`.
- **The sender gate** (`PlayoutGate`, playout-links unit 2) lists `Peer` and `Desk`; its label reads "May drive other show apps from".

**Show and Integrations**
- **`Timer.Show/DeskService.cs`** (new):
  - delegates for the desk's id and name, the hello table, the verified contexts (posted or link-read), window tokens and the clock;
  - `Risk`, `Resolve`, `Send`, `Decide(idOrNext, approve, by, high, why, ticked)`, `Apps`, `Summary`, `QuickVerbs`, `ResultOf`;
  - the freshness rule (configurable).
- **`Timer.Show/ShowContext.cs`:** command rows in §6 notation; `peers[].collects`.
- **`Timer.Integrations/Copilot.cs`:** `Classify` maps `CommandRisk.Of`.

**Remote**
- **`Timer.Remote/WebServer.cs`:**
  - `POST /api/peer` (JSON body);
  - the generic path passes the gate's facts: credentialed, PIN set, loopback/listed;
  - WS `WsClient.ByKey`/`Credentialed`;
  - 413 on a large context, with the bound node recorded;
  - `/api/config` `peers`;
  - `GET /api/proposals/<id>` resolves short and old-run ids.

**App** (PC work in SW-6; the logic stays in DeskService)
- **`MainViewModel.Desk.cs`:**
  - mirror of the service;
  - executors for `Peer`/`Desk` (journal with who and where, `ReceiptRefId`, the awaited result);
  - `ApproveShared` honours the tick and the freshness rule.
- **`MainViewModel.Receipts.cs`:** the notes stack; `ReceiptRefId`.
- **`MainViewModel.ShowBus.cs`:** `PeerRow.Collects/Verified/LastSent/QuickVerbs`; `playmap => "PlayMap"` (shared with playout-links).
- **`MainViewModel.cs`:** the send dialog bypasses `RunOperatorLine`, so nothing is recorded.
- **New and changed views:**
  - `MainWindow.xaml(.cs)`: the row's ContextMenu and "…";
  - `SendLineWindow.xaml(.cs)` (new);
  - `CopilotWindow.xaml`: Approve is greyed with its reason;
  - `Help/HelpContent.cs`.

**Pages, rig, tests and papers**
- **`Timer.Remote/wwwroot/producer.{html,js}`:** the desk panel (pending list; Approve/Reject by id; a high item confirms and sends `high`).
- **`tools/Stagewatch.Rig/Program.cs`:** hosts `DeskService`; options `--desk-fresh <s>` and `--node-keys` (peers-not-screens SW-6); `Run`
  handles `Peer`/`Desk`; the frame carries `desk`.
- **Tests:**
  - `tests/Stagewatch.Tests/DeskTests.cs` (new), `ShowBusTests.cs` (DeskRules extended);
  - fixtures `playmap-context-busy.json`, `playmap-risk-examples.json` (after PM-0), `patterns-context.json`, `agmessage-context.json`,
    `peer-lines.json`, each with `_source`;
  - `tests/e2e/fakepeers.cjs` (shared with playout-links; node-key collectors with configurable poll and id memory) and
    `tests/e2e/desk.e2e.cjs`; `pages.e2e.cjs` gets the desk panel case.
- **Papers:** `docs/REMOTE.md`, `docs/FEATURES.md`, `docs/WHATS_NEXT.md` (release A and what B adds), `docs/ECOSYSTEM.md`, `docs/NODES.md`,
  `README.md`, `CLAUDE.md` (`CommandRisk.Of`), and the words for spec-1-3's S1.

### Stagewatch — release B
- **Companion:**
  - `Timer.Companion/CompanionServer.cs`, `CompanionProtocol.cs`: `PEER`/`DESK` through the gate; `ACCESS` per connection; help lines;
  - `companion-module/src/{actions,variables,feedbacks,presets,state}.ts`: `peer_line` (app dropdown from `peers`), `desk_approve` (approves
    `desk_next_id`, a second press within 3 s for high, which sends `HIGH`), `desk_reject`; variables `desk_pending` and
    `desk_next_{id,from,target,line,risk}`; feedback `desk_waiting`; `ACCESS` greys the actions;
  - `test/module.test.mjs`.
- **`Timer.Remote/OscServer.cs`:** `peer`/`desk` through the gate.
- **Copilot:**
  - `Timer.Integrations/Copilot.cs`: `CopilotStep.Target`; schema `target` and `need_verbs`; `ParsePlan(json, apps)` keeps a listed,
    secret-free step at its table's risk; refuse over 2048; the prompt;
  - `Timer.Show/AiContext.cs`: `PeerFact` gains `Id`, `Collects`, `Verified`, `VerbHeads`, `Verbs?` and `MoreVerbs`;
  - `Timer.Show/PlanRunner.cs` (new, pure): runs steps in order with two delegates, waits ≤ 10 s on an app step and stops on a refusal;
  - `MainViewModel.Copilot.cs`: `CopilotStepItem.Target/TargetId/SharedId`; `ApproveProposal` through `PlanRunner`; Dropped words per app;
    `CopilotContext` with the two-pass verbs.
- **`Timer.Remote/McpEndpoint.cs`:** `target`; `get_proposal` strings (an `IShowApprovals` overload); `get_context.peer`; instructions.
- **Papers:** `docs/COMPANION.md`, `docs/REMOTE.md`, `docs/FEATURES.md`, `docs/WHATS_NEXT.md`.

### Patterns — Round 90.3 (rows and the APPLY fence), 90.4 (papers, with the round)
- `src/Patterns.Core/Services/ShowContext.cs`: the 16 rows of §2.8 after the 14, order kept.
- `src/Patterns.App/Services/StagewatchLinkService.cs:482-495`: `APPLY` runs only when `p.FromId == SelfId` and
  `Approvals.ByDesk(p.Id)?.LocalId == local`; otherwise `(false, "an APPLY runs only for the proposal this desk made, under its own id")`.
- `tests/Patterns.Core.Tests/ShowBusTests.cs` and a `Patterns.App.Tests` case for the fence.
- Papers (90.4):
  - PLAN §108 (the requester and the evidence class: a handed-in critique);
  - REVIEW, CHANGELOG, REMOTE;
  - `docs/SHOWBUS.md` 1.3 byte copy;
  - OPEN: add and close "the context omits VIDEO STOP and the staged verbs"; add "WEB OPEN/TYPE not advertised";
  - the readers named: CONTEXT reply, `/api/context`, the posted context; node mode unchanged.

### Mapper
- **PM-0** (integration pass):
  - `src/PlayMap.Core/Bus/ShowContext.cs`: `CommandsFromCatalog` appends a row per `RiskWhen`/`HighFor` and writes `secret` from `SecretFrom`;
  - `tests/PlayMap.Core.Tests/ContextRiskRowsTests.cs` (new): every derived row's example line has `RiskOf` equal to the row's risk;
    `ContextOrderTests` still ≤ 256 and the air first;
  - `tests/fixtures/bus/risk-examples.json`, written with `PLAYMAP_WRITE_FIXTURES=1`.
- **PM-1** (after W4, the integration pass and decision 7):
  - `Host/NetworkHost.cs` (`ApprovalDesk`, `ApprovalDeskChanged`, `PauseLinkApprovals`);
  - `Net/Links/StagewatchLink.cs` (`CollectApprovals` at `:471`, `DeskHandler()` with the pin validator);
  - `Engine/OpsParts.cs` (the assistant extension wires the sink);
  - `Engine/Assistant/AssistantService.cs` and `EngineCore.Assistant.cs` (workflows' in W4: through that package or after it), `VerbCatalog.cs`
    (`ASSISTANT SET` syntax gains `desk`, through the spine lane);
  - STATE `assistant.desk` and its card descriptor; `src/PlayMap.App/Views/NetworkDrawer.axaml` toggle and right-click;
  - `integrations/companion-module-playmap/src/catalog/verbs.json` regenerated;
  - tests: `StagewatchSinkTests.cs`, `Network/ApprovalDeskWiringTests.cs` (new), `DeskVerbRuleTests`.
- **PM-2:** `README.md:208-213`, `docs/UX.md:280`, `docs/round2/CARRYOVER.md:99` (answered with the corrected reason), `docs/SHOWBUS-NODE.md`.

---

## 4. Tests

### Stagewatch suite (`tests/Stagewatch.Tests`, Linux and Windows)

1. **VerbSyntax:**
   - each notation piece;
   - a one-word value vs the last element, and `words`/`line` values;
   - `EYE` vs `EYE NEXT`;
   - specificity and the higher tie;
   - carried ` | ` forms;
   - quoted and JSON words;
   - a carried line that names no verb adds nothing; depth 5 is high;
   - JSON in a carrying row is high;
   - 65 words is high;
   - a 513-character verb is dropped and counted; 300 rows keep 256 and count 44.
2. **Golden replay** on `playmap-context-busy.json`:
   - 502 of 504 examples rated as their row, 2 higher, 0 lower;
   - the 35 probes of Appendix A;
   - the legacy matcher pinned at 114 wrong of 144.
   - After PM-0, on `playmap-risk-examples.json`: no line is rated lower than PlayMap's `RiskOf`.
3. **Desk rules** (`DeskRules`, using the old 4-argument `Submit` and the new one):
   - self-raise only when bound, never lowering;
   - 2049 characters refused, not cut;
   - `swk_`/`agn_`, a window token, a control character and `LINK STAGEWATCH h swk_x` refused;
   - `LINK STAGEWATCH h ‹secret›` accepted;
   - `from.t` main/mcp and `from.id` = desk refused;
   - `peer`/`desk` to the desk refused;
   - ids `p1-<run>` with a fresh run after a new desk; `p1` resolves; an old-run id gets 404 with words;
   - `LastCollect`; `ProposeApproved` raises one Approved `Changed`;
   - the JSON ends `…,"collected":…,"form":…`.
4. **Peers' documented lines** (`peer-lines.json`): AGMessage's corrected line parses as `Message` warn/off/ack and the old one fails; Patterns'
   `APPLY` rates review; AGMessage's `notice` rates review.
5. **DeskService on an injected clock:**
   - unknown and ambiguous app words;
   - `stagewatch` is not an app word;
   - unlisted refused; unverified table refused;
   - no collect for 16 s refused, for send and approve;
   - a collect 3 s ago → born approved with `decidedBy:"wire"` and a journal row holding the key name;
   - `ResultOf`;
   - `next` takes the oldest and refuses a high one; `<id> high` approves it;
   - an unticked approve does nothing;
   - triggers, MCP and advisor origins are refused for `desk`.
6. **Grammar:**
   - `peer` with two tokens after the app is refused;
   - `Help` has no `peer`/`desk`, `PersonHelp` has both;
   - `CommandRisk.Of` equals `Classify` for every kind;
   - neither kind is in `ShowContext.Commands`;
   - the advisor's allow-list excludes both.
7. **Receipts:** a `desk approve` of a desk-targeted line keeps the outer receipt's refs and returns the inner refusal; `refs` carries
   `sharedProposal` as a string.
8. **Transports (A):**
   - HTTP and WS with the PIN → receipt;
   - with an **empty PIN** from a non-loopback address → 403 with words (HTTP and WS `role=control`); from loopback → allowed;
   - a node key → 403;
   - round trips keep `SET out1 name "Left wall"`, `ADD media {"path": "a b.mp4"}` and `a/b` exact (GET one segment, POST body, WS args);
   - a WS message over 4096 bytes closes with "too big".
9. **Frames:** the operator frame ends with `desk`, the public frame has none; config ends with `peers`.
10. **Release B:**
    - Companion from loopback is OK; from another address it is refused unless listed; `ACCESS` follows the setting; `\n` inside the line is
      refused;
    - OSC likewise;
    - `ParsePlan` keeps a listed target step, drops unlisted, secret-bearing and over-2048 steps with app words, and asks `need_verbs` once;
    - `PlanRunner` order: a failed app step stops later steps;
    - `AiApps` budgets; MCP `target` pending with direct agents on, `get_proposal "p1"`, `get_context {peer}` verified only, `peer`/`desk` isError.
11. **UiSmoke** (Windows, `Bench.OnWindows`): the row menu with greyed reasons; the dialog's risk chip; an unticked shared item stays pending;
    Approve is greyed when the target is silent.

### Rig e2e (`tests/e2e/desk.e2e.cjs`; rig `--desk-fresh 2`, fakes polling every 200 ms with node keys)

1. Risk via POST: `UNDO` review, `PANIC` high, `SHOW SET REMOTE-VIEW-ONLY ON` review, `BIND ADD note 60 TO BLACKOUT ON` high.
2. `peer playmap "PLAYLIST CUE show #3"` → exactly `applied` with the fake's words; the fake saw it once.
3. `peer agmessage "notice Q&A in 2 minutes"` → applied "shown in 1 meeting"; the fake saw `decidedBy:"wire"`.
4. The fake PlayMap stops polling → after 2 s `peer playmap …` is refused, and nothing is queued.
5. **Restart:** run A sends p1 to the fake (which keeps its run memory); the rig restarts; run B's first send is collected and run (not skipped) by
   all three fake collectors.
6. **Empty PIN:** a non-loopback `peer` is 403; a loopback one is applied.
7. A pending MCP-shaped proposal → the WS operator frame shows `desk.pending` 1 → `desk approve next` → collected and run; a high one → `next` is
   refused and `desk approve <id> high` works.
8. `peer playmap "LINK STAGEWATCH host swk_x"` and `"LINK PATTERNS 10.0.0.5 tok"` are refused; no `swk_`/`tok` appears in the rig log, journal,
   frames or `/api/proposals`.
9. `pages.e2e.cjs`: the producer page shows the pending item and approves it by id.

### Optional interop lane (`tools/test.sh interop`, "not run" when apps are absent)

ecosystem-lane S3.1–S3.3: a real AGMessage and `PlayMap.Node`.

### Patterns
- `Patterns.Core.Tests`:
  - the first 14 rows are unchanged; the 16 appended rows have their risks;
  - each row's example parses through `ControlProtocol.Parse` (`APPLY` via `DeskProposals.LocalId`);
  - `WEB OPEN`, `WEB TYPE`, `RESTART` and `UPDATE` are absent;
  - no row's placeholder has a secret name.
- `Patterns.App.Tests`: an `APPLY` collected with another `FromId`, or under a different desk id, is refused with words and never applied.

### Mapper
- **PM-0:** `ContextRiskRowsTests` (every derived row matches `RiskOf`; `secret` equals `SecretFrom`); `ContextOrderTests` (≤ 256, air first);
  the fixture writer.
- **PM-1:**
  - the wiring tests;
  - one collector;
  - a secret-bearing own proposal runs from the record;
  - after switch-off, an orphaned forwarded id fails with words and runs nothing;
  - no line runs twice across a switch;
  - STATE `assistant.desk` with `ran`/`collector`;
  - `DeskVerbRuleTests` for the toggle.

---

## 5. Units, in order

| Unit | Repo | Title | Gaps | Depends on |
|---|---|---|---|---|
| SW-1 | Stagewatch | The verb table: `VerbSyntax`, `PeerContexts` caps (256/512/words, UTF-8 bytes, 413), verified-only risk, golden replay | G02 | peers-not-screens SW-1 |
| SW-2 | Stagewatch | The desk's rules: ids with a run tag, `LineFence`, refuse-not-cut, `from.t`, self-raise, last collect, `ProposeApproved`, JSON, §6-notation context rows, peers' lines test | G02, G03, G05 | SW-1 |
| SW-3 | Stagewatch | `DeskService`, `LineRisk`/`CommandRisk.Of`, the tick fix, receipt stack, `RefIds`, the mirror | G14 | SW-2 |
| SW-4 | Stagewatch | `peer`/`desk` on HTTP/WS: kinds, parser, `PersonHelp`, gate (with playout-links unit 2), `POST /api/peer`, executors, rig, `desk.e2e.cjs` | G14, G20 | SW-3, peers-not-screens SW-2 |
| SW-5 | Stagewatch | Readers A: operator frame `desk`, config `peers`, context `collects`, producer page panel, pages e2e | G14, G20 | SW-4 |
| SW-6 | Stagewatch | The desk (PC): row menu and "…", send dialog, approval greys, help, UiSmoke | G14, G20 | SW-4 |
| SW-7 | Stagewatch | Papers A and the spec words to spec-1-3 S1 | G02, G14, G20 | SW-4–SW-6 |
| SW-8 | Stagewatch | B: Companion/OSC doors, `ACCESS`, module actions/variables/feedback, tests | G14, G20 | SW-4 |
| SW-9 | Stagewatch | B: copilot targets, two-pass verbs, `PlanRunner`, AI Help split | G14 | SW-4 |
| SW-10 | Stagewatch | B: MCP `target`, string `get_proposal`, `get_context.peer` | G14 | SW-4 |
| SW-11 | Stagewatch | B: papers | G14 | SW-8–SW-10 |
| PT-90.3 | Patterns | Context rows and the APPLY fence | G03, G33 | — (rated right after SW-1) |
| PT-90.4 | Patterns | The round's papers (with patterns-playmap-side's 90.x) | G03, G33 | PT-90.3 |
| PM-0 | Mapper | Integration pass: derived risk rows, `secret`, `risk-examples.json` | G02 | — |
| PM-1 | Mapper | The assistant's desk | G13 | SW-2, PM-0, owner's decision 7, W4 |
| PM-2 | Mapper | Papers | G13 | PM-1 |

AGMessage has no unit here: agmessage AG-1 (0.5.0) carries the line. G05 closes with AG-1 and SW-2's test.

---

## 6. Gaps

**Closed:**
- G02: full forms, carried lines, word-risk rows, caps, verified tables.
- G03: `APPLY` listed and fenced; self-raise.
- G05: with agmessage AG-1.
- G14: release A for the operator and the wire; release B for the AIs.
- G20: `peer agmessage notice …`, the row menu and a trigger.
- G33: the staged, VT-stop, Eye, cue, WEB action and click verbs.

**Ready, not closed:**
- G13: the owner's decision 7, workflows' W4 file, and the integration pass.

**Left:**
- G33's WEB OPEN/TYPE part: G10's lease, and a password-safe typing path.
- PlayMap's assistant proposing for *other* apps: a later design.

---

## 7. Risks

- **Until PM-0 lands,** three kinds of PlayMap line rate lower on the desk than on PlayMap: `ASSISTANT SET enabled|provider`,
  `DECODE POLICY … FORCE`, `MACHINE SET GPU-PRIORITY REALTIME`. PlayMap's own policy still holds (the Approved role while armed,
  `ShowPolicy.cs:266-280`).
- **`peer` turns Stagewatch's credential into a listed-verb relay** onto Patterns and PlayMap. It is bounded by:
  - the listed-only rule and the secret fence;
  - the gate (a PIN or control key, or this PC or a listed address);
  - the targets' own fences.

  REMOTE and SHOWBUS say so.
- **Unlisted proposals** (POST from an AI) can still carry a secret the desk cannot read. §11 tells proposers to send `‹secret›`; the risk stays
  high.
- **Born-approved lines skip pending.** Mitigations: the dialog's live form and risk, the listed-only rule, the journal.
- **The 15 s rule** refuses sends just after a target or the desk restarts. The words say to try again.
- **Merge order with playout-links:**
  - the gate;
  - `ReceiptNotes.Peer`;
  - the `ShowCommandKind` order;
  - the `playmap` label;
  - `fakepeers.cjs`;
  - `final`.
- **Shared field orders:** agmessage's `delivery` and spec-1-3's S1.
- **Copilot cost:** a second pass costs one more model call when the model needs an app's verbs.
- **SW-6** is the maintainer's PC work.

---

## 8. What each repo's process requires

**Stagewatch** (`CLAUDE.md`):
- One `dotnet build` at a time.
- A new kind is appended with its parser verb, help (`PersonHelp`) and risk (`CommandRisk.Of`); `CLAUDE.md`'s line names it.
- Frame and config fields go at the end of their objects.
- Secrets appear in no log, journal or frame; tested.
- `Bench.OnWindows` for UiSmoke.
- The `tools/test.sh` lanes build, unit, module, holo and e2e stay green.
- Papers per release, with "what the release did not do".

**Patterns** (`CLAUDE.md`):
- Round 90 (PLAN §108), as patterns-playmap-side numbers it: 90.3 here, 90.4 papers.
- Open by reading `docs/field` and `OPEN.md`.
- The evidence class is a handed-in critique.
- Commits are `Round 90.k: …` in the maintainer's authorship.
- Rows are appended (substring tests).
- No verb an output can show, so the papers say there is no journal, log or Super Check row.
- "Walk: unwalked — no platform path".
- The SHOWBUS copy is byte-identical under spec-1-3's hash.
- All seven suites and the module's tests must be green.

**Mapper** (`CLAUDE.md`, round 3 §4/§5.6/§5.7):
- PM-0 is the integration pass's file (§5.6 item 7).
- PM-1 waits for W4 (`EngineCore.Assistant.cs` is workflows') and the owner. Its `VerbCatalog` edit goes through the spine lane.
- Fixtures are regenerated with `PLAYMAP_WRITE_FIXTURES=1`, with card descriptors.
- `DeskVerbRuleTests` must hold.
- The analyzers apply to tests.

**AGMessage:** nothing here; agmessage AG-1 owns its change.

---

## Appendix A. Prototype 2 on PlayMap's busy golden context

Run: `python3 -I verbsyntax2.py Mapper/tests/PlayMap.Engine.Tests/Fleet/Golden/context-busy.json`.

| Table | Examples | As their row | Higher | Lower |
|---|---|---|---|---|
| Golden (199 rows) | 504 | 502 | 2 (`BIND ADD <json>`: JSON in a carrying row) | 0 |
| Golden + the 9 derived rows of §2.9 | 504 | 499 | 3 (2 as above, plus `DECODE POLICY AUTO FORCE`, now PlayMap's high) | 2 (`MEDIA OPTIMISE CANCEL [x]`, now PlayMap's low) |
| Today's matcher | 144 low/review rows | — | 114 | — |

| line | new (+ derived rows) | golden only | form |
|---|---|---|---|
| `MACHINE SET GPU-PRIORITY REALTIME` | high | review | the derived row |
| `DECODE POLICY DIRECT FORCE` | high | review | the derived row |
| `ASSISTANT SET enabled on` / `model x` | high / review | review / review | derived / own row |
| `SEND ENABLE s1 ON` / `OFF` | review / high | high / high | derived / own |
| `MEDIA OPTIMISE CANCEL` | low | review | derived |
| `BIND ADD note 60 TO BLACKOUT ON` | high | high | carried line |
| `BIND ADD note 60 TO PLAYLIST CUE show #3` | review | review | carried line |
| `BIND LEARN BLACKOUT ON` / `BIND LEARN OFF` | high / review | high / review | carried / exact tie goes higher |
| `BIND ADD {"input":"note 60","target":"BLACKOUT ON"}` | high | high | JSON in a carrying row |
| `MOVE it1 3 BLACKOUT ON` | low | low | the verb is MOVE (PlayMap's `RiskOf` agrees) |
| `SCREEN 2 TAKE PVW LOOK x` (Patterns' rows) | high | high | no form: a middle value is one word |
| `PLAYLIST CUE "Main show" #3` | review | review | quoted phrase |
| `LINK STAGEWATCH host swk_abc` | refused (secret) | — | `[<key>]` filled |
| `LINK STAGEWATCH host ‹secret›` | review | — | the marker |
| `SET out1 name "Left wall"` / `ADD media {"path": "a b.mp4"}` | review | review | |
| `SHOW SET REMOTE-VIEW-ONLY ON`, `VIDEOIO SET NDI-GROUPS a,b`, `MACHINE SET TIMER HIGH` | review | review | carried forms |
| `PANIC`, `FOO BAR`, `BLACKOUT` (bare) | high | high | |
