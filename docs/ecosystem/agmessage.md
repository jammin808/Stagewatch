# AGMessage on the show bus: a valid stage line, results that arrive once, Stagewatch's facts read, PlayMap heard, an honest link

Area: agmessage. Gaps: G05, G16, G17, G18, G19, G32, G37, G43 (and rows for G44). Design only: nothing in any repo was edited or built.
Revised after review: §9 answers each review item.

Paths are repo-relative. Line numbers are from the trees as read on 2026-10-25. AGMessage is `AGMessage/app/…` (app 0.4.0,
`app/package.json:3`, `server.js:42`).

**What changed in this revision, in one paragraph.** Stagewatch gets no code from this area. The re-offer word (`redeliver=1`) is
gone. AGMessage now recovers a lost collect reply itself, by reading the desk's queue, which every Stagewatch already serves. Each run
AGMessage owes a result for is keyed by the desk's id, the proposal id and its `created` time, because today's proposal ids restart at
`p1` with every Stagewatch run. Before any retry, AGMessage reads the proposal back and posts only onto the one it ran. Any 401 on any
keyed call refuses the link in one place, and nothing keyed leaves AGMessage while the refusal hold lasts. AGMessage takes only a
Stagewatch key (`swk_…`), as Show Bus §7 already says. Which evidence counts is set by the first message on the socket, never by a
race. A refused key keeps the board's public timer. The window actions come from the rows Stagewatch lists, and the readers follow
spec-1-3's limits (128 peers, verbs dropped never cut). The interop scenarios move into the ecosystem lane, which CI runs.

**Ownership.** This design owns every AGMessage code change the sibling designs name, so AGMessage ships one release (0.5.0) after
spec-1-3's 0.4.1:
- cross-app-proposals: "AGMessage has no unit here: agmessage AG-1 (0.5.0) carries the line". Its SW-2 adds the peers' lines fixture
  that carries AGMessage's line. Its proposal JSON appends `collected` and `form`, and no `delivery` follows them any more.
- peers-not-screens AG-1, AG-2 and AG-3 (the verdict, the node link, papers) are this design's AG-5 and AG-7. The differences are listed in
  §8.
- spec-1-3 A1 (0.4.1: the 1.3 copy, `proto` 3, 32 caps, the hello scope, loopback first, the "every second" ping wording) lands
  first. This design does not touch those hunks. AG-4 builds its hello table on A1's hearing rule.
- windows-for-peers AG-W1 (SHOWBUS copy 1.3 and a CHANGELOG line) is a papers-only unit. It folds into spec-1-3 A1 or AG-7, whichever
  is cut first.
- ecosystem-lane rows S1.3b, S3.1, S3.2 and S6.5 (unit SW-R8) and four new rows from this area are SW-AG-2 here (§3, §4).

---

## 0. What exists (evidence)

**The stage proposal line (G05)**
- AGMessage proposes `'message "' + text.replace(/"/g, "'") + '" ' + urgency + ' ack'` (`AGMessage/app/server.js:703`).
- Stagewatch reads the third argument as the flash rate (`Stagewatch/src/Timer.Core/ShowState.cs:750-756`). `ack` in that slot throws
  "flash must be off|slow|fast" (`ShowState.cs:1187-1193`).
- The desk rates a desk-targeted line from its own parse (`Stagewatch/src/Timer.App/ViewModels/MainViewModel.Desk.cs:75-79`). The
  failed parse rates it high, and AGMessage's `risk:'review'` (`server.js:705`) is ignored. On approval the parse fails again and the
  proposal ends failed "not a valid Stagewatch command" (`MainViewModel.Desk.cs:149-155`).
- The direct path is right: `/api/message/<text>/<urgency>/off[/ack]` (`AGMessage/app/lib/nodes.js:374`).
- The wrong line is pinned by `app/test/server.test.js:336` and `app/test/tls.test.js:141` and documented at `AGMessage/docs/NODES.md:14`.
- `McpEndpoint.Tokenize` (`Stagewatch/src/Timer.Remote/McpEndpoint.cs:173-186`): a `"` toggles quoting and nothing escapes it.

**Result reports and proposal identity (G16)**
- AGMessage runs and reports only what the current collect returns (`server.js:1097-1101`). `ranProposals` lives in memory and is
  keyed by the bare id (`server.js:1059`, bounded at 200 at `:1099`). `reportResult` returns a bare boolean (`nodes.js:455-462`).
- `Collect` stamps `ClaimedUtc` and never returns a claimed proposal again (`Stagewatch/src/Timer.Core/Approvals.cs:135-146`). A
  claimed proposal with no result for 5 minutes becomes `undelivered` "<target> didn't report back" (`Approvals.cs:46`, `:185-187`).
- `Result` takes any `Approved` proposal, claimed or not, plus an `Undelivered` one that was claimed (`Approvals.cs:153-157`). Other
  errors come back as **409** (`MainViewModel.Desk.cs:61`), a malformed body as **400** (`:57-60`).
- **Ids are unique only within one desk run.** They come from an instance counter (`Approvals.cs:51-52`, `:101`). The desk is built
  once per app run and nothing is persisted (`MainViewModel.Desk.cs:15-23`, `Approvals.cs:49`). So after a Stagewatch restart, `p9`
  names a different proposal. Stagewatch's node id persists (12 hex, `Stagewatch/src/Timer.Core/SettingsStore.cs:201-202`), and each
  proposal carries `created` in ms (`Approvals.cs:26`). cross-app-proposals decision 5 tags ids with the desk run (`p<n>-<run>`) from
  its SW-2.
- `GET /api/proposals` with no `from` lists the whole queue with read scope (`MainViewModel.Desk.cs:38-39` → `Desk.All()`,
  `Stagewatch/src/Timer.Remote/WebServer.cs:604-605`). The JSON never says whether a proposal was claimed (`Approvals.cs:20-31`).
  cross-app SW-2 appends `collected`.
- Patterns (`Patterns/src/Patterns.App/Services/StagewatchLinkService.cs:459-463`) and PlayMap
  (`Mapper/src/PlayMap.Net/Links/StagewatchLink.cs:538-552`) also post each result once and keep their ran ids in memory.

**The proposal's shape (G17)**
- `SharedProposal.ToJson` sends `result` as a string, with `ok` (null until it ran) and `words` at the top level (`Approvals.cs:20-31`),
  as REMOTE 2.16 documents (`Stagewatch/docs/REMOTE.md:250`). §11 never states the shape (`Stagewatch/docs/SHOWBUS.md:199-201`).
- `readProposal` reads `ok` only from an object `result` (`nodes.js:169`, `:175`). The fakes assert a shape the desk never sends
  (`tls.test.js:83-84`, `:105`; `server.test.js:312`).

**Hellos (G18)**
- AGMessage keeps only `main|agent|patterns|messenger` (`nodes.js:33`, `:38`), fenced by `app/test/resilience.test.js:178`. PlayMap only
  says `t:"playmap"` (`Mapper/src/PlayMap.Net/Bus/HelloService.cs:99-114`).
- Stagewatch's reader accepts a kind and an id of letters, digits, `-` and `_` (≤ 16 and ≤ 40), and keeps at most 128 peers
  (`Stagewatch/src/Timer.Integrations/RemoteSlides.cs:132`, `:186`, `:195`). spec-1-3 decision 4 makes that the 1.3 reader rule
  ("peers kept 128").
- AGMessage's peer table has no bound (`nodes.js:67`, `:88`). Its admin labels have no PlayMap (`app/public/admin-plus.js:72`), and its
  hint says "Stagewatch, Patterns and presenter PCs" (`:71`).

**What AGMessage reads of Stagewatch's context (G19)**
- Only `headline`, `light` and `peers` are used (`app/lib/context.js:112-115`, `server.js:860`, `admin-plus.js:25-27`).
- Each window is an item `window:<name>` with green, amber or grey and words such as "link lost — the screen shows its holding page"
  (`Stagewatch/src/Timer.Core/ScreenWindows.cs:325-348`). The headline names at most one item (`Stagewatch/src/Timer.Show/ShowContext.cs:116-117`).
