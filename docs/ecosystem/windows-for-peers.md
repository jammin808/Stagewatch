# Windows for peers — a Stagewatch window shown by Patterns and drawn by PlayMap, its pass never published (revised after review)

Area: windows-for-peers. Gaps: G09, G10, G11, G12, G22, G23, G24, G46, G47. Design only: nothing in any repo was edited or
built. Line numbers are from the trees as read on 2026-10-25. It builds on peers-not-screens (`design/peers-not-screens.md`: node
links `/ws?node=<id>&t=<kind>`, one node one key, `NodeBindings`, `verified:"key"`). It uses cross-app-proposals' §6 verb notation
(`VerbSyntax`). It leaves playout-links' files alone except where named. §9 answers the review item by item.

**What changed in this revision**
- **Nothing on air ever says words or goes white.**
  - A held page is drawn only while the window shell says it has its assignment and is not gone. Otherwise every branch draws
    black: no page, no frame, a failed navigation, gone. There is no `about:blank`.
  - Every link on a grant address whose token stops resolving hears the black end, `revoke` included.
- **PlayMap's window source fails closed.** It is a source kind of its own, `window:<id>`, never the followed timer. An unknown or
  withdrawn window draws black.
- **Draw grants use node-link-only messages** (`window-assign`, `window-end`, `window-identify`, `window-shown`). `window reload`
  is refused for a window only apps draw.
- **Status.** `WindowStatus()` stays one row per window. A separate `ViewerStatus()` gives one row per (window, via), bounded and
  evicted per key.
- **Grants survive link loss.** They are persisted per machine and never twinned. The twin gets its own grant and is told what it
  lacks. Kept passes become the show's secrets on the `Secrets` list.
- **`SCREEN n WINDOW` never blanks a screen.** It is live only when the page is ready. Otherwise it is refused and steered to
  PVW + `SCREEN n TAKE`.
- **Trust rules.**
  - Placements are claims, taken only from key-verified holders of the grant.
  - A node may ask a grant only for itself.
  - The path rides only links whose current `can=` offers `pages`, and never to a key that has crossed the LAN unencrypted.
- **Same-machine peers dial loopback**, and Stagewatch counts its own addresses as local.
- **The commands use cross-app-proposals' §6 notation**, so they stay machine-readable.
- **The kept-pass rule shrinks.**
  - It holds Stagewatch window paths automatically; other addresses are kept only when the operator asks.
  - There is no show-load migration: a raw window address in an old show is inert and named by a Super Check row.
- **Tombstones are dropped** (no measured cause). An e2e step guards the lockout case.
- **PlayMap.**
  - Its flash follows leader time.
  - Query flags it does not draw are reported as an error.
  - An https stream is refused with the reason rather than assumed playable.
- **A maintainer decision gate** comes before any grant path is sent.

## The story the operator gets

1. **Granting.** On Stagewatch: Screens → Windows → right-click **lobby** → **Grant to ▸ Patterns FOH**, or
   `window grant lobby 9f8e7d6c` from any transport. Stagewatch hands that app the window over the app's own key-verified node
   link. Nobody copies an address.
2. **Where it appears on Patterns.** **Stagewatch lobby** shows up in:
   - Library → Web → Stagewatch windows;
   - the screen tile's right-click (**Stagewatch window ▸ lobby**);
   - the Run strip, Companion and the wire.
3. **Putting it on air.**
   - Once the page is open and the window shell has drawn its assignment, `SCREEN 2 WINDOW lobby` is instant.
   - Before that, the desk opens it on Screen 2's PVW (`SCREEN 2 PVW WINDOW lobby`). The tile says *opening…* and then
     *ready*, and `SCREEN 2 TAKE` puts it up.
   - The screen never goes black or white while a browser starts.
4. **One chain everywhere.** Stagewatch's window list, its context, the Companion keys and Patterns' Eye all say:
   *Stagewatch window lobby → shows /clock → granted to Patterns FOH → Patterns FOH says: Screen 2 — LED wall*. Each step is either
   observed or labelled as the app's claim.
5. **PlayMap draws instead of browsing.** It gets **Grant to ▸ PlayMap Stage left** as well. With no browser, it draws the window
   itself (timer, clock, holding, black) as the source `window:wk3m9x2` and reports what it drew, exactly as a browser window
   does. Before a restart's link is back, it draws the window's own loss state.
6. **Withdrawing.** **Withdraw** (or `revoke`) ends the app's address. The app's screen goes black, never Stagewatch's "address
   replaced" notice in front of an audience. The desk says why.
7. **The standby twin.** A standby that would take over a screen showing a granted window needs its own grant. Patterns' Super
   Check says so before the show, by name.

A Patterns operator who pastes a Stagewatch window's address by hand still gets a working page. Patterns keeps the address as one of
the show's secrets and publishes only `kept://10.0.0.5:9924/<id>`.

---

## 0. What exists (evidence)

**Stagewatch windows (2.17)**
- A window's token is its identity and its pass. It is persisted in settings (`Stagewatch/src/Timer.Core/ScreenWindows.cs:13-37`,
  token at `:18-19`). The ids are `"w"` plus 6 characters of a 32-letter alphabet (`:83,91`), and there are at most 64 windows
  (`:80`). `ByToken` compares every window in constant time (`:110-119`).
- Content is limited to Stagewatch's own pages (`:121-140`). `Apply` is pure (`:171-311`), and `revoke` rotates the token
  (`:295-299`).
- `ContextItems` (`:320-348`) reads the observed row with `FirstOrDefault` by window id (`:330`). The item is green only when the
  shown revision equals the sent one.
- `WindowDirector.Run` applies, saves and pushes (`Stagewatch/src/Timer.Remote/WindowDirector.cs:35-61`).
  - Identify and reload count a window reachable only when `WindowStatus()` has `Connected > 0` (`:51`).
  - `ReloadWindows` pushes only to window sockets (`WebServer.cs:172-186`).
- **The window link.**
  - Admission: `/ws?win=<token>`. An unknown token hears `WindowGone` and is closed (`Stagewatch/src/Timer.Remote/WebServer.cs:1140-1189`, the gone branch at `:1179-1189`).
  - `SendLoopAsync` re-checks `ByToken` on every turn and sends the plain `WindowGone` on any miss (`:1248-1253`, `:1285`).
  - Assignment: `WindowJson` (`:1288-1292`).
  - `shown` reports are kept in `_windowSeen[windowId]`. The map is cleared whole above `4 × Max` entries (`:1380-1395`, clear at
    `:1392`).
- **`WindowStatus()`** is one row per window (`:189-205`). Its readers:
  - `HandleWindowListAsync`, through `ToDictionary(o => o.WindowId)` (`:680`);
  - `ScreensWindow.xaml.cs:101-116`, whose subtitle counts open windows;
  - `MainViewModel.Windows.cs:26,49`;
  - `WindowDirector.cs:51`;
  - the rig (`tools/Stagewatch.Rig/Program.cs:298`);
  - `tests/Stagewatch.Tests/WindowTests.cs:260-276` (`.Single` by window id).
- **The shell** (`Stagewatch/src/Timer.Remote/wwwroot/window.js`).
  - It exposes `SWShell.state()`: content, page, hold, link, rev, error, loading (`:54-56`). It writes `data-hold`, `data-link`,
    `data-showing`, `data-error` and `data-rev` on the root element (`:212-216`).
  - While a page loads with no hold, it shows nothing (black) (`:170-175,197-201`).
  - `revoked()` shows `#gone`, "This window's address was replaced or removed…", and stops for good (`:268-278`;
    `window.html:41`).
  - It reconnects with a backoff of up to 5 s (`:262-266`).
- **Wrong addresses lock a device out.** Five a minute count as a lockout (`WebServer.cs:625-642`). A dead token draws one
  `WindowGone`, and the shell stops (`:1179-1189`, `window.js:250`). An unknown `/w/` page is a 404 notice: grey words on black
  (`WebServer.cs:654-658`, `:665-668`).
- **What a node key may do.** It may run only `message` and `clearmessage` (`Stagewatch/src/Timer.Core/ApiKeys.cs:84-85`). The
  copilot rates every window action except `new|rename|loss|fade` as high (`Stagewatch/src/Timer.Integrations/Copilot.cs:228`).
- **The parser** defaults a missing window to `all` (`Stagewatch/src/Timer.Core/ShowState.cs:940`).

**Stagewatch context, proposals and copilot (G23, G24)**
- **The context's links.** `LinkItems()` puts the window items and `clock:patterns` into the links list
  (`Stagewatch/src/Timer.App/ViewModels/MainViewModel.ShowBus.cs:53-67`). `ShowContext.Build` then emits `timer -hears-> <id>` for
  each (`Stagewatch/src/Timer.Show/ShowContext.cs:126`).
- **The command rows.** There are 15 (`ShowContext.cs:28-45`). Patterns' brief keeps 12
  (`Patterns/src/Patterns.Core/Services/StagewatchFollow.cs:281`).
- **The risk matcher.** `RiskFromCommands` treats a row as fixed words up to the first `<…>` or word containing `/`
  (`Stagewatch/src/Timer.Core/Approvals.cs:225-237`). So `show|cue` never matches `show`, and `next | start | pause` needs five
  words. cross-app-proposals replaces this matcher with `VerbSyntax`, which reads `A|B`, ` | `, `[…]` and `…`.
- **Peer contexts.** `PeerContexts.Accept` stores any valid context (`Stagewatch/src/Timer.Core/ShowBus.cs:57-69`). `Parse`
  keeps 64 items and drops `links` (`:87-112`). The Show Bus contract sets no item cap. 200 is Patterns' writer cap
  (`Patterns/src/Patterns.Core/Services/ShowContext.cs:68`). PlayMap's round 3 states its reader's own bounds as ≤ 200 items and
  ≤ 400 links (`Mapper/docs/round3/PLAN.md:2340`).
- **Proposals.** `ProposalDesk.Submit` binds the key only to `from.id` (`Approvals.cs:92`). The desk's headline for a request is
  the peer's `reason` when it gave one (`Stagewatch/src/Timer.App/ViewModels/MainViewModel.Desk.cs:99`). `NodeRef(Kind, Id, Name)`
  is already the proposals' record (`Approvals.cs:8`).
- **The copilot's peers.** `AiContext.PeerFact` has no id and no abilities (`Stagewatch/src/Timer.Show/AiContext.cs:11,97`).
  cross-app-proposals appends `Id`.
- **The spec.** It has no window section and no `windows` cap (`Stagewatch/docs/SHOWBUS.md:21` rule 5, `:37-41`). The hello caps
  are at `MainViewModel.Slides.cs:34-39`.
- **The doctrine line.** "the PIN, keys and window addresses ride in no log line, no journal row and no frame"
  (`Stagewatch/CLAUDE.md:38-39`).
- **The detail frame.** Its last block is `rooms` (`Stagewatch/src/Timer.Core/SnapshotJson.cs:294-313`). Rooms enter as ambient
  data (`MainViewModel.Systems.cs:173`). The rig's `Frame()` sets no rooms or windows (`tools/Stagewatch.Rig/Program.cs:258-295`).
- **The e2e lane** depends only on `playwright` 1.63.0 (`Stagewatch/tests/e2e/package.json`).

**Patterns (G09, G10, G24)**
- **The mount key carries the address.** A page's key is `"web:" + its full address` (`Patterns/src/Patterns.Core/Media/InputKeys.cs:11-15`).
  Its readers are:
  - the Eye (`Patterns/src/Patterns.Core/Services/Eye.cs:553,557`; `Patterns/src/Patterns.App/Services/EyeService.cs:614`);
  - the context items (`ShowContext.cs:178-195`), which `CONTEXT` answers unpaired;
  - STATE residency (`Patterns/src/Patterns.App/Services/CommandRouter.cs:699-706`);
  - `InputLabels`, keyed by the input key (`Patterns/src/Patterns.Core/Model/ShowState.cs:2937,2993-3000`).
- **Where the mount comes from.** `MediaLocator` wants pages by `WebUrl` (`Patterns/src/Patterns.Core/Services/MediaLocator.cs:209-210,237-238`).
  STATE `web.url` is `CurrentUrl`, or else the wanted target (`CommandRouter.cs:294-306`;
  `Patterns/src/Patterns.App/Services/WebFrameSource.cs:749`).
- **The Library title.** A web tile's name keeps the path's tail up to 28 characters before cutting (`Patterns/src/Patterns.App/Services/LibraryItems.cs:211-224`,
  the cut at `:219`). `w/` plus 26 is exactly 28, so **all 26 token characters** are kept.
