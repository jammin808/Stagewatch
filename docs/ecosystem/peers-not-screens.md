# Peers, not screens — node identity on Stagewatch's /ws and HTTP (revised after review)

Area: peers-not-screens. Gaps: G06, G15, G32, G43. Design only; nothing in any repo was edited or built. Line numbers are from the
trees as read on 2026-10-25. §9 answers the review item by item.

A show app that opens Stagewatch's `/ws` (Patterns, PlayMap, AGMessage) is a **node**, not a browser **screen**, once it proves
it: a node key, or the operator's own credential (a control key or the PIN). It names itself with its Show Bus id, hears what
Stagewatch made of that claim, and never receives a screen command. An unproven claim stays in Screens, flagged, out of `all`. A
node key speaks for one node id, and a node id that has a key is spoken for only with that key on every route that **writes for a
node** (the posted context, proposals, collection, results). Following the timer is never refused for a key problem that the
viewing code would not refuse: frames are what a stage reads.

**What changed in this revision.** No socket is closed for a key reason that does not close it today (only the viewing code
closes). Unknown or revoked keys stop counting toward the address lockout, which removes the redial-into-lockout hazard for every
peer old and new (and the one that today's HTTP polling already has). The in-band verdict goes only to sockets that opt in
(`node=`), plus one `ok:false` line to a socket whose key was refused. A binding conflict on `/ws` keeps the frames and refuses
only the node routes. An unproven `node=` stays a screen. Release is an explicit rebind, so there is no race. Peers record a
refusal before any I/O. PlayMap's `paired` counts `NotNeeded`. Peers read the words in a 403 body. The rig makes its own keys, and
the bindings fail closed by default.

---

## 0. What exists (evidence)

**Every non-window socket is a screen.**
- `WebServer.ListScreens` groups every socket without a `WindowId` by `ScreenId` (`Stagewatch/src/Timer.Remote/WebServer.cs:77-87`).
  `ScreenId` is the `cid` when `Screens.ValidId` passes, else `"anon-"+guid` (`:1194-1196`).
- `Screens.Match` sends `""`/`all` to every non-control screen, then matches an id, then an operator-given name
  (`Stagewatch/src/Timer.Remote/Screens.cs:61-68`).
- `Redirect`/`Reload`/`Disconnect`/`Identify` push to every socket whose `ScreenId` matches (`WebServer.cs:89-145`). `Disconnect`
  aborts after 300 ms (`:104-120`); its comment says it is "for a stray tablet, not a security wall".
- The `screen` verb's rules live in the WPF view model (`Stagewatch/src/Timer.App/ViewModels/MainViewModel.Screens.cs:104-147`). The
  suite cannot load Timer.App (`Stagewatch/tests/Stagewatch.Tests/Stagewatch.Tests.csproj:16-25`), and the rig refuses `screen` as
  "not simulated" (`Stagewatch/tools/Stagewatch.Rig/Program.cs:182-201`).

**The peers dial as screens, with fixed or local ids.**
- Patterns: `/ws?cid=patterns&page=/patterns` (`Patterns/src/Patterns.App/Services/StagewatchLinkService.cs:276`).
- AGMessage: `/ws?cid=agmessage&page=%2Fagmessage` (`AGMessage/app/lib/nodes.js:317`). Its per-install id is 8 hex
  (`AGMessage/app/server.js:82`).
- PlayMap: `/ws?cid=<id or playmap-id>&page=/playmap&detail=1` (`Mapper/src/PlayMap.Net/Links/StagewatchLink.cs:283-284`).
- PlayMap runs `OUTPUT IDENTIFY ALL 5` on `identify` (`Mapper/src/PlayMap.Engine/Host/NetworkHost.cs:776-784`), and holds its redial
  30 s on `disconnect` (`StagewatchLink.cs:58-59,404`).

**The peers redial fast, and their backoff resets on every successful upgrade.**
- AGMessage: `onopen` sets `delay = 500` (`nodes.js:322-324`), and `onclose` calls `_retry()` (`:333,341-348`).
- Patterns: `attempt = 0` after `ConnectAsync` returns normally (`StagewatchLinkService.cs:182-186`). The redial is then `Redial(0)` ≈
  0.5 s (`:200`; `Patterns/src/Patterns.Core/Services/ShowBusNetwork.cs:21,31-37`).
- PlayMap: a Close message returns `TimeSpan.Zero` (`StagewatchLink.cs:338-339`), then `attempt = 0` (`:236-237`) and the redial
  is `Jitter.Shared.Redial(0)` (`:250`). Its hold comes only from a normal return; an exception path sets `attempt++` with no hold
  (`:243-248`).

**Unknown keys count toward the address lockout, and the peers poll with their keys.**
- `Authorize` counts an unknown or revoked key as a failure (`WebServer.cs:1079-1082`). Five a minute lock the address out of every
  PIN, key and viewing-code check (`:1077-1078`, `:873`, `:1025`). A valid key clears the count (`:1084`). REMOTE 2.10 documents this
  (`Stagewatch/docs/REMOTE.md:214`). A key is 32 characters from a 32-letter alphabet (`Stagewatch/src/Timer.Core/ApiKeys.cs:50-55`),
  160 bits, so it cannot be guessed.
- The peers send their keys on a timer:
  - Patterns: the context read and post every 5 s and the queue every 2 s (`StagewatchLinkService.cs:347-353`).
  - PlayMap: the context post every 5 s and the queue every 2 s (`StagewatchLink.cs:469-472`).
  - AGMessage: the context push every ≤ 5 s and the desk every 3 s (`AGMessage/app/server.js:1114-1125,1080-1101`).
  So a peer that holds a revoked key locks its whole machine's address out within seconds **today**, including a browser's PIN
  page on that machine.

**A refused key on /ws.**
- With no viewing code, the socket is admitted and gets the public frame silently (`WebServer.cs:1154-1164,1203,1422`). Only
  `role=control` sockets hear `auth` (`:1216-1219`).
- A control socket with a refused PIN hears `ok:false` and stays open as a viewer (`:1163-1164,1191,1213-1219`): the precedent for
  "refused, but still viewing".
- With a viewing code set, the socket hears `{"type":"auth","ok":false,"view":true,…}` and is closed (`:1201-1211`).
- A key revoked while a socket is open is never checked again: `ReadKey` is `init`-only (`:1425`) and gives the operator frame
  (`:1422`). Revoking only rewrites the list (`MainViewModel.Screens.cs:84-91`). Settings says "Revoking works at once"
  (`Stagewatch/src/Timer.App/SettingsDialog.xaml:304`).
- A control socket's PIN is not checked again after a PIN change either: `CanControl` is fixed at admission (`WebServer.cs:1191,1417`), and
  the PIN is read live (`MainViewModel.cs:953`).
- Only `Authorization: Bearer` is read as a key or PIN (`WebServer.cs:1136-1137`). The spec-1-3 design's `Authorization: Window
  <token>` is therefore never taken as a key, and this design keeps it so.

**A node key can speak for another node (G15).**
- `AcceptPeerContext(json,address,nodeKeyId)` stores first, then calls `Desk.BindKey` and ignores a `false`
  (`Stagewatch/src/Timer.App/ViewModels/MainViewModel.Desk.cs:64-70`). `PeerContexts.Accept` replaces the entry under the body's
  `node.id` (`Stagewatch/src/Timer.Core/ShowBus.cs:58-70`). `RiskFor` reads it (`MainViewModel.Desk.cs:73-84`) at `Submit`
  (`Stagewatch/src/Timer.Core/Approvals.cs:81,102`).
- `BindKey` is first-come and in memory (`Approvals.cs:50,63-74`). The PIN, a control key and an open Stagewatch are never checked
  against a bound id (`Approvals.cs:92,137,149-154`; `WebServer.cs:570-571`). A control key passes the `Node` gate
  (`ApiKeys.cs:76-77`).
- A key refusal at submit is 400 (`MainViewModel.Desk.cs:29-33`), at collect 403 (`:41-45`), at result 409 (`:47-60`).
- The interface defaults (`Stagewatch/src/Timer.Core/ShowState.cs:497-507,569-579,585-592`) are used by six fakes
  (`tests/Stagewatch.Tests/{Program,WindowTests,ShowBusTests,WebV2Tests}.cs`, `tests/Stagewatch.PageShot/Program.cs`,
  `src/Timer.Companion/CompanionBridge.cs`).

**What the peers do with refusals today.**
- AGMessage is `connected` on upgrade (`nodes.js:323`) and pins on the first `state` frame (`:329`). An `ok:false` line is overwritten
  by `reconnecting` (`:331,343-344`). `fetchContext` needs a §6 body (`:383-393`). The `stage` event goes to every socket.io client,
  participants included (`server.js:996`), while `nodes` goes to admins only (`:1005-1007`).
- Patterns maps any 403 on its context post to "may read but not post" and on proposals to "may not send proposals"
  (`StagewatchLinkService.cs:408-409,431-433`). A refused collect is silent (`:456`). Patterns ignores `auth` (`:523-538`) and pins on an
  authenticated `/api/context` (`:350,380`). `ShowClock` is a static stopwatch, not an injectable clock
  (`Patterns/src/Patterns.Core/Services/ShowClock.cs:12-23`). A desk whose id file cannot be written gets a fresh id every run
  (`ShowBusNetwork.cs:214-228`).
- PlayMap maps 401/403 on the context post to "make a node key" (`StagewatchLink.cs:510-513`), and a refused collect is silent
  (`:530-531`). Its `paired` is `Status == Live` (`NetworkHost.cs:309-316`). The Patterns link counts `NotNeeded` as paired
  (`Mapper/src/PlayMap.Net/Links/PatternsLink.cs:142`). Preflight shows a live-unpaired link amber, and any other state red with the fix
  `LINK STAGEWATCH host` (`Mapper/src/PlayMap.Engine/Show/PreflightBreadth.cs:117-129`). The context item reads only state and detail
  (`Mapper/src/PlayMap.Engine/ContextBuilder.cs:96-100`).

**A 401 at upgrade is readable.** .NET 7+ `ClientWebSocketOptions.CollectHttpResponseDetails` exposes the status and headers of a
failed `ConnectAsync`, and Node's `ws` raises `unexpected-response`. The first version of this design said otherwise, and that was
wrong.

**The desk and the papers.**
- Screens' subtitle counts screens and control pages (`Stagewatch/src/Timer.App/ScreensWindow.xaml.cs:66-68`).
- Show network rows come from hellos and contexts (`MainWindow.xaml:694`; `MainViewModel.ShowBus.cs:71-120`), with no link fact
  and no right-click menu.
- API keys have New and Revoke (`SettingsDialog.xaml:297-300`; `SettingsDialog.xaml.cs:54`; `MainWindow.xaml.cs:567`).
- The context's `peers` rows are `{t,id,name,light,headline,heard}` (`Stagewatch/src/Timer.Show/ShowContext.cs:138`). The AI's
  `PeerFact` has no link (`Stagewatch/src/Timer.Show/AiContext.cs:11`). The copilot sees `ListScreens()` as screens
  (`MainViewModel.Copilot.cs:248`).
- SHOWBUS §11 says a key binds to the id it first posted under (`Stagewatch/docs/SHOWBUS.md:202-205`). Nothing says how a node joins
  `/ws`. The master text is spec-1-3's (`design/spec-1-3.md:8-16`): it carries this design's §6/§7/§7.1/§11 words in its one edit, and
  its §12 leases lean on a "key-verified node link".

---

## 1. Decisions

1. **A node link names itself: `/ws?node=<id>&t=<kind>`, with its credential in the `Authorization` header.** The peers also send
   `cid=<id>&page=/<app>`, which a new Stagewatch ignores when `node` is present. An older Stagewatch then lists each install under
   its own id, which fixes G32 even there.
   - *Why:* the hello id is every other surface's identity (contexts, proposals, pins). It is per install, and it is not a secret
     (§1 rule 5).
   - *Alternatives:* the id as `cid` only (keeps apps in Screens); a first `hello` message (races the first frame).
2. **What is a node, and what stays a screen.**
   - A socket with a valid **node-scope key** is a node link, with or without `node=`. The node scope exists only for show apps
     (`ApiKeys.cs:16-20`).
   - So is a `node=` link with the **operator's credential** (a control key or the PIN).
   - A `node=` with no credential, a read or MCP key, or a refused key is a **claim**. It stays in Screens, flagged
     "claims to be patterns 9f8e7d6c (unproven)". It is skipped by `all` and by no target, but reachable by its screen id or by an
     operator-given name, so the operator can still drop a stray client.
   - *Why:* anything on the LAN can put `node=` in a URL (review item 3). G06 is closed for every peer that says `node=` (no `all`
     reaches it), while only a proven node leaves the operator's list.
   - *Alternative:* claims in `all` (G06's harm returns for keyless PlayMaps); claims out of Screens (no handle on a stray).
3. **One node, one key, on every route that writes for a node** (`NodeBindings.Speak`, in Core):
   - A node key speaks only for the id it is bound to. An unbound key binds on its first proven use on a node link, a context, a
     proposal's `from` or a collect.
   - A key-bound id is spoken for only with that key, so the PIN, a control key or an open request are refused for it.
   - Checked before anything is stored, queued or claimed.
   - A context of weaker proof never replaces a stronger one for the same id within `KeepFor`.
   - *Why:* G15 worked because the check came after storing and because non-node credentials were never checked.
   - *Alternative:* tie an id to an address (DHCP and two-NIC laptops move; no measured cause).
4. **On `/ws`, a binding conflict keeps the frames.** The node link is listed `verified:"none"` with the conflict's words. Only the
   node routes answer 403.
   - *Why:* the shared key (review item 2). A Patterns twin, or two AGMessages pasted one key, must keep the countdown: that is
     their job. The binding governs contexts and approvals, not reading.
   - A legacy keyed socket (no `node=`) is listed under its key ("key 'Show apps' · names no node id"), never as the binding's id.
5. **Unknown or revoked keys never count toward the address lockout.**
   - *Why:* a 160-bit key cannot be guessed, so counting it protects nothing. It does let a peer with a revoked key lock its whole
     machine out of the PIN. Today that happens through polling (§0), and it would happen faster through a redial loop.
   - Wrong PINs, wrong viewing codes and wrong window addresses still count. A valid key from an address locked by wrong PINs is
     still refused (`:1077-1078`, unchanged).
   - *Alternative:* keep counting and rely on each peer's hold. That fails for every peer already deployed (review item 1).
6. **No new close for a key reason.**
   - Only today's rules close: the viewing code, and a window address.
   - A socket whose key is refused at admission, or revoked later, hears one `{"type":"auth","ok":false,…,"retry":60}` and stays open
     as a public viewer, like a control socket with a wrong PIN today.
   - With a viewing code set, that one message carries `"view":true` and the socket closes as today.
   - *Why:* old peers reset their backoff on every upgrade (§0). A close turns into a 0.5 s loop.
   - *Alternative:* a 401 or 403 at upgrade with `Retry-After`. It is readable (§0), but it would stop the timer for a key problem
     on an open Stagewatch. Old peers' backoff still reaches 5 s, so it is not lockout-safe without decision 5 either.
7. **The verdict is in-band and first, on node links only.** A node link hears
   `{"type":"auth","ok":true,"scope":…,"node":{"t","id","verified"}}` (with `words` when `verified` is `none`), or the refusal of
   decision 6. A change later (revoked, rebound, the PIN changed) is sent again, latest wins.
   - Legacy sockets with an accepted key hear nothing new, so a third-party client's first message is still a frame
     (`ShowBusTests.cs:210-220`).
   - *Why:* attempts are not facts. The peer learns at the socket what Stagewatch made of it, and spec-1-3's leases and
     windows-for-peers' grants need a "key-verified node link". Each verdict is also reachable over HTTP (a context POST's
     `verified`), so the peers degrade to HTTP against an older Stagewatch.
8. **Revocation, rebinding and a PIN change re-check live sockets at once** (`CredentialsChanged()`, from `RefreshScreenCopies`).
   - A key gone: the socket drops to the public frame, and a node link becomes a claim (or a plain screen without `node=`).
   - A PIN changed: a PIN-admitted control socket drops to a viewer with `ok:false` "the PIN changed — enter the new one", and the
     control page re-asks (`control.js:31-37`).
   - A rebinding recomputes `verified`.
   - Nothing is compared against `NodeOf(key)` to close. A legacy socket never depends on the binding, so a socket that binds its own
     key by its own context POST is untouched.
9. **The binding is persisted on the key (`ApiKey.node`, `nodeKind`), and the operator rebinds explicitly:** "Let this key speak for
   <node>…" writes the new pair at once.
   - The candidates are the ids that key was refused for this session (kept by `NodeBindings`), the ids heard on the network, or a
     typed id.
   - *Why:* the in-memory binding reopens a race at every restart, and a reinstalled peer has a new id. An explicit pair has no race,
     where "release, then first come" did (review item 5).
   - Keys are never managed over the wire.
10. **An unverified context (Proof None) is kept and labelled, and its `commands` never set a proposal's risk** (unknown = high).
    Patterns' context pulled over Stagewatch's own proof-paired line link counts as Key.
11. **Node links never hear screen messages.** The screen verb's rules move to `Timer.Remote/ScreenDirector.cs`. Matching order:
    - `all` or empty: browsers that are not control pages and not claims;
    - a screen id (claims included);
    - an operator-given screen name;
    - only then, a proven node's id, or the name of a key-verified node: refused "is a show app";
    - else "nothing matches".
    A peer-chosen name never shadows a screen (review item 12). No node-disconnect verb: revoking the key is the wall.
12. **Peers record a refusal before any I/O and honour it on every exit path.** On `ok:false` or a 401, keyed calls (and a closed
    link's redial) wait `retry` s (60, or `Retry-After` for 429). A 403 binding refusal holds nothing: it costs nothing and is
    answered within 5 s once the operator rebinds. A new key typed into the peer restarts its link, so no "dial again now" control is
    needed.
13. **PlayMap's `identify` from any Stagewatch becomes a journal row, never `OUTPUT IDENTIFY ALL`.** Its `paired` is `Paired or
    NotNeeded`, as its Patterns link's is. A refused key is a state of its own in preflight, with its words, not "is not heard".
14. **The bindings fail closed by default.** `IShowAccess.Nodes` defaults to `NodeBindings.For(this)`, a per-implementer instance in a
    `ConditionalWeakTable` that still enforces first-bind-wins. The checks visible in a request run in `WebServer` before the
    implementation is called, so a fake cannot skip them.
15. **The rig makes its own keys** (`--node-keys N` through `ApiKeys.Create`), and its ready line reports them as it reports the PIN.
    No Core API builds a key from a chosen secret.

---

## 2. Wire (exact shapes)

### 2.1 `/ws` — Show Bus 1.3 §7.1 "a node on another node's /ws"

**Node link request**

```
GET /ws?node=9f8e7d6c&t=patterns[&detail=1][&cid=9f8e7d6c&page=/patterns] HTTP/1.1
Upgrade: websocket
Authorization: Bearer swk_…      ← a node key; a control key or the PIN also proves; none = a claim
```

- `node` is `[A-Za-z0-9_-]{1,40}` (the hello's `id`). `t` is `[a-z0-9_-]{1,16}` (the hello's `t`).
- With `node`, `cid` and `page` are ignored. `detail=1` is unchanged.

**Refused before the upgrade.** These are HTTP 400 `{"ok":false,"error":"…"}`. They are never counted, and no peer of this design
triggers them:

| request | error |
|---|---|
| `node` with `win`, or `Authorization: Window …` | `a window's link is not a node link` |
| `node` with `?pin=` | `a node link carries its key in the Authorization header, never in the URL` |
| `node` with `role=control` | `a node link reads; it does not control` |
| a bad `node` | `a node id is 1-40 letters, digits, - or _` |
| a bad or missing `t` | `a node link names its kind (t=patterns, playmap, messenger…)` |
| this Stagewatch's own id | `that's this Stagewatch` |

**Admission order.** The credential is checked first (`Authorize`/`CheckPin` as today; unknown keys are no longer counted). Then
the viewing code, in one message. Then the class:

| credential | binding | class | `verified` | in Screens |
|---|---|---|---|---|
| node key K | K bound to this id, or K unbound and the id free (binds now) | node | `key` | no |
| node key K | K bound to another id, or the id bound to another key | node | `none` + words | no |
| control key or PIN | id not key-bound | node | `operator` | no |
| control key or PIN | id key-bound | node | `none` + words | no |
| read or MCP key, or none | — | claim | `none` | yes, flagged, never in `all` |
| refused key (unknown, revoked, locked out, rate) | — | claim | — | yes, flagged |

**First message on a node link, before any frame.**

```json
{"type":"auth","ok":true,"scope":"node","node":{"t":"patterns","id":"9f8e7d6c","verified":"key"}}
{"type":"auth","ok":true,"scope":"node","node":{"t":"patterns","id":"5e6f7a8b","verified":"none"},"words":"this key speaks for patterns 9f8e7d6c — the Stagewatch operator can let it speak for this one"}
{"type":"auth","ok":false,"error":"unknown or revoked API key","retry":60}
```

- `scope` is one of `node`, `control`, `read`, `mcp`, `pin`, `none`. `verified` is one of `key`, `operator`, `none`.
- `words` (only with `none`) is one of:
  - `no key — the id is only claimed`;
  - `a read key reads; only a node key speaks for a node`;
  - `this key speaks for <kind> <id> — the Stagewatch operator can let it speak for this one`;
  - `<kind> <id> speaks with its own key`.
- `ok:false` errors (with `retry` in seconds):
  - `unknown or revoked API key` (60);
  - `too many wrong PINs — wait a minute` (60);
  - `rate limit: too many requests with this key` (`Retry-After`);
  - `wrong PIN` (60).
- After `ok:false` the socket stays open with the public frame. With a viewing code set, the same single message carries
  `"view":true` (and the view error) and the socket closes, as today.

**Legacy sockets** (no `node=`):
- Admission is unchanged.
- A refused key (header or `?pin=swk_`) on a non-control socket hears the one `ok:false` line above first and stays open.
- An accepted key hears nothing new.
- A valid node-scope key makes the socket a node link listed under its key, with no id asserted.
- Control sockets keep their existing `auth` messages.

**While linked** (latest wins):
- A key revoked: `{"type":"auth","ok":false,"error":"this key was revoked","retry":60}`. The socket drops to the public frame, or
  closes with `"view":true` when a viewing code is set. This applies to node links and legacy keyed sockets alike.
- A key rebound: a node link hears a fresh `ok:true` verdict with its new `verified`.
- The PIN changed: a PIN-admitted control socket hears `{"type":"auth","ok":false,"error":"the PIN changed — enter the new one"}`
  and stays a viewer.
- A node link never receives `redirect`, `identify`, `reload` or `disconnect`. A claim does only when it is named explicitly.

### 2.2 HTTP — one node, one key (§7, §11)

**Credential class per request** (`CredentialOf`):
- a node key → `Key(keyId)`;
- a control key, or the PIN when one is set and given → `Operator`;
- anything else (a read or MCP key, nothing on an open Stagewatch) → `None`.

| route | names a node by | checked where | refused |
|---|---|---|---|
| `POST /api/peers/context` | body `node.id` | `WebServer`, before `AcceptPeerContext` | **403** |
| `POST /api/proposals` | `from.id` | `WebServer`, before `SubmitProposal` | **403** (was 400) |
| `GET /api/proposals?target=<id>&state=approved` | `target` | `WebServer`, before `CollectApproved` | **403** |
| `POST /api/proposals/<id>/result` | the proposal's target | `ProposalDesk.Result` | **409** (as today) |

- Refusal bodies (`{"ok":false,"error":"…"}`):
  - `this key speaks for <kind> <id>`;
  - `<kind> <id> speaks with its own key`;
  - for contexts only, `<kind> <id> spoke with a stronger proof 12 s ago`.
- A context is accepted with `200 {"ok":true,"verified":"key"|"operator"|"none"}` (one field appended).
- A refused request changes nothing.
- Binding refusals never count toward the lockout: they come after `Authorize` accepted the key, which already cleared the address
  (`:1084`).
- An unknown or revoked key still gets 401 `unknown or revoked API key`, and it is no longer counted.

### 2.3 Context §6 `peers` rows

`{"t","id","name","light","headline","heard","link":"ws"|"","verified":"key"|"operator"|"none"|""}`
- The last two fields are appended.
- `link` is `ws` only for a node link whose id is verified `key` or `operator`.
- A proven link with no hello or context is listed. A claim, a conflict or a legacy keyed socket never adds a row.
- The rows are capped at `PeerContexts.MaxPeers` (32): hello and context rows first. The desk says "+N more".
- MCP `get_context` is the same.

### 2.4 Screens and the screen verb

- `GET /api/screens` lists browsers and claims. Each row gains `"claim":"patterns 9f8e7d6c"|""` at its end.
- `screen identify|reload|disconnect|go|save|name` follow decision 11's order.
- Refusal: `Screens: 9f8e7d6c is a show app (Patterns FOH), not a screen — screen commands reach browsers only (Producer → Show
  network)`. The same refusal goes into the HTTP, WS, OSC, Companion TCP and MCP receipts through the existing `Refuse` path.

### 2.5 Settings JSON

`remote.apiKeys[]` gains `"node":"<id>","nodeKind":"<t>"`, appended. They are kept only on node-scope keys, with a valid id and kind.

### 2.6 What changes for each peer

**Patterns**
- Dials `/ws?node=<ShowBusId>&t=patterns&cid=<ShowBusId>&page=/patterns`.
- STATE `bus.stagewatch` gains `key` last (after `stuck` if patterns-playmap-side's round lands first). Its value is
  `"key"|"operator"|"none"|"refused"|""`, from the verdict, else from the context POST's `verified`, else `refused` on a 401, else
  `""` (Stagewatch said neither).
- Companion variable `stagewatch_key`.

**AGMessage**
- Dials `/ws?node=<nodeId>&t=messenger&cid=<nodeId>&page=%2Fagmessage`.
- With a key, it is `connecting` until `auth ok:true` with a scope other than `none`, or an authenticated `GET /api/context` 2xx (by
  status). The pin is confirmed only then.
- With no key, the first `state` frame (unchanged).
- A refusal is a sticky `key refused`, and keyed calls wait `retry` s.

**PlayMap**
- Dials `/ws?node=<NodeId>&t=playmap&cid=<cid>&page=/playmap&detail=1`.
- `links.stagewatch.paired` is `Pairing is Paired or NotNeeded`.
- `state` is `"refused"` when the key is refused and the link is not live.
- `detail` carries Stagewatch's words.
- `identify` is a journal row.

No new verb, no new hello cap, no new frame field.

---

## 3. Changes, file by file

### Stagewatch

**Core**
- **`src/Timer.Core/NodeBindings.cs` (new)**
  - Types: `NodeProof {None, Operator, Key}`; `record struct NodeCredential(string? KeyId, NodeProof Proof)` with `None`, `Operator`
    and `Key(id)`; `record struct NodeVerdict(bool Ok, NodeProof Proof, string? Error)`.
  - `NodeBindings` holds key ↔ (kind, id) under a lock, seeded from `ApiKey.Node`/`NodeKind`.
  - `event Bound(keyId, kind, id, previousId?)` is raised outside the lock.
  - Methods:
    - `Speak(cred, kind, id)` binds a node key on first proven use and refuses both ways, and refuses a non-key credential for a
      key-bound id;
    - `Rebind(keyId, kind, id)` is the operator's: it moves the id off any other key and says which;
    - `LastRefused(keyId)` gives the claimed (kind, id, at) for the rebind dialog;
    - `NodeOf`, `KeyOf`, `Keep(liveKeyIds)`, `ValidId`, `ValidKind`;
    - `static For(object owner)` gives a `ConditionalWeakTable` instance, the fail-closed default.
- **`src/Timer.Core/NodeLinks.cs` (new)**
  - Records: `WsCredential` (none / PIN ok / PIN wrong / key with scope / key refused with retry; plus a from-header flag), `NodeLinkClaim`,
    `WsAdmission` (class: screen, claim or node; `verified`; words; the 400 error if any; the first message's bytes or null), and
    `NodeLinkFact(Kind, Id, Verified, KeyId, KeyName, Address, ConnectedUtc, Links, Words)`.
  - `NodeLinks.Admit(query, authScheme, cred, viewNeeded, ownId, bindings)` is pure and holds the whole §2.1 matrix, including the
    pre-upgrade 400s.
  - `NodeLinks.Recheck(socketState, keys, bindings, pinHash)` is pure. It is what `CredentialsChanged` applies: the new state and the
    message to send, or none.
- **`src/Timer.Core/Approvals.cs`**
  - `ProposalDesk(Func<string> deskId, NodeBindings bindings)`.
  - `_keyNode` and `BindKey` are removed (`:50,59-74`).
  - `Submit`, `Collect` and `Result` take a `NodeCredential` and call `bindings.Speak` first. `Submit`'s refusal carries `Forbidden`.
- **`src/Timer.Core/ShowBus.cs`**
  - `PeerContexts(NodeBindings)`.
  - `Accept(json, address, now, ownId, NodeCredential)` returns `(Status, Error, Proof)`. The order is parse, `Speak`, the
    stronger-proof rule, then store (`:58-70`). `Entry.Proof` is new.
  - `PeerContexts.RiskFor(entry, line)`: `high` without an entry or with `Proof == None`.
  - `PeerSummary` gains `Link` and `Verified` (appended, optional).
  - New pure `ShowNetwork.Summaries(hellos, contexts, nodeLinks, ownId, now, max: 32)`. It is the join now inline in
    `MainViewModel.ShowBus.cs:72-89`, with link-only rows for proven ids only.
- **`src/Timer.Core/ApiKeys.cs`**
  - `ApiKey.Node`, `ApiKey.NodeKind` (`[JsonPropertyName("node")]`, `("nodeKind")`), appended.
  - `Sanitise` keeps them only on node-scope keys with a valid id and kind (`:96-100`).
  - No new maker: `Create` stays the only one (`:48-65`).
- **`src/Timer.Core/ShowState.cs`**
  - `IShowAccess.Nodes => NodeBindings.For(this)`.
  - `IShowContext.AcceptPeerContext(json, address, NodeCredential)` returns `(int, string?, string verified)`. It replaces both
    overloads (`:575-578`).
  - New `IShowContext.PeerName(id)`.
  - `IShowApprovals`' `SubmitProposal`, `CollectApproved` and `ProposalResult` take a `NodeCredential` (`:587-591`).

**Remote**
- **`src/Timer.Remote/WebServer.cs`**
  - `Authorize` no longer counts an unknown key (`:1079-1082`; the lockout check before it stays).
  - `/ws` (`:1141-1219`) runs through `NodeLinks.Admit`. A 400 is written before the 101 head. The verdict or refusal line goes
    before the loops.
  - `WsClient` gains `Node`, `Kind`, `Verified`, `KeyId`, `Claim` and `PinHash` (SHA-256 of an admitting PIN, never the PIN). It
    also gains `volatile` `ReadKey`, `CanControl` and `ScreenId` (now settable; `:1417,1425,1431`).
    - A node's `ScreenId` is `node:<id>` (or `key:<keyId>`), which `Screens.ValidId` never matches.
    - A claim's is `claim-<12 hex of SHA-256(t:id)>`.
  - `ListScreens` (`:77-87`) leaves out nodes and carries `Claim`. New `ListNodes()` returns `NodeLinkFact`, grouped by id or by key.
  - `Push`/`Disconnect`/`Identify` (`:89-145`) skip nodes (defence in depth).
  - New `CredentialsChanged()`: for each keyed or PIN-admitted socket it applies `NodeLinks.Recheck` and sends with `Task.Run`, as
    `Push` does. It never waits and never closes except for the viewing code.
  - `CredentialOf(req)` replaces `NodeKeyId` (`:570-571`). The request-visible `Speak` checks are in `/api/peers/context` (`:452-461`)
    and `HandleProposalsAsync` (`:578-618`): a 403 with words, and 200 `{ok,verified}`.
- **`src/Timer.Remote/Screens.cs`**: `ScreenInfo` gains `Claim` (appended). `Match` skips claims for `all` and empty (`:61-65`).
- **`src/Timer.Remote/ScreenDirector.cs` (new)**
  - `Run(ScreenArgs, WebServer?, nodes, nodeName, rename, savePreset, resolvePreset)` returns `(Ok, Words)`.
  - It holds the rules moved from `MainViewModel.Screens.cs:104-147`, with decision 11's order.

**Show**
- **`src/Timer.Show/ShowContext.cs:138`**: `link` and `verified` are appended to each peers row.
- **`src/Timer.Show/AiContext.cs`**
  - `PeerFact` (`:11`) gains `Link` and `Verified` (appended), so the copilot knows which apps follow the timer and which contexts
    are only claims.
  - `ScreenFact` gains `Claim`.

**App**
- **`MainViewModel.Desk.cs`**
  - `Desk` and `_peerContexts` are built on `_nodes`.
  - `AcceptPeerContext(cred)` goes through `PeerContexts.Accept`.
  - A refusal is journalled once per (key, claimed id) per session, with the key's name and both ids, never a secret.
  - `RiskFor` (`:73-84`) uses `PeerContexts.RiskFor`; the pulled Patterns context counts as Key.
  - Submit, collect and result return 403 or 409 with words (`:29-60`).
- **`MainViewModel.ShowBus.cs`**
  - `_nodes` is seeded from `Settings.Remote.ApiKeys`.
  - `Bound` is posted to the show thread, which persists `key.Node`/`NodeKind` and journals "API key 'Patterns FOH' now speaks for
    patterns 9f8e7d6c (first use)" or "(the operator's choice; it spoke for patterns 1a2b3c4d)".
  - `PeerSummaries` (`:72-89`) becomes `ShowNetwork.Summaries(…, _web.ListNodes())`.
  - `PeerRow` gains `LinkLine` (e.g. "follows over /ws · key 'Patterns FOH' · 10.0.0.5"), `Mark` (✓ key, △ operator, ○ unverified;
    a shape, never colour alone) and `KeyId`.
  - Key-grouped facts (legacy and conflicts) become rows of their own. `PeerFacts` gains link and verified. New `PeerName(id)`.
  - The `playmap` label is left to playout-links.
- **`MainViewModel.Screens.cs`**
  - `ApplyScreenCommand` calls `ScreenDirector.Run` (`Refuse` or `StatusLine` from its words).
  - `RefreshScreenCopies` (`:16-26`) copies `Node`/`NodeKind`, seeds `_nodes.Keep`, and calls `_web?.CredentialsChanged()`. It runs
    after every Settings close (`MainViewModel.cs:919-922`) and every key change, so revocation and PIN changes reach live sockets.
  - New `RebindApiKey(keyId, kind, id)` journals the change and calls `CredentialsChanged`.
  - New `NodeLinkCount` and `ClaimCount`.
- **`MainViewModel.Diagnostics.cs:70`**: `listening on 9924, 3 browser(s), 1 window link(s), 2 show app link(s), 1 unproven claim`.
- **`ScreensWindow.xaml.cs:66-68` (+ row template)**
  - The subtitle adds "· 2 show apps follow this Stagewatch too (Producer → Show network); screen commands never reach them".
  - A claim's row shows "claims to be Patterns 9f8e7d6c — unproven; never in 'all'".
- **`MainWindow.xaml:694` (+ `.xaml.cs`)**
  - Each Show network row shows its `LinkLine` and mark.
  - A right-click menu offers: Copy node id; Let a key speak for this node… (lists node keys, marking one refused for this id);
    Revoke its key… (confirmed; only when verified by key).
- **`SettingsDialog.xaml:297-304` / `.xaml.cs:54-94`**
  - The key row says "speaks for patterns 9f8e7d6c" or "no node yet", and "refused for patterns 5e6f7a8b 12 s ago" when that applies.
  - New button: "Let it speak for…".
  - A right-click menu on the list repeats Revoke and "Let it speak for…", in step with the buttons.
  - `KeyHooks` gains `Rebind`, wired at `MainWindow.xaml.cs:567`.
  - The help line keeps "Revoking works at once", which is now true for open sockets.
- **`Help/HelpContent.cs:828`**: the `show-network` tip names the link line, the three marks and the rebind.

**Companion module**: the screen actions' labels and descriptions (`companion-module/src/actions.ts:376-394,758-768`;
`presets.ts:486-490`) say "empty = all browser screens; show apps and unproven claims are never included".

**Rig** (`tools/Stagewatch.Rig/Program.cs`)
- New option `--node-keys N`. On a first start it makes N node keys with `ApiKeys.Create`, saves their hashes, ids and bindings
  (never secrets) in the `--state` file, and reports `"nodeKeys":[{"id","secret"}]` in the ready line, only for keys made in this
  run, as it reports the PIN (`:10-12,132-137`).
- It adds `ApiKeys`, `Nodes`, `PeerContexts` and `AcceptPeerContext(cred)`.
- `ShowCommandKind.Screen` goes through `ScreenDirector` (replacing the refusal at `:182-201`).
- `Context()` peers come from `ShowNetwork.Summaries` plus `ListNodes` (replacing the empty list at `:249-254`).
- New `PeerName`.

**Tests**: `tests/Stagewatch.Tests/NodeTests.cs` (new, run from `Program.cs`); `ShowBusTests.cs`, `WebV2Tests.cs`; `tests/Stagewatch.UiSmoke/Program.cs`;
`tests/e2e/nodes.e2e.cjs` (new); `tests/e2e/rig.cjs` (a `nodeKeys` option); `tests/e2e/package.json` (`ws` at an exact version, e.g.
`8.18.3`). See §4.

**Papers**
- `docs/REMOTE.md`:
  - the Screens section says browsers and claims;
  - the 2.10 keys line says unknown keys are not counted and that revoking reaches open sockets (`:214`);
  - a new release section, "Show apps on /ws".
- `docs/NODES.md`: the `/ws` line for each app. `docs/COMPANION.md`, `docs/FEATURES.md` and `docs/WHATS_NEXT.md` (what the round did
  not do).
- SHOWBUS §6 peers fields, §7, §7.1 and §11 are handed to spec-1-3's single master edit, word for word from §2 here.

### Patterns (the round the maintainer opens for it; patterns-playmap-side places it as Round 90)

**N.1 — words and holds (works against any Stagewatch)**
- `StagewatchLinkService.cs`:
  - a refusal (401, or `auth ok:false`) is recorded in `_keyHeldUntil` before any further I/O, and keyed calls skip while it holds
    (`:347-353`);
  - the 403 bodies' `error` is shown (`:405-409,431-433`), a refused collect is said in the approval words with
    `_approvalsLive = false` (`:456`), and the redial on every exit path waits for the hold (`:197-200`);
  - the hold's length comes from a pure `StagewatchFrame.Hold(status, retryAfter)` in Core.
- `ShowBusIdentity.LoadOrCreate` (`ShowBusNetwork.cs:191-228`) returns whether the id was kept. The Nodes page's identity line says
  "show network id 9f8e7d6c — not kept (folder not writable): a Stagewatch key bound to it refuses this desk after a restart".

**N.2 — the node link (needs Stagewatch's node links)**
- `src/Patterns.Core/Services/StagewatchFollow.cs`:
  - `StagewatchFrame.SocketQuery(selfId)` returns `/ws?node=<id>&t=patterns&cid=<id>&page=/patterns` for a valid id, else today's
    query;
  - `StagewatchFrame.Auth(json)` returns `StagewatchAuth(Ok, Scope, Verified, Words, Error, Retry, View)`.
- `StagewatchLinkService.cs`:
  - `:276` dials `SocketQuery`;
  - `ReceiveAsync` gains `case "auth"` (`:523-538`). It sets `KeyVerdict` and the hold, and pins on `ok` when TLS is on and the scope
    is not `none`, as an authenticated answer;
  - the context POST's `verified` also sets `KeyVerdict`;
  - new public `KeyVerdict` and `KeyWords`.
- `AppServices.ShowApps` (`:908-918`): while `KeyVerdict` is `refused`, or `none` with words, the linked Stagewatch's card says "KEY
  REFUSED — …" or "key not accepted for this desk — …", as the certificate refusal does. That one change reaches the Eye
  (`EyeService.cs:598`), the Nodes page, the context's peers (`AppServices.cs:930`) and the assistant's facts (`:1810`).
- `CommandRouter.cs:765-777`: `stagewatch.key = link.KeyVerdict`, last.
- The Companion module: `src/state.js:313-316` and `src/variables.js:212-215` gain `stagewatch_key`. The fixture and
  `test/module.test.mjs` cover it.

### AGMessage

**AG-1 (works against any Stagewatch)** — `app/lib/nodes.js`
- `onopen` (`:322-324`) leaves the status `connecting` when a key is set, and calls `fetchContext()`.
- `fetchContext` (`:383-393`):
  - a 200 with the key counts as the verdict by its status, whatever the body: `_confirmPin()` and `connected`;
  - a 401 or 403 records `refused = {error, until}` from the body and `Retry-After`;
  - the context is kept only when it parses.
- `onmessage` (`:325-332`):
  - a `state` frame sets `connected` only without a key;
  - `auth ok:false` records the refusal.
- `_retry` (`:341-348`) never overwrites a refusal, and waits `refused.until` when set.
- `_watch` (`:350-359`) asks `fetchContext` every 5 s while `connecting` with an open socket.
- `pushContext`, `approvedFor` and `reportResult` (`:401-408,446-462`) keep the body's `error` as `lastRefusal` and skip while held.
- `app/server.js`:
  - the shared `stage` event (`:996,1086`) carries `status` from a closed set (`connected`, `connecting`, `reconnecting`, `stale`,
    `key refused`, `certificate refused`, `off`);
  - a new admin-only `stageLink` event carries the refusal's words and until-time, sent to `s.data.isAdmin` sockets as `nodes` is
    (`:1005-1007`).

**AG-2 (needs Stagewatch's node links)**
- `StagewatchLink({…, nodeId})`.
- `_dial` (`:317`) uses `/ws?node=<id>&t=messenger&cid=<id>&page=%2Fagmessage`.
- `auth ok:true` with a scope other than `none` confirms the pin and sets `connected`. Its `words` go to the admin.
- `server.js:995-1011` passes `nodeId: settings.nodeId`.

### Mapper (PlayMap) — a round-2 fix batch, as CARRYOVER records them (`Mapper/docs/round2/CARRYOVER.md:186,251,284`)

**PM-1 (works against any Stagewatch)**
- New pure `src/PlayMap.Net/Links/StagewatchScreenCommands.cs`: `identify` gives a journal row; `redirect`, `reload` and `disconnect`
  keep today's words and hold.
- `NetworkHost.OnScreenCommand` (`:776-799`) uses it. `Run("OUTPUT IDENTIFY ALL 5", …)` is removed.

**PM-2 (the node link)** — `src/PlayMap.Net/Links/StagewatchLink.cs`
- `:283-284` dials `node=<SelfId>&t=playmap&cid=<cid>&page=/playmap&detail=1` when `SelfId` is a Show Bus id, else today's query.
- New public `Pairing` (`PairState` from `WireClient.cs:57-68`) and `_refusedUntilNs`, both set before any I/O:
  - in `Take` (`:372-434`): `auth ok:true` with `key` or `operator` gives Paired; with `none` and a key, Refused plus the words; with no
    key, NotNeeded; `auth ok:false` gives Refused plus the hold;
  - in `PostContextAsync` (`:497-520`): 2xx gives Paired, 401/403 Refused with the body's `error` as words;
  - in `ApprovalsAsync` (`:528-531`): a refused collect gives words.
- `RunAsync` (`:227-254`) waits `max(hold, refusedRemaining, Redial(attempt))` on every exit path, exceptions included (the
  `CloseOutputAsync` race at `:362-365`).
- `src/PlayMap.Engine/Host/NetworkHost.cs:309-316`:
  - `paired = link.Pairing is PairState.Paired or PairState.NotNeeded`;
  - `state = "refused"` when Pairing is Refused and the link is not live;
  - detail `key refused — <Stagewatch's words>`.
- `src/PlayMap.Engine/Show/PreflightBreadth.cs:117-129` gains a `"refused"` arm: red, "Stagewatch at <host> refused this node's key —
  <words>", with no fix line, because the fix is on Stagewatch.
- `ContextBuilder.cs:96-100` (a spine file, round 3 §2.2) is not edited: its red light and `state + detail` text already carry
  "refused, key refused — …".
- `LinkView` (`Seams.cs:99`, spine) is not edited. The desk's Links drawer reads state and detail as given (`StateSnapshot.cs:212-213`).
  The fixtures are unaffected (`tests/fixtures/state/main.json:171-173` is `{"state":"off"}`).

---

## 4. Tests

**Stagewatch, pure** (`tests/Stagewatch.Tests/NodeTests.cs`)
- `NodeBindings`:
  - first bind;
  - the same key for another id is refused, and so is another key for a bound id;
  - the PIN, a control key and `None` are refused for a key-bound id and allowed for a free one;
  - `Rebind` moves an id and names the old key;
  - `LastRefused`;
  - `Keep` forgets revoked keys;
  - `Bound` is raised once per pair;
  - pairs seeded into a new instance survive (a restart);
  - `For(owner)` is per owner and enforces first-bind-wins.
- The `NodeLinks.Admit` matrix (every row of §2.1 with its exact first message or 400 body):
  - the shared key: key K bound to `pat1` with `node=pat2` is a node with `verified:none`, words, no close, and frames flowing;
  - a legacy keyed socket with a valid node key is a node by key with no id;
  - `node` with no key is a claim;
  - `node` with `Authorization: Window x` is a 400;
  - `node` with `?pin=swk_` is a 400;
  - own id is a 400;
  - a refused key with a viewing code gives one message with `view:true`, then close;
  - a keyless node link with a viewing code gives one message.
- `NodeLinks.Recheck`:
  - a key revoked gives `ok:false` and a downgrade, with a close only when a viewing code is set;
  - a rebind gives a fresh `ok:true`;
  - the PIN changed drops `CanControl`;
  - a legacy keyed socket whose key gets bound by its own context POST: no message, no change.
- `ScreenDirector`:
  - `all` skips nodes and claims;
  - a claim's id reaches it;
  - a node's id, or a key-verified node's name, is refused with "is a show app";
  - a browser named "Lobby TV", beside a node whose hello says "Lobby TV", is reached;
  - a node named "all" changes nothing;
  - `name` and `save` follow the same order.
- G15:
  - key A bound to `ag1` posts `{node: patterns pat1}`: 403, and Patterns' entry is unchanged;
  - an open post for a bound id: 403;
  - an open post for a free id is stored with Proof None; `RiskFor` gives `high` for a verb listed `low`, and `low` with its key;
  - an open post does not replace an operator-proof entry within 30 s (403 with words);
  - proposals' `from`, collect and result with a control key for a bound id are refused.

**Stagewatch, end to end on a real `WebServer`** (`ShowBusTests.Web`/`Approvals`, `WebV2Tests`)
- A node key at `/ws?node=ag1&t=messenger`: the verdict comes first; `ListScreens()` is empty; `ListNodes()` holds `ag1`.
- Beside a browser socket (`cid=screen01`) and a claim (`node=pat9`, no key):
  - `Identify`/`Disconnect` on `Match(ListScreens(),"all")` reach the browser only; the node and the claim stay open;
  - `Identify(["node:ag1"])` returns 0;
  - the claim's own id reaches it.
- `pat1`/`pat2` with keys K1/K2 make two node rows (G32). One key shared by `pat1`/`pat2` makes two rows, one `verified:none`, and
  both keep receiving frames.
- **The lockout test (the review's most important):**
  - a legacy-style client (no `node=`, an unknown header key) reconnects every 0.5 s for 6 s, and posts `/api/peers/context` with that
    key every 0.5 s;
  - afterwards, from the same address, `?pin=4321` still opens `/api/state?detail=1` and a control socket.
- Revoked plus `CredentialsChanged()`: a node link and a keyed dashboard socket hear "this key was revoked" and keep public frames;
  with a viewing code set, they close.
- A PIN changed plus `CredentialsChanged()`: a control socket hears `ok:false` and its next command is refused.
- `peers/context` with `ag1`'s key and `node.id=pat1` gives 403 with words; its own id gives `200 {"ok":true,"verified":"key"}`.
- **Changed existing tests:**
  - `ShowBusTests.cs:210-220` keeps its assertion: an accepted read key without `node=` hears a frame first, unchanged by design;
  - `DeskRules` (`:292-317`) moves to `NodeCredential`; `refused` is still non-null;
  - the `BusControl` fake (`:20-51`) implements the credential overloads and holds `Desk`/`Peers` built on `Nodes`;
  - `Approvals` (`:377-410`): "another app's key can't collect" stays 403; a new case, a key speaking for another node's
    `from`, is 403 (was 400).

**Stagewatch, rig e2e** (`tests/e2e/nodes.e2e.cjs`, Playwright plus `ws`, the rig with `--node-keys 2`)
- A `/viewer` page plus a `ws` peer with key 1 at `?node=pat00001&t=patterns`. The peer's first message is `auth ok`, `verified:key`.
- `screen identify all`: the page shows `#sw-identify`; the peer gets nothing.
- `screen disconnect all`: the page shows "off"; the peer stays open and beats.
- `screen identify pat00001`: a receipt with `ok:false` naming a show app.
- `/api/screens` has no `pat00001`. `/api/context` peers has it with `link:"ws"`, `verified:"key"`.
- The same key at `node=pat00002`: `verified:none` with words, frames flowing. `peers/context` for `pat00002` is 403, for `pat00001` 200.
- A keyless `node=pat00003`: listed in `/api/screens` with its `claim`; not reached by `screen identify all`; reached by its id.
- An unknown key: `ok:false`, `unknown or revoked API key`, retry 60, the socket open with the public frame. Ten such dials, then the
  control page still accepts the PIN.
- The rig restarted on the same state file: key 1 still speaks for `pat00001` (the persisted binding).

**Stagewatch, Windows** (UiSmoke `:157,309-319` on the new signatures; named through `Bench.OnWindows` elsewhere):
- the Show network row shows the link line and mark;
- the Screens subtitle names the show apps, and a claim row is flagged;
- the key row says "speaks for";
- "Let it speak for…" rebinds and journals;
- the right-click menu entries match the buttons.

**Patterns**
- Core: `SocketQuery` (`node`, `t`, `cid`; legacy for a bad id); `Auth` parsing; `Hold`.
- App, `ShowBus12AppTests`, with a scripted Stagewatch:
  - the asked URL has `node=`, `t=` and `cid=`;
  - `auth ok` gives `bus.stagewatch.key = "key"` and the pin;
  - `auth ok:false` gives `"refused"`, the card's words in `ShowApps`, and exactly one keyed HTTP request in the next 6 s (the hold,
    by count, not on a clock);
  - a 403 `this key speaks for patterns 1a2b3c4d` reaches the approval words verbatim;
  - a 403 on collect sets `_approvalsLive` false with words.
- Module: `stagewatch_key`.

**AGMessage**
- `units.test.js:107-131` changes: the fake's `/api/context` 200 makes the link `connected` (by status). The test asserts the last
  status is `connected`, and that a frame before it reported `connecting`. The URL has `node=` and `t=` and no key.
- `resilience.test.js`:
  - a 401 from `/api/context` gives a sticky `key refused`, never overwritten by `reconnecting`, and no keyed request in the next 1.5 s;
  - `auth ok:false` gives the same.
- `tls.test.js`: a `state` frame before any verdict does not pin; `auth ok` pins; an authenticated `/api/context` 200 from a fake with
  no verdict pins.
- `server.test.js`: a participant socket's `stage` event carries `status:"key refused"` and no error words; an admin socket gets
  `stageLink` with them.

**PlayMap** (`tests/PlayMap.Net.Tests/StagewatchLinkTests.cs`)
- The asked line (`:162`) is `ws /ws?node=a1b2c3d4&t=playmap&cid=a1b2c3d4&page=/playmap&detail=1 auth=Bearer swk_test0123456789`.
- Verdict `ok key` gives Paired. A keyless link gives NotNeeded, and `paired` is true.
- `ok:false` gives Refused, and one connection in 3 s — also when the fake aborts right after the verdict (the close race).
- A keyed context 2xx from a fake with no verdict gives Paired. A 403 body's words reach `Words`.
- Engine: the screen-command plan; preflight's `"refused"` row; `paired` for NotNeeded (`PreflightBreadth` tests).

---

## 5. Units, in order (two phases)

**Phase 1.** Each unit is safe alone, in any order. Together they remove the harms.

| # | repo | unit | gaps | depends on |
|---|---|---|---|---|
| SW-1 | Stagewatch | Core: `NodeBindings` (fail-closed default); `PeerContexts` and `ProposalDesk` on it; proof-gated risk; the stronger-proof rule; `ApiKey.node`/`nodeKind` | G15 | — |
| SW-2 | Stagewatch | Remote and App: `CredentialOf` on every node route, the checks in `WebServer`, 403s with words; unknown keys not counted; interface signatures, fakes, UiSmoke signatures | G15 | SW-1 |
| PM-1 | Mapper | Engine: `identify` is a journal row | G06 | — |
| AG-1 | AGMessage | `connected` and the pin only on an authenticated 2xx; sticky refusal; key hold; 403 words; admin-only words | G43 | — |
| PT-1 | Patterns | N.1: refusal hold before I/O, 403 words, refused collect said, id-not-kept words | G43 (part) | — |

**Phase 2.** The node link and the desk.

| # | repo | unit | gaps | depends on |
|---|---|---|---|---|
| SW-3 | Stagewatch | Remote: node links on `/ws` (admission matrix, claims, verdict, refused-key line, `CredentialsChanged`), Screens/ListNodes/Push filters, `ScreenDirector` | G06, G32, G43 | SW-1, SW-2 |
| SW-4 | Stagewatch | App and Show: Show network link line, mark and right-click; Screens subtitle and claim flag; diagnostics; context peers `link`/`verified`; AI facts | G06 | SW-3 |
| SW-5 | Stagewatch | App: persisted bindings and "Let it speak for…" (Settings buttons and right-click; Show network right-click); journal rows | G15 | SW-4 |
| SW-6 | Stagewatch | Rig (`--node-keys`, screen commands, peer contexts, context peers) and `nodes.e2e.cjs` | G06, G15, G43 | SW-3 |
| SW-7 | Stagewatch | Companion labels; REMOTE, NODES, COMPANION, FEATURES, WHATS_NEXT; SHOWBUS words handed to spec-1-3's master edit | — | SW-5, SW-6 |
| PM-2 | Mapper | Net and Engine: the node link, `Pairing`, the refused state and preflight arm, holds before I/O, 403 words, refused collect | G06, G43 | SW-3, PM-1 |
| PT-2 | Patterns | N.2: the node link, `KeyVerdict`, `bus.stagewatch.key`, the cards' words (Eye, Nodes, context, assistant), `stagewatch_key` | G32, G43 | SW-3, PT-1 |
| AG-2 | AGMessage | The node link and the verdict | G32 | SW-3, AG-1 |
| PM-3 | Mapper | Papers: SHOWBUS-NODE §2 row, CARRYOVER entry | — | PM-2, spec-1-3's 1.3 cut |
| PT-3 | Patterns | The round's papers | — | PT-2, spec-1-3's 1.3 cut |
| AG-3 | AGMessage | Papers: NODES, SHOWBUS copy, CHANGELOG | — | AG-2, spec-1-3's 1.3 cut |

**Mixed versions.**
- An older Stagewatch ignores `node`/`t`, and lists each peer under its `cid` (now its own id).
- The peers then read HTTP: AGMessage's authenticated 2xx, PlayMap's keyed POST, Patterns' context POST, and 401 holds.
- An older peer against the new Stagewatch: it gets no new close, its revoked key no longer locks its address, and a node-key peer
  leaves Screens.

---

## 6. Gaps

**Closed**
- **G06**:
  - proven nodes leave Screens, `all` and the push paths; claims leave `all`;
  - a node target is refused with the reason, in a fixed order that peers cannot shadow;
  - nodes are listed with their link;
  - PlayMap's identify is journal-only against any Stagewatch.
- **G15**:
  - one node, one key on every node route, checked before storing;
  - non-node credentials are refused for a bound id;
  - a weaker proof never replaces a stronger one;
  - unverified contexts never set risk;
  - the binding is persisted and rebound explicitly.
- **G32**: per-install ids on `/ws` (`node=`, and `cid=<id>` for older Stagewatches); node links are grouped by id.
- **G43**: AGMessage is `connected` and pins only on an authenticated 2xx or a node verdict; a refused key is told on every socket
  that sent one; refusals are sticky and held.

**Left**
- **G15 (remainder)**: an open Stagewatch (no PIN) accepts a context for a free id from anyone. It is labelled unverified, never sets
  risk, and never replaces an operator-proof one.
- **G11**: `node` with `win`, or with `Authorization: Window`, is refused here. A node joining as a window is spec-1-3's §12.6 and
  windows-for-peers'.
- **G31**: PlayMap's `paired` is honest here. TLS for typed addresses, the desk probe and naming a 403 as "requires wss" stay with
  G31.
- **G41**: only the `/ws` line per app in NODES.md.
- **G44**: the rig e2e drives scripted peers.

---

## 7. Risks

- **A shared key loses its approvals.** An operator who pasted one node key into several installs sees the second install's
  context and proposals refused (403, "this key speaks for patterns 9f8e7d6c"), where today its context is stored. The frames are
  never lost. Mitigation: the words, a journal row naming the key and both ids, the release notes ("one node key per install"), and
  "Let it speak for…".
- **A reinstalled peer** is refused on the node routes until the operator rebinds its key. Mitigation: the same words and journal
  row, and the rebind dialog lists the id that key was just refused for.
- **The lockout rule changes:** unknown keys no longer count. A 160-bit key cannot be guessed, and wrong PINs, codes and window
  addresses still count. REMOTE 2.10 is corrected in the same unit.
- **Claims stay in Screens.** A keyless peer that says `node=` is listed in Screens flagged and out of `all`. An operator may still
  target it by its id, by choice.
- **Operators lose the familiar `/patterns`, `/agmessage` and `/playmap` rows.** The Screens subtitle names the linked apps and points
  to Show network.
- **Interface churn:** `IShowContext` and `IShowApprovals` change, which touches six fakes. It lands in one unit (SW-2), so the tree
  never builds half-migrated.
- **`CredentialsChanged` runs on the show thread over a `ConcurrentDictionary`.** It only sets flags and sends through `Task.Run`, and
  it never waits.
- **Merge order with siblings:**
  - playout-links and spec-drift edit `MainViewModel.ShowBus.cs` (different hunks);
  - spec-1-3 owns the master text and carries these words;
  - windows-for-peers and spec-1-3's leases use "a key-verified node link", which is `verified:"key"` here;
  - patterns-playmap-side numbers the Patterns rounds and orders `stuck` before `key` in STATE.
- **Mapper ownership:** `StagewatchLink.cs`, `NetworkHost.cs`, `PreflightBreadth.cs` and `WireClient.cs` have no round-3 owner (§4:
  "nothing in that wave edits it", `Mapper/docs/round3/PLAN.md:2121-2125`). The units ride round 2's fix-batch precedent; if the
  owner reads §4 as a block, they wait for the integration pass. `ContextBuilder.cs` and `Seams.cs` are spine files and are not
  touched.
- **A Patterns desk with an unwritable id folder** gets a new id each run, so its key's binding refuses it after a restart. The Nodes
  page says so. The fix is the folder's.

---

## 8. Process notes

**Stagewatch** (`Stagewatch/CLAUDE.md`)
- One `dotnet build` at a time. Guard rails: no `Thread.Sleep`/`TickCount` in `src/` (`Environment.TickCount64` as today); invariant
  formatting.
- No frame field changes. New JSON fields go at the end of their objects: peers rows `link`/`verified`; `/api/screens` `claim`; the
  context 200's `verified`; settings `node`/`nodeKind`.
- No new `ShowCommandKind`, parser verb, help line or `Copilot.Classify` row.
- Every chain:
  - Core: bindings, links, contexts, desk, keys;
  - Show: context peers, AI facts;
  - desk: Screens subtitle and claim rows, Show network with right-click, Settings keys with buttons and right-click in step,
    diagnostics;
  - wire: `/ws`, HTTP routes, receipts on HTTP/WS/OSC/Companion/MCP through `ScreenDirector`;
  - pages: unchanged, since `common.js` ignores an `auth` without `view` and the control pages re-ask on `ok:false`;
  - the Companion module's labels; context and copilot risk; the papers.
- No key, PIN or window address in any journal row or frame: key names and node ids only. The rig's ready line reports its own test
  keys, as it reports the PIN.
- Lanes: `tools/test.sh` build, unit, module, holo and e2e green on Linux.
- The WPF pieces need the maintainer's Windows PC for the walk-through and UiSmoke; name that work as such. Windows-only checks go
  through `Bench.OnWindows`.

**Patterns** (`Patterns/CLAUDE.md`)
- The round number is the maintainer's: patterns-playmap-side places this as Round 90.
- The round opens by reading the newest `docs/field/round-NN`. There is none (`docs/field/` holds only `README.md`), so PLAN says
  "no field files this round". It then reads `docs/OPEN.md`: L50 and L56 stay open, noted.
- The PLAN section (the next free §, §107 or later) names the requester and the evidence class: a handed-in critique, the cross-repo
  gap analysis.
- One commit per unit (`Round N.1: …`, `Round N.2: …`) with the maintainer's authorship. Each is validated by `dotnet build` plus
  the suites it touches (Core, App) and the module's `npm test`.
- The papers:
  - PLAN (design, proof, what the round did not do: no desk control was added because a new key restarts the link, and the Eye,
    Nodes page, context and assistant are reached through `ShowApps`);
  - REVIEW with "Walk: unwalked";
  - CHANGELOG `## Round N — date — words`;
  - README (Nodes page words);
  - `docs/REMOTE.md` (the link URL and `bus.stagewatch.key`, last in its row, because STATE is matched by substring);
  - `docs/COMPANION.md` (`stagewatch_key`);
  - `docs/SHOWBUS.md`, copied word for word after spec-1-3's 1.3 cut;
  - OPEN rows;
  - a new QUALIFICATION section (§26 is round 87's, §27 round 88's; the next free number) with a row "a key refused by a real
    Stagewatch, then rebound";
  - the tag table row for the round before.
- The closing sentence: the round has no verb an output can show. Its fact is a log line ("Stagewatch link: refused by Hall A —
  this key speaks for patterns 1a2b3c4d"), read beside the Nodes page and STATE's `key`.
- All seven suites plus the module green before the push.

**Mapper** (`Mapper/CLAUDE.md`)
- A round-2 fix batch, recorded in `docs/round2/CARRYOVER.md`, with commits by area (`Engine: …`, `Net: …`, `Papers: …`).
- It touches no package's files, no spine file (`ContextBuilder.cs`, `Seams.cs`, `Core/Wire/*` untouched) and no catalogue row.
- `dotnet test tests/PlayMap.Net.Tests` and `tests/PlayMap.Engine.Tests` with the analyzers on. No fixture moves.
- `docs/SHOWBUS-NODE.md:37` becomes "a node on Stagewatch's Show network, never a screen; the key's verdict read". `docs/WIRE.md` adds
  `refused` to `links.stagewatch.state`.

**AGMessage** (no agent brief)
- `npm test` in `app/` (units, resilience, tls, server).
- `CHANGELOG.md` 0.4.1; `docs/NODES.md`; `docs/SHOWBUS.md` copied after spec-1-3's cut.
- The source guard (no key in a URL) keeps passing: `node=` and `cid=` carry the node id, never the key.

---

## 9. The review's items, answered

| # | item | answer |
|---|---|---|
| 1 | Rollout hazard: closes plus old peers' backoff reset gives a lockout | **Valid.** No new closes for a key reason (decision 6); unknown keys never count (decision 5), which also removes today's polling lockout; the lockout test in §4. |
| 2 | A shared key loses the timer, and legacy holders are misattributed | **Valid.** Conflicts keep frames (`verified:none` plus words); only node routes 403; legacy keyed sockets are listed by key, never by the binding's id (decision 4); a risk and a release note. |
| 3 | An unproven `node=` escapes Screens | **Valid.** Claims stay in Screens, flagged, out of `all`, reachable by id (decision 2). |
| 4 | Node rows are unbounded | **Valid.** Only proven ids add context rows; rows capped at 32; claims never in the context (§2.3). |
| 5 | Release reopens a race the new holder loses; retry 1 contradicts max(60, retry) | **Valid.** Release is an explicit rebind; the retry-1 path is gone (decision 9). |
| 6 | KeysChanged drops on a NodeOf mismatch | **Valid.** `Recheck` never closes on binding; a legacy socket never depends on it; a test (decision 8). |
| 7 | Existing tests break | **Valid.** Listed in §4. `ShowBusTests.cs:210-220` stays green by design (accepted legacy keys hear nothing new). AGMessage counts a 2xx by status. 400 becomes 403. |
| 8 | PlayMap's `paired` regression; chain unfollowed | **Valid.** `Paired or NotNeeded`; a `refused` state; preflight arm; the context item via state and detail without touching the spine file. |
| 9 | Peers' 403 words become false; collect refusals silent | **Valid.** Body `error` shown in Patterns, PlayMap and AGMessage; refused collect surfaced. |
| 10 | The refusal hold is lost to the close race | **Valid.** Recorded before I/O and honoured on every exit path; a test with an immediate abort. |
| 11 | An "injected show clock" test cannot be written | **Valid.** A pure `Hold` in Core plus a count-based App test. |
| 12 | Peer-chosen names shadow screen targets | **Valid.** Fixed order; node refusal only by id or a key-verified node's name (decision 11). |
| 13 | "A 401 at upgrade gives only an exception string" is wrong | **Valid**, corrected in §0. The 401 is still not chosen for key problems: it would stop the timer on an open Stagewatch. 400s at upgrade are used for malformed node requests. |
| 14 | `ApiKeys.FromSecret` | **Valid.** Dropped; the rig uses `Create` and reports its keys. |
| 15 | `IShowAccess.Nodes` null fails open | **Valid.** `NodeBindings.For(this)` by default, plus the request-visible checks in `WebServer` (decision 14). |
| 16 | Admission details unspecified | **Valid.** Key in URL, viewing code order (one message), own id, Window scheme, `role=control`: all in §2.1. Binding refusals never count. `Bound` carries the kind. |
| 17 | Patterns process: QUALIFICATION section, no field files, Every reader | **Valid.** The next free QUALIFICATION §; "no field files"; the Eye, Nodes, context and assistant reached through `ShowApps`; no "dial again now" needed, and the papers say why. |
| 18 | The SHOWBUS copies freeze a partial 1.3 | **Valid.** The words go to spec-1-3's single master edit; the peers' copies depend on its cut. |
| 19 | AGMessage's `stage` emit reaches participants | **Valid.** A closed status set for all; the words to admins only. |
| 20 | Over-building; phase it | **Partly valid.** Phase 1 holds the independent units that remove every harm; phase 2 the node link and desk. Persisted bindings stay, but in phase 2 and without the release race. The in-band verdict stays for node links only, because sibling designs (spec-1-3 §12.4 leases, windows-for-peers' grants, agmessage AG-5) name a key-verified node link. |
| — | The missing items | All folded in: the old-peer test, the shared-key test, changed tests, peer 403 chains, PlayMap preflight and context and ownership, AGMessage admin words (a new key restarts the link, so no "dial again now"), Patterns' Every reader, the stronger-proof rule, PIN changes on live sockets, the unwritable Patterns id, the Window scheme, the matching order. |

**Not done here** (for WHATS_NEXT): a viewing code set or changed while public screens are open still leaves them viewing until they
reconnect. That is the viewing code's own "works at once" question, and outside this area.
