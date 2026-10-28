# The Eye — every app, node and device in one view, fed by PeerLinks and the hello

Area: the Eye (Patterns' God's Eye view) as layer 4 of the Show Bus / PeerLink stack. This is design only: no repo was edited or
built. Paths are relative to each repo. Line numbers are from the trees as read on 2026-10-25:
- Patterns at `bcdbf28` (Round 88.1's last commit);
- Stagewatch at `855e8b6`;
- Mapper at `f647094`;
- AGMessage 0.5.0 at `AGMessage's 0.5.0 branch` `7b97cca`, read with `git show`.

**Requester and evidence class** (Patterns' round shape, `Patterns/CLAUDE.md` "How a round opens", item 3):
- The ask is the maintainer's, relayed by the workflow: "Remember that Patterns has the Eye View that knows about all connected apps,
  nodes, and devices. Update Patterns and the ShowBus/PeerLink with the best standards we are developing", and the user chose to
  "change Patterns and PlayMap too, each in its own repo's process".
- It is answered from **a handed-in critique**: the verified gap list (`gaps.json`: G04, G06, G15, G18, G22–G24, G32, G35, G37, G45),
  the eight sibling designs and their critics, the PeerLink profile (`design/peerlink.md`), and a code reading of the four trees.
- **No field files arrived.** `Patterns/docs/field/` holds only its README, so "what the files said" is "none this round".

**Its place in the stack.** The answer already given stands:
- Show Bus is the contract.
- A PeerLink is one authenticated link that carries it (layer 2).
- The Eye is the chief reader at layer 4. It reads layer 1 (the hello), layer 2 (each link's record) and layer 3 (each node's §6
  context).
- It never makes a trust decision and never invents a light (`peerlink.md` "Layer 4 — Views"; profile §13.16.4).

---

## 0. The answer in one paragraph

Today the Eye draws Patterns' own rig well. It draws other apps as a row of grey, amber or red dots, plus the dozen things of the
**one** Stagewatch this desk follows, all stacked in a single column. Six things are needed:
- **One Show Bus id, one node.** Hello, link, device, deck and context all fold into one app node.
- **A link is an edge, never a thing.** It carries the PeerLink record verbatim, with its mark (✓ △ ✕ ○ i) beside its colour.
- **Every node and edge says where it came from:** own, link, told, relayed or heard. Claims are worded as claims.
- **One context rule:** an item is the node's own thing. Another node's thing is named by a link end, `@<id>` or `@<id>/<item>`.
  That one rule lets the Eye draw *Stagewatch window lobby → Patterns FOH Screen 2 → LED wall* and *Stagewatch window stage →
  PlayMap Stage left output 3 → projector P1* from what each app says about itself.
- **A network band of per-app clusters, bounded.** It replaces the single column. Sixteen apps are expanded, the rest are cards, and
  "+N more" names what was left out. The view stays readable and instant with 128 apps.
- **A small publishing duty for each app**, adopted in each repo's own process.

---

## 1. What the Eye reads and draws today (evidence)

### 1.1 The facts and the build
- **Gathering.** `EyeService.Gather` reads every service once a second on the desk's tick
  (`Patterns/src/Patterns.App/Services/EyeService.cs:396-600`; the tick is `MainViewModel.Eye.cs:225-231`).
- **Rebuilding.** The graph is rebuilt only when `JsonUtil.SerializeCompact(facts)` changes (`EyeService.cs:66-70`). That hash
  serializes **every fact**, each other app's whole `ShowContext` included (`EyeFacts.ShowApps`, `Eye.cs:133-134`, `:142-145`). The
  context's `at` is zeroed so a re-sent context does not move the hash (`StagewatchFollow.cs:287-288`).
- **The graph is pure.** `EyeGraph.Build(EyeFacts)` (`Patterns/src/Patterns.Core/Services/Eye.cs:513-861`).
  - **Nodes** have an id, a kind (19 kinds, `:16-41`), a plane (Control, Video, Audio, Room, `:7-13`), a tier, a label, a sub line
    and a `CheckLight`.
  - **Edges** have one of eleven verbs (`:44-57`) and a light. Edges carry no record, no source and no shape.
- **Who sends the facts.** The other apps arrive as `EyeShowApp(Id, KindWords, Name, State, Linked, Words){Context}` (`Eye.cs:142-145`),
  built by `AppServices.ShowApps` (`AppServices.cs:909-919`) and `ShowBusView.Apps` (`StagewatchFollow.cs:206-227`):
  - Kind `patterns` is left out (`:210`). Another desk is drawn from the older beacon as `node:<instance>`
    (`Eye.cs:760-769`; `EyeService.cs:501-503`).
  - Only a `main` (a Stagewatch) can be `Linked`, and only by matching the typed host and port (`StagewatchFollow.cs:212,256-262`).
    So PlayMap and AGMessage are never linked, and their contexts are never read (G35, G37).
  - A typed Stagewatch that sent no hello is still drawn (`:221-225`).
  - The desk's own timer and caller nodes come over the twin as "Patterns node" apps (`:233-239`; `AppServices.cs:917`).
  - A changed certificate forces the Stagewatch to `Gone` with words (`AppServices.cs:911-916`).
  - `KindWords` has no `playmap`, so PlayMap is shown as "Playmap" (`ShowBusNetwork.cs:168-175`).

### 1.2 How another app is drawn (`Eye.cs:771-820`)
- **The app node** sits in CONTROL tier 2.
  - Its light: Gone red, Stale amber, otherwise the context's own light, else green if linked, else grey (`:778-783`).
  - Its sub line is the context's headline (`:785`).
  - Its words are the last 3 `recent` lines and up to 6 `peers` as text (`:790-791`).
- **One edge to the desk:** `Follows` if linked, otherwise `Hears` (`:794-795`).
- **Its things** sit in CONTROL tier 3.
  - Only the first **12** are drawn, reds and ambers first (`:798`).
  - `links` rows are drawn **only when both ends are among those 12 of the same app** (`:812-818`).
  - Every unlinked thing gets a grey `Serves` edge from the app (`:819`).
- **A gone app has its things dropped** (`:796`).
- **The hello is all this desk knows of every heard-only app.** A heard app that goes quiet turns **red** whether or not the show
  needs it (`:780`). The existing test asserts this for a heard-only Slides agent
  (`tests/Patterns.Core.Tests/ShowBusTests.cs:306-325`, `app:66cc66cc` red at `:324`).

### 1.3 The same app drawn twice, and the links nobody draws
- **Decks.** Every connection that says `HELLO` on 9697 becomes a **deck** node, `deck:<name>@<address>`
  (`ControlService.cs:470`; `Eye.cs:681-693`). Two such connections already exist:
  - Stagewatch's playout link says `HELLO Stagewatch` (`Stagewatch/src/Timer.Integrations/PatternsLink.cs:199`);
  - PlayMap's Patterns link says `HELLO PlayMap` (`Mapper/src/PlayMap.Net/Links/WireClient.cs:276`).

  So the Stagewatch this desk follows is drawn **twice**: as `app:<id>` and as `deck:Stagewatch@10.0.0.5`, an amber "connected,
  not paired — its keys do nothing" when unpaired. Nothing ties them together.
- **Link facts.** No link fact reaches an edge except the words "linked", "quiet" or "gone quiet". STATE's `bus.stagewatch` has the
  link's state and clock (`CommandRouter.cs:754-779`), but the edge does not.
- **Links between other apps** (Stagewatch→PlayMap, AGMessage→Stagewatch) appear only as the text "hears X — light: headline" in the
  app node's words (`Eye.cs:791`).
- **The export drops cross-app edges.** `ShowContextBuilder.FromEye` leaves out every node and edge that touches another app's things
  (`ShowContext.cs:176-190`). So Patterns can never tell another app "my Screen 2 shows your window" (G24).

### 1.4 What the other apps publish today (what the Eye could draw)

| app | items | links rows | evidence |
|---|---|---|---|
| Stagewatch (`main`) | `timer`, `schedule`, `cue:<dept>` (department), `message`, every integration as `link:<name>` (`playout` or `link`), `window:<name>` (kind `screen`), `clock:patterns`, `go-latency` | `timer → <every link item>`, `follows` for playout and `hears` otherwise. Windows are linked as `hears` too (G23). No row names a peer | `Stagewatch/src/Timer.Show/ShowContext.cs:47-140` (`:126` the rows); `Timer.App/ViewModels/MainViewModel.ShowBus.cs:53-67`; `Timer.Core/ScreenWindows.cs:325-348` |
| PlayMap (`playmap`) | `air`, `playlist:<id>`, `output:<id>`, `projector:<id>` with its device folded in, `media`, `link:patterns`, `link:stagewatch`, `sync`, `scan`, `fault:*`, ops and module items, `client:<session>` decks | `output → projector` (`drives`); `link:patterns → air` (`controls`); no row for `link:stagewatch` | `Mapper/src/PlayMap.Engine/ContextBuilder.cs:16-127`. It is already **shaped for the Eye's twelve** (`Mapper/src/PlayMap.Core/Fleet/ContextShape.cs:9-21,89-94`; `tests/PlayMap.Engine.Tests/Fleet/EyeContextTests.cs`) |
| AGMessage 0.5.0 (`messenger`) | `meetings`, `prompt:<n>`, `link:stagewatch`, `link:stagewatch:<n>` (one per hall's Stagewatch), `help` (a count), `ai`, `tunnel`. **No displays** | `meetings → prompt / link` (`sends`) | `app/lib/context.js:28-114`; facts at `app/server.js:1673-1684`. Display status is computed (`app/lib/displays.js:165-173`) but never published. Linked Patterns and PlayMap are named only in `peers` (`context.js:83-91`) |
| Patterns (`patterns`, its own export) | the Eye's own nodes | edges between its own nodes | `ShowContext.cs:178-195` |

Two other readers exist and are noted for the record:
- PlayMap draws its own fleet with the same method (`Mapper/src/PlayMap.Core/Fleet/FleetGraph.cs:44-55`; verbs `FLEET FOCUS…`, alias
  `EYE FOCUS`, `VerbCatalog.cs:315-319`).
- Stagewatch's "Show network" rows read light, headline and problems per peer (`MainViewModel.ShowBus.cs:89-109`).

### 1.5 Faults found (each answered in §2)

| # | fault | evidence |
|---|---|---|
| F1 | **Every other app's things stack in one column.** All `ShowAppItem`s go in CONTROL tier 3 (`Eye.cs:805`), and a cell is a vertical stack of `NodeH` 52 + `RowGap` 16 (`EyeLayout.cs:83-89,120-141`). Labels are drawn only at scale ≥ 0.35 and sub lines only at ≥ 0.6 (`EyeCanvas.cs:217,222`). Fit is `(H − 80) / height` (`EyeCamera.cs:50-55`), floored at 0.12 (`:15`). Worked out for a modest room — Stagewatch 12 + PlayMap 12 + AGMessage 12 + three timer nodes × 4 = 48 things — the column is 48 × 68 − 16 + 56 = **3,304** world units. The fit view then shows **no label on any canvas under 1,236 px tall**. At 128 apps (spec-1-3's peer cap) the column is ~104,000 units: the camera hits its 0.12 floor and most of the picture is off screen. | computed from the constants cited |
| F2 | One app is drawn twice (`app:` and `deck:`). PlayMap and AGMessage are never linked. Another desk's Show Bus hello is ignored, though its `inst` is the beacon's (`Patterns/src/Patterns.Devices/BeaconService.cs:89-90`) | §1.1, §1.3 |
| F3 | Every heard app that goes quiet is red, needed or not, contrary to the profile's "red only when the show needs it" (13.16.1–2) | `Eye.cs:780`, `:763` |
| F4 | Edges carry no link record, mark or source. The only links drawn are within one app. Nothing is drawn one hop out | §1.2–1.3 |
| F5 | The per-second hash serializes every context. The cap is 512 KB per context (`ShowContext.cs:88`), 64 KiB once spec-1-3 lands. That is up to 8 MiB a second on the desk's thread at 128 apps; about 30 KB today. No timing was taken this round | `EyeService.cs:66` |
| F6 | The focus walk (`Hops`, a BFS) runs on every render and every animation tick | `EyeCanvas.cs:168,306` |
| F7 | Lights are colour only. There is no ✓ △ ✕ ○ i shape, which the profile requires for every link light (13.16.1) | `EyeCanvas.cs:205-216` |
| F8 | Stagewatch links its timer to every link item and every window as `hears`. No link row names the peer. Window items do not say who holds them (G23, G24) | `Stagewatch/src/Timer.Show/ShowContext.cs:126` |
| F9 | PlayMap's `link:stagewatch` has no row, and its timer and window sources name no Stagewatch item | `ContextBuilder.cs:96-100` |
| F10 | AGMessage publishes no displays. Its Patterns and PlayMap links are only `peers` rows. It drops PlayMap's hello (G18). Its comment says Patterns' Eye reads its context, which is false (G37; `context.js:3`) | §1.4 |

---

## 2. Decisions

**D1. The Eye is layer 4 and decides nothing about trust.**
- It shows lights that someone published:
  - the desk's own facts;
  - a link's record (13.16.3);
  - an acceptor's list (13.16.5);
  - a node's own context;
  - a hello's liveness.
- It only chooses **where** each light is drawn.
- It never pins, never lowers a floor, never merges two ids, never colours a node from a hello other than grey, and never makes an
  edge greener than the record that feeds it.
- *Why:* the doctrine (attempts are not facts; fail closed) and the profile's layer-4 rule.
- *Alternative rejected:* deriving the link's light from the edge's words or from the hello. That is two interpreters of one fact,
  which the synthesis dropped.

**D2. One Show Bus id, one node.**
- An app is keyed by its §2 `id`. These all fold into the **one** `app:<id>` node:
  - every hello for that id (spec-1-3: every address per id, the sticky dial, the conflict mark);
  - every link this desk holds with it or accepts from it;
  - its context, whether read, posted or relayed by the twin;
  - every mention of it in other contexts;
  - a Show Bus node device pointed at it.
- **The fold needs evidence of identity**, at one of three levels:
  - **Proved (level 1).** A LINK verdict names the id (13.6), so the connection's deck or device folds into `app:<id>`.
  - **Level 0, as a connector.** This desk dialled the address. The device folds into the app whose hello names that address and port
    *and* whose context claims the same id. Its edge carries `legacy`, with the profile's words "<node> cannot prove itself yet —
    anything at its address could answer" (13.6.8). If the hello and the context disagree, there is no fold, and a note says so.
  - **Level 0, as an acceptor.** An accepted connection proves nothing about who dialled. The deck stays a deck. Its words state
    both observations without joining them: "says it is Stagewatch — from 10.0.0.5, where Stagewatch Hall A's hello comes from; not
    proved (PeerLink not run)".
- Another Patterns desk's Show Bus hello folds into its beacon card `node:<instance>` by `inst`. This keeps the existing ids.
- An id in §2 conflict is one node, amber, words "two machines answer as <id> (inst a41f…, b7c3…)".
- *Why:* F2. Also "an address is where to dial, never who" (13.4.1).
- *Alternative rejected:* folding a level-0 deck by HELLO name plus source address. That guesses identity from an address.

**D3. A link is an edge, never a thing.**
- These are drawn as **one edge between the two nodes**, never as nodes:
  - a link this desk holds or accepts;
  - any context item of kind `link` that has a row to `@<peer id>` (P2 in §5).
- The edge carries the 13.16.3 record (or the 13.16.5 acceptor row) verbatim: light, mark, words, flags, clock and counts.
- Every other row that touched that link item is **re-anchored** to the far node. Two examples:
  - PlayMap's `link:patterns -controls-> air` becomes `desk -controls-> PlayMap's air`: this desk's blackout controls that air, which
    PlayMap already says (`ContextBuilder.cs:94`).
  - Stagewatch's `timer -follows-> link:patterns` becomes `Stagewatch timer -follows-> desk`.
- **When several sources describe one link**, the edge is drawn once:
  - this desk's own record leads, then the connector's item, then the acceptor's item, then a relayed `peers` row;
  - the others' words follow it ("Stagewatch Hall A says: live, key 52f6…");
  - when two sources disagree on the state, the edge takes the worse light and shows both words.
- *Why:* F1, F4. A link drawn as a node doubles the picture and hides the topology.

**D4. Every node and edge carries its source, and a claim is worded as a claim.**
- `EyeSource` is one of five values:

  | source | meaning |
  |---|---|
  | `own` | this desk's own facts |
  | `link` | a record of a PeerLink this desk holds or accepts |
  | `told` | a node's own context, about itself |
  | `relayed` | a node's context about a third node: a `peers` row, a link row to someone else, or a cross-node end |
  | `heard` | the hello only |

- **Drawing:**
  - `own`, `link` and within-app `told` edges are solid;
  - `relayed` edges and cross-app `told` edges are dashed, and their words begin "<app> says:";
  - a `heard` node has a hollow dot.
- The assistant's lines carry the same words.
- *Why:* the doctrine. A claim from PlayMap about Stagewatch's window is PlayMap's claim (windows-for-peers §2.9: "a link whose `from`
  is another node's item is that node's claim, never a fact about it").

**D5. Lights follow the profile, not the hello.**
- **Heard only:**
  - grey ○ "heard on the show network";
  - quiet: grey "quiet for N s", or amber △ if the show needs it;
  - gone: grey "not heard for N s", or red ✕ if needed;
  - forgotten after 60 s unless needed (`ShowBusNetwork.cs:25`).
- **"Needed"** follows 13.1. For this desk, an app is needed if any of these holds:
  - its link is configured to be followed (the Stagewatch link on, or Follow stage on);
  - it is an enabled Show Bus node device;
  - it holds a window grant drawn on this desk's air.
- **An accepted link is never red here.** The acceptor cannot redial, so its gone row is grey.
- **Link edges:** the record's light and mark, verbatim (13.16.1–2: the worse of the state's and the flags', red only when the show
  needs the link).
- **The app card:**
  - its context's own light while its link is live and its show is live;
  - if its **show** is stuck (record `show:"stuck"`), the worse of that light and amber △, with words "says its show is stuck — no time
    shown". The link edge stays green (13.10.5–6);
  - if the link is stale, amber △ "quiet for N s", with its things kept but their words marked "(N s ago)";
  - if the link is down, held or refused, the edge's light, and its things are not drawn (nothing current).
- **Counting things** (kinds `timer`, `schedule`, `playlist`, `chase`, `timecode`, `clock`) from an app whose show is stuck or whose
  link is not live:
  - state "? — <reason>";
  - light: the worse of their own light and amber, red if the show needs the link (13.10.6; profile table 13.16.1 "every value from it
    ✕ no time shown").
- **A held safety fact** shown in a context ("holding Patterns' blackout since 20:14:03 — link lost") is drawn as told (13.17).
- **A node known only from a context's `peers`** is grey "heard, not linked" (13.16.4). It never takes the relaying app's colour for
  it, though the words quote it.
- *Why:* F3, and alarm fatigue, which the profile names as a migration risk.

**D6. Cross-node ends.**
- **The two forms:**
  - `@<node id>/<item id>` names another node's item (windows-for-peers §2.9, handed to spec-1-3's single edit);
  - **`@<node id>` alone names the node**, which this design adds.
- **What a reader does with an end:**
  - it validates the end (≤ `context.link.end.chars` 120; id `[A-Za-z0-9_-]{6,40}`; item ≤ `context.item.id.chars` 60);
  - `@<own id>` resolves to the desk, and `@<own id>/<item>` to this desk's node with that id. The item ids Patterns exports are its
    Eye node ids;
  - an end resolves to the item if it is drawn, otherwise to the app's card, with the item named in the edge's words;
  - an end naming an app that is not on the Eye is ignored and counted (`unplaced`).
- **A `serves @<holder>` grant row is not drawn** when a more specific `feeds` edge from the same item into that holder's things exists.
- *Why:* G24. This is the whole window chain, with no new message type.

**D7. A NETWORK band of per-app clusters.**
- **The band and its order.** A new `EyePlane.Network` band sits above CONTROL. In it, each app is a **cluster**: its card, then its
  drawn things in a 3-column grid, row by row, in the order that the kind table (§5.3) and then the context give them.
- **Lens and layout are separate:**
  - a thing keeps its kind's plane for the lens, so the VIDEO lens shows PlayMap's outputs and projectors, Stagewatch's windows and
    AGMessage's displays;
  - layout keeps each thing inside its app's cluster;
  - an app's card is visible on a lens when any of its things is.
- **A new lens `NETWORK`** (`EYE LENS NETWORK`) shows every app card, every link edge and the desk.
- **Clusters per row** = ⌈√(expanded)⌉. Collapsed cards go in a compact strip below them, in columns.
- **Slots are sticky for the session.** A new app takes the first free slot, and a slot is freed when its app is forgotten. The same
  history of facts always draws the same picture. A cluster that expands or collapses moves once, never its neighbours.
- **The focus walk** never continues through an app card unless the focus is that card. This is the desk's existing hub rule
  (`Eye.cs:368`) applied per cluster.
- *Alternatives rejected:*
  - placing PlayMap's things in the VIDEO band by plane (patterns-playmap-side 92.1). Ownership would vanish, and Patterns' own bands
    would move whenever a peer's context changed;
  - a force layout (EYE.md §3 rejected it already).

**D8. Bounds** (constants in Core, beside the Eye):

| bound | value | why |
|---|---|---|
| apps on the Eye | ≤ 128 | spec-1-3's hello reader keeps 128 peers. Beyond that, one grey node "+N more apps heard" |
| expanded clusters | ≤ 16 | readability (§8). The rest are collapsed cards |
| things drawn per expanded app | the first 12, reds and ambers first (unchanged), **plus at most 8 forced** | PlayMap's `ContextShape.Window = 12` depends on the 12. Forced things are chain ends: an item that has a cross-node row resolving to something drawn, or an item one of this desk's sources holds (windows-for-peers' "≤ 8") |
| relayed edges | ≤ 256 | — |
| cross-app `told` edges | ≤ 256 | — |
| mention-only nodes | ≤ 32 per context (`context.peers`), ≤ 128 in all | — |

- **Expand order:** needed apps, then apps with a red or amber, then linked apps, then the focus's neighbourhood, then by kind and
  name. Focusing a collapsed card expands it (a view change, not a verb).
- **What a collapsed card shows:** "PlayMap Stage left — 31 things · 1 amber: Out 7 · not open".
- **What is left out is said:** EYE JSON `left`, STATE `eye.network`, and a grey line on the NETWORK band.
- *Why:* F1. The geometry is computed in §8, so the cause is measured by arithmetic, not by a timer.

**D9. Remove the per-second cost; add no governor.**
- **The hash.** Each context is kept with a **revision**, bumped on the receiving thread only when its text with `at:0` changes. The
  Eye's hash covers ids, revisions and record versions, never contexts. A context arrives at most once a second per app (spec-1-3
  posting).
- **The walk.** `Hops(focus)` is cached by (`Rev`, focus).
- **Culling.** Edges whose segment misses the view rectangle are skipped.
- **Measurement.** The rebuild's duration and node count are recorded in `patterns.metrics.csv` for a later round. There is **no
  measurement this round** and no time-based fence.
- *Why:* "removing the cost beats governing it" (doctrine).

**D10. The feeds** (§4) and what is refused.
- **Feeds, in order:**
  1. links this desk holds;
  2. links this desk accepts;
  3. contexts read or **posted** over a verified link;
  4. the twin;
  5. one hop from contexts held;
  6. the hello.
- **Posting.** A new line form `CONTEXT POST <json>` lets a verified connector with scope ≥ `post` (13.5.3) give its context to a line
  acceptor. This is the line twin of Stagewatch's `POST /api/peers/context`.
  - The context's `node.id` must equal the link's verified id. Otherwise: `ERR @id this key speaks for <kind> <id>`, never counted
    (13.5.2; G15).
  - At level 0 it is refused: `ERR … posting a context needs PeerLink — this connection did not prove its id`.
  - The latest context wins and is kept for `context.kept.ms` (30 s).
  - The line counts against `link.command.bytes` (64 KiB), so a sender trims by §6's order until the line fits.
- **Rejected:**
  - Patterns polling AGMessage's `/api/context` with an `agn_` token (G37's first option). HTTP polling is a reader, not a PeerLink
    (13.3.6); Patterns would hold another app's secret; and over plain HTTP it would send a key in clear (13.6.9).
  - Stagewatch relaying whole peer contexts. That is a second hop of trust, and §6 keeps one hop (`peers` rows: "never its peers").
- **Until AGMessage posts** (AG-PL3), AGMessage reaches this Eye only by its hello and by Stagewatch's `peers` and link rows, and the
  papers say so.

**D11. Patterns' export says only what it knows.** `FromEye` exports:
- its own nodes (as today);
- one `link:<peer id>` item per link it holds or accepts, with the P2 row;
- every `own` edge between its own node and another app's item, its far end written `@<app id>/<item id>` (windows-for-peers PT-W3).

It **never** re-exports another app's things, a `told` or `relayed` edge, a mention, or a record field that the 13.16.3 record itself
excludes (key, code, K, S, proof, nonce).

**D12. Marks beside colour.**
- Every node and edge carries a mark: ✓ green, △ amber, ✕ red, ○ grey, and **i** for information, which is grey with an `i`.
- A link's mark is the record's own (13.16.1–2). The flags `first`, `note`, `unfit`, `malformed` and a local `plain` are **i**.
- Nodes derive their mark from their light.
- The mark is drawn in the dot and at an edge's midpoint (scale ≥ 0.35), and in the JSON, STATE and the brief.

**D13. The menus stay one.**
- One builder, `NetworkMenus.For(app, links)`, serves:
  - the Eye's app card;
  - an edge (now hit-testable on click and right-click);
  - the Nodes page's card;
  - the deck's Nodes bank.
- Its LINK group lists, per link, the desk's existing trust verbs, each showing its wire line and run through the action layer:
  - *Link again now* (`STAGEWATCH LINK AGAIN`, or the device's `DEVICE <name> LINK AGAIN`);
  - *Accept certificate* (`STAGEWATCH ACCEPT <8 hex>`);
  - *Lower floor* (`STAGEWATCH FLOOR LOWER`);
  - *Accept new id*;
  - on an accepted link, *Revoke key* (`KEY REVOKE <kid>`) and *Let this key speak for…* (`KEY BIND`).

  These verbs are Round 90's and Round PL's. The Eye adds none.
- An entry that does not apply is shown with its reason ("Because"), as every desk menu does.
- The assistant's facts list these verbs as **a person's** and never propose them (13.16.6; patterns-playmap-side D16).

**D14. Each app publishes for the Eye** (P1–P8, §5). This is normative text handed to the master's §6 and adopted unilaterally by
each app. No app waits for another to publish: an app that has not adopted it is drawn as today, a card with its own things, and its
rows are simply not resolved.

**D15. Process.** Each repo adopts in its own process (§11):
- **Patterns:** rounds, numbered by the maintainer; this design calls its round **Round EYE**.
- **Mapper:** `Engine/ContextBuilder.cs` is a **spine file** (`Mapper/docs/round3/PLAN.md:307-311`), so it goes through the spine
  amendment lane (§5.7). `Net/Links/**` rides PeerLink's PM-X exception request.
- **AGMessage:** the 0.5.x branch.
- **Stagewatch:** its train with SW-PL3 and windows-for-peers SW-W1.
- **The master text:** spec-1-3's single edit if S1 is still open, otherwise Show Bus 1.4.

**D16. What is left out of this round.**
- The replay (round 84) relighting link edges from the journal's link rows.
- A memory for the problems queue (OPEN L44).
- NDI chains across apps, which are known by source name, not by id.
- Each is a row in §13.

---

## 3. The model

### 3.1 Core types (Patterns.Core, no package references)

```csharp
// Eye.cs — EyeFacts gains Apps (EyeShowApp and ShowApps are replaced, no legacy); EyePlane gains Network.
public sealed record EyeApp(string Id, string Kind, string Name)          // one Show Bus id
{
    public EyeHeard? Heard { get; init; }               // layer 1: state live/stale/gone, ageMs, addresses, ports, caps, tls hint, inst, conflict
    public bool Needed { get; init; }                   // 13.1 for this desk (D5)
    public IReadOnlyList<EyeLinkFact> Links { get; init; } = [];   // layer 2: records this desk holds or accepts with this id
    public ShowContext? Context { get; init; }          // layer 3: its own (at = 0)
    public string ContextVia { get; init; } = "";       // "read" | "posted" | "twin" | ""
    public long ContextRev { get; init; }               // D9: hashed instead of the context
    public long ContextAgeMs { get; init; }
    public string DeviceId { get; init; } = "";         // a Show Bus node device folded in (D2)
    public string Note { get; init; } = "";             // a fold refused, a conflict, an id another than meant
}
public sealed record EyeLinkFact(string Role /* connector | acceptor */, string Use /* follows | controls | sends */,
                                 LinkRecord Record);    // LinkRecord: Round 90's PeerLink core type (13.16.3), never a key, code, S, proof or nonce
public enum EyeSource { Own, Link, Told, Relayed, Heard }

// EyeNode and EyeEdge gain init-only fields, appended:
//   EyeNode: App ("" for this desk's things), Source, Mark ("✓","△","✕","○","i"), Collapsed
//   EyeEdge: Source, Mark, Claim ("" or "Stagewatch Hall A"), Link (EyeLinkFact? for link edges)
```

`EyeNetwork.cs` (new, pure) holds the parts of `Build` that belong to the network:
- `Fold` (D2);
- `Resolve(end)` (D6);
- `ReAnchor` (D3);
- `Expand(apps, focus)` (D8);
- `KindPlane(kind)` and `KindColumn(kind)` (§5.3);
- the counting-kind set (D5).

`EyeLayout.Place(graph, slots)` takes the sticky slot map, which `EyeService` keeps.

### 3.2 The node ids

| thing | id | label |
|---|---|---|
| another app | `app:<id>` (another Patterns desk: `node:<instance>`, as today) | "Stagewatch Hall A", "PlayMap Stage left", "AGMessage" |
| its thing | `app:<id>:<item id>` (unchanged) | the item's label |
| a mention-only node | `app:<id>`, source `relayed` | "PlayMap Stage left — heard by Stagewatch Hall A" |
| "+N more" | `apps:more` | "+12 more apps heard" |
| this desk's things | unchanged | — |

### 3.3 Worked example — who shows whose window on which screen

The room:
- **Stagewatch Hall A** (`3f9c2a1b7d0e`) has windows `lobby` and `stage`.
  - `lobby` is granted to **Patterns FOH** (`9f8e7d6c`, this desk) and is on Screen 2, the LED wall.
  - `stage` is granted to **PlayMap Stage left** (`0a1b2c3d`), drawn on output 3, which drives projector P1.
- **AGMessage** (`77aa77aa`) has display "Foyer TV", showing Hall A's countdown.

What this desk's Eye draws:

```
desk ──follows ✓── app:3f9c…            (link: this desk's ws connector record — "pinned 03e9 b152 · key 52f6… · scope post · PeerLink 1 · +12 ms ±3")
app:3f9c… ──controls ✓── desk           (link: 9697's acceptor row for Stagewatch's playout link — scope drive)
desk ──controls △── app:0a1b…           (link: the node device's record — legacy: "PlayMap Stage left cannot prove itself yet …")
app:0a1b… ──follows ✓── desk            (link: 9697's acceptor row for PlayMap's Patterns link)
app:0a1b… ┄┄follows ✓┄┄ app:3f9c…       (told by PlayMap's link:stagewatch row; then "Stagewatch Hall A says: verified key" from its peers row)
app:77aa… ┄┄follows ✓┄┄ app:3f9c…       (relayed: Stagewatch's peers row link:"ws" verified:"key" — until AGMessage posts to this desk)

app:3f9c…:timer ──feeds── app:3f9c…:window:lobby                 (told by Stagewatch, while lobby shows a page)
app:3f9c…:window:lobby ──feeds── source:web:stagewatch://3f9c…/w1  (own: this desk mounted the grant) ──feeds── screen:2 ──drives── display:LED ──carries── farend:Processor
app:3f9c…:timer ──feeds── app:3f9c…:window:stage
app:3f9c…:window:stage ┄┄feeds┄┄ app:0a1b…:output:op3 ──drives── app:0a1b…:projector:pj1     ("PlayMap Stage left says"; drives is PlayMap's own row)
app:3f9c…:timer ┄┄feeds┄┄ app:77aa…:display:d7k2              ("AGMessage says" — drawn once AGMessage's context reaches this desk)
desk ──controls── app:0a1b…:air                              (re-anchored from PlayMap's link:patterns -controls-> air: this desk's blackout drives that air)
```

The same chain reaches every reader in words. In the brief:
- "Stagewatch Hall A window lobby → this desk's Screen 2 (LED wall) — shown";
- "Stagewatch Hall A window stage → PlayMap Stage left Out 3 → projector P1 (PlayMap says)".

---

## 4. The feeds: what each layer gives the Eye

| feed | layer | carrier | what the Eye gets | source | light |
|---|---|---|---|---|---|
| the hello (UDP 9928), `ShowBusPeers` | 1 | broadcast; untrusted | that an app exists (id, kind, name, host, doors, caps, `tls` hint, `inst`), and its liveness | heard | grey ○, quiet grey or amber, gone grey or red (D5). **Never green**, never a pin, never a fold across ids. A hello that contradicts a pin is the record's `note` (i), drawn on the edge |
| links this desk holds (connector) | 2 | `ws` to Stagewatch (`StagewatchLinkService`); `line` to PlayMap (the Show Bus node device, Round 92.1) | the 13.16.3 record | link | the record's light and mark |
| links this desk accepts | 2 | `line` 9697 (Round PL), HTTP side calls | each verified peer's row (13.16.5): id, kind, carrier, level, TLS, kid or key name, scope, since, seen in clear; refusals with counts | link | ✓ verified; △ legacy (AUTH-PROOF at level 0) or seen in clear; never red (D5). Refusals are words on the app card ("key 52f6… refused (revoked) ×3 this minute") |
| a context read over a held link | 3 | `ws` side call `GET /api/context` every 5 s (`StagewatchLinkService`); `line` `SUBSCRIBE CONTEXT` (node device) | the peer's items, rows, peers, headline, light | told | the context's own |
| a context posted to this desk | 3 | `line` `CONTEXT POST` (D10) | the same | told | the same. `node.id` must equal the verified id |
| the twin | exemption 13.18 | twin carrier | this desk's own timer and caller nodes (`CONTEXT` over the twin) | own | as today (`StagewatchFollow.cs:233-239`) |
| one hop | 3 | inside any context held | `peers` rows (light, headline, and peers-not-screens' `link` and `verified`); `link` items with `@<peer>` rows; `@<id>/<item>` rows | relayed | mention-only nodes grey; dashed edges in the item's light, worded "X says" |

**Order of precedence for one fact:** own, then link, then told, then relayed, then heard (D3, D4).

**Freshness.**
- A context older than `context.kept.ms` (30 s) is dropped.
- A link's things follow D5.
- A posted context from a link that went down is kept no longer than the link's `down` plus 30 s, and is drawn as in D5 meanwhile.

---

## 5. What each app publishes (P1–P8) — normative, handed to the master's §6

### 5.1 The rules
1. **P1 — items are the node's own.** A context's items are the node's own things: its parts, its links and its health. A thing of
   another node is never an item. It is named by a link end (`@<id>/<item>`, `@<id>`) or by a `peers` row.
   - *This answers ecosystem-lane's critic:* a peer's amber mirrored into Stagewatch's items or headline made every reader show
     Stagewatch as faulted.
2. **P2 — every PeerLink is one `link` item and one row.**
   - **The item.** For every PeerLink a node holds (connector) or accepts and verified (acceptor), it publishes one item of kind
     `link`, with the light and state words of its 13.16.3 record (connector) or 13.16.5 row (acceptor).
   - **The row** runs from the connector's end to the acceptor's end:
     - as connector: `{"from":"<link item>","to":"@<peer id>","kind":K}`;
     - as acceptor: `{"from":"@<peer id>","to":"<link item>","kind":K}`.
   - **K by the link's use:** `follows` for scope follow, `sends` for post, `controls` for drive and operate.
   - **A link not yet proved** (level 0, keyless) names the id its hello or answer gave, and its words say "not proved".
   - This is 13.16.4's "a `links` row to the peer node", with its shape fixed.
3. **P3 — a thing from another node is a claim the shower makes.** When a thing a node shows comes from another node (a window it
   draws, a timer it shows, a page it mounts), it publishes `{"from":"@<id>/<item>","to":"<own item>","kind":"feeds"}`.
4. **P4 — grants.** A node that grants a thing to another publishes `{"from":"<its item>","to":"@<holder id>","kind":"serves"}` per
   holder:
   - light green while the holder reports it shown;
   - amber while it is opening;
   - grey once withdrawn.
5. **P5 — show liveness.** A node whose show writer is stuck says so on its counting items, red. Stagewatch does this today
   (`ShowContext.cs:54-59`). The others do it once measured (13.10.5).
6. **P6 — kinds.** A node uses the kind words of §5.3 where one fits. Any other word is drawn as a generic thing in column 1.
7. **P7 — order.** Of up to 200 items, the node lists reds and ambers first, then its own choice. Items that carry a cross-node row
   are listed among its first twelve. PlayMap's `ContextShape` already orders for the Eye's twelve.
8. **P8 — content.** No secret and no private content: no message text, no people's names, no room codes and no capability URLs
   (§1 rule 5 with windows-for-peers' named exemption; 13.19).

### 5.2 Per app — what each must publish for the Eye to draw it

| app | already published (kept) | to add | its process |
|---|---|---|---|
| **Stagewatch** | `timer`, `schedule`, `cue:<dept>`, `message`, `window:<name>`, `link:<name>`, `clock:patterns`, `go-latency` (§1.4) | **Rows:** drop `timer → <every link>` (G23). Add `timer -feeds-> window:<name>` only while it shows a page (windows-for-peers D11). Add `window:<name> -serves-> @<holder id>` per grant (P4). Each PeerLink item gets its `@<peer id>` row and its record words (P2): `link:patterns -controls-> @<patterns id>` for the playout link, and one per PlayMap playout link (playout-links). `/ws` node links stay in `peers` rows with `link:"ws"` and `verified` (peers-not-screens 2.3), which the Eye reads as acceptor-side edges. **Items:** `clock:<peer id>` per link with `errMs` (not only for Patterns). A window held by a plain browser says where in its state words ("showing /clock · a browser at 10.0.0.31"). | its train: SW-EYE after SW-PL3 and SW-W1 |
| **PlayMap** | `air`, `output:*`, `projector:*` with devices folded, `playlist:*`, `media`, `sync`, `link:patterns`, `link:stagewatch`, `client:*`, `fault:*`, `ContextShape` for the Eye's twelve | **Rows:** `link:patterns -follows-> @<patterns id>`. `link:stagewatch -follows-> @<sw id>` (it reads frames). With `post`, add `link:stagewatch -sends-> @<sw id>` too. `@<sw id>/timer -feeds-> output:<id>` when an output or surface shows `timer:`. `@<sw id>/window:<name> -feeds-> output:<id>` when it draws a granted window (`window-draw`). `playlist:<id> -feeds-> output:<id>` (which playlist is on which output; `ShowingWords` already knows it). Keep `link:patterns -controls-> air`. **Items:** `air` says the held blackout with its age when its link is lost (13.17). **Order:** `ContextShape` keeps items with a cross-node row in the first twelve. **Posting:** `CONTEXT POST` to Patterns over its Patterns link (scope post, PM-PL3) | `ContextBuilder.cs` is a spine file: one "Spine: …" commit in the amendment lane (round 3 §5.7). `Net/Links/**` rides PM-X (PeerLink's exception request) or the integration pass. Fixtures regenerated (`PLAYMAP_WRITE_FIXTURES=1`) |
| **AGMessage** | `meetings`, `prompt:<n>`, `help` (a count), `link:stagewatch`, `link:stagewatch:<n>`, `ai`, `tunnel` | **Items:** `display:<id>` (kind `screen`), labelled with the display's name (≤ 60), from `Displays.status`. Its state uses **kind words only**: "on screen: the countdown", "sending", "could not show it", "offline — no screen has this link open". Never message text or file names: `describe()` quotes text (`displays.js:58-70`) and is not used here. Light: green when shown, amber when sending or in error, amber when offline after having been shown, grey before it was ever opened. One `link:patterns:<id>` or `link:playmap:<id>` item per `peerLinks` entry. **Rows:** `link:stagewatch[:n] -follows-> @<sw id>`. `@<sw id>/timer -feeds-> display:<id>` while it shows that hall's countdown. Each peer link's row, `follows` while a reader and `sends` once it posts. **Also:** accept the `playmap` hello (G18); use the node id as `cid` (G32); fix `context.js:3`'s claim (G37). **Posting:** `CONTEXT POST` to Patterns and PlayMap over AG-PL3 | the 0.5.x branch: AG-EYE1 now, AG-EYE2 after AG-PL3 |
| **Patterns (its export)** | its Eye's own nodes and edges | **Items:** `link:<peer id>` per held and accepted link (P2). **Rows:** own cross-app edges with `@` far ends (D11). **Posting:** `CONTEXT POST` accepted on 9697 (D10) | Round EYE |

### 5.3 The kind table (informative; where the Eye places a thing)

| kind words | plane, for the lens | column in its cluster |
|---|---|---|
| `link`, `deck`, `client`, `desk`, `node`, `twin`, `assistant`, `ai`, `approvals`, `health`, `machine`, `preflight`, `fault`, `clock`, `sync`, `mirror`, `fleet`, `decode`, `optimise`, `scan`, `calibration`, `align`, `site`, `survey` | control | 0 — links and health |
| `timer`, `schedule`, `department`, `cue`, `message`, `stack`, `playlist`, `playlists`, `chase`, `timecode`, `show` | control | 1 — the show |
| `air`, `media`, `content`, `input`, `videoio` | video | 1 |
| `screen` (Stagewatch windows, AGMessage displays), `output`, `outputs`, `send`, `stage` | video | 2 — screens |
| `projector`, `surface`, `led` | video | 3 — far ends |
| `room`, `meetings`, `prompt`, `help`, `phone`, `audience` | room | 1 |
| `audio` | audio | 1 |
| anything else | control | 1 |

---

## 6. Wire, STATE and JSON (exact shapes; new fields at the end of their rows)

**`EYE` / `EYE STATUS`** (`EyeJson.cs:9-48`) appends fields:

```json
{"headline":"…","worst":"…","worstLight":"amber","worstWords":"…","counts":{…},"focus":"","lens":"all","problems":[…],
 "nodes":[{"id":"app:0a1b2c3d:output:op3","kind":"showappitem","plane":"video","tier":2,"label":"Out 3","sub":"60 fps · Stagewatch window stage",
           "light":"green","x":…,"y":…,"w":200,"h":52,"words":[…],"page":"Nodes","item":"0a1b2c3d","wire":"EYE FOCUS app:0a1b2c3d:output:op3",
           "app":"0a1b2c3d","source":"told","mark":"✓","collapsed":false}],
 "edges":[{"from":"desk","to":"app:3f9c2a1b7d0e","kind":"follows","light":"green","words":"Hall A · pinned 03e9 b152 · key 52f6… · scope post · PeerLink 1 · +12 ms ±3",
           "source":"link","mark":"✓","claim":"","link":{…the 13.16.3 record…}},
          {"from":"app:3f9c2a1b7d0e:window:stage","to":"app:0a1b2c3d:output:op3","kind":"feeds","light":"green","words":"PlayMap Stage left says: drawing it",
           "source":"told","mark":"✓","claim":"PlayMap Stage left","link":null}],
 "replay":false,"replayAt":"","moment":"",
 "apps":4,"links":{"green":4,"amber":1,"red":0,"grey":0},"left":{"apps":0,"items":19,"edges":0},"unplaced":0}
```

**`EYE LENS NETWORK`** joins the lens words: `network`, `apps` and `links` in `EyeGraph.ParseLens` (`Eye.cs:410-419`). Like every
EYE verb it is desk-only and moves the view, not the show (EYE.md §3).

**`EYE FOCUS <words>`** resolves `app:<id>`, an app's name ("PlayMap Stage left") and its things' labels, using today's `Resolve` order
(`Eye.cs:426-445`).

**STATE**
- `eye` (`EyeService.cs:141-157`) appends `"apps":4,"linksRed":0,"linksAmber":1,"network":"4 apps · 5 links · 1 amber: PlayMap Stage left — legacy, plain"`.
- `bus.apps[]` rows (`CommandRouter.cs:754-768`) append `"mark":"△","link":"live"` (the record's state, or "").

**`CONTEXT POST <json>`** on 9697 (after Round PL):

```
→ @c7 CONTEXT POST {"v":1,"node":{"t":"messenger","id":"77aa77aa","name":"AGMessage"},…}
← OK @c7 kept                      (scope ≥ post, id = the link's verified id, ≤ 64 KiB line)
← ERR @c7 this key speaks for messenger 77aa77aa — not for 5e6f7a8b       (never counted)
← ERR @c7 posting a context needs PeerLink — this connection did not prove its id
← ERR @c7 not a context — {"v","node":{"t","id"}} is the least it carries
```

It is a catalogued verb with risk `low`. It is not in Patterns' own `commands` list: it is a node's act, never an assistant's.

**Companion module**
- variables `eye_network` (the STATE line) and `eye_links_worst`;
- feedback `eye_links` ("the worst link light is …");
- the `eye_lens` action gains `network`;
- an EYE page preset "NETWORK";
- `lines.txt` gains `EYE LENS NETWORK`.

---

## 7. Every reader, in the same round

| reader | what changes |
|---|---|
| the desk (Eye page) | The canvas (`EyeCanvas.cs`): the NETWORK band with clusters and collapsed cards; marks in dots and at edge midpoints; dashed `relayed` and cross-app `told` edges; hollow `heard` dots; edges hit-tested on click and right-click (distance to the segment ≤ 6 px, after nodes); the walk cached; edges culled. The side card (`MainViewModel.Eye.cs`): a LINKS list on an app (each link's record words, flags and clock); a contact row says its source ("← follows · Stagewatch Hall A says"); the NETWORK lens chip |
| the rail | unchanged: the worst light and the headline. The headline counts things; links are counted in `eye.network` |
| right-click menus | `NetworkMenus.For` on an app card, an edge, the Nodes page card and the deck bank (D13). The menus stay one: every entry's wire line is the button's and Companion's |
| STATE | `eye` and `bus.apps[]` appended (§6) |
| the wire | `EYE` JSON appended; `EYE LENS NETWORK`; `EYE FOCUS app:<id>`; `CONTEXT POST` |
| the deck | §6 variables, feedback, preset and lens choice |
| the assistant | `EyeGraph.Lines()` gains the source words ("Stagewatch Hall A says: …", "heard only", "link: … ✓ PeerLink 1 · TLS · scope post") and the window chains in words. `ShowBusView.BriefLines` keeps commands and recent lines, its cap at 32 (windows-for-peers D11). The brief says the LINK verbs are a person's |
| Super Check | Round 90's row "Show network links" names posted contexts with their age, and "the Eye left out N apps" in grey when the bounds cut. This is the round's row for `CONTEXT POST` |
| the journal | one row the first time a connector's post is kept, and one row per refused post (counted per minute), never per post. Link transitions are Round 90's rows |
| the log | one line per poster state change (kept, refused, expired) |
| the export (`CONTEXT`, `/api/context`, posts to Stagewatch) | D11 |

---

## 8. Bounds and performance

**The geometry**, worked out from the constants (`EyeLayout.cs:83-89`; `EyeCanvas.cs:217,222`; `EyeCamera.cs:15,50-55`). The
reference room:
- Stagewatch, PlayMap and AGMessage, 12 things each;
- three timer nodes of 4 each;
- a desk of 4 screens and 6 control things.

| | layout | NETWORK or CONTROL height | fit scale on a 1,080-px canvas | labels at the fit view |
|---|---|---|---|---|
| today | one column of 48 | 3,304 | ≤ 0.28 | none (< 0.35) |
| this design | 6 clusters, 3 per row (3 columns × 4 rows of things each) | 2 rows × ~400 = ~800 | ~0.45 | drawn |

At 128 apps (16 expanded, 112 cards) the band is about 4 × 4 clusters plus a card strip. The fit view shows card labels down to the
camera's floor, and focus or a lens reads the rest. **The fence** (§10) asserts the reference room's fit scale is ≥ 0.35, computed
from the layout and the camera with no rendering.

**Cost.**
- **The build.** It is O(apps × (items + links)) with dictionary lookups. A context is at most 200 items and 400 links, apps at most
  128, so a build is about 77,000 element visits, and it runs only when a revision moves.
- **The hash.** D9 removes the per-second serialization of contexts.
- **Rendering.** It redraws only on a revision, a camera move or a pointer crossing (unchanged). The walk is cached. Edges off view
  are skipped.
- **Hit-testing.** Nodes are found by a linear rectangle scan, at most about 600 rectangles. Edges are tested only on a click.

**Measured:** no timing this round. The rebuild's duration and node count go to `patterns.metrics.csv`, so a later round can govern
from a measured cause. Nothing here is a governor: the bounds are what a person can read, and the geometry above is their cause.

---

## 9. Changes, per repo

### 9.1 Patterns (Round EYE; its number is the maintainer's)
- `src/Patterns.Core/Services/Eye.cs`:
  - `EyeFacts.Apps` replaces `ShowApps`;
  - `EyeNode` and `EyeEdge` gain their appended fields;
  - `EyePlane.Network`;
  - `Build` takes the network from `EyeNetwork`: the fold, provenance, needed-only red, counting `?`, D3's re-anchoring, D6's ends,
    and D8's bounds with forced chain ends;
  - `Hops` stops at app cards;
  - `Lines()` gains source and chain words;
  - lens `Network`.
- `src/Patterns.Core/Services/EyeNetwork.cs` (new): `Fold`, `Resolve`, `ReAnchor`, `Expand`, `KindPlane`, `KindColumn`, the
  counting-kind set, the bounds' constants.
- `src/Patterns.Core/Services/EyeLayout.cs`: the NETWORK band of clusters, sticky slots passed in, the collapsed strip, "+N more".
- `src/Patterns.Core/Services/EyeJson.cs`: the appended fields (§6).
- `src/Patterns.Core/Menus/NetworkMenus.cs` (new) and `EyeMenus.cs`: the LINK group (D13).
- `src/Patterns.Core/Services/ShowContext.cs`:
  - `Parse` validates link ends (D6);
  - `FromEye` exports `link:` items, P2 rows and own cross edges with `@` ends, never borrowed items or relayed edges (D11);
  - `ShowContextLinkEnd` (new).
- `src/Patterns.Core/Services/StagewatchFollow.cs`:
  - `ShowBusView.Apps` becomes `ShowBusView.Network(peers, records, contexts, devices, twinNodes, now)` → `EyeApp`s;
  - any kind may be linked;
  - a `patterns` hello folds into its beacon card by `inst`;
  - `Peers` rows unchanged, with `link` and `verified` appended once peers-not-screens lands.
- `src/Patterns.Core/Services/ShowBusNetwork.cs`: `KindWords` gains `playmap` → "PlayMap" (one hunk shared with Round 92.1).
- `src/Patterns.Core/Services/ControlProtocol.cs`: `CONTEXT POST` (`RemoteCommandKind.ContextPost`), and the lens words.
- `src/Patterns.App/Services/EyeService.cs`: `Gather` builds `Apps` with revisions (the hash no longer serializes contexts); keeps the
  sticky slots; records the rebuild's duration and count in metrics.
- `src/Patterns.App/Services/AppServices.cs`: `Apps(now)` composes the hello table, `StagewatchLinkService` (record, context, revision),
  `DeviceService`'s Show Bus node devices (record, context), `ControlService`'s accepted links and posted contexts, and the twin's node
  contexts; `ContextJson` uses the new export.
- `src/Patterns.App/Services/ControlService.cs`: `CONTEXT POST` for verified links of scope ≥ post; `PostedContexts` (latest wins, 30 s,
  ≤ 128); refusals per D10; `WireDeck` gains `PeerId`, `Kind`, `Scope`, `Level` (appended) once Round PL proves them.
- `src/Patterns.App/Services/CommandRouter.cs`: STATE `eye` and `bus.apps[]` appended (§6).
- `src/Patterns.App/Views/Controls/EyeCanvas.cs`, `src/Patterns.App/ViewModels/MainViewModel.Eye.cs`, `Views/Sections/EyeSection.axaml`:
  §7's desk changes.
- The Super Check's "Show network links" row (Round 90): the posted contexts' age and the bounds' line.
- `integrations/companion-module-patterns`: `src/variables.js`, `src/feedbacks.js`, `src/actions.js`, `src/presets.js`, `lines.txt`,
  `package.json` (its next minor).
- **Papers:**
  - `docs/EYE.md` §7 "The network: apps, links and claims";
  - `docs/PLAN.md` §NN;
  - `docs/REVIEW.md`;
  - `CHANGELOG.md`;
  - `README.md`;
  - `docs/REMOTE.md` (`CONTEXT POST`, `EYE LENS NETWORK`, `EYE` and STATE fields);
  - `docs/COMPANION.md`;
  - `docs/SHOWBUS.md` (its copy once the master lands);
  - `docs/OPEN.md` (§13 rows);
  - the tag table's row for the previous round.

### 9.2 Stagewatch (SW-EYE, its train)
- `src/Timer.Show/ShowContext.cs`: the rows by P2–P4 (drop `timer → link`, keep `timer -feeds-> window` while it shows, add
  `window -serves-> @holder` and `link -K-> @peer`).
- `src/Timer.App/ViewModels/MainViewModel.ShowBus.cs`: `LinkItems` carry the peer id and the PlayoutLink record's light and words
  (SW-PL3); `clock:<peer id>` per link.
- `src/Timer.Core/ScreenWindows.cs`: holder and browser words; `serves` rows from the grants (windows-for-peers D10).
- `docs/SHOWBUS.md` (master): §6 gains P1–P8, `@<id>` alone, `CONTEXT POST` on line carriers, and the informative kind table, in
  spec-1-3's single edit or 1.4.

### 9.3 PlayMap (PM-EYE; round-3 ownership)
- `src/PlayMap.Engine/ContextBuilder.cs` (spine; amendment lane §5.7): P2 and P3 rows, the `playlist → output` rows, and the held air
  words.
- `src/PlayMap.Core/Fleet/ContextShape.cs`: chain-end items stay in the first twelve.
- `src/PlayMap.Net/Links/WireClient.cs` / `PatternsLink.cs` (PM-X or the integration pass): `CONTEXT POST` once LINK proves `post`.
- `docs/SHOWBUS-NODE.md`: what PlayMap publishes for the Eye.
- STATE fixtures and the card descriptor, if a STATE block changes (none is planned).

### 9.4 AGMessage (AG-EYE1, AG-EYE2; the 0.5.x branch)
- `app/lib/context.js`: `display:` items, peer-link items, P2 and P3 rows, and the header comment.
- `app/server.js`: `currentContext` passes display statuses and peer-link records.
- `app/lib/nodes.js`: any kind `[A-Za-z0-9_-]{1,16}` (G18) and `cid` = node id (G32).
- `app/public/admin-plus.js`: `playmap` → "PlayMap".
- `app/lib/peerlinks.js`: `CONTEXT POST` over AG-PL3's line connector (AG-EYE2).
- `docs/NODES.md`, `docs/SHOWBUS.md` copy, `CHANGELOG.md`.

---

## 10. Tests

**Patterns, Core**
- **`EyeNetworkTests` (new):**
  - **Fold:** hello + held record + context + mention + a node device give one `app:<id>`. A level-0 deck stays a deck, with both
    observations in its words. A level-0 device folds only when the hello and the context agree, flagged `legacy`; otherwise a note.
    A `patterns` hello folds into `node:<inst>`. An id conflict gives one amber node.
  - **A link is an edge:** a `link` item with an `@peer` row draws no node. Its other rows re-anchor; for example, PlayMap's
    `link:patterns -controls-> air` gives `desk → app:…:air`.
  - **One edge per link:** this desk's record leads, the connector's and acceptor's words follow, and disagreement takes the worse light
    with both words.
  - **Sources:** every node and edge has one. Relayed and cross-app `told` edges carry `claim`.
  - **Lights:** a hello alone is never green. A gone heard-only app is grey. A gone needed app is red ✕. An acceptor's row is never red.
    The record's light and mark go to the edge verbatim, including `i`. A stuck show gives an amber card, a green link and counting
    things "? — …". A stale link gives "(N s ago)". A down link draws no things.
  - **Ends:** `@<own id>` is the desk; `@<own id>/screen:x` is this desk's screen; an undrawn item falls back to its card; an unknown
    app is counted `unplaced`. Malformed and over-long ends are refused at parse.
  - **The window chain of §3.3,** end to end.
- **`SyntheticNetworkTests` (new; round 84's fixed-seed xorshift, `SyntheticShowTests.cs:20-35`).** Generated rooms of 0–160 apps,
  random contexts within §6's bounds, and links. Asserted per room:
  - apps ≤ 128 plus one "+N more";
  - expanded ≤ 16;
  - at most 12 + 8 things per app;
  - relayed edges ≤ 256;
  - no two rectangles overlap;
  - the same facts in the same order give the same ids, rectangles and JSON;
  - a slot never moves while its app stays;
  - `left` counts exactly what was cut;
  - a secret-shaped canary (`swk_…`, `ptk_…`, a `/w/` path) in a context's words never reaches `FromEye`'s export.
- **`EyeLayoutTests`:** the reference room's fit scale ≥ 0.35 on a 1920 × 1080 canvas, computed from `EyeLayout` and `EyeCamera`, and
  the 0.28 of today's layout recorded as the cause in the test's comment.
- **`EyeJsonTests`:** the new fields at the end of each row, by raw substring, since some tests match STATE that way.
- **`ShowBusTests`:** `TheEyeDrawsALinkedStagewatch…` (`:306-325`) is rewritten, not skipped. The app card is `EyePlane.Network`, and
  a gone heard-only Slides agent is grey; the papers say why (13.16). `TheContextIsTheEyesPicture…` (`:250-286`) gains P2 rows and `@`
  ends, and still has no `app:` item.
- **`ShowContextTests`:** parse of `@` ends; `FromEye` never exports a borrowed item, a relayed edge or a mention.

**Patterns, App (headless)**
- **`EyeNetworkAppTests` (new), with FakeStagewatch and the scripted peer** (`tools/showbus/peerlink-peer.mjs` once PeerLink's kit
  lands; FakeStagewatch alone before then):
  - the link record goes to the edge, and STATE `eye.linksAmber` moves with it;
  - CERTIFICATE CHANGED gives a red edge and a menu entry *Accept certificate* with `STAGEWATCH ACCEPT <8 hex>`, the same line as the
    Nodes page button;
  - Stagewatch's stuck beat gives an amber card, a live link and the timer "?";
  - a forged hello for a pinned id gives an `i` note on the edge and no change of light;
  - a granted window on Screen 2 gives the chain's edges in EYE JSON;
  - a Show Bus node device's PlayMap context puts outputs and projectors on the VIDEO lens;
  - `CONTEXT POST`: kept for a verified `post` link; another id refused and never counted; refused at level 0; replaced by the latest;
    expired at 30 s; its journal row once.
  - Every loopback test sets `ControlService.TrustLoopback = false` and restores it in `finally`.
- **`DeskMenuAppTests`:** the Eye's LINK entries, the Nodes card's entries and the module's actions carry identical wire lines.
- **`EyeCanvas`:** marks drawn; an edge hit on right-click opens the LINK group; the walk is computed once per revision and focus
  (a counter seam).

**Companion module** (`npm test`): the variables and feedback from a STATE fixture; `lines.txt` holds `EYE LENS NETWORK`.

**Stagewatch:** `ShowContextTests`:
- `timer → link` rows are gone;
- `window -serves-> @holder` per grant;
- `link -controls-> @<patterns id>`;
- no item restates a peer's thing (P1);
- peers rows' `link` and `verified`.

**PlayMap:** `EyeContextTests`:
- P2 rows with `@` ends;
- `@<sw>/timer` and `@<sw>/window:x -feeds-> output`;
- `playlist → output`;
- chain-end items inside the twelve;
- the held air words.

`ContextShape` tests keep passing.

**AGMessage:** `showbus.test.js`:
- display items carry no message text, no file name and no hall code (a canary text);
- `@<sw>/timer -feeds-> display`;
- P2 rows;
- the `playmap` hello is kept, and a malformed kind is refused (`resilience.test.js:178` on the 0.5.0 branch changed);
- `cid` is the node id.

**Ecosystem lane** (cross-app rows handed to ecosystem-lane), on the one-kernel LAN rig:
- Patterns' EYE JSON has one node per app id, with no `deck:` duplicate for a proved link;
- the §3.3 chain resolves with real apps;
- a stuck rig gives every app card amber and every link green within `stuckMs` + 4 s;
- AGMessage's displays appear on Patterns' Eye after it posts.

---

## 11. Units

| # | repo | unit | depends on |
|---|---|---|---|
| SB-EYE | Stagewatch (master spec) | §6: P1–P8, `@<id>` alone, `CONTEXT POST` on line carriers, the informative kind table — in spec-1-3's single edit S1, else 1.4 | spec-1-3 S1 open (or 1.4) |
| PT-E.1 | Patterns | Round EYE.1: the NETWORK band of clusters, sticky slots, bounds and "+N more", the walk cached and edges culled, the hash by revisions (works on today's facts; closes F1, F5, F6) | — |
| PT-E.2 | Patterns | Round EYE.2: one id one node, sources and claims, needed-only red, link edges from records with marks, re-anchoring, `@` ends, counting `?` (Core, with `EyeNetworkTests` and `SyntheticNetworkTests`) | Round 90 PeerLink core (the record type); windows-for-peers PT-W3 (Round 92.3); SB-EYE words |
| PT-E.3 | Patterns | Round EYE.3: the feed — `AppServices.Apps` from the hello, the Stagewatch link, the node devices, accepted links and the twin; any kind linked; `playmap` words; the export by D11 | PT-E.2; Round 92.1 (node devices) |
| PT-E.4 | Patterns | Round EYE.4: `CONTEXT POST` on 9697 for verified `post` links; its Super Check words, journal row and log line | PT-E.3; Round PL (LINK, scope, the acceptor list) |
| PT-E.5 | Patterns | Round EYE.5: every reader — canvas marks, dashed claims, edge hit-test, side-card LINKS, `NetworkMenus` (Eye, Nodes card, deck), lens NETWORK, STATE and EYE appended fields, the brief, the module's variables, feedback and preset | PT-E.3 |
| PT-E.6 | Patterns | Round EYE papers: PLAN, REVIEW ("Walk: unwalked — no platform path touched"), CHANGELOG (a handed-in critique), README, REMOTE, COMPANION, EYE.md §7, OPEN rows, the tag row | PT-E.1…E.5 |
| SW-EYE | Stagewatch | context rows by P2–P4; per-link clock items; holder and browser words on windows; tests | SB-EYE; SW-PL3; windows-for-peers SW-W1; peers-not-screens SW-4 |
| PM-EYE.1 | Mapper | "Spine: the context names its peers and the windows it draws" — `ContextBuilder` P2/P3 rows, `playlist → output`, held air words; `ContextShape` chain ends; `EyeContextTests` | SB-EYE; round-3 amendment lane §5.7 |
| PM-EYE.2 | Mapper | `CONTEXT POST` to Patterns over the Patterns link | PM-PL3 (PM-X or the integration pass); PT-E.4 |
| AG-EYE.1 | AGMessage | display items, peer-link items, P2 and P3 rows, `playmap` hello, `cid`, the context.js comment; tests | SB-EYE |
| AG-EYE.2 | AGMessage | `CONTEXT POST` to Patterns and PlayMap over AG-PL3 | AG-PL3; PT-E.4 |

**How each Patterns round closes:**
- one commit per unit, `Round NN.k: <what landed>`;
- all seven suites and the module's tests green before the push.

The verb an output can show is `CONTEXT POST`. Its rig row is the Super Check's "Show network links", read beside the Eye's NETWORK
lens. **No platform path is touched**, so the round is `unwalked` as a fact in the papers, not a block.

---

## 12. Risks
- **Claims can be false.** A node can claim it shows another's window, or relay a link's state, wrongly or maliciously. They are
  drawn dashed with "X says" and never drive anything. Placement claims are also checked by their granting node (windows-for-peers
  D10).
- **Amber during migration.** Legacy, plain, keyless and first-use ambers will crowd the problems queue, which has no dismiss memory
  (OPEN L44). The marks `i` keep notes out of it, and red is kept for needed links. The queue's memory is still open.
- **Cluster moves.** A cluster that expands when an app turns amber moves once, and the operator's eye loses it. Slots are sticky, the
  move is said, and focus follows the app's id.
- **Level-0 folds of a dialled device** rest on an address plus a claimed id. They are flagged `legacy` and end when PlayMap speaks
  LINK, which may wait on Mapper's integration pass.
- **`CONTEXT POST` is a new acceptor input.** It is bounded at 64 KiB per line, one context per id kept, ≤ 128 kept, 30 s, and needs
  a verified `post` scope. A flood of posts is a connection's own flood, under 13.14's bounds.
- **The edge hit-test** on dense pictures can pick the wrong edge. The app card's LINK group carries every link verb too.
- **AGMessage's display names** may name places. Its privacy rule keeps hall names and codes out; display names are its owner's call,
  and a test holds text and file names out.
- **No timing was measured.** The bounds are geometric. If the rebuild proves slow on the show laptop, the metrics row will say so
  before any governor lands.
- **Dependencies.** The `@` ends depend on spec-1-3's single edit. Readers that predate it ignore unresolved ends (Patterns today:
  `Eye.cs:814`; AGMessage ignores links; PlayMap keeps them raw), so publishing early breaks nobody.
- **PlayMap's `ContextShape` contract** (the first twelve) is now relied on by name. If the Eye's twelve ever changes, `EyeContextTests`
  in Mapper and `EyeLayoutTests` in Patterns must move together. The forced-ends rule adds up to eight and never removes any.
- **Twin nodes stay a named exemption** (13.18). Their link is drawn as the twin's mirror edge, not as a PeerLink.

---

## 13. What this design does not do (OPEN rows to add)
- The replay does not relight link edges from the journal's link rows. Round 90 writes the rows; a later Eye round reads them.
- The problems queue's memory (L44) is still open.
- NDI chains across apps: PlayMap knows an NDI source by name, not by Show Bus id, so "Patterns' NDI send → PlayMap input" is not
  drawn. This needs a hello or context field naming its sends.
- PlayMap's fleet board and Stagewatch's Show network page could draw the same edges from the same rows. That is each owner's call.
- The Eye in a browser with the view in the URL (PLAN §102.5) is unchanged.
- AGMessage is drawn by its hello and Stagewatch's rows only, until AG-EYE.2 and PT-E.4 have both landed.