- **The browser source.**
  - It navigates with no background colour set. No `DefaultBackgroundColor` appears anywhere in `Patterns/src`.
  - `NavigateCore` logs the address on failure (`WebFrameSource.cs:1389-1404`).
  - `NavigationCompleted` sets "Showing" or "The page failed" (`:253-264`). `ProcessFailed` only sets words; nothing reloads by
    itself (`:272`).
  - `StatusText` (`:713-728`).
  - It already injects document-created scripts (`:873,959,1026`).
  - There is no `ServerCertificateErrorDetected` handler (`:248-272`). The SDK is 1.0.3595.46 (`Patterns.App.csproj:39`).
- **What the renderer draws without a picture.**
  - A pattern's page draws `PlaceholderCard` with words when there is no source (`Patterns/src/Patterns.Rendering/Patterns/MediaAndParticlePatterns.cs:144-150`).
    It does the same with `page.StatusText` when `DrawInput` fails (`:152-155`). `DrawInput` with no frame asks the source to
    draw (`:258-274`).
  - A layer's page draws its status words (`Patterns/src/Patterns.Rendering/LayerRenderer.cs:143-155`).
  - `IWebSource` has default interface members (`Patterns/src/Patterns.Rendering/Media/VideoService.cs:75-105`).
- **The verbs.** `WEB OPEN` only re-points a mounted page (`Patterns/src/Patterns.App/Services/WebActions.cs:45-51`). At most 4
  pages run (`Patterns/src/Patterns.App/Services/WebEngine.cs:24`).
  - `SCREEN n PATTERN` is live, own picture (`Patterns/src/Patterns.Core/Services/ControlProtocol.cs:461-466`;
    `Patterns/src/Patterns.App/Services/ShowActions.Content.cs:168-188`).
  - `SCREEN n PVW …` stages (`ControlProtocol.cs:215-230`).
  - `SCREEN n TAKE` exists (`:477-478`).
- **The Stagewatch link.**
  - It dials the hello's sender address (`Patterns/src/Patterns.App/Services/StagewatchLinkService.cs:225-241`, `peer.Address`
    at `:238`). The hello goes to broadcast and loopback (`Stagewatch/docs/SHOWBUS.md:27-28`).
  - It logs approved lines raw (`StagewatchLinkService.cs:497`).
  - Proposals target this desk (`Patterns/src/Patterns.Core/Services/ShowBusNodes.cs:217-226`).
- **The Eye** caps a linked app at 12 items (`Eye.cs:798`) and links within one app only (`:814`). `FromEye` drops borrowed
  edges (`ShowContext.cs:189`).
- **Secrets and the twin.**
  - `Secrets.Names`/`Placed` are "the one list every exit door reads" (`Patterns/src/Patterns.Core/Services/Secrets.cs:8-28`).
  - The twin's wire carries the mirrored sections, with credentials blanked unless `SendSecrets` (`Twin.cs:364-383`;
    `CarriesSecrets` at `:398`).
  - A standby keeps its own secrets where the wire is blank (`:404-407`).
  - `Control` is a local section, never twinned (`Twin.cs:324-328`). The Stagewatch link config lives there
    (`ShowState.cs:2331-2360`).
  - The twin link is plain TCP after the key proof, and `SendSecrets` is on by default (`Patterns/docs/OPEN.md:29` L36).
  - Each install folder has its own Show Bus id (`Patterns/src/Patterns.App/Services/ServiceKernel.cs:181`).
- **Show-level web state.** `WebConfig.Url` and `SavedUrls` hold typed addresses in the show (`ShowState.cs:2783-2822`).
- **The Run strip's view model** is `Patterns/src/Patterns.App/ViewModels/RunViewModel.cs` (its chips, `:242,526`).

**PlayMap (G11, G12, G47)**
- **The link.**
  - It is a screen: `/ws?cid=…&page=/playmap&detail=1` (`Mapper/src/PlayMap.Net/Links/StagewatchLink.cs:283-284`).
  - It dials the hello's address (`:63-75`).
  - `identify`, `redirect`, `reload` and `disconnect` go to `ScreenCommand` (`:395-407`). `identify` becomes
    `OUTPUT IDENTIFY ALL 5` (`Mapper/src/PlayMap.Engine/Host/NetworkHost.cs:780-783`). Unknown types are dropped (`:437-439`).
- **The timer.**
  - The frame parser reads watermark, holding, flash, urgency, next, blackout and `show{}`
    (`Mapper/src/PlayMap.Net/Links/StagewatchTimer.cs:73-114`). The watermark must always be shown (`:55`).
  - The timer JSON drops them (`NetworkHost.cs:318-341`). `TimerView` draws text, colour, title and message
    (`Mapper/src/PlayMap.Engine/Rendering/TimerView.cs:131-150`).
- **Sources.**
  - `SourceKind` (`Mapper/src/PlayMap.Core/Model/Content.cs:7-23`) is parsed by name, and the ids are `Ids.IsValid` (`:44-63`).
  - The `timer:` source ignores its id (`Mapper/src/PlayMap.Engine/Rendering/EngineSources.cs:178-179`).
  - A kind with no case falls to `Video(source) ?? Black` (`Mapper/src/PlayMap.Render/Engine/SourceResolver.cs:163-181`). A null
    lease keeps the last uploaded picture (`:417-432`), so "no frame" is not "black".
- **Import.**
  - `MEDIA IMPORT` of an http(s) page adds a Web item, named after the full URL, and answers "added"
    (`Mapper/src/PlayMap.Engine/EngineCore.Content.cs:388-405`).
  - Web and Embed open nothing (`Mapper/src/PlayMap.Engine/Host/MediaBackend.cs:144-145`), yet readiness, LIST and FETCH call them
    ready (`MediaBackend.cs:381-382`; `EngineCore.Content.cs:447,508-510,527-528`).
  - Streams open through `CaptureSource` (`Mapper/src/PlayMap.Media/MediaLibrary.cs:196`). No paper or code shows the FFmpeg
    build has a TLS protocol.
- **Settings.** `NodeSettings` is a partial Core/Bus record (`Mapper/src/PlayMap.Core/Bus/Pairing.cs:101-123`, and the
  `NodeSettings.*.cs` partials).
- **Round 3's rules.**
  - `Core/Model/*`, `StateWriter`, `ContextBuilder` and `VerbCatalog` (outside blocks) are spine files, changed only by `Spine:`
    commits (`Mapper/docs/round3/PLAN.md:307-312`, §5.7 `:2343-2352`).
  - `EngineCore.Content.cs` belongs to desk-map, then canvas (`:2180`).
  - A folder not named for a wave has no owner, and nothing in that wave edits it (`:2121-2125`).

---

## 1. Decisions

**D0. A maintainer decision gate before any grant path travels.**
- Show Bus 1.3 §1 rule 5 gets an exemption naming exactly two carriers of a page address that is a pass:
  - a browser's address bar;
  - one key-verified node link (TLS or local) to the grant's own node.
- Stagewatch's doctrine line (`CLAUDE.md:38-39`) would read "… ride in no log line, no journal row and no frame; a grant's
  address rides only its own app's key-verified node link".
- Both are the maintainer's calls. The CLAUDE.md line is the maintainer's to write; no unit edits CLAUDE.md. SW-W3's delivery code
  waits for that answer, which is recorded in REMOTE 2.18 and WHATS_NEXT.
- *Why:* the spec and the doctrine must say what the wire does before the wire does it.

**D1. A grant, not a copied address.**
- `window grant <window> <app>` is an operator verb on Stagewatch, risk high.
- The app receives the window only on its node link, authenticated by its own node key (`verified:"key"`).
- *Alternatives rejected:*
  - the path in `/api/windows` for node keys: every node would see every window;
  - an HTTP grant pull: a second credential path, and no push on revoke;
  - the copied address: today's leak.

**D2. Each page grant has its own address** (`/w/<grant token>`, the same alphabet). At most 4 grants per window.
- Draw grants have no token at all.
- `revoke` rotates the main token and every page grant's token.
- **Every window link whose token stops resolving because it was a grant** hears the black end
  `{"type":"window","revoked":true,"black":true}`. That covers withdraw, delete and revoke. The window's main address keeps today's
  notice.
- *Why:* withdrawing one app stops only its address. Stagewatch observes which link is the app's by the token it came in on (G24).

**D3. Two ways to hold a window.**
- `how:"page"`: the app opens the page in its browser (Patterns, ability `pages`).
- `how:"draw"`: the app draws the content itself (PlayMap, ability `window-draw`) and holds no address.
- `how` is chosen when the grant is made: from the node link's `can=` when linked, else from the hello caps, worded as a claim in
  the receipt.
- Delivery always checks the **current** key-verified link's `can=`. A page path or draw assignment is sent only when that link
  offers the matching ability. Otherwise the grant row says `cannot`, with the reason.
- *Alternative rejected:* G11's `Authorization: Window` header. It puts a pass in PlayMap's store and needs a second socket.

**D4. A page address travels only over TLS or a local link, and never to a key that has crossed the LAN in clear.**
- *Local* means the remote address is loopback or one of this machine's own interface addresses. That set is cached and
  refreshed on `NetworkChange.NetworkAddressChanged`.
- Patterns and PlayMap dial `127.0.0.1` when the hello's `host` equals this machine's name, or its address is one of this machine's
  own.
- When this Stagewatch offers TLS and a node key arrives on a plain non-local link, the key is marked `clearSeen`. That key's grants
  then get no path until the operator replaces the key.
- *Why:* on one laptop the hello's address is often the LAN IP (§0), so "plain loopback" would rarely happen. A sniffed key replayed
  over TLS must not receive the pass.
- *Alternative rejected:* refusing every node key on plain links when TLS is offered. That is peers-not-screens' call, and it would
  stop AGMessage's plain link for something only grants need.

**D5. Patterns' mount key is a held address, never a pass.**
- A grant is `stagewatch://<sw id>/<window id>`; a kept pass is `kept://<host[:port]>/<id>`.
- Every reader of the key carries only that: STATE, the Eye, the context, residency, `InputLabels`, the journal, the logs, the show
  file, the twin and the Library.
- Resolution to the real URL happens only inside the browser source, at navigation.
- **The fence is in Core, at the key.** `InputKeys.Web` and `MediaLocator` publish any **raw** Stagewatch window address as
  `unkept://<host[:port]>` with the token stripped. A held address that does not resolve draws black (D8), so a raw address that
  arrives from anywhere (an old show, an older twin main, a hand edit) is inert. A Super Check row names where it sits.
- *Why:* G09 leaks through the key itself. Fixing the key fixes every reader at once, and a fence test builds every published
  artefact.
- *Alternative rejected:* redacting each reader. There are at least nine, and the next one would leak.

**D6. Kept passes are the show's secrets.**
- `ShowState.Web.Passes` is `[{Id, Label, Secret}]`.
  - `Secret` is already on `Secrets.Names`, so the support bundle masks it.
  - The twin carries it under `SendSecrets`, as it carries a box's password: `CarriesSecrets` gains `Web`, and
    `OwnSecrets`/`KeepSecrets` keep a standby's own passes where the wire is blank.
- **What is held automatically:** only a Stagewatch window path (`/w/` plus 26 of its alphabet), at every entry point:
  - the Media page;
  - `WEB OPEN`;
  - the cue editor;
  - `Web.Url`;
  - `SavedUrls`;
  - an approved line.