- Cue items are `"cue:" + target` (`ShowContext.cs:92`), and targets are free-text department names, so an id can hold a space.
- Today's 15 command rows list `window show`, `hold` and `revoke`, but **not `release`** (`ShowContext.cs:41-43`). Stagewatch does run
  `window release` (`ScreenWindows.cs:255-261`). windows-for-peers row 9 lists `window hold|release <window or all>`.
- Today's desk **cuts** a proposal's line at 400 characters without a word (`Approvals.cs:88` with `Str` at `:214-218`). cross-app SW-2
  raises the limit to 2048 and refuses longer lines instead of cutting them.
- spec-1-3 decision 4 sets the reader rules: ≥ 128 items, ≥ 256 commands, verbs ≤ 512 dropped and never cut, and ≥ 200 characters of
  `words` kept. A reader that keeps fewer for a card keeps reds and ambers first and says how many it left out.
- A node key may read (`Stagewatch/src/Timer.Core/ApiKeys.cs:81-82`) but run only `message` and `clearmessage` (`:85`). Any other verb
  goes as a §11 proposal that a person approves.

**The link and its credential (G43, G32)**
- AGMessage dials `/ws?cid=agmessage&page=%2Fagmessage` (`nodes.js:317`). It sets `connected` on the upgrade (`:322-324`) and pins on
  the first `state` frame (`:329`). A refusal is overwritten by `reconnecting` (`:331`, `:343-344`).
- On today's Stagewatch, a key on a non-control socket gets no verdict, only the public frame (`WebServer.cs:1153-1164`, `:1216-1219`).
  A refused key with a viewing code set gets `{"type":"auth","ok":false,"view":true,…}` and the socket closes (`:1201-1211`).
- **Any Bearer value that is not `swk_…` is checked as the PIN** (`WebServer.cs:1073-1074`). With no PIN set it is let in
  (`:869-870`). So on an open Stagewatch a 200 proves nothing about the value. An unknown `swk_…` key is always a 401, PIN or no PIN
  (`:1079-1082`).
- `X-RateLimit-*` headers go out only when a key matched (`WebServer.cs:1121-1127`; `/api/context` at `:450`). `tls.request` returns no
  headers (`app/lib/tls.js:242-246`).
- **Every unknown key counts toward the lockout** of 5 a minute per address, on HTTP (`WebServer.cs:1081`) and on `/ws` admission (`:1157`
  through `Authorize`). A locked address gets 401 "too many wrong PINs or keys — wait a minute" for everything, PIN included (`:1077-1078`,
  `:873-874`).
- AGMessage stores any string up to 80 characters as the key (`server.js:847`). Show Bus §7 says AGMessage asks for a node-scope key
  (`Stagewatch/docs/SHOWBUS.md:138-139`).
- `/api/peers/context` needs Node scope (`WebServer.cs:456`), so a read key gets 403 "this API key is read only" (`:1085-1086`).
- `configure` returns early when nothing changed (`nodes.js:229`). The watchdog restarts a crashed server after 2, 4, 8, 15 and 30 s
  (`app/lib/resilience.js:30`).
- AGMessage's node id is 8 hex per install (`server.js:82`), which is a valid Screens id (`Stagewatch/src/Timer.Remote/Screens.cs:23-24`,
  `WebServer.cs:1195`). `screen disconnect` sends `{"type":"disconnect"}` (`WebServer.cs:97-120`), and AGMessage ignores it (`nodes.js:325-332`).

**Load on the key**
- The desk tick sends one GET per followed proposal every 3 s (`server.js:1090-1095`). `GET /api/proposals?from=<id>` already answers all
  of them in one call (`MainViewModel.Desk.cs:38-39`). A key has 600 requests a minute (`ApiKeys.cs:46`).
- The context is posted whenever its JSON changes, and at least every 5 s (`server.js:1118-1125`).

**Durability**
- `WriteLane.flushSync()` catches a failed write, records `lastError` and still returns true (`app/lib/resilience.js:120-140`).

**Tests that assume today's link**
- The fakes answer `/api/context` with `{"ok":true}`, not a §6 body: `server.test.js:246-250`, `:305-317`, and
  `app/test/units.test.js:111-114`. Those in `resilience.test.js:239-249` and `tls.test.js:91-118` answer a §6 body but send no
  `X-RateLimit-Limit`.

**Cadence, caps, Patterns (G42, G37)**
- spec-1-3 decision 12 keeps the cap `proposals` and the 1 s ping. Its unit A1 corrects `nodes.js:5,9` and `NODES.md:10`.
- AGMessage tells the operator that Patterns' Eye reads its context (`app/public/admin.html:136-137`, `server.js:298`, `context.js:3`).
  No Patterns or Mapper code calls it. §8's hub brings each app's light and headline through Stagewatch's `peers` (`ShowContext.cs:138`).

---

## 1. Decisions

1. **The stage line is `message "<text>" <info|warn|crit> off ack`, built by one pure function.** (G05)
   - `proposeStageLine(text, urgency)` in `lib/nodes.js`: whitespace is collapsed, `"` becomes `'`, the text is cut at 300 (as today,
     `server.js:696`), and an unknown urgency becomes `info`. The longest line is 323 characters.
   - The risk the desk stored (`{ok,id,state,risk}`, `MainViewModel.Desk.cs:32`) is kept on the message and shown while it is pending.
   - *Rejected:* Stagewatch accepting `ack` in the flash slot. An unknown word stays refused.

2. **The proposal is read as Stagewatch sends it.** (G17)
   - Top-level `ok` and `words` are read first, then a string `result`, then an object `result`.
   - `risk`, `created` (ms) and, when present, `collected` (cross-app SW-2) are kept.
   - The fakes speak `ToJson`'s exact shape through one shared helper. The §11 shape goes into the master through spec-1-3's single edit
     (SW-AG-1).

3. **A result AGMessage owes the desk is reported until the desk takes it or 30 minutes pass. A line runs at most once, across
   restarts and across desk restarts.** (G16)
   - **Identity.** A run is keyed `<desk id>/<proposal id>@<created>`. The desk id is the linked Stagewatch's §6 `node.id`
     (`ShowContext.cs:130`), and the desk tick does nothing until it is known. *Why:* today's ids repeat with every Stagewatch run.
   - **Write-ahead, checked.** `begin` records `running` and flushes `data/desk-runs.json` before the line runs. The flush counts only
     when the lane's `writes` went up and `lastError` is null. Otherwise the line is not run, and the result is failed: "AGMessage
     couldn't record the run before running it (<error>) — not run". *Why:* `flushSync` reports success on a failed write
     (`resilience.js:136-140`).
   - **The first report** goes at once.
   - **Retries** follow 3 → 6 → 12 → 24 → 30 s (then every 30 s), whatever the collect returns, at most 10 per tick. Before each retry,
     AGMessage reads `GET /api/proposals/<id>`. It posts only when `created` matches, `target.id` is this AGMessage and the state is
     `approved` or `undelivered`. Otherwise the result is settled with words: "the show desk restarted — p9 is another proposal now", or
     "already done".
   - **Outcome by status:**

     | status | meaning | AGMessage |
     |---|---|---|
     | 2xx | taken | delivered |
     | 409 | settled, unknown, or another node's (the desk's words) | settled, with the words in an event |
     | 404 | no queue, or another run's id (cross-app SW-2) | settled |
     | 400 | the desk can't read the body | settled: "AGMessage sent a result the desk couldn't read" |
     | 401 | the key is refused | **the link's refusal** (decision 7): the result stays owed, its schedule is paused, and the attempt doesn't count |
     | 429 | the key is over its minute | **the link waits** `Retry-After`; the attempt doesn't count |
     | 403 | this key may not report (scope) | retried on the schedule, with the desk's words (a 403 is never counted toward a lockout, `WebServer.cs:1084-1086`) |
     | 0 (no answer), 408, 5xx | not taken | retried on the schedule |

   - **Expiry.** 30 minutes after the run, an untaken result is dropped with one event. *Why 30 minutes:* the desk still takes a late
     report for a claimed proposal that went `undelivered` (`Approvals.cs:155`). 30 minutes bounds the work to ≤ 64 attempts and covers a
     desk restart or a link outage. No measurement this round; a deterministic test fences the schedule.
   - **Tombstones.** Delivered, settled and expired runs are kept 30 minutes (≤ 200), so a line seen again is never run again. They are
     persisted with the owed ones.
   - **A full table stops intake.** While 50 results are owed, the tick does not collect and does not recover. There is one event, and
     the context shows it in red. The desk then says it itself: "AGMessage didn't collect it" (`Approvals.cs:185-187`), and after
     cross-app SW-2 it refuses approvals for a target that isn't collecting. *Why:* dropping entries from persistence would make "at most
     once" a lie.
   - **A different desk.** When the linked desk's id changes, the results owed to the old one are settled with an event: "the show desk
     changed — Stagewatch a1b2… never took the result of p9".
   - **After a restart,** a `running` entry is reported failed with "AGMessage restarted before it could confirm this ran — check the
     meetings", and is never run again. A `ran` entry whose result wasn't taken is owed again.
   - *Rejected alternatives:* retry only on the next collect (today's behaviour: it never comes again); keep runs in memory (a watchdog
     restart loses them); retry forever (unbounded); key by bare id (posts onto another proposal after a desk restart).

4. **A lost collect reply is recovered by reading the desk's queue, not by a new Stagewatch word.** (G16, the half the gap gave to
   Stagewatch)
   - **When:**
     - a collect that got no answer: status 0, a timeout or reset, 408, a 5xx, or a 200 AGMessage couldn't read;
     - the first desk tick after each verified link, which covers AGMessage dying between a collect and its record, and a link that
       dropped mid-collect.
   - **What:** after the tick's collect, one `GET /api/proposals` (read scope, the desk's whole queue). AGMessage runs each proposal
     that is `approved`, whose `target.id` is this AGMessage, whose key is in no run or tombstone, and that this tick's collect didn't
     return. When the JSON carries `collected` (cross-app SW-2), it runs only the collected ones, which is exactly the lost-reply case.
     The flag clears when the read answers 200.
   - **The unclaimed race on today's Stagewatch.** A proposal approved between the collect and the read can be run unclaimed. The next
     collect claims it, and AGMessage dedupes it by key.
   - **Size:** bounded by the desk's own retention (≤ 200 pending, settled trimmed to 500, `Approvals.cs:42`, `:192-194`), and under
     `tls.request`'s 1 MB reply cap (`tls.js:241`). It is asked only on the two events above.
   - *Why not `redeliver=1`* (the first draft): a new wire word, a 10 s gate with no measured cause (Stagewatch's "Measured, then
     governed"), a journal row per re-offer and an interface change. The queue the desk already serves answers the question. Removing
     the cost beats governing it. It also works against today's Stagewatch.

5. **Every token kind is heard and PlayMap is named. The table is bounded at 128 peers.** (G18)
   - `t` and `id` follow Stagewatch's reader (`[A-Za-z0-9_-]{1,16}` and `{1,40}`, `RemoteSlides.cs:132,186`). That accepts every 1.3
     sender (spec-1-3 decision 4).
   - A new id is not added once 128 peers are listed (`RemoteSlides.cs:195`; spec-1-3's "peers kept 128"). An event says so once a
     minute.
   - Labels: `playmap: '🎞 PlayMap'`. An unknown kind is shown as its own word. The hint names PlayMap.
   - AG-4 builds on spec-1-3 A1's hearing rule (one id, one peer, loopback first) inside the same handler.

6. **AGMessage reads Stagewatch's items and commands. The brief and the admin page show what needs attention and every window. The
   admin may propose any line Stagewatch lists, and the risk shown is Stagewatch's.** (G19)
   - **`readStageFacts(ctx)`** (pure, `lib/context.js`), by spec-1-3's reader rules:
     - reads ≤ 256 items;
     - `attention` holds red items, then amber, in Stagewatch's order, ≤ 6, with `moreAttention`;
     - `windows` holds every `window:*` item not already in `attention`, ≤ 8, with `moreWindows`;
     - `commands` holds ≤ 256 rows; a verb over 512 characters is **dropped**, never cut; `words` keeps 200; a risk outside
       `low|review|high` reads as `high`;
     - labels keep 80 characters, states 160;
     - ids keep 80 characters with control characters removed, and an item is **never dropped for its id** (so `cue:front of house`
       stays).
   - **The brief** (`showBrief`) gains `  needs attention [<light>]: <label> — <state>` and `  window <Name> [<light>]: <state>` lines,
     plus "(+N more)". The AI still drafts only words.
   - **The admin page** shows the headline, the attention list and the windows, each with a mark beside its colour (✕ red, △ amber,
     ✓ green, ○ grey) and "+N more". It adds a **Propose to Stagewatch's show desk** panel built from Stagewatch's `commands`.
   - **Window actions come from the listed rows.**
     - `windowActions(commands)` takes the second words of listed forms that begin `window` and continue with a `<…>` window
       placeholder.
     - Against today's rows that is `show`, `hold` and `revoke`, and **no `release`**. Against windows-for-peers' rows, `hold` and
       `release` both appear.
     - Each window row offers a **Propose…** menu of those actions. A pick only fills the panel and sends nothing.
   - **Lines are checked before they leave AGMessage** (fail closed):
     - **Length:** 1 to 400 characters. A longer line is refused, never cut: "Lines proposed from AGMessage are at most 400 characters
       (an older Stagewatch cuts longer ones) — this one is 431. Nothing was cut or sent." *Why 400 and not 2048:* today's desk cuts
       silently at 400 (`Approvals.cs:88`), and 400 is under every desk's limit.
     - **Secrets:** refused in the line or the reason:
       - a word shaped `swk_…`, `agn_…` or `sk-ant-…`;
       - the stored AI key, this AGMessage's context token or its admin token (`server.js:56`, `:83`, `:86-90`);
       - `/w/` followed by 26 token characters (a Stagewatch window address, `ScreenWindows.cs:85-88`; cross-app decision 4);
       - any control character.

       A PIN is not fenced, because AGMessage no longer stores one (decision 7), as the desk's own rule does (cross-app decision 4).
     - **Listed verbs:** the leading words must match a listed form's fixed words. Alternatives are `a|b`, ` | ` and `a / b`. The fixed
       words end at the first `<…>`, `[…]` or `…`. A line that matches nothing is refused with the first word and the listed verbs.
   - **The risk.** Before sending, the page shows "Stagewatch lists it as <risk>", taken from the most specific matching form, higher on
     a tie. After sending it shows the risk the desk stored.
   - **Following proposals.** Research and admin proposals alike are followed with **one** `GET /api/proposals?from=<nodeId>` per tick,
     and only while one is open, matched by id and line. An id missing from a 200 list ends `undelivered` with "Stagewatch no longer has
     this proposal (it restarted) — propose it again".
   - **Admin proposals are persisted** (≤ 20) in `desk-runs.json` with their desk id, so a restart keeps following them. Research
     proposals are recovered from the room messages (`server.js:1044-1049`).
   - **Refusals from the desk are said.** A 403 on a collect or a proposal ("this key speaks for another node…", "this API key is read
     only") shows on the admin page and in the context item `desk:collect` (amber) with Stagewatch's words.
   - *Who:* the admin only, who logs in only on the host laptop (`server.js:262-263`). The research team keeps its one 📝 message
     proposal. *Left:* a research picker; AI-drafted proposals.
   - *Why not `GET /api/windows`:* the 5 s context read already carries each window's light and words.

7. **The link: a key only, the verdict from the evidence the socket offers, one door for refusals, and the board keeps its timer.**
   (G43, G32)
   - **A key only.**
     - `admin:settings` accepts only `swk_[A-Za-z0-9]{8,76}` as the Stagewatch credential. Anything else is refused and nothing is
       saved: "AGMessage takes a Stagewatch node key (swk_…). A PIN opens all of Stagewatch — make a node key: Stagewatch → Settings →
       Remote → API keys, scope node."
     - A non-key value saved by 0.4.x is not sent. The link runs keyless, and the admin page says why.
     - *Why:* Show Bus §7 says AGMessage asks for a node key (`SHOWBUS.md:138-139`). It is also the only credential whose 200 is
       evidence: a non-key value is let in by an open Stagewatch whatever it is (`WebServer.cs:869-870`, `:1073-1074`).
   - **Dial:** `ws(s)://<sw>/ws?node=<id>&t=messenger&cid=<id>&page=%2Fagmessage`, with the key in `Authorization: Bearer`.
   - **The first message decides which evidence counts.** There is no timer race.
     - **`auth` first** (a Stagewatch with peers-not-screens SW-3, which sends it before any frame) puts the link on the **socket path**.
       The latest verdict always wins.
       - `ok:true` with `node.verified` `key` or `operator`: `connected`, auth `key` or `operator`, and the pin is confirmed.
       - `ok:true` with `verified:"none"` (a binding conflict, or a read key): `connected`, auth `unbound` or `read`, with Stagewatch's
         `words`. Frames are shown and the pin is confirmed, because the scope is not `none`.
       - `ok:false`: the refusal below.
     - **Anything else first** (a `state` or `beat` frame: a Stagewatch that sends no node-link verdict) puts the link on the **HTTP
       path**. The frame is held while AGMessage asks `GET /api/context` with the key.
       - A 200 with a §6 body and `X-RateLimit-Limit` (Stagewatch's own statement that a key matched, `WebServer.cs:1121-1127`) gives
         `connected`, auth `key`, and the pin is confirmed.
       - A 401 is the refusal below.
       - Any other answer stays `connecting`, with words such as "Stagewatch answered the link but not its context (404 no context) —
         AGMessage can't confirm the key". It is asked again every 5 s.
       - An `auth` message arriving later on this socket overrides the HTTP verdict (latest wins).
     - **Scope on the HTTP path** comes from the first context post (Node scope, `WebServer.cs:456`). A 403 makes auth `read`, with
       "this API key is read only — AGMessage needs a node key to send to the stage and take part in the show desk" (amber).
     - **Keyless:** the first frame gives `connected`, auth `none`, and the trust-on-first-frame pin is **unchanged** (`nodes.js:329`). No
       secret is ever sent on that link, and the pin then guards a key added later.
   - **One door for keyed HTTP** (`_call`, `nodes.js:284-287`):
     - any 401 calls `_refused(error, 60)`;
     - a 429 waits for `Retry-After`;
     - while a hold lasts, `_call` throws `held` and sends nothing.

     That covers the context read and post, the stage send, propose, follow, collect, the recovery read and every report.
     `tls.request` returns the response headers.
   - **Refusal:**
     - the status becomes `key refused`, and Stagewatch's words go to admin sockets only;
     - `heldUntil` is now + max(60 s, `retry`), persisted as `settings.stageHold {until, base}` so a crash loop can't sum past the
       lockout;
     - keyed HTTP stops;
     - while Stagewatch keeps sending the public frame, the socket stays open and the board keeps the time, marked "viewing only";
     - when the hold ends: on the HTTP path with the socket open, one `/api/context` read; otherwise close and dial.
     - `_retry` never overwrites `key refused`, a certificate problem or a `disconnect` hold.

     *Why 60 s:* on today's Stagewatch each re-check costs one counted failure (two if it redials). That stays under the 5-a-minute
     lockout, which would also lock out a browser on the same machine.
   - **"Link again now"** (admin) clears the hold, in memory and in settings, and re-verifies at once. Re-saving the same key and
     address does nothing (`nodes.js:229`), and the admin page says so.
   - **No verdict** 12 s after the open (`GONE_MS`, an option for tests) closes the socket and redials with the backoff.
   - **The shared `stage` event** carries a status from a closed set: `connected`, `connecting`, `reconnecting`, `stale`, `key refused`,
     `certificate refused`, `off` (as peers-not-screens' AG-1 has it). Words go only to admins, in a new `admin:stageLink`. The board
     chip (`app/public/app.js:296`) is stale when the view is stale or the status is anything other than `connected` or `key refused`.

8. **One id per install; the operator's `disconnect` holds the redial 30 s.** (G32)
   - `node=<nodeId>` names the install to a Stagewatch with SW-3. `cid=<nodeId>` gives an older Stagewatch one Screens row per install.
     PlayMap already does this (`StagewatchLink.cs:283-284`).
   - `{"type":"disconnect"}` holds the redial 30 s, with the words "disconnected by Stagewatch's operator — linking again in 30 s" (PlayMap's
     hold, `StagewatchLink.cs:59`, `:404`). `identify`, `redirect` and `reload` stay ignored.

9. **Cadence and caps are spec-1-3's.** Decision 12 there keeps `proposals` and the 1 s ping, and its unit A1 corrects the papers. This
   design changes neither. The first draft's rename to `proposes` is withdrawn.

10. **AGMessage does not read Patterns' context, and stops saying Patterns does.** (G37)
    - §8's hub already brings Patterns' light and headline through Stagewatch's `peers`. Patterns' HTTP context needs its control token,
      which is too strong for a messenger.
    - The admin text and comments say what is true: Stagewatch receives this context every 5 s, Patterns and PlayMap see it as one line
      in Stagewatch's peers, and the token is for a tool or AI that asks directly.
    - *Left:* Patterns reading peers' contexts with a typed token (Patterns' own round).

---

## 2. Wire (exact shapes)

### 2.1 AGMessage → Stagewatch (all with `Authorization: Bearer swk_…`; no new route, verb or field on Stagewatch)

- **Research proposal:** `POST /api/proposals`
  `{"from":{"t":"messenger","id":"ab12cd34","name":"AGMessage"},"target":{"t":"main"},"line":"message \"Capex was '4.2m'\" warn off ack","reason":"The research team in AGMessage suggests this for the speaker.","risk":"review"}`
  - Stagewatch tokenizes it as `message` · `Capex was '4.2m'` · `warn` · `off` · `ack`.
  - The reply is `201 {"ok":true,"id":"p17","state":"pending","risk":"review"}`.
- **Admin proposal:** the same route, with `"line":"window hold lobby"`, `"reason":"AGMessage's admin proposes this: <reason>"` and
  `"risk":"<the listed row's>"`. It is sent only if decision 6's checks pass.
- **Follow:** `GET /api/proposals?from=ab12cd34`, once a tick, only while one proposal is open.
- **Collect:** `GET /api/proposals?target=ab12cd34&state=approved` (unchanged).
- **Recovery:** `GET /api/proposals` (read), only after a collect without an answer, or on the first tick after a verified link.
- **Check before a retry:** `GET /api/proposals/<id>`.
- **Result:** `POST /api/proposals/<id>/result {"ok":true,"words":"shown in 2 meetings"}` (unchanged body, ≤ 300), retried per decision 3.
- **Link:** `GET /ws?node=ab12cd34&t=messenger&cid=ab12cd34&page=%2Fagmessage`.
  - AGMessage reads `{"type":"auth","ok":true,"scope":"node","node":{"t":"messenger","id":"ab12cd34","verified":"key"}}`, the same with
    `"verified":"none","words":"…"`, `{"type":"auth","ok":false,"error":"…","retry":60}`,
    `{"type":"auth","ok":false,"view":true,"error":"…"}` and `{"type":"disconnect"}`.
  - On the HTTP path it reads `GET /api/context`: a 200 with a §6 body and `X-RateLimit-Limit` is the verdict, and a 401 is the refusal.
  - Context post: `POST /api/peers/context` (unchanged). A 403 there shows the scope. A 200 may carry `verified` (peers-not-screens),
    which AGMessage reads when present.
- **Hello:** unchanged by this design (spec-1-3 A1 owns `proto` and caps).

Request budget per key: collect 20, follow ≤ 20, context read 12 and context post ≥ 12 a minute. At most 50 owed results each make 4
requests a minute (GET and POST every 30 s). Worst case ≈ 270 of the key's 600 a minute (`ApiKeys.cs:46`).

### 2.2 Stagewatch → AGMessage (read)

- **Proposal** (`ToJson`, `Approvals.cs:20-31`). AGMessage reads `ok` and `words` first, plus `created` and, when present, `collected`.
  - Shape: `{"id":"p17","from":{t,id,name},"target":{t,id,name},"line","reason","risk","state","created":1761400000000,"decided","decidedBy","result":"<words>","ok":true|false|null,"words":"<words>"[,"collected":ms|null,"form":"…"]}`.
- **Hellos:** any `t` of `[A-Za-z0-9_-]{1,16}` with an `id` of `[A-Za-z0-9_-]{1,40}`. At most 128 are kept.
- **Context:** `items` (≤ 256 read) and `commands` (≤ 256; verbs > 512 dropped) are now used.

### 2.3 Stagewatch: nothing new on the wire

These words go to spec-1-3's single master edit (S1) for §11 (SW-AG-1):
- the proposal JSON shape, with top-level `ok` and `words`, plus `created`;
- `undelivered` also means "collected but no result in 5 minutes";
- a target retries its result until the desk takes it (a late report for a claimed proposal still lands);
- a target that lost a collect reply finds its claimed, approved proposals in `GET /api/proposals` and runs only those it never ran,
  keeping what it ran across restarts;
- a target pairs an id with `created` (ids are unique only within a desk run until cross-app SW-2's run tag).

### 2.4 AGMessage's own surfaces

| surface | change |
|---|---|
| Socket.io `admin:settings` patch | `stagewatchKey` must be `swk_…` or empty; otherwise `{ok:false,error}` and nothing is saved |
| `admin:settings` reply | `stageContext` gains `attention[]`, `windows[]` (`{id,label,light,state}`), `commands[]` (`{verb,risk,words}`), `windowActions[]`, `moreAttention`, `moreWindows`; new `link:{auth:"key"\|"operator"\|"unbound"\|"read"\|"refused"\|"none"\|"", scope, words, heldUntil, evidence:"ws"\|"http"\|""}`; new `deskProposals[]` |
| `admin:stageLink` (new push, admin sockets) | the `link` object above, on change |
| `admin:linkAgain` (new, admin) | `{}` → `{ok:true, status}` |
| `admin:deskPropose` (new, admin) | `{line, reason}` → `{ok:true, proposal:{id,state,risk,line}}` or `{ok:false, error}` |
| `admin:deskProposals` (new push, admin) | `[{id,line,state,risk,by,words,at}]`, newest first, ≤ 20 |
| `stage` event (everyone) | `status` from the closed set; no refusal words |
| `proposalUpdate` / message `proposal` | gains `risk` |
| `GET /api/health` detail (admin or host) | `desk:{owed, oldestMs, nextInMs, collecting, lastError}`, `link:{auth, heldUntil}` |
| `GET /api/context` (its own §6) | `desk:results` (`kind:"link"`, amber) "N result(s) for Stagewatch not yet taken — retrying", or red "AGMessage has stopped taking approved lines: 50 results the show desk hasn't taken", only while true, with the headline part "N desk result(s) owed"; `desk:collect` amber with Stagewatch's 403 words; `link:stagewatch` state `key refused: <Stagewatch's words>` (red), `<words>` for `unbound`/`read` (amber), `viewing only — no key` for keyless. No countdown in any state, so the posted context changes only on events |

Refusal words for an admin proposal:
- `This Stagewatch keeps no approval queue (Show Bus 1.2) — nothing was sent.`
- `The Stagewatch link is key refused — not proposed.`
- `Stagewatch doesn't let this key speak for this AGMessage — nothing was sent: <words>`
- `Lines proposed from AGMessage are at most 400 characters (an older Stagewatch cuts longer ones) — this one is 431. Nothing was cut or sent.`
- `A key, token or window address never rides in a line or a reason — nothing was sent.`
- `Stagewatch doesn't list "format" for other apps — it lists: note, message, signal, nudge, trim, delay, next, start, pause, blackout, window, patterns.`

---

## 3. Changes, file by file

### AGMessage (0.5.0, after spec-1-3 A1's 0.4.1)

**`app/lib/nodes.js`**
- `proposeStageLine(text, urgency)` and `linkUrl(base, nodeId)`, both pure and exported. `isNodeKey(s)` checks
  `/^swk_[A-Za-z0-9]{8,76}$/`.
- `readProposal` (`:166-178`): words come from top-level `words` (string), else `result` (string), else `result.words ?? result.error`,
  else `error`. `ok` is top-level `ok`, else derived from an object `result`, else null. It keeps `risk` and adds `created` and
  `collected`.
- `parseHello` (`:37-52`): `KIND_RE`, `ID_RE`, and `KINDS` removed. `Discovery.heard(h, now)` takes the handler's body with A1's rule,
  and `MAX_PEERS = 128`.
- `StagewatchLink`:
  - options `nodeId`, `goneMs`, `holds {refusedMs: 60_000, disconnectMs: 30_000}` and `hold {until, base}`. Fields `auth`, `evidence`,
    `heldUntil`, `pendingView`, `firstSeen`. Callback `onLink`.
  - `configure` (`:225-239`): a non-key value is not sent, with words. It clears holds only when the key or address changed.
  - `linkAgain()` clears the hold and re-verifies.
  - `_dial` (`:316-335`): the URL from `linkUrl`. `onopen` stays `connecting`. `onmessage` decides the path on the first message.
    `auth` messages are handled at any time (latest wins). `state` frames go to `pendingView` until verified (or keyless or refused).
    `disconnect` holds 30 s.
  - `_verified(auth, scope, words)`, `_refused(error, retry)` and `_wait(seconds)`.
  - `_retry` (`:341-348`) respects refusals and holds. `_watch` (`:350-359`) re-asks while on the HTTP path and closes at `goneMs`.
  - `_call` (`:284-287`) is the one door: a hold means no I/O; a 401 is a refusal; a 429 is a wait.
  - `fetchContext` (`:387-398`) is the HTTP verdict, with the header check. `pushContext` returns `{ok,status,error,verified}` and sets
    auth `read` on a 403.
  - `followFrom(nodeId)`, `listAll()`, `proposal(id)`, and `approvedFor` returning `{status, items}` so a lost reply can be told apart.
    `reportResult` returns `{ok,status,error}`.
  - The header comment (`:5-20`, apart from A1's lines): every kind, the verdict, the one door.

**`app/lib/tls.js`** (`:242-246`): `request` resolves `{status, headers, body, text}`.

**`app/lib/desk.js`** (new, pure, no `fs`)
- `runKey(deskId, p)`.
- `DeskRuns({expireMs:30*60_000, firstRetryMs:3000, maxRetryMs:30_000, maxOwed:50, keepTombs:200}, saved, now)`: `has(key)`, `begin`,
  `finish`, `due(now, 10)`, `reported(key, status, error, now)` (with `link` for 401/429), `settle(key, words)`, `changeDesk(deskId)`,
  `expire(now)`, `full()`, `stats(now)`, `toJSON()` (owed + tombstones + `admin[]`), and `restore` (`running` → failed with words).
- `reportOutcome(status)` → `delivered|settled|retry|link`. `retryDelay(attempts)`. `retryCheck(run, proposalRead)` → `post|settled`
  with words. `toRecover(list, nodeId, runs, collectedIds)`.
- `fixedWords`, `matchListed(commands, line)` (the most specific form, higher on a tie), `windowActions(commands)`, and
  `checkDeskLine(line, reason, commands, secrets)`.

**`app/lib/context.js`**: `readStageFacts(ctx)`. `showBrief` (`:103-117`) gains the attention and window lines with "+N more".
`buildContext` (`:25-100`) gains `desk:results`, `desk:collect` and the link words (no countdowns). The header comment (`:2-4`) follows
decision 10.

**`app/server.js`**
- `:699-713`: the line comes from `proposeStageLine`, and `proposal.risk` is kept.
- `:841-863`: the key check on save; the reply gains `stageContext`, `link` and `deskProposals`.
- New handlers `admin:linkAgain` and `admin:deskPropose`, with the socket's write limiter as `:692`.
- `:995-1011`: `StagewatchLink({…, nodeId, hold: settings.stageHold})`. `onLink` persists the hold and pushes `admin:stageLink`.
  `onState` emits the closed-set status.
- `:1034-1103` (desk tick) is rebuilt:
  - the desk id from the context first;
  - owed results for an older desk are settled;
  - due reports, each retry after `retryCheck`;
  - one `?from=` follow;
  - collect, unless full: skip a foreign `target.id`, then `begin`, flush and check, `runApproved`, `finish`, save, report;
  - the recovery read when flagged;
  - `expire`.
- Desk state lives in `data/desk-runs.json` through `resilience.readJsonWithBak` and its own `WriteLane` (`delayMs 100`, `mode 0o600`).
- `healthFacts` (`:119-132`) and `currentContext` (`:1106-1114`) gain desk stats. `shutdown` (`:1208-1218`) and the crash handler
  (`:1225-1234`) flush the desk lane.
- The comment at `:298` changes (decision 10). `VERSION = '0.5.0'` (`:42`).

**`app/public/admin.html`** (`:113-137`): the facts block, the propose panel (verb select, risk chip, line input with a window
datalist, reason, Propose, the proposals list), "Link again now" beside Link, and the token text (`:136-137`, decision 10).

**`app/public/admin-plus.js`**
- `:20-27`: the link words from `link`, the facts lists with marks, "+N more", and "Link again now".
- `:71-72`: the PlayMap label and hint; an unknown kind shown raw.
- The propose panel and the per-window **Propose…** menu, both from `windowActions`.
- `renderHealth` (`:53-66`) gains the owed and collecting lines.

**`app/public/app.js`**
- `:181-193`: pending reads "📝 Waiting for the show desk to approve (Stagewatch rates it <risk>)", and `undelivered` shows its words.
- `:296`: the chip rule of decision 7.

**`app/public/styles.css`**: `.fact-list` and `.risk-chip`, with a mark and a word beside the colour.

**Tests**: §4. **Papers** (AG-7):
- `docs/NODES.md`: rows `:9` (every kind, PlayMap, 128), `:10` (the URL, the evidence paths, the key-only rule, the one door, the
  holds; A1 owns the ping wording), `:13` (items and windows read), `:14` (the line with `off`; results keyed, retried, persisted;
  recovery), and a new row for the admin's proposals.
- `CHANGELOG.md` 0.5.0 with "Tests: N (was M)", and `README.md:17-21`.
- `app/package.json` 0.5.0.
- `docs/SHOWBUS.md` follows the master if spec-1-3 S9 cuts again with §11's words. The copy is byte-identical under its hash
  (spec-1-3 decision 10).

### Stagewatch (papers and interop rows only; no code)

- **SW-AG-1:**
  - §2.3's words go to spec-1-3 S1's single master edit (§11).
  - The AGMessage section words go to spec-1-3 S6's rewrite of `docs/NODES.md` (G41): the URL, the evidence paths, key-only, results
    keyed and retried, the recovery read.
  - `docs/WHATS_NEXT.md`: Patterns and PlayMap could adopt the same three things: runs kept across restarts, results retried, and the
    recovery read.
- **SW-AG-2** (in the ecosystem lane's `tests/interop`, under its decisions 1 and 4: each row lands with the pin bump that makes it
  pass; "not run" is never counted as passed; CI's `interop` job checks AGMessage out at its pin with `SW_INTEROP_REQUIRED=1`):
  - **S6.5** reworded: "AGMessage dies after a run and comes back". The notice is run and the rig's proposal reads `done`. Then
    SIGKILL AGMessage and restart it in the same data folder. For 10 s there is no second notice on the board socket, and the rig shows
    no second result. The recovery read at start finds it `done`, not `approved`. "Results re-offered" is dropped, because the rig
    cannot make a result post fail on cue. The owed-result-survives-a-restart case is AGMessage's own server test.
  - **S2.8 (new) AGMessage's link verdict:** `admin:settings.link.auth == "key"`; the rig's context `peers` row for AGMessage's id has
    `link:"ws"` (after peers-not-screens SW-3), or `/api/screens` lists one row with AGMessage's id (before).
  - **S2.9 (new) a lost window reaches AGMessage:** a window `lobby` opened in Playwright, shown, then closed puts `window:lobby` amber
    "link lost …" in `stageContext.attention` within 9.5 s.
  - **S3.9 (new) the admin proposes a window hold:** `admin:deskPropose window hold lobby` reads pending `high` on the rig. After it is
    approved, `/api/windows` shows lobby `hold:true`, and `admin:deskProposals` shows it `done`.
  - **S7.4 (new) a wrong key is held:** AGMessage `stage.status == "key refused"`, and the rig's log shows at most two counted attempts
    in 60 s (one `/ws` and one `/api/context` before SW-3; one `/ws` after).

### Patterns, Mapper
No change in this area. PlayMap already announces (G18). G37 is decided without a Patterns change. Patterns' halves of G32 and G43 are
peers-not-screens PT-1 and PT-2.

---

## 4. Tests

**Shared fake** — `app/test/fake-stagewatch.js` (new): `context(res)` writes a §6 body with `X-RateLimit-Limit: 600`. `proposal(p)`
returns `ToJson`'s shape. `auth(ws, verdict)` sends a socket verdict. units, resilience, tls and server tests all use it, so no fake
drifts from Stagewatch again.

**`app/test/units.test.js`**
- `proposeStageLine` against a 6-line port of `McpEndpoint.Tokenize`: token 4 is `off`, token 5 is `ack`, a `"` becomes `'`, and an
  unknown urgency becomes `info` (G05).
- `readProposal` on `ToJson`'s exact shape (`done`, `failed`, `pending`, `undelivered`), with `created` and `collected`, and the old
  object shape (G17).
- `DeskRuns`:
  - the key includes the desk and `created`, and a new `p9` with another `created` is not deduped;
  - the outcome table (401 and 429 → `link`, not counted; 403 → retry);
  - backoff 3/6/12/24/30/30 s, at most 10 due per tick, and expiry at exactly 30 min;
  - `retryCheck` settles on a `created` mismatch, a foreign target, `done` or a 404, and posts on `approved` or `undelivered`;
  - `changeDesk` settles with words;
  - `running` restores as failed and is never re-run; tombstones survive `restore` and dedupe;
  - `full()` at 50.
- The write-ahead check: with a writer that throws, the line is not run, and the result is failed "couldn't record" (G16).
- `toRecover`: it honours `collected` when present, skips runs, tombstones and this tick's collected ids, and skips a foreign
  `target.id` (G16).
- `matchListed`, `checkDeskLine` and `windowActions` against today's 15 rows (no `release`), cross-app's §6-notation rows and
  windows-for-peers' 13 rows. Unlisted verbs, 401 characters, `swk_`, `agn_`, `sk-ant-`, the stored AI key, `/w/` + 26 and a control
  character are each refused with their words. `nudge 1234` passes (G19).
- `readStageFacts`: reds before ambers, ≤ 6 and ≤ 8 with the "more" counts, a 600-character verb dropped (not cut), `words` cut at
  200, and `cue:front of house` kept (G19).
- `linkUrl` carries `node`, `t` and `cid` and never a key. `isNodeKey` accepts `swk_…` and refuses `1234` and `swk_`.
- The existing link test (`:107-140`) uses the shared fake and stays green.

**`app/test/resilience.test.js`**
- Hellos (`:173-180`): `playmap` and `toaster` are heard. `bad kind`, `''`, 17 letters, `<x>` and the id `a b` are dropped. The 129th id
  is refused through `heard` (G18).
- The brief carries attention and window lines with "+N more" (G19).
- The link (`:235-290`, with the shared fake):
  - **HTTP path:** frames are held and the status is `connecting` until `/api/context` answers 200 with the header. Without the header
    it stays `connecting`, with words.
  - **A 401:** the status is `key refused`, the board's view keeps flowing, the fake sees **no** keyed request for the hold, and
    exactly one `/api/context` re-check follows when the hold ends (with an injected 1 s hold).
  - **Socket path:**
    - `auth ok verified key` first gives `connected`, with no verdict read of `/api/context`;
    - `verified none` gives `connected`, auth `unbound`, with words;
    - `auth ok:false retry 60` gives `key refused`, sticky.
  - **Latest wins:** a context 200, then `auth ok:false`, gives `key refused`.
  - **Other cases:**
    - a 429 with `Retry-After: 2` sends nothing for 2 s;
    - `disconnect` gives exactly one dial in 1.5 s;
    - no context route (404), with `goneMs` 1 s, closes and stays `connecting`, with words;
    - `linkAgain()` clears a hold;
    - a link built with a saved, unexpired hold dials only after it ends;
    - the fake sees `/ws?node=…&t=messenger&cid=…` (G43, G32).
- `buildContext` shows `desk:results` while a result is owed and `desk:collect` on a 403, and two builds 1 s apart are equal (no
  countdown) (G16, G19).
- The guard rails (`:293-315`) are unchanged: `lib/desk.js` has no `fs`, and the desk lane writes through `WriteLane`.

**`app/test/tls.test.js`**
- `fakeDesk` (`:91-118`) speaks `ToJson`'s shape through the shared helper. `:141` expects the line with `off`, and `:148` reads
  `reportResult(...).ok` (G05, G17).
- The pin is not confirmed while `/api/context` is held 500 ms after a frame, and is confirmed after it answers with the header. A fake
  answering 401 is never pinned (G43).
- A keyless link is pinned on its first frame, unchanged.

**`app/test/server.test.js`**
- Both fakes (`:246-250`, `:305-317`) answer a §6 context with the header through the shared helper. `:312` uses the `ToJson` shape and
  `:336` the line with `off`.
- **Result retry:** the fake answers 503 twice, then takes the result. There are three identical bodies, one `GET /api/proposals/p9`
  before each retry, one notice in the meeting, and health `desk.owed` back at 0.
- **An owed result survives a restart:** with the fake down, stop and start AGMessage, then bring the fake back. The original words
  arrive, and there is one notice.
- **A lost collect reply:** the fake claims `p9` and destroys the socket on the first collect. The next tick's `GET /api/proposals`
  lists it, and it runs once, with one notice and one result.
- **A restarted desk:** an owed result for `p9`@c1 meets a `p9`@c2 approved for AGMessage. The c1 result is settled, never posted.
  `p9`@c2 is run and reported. There are two notices, one each.
- **A full table:** with 50 owed results, no collect is sent, and the context shows the red item.
- **The admin's desk:**
  - `admin:deskPropose window hold lobby` sends a body with `risk:"high"` and `target.t:"main"`;
  - `format c:`, `note swk_x…` and a 401-character line are refused, and nothing is sent;
  - the admin proposal is followed to `done` through one `?from=` GET per tick (counted: no per-id GETs);
  - it is still followed after a server restart.
- **Settings:** `admin:settings` refuses `stagewatchKey:"1234"` and saves nothing. `admin:settings.stageContext.windows[0].id ===
  "window:lobby"`.

**`app/test/browser-smoke.js`**: the admin page with a linked fake shows the facts, the propose panel and "Link again now", with no
script or CSP error. Screenshot `admin-desk.png`.

**Stagewatch** (`tests/interop`, SW-AG-2): rows S2.8, S2.9, S3.9 and S7.4, and S6.5 reworded, as in §3. cross-app SW-2's
`fixtures/peer-lines.json` carries AGMessage's line with `_source: AGMessage/docs/NODES.md:14`. It is tokenized and parsed as `Message`
warn/off/ack, and the old line fails with "flash must be off|slow|fast".

---

## 5. Units, in commit order (each builds and tests alone)

| # | repo | unit | gaps | depends on |
|---|---|---|---|---|
| AG-1 | AGMessage | The stage line Stagewatch parses (`message "…" <urgency> off ack`), and the risk it gave shown on the message | G05 | — |
| AG-2 | AGMessage | The desk's proposal read as Stagewatch sends it; the shared fake Stagewatch in `ToJson`'s shape | G17 | — |
| AG-4 | AGMessage | Every token kind heard, PlayMap named, 128 peers at most | G18 | spec-1-3 A1 (same handler) |
| AG-5 | AGMessage | The link: key-only credential; node/t/cid; the evidence path set by the first message; latest verdict wins; one door for 401/429; refusal keeps the public timer; persisted hold; Link again now; `disconnect` held; closed-set status | G43, G32 | AG-2 |
| AG-3 | AGMessage | Results keyed by desk, id and `created`, written before they run (checked), retried with a read-back until taken or 30 min; tombstones; intake stops at 50; the recovery read for a lost collect reply | G16 | AG-2, AG-5 |
| AG-6 | AGMessage | Stagewatch's attention items, windows and commands in the brief and the admin page; window actions from the listed rows; the admin's desk proposals, fenced, followed with one `?from=`, persisted | G19 | AG-1, AG-2, AG-3, AG-5 |
| AG-7 | AGMessage | Papers and the 0.5.0 release; the context-token text and comments | G37 | AG-1–AG-6 |
| SW-AG-1 | Stagewatch | Words for §11 (via spec-1-3 S1), for `NODES.md` (via spec-1-3 S6), and `WHATS_NEXT.md` | G16, G17 | spec-1-3 S1/S6, cross-app SW-2 (its run tag named) |
| SW-AG-2 | Stagewatch | Interop rows S2.8, S2.9, S3.9 and S7.4, and S6.5 reworded (ecosystem lane, with SW-R8) | G44 (part) | ecosystem-lane SW-L2 and SW-R1, peers-not-screens SW-6, AG-3–AG-6 at a pinned commit |

AGMessage's units are safe against today's Stagewatch:
- `node` and `t` are ignored, and `cid` gives each install its own row;
- the HTTP path is today's evidence;
- the recovery read and the read-back use routes every Stagewatch with a desk serves.

---

## 6. Gaps

**Closed**
- **G05:** the line is valid and rated review by the desk's own parse. Stagewatch's fence on peers' documented lines is cross-app SW-2,
  with AGMessage's row.
- **G16:** AGMessage reports until the desk takes it or 30 minutes pass, and runs each line at most once across its own restarts and
  the desk's. A lost collect reply is recovered from the desk's queue, which needs no Stagewatch change.
- **G17:** top-level `ok` and `words` are read, the fakes speak the desk's shape, and §11 states it (via spec-1-3 S1).
- **G18:** every token kind is heard, PlayMap is named, and the table is bounded at 128 as Stagewatch's is.
- **G19:** red and amber items and every window reach the brief and the admin page. The admin proposes any listed line, window hold and
  release included where Stagewatch lists them, with Stagewatch's risk shown.
- **G32 (AGMessage's half):** one id per install, with or without SW-3.
- **G37:** decided: no direct read, and AGMessage's text is corrected.
- **G43 (AGMessage's half):** `connected` and the pin come only from Stagewatch's verdict or a key-matched context read. A refusal is
  sticky, held and seen.

**Left**
- **G43, G32 (other halves):** Stagewatch's verdict on node links is peers-not-screens SW-3, and Patterns' dial is PT-1 and PT-2.
- **G42:** spec-1-3 A1 and its decision 12 (keep `proposals` and the 1 s ping). This design adds nothing.
- **G37 (Patterns side):** Patterns' Eye still cannot draw AGMessage's open prompts. Only the headline arrives through Stagewatch.
- **G19 (part):** no research-team picker and no AI proposals; neither has an ask from the room.
- **G44:** the lane and its CI job are the ecosystem lane's. This area adds four rows and rewords S6.5. Result-post faults stay in
  AGMessage's own suite, because the rig cannot fail a result post on cue.
- **New finding (outside the 49):** Patterns (`StagewatchLinkService.cs:459-463`) and PlayMap (`StagewatchLink.cs:538-552`) also post
  each result once and keep ran ids in memory. They could adopt the same three pieces (SW-AG-1's WHATS_NEXT line).

---

## 7. Risks

- **A Stagewatch with no `/api/context`.** On a Stagewatch without node-link verdicts, AGMessage needs one key-matched `/api/context`
  read before it shows `connected`. Where that route answers 404 (`WebServer.cs:449`), the link never connects and the admin page says
  why. Today it would say `connected`.
- **Installs that saved a PIN.** An install that saved a PIN or another non-key value loses send-to-stage, the context post and the
  desk until a node key is made. The admin page says so, in the words of decision 7.
- **A refused key's hold.** A refused key holds keyed calls for 60 s. After a fix on Stagewatch's side, "Link again now" re-verifies
  at once.
- **The write-ahead fsync.** It costs one fsync per approved run, through `WriteLane` (a handful per show). The guard-rails caps are
  unchanged.
- **The recovery read's size.** It lists the whole queue, at most ~700 proposals under the desk's own bounds, and only after a lost
  reply or on a fresh link. If a sibling ever narrows the unfiltered list, the recovery needs `?target=` without a claim, and that would
  be a Stagewatch change. It is said in SW-AG-1's words.
- **The unclaimed race.** On today's Stagewatch a recovery read may run a just-approved proposal before it is claimed. The next collect
  claims it, and the key dedupes it. After cross-app SW-2, `collected` removes the race.
- **Local check drift.** AGMessage's line check follows Stagewatch's row notation. A future notation could refuse a line Stagewatch
  would accept. It fails closed with the listed verbs, and the tests carry all three known notations.
- **High-risk admin proposals.** The admin can propose high-risk lines. The desk rates them high, never pre-ticked, and a person
  decides. The admin page is host-laptop only.
- **Sibling overlap.** cross-app, peers-not-screens (AG-1–AG-3), spec-1-3 (A1), windows-for-peers (AG-W1) and the ecosystem lane (SW-R8)
  must point at this design's units. A1 lands first because it edits the same hello handler.

---

## 8. Process notes

**AGMessage** (no agent brief; its conventions):
- `npm test` green on node 22 (CI: Windows, node 22, `.github/workflows/ci.yml`), and `npm run smoke` green.
- The guard rails (`resilience.test.js:293-315`) hold: `lib/desk.js` has no `fs`, and `nodes.js` puts no `pin=` in a URL.
- One CHANGELOG entry for 0.5.0 with "Tests: N (was M)", with the version in `package.json` and `server.js:42`, the NODES rows, the
  SHOWBUS copy kept byte-equal with the master, and README.
- No model or tool identifiers.

**Stagewatch** (`Stagewatch/CLAUDE.md`):
- No code and no build in this area. Papers go through spec-1-3's single master edit and its NODES.md rewrite, so the copies change
  once under one hash.
- The interop rows follow the ecosystem lane's rules: a row only when it can pass; "not run" named and never counted; CI requires it.
- The doctrine is honoured by removal: no governor is added ("Measured, then governed"), no wire word is added, and nothing new
  touches the show thread.

**Patterns, Mapper:** untouched, so no round, papers or spine amendment.

**Differences from peers-not-screens' AGMessage units, to reconcile there:**
- its AG-1 counts a context 200 "by its status, whatever the body". Here the read needs a §6 body and `X-RateLimit-Limit`, and is used
  only when the first message is not `auth`;
- here AGMessage takes only `swk_…` keys;
- here a refusal keeps the public frame (as its decision 6 lets it), and "Link again now" exists. A re-saved key does not redial,
  whereas its decision 12 assumes a new key restarts the link.

---

## 9. The review's items, answered

1. **Proposal ids repeat across desk runs (high).** *Accepted.* Confirmed at `Approvals.cs:51-52`, `:101`, `:153-157` and
   `MainViewModel.Desk.cs:15-23`. Runs are keyed `<desk>/<id>@<created>`. Every retry reads the proposal back first. A desk change
   settles what was owed. cross-app decision 5's run tag makes ids unique from its SW-2 on, and §11's words say so.
2. **The server fakes break under the verdict rule (high).** *Accepted, and wider than stated.* `units.test.js:111-114` breaks too. The
   resilience and tls fakes need the header. One shared fake serves all four files, and a no-context fake is a new test.
3. **Retrying 401 feeds the lockout (medium).** *Accepted.* `_call` is the one door: a 401 refuses the link, a 429 waits, and nothing
   keyed leaves during a hold. 401 and 429 are out of the per-result table. A 403 stays in it, because it is never counted
   (`WebServer.cs:1084-1086`) and a new key can still deliver. "This key speaks for another node" is a 409 on results (settled) and a
   403 on collect, where it is now said on the admin page and in the context.
4. **A 200 from a non-key value proves nothing (medium).** *Accepted, with a stronger fix than labelling.* AGMessage takes only
   `swk_…` (Show Bus §7 already says so), and the HTTP verdict needs `X-RateLimit-Limit`. `tls.request` returns headers.
5. **The fallback and the socket verdict can disagree (medium).** *Accepted, without a timer.* The first message on the socket picks the
   evidence path, and any later `auth` wins. In the sibling's revised matrix a binding conflict is `ok:true, verified:"none"`, which
   AGMessage shows as `unbound` with words rather than a flip. Tested: "context 200, then `auth ok:false`".
6. **The write-ahead record is unchecked (medium).** *Accepted.* The `writes` counter and `lastError` are checked, and the line is not
   run without a record. At 50 owed results, intake stops instead of dropping from persistence.
7. **SW-AG-1's 10 s gate and its journal flood (medium).** *Accepted, by removing the Stagewatch rule entirely.* The recovery read of
   the existing queue replaces `redeliver`, with no governor, no word and no journal row. This also makes items 9, 10 and 11 moot.
8. **"Propose release" fails against today's rows (medium).** *Accepted.* Window actions come from the listed forms. Today: show, hold,
   revoke. The test pins that `release` is absent.
9. **SW-AG-1 belongs in DeskService (medium).** *Moot:* no Stagewatch code remains in this area.
10. **The `CollectApproved` signature falls silently to the default member (low).** *Moot*, for the same reason. The finding is right
    (`ShowState.cs:590`), and it is worth keeping in mind for any future change to that interface.
11. **MCP `get_proposal` does not carry `delivery` (low).** *Accepted as wrong* (`McpEndpoint.cs:151-154` takes an int Copilot id). The
    claim is withdrawn with the field.
12. **The interop test would run in the default e2e lane and its skip would count as passed (low).** *Accepted.* The rows move into
    the ecosystem lane's `tests/interop`, which `tools/test.sh interop` runs. "Not run" is never counted there, and CI's `interop` job
    checks AGMessage out at its pin with `SW_INTEROP_REQUIRED=1`.
13. **The context churns on a countdown (low).** *Accepted.* States are steady ("retrying"), and the next attempt moves to the health
    detail (`nextInMs`). A test asserts two builds 1 s apart are equal.
14. **The 400-character limit drifts from 2048 (low).** *Partly rejected.* Today's desk silently cuts at 400 (`Approvals.cs:88`), so 400
    is the only limit safe on every desk. The refusal is now worded neutrally and says why.
15. **Ids with spaces drop cue items (low).** *Accepted.* Ids are capped and stripped, never a reason to drop an item. The test uses
    `cue:front of house`.
16. **Keyless links stop being pinned (low).** *Accepted.* The keyless trust-on-first-frame pin is kept unchanged, in step with the
    sibling. The reason is stated: no secret rides that link, and the pin guards a key added later.
17. **Re-saving does not redial; the hold is lost on restart (low).** *Accepted.* "Link again now" was added, the page says a re-save
    does not redial, and the hold persists in settings.
18. **One GET per followed proposal (low).** *Accepted.* One `?from=` per tick while any is open. A test counts the requests.
19. **The secret fence misses the AI key and over-fences a PIN (low).** *Accepted.* The fence covers `sk-ant-…`, the stored AI key, the
    admin and context tokens and window addresses. No PIN is stored or fenced, and a test proves `nudge 1234` passes.

**The "missing" items**
- **Proposal identity:** decision 3 and §2.3.
- **Interop that runs:** item 12.
- **DeskService:** moot.
- **One 401 door:** item 3.
- **Headers:** item 4.
- **Admin proposals in memory:** persisted (decision 6).
- **A silent 403 on collect:** said (decision 6).
- **Stagewatch's context not told that a target keeps failing:**
  - AGMessage's own context carries `desk:results` (amber, or red when intake stops), which reaches Stagewatch's context, AI and Eye
    as the `peers` row's light and headline (`ShowContext.cs:138`);
  - the desk also says `undelivered` itself.
- **Read-scope keys:** the first context post's 403 (HTTP path) or the verdict's scope (socket path) sets auth `read`, with
  Stagewatch's words.