- **Any other address:** held only when the operator ticks **Keep as the show's secret** on the Media page.
- There is no show-load migration (D5's fence covers old shows).
- *Why:* the review's scope point and Patterns' "no legacy" brief. Every exit door already reads `Secrets`.

**D7. Grants outlive the link.**
- Patterns persists each delivered grant in `Control.Stagewatch.Grants`: `{Sw, Window, Name, How, Base, Secret}`. `Control` is
  local and never twinned, and `Secret` is masked in the bundle. PlayMap persists its draw grants and last assignments (no secret)
  in `NodeSettings`.
- Only a received `grants` list that omits a window removes it. A dropped link, a Stagewatch restart or a Patterns restart removes
  nothing.
- So the page or drawing comes back at once, and the shell's own loss rule covers a Stagewatch outage.
- `rev` is per link: a receiver takes the first `grants` on a new link whatever its number.

**D8. Black on air, never words or white, for every held page.**
- There is no `about:blank`.
- For a held Stagewatch page, Patterns injects a document-created observer. On each change of the root element's `data-*`, it posts
  `SWShell.state()` to the host.
- The source is `Withheld` (black) unless:
  - the navigation succeeded;
  - `rev > 0`;
  - and `link ≠ "gone"`.
- **Every branch draws black for a held page:** no page, no frame, a `DrawInput` failure, a failed navigation, gone. This covers a
  pattern's page and a layer's.
- The words go to the desk, STATE, the Eye and the Run strip.
- A non-Stagewatch kept page is drawn on navigation success, and is black on any failure.

**D9. `SCREEN n WINDOW` is live only when the page is ready.**
- *Ready* means mounted, not `Withheld`, `loading` empty and `link` `"ok"`.
- Otherwise it is refused with the reason, nothing changes, and it steers to `SCREEN n PVW WINDOW <window>` then `SCREEN n TAKE`.
- `SCREEN n TAKE` of a staged held page that is not ready is refused the same way.
- *Why:* one air. A live verb never blanks a screen, and AirState never says one thing while the glass shows another.
- *Alternative rejected:* keeping the previous picture until ready. The air state would then disagree with the glass.

**D10. Placement is the app's claim, told in its §6 context with a link end `@<node id>/<item id>`.**
- Stagewatch takes a placement only when all of these hold:
  - the context is key-verified;
  - the poster holds a grant of that window;
  - that grant has an open viewer or a `window-shown` report.
- It is always worded as a claim: "Patterns FOH says: Screen 2 — LED wall".

**D11. G23.**
- `timer -feeds-> window:<name>` only while the window shows a page.
- No link for health items.
- The commands are written in cross-app-proposals' §6 notation, read by its `VerbSyntax`. SW-W1 lands after that unit.
- Patterns' brief cap rises from 12 to 32.

**D12. Placing a window on Patterns is Patterns' act.**
- `SCREEN <n> WINDOW <window>` (D9), `SCREEN <n> PVW WINDOW <window>`, `PVW WINDOW <window>`.
- `NODES ASK WINDOW <window>` sends a proposal to Stagewatch's desk.
- **The proposal fence:** at `Submit`, a node-key proposal whose line is `window grant …` must name its own `from.id` as the app.
  The desk shows the **line** as the headline for window grants, never the peer's reason.

**D13. Draw grants ride node-link-only message types:** `window-assign`, `window-end`, `window-identify` (Stagewatch → app) and
`window-shown` (app → Stagewatch).
- No screen handler reads them, so nothing becomes `OUTPUT IDENTIFY ALL`.
- `window reload` of a window that only apps draw is refused: "it has no page to reload".
- peers-not-screens §2.1's rule "a node link never receives redirect, identify, reload or disconnect" gains: "the only window
  messages a node link hears are §12's, for its own grants".

**D14. PlayMap's window source fails closed.**
- `window:<id>` is a new `SourceKind.Window`, appended by a `Spine:` commit.
- It always yields a frame (never null, which would keep the last picture):
  - a granted, assigned window draws its content, or its loss state while the link is down;
  - an unknown or withdrawn window draws black.
- It never falls back to the followed timer. `timer:stagewatch` stays the followed timer.
- The flash phase is a pure function of leader time at vblank.
- Query flags it does not draw are reported in `window-shown.error` (amber).

**D15. No tombstones.**
- A dead token draws one `WindowGone`, and the shell stops (§0). Patterns never reloads by itself. So the lockout burst has no
  measured cause.
- "No measurement this round" is written in the papers.
- An e2e step proves a revoke plus a reload leaves the address unlocked; it would show the cause if it ever appeared.

**D16. The twin.**
- A grant belongs to a node, and a standby is a node of its own (its own id, `ServiceKernel.cs:181`).
- Grant paths never ride the twin link, which is plain TCP (OPEN L36).
- The standby tells its main which windows it holds (twin word `WINDOWS`, ids only). The main's Super Check names any on-air window
  the standby lacks.
- Kept passes travel as show credentials (D6).

**D17. Status stays one row per window.**
- `WindowStatus()` keeps its shape and gains `Drawn` (appended).
- A new `ViewerStatus()` has one row per (window, via). It is bounded at `(MaxGrants + 1) × Max` = 320 and evicted per key when a
  window or grant ends, never cleared whole.

**D18. G46 is left.** No peer serves a frameable page, and a cross-origin page breaks the window's CSP, its `window.SW` check and
pinning.

---

## 2. Wire (exact shapes)

### 2.1 Stagewatch grammar (HTTP `/api/window/...`, WS `{cmd:"window",args}`, OSC `/stagewatch/window`, Companion `WINDOW …`, MCP `run_command`, copilot)

```
window grant <window> <app>              # one window, never all; app = a Show Bus id [A-Za-z0-9_-]{1,40}, or a quoted name when unique
window withdraw <window|all> [<app>]     # the window (or all) must be named: a bare `window withdraw` is refused
```
- **The receipt** (applied):
  ```json
  {"type":"receipt","id":"a1","ok":true,"stage":"applied","effect":"lobby granted to Patterns FOH (9f8e7d6c) as a page — it takes the address when it links with its node key","refs":{"window":"wk3m9x2","node":"9f8e7d6c"}}
  ```
  When `how` came from hello caps (unauthenticated), the effect adds "(it says it shows pages; checked when it links)".
- **Refusals** (`ok:false`, nothing changed):

| case | error |
|---|---|
| no window named | `window withdraw takes a window (or all)` |
| unknown app | `No show app called "x" on the show network (heard: Patterns FOH 9f8e7d6c, PlayMap Stage left 1a2b3c4d)` |
| ambiguous name | `"FOH" names 2 show apps — use its id (9f8e7d6c, 7e6d5c4b)` |
| no ability | `AGMessage Office can't show a window: it offers neither pages nor window-draw` |
| already | `lobby is already granted to Patterns FOH` |
| full | `lobby is granted to 4 show apps already — withdraw one first` |
| all | `Grant one window at a time` |
| withdraw nothing | `lobby isn't granted to anyone` · `lobby isn't granted to PlayMap Stage left` |
| reload, draw only | `lobby is drawn by PlayMap Stage left, which has no page to reload — window show lobby <page> sends it again` |

- **Risk:** `grant` and `withdraw` are high (`Copilot.Classify` lists them explicitly).
- **Help:** `window grant <window> <app> | window withdraw <window|all> [app]`.

### 2.2 Settings (`remote.windows[]`, never a show file)

```json
{"id":"wk3m9x2","name":"lobby","token":"…26…","content":"/clock","…":"…","rev":12,
 "grants":[{"node":"9f8e7d6c","t":"patterns","name":"Patterns FOH","how":"page","token":"…26…","made":"2026-10-25T10:00:00Z"},
           {"node":"1a2b3c4d","t":"playmap","name":"PlayMap Stage left","how":"draw","token":"","made":"…"}]}
```
- `grants` is appended and holds at most 4 entries.
- An entry is dropped at load unless all of these hold:
  - a valid node id;
  - `how` is `page` or `draw`;
  - a page grant has a valid, distinct token;
  - a draw grant has an empty token.
- `remote.apiKeys[]` (peers-not-screens' `node`/`nodeKind`) gains `"clearSeen":"<ISO>"|""`, appended (D4).

### 2.3 Node link additions (peers-not-screens §2.1)

**Upgrade query:** `/ws?node=9f8e7d6c&t=patterns&can=pages[&detail=1]`.
- `can` is a comma list of at most 4 words of `[a-z-]{1,16}`. Unknown words are ignored: they are abilities, not commands.

**Stagewatch → app.** Sent only on links with `verified:"key"` whose bound id is the grant's node:
- after `auth` and before any frame;
- then whole, whenever its bytes change (latest wins).

```json
{"type":"grants","from":"a1b2c3d4","rev":3,"link":"tls","grants":[
  {"window":"wk3m9x2","name":"lobby","how":"page","path":"/w/k7m2…(26)","cannot":""},
  {"window":"wq8r4t1","name":"foyer","how":"page","path":"","cannot":"this node's key crossed the network unencrypted — the Stagewatch operator makes it a new key"},
  {"window":"wz5n6p7","name":"stage","how":"draw","path":"","cannot":""}]}
```
- `link` is `tls` (the stream is `SslStream`), `local` (D4) or `plain`.
- `path` is non-empty only when **all** of these hold:
  - `how` is `page`;
  - this link's `can` has `pages`;
  - `link` is `tls` or `local`;
  - the key is not `clearSeen`.
- `cannot` is one of:
  - `""`;
  - `this link is not encrypted — link over wss://`;
  - `this node's key crossed the network unencrypted — the Stagewatch operator makes it a new key`;
  - `this link doesn't offer pages`;
  - `this link doesn't offer window-draw`.
- `rev` rises per node whenever the list's bytes change. It is held in memory, so it restarts with the process. A receiver takes
  the first `grants` on each new link whatever its `rev`, and later ones only with a higher `rev`.

**Draw grants only**, only when this link's `can` has `window-draw`. The assignment has the shape of `WindowJson`
(`WebServer.cs:1288-1292`) under a node-link type, sent on connect and on every change:
```json
{"type":"window-assign","window":"wz5n6p7","name":"stage","content":"/viewer","hold":false,"holdText":"","holdImage":"","onLoss":"hold","fadeMs":0,"rev":12}
{"type":"window-end","window":"wz5n6p7","why":"withdrawn"}
{"type":"window-identify","window":"wz5n6p7","name":"stage"}
```
`why` is one of `withdrawn` or `deleted`.

**App → Stagewatch:**
```json
{"type":"grants-seen","rev":3,"cannot":""}
{"type":"window-shown","window":"wz5n6p7","rev":12,"content":"/viewer","hold":false,"ms":40,"error":""}
```
- `cannot` (≤ 200 chars) is why this app shows no page at all, for example "WebView2 runtime missing".
- `window-shown` is accepted only for a draw grant of that window held by this link's node. Otherwise the reply is
  `{"type":"error","error":"only a show app a window is granted to draw may say what it draws"}`.

### 2.4 The window link on a grant address

`/ws?win=<grant token>&detail=1`, otherwise unchanged. The socket's `Via` is the grant's node id.
- When a grant token stops resolving (withdraw, delete, revoke), the socket hears `{"type":"window","revoked":true,"black":true}`
  and is let go.
- With `black:true`, the shell draws plain black: no words, `data-link="gone"`, and it stops.
- An unknown token at admission still hears today's `WindowGone`. A grant token is never shown to a person, so that case is a
  stale app tab only.

### 2.5 `GET /api/windows` (read), per window, appended

```json
"drawn":1,
"grants":[{"node":"9f8e7d6c","t":"patterns","name":"Patterns FOH","how":"page","linked":true,"link":"tls","delivered":true,"open":1,"state":"shown","words":"Patterns FOH shows it; it says: Screen 2 — LED wall"}]
```
- `state` is one of:
  - `waiting`: no key-verified link;
  - `plain`;
  - `key-in-clear`;
  - `cannot`;
  - `sent`: the rev is not acknowledged;
  - `held`: acknowledged, no page open or nothing drawn;
  - `open`;
  - `shown`;
  - `stale`: the shown rev ≠ the window rev.
- A grant's path is never listed, for any credential, the PIN included.
- The existing per-window fields (`connected`, `shown`, `inStep`, `device`) are unchanged: they come from the aggregate row.

### 2.6 Context (§6)

**Items.** A `window:<name>` item's state words append the grants and the placement claims:
`showing /clock · granted to Patterns FOH (shows it) · Patterns FOH says: Screen 2 — LED wall`.
- The light is the worst over the window's opened viewers: its own address, each page grant and each draw grant's report.
- A grant held but never opened is grey.

**Links.**
- `{"from":"timer","to":"window:lobby","kind":"feeds","light":…}` only while the window shows a page (not hold, `holding` or
  `black`).
- None for `clock:*`, `go-latency` or any health item.
- Link items keep `follows` and `hears`.

**Commands** — 13 rows in cross-app-proposals' §6 notation, each a whole-form template its `VerbSyntax` rates:

| # | verb | risk |
|---|---|---|
| 1 | `note <text>` | low |
| 2 | `message <text> [warning]` | review |
| 3 | `signal standby <dept> <cue>` | review |
| 4 | `signal go <dept> <cue> \| signal go all` | high |
| 5 | `nudge\|trim\|delay …` | review |
| 6 | `next \| start \| pause` | high |
| 7 | `blackout` | high |
| 8 | `window show\|cue <window> <page or setup or holding or black>` | high |
| 9 | `window hold\|release <window or all>` | high |
| 10 | `window grant <window> <app> \| window withdraw <window or all> [<app>]` | high |
| 11 | `window identify\|reload\|revoke\|delete <window>` | high |
| 12 | `window new\|rename\|loss\|fade <window> …` | low |
| 13 | `patterns end\|restart\|play\|pause\|stop\|seek …` | high |

Peer contexts:
- `links` are read: at most 400, each end at most 120 characters.
- Items are capped at 200 (Patterns' writer cap; PlayMap's reader states the same).
- `Entry` gains `Links`.

### 2.7 Detail frame (operator only), the last block after `rooms`

```json
"windows":[{"name":"lobby","light":"green","inStep":true,"hold":false,"showing":"/clock","granted":"Patterns FOH, PlayMap Stage left","on":"Patterns FOH says: Screen 2 — LED wall"}]
```
It never carries a token or path. The public and public-detail frames carry no `windows`.

### 2.8 Hello caps

- Stagewatch adds `windows` while its web server runs.
- Patterns adds `pages` when its browser source is available.
- PlayMap adds `window-draw`.

### 2.9 Show Bus 1.3 (master `Stagewatch/docs/SHOWBUS.md`, copies in Patterns and AGMessage), subject to D0

- **§1 rule 5, the named exemption:**
  > A window's address (and Stagewatch's `?view=` and crew `?key=` links) is a page address that is itself a pass. It may ride in
  > exactly two places: a browser's address bar, and one key-verified node link (TLS or local) to the node it was granted to
  > (§12). Every other carrier treats it as a secret: no STATE, context, journal, log, show file or other URL.
- **§2:** the caps `windows`, `pages` and `window-draw`.
- **§6:**
  - a link end `@<node id>/<item id>` names another node's item, one hop. A reader that cannot place it ignores the link. A link
    whose `from` is another node's item is that node's claim, never a fact about it;
  - a reader states its own caps.
- **§12 Windows** (new):
  - the `window:<name>` item;
  - the caps;
  - grants and the node-link messages (2.3), the per-link `rev` rule, `how`;
  - the path rule (TLS or local; never to a key seen in clear);
  - the black end (2.4);
  - held forms (`stagewatch://<id>/<window id>`, `kept://`);
  - placement claims;
  - "a host browser may read the shell's state (`SWShell.state()`, the root's `data-*`) to decide when to draw it, and draws
    black, never words, until it can".

### 2.10 Patterns wire (TCP 9697 / HTTP `/api/cmd`, paired as today)

```
SCREEN <n> WINDOW <window>        -> OK … "Stagewatch lobby on Screen 2 alone — every other screen stays." (live: air and edit, own picture; only when ready)
SCREEN <n> PVW WINDOW <window>    -> staged on Screen n's PVW (StagedWords: WINDOW beside LIBRARY); opens the page
PVW WINDOW <window>               -> the programme preview
NODES ASK WINDOW <window>         -> OK "asked Stagewatch Hall A for window lobby (pending there)"
```
- `<window>` is a granted window's name (case-blind) or id.
- **Refusals** (`ERR`, nothing changed):
  - `No Stagewatch window 'x' is granted to this desk (granted: lobby, foyer) — ask for it: NODES ASK WINDOW x`
  - `Screen 2 already shows Stagewatch lobby`
  - `Stagewatch lobby isn't open here yet — SCREEN 2 PVW WINDOW lobby opens it on the preview, then SCREEN 2 TAKE`
  - `Stagewatch lobby is still opening on Screen 2's PVW — take it when the tile says ready`, the refusal for `SCREEN 2 TAKE`
  - `Stagewatch Hall A granted lobby, but over this link it sends no address: <the grant's cannot words>`
  - `Web pages need Windows with the WebView2 runtime`
  - ASK: `no Stagewatch linked` · `Stagewatch Hall A is not an approval desk` · `make a node key on the Stagewatch` ·
    `lobby is already granted to this desk`
- **The `NODES ASK` body** (POST `/api/proposals` with the node key):
  ```json
  {"from":{"t":"patterns","id":"9f8e7d6c","name":"Patterns FOH"},"target":{"t":"main","id":"a1b2c3d4"},
   "line":"window grant lobby 9f8e7d6c","reason":"Patterns FOH asks to show window lobby on its screens","risk":"high"}
  ```

**STATE**
- `web.url` is the held address (`stagewatch://a1b2c3d4/wk3m9x2`, `kept://10.0.0.5:9924/q3x7…`, `unkept://10.0.0.5:9924`). A
  private `CurrentUrl` is never published.
- `web.page` is `Stagewatch lobby`.
- The residency key is the held key.
- `bus.stagewatch` gains, last in the row:
  ```json
  "windows":[{"id":"wk3m9x2","name":"lobby","address":"stagewatch://a1b2c3d4/wk3m9x2","state":"held|opening|ready|shown|black|cannot","screens":["2"],"words":"on Screen 2 (its own picture, on air)"}]
  ```

**Context**
- The source item is labelled `Stagewatch lobby`.
- Link `{"from":"@a1b2c3d4/window:lobby","to":"source:web:stagewatch://a1b2c3d4/wk3m9x2","kind":"feeds"}`.
- Commands appended: `SCREEN <n> WINDOW <window>` high, `SCREEN <n> PVW WINDOW <window>` review, `NODES ASK WINDOW <window>` low.

**Companion module**
- Action `screen_window`: `SCREEN <n> WINDOW <name>`.
- Preset "stage, then take": `SCREEN <n> PVW WINDOW <name>`, then `SCREEN <n> TAKE`.
- Variable `stagewatch_windows`.

**Twin word** `WINDOWS` (standby → main, on change): `{"held":["wk3m9x2"]}`. Ids only, never a path. An older main ignores
an unknown word.

### 2.11 PlayMap wire (after round 3)

- **The source.** `window:<window id>` is a new kind (`SourceKind.Window`). `OUTPUT SOURCE` and `SURFACE SOURCE` take it, so there
  is no new verb; their catalogue rows' source words name it. `timer:stagewatch` is unchanged.
- **STATE** `links.stagewatch.windows` is a new block, with its fixture and card descriptor:
  ```json
  [{"id":"wz5n6p7","name":"stage","sw":"a1b2c3d4","content":"/viewer","rev":12,"shownRev":12,"drawing":"timer|clock|holding|black|loss","on":["out1"],"error":"","link":"up|down"}]
  ```
- **STATE** `links.stagewatch.timer` gains, at the end: `next, urgency, flash, flashTimer, blackout, holding, holdingName,
  holdingFill, watermark, timeOfDay, show{}`.
- **`MEDIA IMPORT` refusals** (no item added; a private address is never echoed):
  - a page or embed: `ERR pages and embeds need a browser source, which PlayMap has not built (ROADMAP L10) — send it as NDI from a browser, or have Stagewatch grant a window to this node`;
  - a Stagewatch window address: `ERR that is a Stagewatch window's private address — PlayMap draws Stagewatch windows when one is granted to this node (Stagewatch: window grant <window> <this node id>)`;
  - an https stream (`.m3u8`, `.mpd`): `ERR this node's FFmpeg is not known to open https streams — use rtsp://, srt:// or NDI`.
- **Context:** link `@<sw id>/window:stage -feeds-> <the output or surface item drawing window:wz5n6p7>`.

---

## 3. Changes, file by file

### Stagewatch (release 2.18)

**Core**
- **`src/Timer.Core/ScreenWindows.cs`**
  - **Model.**
    - `WindowGrant {Node, Kind ("t"), Name, How, Token, MadeUtc}`; `ScreenWindow.Grants` (JSON `grants`), deep-copied by `Clone`;
      `MaxGrants = 4`.
    - `ShowApp(Kind, Id, Name, Can, Verified)`, a new record: `NodeRef` stays the proposals' (`Approvals.cs:8`).
    - `WindowContext` gains `FindApp` (`Func<string, AppLookup>?`) and `NowUtc`, appended and optional.
  - **Lookup.**
    - `Find(windows, token) → (ScreenWindow, Via)?` is constant-time over every main and page-grant token.
    - `ByToken` is kept and returns `Find(...)?.Window`.
  - **`Apply`.**
    - `grant` resolves through `FindApp`, picks `how` and makes a token for page grants only.
    - `withdraw`.
    - `revoke` rotates the main and page-grant tokens.
    - `delete` drops the grants.
  - **Observed rows.**
    - `WindowObserved` gains `Via` and `Drawn`, appended.
    - New `ViewerObserved(WindowId, Via, How, Connected, Revision, Content, Hold, SwapMs, ShownUtc, Error, Device, Address)`.
  - **`ContextItems(windows, observed, viewers = null, placements = null)`.** With `viewers`, the light is the worst over opened
    viewers, and grant and claim words are added. Without them it is today's rule, so every existing caller compiles.
- **`src/Timer.Core/ShowBus.cs`**
  - `PeerContexts.Entry.Links` parsed (≤ 400, ends ≤ 120). The items cap goes 64 → 200.
  - `WindowPlacements.From(entries, ownId, holders)` (pure). It follows `@<ownId>/window:<name>` links at most two `feeds|shows`
    hops to `screen|output|surface|display` items, ≤ 8 per window. It keeps only entries that are key-verified (peers-not-screens'
    proof) and whose node is in `holders[window]` (grant held, and open or shown).
- **`src/Timer.Core/ShowState.cs`**
  - The `window` parser takes `grant` (window and app required, never `all`) and `withdraw` (window or `all` required, then an
    optional app). The `:940` default to `all` no longer applies to `withdraw`.
  - Help line (`:673-676`), error words (`:933-935`).
  - `IShowScreens.FindApp(idOrName)`, defaulting to "hears no show apps".
- **`src/Timer.Core/Approvals.cs`**
  - **The proposal fence in `Submit`:** when `nodeKeyId` is not null and the line's first two words are `window grant`, the
    line's app word must equal `from.id`. Otherwise: `a show app may ask a window only for itself`.
- **`src/Timer.Core/SnapshotJson.cs`, `ShowSnapshot`**: `Windows`, an `EquatableArray<WindowView(Name, Light, InStep, Hold,
  Showing, Granted, On)>`, written after `rooms` in the operator detail frame only.

**Show**
- **`src/Timer.Show/ShowContext.cs`**
  - `ContextInput` gains `Windows` and `Health`, appended and optional.
  - `Assess` keeps the item order: links, windows, health.
  - `Build` emits links per D11; `Commands` per §2.6.
- **FrameBuilder** copies `ambient.Windows`.
- **`src/Timer.Show/AiContext.cs`**
  - `PeerFact` gains `Can`, appended after cross-app-proposals' `Id`.
  - The copilot's own facts gain `windows`: `[{name, showing, light, granted, on}]`, so it can propose `window grant lobby
    9f8e7d6c` (high, never pre-ticked).

**Integrations**
- **`src/Timer.Integrations/Copilot.cs:228`**: `grant` and `withdraw` are listed explicitly as high.

**Remote**
- **`src/Timer.Remote/WindowDirector.cs`**
  - `Run(args, setupPath, images, findApp = null)`.
  - Identify is reachable when `Connected + Drawn > 0`.
  - Reload is reachable only through `Connected`, with the draw-only refusal (§2.1).
- **`src/Timer.Remote/WebServer.cs`**
  - **Window links.**
    - `HandleWebSocketAsync` uses `Find` and sets `WsClient.WindowVia`.
    - In `SendLoopAsync`, a miss on a client whose `WindowVia` is a grant sends `WindowGoneBlack`.
  - **Local addresses:** `IsLocal(remote)` (loopback, or an own interface address, cached and refreshed on change).
  - **Node links** (`verified:"key"`):
    - `can=` is parsed into `NodeLinkClaim.Can`;
    - `clearSeen` is set on the key when it arrives plain and non-local while TLS is offered;
    - `SendLoopAsync` sends `grants` and the draw messages when their bytes change;
    - `ReceiveLoopAsync` takes `grants-seen` and `window-shown`.
  - **Status.**
    - `WindowStatus()` keeps its shape, plus `Drawn`.
    - New `ViewerStatus()` reads `_viewerSeen[(window, via)]`, bounded at 320 and evicted per key in `PushWindows` when a window or
      grant is gone. `_windowSeen` keeps its own rule.
    - `GrantStatus()`.
  - **Pushes.** `PushWindows` also signals node links. `IdentifyWindows` also sends `window-identify` to draw links.
  - **Callers:** `HandleWindowListAsync` keeps `ToDictionary` over the aggregate and adds `drawn` and the `grants` rows.
    `ScreensWindow`, `MainViewModel.Windows`, `WindowDirector`, the rig and `WindowTests` keep compiling unchanged; the new readers
    take `ViewerStatus()`.
- **`src/Timer.Remote/wwwroot/window.js:250,268-278`**: `revoked(msg.black)`. With `black` it draws plain black, no `#gone`, sets
  `data-link="gone"` and stops.

**App (the maintainer's PC: WPF)**
- **`ViewModels/MainViewModel.Windows.cs`**
  - `FindApp` joins node links, contexts and hellos.
  - Journal rows: "Window lobby granted to Patterns FOH (9f8e7d6c) — its page", "Window lobby: Patterns FOH took its address"
    (once per grant), "Window lobby withdrawn from Patterns FOH — that address stopped". Never an address.
  - `WindowViews()` feeds the frame. `ContextItems` gets `ViewerStatus()` and the claims.
- **`ViewModels/MainViewModel.ShowBus.cs:53-67`**
  - `LinkItems()` returns links only. Windows and health go apart into `ContextInput`.
  - Placements come from `_peerContexts` and the Patterns line link's context, with the holders.
- **`ViewModels/MainViewModel.Desk.cs:99`**: for a `window grant` line, `Request` is the line, never the reason.
- **`ViewModels/MainViewModel.Systems.cs:173`**: `Windows = WindowViews()` beside `Rooms`.
- **`ViewModels/MainViewModel.Slides.cs:34-39`**: the `windows` cap while the server runs.
- **`ScreensWindow.xaml(.cs)`**
  - A **Granted to** column, with a shape beside every colour.
  - Right-click **Grant to ▸** and **Withdraw ▸**, in step with a **Grant…** button.
  - The subtitle still counts `observed.Connected`.
- **`MainWindow.xaml:691-721`**: the Show network row's right-click gets **Grant a window ▸**.
- **`Help/HelpContent.cs`**: a tip on grants.

**Companion module (`companion-module/src`)**
- `actions.ts`: `window_grant` (window, app, grant or withdraw) sends `WINDOW GRANT|WITHDRAW …`.
- `state.ts` parses `windows`.
- `feedbacks.ts`: `window_state` and `window_holding`.
- `variables.ts`: `windows_open`, `windows_attention`.
- `presets.ts`.

**Rig (`tools/Stagewatch.Rig/Program.cs`)**
- `FindApp` from node links and posted contexts. `WindowCommand` (`:242-246`) passes it.
- `Frame()` (`:258-295`) sets `Windows = WindowViews()`.
- `Context()` (`:296-301`) passes windows, health, viewers and placements apart.
- `--state` keeps the grants. The rig never prints a token.

**e2e (`tests/e2e/package.json`)**: devDependency `"ws": "8.18.0"`, shared with peers-not-screens' `nodes.e2e.cjs`; whichever
lands first adds it.

**Papers**
- `docs/SHOWBUS.md` 1.3: §1.5's two carriers, §2, §6, §12.
- `docs/REMOTE.md` "2.18: windows for show apps":
  - the grammar, settings and node-link messages;
  - `/api/windows` grants;
  - the black end, including on revoke: "the app's screen is black, then the window's page, within about a second";
  - D0's answer;
  - "no measurement this round" for tombstones.
- `docs/COMPANION.md`; `docs/FEATURES.md` and `docs/NODES.md` (page or draw, per app).
- `docs/WHATS_NEXT.md`: G46, an HTTP grants read, D0's answer.

### Patterns (numbered by patterns-playmap-side's reconciliation: PT-W1 in Round 89.2–89.3, the rest in Round 92; the maintainer's numbering wins)

**Core**
- **`src/Patterns.Core/Services/WindowAddresses.cs` (new, pure)**
  - Held forms: `Granted(swId, windowId)`/`TryGranted`; `Kept(host, port, id)`/`TryKept`; `Unkept(url)`.
  - `IsStagewatchWindowUrl`: a path of `/w/` plus 26 of `abcdefghijkmnpqrstuvwxyz23456789`.
  - `KeptId`: 12 base32 characters of SHA-256 over the normalised URL.
  - `PublicForm`: `scheme://host[:port]`. `IsHeld`.
  - `Scrub(text)`: every window path in free text becomes `/w/…`, for log lines.
  - `WindowCertificate.Decide(held, pinned, presented) → Allow|Refuse|Default`.
  - `ShellState.Parse(json)`, a `record struct`: rev, link, showing, hold, loading, error. `Ready` and `Withheld` follow D8 and D9.
- **`src/Patterns.Core/Media/InputKeys.cs:11-15`** and **`MediaLocator.cs:209-210,237-238`**: a raw Stagewatch window URL publishes
  as `WindowAddresses.Unkept(url)` (D5's fence).
- **`src/Patterns.Core/Model/ShowState.cs`**
  - `WebConfig.Passes` (`ShowCollection<WebPassConfig{Id, Label, Secret}>`, ≤ 64), appended.
  - `StagewatchLinkConfig.Grants` (`ShowCollection<WindowGrantConfig{Sw, Window, Name, How, Base, Secret}>`, ≤ 64), appended, in
    the local `Control` section.
- **`src/Patterns.Core/Services/Twin.cs`**
  - `CarriesSecrets` includes `Web` (`:398`).
  - `OwnSecrets`/`KeepSecrets` (`:404-407`) keep a standby's own passes where the wire is blank.
  - New `TwinWord.Windows`.
- **`src/Patterns.Core/Services/SuperCheck.cs`** gains two rows:
  - "Screen 2's page is a Stagewatch window's address written raw into the show — paste it again to keep it as the show's secret";
  - "Stagewatch lobby is on air (Screen 2) and granted to this desk, not to its standby FOH-B — a takeover shows black there:
    Stagewatch → window grant lobby <standby id>".
- **`src/Patterns.Core/Services/WebAddress.cs:16-26`**: `ShortName` of a held address is "Stagewatch window" or the kept host.
- **`src/Patterns.Core/Services/StagewatchFollow.cs`**
  - `StagewatchFrame.Grants(json)` validates: `from`, `rev`, ≤ 64 grants, `path` matching the window path or `""`, `how`, `cannot`
    ≤ 200.
  - `GrantsSeen(rev, cannot)`.
  - The brief's `Take(12)` (`:281`) becomes `Take(32)`, plus a granted-windows line.
- **`src/Patterns.Core/Services/ControlProtocol.cs`**
  - `SCREEN n WINDOW` beside `PATTERN` (`:461-466`); `WINDOW` in `StagedWords` (`:215-230`); `NODES ASK WINDOW`.
  - New kinds `ScreenWindow`, `ScreenStageWindow`, `NodesAskWindow`.
- **`src/Patterns.Core/Services/ShowBusNodes.cs:217-226`**: `DeskProposals.Ask(fromId, fromName, swId, line, reason)` with target
  `{t:"main"}`.
- **`src/Patterns.Core/Services/ShowContext.cs`**
  - `FromEye` (`:189`) keeps a borrowed `ShowAppItem` → own node edge as `@<app id>/<item id>`.
  - Three command rows.
- **`src/Patterns.Core/Services/Eye.cs:772-820`**
  - A linked app's items always keep those a local source holds (forced, ≤ 8), then the 12 by light.
  - Link `app:<id>:window:<name> -feeds-> source:<key>`.
- **`src/Patterns.Core/Services/ShowBus.cs:25`**: the beacon adds `pages` when available.

**Rendering**
- **`src/Patterns.Rendering/Media/VideoService.cs:75-105`**: `IWebSource` gains `bool Withheld => false;` (a default member).
- **`src/Patterns.Rendering/Patterns/MediaAndParticlePatterns.cs:139-156`**: for a held `WebUrl`, black when `page is null`, when
  `Withheld`, or when `DrawInput` fails. Never `PlaceholderCard`.
- **`src/Patterns.Rendering/LayerRenderer.cs:143-155`**: a held web layer with no picture is black with no status words.

**App**
- **`src/Patterns.App/Services/WebPasses.cs` (new)**
  - It holds an immutable book (Interlocked swap): the link's grants plus `Control.Stagewatch.Grants`, and `Web.Passes`.
  - `Resolve(address) → (Url?, PinKey, Why)`.
  - `SetGrants(swId, base, list)` persists the grants. Only a received list removes; a disconnect changes nothing.
  - `Secure(typed, keep)`: Stagewatch window paths always; others only with `keep`.
  - `Label`, `Changed`.
- **`WebFrameSource.cs`**
  - **Resolution:** `NavigateCore` (`:1389-1404`) resolves held addresses. Unresolved means no navigation; the source stays
    `Withheld`, with words.
  - **The shell observer:** for a resolved Stagewatch page, a document-created observer script, `WebMessageReceived` taken only
    from the resolved origin, and `ShellState`.
  - **Reads and logs:** `CurrentUrl` (`:749`) returns the held address; logs name the held address only.
  - **Certificates:** `ServerCertificateErrorDetected` applies `WindowCertificate.Decide` against `Control.Stagewatch.Pins`.
- **`WebEngine.cs`**: a `Resolve` seam beside `SourceFactory` (`:81`). On `WebPasses.Changed`, mounted held pages whose resolution
  moved are navigated again.
- **`StagewatchLinkService.cs`**
  - **Dialling:** `can=pages` on the node link. `Resolve` (`:225-241`) dials `127.0.0.1` when the hello's host is this machine.
  - **Grants:** `ReceiveAsync` handles `grants` (per-link rev), then `SetGrants`, then `grants-seen` on the send gate.
  - **The approved-line log** (`:497`) goes through `WindowAddresses.Scrub`.
  - **Asking:** `Ask(window)` posts via `DeskProposals.Ask` and is followed in `DeskApprovals`.
- **`TwinService.Standby.cs` / `.Main.cs`**: the standby sends `WINDOWS` on change; the main keeps it for the Super Check.
- **`WebActions.cs:45-51`**: `WEB OPEN` values pass through `Secure`.
- **`LibraryItems.cs:100-110,211-224`**
  - Grant tiles: `stagewatch:<sw>/<w>`, Section Web, Category "Stagewatch windows", Name "Stagewatch <name>".
  - `WebTitle` of a held address is its label.
- **`ShowActions.Content.cs:168-195`**
  - `ScreenWindow`: live, own picture, Visibility and Effect; refused when not ready or already shown.
  - `ScreenStageWindow` goes through `Stage`.
  - `ScreenTake`'s check for a staged held page that is not ready.
  - `NodesAskWindow`.
- **`ViewModels/MediaPage.cs:160-197`, `ViewModels/CueEditor.cs:928`**
  - Typed addresses go through `Secure`.
  - The Media page gets a **Keep as the show's secret** check, and the status line says when an address was kept.
- **`ViewModels/RunViewModel.cs`**: a chip, "WINDOW lobby: shown / opening / black — withdrawn", raised on change only.
- **`CommandRouter.cs`**
  - `WebRow` (`:294-316`): the held url.
  - The residency key (`:706`).
  - `ShowBusRow` (`:753-779`): `stagewatch.windows`, last.
- **`DeskMenuFacts.cs`, the Nodes section, `AppServices.cs:930`**
  - The screen tile's **Stagewatch window ▸** (live when ready, else "open on PVW").
  - The Eye item's **Ask for it** / **Show on ▸**.
  - The Nodes page lists grants and their `cannot`.
  - The context builder gets the window facts.

**Companion module (`integrations/companion-module-patterns`)**
- `src/actions.js`: `screen_window`, plus the stage-then-take preset.
- `src/variables.js`, `src/state.js`: `stagewatch_windows`.
- `lines.txt`: `SCREEN 2 WINDOW lobby`, `SCREEN 2 PVW WINDOW lobby`, `NODES ASK WINDOW lobby`.

**Papers** (per round)
- `docs/PLAN.md` §NN, `docs/REVIEW.md`, `CHANGELOG.md` and `README.md` (Library's Stagewatch windows, `SCREEN n WINDOW`, the
  secret check).
- `docs/REMOTE.md` (the verbs, STATE, held addresses), `docs/COMPANION.md`, `docs/SHOWBUS.md` copy 1.3.
- `docs/OPEN.md`:
  - L40 amended: passes join the plain-JSON class;
  - L36 cited: passes ride the plain twin link under `SendSecrets`;
  - a new row for the unpaired STATE greeting.
- `docs/DRILL.md` scenario 23: a takeover with a granted window and a kept pass on air.
- `docs/QUALIFICATION.md` rows:
  - a §22 walk: an https window in WebView2 against a pinned Stagewatch; the injected observer under the window's CSP; revoke,
    withdraw and restart going black;
  - §27.
- The tag table row for the previous round.

### Mapper / PlayMap (a post-round-3 package, `wp4-showbus-windows` proposed, or fix branches with the owner's leave)

**Spine commits (§5.7, `Spine:` subjects on the package branch, reviewed with it)**
- `src/PlayMap.Core/Model/Content.cs`: `SourceKind.Window`, appended.
- `src/PlayMap.Core/Wire/VerbCatalog.cs` (outside blocks): the source words of `OUTPUT SOURCE` and `SURFACE SOURCE`.
- `src/PlayMap.Engine/StateWriter.cs`: `links.stagewatch.windows`.
- `src/PlayMap.Engine/ContextBuilder.cs`: the `@<sw>/window:<name>` link.

**Core**
- `src/PlayMap.Core/Media/PrivateAddress.cs` (new): the Stagewatch window path rule.
- `src/PlayMap.Core/Bus/ShowBus.cs:71`: `PlayMapCaps` gains `window-draw`.
- `src/PlayMap.Core/Bus/NodeSettings.Windows.cs` (new partial): `HeldWindows` (`ImmutableArray<HeldWindow(Sw, Id, Name, Content,
  Hold, HoldText, HoldImage, OnLoss, FadeMs, Rev)>`, ≤ 16), no secret.

**Net**
- **`src/PlayMap.Net/Links/StagewatchTimer.cs`**: `HoldingFill`, `FlashTimer`, `TimeOfDay` (appended). `StagewatchGrant` and
  `StagewatchWindow` parse §2.3.
- **`src/PlayMap.Net/Links/StagewatchLink.cs`**
  - `can=window-draw` on the node link.
  - `FromPeer` (`:63-75`) dials `127.0.0.1` when the hello is this machine's.
  - `Take` (`:372-440`) handles `grants` (draw only, path ignored and never stored, per-link rev), `window-assign`, `window-end`
    and `window-identify`, and replies with `grants-seen`.
  - `ReportShown` goes on a bounded latest-wins outbox, sent by the beat task.
  - `HoldingImageAsync`: `GET /api/holding/<id>` on the pinned client, ≤ 8 MB, ≤ 8 cached.

**Engine**
- **`Host/NetworkHost.cs`**
  - The G12 fields at the end of `StagewatchTimerJson` (`:318-341`).
  - `StagewatchWindowsJson`.
  - The redirect words and `_stagewatchView` are retired.
  - `window-identify` overlays the window's name inside its own picture for 5 s; it never runs `OUTPUT IDENTIFY`.
  - Held windows are persisted to `NodeSettings` on change.
- **`Rendering/TimerView.cs`**
  - Draws the watermark always, blackout, holding (image fit or fill, else the name), urgency colours, `next` and the `show{}`
    flags.
  - The flash phase comes from leader time (`(leaderNs − flashStartNs) / period`) at 1 s or 0.33 s. Both pictures are pre-drawn,
    and `Acquire` picks one.
  - Modes `Timer|Clock|Holding|Black|Loss`.
- **`Rendering/NativeWindows.cs` (new)**
  - One view per held window. `Acquire(id, leaderNs)` always returns a frame: black for an unknown or ended window, the loss
    state while the link is down.
  - `Classify(content)`:
    - `/viewer*` → Timer, honouring `for=speaker` only;
    - `/clock*` and `/studio*` → Clock, honouring `h`, `date` and `timer`;
    - `/holding*` and `holding` → Holding;
    - `black` → Black;
    - else unsupported.
  - Unhonoured flags (`tz`, `mirror`, `delay`, `style`, `scene`…) go into `error`. Unsupported pages report an error and keep the
    old picture.
  - `Acquire` marks the rev taken; `Shown` is raised off the render thread.
- **`Rendering/EngineSources.cs:166-182`**: `case SourceKind.Window: return _windows.Acquire(source.Id, leaderNs)`.
  `SourceKind.Timer` is unchanged.
- **`EngineCore.Content.cs:388-405,447,508-528`** and **`Host/MediaBackend.cs:381-382`**: the import refusals (§2.11), and honest
  readiness, LIST and FETCH for existing Web and Embed items.
- **`EngineCore.Transport.cs:136`**: `Window` joins the duration-less kinds.

**App and Assistant**
- `src/PlayMap.App/State/StateSnapshot.cs:213,289-292` and `src/PlayMap.Assistant/StateExplainer.cs:263-276` read the new fields
  and block.
- `src/PlayMap.App/ViewModels/MapInspectors.cs:80,110-120`: a "Stagewatch window" kind, the source menu's **Stagewatch window ▸
  stage**, and the output and surface right-click menus in step.

**Fixtures and papers**
- Fixtures regenerated with `PLAYMAP_WRITE_FIXTURES=1`, plus a card descriptor for `links.stagewatch.windows`.
- `docs/SHOWBUS-NODE.md` §2, §3 and §5.
- `docs/WIRE.md`; `docs/ROADMAP.md` (L10, and an FFmpeg TLS probe as next); `docs/round2/CARRYOVER.md`.

### AGMessage
- `docs/SHOWBUS.md` copy to 1.3 and a `CHANGELOG.md` line. No code: AGMessage holds no window, and its context reader ignores
  links (`AGMessage/app/lib/context.js:102-117`).

---

## 4. Tests

**Stagewatch, pure (`tests/Stagewatch.Tests/WindowTests.cs`, `ShowBusTests.cs`)**
- **Grammar.**
  - `window grant lobby 9f8e7d6c` and `window withdraw all` parse.
  - `window grant all x`, `window grant lobby` and a bare `window withdraw` are refused with words.
- **Grant.**
  - `pages` gives `page` with a valid, distinct token; `window-draw` gives `draw` with no token; neither is refused.
  - Unknown, ambiguous, duplicate and fifth grants are refused, each leaving the list by reference.
- **Revoke, delete, Find.**
  - `revoke` rotates the main and page-grant tokens, and the old ones stop being `Find`-able.
  - `delete` drops the grants.
  - `Find` returns `Via`; `ByToken` is unchanged.
- **Load.** A bad token, a draw grant with a token, a duplicate node, a fifth entry or an unknown `how` is dropped.
- **ContextItems.**
  - With no viewers, the result is identical to today (existing tests unchanged).
  - With viewers: a grant never opened is grey; a TV green with a grant viewer on the old rev is amber.
  - Claim words read "… says: …".
- **Proposal fence.**
  - A node key bound to `pat1` proposing `window grant lobby pat2` gets the refusal; `window grant lobby pat1` is taken.
  - The PIN proposing for any app is taken.
- **The rest.**
  - Copilot gives grant and withdraw `High`.
  - G23: a window on `/clock` gets one `timer -feeds-> window:lobby`; held, black, `clock:patterns` and `go-latency` get none.
  - The 13 rows rate through cross-app-proposals' `VerbTable`: `window grant lobby x` is high; `window rename lobby x` is low;
    `pause` is high; `nudge 30` is review.
  - `PeerContexts` keeps 400 links and 200 items.
  - `WindowPlacements` follows two hops; it ignores an unverified context, a non-holder's link and a holder with no open viewer;
    it stops at 8.

**Stagewatch, real server (`WindowTests.RunWeb`, real `WebServer`, `ClientWebSocket`)**
- **Delivery.**
  - A key-verified node link with `can=pages` over loopback gets `auth`, then `grants` with the path, before any frame.
  - With `can=` lacking `pages`: `path:""` and `cannot` "doesn't offer pages".
  - Over a plain non-local remote (a test hook): `link:"plain"` and `path:""`.
  - After the same key once arrived plain while TLS was offered: `key-in-clear`, even over TLS.
  - A control-key link claiming the node, and another node's key, get no `grants`.
- **States and status.**
  - `grants-seen` makes the state `held`; a grant-path window link makes it `open`, then `shown` after its report.
  - `WindowStatus()` stays one row per window with two viewers; `/api/windows` answers 200 with two viewers.
  - `ViewerStatus()` has a row per via.
  - 400 grant churns leave `_viewerSeen` ≤ 320 and the lights never flicker to grey.
- **No path anywhere.** For the PIN, control and read credentials, `/api/windows`, `/api/context` and every frame never contain a
  grant token; the main path is still listed for the PIN.
- **Draw.**
  - The draw link gets `window-assign` and never `identify` or `reload`.
  - Its `window-shown` sets the viewer row; the same from a page grant is refused.
  - `window identify lobby` reaches it as `window-identify`.
  - `window reload lobby` with only the draw app is refused with the draw words.
- **Withdraw and revoke.**
  - The grant link hears `revoked`+`black` on withdraw **and on revoke**.
  - The main-address link on revoke hears today's message.
  - The app hears `grants` with the new path (revoke) or without the window (withdraw).
- **Frames.** The operator detail frame has `windows`; the public frame has none.

**Stagewatch rig e2e (`tests/e2e/grants.e2e.cjs`, Playwright and `ws`; rig with `--node-keys 2`)**
1. A Patterns-like peer dials `node=pat00001&t=patterns&can=pages` and posts its context.
2. `window new lobby /viewer` and `window grant lobby pat00001` return `ok`. The peer gets `grants` and acknowledges it.
3. Playwright opens the grant path: `data-showing=/viewer`; `/api/windows` grant `shown`; the context item is green.
4. The peer posts `@<rig id>/window:lobby -feeds-> source:…` and `source:… -feeds-> screen:2` ("Screen 2 — LED wall"). The frame's
   `windows[0].on` reads "Patterns FOH says: Screen 2 — LED wall". A second keyed peer with no grant posting the same link changes
   nothing.
5. A PlayMap-like peer (`can=window-draw`) is granted. It gets `window-assign` and no path. After `window-shown` its grant is
   `shown`; `window show lobby /clock` makes it `stale` until its next report.
6. `window revoke lobby`: the page shows black (`data-link=gone`, no `#gone` text visible); the peer hears its new path. Then a
   `page.reload()` on the old path, and a new window page from the same address loads (the D15 guard).
7. `window withdraw lobby pat00001`: black, the list empties, and no lockout.
8. **Fence:** the rig's stdout and every captured frame, context and `/api/windows` body never contain a grant token.

**Stagewatch, Windows (UiSmoke, `Bench.OnWindows`)**: the Granted-to column, Grant ▸ / Withdraw ▸ and the button; the Show network
row's Grant a window ▸.

**Stagewatch module** (`companion-module/test/module.test.mjs`): `window_grant` lines; the feedbacks and variables from a detail
frame's `windows`.

**Patterns, Core** (`WindowAddressesTests`, `ShowBus12Tests`, `EyeTests`, `SecretsTests`, `TwinTests`, parser tests)
- **Addresses.**
  - Classification, kept-id determinism, `ShortName`, `Scrub`.
  - `InputKeys.Web(raw window URL)` is `web:unkept://10.0.0.5:9924`, and so is `MediaLocator`'s target.
  - `ShellState` readiness truth table: rev 0, gone, loading, ok.
- **Grants parse.** `StagewatchFrame.Grants` rejects a bad path, a 65th grant and a wrong `from`.
- **Certificates.** The `WindowCertificate.Decide` truth table.
- **Secrets and the twin.**
  - `Secrets.Redact` masks `Web.Passes[].Secret` and `Control.Stagewatch.Grants[].Secret`.
  - `Twin.WireJson(sendSecrets:false)` blanks passes; `KeepSecrets` keeps the standby's own.
  - `WireJson` never contains `Control`.
- **The Eye and the context.**
  - A forced window item outside the 12.
  - `FromEye` writes `@a1b2c3d4/window:lobby`.
- **Parser.** The three verbs; `SCREEN 2 WINDOW` alone is Unknown.

**Patterns, App headless** (`ShowBus12AppTests` with a scripted Stagewatch, `LibraryWebTests`, `WebAppTests`, `TwinAppTests`, new
`PrivateAddressFenceTests`)
- **Grants.**
  - `grants-seen` echoes the rev. A second link's `rev:1` is taken (per link).
  - The link dropped: the grant is kept and the page stays mounted. A list without the window removes it.
  - A restart resolves at once from `Control.Stagewatch.Grants`.
- **Readiness.** With a fake page whose shell state is `rev 0`:
  - `SCREEN 2 WINDOW lobby` is refused with the PVW words, and nothing changed;
  - `SCREEN 2 PVW WINDOW lobby` stages it;
  - with the shell state ready, `SCREEN 2 TAKE` is Done with Visibility and Effect;
  - STATE `web.url` is held, and `bus.stagewatch.windows[0].screens == ["2"]`;
  - the Run strip chip reads "shown".
- **Withdraw.** An empty list leaves the fake page `Withheld`; the output pixels are black, with one journal row.
- **Fence.** With a granted window on air, a kept pass on a layer and an approved `WEB OPEN http://10.0.0.5:9924/w/<token> ON …`
  line run, the token appears in none of:
  - STATE, CONTEXT, EYE;
  - the journal file, the log;
  - a built support bundle;
  - the module's recorded lines;
  - `InputLabels` after a label is set on that input;
  - the twin's wire with `SendSecrets` off.

  The settings file holds it only under `Secret`. A raw window URL written into a loaded show draws black, publishes `unkept://`,
  and lights the Super Check row.
- **Twin.**
  - A standby without the grant reports `WINDOWS {"held":[]}`, and the main's Super Check row names lobby.
  - After takeover the standby's own grant resolves; without one, black and words.
- **ASK.** `NODES ASK WINDOW lobby` posts the §2.10 body and follows its state.

**Patterns, Rendering**
- A held address with no page, `Withheld`, a failed `DrawInput` and a held layer are all-black.
- There are no text pixels in any of them.

**Patterns module** (`npm test`): `lines.txt` has the three lines; `stagewatch_windows` comes from a STATE fixture.

**PlayMap**
- **Net (`tests/PlayMap.Net.Tests/StagewatchLinkTests.cs`, `FakeStagewatch`).**
  - `grants` (draw), `window-assign`, `window-end` and `window-identify` are raised and acknowledged with `grants-seen`.
  - A page grant's path is ignored and never stored.
  - `can=window-draw` is on the asked line.
  - One `window-shown` per rev.
  - A dial to a same-machine hello uses `127.0.0.1`.
- **Engine, Rendering (`TimerView`, `NativeWindowsTests`, `EngineSourcesTests`).**
  - **The timer.**
    - The G12 fields come at the end, with the fixture regenerated.
    - The watermark shows in blackout, unknown and every mode.
    - The flash phase is a function of `leaderNs` alone: two engines at the same leader time draw the same phase.
  - **Window sources.**
    - `window:wz5n6p7` unknown is black, never the timer.
    - After a restart with `HeldWindows`, it draws the loss state before the link is up.
    - After `window-end`, black.
    - `Classify` reports `tz` and `mirror` as an error.
    - `Acquire` never returns null for `Window`.
  - **Identify.** `window-identify` overlays the name only on outputs drawing that window, and runs no `OUTPUT IDENTIFY`.
- **Content.**
  - `MEDIA IMPORT` of a `/w/<token>` URL fails, adds no item, and the reply and journal hold no token.
  - A page gives the L10 words; an https `.m3u8` gives the FFmpeg words and adds no item.
  - An existing Web item's LIST is `ready:false`.

**AGMessage**: `npm test` unchanged (a document-only unit).

---

## 5. Units, in order

| # | repo | unit | gaps | depends on |
|---|---|---|---|---|
| SW-W1 | Stagewatch | Context: links per D11, health apart, 13 rows in §6 notation, peer `links` parsed (≤ 400; items ≤ 200), `WindowPlacements` with the holder and verified rule (pure), `windows` hello cap, `AiContext` windows facts + `PeerFact.Can` | G23, G24, G22 | cross-app-proposals unit 1 (`VerbSyntax`), peers-not-screens SW-1 (proof on entries) |
| SW-W2 | Stagewatch | Grants in Core: model, `ShowApp`, parser (explicit `all`), `Apply`, revoke and delete, `Find`/`Via`, settings, copilot rows, help, `FindApp`, `WindowDirector.Run(findApp)`, the proposal fence | G10 | — |
| SW-W3 | Stagewatch | WebServer: grant tokens and `Via`, black end on every grant-token miss, `IsLocal`, `clearSeen`, `can=`, `grants`, the `window-*` messages, `grants-seen`, `ViewerStatus` (bounded per key) beside an unchanged `WindowStatus` + `Drawn`, identify and reload rules, `/api/windows` grants, `window.js` black end | G10, G11 | SW-W2, peers-not-screens SW-1 and SW-3, **D0 (maintainer)** |
| SW-W4 | Stagewatch | Lights and words: `ContextItems` with viewers and claims; detail-frame `windows`; view model `FindApp`, journal rows, the desk headline; Screens column and menus; Show network menu (PC) | G22, G24 | SW-W1, SW-W3 |
| SW-W5 | Stagewatch | Rig (`FindApp`, `Frame().Windows`, placements, `--state`), `ws` devDependency, `grants.e2e.cjs` | G10, G11, G24 | SW-W4, peers-not-screens SW-5 |
| SW-W6 | Stagewatch | Companion module: action, feedbacks, variables, presets | G22 | SW-W4 |
| SW-W7 | Stagewatch | Papers: SHOWBUS 1.3, REMOTE 2.18 (with D0's answer), COMPANION, FEATURES, NODES, WHATS_NEXT | G22 | SW-W1…W6 |
| PT-W1 | Patterns | Round 89.2–89.3: held addresses: `WindowAddresses`, the `InputKeys`/`MediaLocator` fence, `Web.Passes` on `Secrets` and the twin, `Secure` at every entry, the resolver, `CurrentUrl` and logs (approved lines scrubbed), `Withheld` and black in every branch, the shell observer, public `web.url`, the raw-address Super Check row, `PrivateAddressFenceTests` | G09 | — |
| PT-W2 | Patterns | Round 92.2: grants from the link: `can=pages`, the `pages` cap, loopback dial, per-link rev, persisted grants, certificate decision, Library tiles, `SCREEN n WINDOW` (ready only) / `PVW WINDOW` / `TAKE` check, STATE `bus.stagewatch.windows`, the Run strip chip, menus, Nodes page, module | G10 | PT-W1, SW-W3, peers-not-screens PT-1 |
| PT-W3 | Patterns | Round 92.3: the Eye chain, `FromEye` `@` links, brief cap 32, `NODES ASK WINDOW` | G24, G23 | PT-W2, SW-W1 |
| PT-W4 | Patterns | Round 92.4: the twin: `WINDOWS` word, the standby Super Check row, DRILL scenario 23 | G10 | PT-W2 |
| PT-W5 | Patterns | Round 92 papers: PLAN, REVIEW, CHANGELOG, README, REMOTE, COMPANION, SHOWBUS copy, OPEN, DRILL, QUALIFICATION §22/§27, the tag row | — | PT-W1…W4, SW-W7 |
| PM-W1 | Mapper | Import refuses pages, embeds, private addresses and https streams with the reason; honest readiness, LIST and FETCH | G47 | round 3's integration pass |
| PM-W2 | Mapper | Timer source: the G12 fields, watermark, blackout, holding with fetch, flash from leader time, urgency, next, `show{}`; readers; fixtures; redirect words retired | G12 | round 3's integration pass; peers-not-screens PM-1 |
| PM-W3 | Mapper | Native windows: Spine commits (`SourceKind.Window`, catalogue words, `StateWriter`, `ContextBuilder`), `window-draw` and `can=`, the link messages, loopback dial, `NativeWindows` (fail-closed, loss state, persisted `HeldWindows`, flag honesty), `window-identify` overlay, STATE block and cards, the `@` link, desk menus | G11 | PM-W2, SW-W3 |
| PM-W4 | Mapper | Papers: SHOWBUS-NODE §2/§3/§5, WIRE, ROADMAP (L10, FFmpeg TLS), CARRYOVER, module fixtures | — | PM-W1…W3, SW-W7 |
| AG-W1 | AGMessage | SHOWBUS.md copy 1.3, CHANGELOG | G22 | SW-W7 |

**Degrading safely when only one side lands**
- **An older Stagewatch** ignores `can=` and never grants. Patterns keeps kept passes (black on any failure); PlayMap keeps its plain
  timer.
- **Against a 2.17 shell**, Patterns' observer still works: `SWShell.state()` and the `data-*` attributes exist since 2.17. A revoked
  kept window may show its notice for the moment between the shell's paint and the host message (a frame or two). Grant addresses
  end in black on 2.18.
- **An older Patterns or PlayMap** sends no `can=` and no new cap, so a grant to it is **refused** with "offers neither pages nor
  window-draw". Its operator hears it at the Stagewatch.
- **AGMessage** ignores links.

---

## 6. Gaps

**Closed**
- **G09.**
  - No published artefact carries a pass: the key is held, a raw window address is inert, and kept passes are on `Secrets`.
  - A fence test covers every exit, approved lines and `InputLabels` included.
- **G10.**
  - Grants are delivered over the key-verified link, never to a key seen in clear.
  - Pages open over pinned TLS or local links.
  - Placement is ready-gated, with no blank air.
  - Grants survive link loss and restarts.
  - Stagewatch observes held, open and shown per grant.
- **G11.** PlayMap holds a window natively with no token, through node-link-only messages. Unknown or withdrawn windows draw black,
  and loss states draw locally.
- **G12.** Every dropped field is carried and drawn. The watermark is always shown, and the flash follows leader time.
- **G22.** §12 and its carriers in the spec and its copies; caps; the detail frame's `windows`; Companion feedbacks.
- **G23.** Links, health and machine-readable grouped commands; the reader cap raised.
- **G24.** The §6 cross-node link end; Patterns' Eye chain; Stagewatch's placements as claims from holders.
- **G47.** Import refuses with the reason; private addresses are never kept or echoed; readiness is honest.

**Left**
- **G46:** D18. A Stagewatch page drawing peers' contexts is noted in WHATS_NEXT, with no measured ask.
- **G09 remainder:** Patterns still greets unpaired wire peers with STATE (PLAN §103.7). Nothing secret rides it now, but §1 rule 4
  says it should not. It becomes a new OPEN row.
- **G47's https streams:** filing https HLS/DASH as Stream needs a probe of the FFmpeg build's protocols. Until then they are
  refused with the reason (a ROADMAP note).
- **L10:** PlayMap still has no browser. Native drawing covers `/viewer`, `/clock`, `/studio`, `/holding`, holding and black, and
  says which flags it does not draw.
- **L36/L40:** kept passes ride the plain twin link under `SendSecrets` and sit in plain JSON, the same class as a box's password.
  Their fixes (a sealed twin link, DPAPI) are those rows' own.
- **The stand-by's grant:** a twin needs a second grant. The Super Check names it; a "grant to the pair" convenience is not built.

---

## 7. Risks

- **The maintainer gate (D0).** Until it is answered, SW-W3 and everything after it wait. SW-W1, SW-W2 and PT-W1 do not.
- **Dependencies.**
  - peers-not-screens' key-verified links (SW-1, SW-3, PT-1, PM-1).
  - cross-app-proposals' `VerbSyntax` before SW-W1's rows. Without it the grouped rows are unmatchable; if it slips, SW-W1 lands one
    plain template per row instead.
- **WebView2 is unproven off a real machine:**
  - the certificate exception must cover the page's `wss`;
  - the document-created observer must run under the window's CSP;
  - `WebMessageReceived` must reach the host.

  CI cannot construct WebView2 (OPEN L26), so these are §22 walk rows, `unwalked` until filled. A same-machine show avoids TLS.
- **The 4-page cap.** A window takes one of Patterns' 4 browsers. On the field machine the screencast is refused (L10), so windows
  live on the 20 fps screenshot poll: enough for a clock.
- **More frames.** A `windows` block publishes a frame on window events, as rooms do. This is bounded by swaps and connects.
- **A behaviour change.** A window's light becomes the worst over its viewers, so a TV and an app on different revs read amber.
  This is intended and stated in REMOTE.
- **`clearSeen` could surprise.** A peer that once dialled plain while TLS was offered keeps getting no path until its key is
  replaced. The grant row says so, with the fix.
- **Merge conflicts.** `ShowContext.cs`, `AiContext.cs`, `MainViewModel.ShowBus.cs`, `WebServer.HandleWebSocketAsync`,
  `Approvals.cs` and `SHOWBUS.md` are shared with playout-links, peers-not-screens and cross-app-proposals. Each lands in merge
  order, and SHOWBUS 1.3 has one owner.
- **Mapper round 3.** The PM units wait for the integration pass, or go as fix branches with the owner's leave. The spine changes
  are `Spine:` commits.
- **Native drawing is a subset.** A PlayMap on `/layout` or with unhonoured flags stays amber, honestly. The Grant ▸ menu says what
  `window-draw` apps draw.
- **WPF parts** (the Screens column and menus, the Show network menu, UiSmoke) are built and walked on the maintainer's PC; a cloud
  session names them and leaves them.

---

## 8. Process notes

**STAGEWATCH (CLAUDE.md)**
- **Grammar and risk.** No new `ShowCommandKind`: grant and withdraw extend `Window`. They still need parser words, a help line, an
  explicit `Copilot.Classify` row (High) and `ShowContext.Commands` rows.
- **Appended fields.** New fields go at the end of their records and objects: frame JSON, `/api/windows`, `WindowObserved`,
  `ContextInput`, `PeerFact`, ApiKey JSON.
- **One writer.** The show thread stays the only writer: grants change only through `WindowDirector.Run`. Sends go on the pool.
- **No secrets anywhere.** No address, grant path or key appears in a log, journal row or frame. The rig prints none.
- **Tests.** They assert fields and published state, on injected clocks. The lanes (`tools/test.sh`) are build, unit, module and
  e2e; holo is untouched.
- **Windows-only checks** go through `Bench.OnWindows`.
- **D0.** The doctrine line in CLAUDE.md and the §1 rule 5 wording are the maintainer's to decide and write. No unit edits
  CLAUDE.md.

**PATTERNS (CLAUDE.md round process)**
- **Opening the rounds.**
  - Read the newest `docs/field/round-NN` (only the README exists, so PLAN says "no field files this round").
  - Read `docs/OPEN.md`: L40 is amended; L36, L10 and L26 are cited; the unpaired-greeting row is added.
  - Name the requester and evidence class: the maintainer's ask and a handed-in critique (the cross-repo gap analysis).
  - Numbering follows patterns-playmap-side's reconciliation table (PT-W1 → 89.2–89.3, the rest → 92), and the maintainer's
    numbering wins.
- **Commits.** One commit per unit, `Round NN.k: …`, the maintainer's authorship. Each is validated by `dotnet build Patterns.sln`
  (the analyzers are fences) and the suites it touches. All seven suites plus the module's `npm test` must be green before the
  push.
- **The wire.**
  - New STATE fields go last in their rows.
  - Loopback wire tests set `ControlService.TrustLoopback = false` and restore it in `finally`.
  - Bare TAKE and CUT stay off the wire: `SCREEN n WINDOW` is per screen, like `SCREEN n PATTERN`.
- **Doctrine.**
  - Done carries Visibility and Effect.
  - A not-ready take is refused with the reason (D9). One air.
  - Every reader in the same round: the desk, the Run strip, the menus, STATE, the Eye, the wire reply, the module, the
    assistant's brief and the twin.
- **Papers.**
  - PLAN, REVIEW (with `Walk: unwalked`: WebView2 and TLS are a platform path) and CHANGELOG.
  - README, REMOTE, COMPANION and the SHOWBUS copy.
  - OPEN, DRILL 23, QUALIFICATION §22/§27 and the tag row.
- **The closing sentence.** `SCREEN n WINDOW` is a verb an output can show, so the round closes with its journal row read beside
  the output.
- No model or tool identifiers in anything pushed.

**MAPPER / PLAYMAP (CLAUDE.md, docs/round3/PLAN.md)**
- **When the units land.** The round-3 ownership forbids edits to unowned folders during its waves (`PLAN.md:2121-2125`).
  `EngineCore.Content.cs` is desk-map's, then canvas's. So the PM units are a post-integration-pass package, or fix branches with
  the owner's leave.
- **Spine changes** go as `Spine:` commits on the package branch, reviewed with it (§5.7): `Core/Model/Content.cs`, `VerbCatalog`
  outside blocks, `StateWriter` and `ContextBuilder`.
- **The new STATE block** gets its fixture written by a test and a card descriptor (`PLAYMAP_WRITE_FIXTURES=1`).
- **Desk doors in step.** The source menu and right-click (`MenuCoverageTests`, `DeskVerbRuleTests`). Lights carry a shape
  (`StatusRuleTests`).
- **Doctrine.**
  - Content is a pure function of show time: the flash is from leader time.
  - Nothing is allocated, compiled or waited on in the render path: both flash pictures are pre-drawn, and `Acquire` only picks and
    marks.
  - No new native library.
  - The analyzers apply to tests (`record struct`, no `Task.Wait` or `TickCount` in `src`).

**AGMESSAGE**: no agent brief. `docs/SHOWBUS.md` copy (header kept) and a CHANGELOG line; `npm test` on node 22 stays green.

**CROSS-AREA**: SHOWBUS 1.3 is one papers unit with one owner, shared with peers-not-screens (§6 peers, §7.1), cross-app-proposals
(§6 notation and caps, §11) and playout-links/spec-drift (§2, §3, §8). Each area writes its own sections, and they land in merge
order.

---

## 9. The review's items, answered

1. **About:blank goes white; the second PlaceholderCard puts words on air** — *accepted.* `MediaAndParticlePatterns.cs:152-155`
   draws `page.StatusText`, and no `DefaultBackgroundColor` is set.
   - There is no `about:blank`. An unresolved held page is never navigated; the source is `Withheld`.
   - Every held branch draws black: no page, `Withheld`, a `DrawInput` failure, and layers (`LayerRenderer.cs:143-155`).
   - A Rendering test covers all four (D8).
2. **`timer:<id>` fails open** — *accepted.* `EngineSources.cs:178-179` ignores the id, and a null lease keeps the last picture
   (`SourceResolver.cs:417-432`).
   - The fix is a distinct `SourceKind.Window` that always yields a frame: black when unknown or ended, the loss state when the link
     is down.
   - Held windows are persisted, so a restart before the grants draws the loss state.
   - Tests cover restart-before-grants, withdraw and unknown (D14).
3. **Identify/reload collide with screen semantics** — *accepted.* `StagewatchLink.cs:395-407` and `NetworkHost.cs:782-783`
   confirm it.
   - The fix is node-link-only `window-*` types.
   - `window reload` is refused for draw-only windows, which `WindowDirector.cs:51` and `WebServer.cs:172-186` confirm would
     otherwise claim success.
   - peers-not-screens §2.1 is amended (D13).
4. **Revoke puts the notice on air** — *accepted.* `WebServer.cs:1248-1253,1285` sends plain `WindowGone` on any miss. Every
   grant-via link now hears `black:true` on any token miss, revoke included (D2, 2.4). Tombstoning rotated tokens is not needed
   (item 15), and REMOTE describes the on-air sequence.
5. **`WindowStatus()` per (window, via) breaks readers** — *accepted.* `WebServer.cs:680` (`ToDictionary`), `:1392` (clear) and
   the Screens subtitle confirm it.
   - `WindowStatus()` keeps its shape (plus `Drawn`).
   - `ViewerStatus()` is separate, bounded at 320 and evicted per key.
   - Every caller is listed in SW-W3 (D17).
6. **The twin and takeover chain is missing** — *accepted in part.*
   - Kept passes move onto `Secrets`, as `Web.Passes[].Secret`, and travel under `SendSecrets` with the standby keeping its own
     (`Twin.cs:398,404-407`).
   - Grant paths are **not** carried on the twin link: it is plain TCP (OPEN L36), and a pass across the LAN in clear is what D4
     forbids. A grant belongs to a node, and the standby is a node with its own id (`ServiceKernel.cs:181`). So it needs its own
     grant, and the `WINDOWS` word plus a Super Check row say before the show when it lacks one.
   - Fence and DRILL 23 cover the takeover (D16).
7. **Grant lifetime across loss and restart** — *accepted.*
   - Grants are persisted per machine (Patterns in the local `Control` section under `Secret`; PlayMap in `NodeSettings`, no
     secret).
   - Only a received list removes one. `rev` is per link (D7).
8. **The live `SCREEN n WINDOW` blanks a screen** — *accepted*, with the refuse-and-pre-roll option.
   - The live verb works only when the page is ready. Otherwise it is refused and steered to PVW + `SCREEN n TAKE` (which exists,
     `ControlProtocol.cs:477-478`), and that take checks readiness too.
   - "Keep the previous picture" was rejected: it splits AirState from the glass (D9).
9. **Placements taken from any peer** — *accepted.* `ShowBus.cs:57-69` accepts any context. Placements need a key-verified context
   from a grant holder with an open or shown viewer, and are worded as claims (D10).
10. **ASK on behalf of another node** — *accepted.* `Approvals.cs:92` binds only `from.id`, and `MainViewModel.Desk.cs:99` shows the
    reason. The fence in `Submit` and the line-as-headline fix it (D12).
11. **The spec and doctrine contradict the wire** — *accepted.* The rule 5 exemption names both carriers, and D0 is a maintainer gate
    before SW-W3, recorded in REMOTE and WHATS_NEXT. The CLAUDE.md line is the maintainer's to write.
12. **The TLS-or-loopback gate doesn't match how peers dial** — *accepted.* `StagewatchLinkService.cs:238` and
    `StagewatchLink.cs:63-75` dial the hello's address.
    - Peers dial `127.0.0.1` for a same-machine hello, and Stagewatch counts its own addresses as local.
    - Replay is closed by `clearSeen`: a key seen plain while TLS is offered gets no path.
    - The broader "refuse plain node keys" is left to peers-not-screens, because it would cut AGMessage's link (D4).
13. **Grouped rows are not templates** — *accepted* against today's matcher (`Approvals.cs:225-237`).
    - The rows are written in cross-app-proposals' §6 notation, which its `VerbSyntax` expands; SW-W1 depends on that unit.
    - Patterns has no risk matcher of its own (only the brief, `StagewatchFollow.cs:281`), so it needs only the cap. Tests rate
      sample lines through `VerbTable`.
14. **The kept-pass rule is scope creep** — *accepted.*
    - Only Stagewatch window paths are held automatically; any other address only when the operator ticks Keep.
    - The show-load migration is dropped. Old raw addresses are made inert by D5's key fence and named by a Super Check row.
15. **The tombstones' cause wasn't measured** — *accepted.* A dead token draws one `WindowGone`, and the shell stops
    (`WebServer.cs:1179-1189`); Patterns never reloads (`WebFrameSource.cs:272`). Tombstones are dropped: "no measurement this
    round", and e2e step 6 would show the cause.
16. **Mapper spine** — *accepted.* `Core/Model/Content.cs`, `VerbCatalog`, `StateWriter` and `ContextBuilder` go as `Spine:`
    commits per §5.7.
17. **Evidence** — *accepted.*
    - (a) All 26 characters are kept (`LibraryItems.cs:219`).
    - (b) 200 is Patterns' writer cap (`ShowContext.cs:68`), and PlayMap's reader states the same (`PLAN.md:2340`); SHOWBUS sets
      none.
    - (c) A new `ShowApp` record; `NodeRef` is untouched.
    - (d) An older Patterns is refused a grant, not left at "sent" (§5).
18. **Missed leak paths** — *accepted.* `StagewatchLinkService.cs:497` now logs through `Scrub`; approved `WEB OPEN` values pass
    `Secure`. `InputLabels` hold only held keys, because the key is held. Both are in the fence test.
19. **`how` fixed from hello caps** — *accepted.* Delivery checks the current link's `can=`; otherwise `cannot` with the reason
    (D3, 2.3).
20. **PlayMap claims what it didn't honour; flash on a local clock** — *accepted.* Unhonoured flags go into `error`, and the flash
    is a function of leader time (D14).
21. **Untested pieces** — *accepted.*
    - (a) https streams are refused with the reason until an FFmpeg TLS probe exists (a gap left).
    - (b) `ws@8.18.0` is named.
    - (c) The rig's `Frame()` sets `Windows`.
    - (d) `window withdraw` needs an explicit window or `all`.
    - (e) `rev` is per link on both receivers.

**The "missing" list**
- Each item is designed above:
  - the twin (D16);
  - the lifetime (D7);
  - revoke (D2);
  - node-link reload and identify (D13);
  - the placement trust (D10);
  - the proposal fence (D12);
  - the maintainer gate (D0);
  - unresolved rendering (D8);
  - pre-roll (D9);
  - the copilot's facts (`AiContext` windows plus `PeerFact.Can`);
  - the Run strip (`RunViewModel` chip);
  - the rig's ambient windows;
  - `Secrets` integration (D6);
  - same-machine dialling (D4).
- *Rejected in part:* "integrate with `Secrets` instead of a parallel store" is done for kept passes and persisted grants. The grant
  paths themselves are deliberately kept off the twin's wire (item 6).
