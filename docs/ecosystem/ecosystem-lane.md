# The ecosystem lane: four real apps on a one-kernel LAN, plus the network's one verdict and one report

Area: ecosystem-lane. Gaps: G44, G49. This is a design only: nothing in any repo was edited or built. This is the revised version.
Every critique item was checked against the code. §10 answers each item, says what was changed, and says what was rejected and why.
New measurements are in `lane-probe2/` and `lane-probe3/` beside `lane-probe/`, under
`<scratch>/`.

This design is the "interop lane" that the other areas point at:
- cross-app-proposals (its optional interop lane);
- agmessage (its `agmessage.e2e.cjs`);
- playout-links (a real `PlayMap.Node`);
- spec-1-3 (`tools/showbus-copies.sh`, "only the interop lane sees a stale copy", decision 10);
- windows-for-peers (the grant end to end);
- patterns-playmap-side (SW-ST's rig `stall`).

Their scenario files move into this lane's folder. This lane adds no feature to any of them.

## What the maintainer gets

1. **One command.** `tools/test.sh interop` runs on the cloud bench or in CI. It builds the four apps in clones the lane owns,
   then runs them on a **one-kernel LAN**:
   - each app in its own network namespace, on one bridge, with its own address and its own loopback;
   - the real UDP 9928 and real keys and tokens;
   - nothing touches the host's network or another session's processes;
   - every process dies with the run.

   The four apps are:
   - Stagewatch's real servers, in the rig;
   - Patterns' real desk services, booted headless;
   - PlayMap's real engine, headless;
   - AGMessage's real server.
2. **A matrix with one row per cross-app fact.** Examples:
   - "PlayMap pairs with Patterns by proof and follows its blackout";
   - "Patterns' cable pulled, PlayMap lets go, the cable back, it pairs again";
   - "the rig killed and restarted, every follower back";
   - "no secret in any log, frame, STATE, context or report";
   - "no app locks another out of Stagewatch".

   Each row says pass, fail or not run, with its measured time and its evidence files.
3. **Three topologies, each a run of its own:**
   - **LAN:** separate hosts on one switch;
   - **laptop:** all four apps on one host with its Wi-Fi up, which is the maintainer's real rig;
   - **offline:** one host with only loopback.
4. **Releases wait on the lane.** Stagewatch's release waits for the lane at the siblings' pinned commits. A nightly run at the
   siblings' heads reports drift.
5. **Two G49 pieces:**
   - **One verdict for the doors:** `GET /api/preflight`, the operator frame's `network`, Companion's `network_ready`, MCP's
     `get_preflight`, the desk's Show network line and the copilot's facts.
   - **One report that names every app:** verified peers' recent lines, and the desk's decisions with their outcomes, in
     `/report` and `/api/report/network.csv`.

---

## 0. What exists (evidence)

### No cross-app test exists today (G44)

Every interop test plays the other app with a script:
- Stagewatch: `tests/Stagewatch.Tests/ShowBusTests.cs:226-289`;
- PlayMap: `Mapper/tests/PlayMap.Net.Tests/StagewatchLinkTests.cs:17-20`;
- AGMessage: `AGMessage/app/test/tls.test.js:91-118` (a `fakeDesk`);
- Patterns: `Patterns/tests/Patterns.App.Tests/ShowBus12AppTests.cs:131` (a `FakeStagewatch`).

The walk rows are unwalked: `Patterns/docs/QUALIFICATION.md` §26 and §27, and `Patterns/docs/OPEN.md:37,40,43` (L47, L50, L56).
ECOSYSTEM's conformance suite is only a risk note (`Stagewatch/docs/ECOSYSTEM.md:153-154`).

### Stagewatch (the hub)

**The rig is the only Stagewatch that runs on Linux.** The desk is WPF (`src/Timer.App/Timer.App.csproj:5,9`).
- The rig hosts the real `WebServer`, `CompanionServer` and `OscServer` around a simulated show
  (`tools/Stagewatch.Rig/Program.cs:9-14`).
- Options: `--port 19940`, `--companion 19941`, `--osc 19942`, `--pin` (default `4321`, from argv), `--bind local|all`, `--state`,
  `--view`, `--room`, `--exit-after` (`:20-45`).
- The node id is random for each process (`:66`).
- The ready line is stdout line 1. It prints the PIN and every window's `path` `/w/<token>` (`:132-136`).

**What the rig lacks:**
- No hello, peers, journal or desk: `Context()` passes empty peers and an empty journal (`:296-301`).
- No API keys: `IShowAccess.ApiKeys` defaults to empty (`src/Timer.Core/ShowState.cs:503`).
- No report: `/report` answers 404 (`WebServer.cs:414-419`).
- Other areas add most of this:
  - the hello and `--bus`: spec-1-3 S2;
  - `--node-keys N`, peer contexts and context peers: peers-not-screens SW-6;
  - `DeskService`: cross-app-proposals SW-4;
  - `--playout`: playout-links unit 5;
  - grants: windows-for-peers SW-W5;
  - a `stall <ms>` stdin line: patterns-playmap-side SW-ST (`design/patterns-playmap-side.md:573-574`).

**The lockout is one table per remote address, shared by every check.**
- Five failures a minute lock an address out of every PIN, key, viewing-code and window check (`WebServer.cs:625-631`,
  `:873-883`, `:1025-1028`, `:1077-1081`).
- A **missing** PIN counts as a failure: `CheckPin` counts `supplied is null` and answers "PIN required" (`:867-883`).
- An unknown key counts (`:1079-1082`).
- Any success removes the address's count (`:880`, `:1084`).
- There is no loopback exemption (`IPAddress.IsLoopback` appears only in the HTTPS rule, `:325`).
- `/api/context` needs read scope (`:444-448`).

**Stuck beat.**
- A stuck show thread sends `{"type":"beat","live":false}` and no frames (`WebServer.cs:1265-1271`).
- The show thread counts as stuck 3 s after the last `MarkAlive` (`ShowState.cs:216-226`).
- The WS loop re-checks only after waiting up to `HeartbeatEvery` (2 s) (`:1268`, `:1280`, `:1295`).
- Pings are answered off the show thread (`:1349-1355`).
- Companion is told `PONG … STUCK` (`src/Timer.Companion/CompanionServer.cs:399`), and the module tracks it
  (`companion-module/src/connection.ts:193-196`).

**Lanes.**
- `tools/test.sh` runs `build unit module holo e2e` by default and refuses an unknown lane (`tools/test.sh:20,61`).
- The e2e harness spawns the rig on free ports (`tests/e2e/rig.cjs:40-41`).

**CI.** `build-test-package` and `smoke` run on Windows, and `linux` runs `tools/test.sh`. `release` needs all three
(`.github/workflows/ci.yml:25,69,95,140`). Permissions are `contents: read` (`:13-14`).

### Stagewatch facts behind G49

**Peer lines lose their time.**
- `PeerContexts.Parse` keeps `recent` as text only. It drops `at`, keeps the last 20 lines (`ShowBus.cs:96`), and holds a
  context for 30 s, at most 32 peers (`:42-43`).
- `Accept` stores the context under the `node.id` in its body (`:56-69`).
- With a node key, `AcceptPeerContext` calls `Desk.BindKey` and ignores its result (`MainViewModel.Desk.cs:65-70`). This is G15:
  any node key can post as another node.

**Patterns' context arrives by two roads.**
- It is posted to `/api/peers/context`.
- It is also pulled through `PatternsLink` and parsed beside the posted ones, never through `Accept`
  (`MainViewModel.ShowBus.cs:79,92,115,126`).

**The report reads only Stagewatch's own journal.**
- The AI section takes the Note rows that match `AiPrefixes` (`Journal.cs:206-209`).
- The desk writes "X proposed: …", "X: … — done" and "Approved (X): …" (`MainViewModel.Desk.cs:108,124,139`). None of these
  matches a prefix.
- A rejected shared proposal writes no row at all (`:127-129`).
- So §11's evidence is broken.

**Culture.** `ToCsv` formats times with no culture (`Journal.cs:214`). Stagewatch's own context `recent` is
`$"{…:HH:mm:ss} …"`, also in the current culture (`src/Timer.Show/ShowContext.cs:136`).

**Stagewatch's own context.**
- Its `light` is the worst of its items (`ShowContext.cs:105`).
- Its headline adds the first red or amber item (`:115-116`).
- So any item appended there colours Stagewatch itself.

**The show network is passive.** Producer → Show network shows the peers' lights and problems and gives no verdict
(`MainViewModel.ShowBus.cs:89-108`).

**Key scopes.** Every key allows read scope (`src/Timer.Core/ApiKeys.cs:81-82`). MCP has `get_context` (`src/Timer.Remote/McpEndpoint.cs:108,156`).

### Patterns

**The exe and its headless boot.**
- The exe is an Avalonia desktop app (`src/Patterns.App/Program.cs:105`, `UsePlatformDetect`). `App.axaml.cs:19` builds the desk
  only under a classic desktop lifetime.
- The proven headless path is the test boot, `TestApp.Boot` (`tests/Patterns.App.Tests/TestApp.cs:138-170`). It:
  - creates `AppServices` and sets `Instance`;
  - creates `MainViewModel`;
  - **then sets the test knob `vm.PollLanes.BudgetMs = 10_000` before the window is made** (`:155-160`);
  - then makes `MainWindow`, attaches it, shows it and runs `RunJobs` once (`:161-164`).
- CI's `lifecycle` lane boots and closes that desk 100 times on Ubuntu (`.github/workflows/build.yml:40-56`).

**Ports and settings.**
- Control is TCP 9697 and HTTP 9696 (`src/Patterns.Core/Model/ShowState.cs:2244-2250`).
- The token is `Control.Token` (`:2274`).
- The Stagewatch link is `Control.Stagewatch` {Enabled, Host, ApiKey, FollowStage, Pins} (`:2340-2378`).

**Hello.**
- The hello is on when `ShowBusHello.MayAnnounce(Control.Enabled, Control.Bind)` (`src/Patterns.Devices/BeaconService.cs:103-113`).
- It is sent to broadcast and to 127.0.0.1 (`:312-327`).

**The Stagewatch link.**
- **It reads Stagewatch's `/api/context` every ~5 s whether or not it has a key** (`src/Patterns.App/Services/StagewatchLinkService.cs:347-351`).
  With no key the request carries no credential (`:358-362`, `:367-380`).
- It posts its own context only with a key (`:390-392`), and uses the approval desk only with a key and the hello's `approvals`
  cap (`:353`).
- A typed host gets that cap only if it matches a heard peer's address and port (`:225-243`).

**Secrets at rest.**
- The settings file has a `.bak` (`src/Patterns.Core/Services/SettingsStore.cs:7,111`).
- Timed copies go to `backups/patterns.settings.<stamp>.json` (`:49,330-345`).
- The recovery record `patterns.recovery.json` and its `.bak` hold "the same secrets the settings hold"
  (`src/Patterns.Core/Services/Resilience.cs:230-234`; `RemoteAdmin.cs:125-133`).
- This is the at-rest storage that OPEN.md's L40 already tracks.

**The assistant.**
- Its proposals go through `AssistantPage.ApplyProposal(chip)` (`src/Patterns.App/ViewModels/AssistantPage.cs:379-396`).
- With an approval desk linked, it sends `Approvals.Add(…, () => (true, ApplyHere(chip, p)), chip.Said)`.
- With none, it applies locally.
- `ApplyHere` opens EDIT SAFE for anything that draws, so a look lands in the preview, never on air (`:398-413`).
- A proposal is `AssistantProposal { Kind, Title, Looks = [LookPart(name, hotkey, pattern, overlays)] }`
  (`src/Patterns.Assistant/Assistant.cs:776-822`).

**Proof.** `AUTH-PROOF` is HMAC-SHA256 over the **normalised** token: no dashes or spaces, upper case
(`src/Patterns.Core/Services/ShowBusNetwork.cs:322-324`).

**Platform limits.**
- Video needs libVLC (`src/Patterns.App/Services/VideoEngine.cs:647-667`). The tests swap the `SourceFactory`
  (`tests/Patterns.App.Tests/AudioFakes.cs:156-172`).
- Web pages are WebView2 only (`src/Patterns.App/Services/WebEngine.cs:13-14`).

**Fences.** The banned-API list applies to every project whose name does not end in `.Tests` (`Directory.Build.props:26`).

### PlayMap (Mapper)

**The headless engine.**
- `tools/PlayMap.Node` runs `engine --headless --home … --no-recover` (`src/PlayMap.Engine/EngineOptions.cs:5-7,27-96`).
- With no OpenGL it runs on `NullBackends`: "the node plays and answers, it draws nothing" (`src/PlayMap.Engine/Rendering/RenderHost.cs:91-98`).
- Mapper's module e2e already writes `playmap.node.json`, spawns the engine and waits for "is up: wire N, web N"
  (`integrations/companion-module-playmap/test/e2e-engine.test.ts:36-55,98,117`). CI runs it (`Mapper/.github/workflows/build.yml:154-162`).

**Settings and pairing.**
- `NodeSettings` holds `nodeId` (8 hex), `name`, `bind`, `httpPort`, `wirePort`, `syncPort`, `token`, `trustLoopback`,
  `trustedAddresses`, `requireHttps`, `announce`, `patternsHost`/`Token` and `stagewatchHost`/`Key`/`Enabled`
  (`src/PlayMap.Core/Bus/Pairing.cs:101-129`).
- The proof keys the HMAC with `Normalise(token)` (`:39-47,53-58`).

**STATE.** The blackout is `air.blackout` (the effective one) and `air.patternsBlackout` (`src/PlayMap.Engine/StateWriter.cs:86-88,103-104`).
There is no top-level `blackout`.

**Context.** Air is amber whenever PlayMap is blacked out, Patterns' blackout included, or when a test pattern is up
(`src/PlayMap.Engine/ContextBuilder.cs:26-29`). Missing media, faults and unopened outputs are red (`:53-54`, `:64`, `:110`).

**Links.**
- Patterns link: stale after 6 s, dropped after 12 s, PING every 2 s. Its blackout is followed only while the link is Live or
  Stale (`src/PlayMap.Net/Links/PatternsLink.cs:60-67,95-97,183`).
- Stagewatch link:
  - a stuck beat sets `_stuck`, and the pong path leaves it alone (`StagewatchLink.cs:378-391`);
  - its key rides in `Authorization` (`:278-279`, `:488-489`);
  - it posts its context only with a key (`:469-472`).

**Media.** A colour item plays for 10 s by default (`src/PlayMap.Engine/EngineCore.Transport.cs:136-137`). `SET <id> <field> <json>`
changes it (`src/PlayMap.Core/Wire/VerbCatalog.cs:233`).

### AGMessage

**Start.**
- `node server.js` with `PORT`, `HOST`, `AGM_DATA_DIR`, `AGM_KEY_DIR`, `ADMIN_TOKEN` and `AGM_TLS` (`app/server.js:43-56`).
- Uploads go to `app/uploads` beside the code, whatever `AGM_DATA_DIR` says (`:45,51`).
- It is ready when stdout says "is running". It prints its admin token by design (`:1248-1256`).

**Admin.**
- Admin is from loopback only (`:262`).
- The `admin:settings` reply includes `contextToken` (`:855-860`).

**The Stagewatch link.**
- Context fetch and push happen only with a key (`app/lib/nodes.js:387-405`).
- **Every non-beat message refreshes `lastHeard`, and the link pings each tick.** The pongs come back while Stagewatch is stuck,
  so the link never goes stale (`:325-329`, `:348-356`). This is G04's AGMessage half, and design/agmessage.md has no unit for
  it.

**Packages.** No package has an install script (`app/package-lock.json` has no `hasInstallScript`).

### Show Bus copies

- The master says "A copy of this file lives in each repository; this one is the master" (`Stagewatch/docs/SHOWBUS.md:6`).
- Copies exist in Patterns and AGMessage (`Patterns/docs/SHOWBUS.md`, `AGMessage/docs/SHOWBUS.md`).
- **Mapper has none today** (`Mapper/docs` has only `SHOWBUS-NODE.md`). spec-1-3 decision 10 adds one.
- spec-1-3 decision 1: one master edit (S1 draft, S8 final), and every other area supplies words.

### What each app puts in `recent` (audit for the report)

- Patterns: its journal rows as "kind target — outcome: message (origin)". "Never a secret (the journal holds none since round
  75)" (`Patterns/src/Patterns.Core/Services/ShowContext.cs:196-203`).
- PlayMap: its journal's last rows, refused rows marked. Secrets never reach the journal (`Mapper/src/PlayMap.Engine/ContextBuilder.cs:12,123`).
- AGMessage: "role-level events, no content", cut to 160 characters (`AGMessage/app/lib/context.js:22,95`).
- Stagewatch: its own journal's last 20 (`ShowContext.cs:136`).

Each is designed to be shared. Origins can name a controller's address, which is the operator's to give to a client.

### Measured on this bench (Linux 6.18, node 22.22, Python 3.13; the kernel has USER_NS, NET_NS, BRIDGE and VETH, and **no DUMMY**)

**One shared namespace** (`lane-probe/`; and `lane-probe2/` for the veth and the default route). Sockets were bound to
`0.0.0.0:9928` with `reuseAddr`.

| Send to | Host network | Loopback only | One namespace with a veth at 10.99.0.1/24 and a default route |
|---|---|---|---|
| 255.255.255.255 | every listener hears it, from eth0's address | `ENETUNREACH` | every listener, from 10.99.0.1 (with no default route: `ENETUNREACH`) |
| 127.0.0.1 | **one** listener of several | one listener | one listener |
| 127.255.255.255 | every any-bound listener | every any-bound listener | every any-bound listener |

**Source addresses** (`lane-probe2/src.cjs`, `src2.cjs`):
- Dialling 127.0.0.2, 127.0.0.3 or 127.1.2.3 still arrives from **127.0.0.1**.
- In one namespace, secondary addresses in the same /24 arrive from the primary.
- /32 primaries arrive from themselves, but a typed address that differs from the hello's breaks the apps' hello matching (see
  decision 6).

**A namespace per host on one bridge** (`lane-probe3/lan.py` + `host.cjs` + `run.sh`). Each of rig, Patterns, PlayMap and
AGMessage was in its own namespace, on a veth into `br0`, with `10.99.0.N/24` and a default route.
- Every host heard every other host's broadcast **from that host's own address**.
- Each 127.0.0.1 copy reached only its own host.
- Every server saw each dialler **from its own address**.
- Pulling one host's cable (its bridge port down) cut it off both ways. The others kept hearing each other. Up restored it.

**Process lifetime** (`lane-probe3/pidns.sh`):
- `unshare -rnpf --mount-proc`: when the namespace's first process exits, the kernel kills everything inside, including a
  process inside a host namespace and the namespace holders.
- `timeout 2 unshare -rnpf --mount-proc --kill-child …` left nothing behind.

**Consequences:**
1. A LAN of separate hosts can be built on one kernel, unprivileged, with no network tools installed. It gives each app its own
   address (so Stagewatch's per-address lockout no longer couples the apps), its own port space (every app on its default
   ports) and its own loopback.
2. The maintainer's real rig, everything on one laptop, is a different topology: one namespace. There the 127.0.0.1 lottery
   and the shared lockout address are facts the lane must show, not hide.
3. A listener the driver adds on 9928 changes who gets the unicast copies, so the driver never listens on 9928.

---

## 1. Decisions

1. **The lane lives in Stagewatch.**
   - `tools/test.sh interop`, with the driver in `tests/interop/` and the pins, builds and the LAN helper in `tools/interop/`.
   - *Why:* Stagewatch is the hub, keeps the master spec and owns the rig. Its `tests/e2e` already runs Node and the rig. Six other
     designs point here.
   - *Alternatives:*
     - A fifth repository: one more tree and CI for one maintainer, with no rig.
     - Slices in each repo: these never run the apps together.
     - Mapper: round-3 ownership forbids new files outside its packages.

2. **Each app runs as its real self on Linux.**

   | App | Runs as | What it is not |
   |---|---|---|
   | Stagewatch | the rig, with the shared services the other areas put in it (`DeskService`, `PeerContexts`, node keys, `PlayoutLinks`, the hello) | the WPF desk's mirror and right-click menus (UiSmoke, on Windows) |
   | Patterns | `tools/Patterns.Bench`: the real `AppServices` + `MainViewModel` + `MainWindow` on Avalonia's headless platform, booted by the same `HeadlessDesk` the tests use | the exe's start and watchdog, libVLC, WebView2 |
   | PlayMap | `PlayMap.Node engine --headless`, as Mapper's module e2e runs it | Direct3D 12, the desk |
   | AGMessage | `node app/server.js` | the Windows zip's launcher |

3. **The default topology is a one-kernel LAN. Laptop and offline are runs of their own.**
   - `tools/interop/lan.py` builds the LAN: a bridge in the switch's namespace (10.99.0.1, the driver) and one namespace per app,
     with each app on its default ports:
     - rig 10.99.0.2;
     - Patterns 10.99.0.3;
     - PlayMap 10.99.0.4;
     - AGMessage 10.99.0.5.
   - The whole run lives in `unshare --user --map-root-user --net --pid --fork --mount-proc --kill-child`.
   - *Why:*
     - The machine's UDP 9928 and ports are shared with every other session (another session's Patterns App tests open a hello
       on 9928, `BeaconService.cs:103-113`).
     - Separate hosts give each app its own lockout address.
     - A cable can be pulled for real.
     - The PID namespace reaps every process when the run ends, a crashed driver included.
     - All of this is measured (§0).
   - **The laptop topology** puts all four apps and the driver in one namespace with one veth "Wi-Fi" and a default route. It is
     the maintainer's rig, where the shared address and the one-listener 127.0.0.1 copy are the facts under test.
   - **The offline topology** is one namespace with only loopback.
   - *Alternatives:*
     - One shared namespace (the review's recipe). It still puts every app behind one address. Its `dummy` link is not in this
       bench's kernel (§0), and veth works.
     - Distinct 127.0.0.x addresses per app: measured, Linux dials them all from 127.0.0.1.
     - The host's own network with a `flock`: it neither isolates from other sessions nor separates addresses.
   - The `flock` is dropped. Two runs on one machine share nothing but CPU.

4. **The lane builds in clones it owns. Working trees are opt-in.**
   - `tools/interop/build.sh` makes `git clone --shared --no-checkout <sibling> $SW_INTEROP_OUT/trees/<app>` and checks out the ref
     detached:
     - on the bench, the sibling's `HEAD`, with the count of uncommitted files named in the matrix as "not included";
     - with `SW_INTEROP_PINNED=1` or in CI, the pin.
   - A pin missing from a shallow sibling is fetched **into the clone** from the sibling's `origin` URL, never into the sibling.
   - It builds there and runs `npm ci --ignore-scripts` there. AGMessage's `uploads/` then lands in the clone.
   - `SW_INTEROP_LIVE=1` builds in the working trees, and the operator takes on each tree's "one build at a time".
   - *Why:* the default must not rewrite another session's `obj/`, `node_modules` or `uploads/` (`server.js:45,51`).
   - *Alternative:* `--artifacts-path` into a lane folder. It still restores against the shared tree and does not cover `npm ci`.

5. **Siblings are pinned in CI. The heads run nightly.**
   - `tools/interop/siblings.json` pins full commits. CI checks them out and `release` waits for the job.
   - A scheduled workflow runs the heads and gates nothing.
   - *Why:* a sibling's push must not turn a Stagewatch pull request red unless Stagewatch changes too. Drift is still seen within
     a day.

6. **Typed addresses are the addresses the hello comes from.**
   - In the LAN, each app is given the rig at 10.99.0.2 and Patterns at 10.99.0.3. Those are the addresses their hellos arrive
     from, so Patterns' typed host matches the heard peer and keeps its `approvals` cap (`StagewatchLinkService.cs:225-243`).
   - In the laptop topology, the typed address is 127.0.0.1. That is how the maintainer types it, and the cap is then missing, which
     is a fact the laptop rows record.

7. **A row exists only when it can pass. It lands with the code it proves, and "today" is a reading of the code until it runs.**
   - A known gap is never a skipped or expected-to-fail row.
   - A row lands in the Stagewatch commit that pins the sibling commit that makes it pass.
   - The "today" rows land in SW-L2 only after they pass on the bench. One that fails becomes a named finding for its owner (§7),
     not a row.
   - "Not run" exists only for an absent tree, a pin it cannot fetch, or a kernel without user namespaces.
     `SW_INTEROP_REQUIRED=1` (CI) turns it into a failure.
   - The plan of rows is §3, on paper.

8. **Every assertion reads the app's own published state. The driver never binds UDP 9928.**
   - Patterns: STATE over its wire, after pairing by proof.
   - PlayMap: STATE, `NODES` and `CONTEXT` over its wire, paired.
   - AGMessage: `admin:settings`, read by an admin helper **inside AGMessage's namespace on its 127.0.0.1**, because admin is loopback
     only; plus the room sockets.
   - Stagewatch: frames, `/api/context`, proposals, `/api/windows`, `/api/preflight`, `/report`, receipts, and the rig's `auth` line.

9. **No app is given a Stagewatch key until the rig makes keys. The rig's lockout table is read after every row.**
   - Until peers-not-screens SW-6 (`--node-keys N`) is pinned, PlayMap, AGMessage and the bench get **no** key. A key the rig does not
     know is counted against the app's address on every redial and poll (`WebServer.cs:1079-1082`).
   - After every row the driver asks the rig `auth` and asserts:
     - the switch's (driver's) address 0, PlayMap's 0 and AGMessage's 0;
     - Patterns' count is recorded, not asserted.
   - A keyless Patterns reads `/api/context` every ~5 s with no credential, and each read is counted as "PIN required"
     (`StagewatchLinkService.cs:347-351`; `WebServer.cs:444-448,867-883`). On one laptop that address is 127.0.0.1, so a keyless
     Patterns locks the laptop's own browsers out of Stagewatch within a minute. This is finding F1 (§7).
   - Row S7.4 ("no app's address is ever counted") lands when every app is keyed and F1's fix is pinned. From then on, every count
     is asserted 0.

10. **Deadlines come from the contract. Times are recorded. Nothing is retried.**
    - deadline = the contract number × 1.5 + 2 s:
      - a hello 2 s → 5 s;
      - stale 6 s → 11 s;
      - gone 12 s → 20 s;
      - a context 5 s → 9.5 s;
      - stuck (StuckAfter 3 s + HeartbeatEvery 2 s) → 9.5 s;
      - recovery after a stall (HeartbeatEvery) → 5 s.
    - The numbers come from SHOWBUS §13's `showbus` block once spec-1-3 S8 lands. Until then they are constants that cite §2 and §4
      and `ShowState.cs:216`, `WebServer.cs:1295`.
    - A stall lasts at least `StuckAfter + 2 × HeartbeatEvery + 5 s` = **12 s**, so the stuck beat is certain before it ends.
    - Rows measure from the rig's reported start and end, never from when the driver sent.

11. **Secrets are made for each run, handed over off argv, swept everywhere, and redacted only where a route returns them by design.**
    - **The register:** the PIN (`--pin-file`), the node keys the rig makes (`--secrets-out`), an MCP key, Patterns' token,
      PlayMap's token, AGMessage's admin token, and what the lane learns: AGMessage's context token and the window and grant
      tokens.
    - **How each is handed over:**
      - 0600 files;
      - the apps' own settings files;
      - AGMessage's `ADMIN_TOKEN` environment variable. That is its only door; the environment is readable only by the same user,
        and is not in `ps`.
    - **Redacted at capture**, by name. Each of these is a route returning a secret to the credential that asked:
      - `/api/windows` `path` with the PIN (`docs/REMOTE.md:264`);
      - `admin:settings` `contextToken` (`server.js:859`).
    - **Exempt by name, each a file meant to hold a secret:**
      - `playmap.node.json`;
      - Patterns' `patterns.settings.json` and `.bak`, `backups/patterns.settings.*.json`, and `patterns.recovery.json` and `.bak`
        (OPEN.md's L40 and its recovery twin);
      - the rig's `--state` file (window tokens), `--pin-file` and `--secrets-out`;
      - AGMessage's console banner `Token:` line (by design, `server.js:1253-1256`).
    - **Everything else is swept:** every stdout and stderr, log, journal, frame, STATE, context, report, CSV and capture.
    - The rig prints no secret when `--pin-file`/`--secrets-out` are given.
    - The exemption list never grows to make a row pass.

12. **Faults come from outside each app, through doors the app already has.**
    - **SIGKILL and restart** in the same home, address, ports and `--node-id`.
    - **A pulled cable:** `down <host>` / `up <host>` to `lan.py`, which sets the host's bridge port down. This is a half-open TCP,
      unlike a kill's reset.
    - **A stuck show thread:** SW-ST's `stall <ms>`. One stall mechanism, not two.
    - **No network:** the offline topology.

13. **Picture sources are synthetic where the platform has none, and the matrix says so.**
    - Patterns' clips are `BenchClip`, a wall-clock playhead in `Video.SourceFactory`.
    - PlayMap's VT is a colour item set to 60 s with `SET`.
    - Patterns' WebView2 is a Windows job or walk row.
    - `DISPLAY` stays unset, so PlayMap runs `NullBackends`, until a row needs PlayMap to draw (S5.2, SW-R6). Mesa and Xvfb arrive
      with that row.
    - Playwright arrives with the first browser row (S5).

14. **G49, piece by piece.**
    - **(4) The manifest is not built.** Show Bus 1.2's caps, ports and context commands do its job. ECOSYSTEM.md is corrected, and
      REMOTE.md's route table gains each route's key scope.
    - **(3) The merged report is built:**
      - only from **verified** contexts (posted with a node key bound to that id, or read over a link this Stagewatch paired);
      - each row says how it came;
      - from both roads (posted and link-read);
      - with no backlog on a node's first sight;
      - with a gap row when lines may be missing;
      - and the desk's decisions **including rejections**.
    - **(2) One preflight is built, read-only.** `ready` means no red row. It is served on its own doors and never in Stagewatch's
      own context, so the context stays one hop.
    - **(1) "Link all" is not built.** It is a security design of its own: the node link, pinned TLS and one approval per node.

15. **The preflight's expected set and its lights.**
    - **Configured relationships are expected:**
      - playout links;
      - node keys bound to an id (peers-not-screens);
      - window grants (windows-for-peers);
      - the Patterns link.
      A configured node that is gone or never heard is **red**.
    - **A node only seen** (heard in the last 10 minutes, at most 32) that goes away is **amber**: "went away at HH:mm". It drops
      out after 10 minutes.
    - **Amber is listed and never blocks.** `ready` = `red == 0`. Show Bus amber means attention (a PlayMap blacked out before
      doors is the normal state, `ContextBuilder.cs:26-29`).
    - *Why:* gone-red for nodes no one said were needed is alarm fatigue (an AGMessage closed after Q&A). A configured link that is
      gone is a real reason not to open the doors.
    - *Alternative:* a "Forget this node" or "Accept this amber" verb, with its parser, risk, five transports and menus. It is
      designed out: nothing a person did not configure can turn the verdict red by leaving.

16. **Stuck is never a confident stale verdict.** While `ProducerStuck` holds, or the cached verdict is older than 3 s:
    - `/api/preflight` answers `ready:false`, `stuck:true` and the red row "Stagewatch's show thread is stuck — no verdict until it
      turns over";
    - Companion's `network_ready` reads `unknown` (the module already knows `STUCK` and a lost connection).

17. **Patterns' headless boot is one seam with two halves.**
    - `HeadlessDesk.Create(store, profile)` → services and view model. `HeadlessDesk.Show(services, vm)` → window, attach, show, one
      `RunJobs`.
    - `TestApp.Boot` sets `PollLanes.BudgetMs` between the two, exactly where it does today. The bench calls both back to back.
    - The lifecycle lane's census must be unchanged before and after the refactor.
    - App.axaml.cs is untouched, so the exe's start path is not touched.

18. **The bench proposes as the assistant does.**
    - `propose look <title>` builds `AssistantProposal { Kind="look", Title, Looks=[LookPart(title, 0, null, null)] }` and an
      `AssistantChip`, and calls `vm.Assistant.ApplyProposal(chip)`.
    - It refuses before the call when `Kernel.Stagewatch.ApprovalsLive` is false, because otherwise `ApplyProposal` applies locally
      (`AssistantPage.cs:386-396`).
    - The approved look lands in the preview with EDIT SAFE open. The row asserts the air unchanged.

19. **A pin bump is a Stagewatch commit, and it may add rows.** Its subject is `Interop: <app> to <commit> (<rows>)`. The matrix
    header names each build, so a red row names the exact set.

---

## 2. Wire and file shapes (exact)

### 2.1 The rig (`tools/Stagewatch.Rig`)

**New options** (SW-L1). The other areas' `--bus`, `--node-keys`, `--playout` and the desk are dependencies only.
```
--node-id <id>        [A-Za-z0-9_-]{6,40}: the id in its hello, context, frames and desk (default "rig-" + random, as today)
--journal <file>|-    keep the show journal (JSON lines, the app's ShowJournal; "-" = in memory); /report and /api/report.csv answer from it
--pin-file <file>     read the PIN from the file's first line (an empty line = no PIN); never with --pin
--secrets-out <file>  write, mode 0600, every secret the rig would print: {"pin":"…","windows":[{"name","path"}],"nodeKeys":[{"id","secret"}]}
                      the ready line then carries names only
--exit-on-eof         standard input reaching its end stops the rig as Ctrl+C does
```

**The ready line** (stdout line 1; fields appended, `pin` changed):
```json
{"rig":"ready","http":19940,"companion":19941,"osc":19942,"pin":"set","node":"interop-sw-1",
 "windows":[{"name":"lobby"}],"bind":"all","journal":true,"secrets":"file",
 "bus":9928,"nodeKeys":[{"id":"k1"},{"id":"k2"},{"id":"k3"}],"playout":["patterns","stage-left"]}
```
- `pin`:
  - `"set"` when the PIN came from `--pin-file` or `--secrets-out` is given;
  - `"none"` for an empty PIN;
  - the value only when `--pin` is given with no `--secrets-out` (the `rig` lane's human and today's e2e constant).
- `windows[].path` and `nodeKeys[].secret` appear only without `--secrets-out`.
- `bus`, `nodeKeys` and `playout` appear when their areas land.

**Control lines on stdin.** There is one reader, created by whichever of SW-ST and SW-L1 lands first; the other adds its lines. Each
answer is one JSON line on stdout.
```
stall <ms>     (SW-ST) 1..30000: the ticker stops marking the show alive  → {"rig":"stalled","ms":12000,"at":<unix ms>}  … {"rig":"running","at":<unix ms>}
               a second stall while one runs                               → {"rig":"refused","line":"stall","error":"already stalled"}
auth           (SW-L1) the lockout table, read                             → {"rig":"auth","at":<unix ms>,"addresses":[{"address":"10.99.0.3","failures":4,"windowAgeMs":31200}]}
exit           (SW-L1) stops the rig as Ctrl+C does                        → {"rig":"exiting"}
<other>        refused, never guessed                                      → {"rig":"refused","line":"<first word>","error":"unknown (stall <ms>, auth, exit)"}
```
- `auth` lists only addresses whose window is still open (under 60 s). It holds no secret.

### 2.2 `WebServer.AuthFailures()` (Timer.Remote, new public read)

```csharp
/// <summary>The lockout table as it stands: addresses whose one-minute window is open, with their count. Read-only; no secret.</summary>
public IReadOnlyList<AuthFailureCount> AuthFailures();
public readonly record struct AuthFailureCount(string Address, int Failures, long WindowAgeMs);
```
The counting itself does not change in this area. F1's fix belongs to peers-not-screens SW-2 (§7).

### 2.3 The Patterns bench (`Patterns/tools/Patterns.Bench`, new)

```
dotnet Patterns.Bench.dll --home <folder> --token-file <file>
    [--http <port>] [--wire <port>]     Control.HttpPort / TcpPort (default 9696 / 9697)
    [--bind <address>]                  Control.Bind ("" = every interface, the default)
    [--name <words>]                    the show's and the hello's name
    [--stagewatch <host:port>|find]     Control.Stagewatch.Host ("find" = empty: the first heard); omitted = link off
    [--stagewatch-key-file <file>]      Control.Stagewatch.ApiKey (none = no key)
    [--trust-loopback on|off]           ControlService.TrustLoopback (default off)
    [--clips synthetic]                 Video.SourceFactory = BenchClip (the only kind; the ready line says so)
    [--control stdin] [--exit-after <s>]
```
- Before the boot it writes `patterns.settings.json` from `SettingsStore.Fresh()`:
  - Control: Enabled, ports, Bind, Token, Announce false, OSC off;
  - Stagewatch: {Enabled, Host, ApiKey, FollowStage true}.
- An unknown option is refused with words and exit code 2.

The ready line is stdout line 1:
```json
{"patterns":"ready","http":9696,"wire":9697,"node":"<ShowBusId>","build":"<informational version>","clips":"synthetic",
 "stagewatch":"10.99.0.2:19940","key":false,"trustLoopback":false}
```

Control lines:
```
clip <name> <seconds>      a synthetic clip on air (Media → Video, sandbox off)            → {"control":"clip","ok":true,"key":"<input key>"}
propose look <title…>      decision 18; refused with no desk                               → {"control":"propose","ok":true,"title":"…"}
                           then the chip's words as the desk moves it                      → {"proposal":"<title>","said":"…"} …
                           with no approval desk live                                      → {"control":"propose","ok":false,"error":"no Stagewatch approval desk linked"}
exit                                                                                       → {"control":"exit","ok":true}
<other>                                                                                    → {"control":"<word>","ok":false,"error":"unknown control (clip, propose look, exit)"}
```
- Stdout is one `StreamWriter` over `Console.OpenStandardOutput()`, because `Console.Write` is banned outside `.Tests`.
- With `--control stdin`, an end of input stops the bench.

### 2.4 PlayMap (no change): what the lane writes and reads

`<home>/playmap.node.json`:
```json
{"nodeId":"<8 hex>","name":"Interop PlayMap","token":"<32 upper hex>","bind":"","httpPort":<default>,"wirePort":<default>,"syncPort":0,
 "trustLoopback":false,"trustedAddresses":[],"requireHttps":false,"announce":true,
 "patternsHost":"10.99.0.3:9697","patternsToken":"<Patterns' token>",
 "stagewatchHost":"10.99.0.2:19940","stagewatchKey":"","stagewatchEnabled":true,"companion":{"mdns":false}}
```
- `stagewatchKey` stays `""` until SW-R1 (decision 9).
- Tokens are generated already normalised (32 upper-case hex, no dashes). `wire.cjs` computes the proof as `Pairing.Proof` does,
  over `Normalise(token)`.
- **Start:** `nsenter --net=<playmap ns> dotnet <clone>/tools/PlayMap.Node/bin/Release/net10.0/PlayMap.Node.dll engine --headless --home <home> --no-recover`.
- **Ready:** `playmap.engine.log` `is up: wire (\d+), web (\d+)`.
- **Wire:**
  1. `HELLO Interop`;
  2. `NONCE <hex>` → `AUTH-PROOF <hex>` → `OK paired`;
  3. `STATE`, `CONTEXT`, `NODES`, verbs with `@id`.
- **Blackout:** `air.blackout`, `air.patternsBlackout`.
- **VT setup:** `PLAYLIST NEW show`, `PLAYLIST ADD <pl> color:#202020 Intro`, `SET <item> durationNs 60000000000`,
  `PLAYLIST FOCUS <pl>`, `PLAYLIST GO <pl> #1`.

### 2.5 AGMessage (no change): what the lane does

- **Start:** `nsenter --net=<agm ns> node <clone>/app/server.js` with `PORT=3023`, `HOST=0.0.0.0`, `AGM_DATA_DIR`, `AGM_KEY_DIR`,
  `ADMIN_TOKEN=<random>`, `ANTHROPIC_API_KEY=` and `AGM_TLS=0`. Ready on "is running".
- **The admin helper** `tests/interop/apps/agmessage-admin.cjs` runs inside AGMessage's namespace (`nsenter --net=<agm ns>`). It reads
  the token from a 0600 file and speaks JSON lines on stdio:
  - `login` → `POST http://127.0.0.1:3023/api/admin/login`;
  - `settings <json>` → `admin:settings` (the reply's `contextToken` is redacted before it is written out; the driver gets it in a
    separate `{"secret":"context"}` line that is never logged);
  - `room <name>` → `POST /api/rooms`.
- `stagewatchUrl` is `10.99.0.2:19940`, and `stagewatchKey` stays empty until SW-R1.
- Research and board sockets join the room from the switch over the LAN.

### 2.6 `tools/interop/lan.py` (Stagewatch, new; stdlib only, Python ≥ 3.12 for `os.unshare` and `os.setns`)

```
python3 -I tools/interop/lan.py lan rig patterns playmap agmessage     # inside the run's user+net+pid namespace
python3 -I tools/interop/lan.py laptop                                  # one namespace, one veth "Wi-Fi" at 10.99.0.2/24, default route
python3 -I tools/interop/lan.py offline                                 # one namespace, lo only
```
- **Stdout line 1:** `{"topology":"lan","switch":"10.99.0.1","hosts":{"rig":{"addr":"10.99.0.2","ns":"/proc/<pid>/ns/net"},…}}`.
- **Stdin:** `down <host>` / `up <host>` → `{"cable":"<host>","state":"down|up"}`. Anything else →
  `{"error":"unknown (down <host>, up <host>)"}`.
- The holders wait on a pipe from `lan.py`, never on stdin, so they end with it.
- The driver spawns each app with `nsenter --net=<ns> …`. Everything dies with the PID namespace.

### 2.7 `tools/interop/siblings.json`

```json
{"v":1,
 "patterns": {"repo":"jammin808/Patterns","ref":"<40 hex>","build":"tools/Patterns.Bench/Patterns.Bench.csproj"},
 "mapper":   {"repo":"jammin808/Mapper","ref":"<40 hex>","build":"tools/PlayMap.Node/PlayMap.Node.csproj"},
 "agmessage":{"repo":"jammin808/AGMessage","ref":"<40 hex>","build":"app"}}
```
A branch name or an unknown key is refused.

### 2.8 `artifacts/interop/matrix.json` (and `matrix.md`)

```json
{"v":1,"at":"2026-10-25T03:17:00Z","machine":"ubuntu-24.04 6.8.0","topology":"lan",
 "builds":{"stagewatch":"<sha> 2.18.0-dev","patterns":"<sha> (3 uncommitted files not included)","playmap":"<sha> …","agmessage":"<sha> 0.5.0"},
 "rows":[{"id":"S4.5","title":"PlayMap follows Patterns' blackout, paired by proof","apps":["playmap","patterns"],
          "verdict":"pass","ms":1840,"deadlineMs":5000,"auth":{"10.99.0.1":0,"10.99.0.4":0,"10.99.0.5":0},"evidence":["logs/playmap.engine.log","state/playmap.jsonl"]}],
 "measured":{"authFailures":{"10.99.0.3":12},"ownItems":{"playmap":[{"id":"air","light":"amber"}],"patterns":[…],"agmessage":[…]},
             "peerRowsPerMin":{…},"reconnectMs":{…}}}
```

### 2.9 One preflight (G49 piece 2)

**`GET /api/preflight`**
- Auth: read scope (the PIN or any key), the same as `/api/context`.
- Rate headers are sent.
- A query string is refused: 400 `{"ok":false,"error":"/api/preflight takes no query"}`.
- A host with no preflight answers 404 `{"ok":false,"error":"no preflight here"}`.

```json
{"v":1,"at":1761362220000,"ready":false,"stuck":false,"red":1,"amber":1,"green":9,
 "words":"not ready: Stage left not heard for 14 s (+0 more) · 1 to look at",
 "rows":[
  {"id":"node:9f8e7d6c:heard","node":"9f8e7d6c","name":"Stage left","kind":"playmap","check":"heard","light":"red","configured":"playout link stage-left","words":"not heard for 14 s","fix":"is PlayMap running and on this network?"},
  {"id":"node:a1b2c3d4:heard","node":"a1b2c3d4","name":"AGM","kind":"messenger","check":"heard","light":"amber","configured":"","words":"went away at 21:40","fix":""},
  {"id":"node:5e6f7a8b:own","node":"5e6f7a8b","name":"FOH","kind":"patterns","check":"own","light":"green","configured":"","words":"its own parts are green","fix":""}]}
```

The checks join as their inputs land:

| check | from | for | green / amber / red |
|---|---|---|---|
| `heard` | SW-G49b | configured and seen nodes | live / stale (6–12 s), or a seen-only node gone / a configured node gone or never heard |
| `context` | SW-G49b | nodes with a context | ≤ 15 s / none yet or 15–30 s / — |
| `own` | SW-G49b | nodes with a context | the node's own worst item, with its top two labels |
| `link` | SW-G49b | this Stagewatch's links (`LinkItems`) | the item's own light |
| `window` | SW-G49b | windows (`ScreenWindows.ContextItems`) | the item's own light (a lobby screen is never red, as today) |
| `key` | after peers-not-screens SW-4 | nodes that post | verified by its bound key / posts with the PIN or a control key / refused |
| `desk` | after cross-app-proposals SW-3 | nodes whose hello says `approvals` | collected within the freshness window / — / silent past it |
| `clock` | after playout-links | links with a measured offset | within 2 s / apart / — |

- `ready` = `red == 0`.
- At most 200 rows, sorted red, amber, green, then by name.
- 32 nodes at most. The rest are named in one amber row: "8 more nodes not checked".
- **Stuck** (decision 16): `{"v":1,"at":…,"ready":false,"stuck":true,"red":1,…,"rows":[{"id":"stagewatch:show-thread","check":"stagewatch","light":"red","words":"Stagewatch's show thread is stuck — no verdict until it turns over","fix":""}]}`.

**The readers.** The verdict is never an item of Stagewatch's own context (one hop).
- **Operator frame:** a field after cross-app-proposals' `desk`, so the order ends `…,"desk":{…},"network":{"ready":false,"red":1,"amber":1,"stuck":false}`.
  The public frame never has it.
- **Companion module:**
  - variables `network_ready` (`yes`/`no`/`unknown`: unknown when stuck or not connected), `network_red` and `network_amber`;
  - feedback `network_not_ready`.
- **MCP:** a new tool `get_preflight` (no arguments, read). It returns the route's JSON. *Why:* with the item out of the context, MCP
  agents would otherwise have no reader.
- **The copilot's facts:** the red and amber rows (at most 12).
- **The desk (PC work):** Producer → Show network's header line reads one of:
  - "Show network: 4 apps · ready for doors";
  - "· ready · 1 to look at";
  - "· not ready: Stage left not heard for 14 s".

  Its tooltip lists the rows. Its right-click has "Copy the preflight", in step with a button of the same name. A help tip,
  `network-ready`.
- **The producer page:** a chip read from the operator frame.
- No new `ShowCommandKind`, so the copilot's risk does not change.

### 2.10 One report (G49 piece 3)

**`JournalKind`** gets `Peer` and `Desk`, appended. Kinds are written as strings (`Journal.cs:49`), so old journals still load.

**`JournalEntry`** gets three fields at the end, each written only when set:
```json
{"t":"2026-10-25T03:20:11.4Z","k":"Peer","i":3,"x":"PLAYLIST GO show — Intro","n":"9f8e7d6c","pt":1761362411022,"via":"node key 'Stage left'"}
{"t":"2026-10-25T03:20:15.0Z","k":"Desk","i":3,"x":"AGM proposed for this Stagewatch: message \"Two minutes\" warn off ack — p17-k3m9","n":"a1b2c3d4","via":"node key 'AGM'"}
{"t":"2026-10-25T03:20:31.0Z","k":"Desk","i":3,"x":"Rejected p18-k3m9 (operator, deck): UNDO for Stage left — not now","n":"9f8e7d6c"}
```
- `n` is the node id.
- `pt` is the line's time on that node. The row's `t` is when Stagewatch first heard it. Rows are never shifted by an unmeasured
  offset.
- `via` says how the context came: `node key '<name>'`, `the link`, `the PIN from <address>`, or `control key '<name>' from <address>`.

**The peer lines.**
- **Sources:** the **verified** contexts, posted or link-read, that cross-app-proposals' `DeskService` already gathers ("the verified
  contexts (posted or link-read)", `design/cross-app-proposals.md:540-543`). A context peers-not-screens marks `verified:"none"` is
  never journalled.
- `PeerContexts.Entry` keeps `Recent` (strings) and gains `RecentLines` (`PeerLine(long AtMs, string Text)`), so other readers do
  not churn.
- **`PeerLines`** (new, Core, pure) keeps, per node:
  - the high-water `at`;
  - the texts at that exact `at`;
  - the last list, for lines without `at`.
- **Fresh lines:**
  - lines with `at` above the high-water, or at it with a new text;
  - lines without `at`: the part of the new list after its longest overlap with the last list.
- **Bounds:** 32 nodes, least recently heard evicted.
- **A node's first sight, or its return after eviction:** no backlog. One row: "<name> joins the report; its earlier lines are in its
  own journal".
- **A known node whose post shares no line with the last one:** one row, "lines from <name> may be missing between HH:mm:ss and
  HH:mm:ss".
- The report and the CSV header say: "peer lines are a sample; each node's own journal is the record".

**The desk rows.**
- Every desk journal row is written as `JournalKind.Desk` with `n` and `via`: proposed, sent, approved, the outcome, **and
  rejected**, with who rejected it and why.
- They are written in `DeskService`'s executor path, which the WPF mirror and the rig share, never in two places.

**`/report`** (HTML)
- The AI section is the Note rows that match `AiPrefixes` together with every `Desk` row, in order.
- A last section, "The show network", lists the `Peer` rows with Stagewatch's time, the node, "its clock HH:mm:ss" and `via`.

**`GET /api/report/network.csv`** (read scope, CORS as `/api/report.csv`)
```
Time,Node,Kind,Node id,Node time,Via,Text
2026-10-25 04:20:11,Stage left,playmap,9f8e7d6c,2026-10-25 04:20:11,node key 'Stage left',"PLAYLIST GO show — Intro"
```
- Times are formatted with `CultureInfo.InvariantCulture`.
- `ToCsv` (`Journal.cs:214`) and the context's `recent` line (`ShowContext.cs:136`) get the same fix in the same unit.

**`ShowContext`**: Stagewatch's own `recent` leaves out `Peer` rows.

### 2.11 Words handed to spec-1-3 S1 (the single master edit, 1.3)

This area edits no SHOWBUS text. It hands spec-1-3 S1 two pieces of wording:
- **§6 (one sentence):** "A context posted to, or read by, a desk may appear line by line in that desk's show report and its
  operator's AI summary; put nothing in `recent` that the desk's readers may not see."
- **§8, Stagewatch's consumes cell:** "verified peers' `recent` → its show report".

The copies (Patterns, AGMessage, and Mapper's new one) follow spec-1-3's copy chain. Row S7.2 checks them.

### 2.12 CI

**`.github/workflows/ci.yml`**, a new job `interop`:
```yaml
  interop:
    name: Ecosystem interop (rig, Patterns bench, PlayMap node, AGMessage) on a one-kernel LAN
    if: github.event_name != 'pull_request' || github.event.pull_request.head.repo.full_name == github.repository
    runs-on: ubuntu-24.04
    timeout-minutes: 30
    env: { SW_INTEROP_REQUIRED: '1', SW_INTEROP_LIVE: '1', SW_INTEROP_OUT: '${{ github.workspace }}/Stagewatch/artifacts/interop' }
    steps:
      - uses: actions/checkout@v7
        with: { path: Stagewatch }
      - id: pins
        run: |
          for app in patterns mapper agmessage; do
            echo "$app=$(jq -er ".$app.ref | select(test(\"^[0-9a-f]{40}$\"))" Stagewatch/tools/interop/siblings.json)" >> "$GITHUB_OUTPUT"
          done
      - uses: actions/checkout@v7
        with: { repository: jammin808/Patterns, ref: '${{ steps.pins.outputs.patterns }}', path: Patterns, persist-credentials: false, token: '${{ secrets.SIBLINGS_READ_TOKEN || github.token }}' }
      - uses: actions/checkout@v7
        with: { repository: jammin808/Mapper, ref: '${{ steps.pins.outputs.mapper }}', path: Mapper, persist-credentials: false, token: '${{ secrets.SIBLINGS_READ_TOKEN || github.token }}' }
      - uses: actions/checkout@v7
        with: { repository: jammin808/AGMessage, ref: '${{ steps.pins.outputs.agmessage }}', path: AGMessage, persist-credentials: false, token: '${{ secrets.SIBLINGS_READ_TOKEN || github.token }}' }
      - uses: actions/setup-dotnet@v6
        with: { dotnet-version: '10.0.x' }
      - uses: actions/cache@v4
        with: { path: ~/.nuget/packages, key: "nuget-interop-${{ hashFiles('*/Directory.Build.props', '*/**/*.csproj') }}" }
      - uses: actions/setup-node@v7
        with: { node-version: '22', cache: npm, cache-dependency-path: "Stagewatch/tests/interop/package-lock.json\nAGMessage/app/package-lock.json" }
      - name: A one-kernel LAN needs user namespaces, a bridge and veth (this runner only)
        run: |
          sudo modprobe -a bridge veth
          sudo sysctl -w kernel.apparmor_restrict_unprivileged_userns=0
      - name: The interop lane
        working-directory: Stagewatch
        run: |
          npm ci --ignore-scripts --no-audit --no-fund --prefix tests/interop
          tools/test.sh interop
      - if: always()
        uses: actions/upload-artifact@v7
        with: { name: 'interop-${{ github.sha }}', path: Stagewatch/artifacts/interop, if-no-files-found: warn }
```
- `release` gets `needs: [build-test-package, smoke, linux, interop]`.
- **Pull requests from forks** skip the job. A step in `linux` writes "interop: not run (a fork's pull request gets no sibling
  token)" to the job summary. Releases run only on tags, where the job always runs.
- **The checkouts** keep no credential (`persist-credentials: false`). No sibling install script runs (`--ignore-scripts`). The
  token never reaches `.git/config`.
- **Xvfb, Mesa and Playwright** join the job with SW-R6.

**`.github/workflows/interop-heads.yml`** (new):
- Triggers: a schedule `23 3 * * *` and `workflow_dispatch` with optional refs.
- The same steps at the heads, with `SW_INTEROP_HEADS=1`.
- It gates nothing.

### 2.13 Lane environment

| Variable | Meaning |
|---|---|
| `SW_PATTERNS_DIR`, `SW_MAPPER_DIR`, `SW_AGMESSAGE_DIR` | the sibling trees (defaults `../Patterns`, `../Mapper`, `../AGMessage`) |
| `SW_INTEROP_PINNED=1` | bench: clone at the pins rather than at each sibling's `HEAD` |
| `SW_INTEROP_LIVE=1` | build and run in the trees themselves (CI's disposable checkouts; on the bench the operator owns the one-build rule) |
| `SW_INTEROP_REQUIRED=1` | CI: an absent tree, a pin it cannot fetch, no user namespaces or a row "not run" fails the lane |
| `SW_INTEROP_TOPOLOGY` | `lan` (default), `laptop`, `offline`, or `all` |
| `SW_INTEROP_OUT` | where the clones, matrix, logs and captures go (default `artifacts/interop`) |
| `SW_INTEROP_HEADS=1` | set by the heads workflow and said in the matrix |

---

## 3. Scenarios

"Needs" names the units that must be in the pinned commits. "Today" means the four trees as they are, plus this design's harness
and SW-L1. It is a reading of the code until the row passes on the bench (decision 7).

After every row: the rig's `auth` for the switch, PlayMap and AGMessage addresses is 0, and Patterns' is recorded. From S7.4 on,
every address is asserted 0.

### S0. Start

| Row | Reads | Deadline | Needs |
|---|---|---|---|
| S0.1 each app is ready in its own namespace | rig ready line (no secret in it); the bench's; PlayMap's `is up` (renderer named); AGMessage "is running" + `GET /api/health` 200 | 60 s each | today + P-B1 |

### S1. Discovery: the LAN (separate hosts)

| Row | Reads | Deadline | Needs |
|---|---|---|---|
| S1.1 Patterns hears PlayMap and AGMessage | Patterns STATE `bus.apps[]` holds `{kind:"playmap",id}` from 10.99.0.4 and `{kind:"messenger",id}` from 10.99.0.5 | 5 s | today |
| S1.2 PlayMap hears Patterns and AGMessage | `NODES` holds both ids | 5 s | today |
| S1.3 AGMessage hears Patterns | the admin helper's `peers[]` holds `{t:"patterns"}` | 5 s | today |
| S1.3b AGMessage hears PlayMap | the same with `t:"playmap"` | 5 s | agmessage AG-4 (G18) |
| S1.4 the rig hears all three | rig `/api/context` `peers[]` | 5 s | spec-1-3 S2, peers-not-screens SW-6 |
| S1.5 every app hears the rig | kind `main`, id = `--node-id`, from 10.99.0.2 | 5 s | spec-1-3 S2 |
| S1.6 one id is one peer | over 20 s each app lists each peer once | 20 s | spec-1-3 decision 6 in each reader |

Rows assert "contains", never an exact set.

### S1c. Discovery: the show laptop (one host, Wi-Fi up)

| Row | Reads | Deadline | Needs |
|---|---|---|---|
| S1c.1 every app hears every other on one laptop | as S1.1–S1.3, every hello from the laptop's address (the broadcast; the 127.0.0.1 copy reaches one listener, §0) | 5 s | today |
| S1c.2 PlayMap follows Patterns at 127.0.0.1 | as S4.5, typed 127.0.0.1 | 5 s | today + P-B1 |
| S1c.3 the rig on the laptop, and nobody locks the laptop out | as S1.4/S1.5; `auth` 127.0.0.1 is 0 after 120 s with every app linked | 120 s | spec-1-3 S2, SW-R1 keys, F1's fix |

### S1b. The offline laptop (lo only)

| Row | Reads | Deadline | Needs |
|---|---|---|---|
| S1b.1 typed 127.0.0.1 addresses link every pair | PlayMap↔Patterns paired; each app's Stagewatch link live; `auth` 127.0.0.1 is 0 | 20 s | SW-R1 keys, F1's fix (one shared address) |
| S1b.2 discovery with no network | as S1.1–S1.5 | 5 s | Show Bus 1.4's carrier (spec-1-3 decision 5, lab line LB-1) |

### S2. Contexts both ways

| Row | Reads | Deadline | Needs |
|---|---|---|---|
| S2.1 PlayMap's context reaches the rig | the rig's `peers[]` row for PlayMap equals PlayMap's own `CONTEXT` light and headline | 9.5 s | peers-not-screens SW-6; SW-R1 keys |
| S2.2 AGMessage's | against AGMessage's `/api/context` (its context token, held off the logs) | 9.5 s | as S2.1 |
| S2.3 Patterns' | against Patterns' wire `CONTEXT` | 9.5 s | as S2.1 + the rig hello |
| S2.4 Patterns reads the rig's context | `bus.stagewatch.headline` == the rig's | 9.5 s | as S2.1 |
| S2.5 AGMessage reads the rig's | `stageContext.headline` == the rig's | 9.5 s | as S2.1 |
| S2.6 PlayMap's context fits | bytes < 64 KiB, recorded | — | as S2.1 |
| S2.7 a node key cannot speak for another node | AGMessage's key posting PlayMap's id → 403 with words; the rig's row unchanged; **no Peer row** in `/report` | — | peers-not-screens SW-2 |

### S3. The desk

The driver's hand approves with Companion `@c1 DESK APPROVE <id>` and rejects with `DESK REJECT <id> <words>`.

| Row | Steps → reads | Deadline | Needs |
|---|---|---|---|
| S3.1 AGMessage → the rig | research `stage:propose` → rig pending, risk `review`, line `message "Two minutes" warn off ack` → approve → rig frame `message` → done → AGMessage `proposal.state` "done" | 20 s | cross-app-proposals SW-1–SW-4; agmessage AG-1, AG-2 |
| S3.2 the rig → AGMessage | `peer agmessage "notice Q&A in 2 minutes"` → the board's system message → done | 20 s | cross-app-proposals SW-4 |
| S3.3 Patterns → the rig → Patterns | bench `propose look Keynote blue` → rig pending → approve → Patterns STATE: the look is there, **EDIT SAFE is open and the air's look unchanged** → the chip says applied → rig done | 25 s | cross-app-proposals SW-1–SW-4; the rig hello; P-B1; PT-90.3 |
| S3.4 the rig → Patterns | `peer patterns "VIDEO STOP"` with a synthetic clip on air → `video.paused`, position 0 → done | 25 s | cross-app-proposals SW-4; PT-90.3 |
| S3.5 an MCP agent → PlayMap | `run_command {target, "PLAYLIST CUE show #1"}` → approve → PlayMap collects → done → STATE cued | 15 s | cross-app-proposals SW-10 |
| S3.6 PlayMap proposes | its sink posts → pending with PlayMap's `from` | 15 s | Mapper PM-P3 (owner's decision 7) |
| S3.7 a rejection never runs, and is reported | a second MCP proposal → rejected with words → 15 s later PlayMap's journal has no such line; `/report` has the `Desk` row "Rejected … — <words>" | 15 s | as S3.5 + SW-G49a |
| S3.8 reported in one place | `/report`'s AI section holds S3.1–S3.5 and S3.7 with their outcomes; `network.csv` holds at least one verified line from each of PlayMap, Patterns (posted or link-read) and AGMessage, each with `Via` | — | SW-G49a + the rows above |

### S4. VT links

| Row | Steps → reads | Deadline | Needs |
|---|---|---|---|
| S4.5 PlayMap follows Patterns, paired by proof | `links.patterns` `{state:"live",paired:true}` → bench `clip vt.mp4 120` → `links.patterns.clip` names it → Patterns `BLACKOUT ON` → PlayMap `air.blackout` and `air.patternsBlackout` true → `OFF` → false | 5 s each | today + P-B1 |
| S4.1 the rig's VT link to Patterns | `--playout patterns=10.99.0.3:9697,patterns` → paired → VT within 1.5 s of `remainingMs` → `playout patterns pause` applied → `video.paused` | 5 s | playout-links 1–5, P-B1 |
| S4.2 the rig's VT link to PlayMap | the VT setup → `--playout stage-left=10.99.0.4:<wire>,playmap` → VT reads it → pause applied → `go show #9` refused with PlayMap's words | 5 s | playout-links 1–5 |
| S4.3 both at once | both VTs named; `link:patterns` and `link:stage-left` green | 5 s | as S4.1 + S4.2 |
| S4.4 clocks agree on one kernel | offset within 50 ms; no `clock:` item | 10 s | playout-links |

S4.1 and S4.2 are also where G44's first untested pairing is proved: Stagewatch's link against Patterns' wire and PlayMap's. Today's
`PatternsLink` is in the WPF app and not in the rig, and playout-links replaces it with `PlayoutLink`.

### S5. Windows

| Row | Steps → reads | Deadline | Needs |
|---|---|---|---|
| S5.1 the grant reaches Patterns, which places it | `window new lobby /clock`, `window grant lobby <pat>` → `held` → `SCREEN 1 WINDOW lobby` → STATE names it and its placeholder → Patterns' context links it → the rig's `windows[0].on` | 10 s | windows-for-peers SW-W1–W5; PT-92.2–4 |
| S5.2 PlayMap draws a window | grant → `shown` at `rev` → `show /timeline` → `stale` then `shown` | 4 s after each | windows-for-peers PM-W; Xvfb + Mesa arrive here |
| S5.3 withdraw | black on both, the grant ended | 4 s | as above |
| (S5.1w) Patterns' WebView2 shows the page | — | — | a Windows job or the walk |

### S6. Loss and recovery

| Row | Steps → reads | Deadline | Needs |
|---|---|---|---|
| S6.1 the rig killed and restarted | SIGKILL → PlayMap `links.stagewatch.state` stale then gone; AGMessage `stage` not connected; Patterns `bus.stagewatch.state` stale/gone → restart (same id, `--state`, address, ports) → all live | 11 s / 20 s / 20 s after the restart | today + SW-L1 |
| S6.1b the rig's cable pulled | `down rig` → the same as S6.1 through silence, not a reset → `up rig` → all live | 11 s / 20 s / 20 s | today |
| S6.2a a stuck Stagewatch: PlayMap shows no time | `stall 12000` → PlayMap's timer source "?" (STATE) within 9.5 s of the reported start → frames resume within 5 s of the reported end | 9.5 s / 5 s | SW-ST |
| S6.2b Patterns shows no time | `bus.stagewatch` says stuck, no time | 9.5 s | PT-89.1 (G04) |
| S6.2c AGMessage shows no time | `stage` stale, no stage view | 9.5 s | F2's fix (an AGMessage unit, §7) |
| S6.3 PlayMap killed, the doors say why | rig `peers[]` stale then gone; `link:stage-left` red; `/api/preflight` `ready:false`, row `node:<pm>:heard` red, `configured` names the link → restart → all green; the link pairs again | 20 s each | SW-G49c, playout-links, SW-R1 |
| S6.4 Patterns killed | PlayMap `links.patterns.state` not live and `air.patternsBlackout` false → restart → live, paired | 20 s | today + P-B1 |
| S6.5 AGMessage killed with a result unreported | restart → the rig re-offers it → the notice is shown once | 25 s | agmessage AG-3 (G16); cross-app-proposals SW-4 |
| S6.7 Patterns' cable pulled | `down patterns` → PlayMap `links.patterns` stale (11 s) then not live (20 s), `air.patternsBlackout` false → `up patterns` → live and paired | 11 s / 20 s / 20 s | today + P-B1 |

### S7. Fences across the four apps

| Row | Reads | Needs |
|---|---|---|
| S7.1 no secret anywhere | the sweep of decision 11 over every capture and file | today, after its first bench run passes (decision 7) |
| S7.2 one contract | `tools/showbus-copies.sh` over the clones: every body hash equals the master's | spec-1-3 S8 and the copies (Patterns PT-90.4, AGMessage AG-7, Mapper's owner papers) |
| S7.3 unknown words are refused everywhere | Patterns `FROBNICATE` → `ERR`; PlayMap → `ERR`; rig `/api/frobnicate` → receipt `ok:false`; an approved `frobnicate` for AGMessage → failed with words (the last after cross-app-proposals SW-4) | today, then SW-4 |
| S7.4 no app's address is ever counted | `auth` is empty for every address after every row, in all three topologies | SW-R1 keys + F1's fix |

### S8. The doors

| Row | Reads | Needs |
|---|---|---|
| (S8.0, a measurement) each headless app's own context items | `measured.ownItems`: PlayMap, Patterns and AGMessage items with lights, read from their own CONTEXT | from SW-L2 |
| S8.1 ready with everything up | `ready:true`; `heard`/`context` green for each; operator frame `network.ready` true; Companion `network_ready` yes | SW-G49c, SW-R1, and S8.0 showing no red own item |
| S8.2 not ready, saying why | inside S6.3 | as S8.1 |
| S8.3 stuck is never ready | `stall 12000` → `/api/preflight` `ready:false,stuck:true` within 3.5 s of the stuck beat → Companion `network_ready` unknown | SW-G49c, SW-ST |
| S8.4 a seen-only node leaving does not block | AGMessage (not configured) killed → its `heard` row amber, `ready` still true | SW-G49c |

---

## 4. Changes, file by file

### Stagewatch

**`tools/Stagewatch.Rig/Program.cs`** (SW-L1)
- Adds `--node-id`, `--journal`, `--pin-file`, `--secrets-out` and `--exit-on-eof` (§2.1).
- The control reader is shared with SW-ST. It adds the `auth` and `exit` lines.
- `IShowReports.Report` builds from the journal and the rig's agenda. `Context()` passes the rig's own rows only.
- The ready line follows §2.1.

**`src/Timer.Remote/WebServer.cs`** (SW-L1)
- `AuthFailures()` (§2.2), reading `_authFailures` under its window rule.

**`tests/e2e/rig.cjs`, `tests/e2e/rig-faults.e2e.cjs` (new)** (SW-L1)
- `Rig.create({nodeId, journal, pinFile, secretsOut})` and `rig.control(line)`.
- `rig-faults.e2e.cjs` checks:
  - the id survives a restart;
  - with `--pin-file`/`--secrets-out` the ready line and stdout hold no PIN or window path;
  - 3 wrong PINs → `auth` lists the address with 3, and a right PIN clears it;
  - an unknown control line is refused;
  - EOF with `--exit-on-eof` stops the rig;
  - `/report` answers 200.

**`tools/interop/` (new, SW-L2)**
- `siblings.json`.
- `build.sh`: clones (decision 4), then builds one tree at a time (`dotnet build … -c Release -v q -nologo`) and runs
  `npm ci --ignore-scripts`.
- `lan.py` (§2.6, the probe's code made a tool).
- `run.sh`, run inside the namespace: starts `lan.py` for the topology, then `node --test` the driver.

**`tests/interop/` (new, SW-L2)**
- `package.json` and lock: exact `ws` and `socket.io-client` versions; Playwright from SW-R6.
- `lib/`:
  - `until`, `secrets` (the register, 0600 files);
  - `wire` (HELLO, NONCE → AUTH-PROOF over the normalised token, `@id`, captures);
  - `sweep` (decision 11's exemptions and redactions), `matrix`, `contract` (deadlines; §13 when present);
  - `auth` (the per-row check).
- `apps/`:
  - `stagewatch.cjs`, `patterns.cjs`, `playmap.cjs`, `agmessage.cjs`: start in a namespace, ready, stop, restart, read;
  - `agmessage-admin.cjs` (§2.5).
- `lan.interop.cjs`: `before()` starts the four apps in order (the rig first, so its keys exist), runs the ordered scenarios with
  the loss rows last, writes the matrix in `after()`, and runs the sweep.
- `laptop.interop.cjs` (SW-L5) and `offline.interop.cjs` (SW-R7).
- Other areas' interop files become `scenarios/*.cjs` here.

**`tools/test.sh`, `tools/setup-dev.sh`** (SW-L2)
- Lane `interop`:
  1. `build`;
  2. `tools/interop/build.sh`;
  3. `timeout 1200 unshare --user --map-root-user --net --pid --fork --mount-proc --kill-child tools/interop/run.sh`.
- It is not a default lane, and the header names it.
- A machine without user namespaces gets "not run (no user namespaces)".
- `setup-dev.sh` checks `python3` ≥ 3.12, `unshare` and `nsenter`, and runs `npm ci --ignore-scripts --prefix tests/interop`.

**`src/Timer.Core/Journal.cs`** (SW-G49a)
- `Peer`, `Desk`; `Node`, `PeerAtMs`, `Via`; an `Add` overload.
- `ShowReport.Network`; `AiDecisions` includes the `Desk` rows; the HTML section.
- `NetworkCsv(clock)`.
- Invariant-culture formatting in `ToCsv` and `NetworkCsv`.

**`src/Timer.Core/ShowBus.cs`** (SW-G49a)
- `PeerLine`; `Entry.RecentLines` beside `Recent`; `Parse` keeps `at`.
- New pure `PeerLines` (§2.10).

**`src/Timer.Show/DeskService.cs`** (SW-G49a, after cross-app-proposals SW-3)
- Fresh lines from the verified contexts become `Peer` rows, through a journal delegate.
- Every decide path writes its `Desk` row, rejection included.

**`src/Timer.App/ViewModels/MainViewModel.Desk.cs`, `.ShowBus.cs`** (SW-G49a)
- The mirror writes nothing of its own. `PeerFacts` reads `.Recent` unchanged.

**`src/Timer.Show/ShowContext.cs`** (SW-G49a)
- `recent` (`:136`) skips `Peer` rows and formats with the invariant culture.

**`src/Timer.Remote/WebServer.cs`** (SW-G49a)
- `GET /api/report/network.csv`.

**`src/Timer.Show/NetworkPreflight.cs`** (new, pure, SW-G49b)
- `PreflightInput` (heard table, verified contexts, configured relationships, links, windows, `now`), `PreflightRow` and `Run`
  → the result of §2.9.
- `SeenNodes` (10 min, 32) and the stuck answer.

**`src/Timer.Core/ShowState.cs`, `SnapshotJson.cs`** (SW-G49b)
- `IShowContext.PreflightJson()` (default null).
- The operator frame's `network` after `desk`.

**`src/Timer.Remote/WebServer.cs`, `McpEndpoint.cs`** (SW-G49b)
- `/api/preflight` with the stuck rule.
- `get_preflight`.

**`src/Timer.App/ViewModels/MainViewModel.ShowBus.cs`** (SW-G49b)
- `RefreshShowBus` (once a second) builds the verdict, keeps `_preflightJson` with its `at`, the frame's triple, and
  `ShowNetworkLine`.

**`src/Timer.Show/AiContext.cs`, `src/Timer.App/Help/HelpContent.cs`, `src/Timer.Remote/wwwroot/producer.js`,
`companion-module/src/{variables,feedbacks,state}.ts` + tests** (SW-G49b)
- The other readers (§2.9).

**`src/Timer.App/MainWindow.xaml(.cs)`** (SW-G49b, PC work)
- The header's tooltip, "Copy the preflight" on its right-click and as a button.

**`tools/Stagewatch.Rig/Program.cs`, `tests/e2e/preflight.e2e.cjs`** (SW-G49c)
- The rig computes and serves the same verdict.
- The e2e: one scripted peer with a node key → ready → it stops → amber at 6 s, then red at 12 s when it is configured (bound
  key); amber when only seen.

**`.github/workflows/ci.yml`, `interop-heads.yml`** (SW-L4)
- §2.12.

**Papers** (SW-P)
- `docs/BUILD.md`: the lane, the topologies, the clones and pins, what it proves and what it does not.
- `docs/REMOTE.md`: `/api/preflight`, `network.csv`, the frame's `network`, `get_preflight`, the report's sections, each route's
  key scope, the rig's new options.
- `docs/COMPANION.md`: the variables and feedback.
- `docs/ECOSYSTEM.md`:
  - the manifest superseded;
  - merged report and preflight built;
  - link all not built;
  - the conformance note points at the lane;
  - PlayMap added.
- `docs/FEATURES.md`, `docs/WHATS_NEXT.md`, `CLAUDE.md` (the loop line and where things live), `README.md`.
- The SHOWBUS words go to spec-1-3 S1 (§2.11), not into this unit.

### Patterns (one round; the maintainer numbers it; it depends on no other Patterns round)

**`src/Patterns.App/Services/HeadlessDesk.cs`** (new)
```csharp
public static (AppServices Services, MainViewModel Vm) Create(SettingsStore store, NodeKind profile);   // services, Instance, view model
public static MainWindow Show(AppServices services, MainViewModel vm);                                 // window, attach, show, one RunJobs
```
No sleep and no test knobs. A comment states the invariant: the bench and the tests boot through these two calls only.

**`tests/Patterns.App.Tests/TestApp.cs`**
- `Boot` keeps `CloseLeftovers`, `MachineGB`, `PauseOverride`, `QualityService.MachineGB` and `RenderFence`.
- It calls `Create`, sets `vm.PollLanes.BudgetMs = 10_000`, then calls `Show`.

**`tools/Patterns.Bench/Patterns.Bench.csproj`** (new)
- `Exe` on `$(PatternsTfm)`.
- It references `Patterns.App`, `Avalonia.Headless` 11.3.20 and `SkiaSharp.NativeAssets.Linux` 3.116.1, as the App tests do.
- The fences apply.

**`tools/Patterns.Bench/Program.cs`**
1. Options (§2.3).
2. The settings file.
3. `AppBuilder.Configure<App>().UseHeadless(new() { UseHeadlessDrawing = false }).UseSkia().SetupWithoutStarting()`.
4. `TrustLoopback`.
5. `HeadlessDesk.Create` → `Show`.
6. The clip factory.
7. The ready line.
8. `Dispatcher.UIThread.MainLoop(token)` until SIGTERM, SIGINT, `exit`, EOF or `--exit-after`.
9. `Shutdown()`.

**`tools/Patterns.Bench/BenchClip.cs`**
- An `IMountedSource` with a wall-clock playhead (`ShowClock`).
- Play, Pause, Seek, Stop, Rate, length, Ended.
- A solid colour with the position drawn; no audio.

**`tools/Patterns.Bench/BenchControl.cs`**
- `clip`, `propose look` (decision 18) and `exit`, each run on the UI thread.

**`Patterns.sln`, `tests/Patterns.App.Tests/Patterns.App.Tests.csproj`**
- The bench is under `tools`.
- The test project references it with `ReferenceOutputAssembly=false`, for build order.

**`tests/Patterns.App.Tests/BenchHostTests.cs`** (new; process-level)
- Ready within 60 s.
- The wire pairs by proof and STATE has the `bus` row.
- `propose look x` with no desk is refused, and **the looks and the air are unchanged** (no local apply).
- `clip a 30` gives `video.lengthMs` 30000.
- `exit` gives code 0.

**Papers**
- `docs/PLAN.md` §NN. Its first lines name the requester and the evidence class, **the maintainer's list**, quoting the ask:
  "StageWatch also needs to be able to interact with AGMessage, PlayMap, and Patterns. Consider use of the ShowBus."
- `docs/REVIEW.md`: "Walk: none asked — the exe's start, the watchdog and App.axaml.cs are unchanged".
- `CHANGELOG.md`, `README.md` (the bench under Build).
- `docs/OPEN.md`:
  - L47, L50 and L56 annotated "met the real Stagewatch servers on a one-kernel LAN in Stagewatch's interop lane (`<commit>`); the
    walk stays";
  - L40 annotated with the files the lane exempts by name (settings `.bak`, `backups/`, the recovery record);
  - a row added: "the bench's clips are synthetic; libVLC on Linux unmeasured".
- The tag table row for the round before.

### Mapper (PlayMap): no change

- The lane uses `PlayMap.Node`, `playmap.node.json`, the `is up` line, STATE (`air.*`, `links`, `video`), `CONTEXT`, `NODES`,
  `SET` and `PLAYLIST`.
- Each is already fenced in Mapper:
  - `e2e-engine.test.ts`;
  - the NodeHome tests;
  - `tests/fixtures/state/main.json`;
  - the verb catalogue.
- A later `SHOWBUS-NODE.md` line ("proven against Stagewatch's real servers in its interop lane at `<commit>`") goes only by the
  owner's papers lane (round 3 §4 gives that file to no package).

### AGMessage: no change in this area

- The lane uses its environment, banner, `/api/admin/login`, `admin:settings`, rooms, `join` and `stage:propose`.
- The fixes that rows wait for:
  - AG-1, AG-2 for S3.1;
  - AG-3 for S6.5;
  - AG-4 for S1.3b;
  - F2's new unit for S6.2c.

---

## 5. Tests

### Stagewatch suite (`tests/Stagewatch.Tests`; injected time)

**`ShowBusTests.PeerLines` / `PeerLinesTests` (new, pure)**
- `Parse` keeps `at`.
- A node's first post gives 0 fresh lines and one "joins" row.
- The next post with 2 new lines gives those 2.
- The same post again gives 0. After 31 s (the context dropped), the same lines again give 0.
- Two lines with the same text and different `at` give both.
- Lines without `at` use the overlap rule.
- A post sharing nothing with the last gives a gap row.
- A 33rd node evicts the least recently heard. When that node returns, it gets a "joins" row, not a backlog.

**`ReportTests` (new)**
- The AI section holds the `Desk` rows, the rejection included, and the matching notes, and no plain notes.
- The network section lists the peer rows by `t`, with "its clock" and `via`.
- `NetworkCsv`'s header, quoting and `Via`.
- **Culture:** under `th-TH` and a culture with `.` as its time separator, `ToCsv`, `NetworkCsv` and the context's `recent` give
  Gregorian `yyyy-MM-dd HH:mm:ss` and `HH:mm:ss`.
- A journal from before this change loads, and its report is byte-identical.
- **A forged id:** a context posted by AGMessage's key with PlayMap's id is refused (peers-not-screens SW-2), and no `Peer` row exists.
- A context marked `verified:"none"` writes no row.

**`DeskTests`** (cross-app-proposals' file)
- Every decide path writes one `Desk` row, a rejection by deck, MCP or desk included.

**`ShowContextTests`**
- `Peer` rows never appear in Stagewatch's own `recent`.
- **No preflight item is ever in Stagewatch's own items or light.**

**`NetworkPreflightTests` (new, pure)**
- All live → `ready`.
- A configured node quiet 8 s → amber; 13 s → red; never heard → red "never heard".
- A seen-only node gone 13 s → **amber**, `ready` still true; at 11 min it is gone from the rows.
- A peer's own red → `own` red; its own amber (a PlayMap blacked out by Patterns) → amber, `ready` still true.
- A link red → red. A window off its revision → amber.
- 40 nodes → 32 and one amber "8 more".
- 250 inputs → 200 rows, reds first.
- **Stuck:** `ProducerStuck`, or a cached verdict 3.1 s old → `ready:false`, `stuck:true`, the one red row.

**`WebV2Tests` (a real `WebServer`)**
- `/api/preflight`: 401 without auth; 200 with any key; 400 for a query; rate headers.
- A stuck `SnapshotHub` → `stuck:true`.
- `network.csv`: read scope, CORS.
- The operator frame ends `…"desk":…,"network":…`; the public frame has neither.
- `AuthFailures()`: 3 wrong PINs → 3; the right PIN → empty.
- MCP `get_preflight` returns the route's JSON.

**`Bench.OnWindows`**
- "the Show network header says the verdict, its tooltip lists the rows, Copy the preflight on the right-click and the button"
  (UiSmoke).

### Module (`companion-module`)
- `network_ready` follows `network.ready`.
- It reads `unknown` when the connection is lost or `STUCK`.
- `network_not_ready` is red at `ready:false`.

### Rig e2e
- `rig-faults.e2e.cjs` (§4).
- `preflight.e2e.cjs` (SW-G49c).

### The interop lane (`tests/interop`)
- The rows of §3, each a `test()` writing its matrix row and its `auth` snapshot.
- `after()` runs the sweep, and a failure fails the lane.

### Patterns
- `BenchHostTests`.
- The lifecycle lane through `TestApp` covers `HeadlessDesk.Create`/`Show`, with its census unchanged.
- All seven suites and the module's tests.

### Mapper, AGMessage
- None new. Their existing tests fence the doors the lane uses.

---

## 6. Units, in order (each builds and tests alone)

| # | Repo | Unit | Gaps | Depends on |
|---|---|---|---|---|
| P-B1 | Patterns | `Round NN.1: the bench host — the real desk headless off Windows, for Stagewatch's interop lane` (HeadlessDesk Create/Show, TestApp, Patterns.Bench, BenchClip, BenchControl, sln, BenchHostTests) | G44 | — |
| P-B2 | Patterns | `Round NN papers` | G44 | P-B1 |
| SW-L1 | Stagewatch | `Rig: a stable node id, a journal and its report, secrets off stdout, the lockout table readable` (Program.cs, WebServer.AuthFailures, rig.cjs, rig-faults.e2e.cjs, WebV2 test) | G44 | — (shares the control reader with SW-ST, whichever lands first) |
| SW-L2 | Stagewatch | `Interop: four real apps on a one-kernel LAN — clones, pins, sweep, matrix` (tools/interop incl. lan.py, tests/interop, test.sh, setup-dev.sh; rows S0.1, S1.1–S1.3, S4.5, S6.1, S6.1b, S6.4, S6.7, S7.1, S7.3 (first three), each only after it passes) | G44 | P-B1 pushed and pinned; SW-L1 |
| SW-L3 | Stagewatch | `Interop: a stuck rig (S6.2a)` | G44 | SW-ST; SW-L2 |
| SW-L4 | Stagewatch | `CI: the interop job at the siblings' pins, the nightly heads run, releases wait for it` | G44 | SW-L2 |
| SW-L5 | Stagewatch | `Interop: the show laptop topology (S1c.1, S1c.2)` | G44 | SW-L2 |
| SW-G49a | Stagewatch | `Report: what the show network said, and every desk decision` (Journal, ShowBus PeerLines, DeskService rows incl. rejection, ShowContext, WebServer, culture; tests) | G49 (3) | peers-not-screens SW-2 (verified credentials, G15); cross-app-proposals SW-3 (DeskService) |
| SW-G49b | Stagewatch | `Preflight: one verdict for the doors` (NetworkPreflight, route, frame field after `desk`, MCP get_preflight, AI facts, module, producer page, desk (PC), help; pure + WebV2 tests) | G49 (2) | — (frame field order with cross-app-proposals SW-5 whichever lands first) |
| SW-G49c | Stagewatch | `Rig: the preflight` (rig, preflight.e2e.cjs) | G49 (2) | SW-G49b; spec-1-3 S2; peers-not-screens SW-6 |
| SW-P | Stagewatch | `Papers: the interop lane, the report's network section, the preflight; ECOSYSTEM's manifest superseded; REMOTE's key scope per route` (+ the §6/§8 words to spec-1-3 S1) | G49 (4), G44 | SW-L2, SW-G49a, SW-G49b |
| SW-R1 | Stagewatch | `Interop: keys from the rig; the rig on the bus; contexts both ways (S1.4–S1.6, S2.1–S2.7)` | G44 | spec-1-3 S1–S2; peers-not-screens SW-2, SW-6 and their PT/PM/AG halves |
| SW-R2 | Stagewatch | `Interop: the desk rows (S3.1, S3.2, S3.5, S3.7, S3.8, S7.3's last word)` | G44, G49 (3) | cross-app-proposals SW-1–SW-4, SW-10; agmessage AG-1, AG-2; SW-G49a |
| SW-R3 | Stagewatch | `Interop: Patterns' desk and stuck rows (S3.3, S3.4, S6.2b)` | G44 | PT-89.1, PT-90.3; SW-R2 |
| SW-R4 | Stagewatch | `Interop: the VT rows (S4.1–S4.4)` | G44 | playout-links 1–5 |
| SW-R5 | Stagewatch | `Interop: the doors (S6.3, S8.1–S8.4)` | G44, G49 (2) | SW-G49c, SW-R1, SW-ST; S8.0 showing no red own item |
| SW-R6 | Stagewatch | `Interop: windows for peers (S5.1–S5.3)`, Xvfb, Mesa and Playwright in the job | G44 | windows-for-peers SW-W1–W5, PM-W; PT-92.2–4 |
| SW-R7 | Stagewatch | `Interop: nobody locks anybody out; the offline laptop (S7.4, S1c.3, S1b.1)` | G44 | SW-R1; F1's fix |
| SW-R8 | Stagewatch | `Interop: AGMessage hears PlayMap, re-offers and sees a stuck Stagewatch (S1.3b, S6.5, S6.2c)` | G44 | agmessage AG-4, AG-3; F2's unit |
| SW-R9 | Stagewatch | `Interop: one contract (S7.2)` | G44 | spec-1-3 S8 and every copy |

Each SW-R unit bumps `siblings.json` and adds only rows that pass at the new pins.

---

## 7. Gaps closed and left; findings handed over

**G44: closed in substance.**
- Real apps run against each other in a test on Linux, in CI and on the cloud bench, on three topologies:
  - separate hosts on one switch;
  - the show laptop;
  - offline.
- Stagewatch's shared services run in the rig, beside Patterns' real desk services (headless), PlayMap's real engine and
  AGMessage's real server.
- Faults are real: kill, pulled cable, stuck show thread.
- Results are pinned, measured and reproducible, with nightly drift. Stagewatch's release waits for them.

**G44: left.**
- **Two machines and a real network.** Firewall prompts, multi-NIC machines, Wi-Fi loss and Windows' own UDP sharing need the
  walk (QUALIFICATION §26/§27; L47, L50 and L56 stay open, annotated).
- **Windows-only parts:**
  - the WPF desk's mirror and right-click menus (UiSmoke);
  - WebView2 showing a granted window (S5.1w);
  - DPAPI;
  - PlayMap's Direct3D 12.
- **Real decoders:** Patterns' clips are synthetic, and PlayMap's VT is a colour item.
- **AI models:** the AI path is an MCP client and Patterns' assistant call, never a model's output.
- **TLS between real apps:** the rig serves plain HTTP. A rig `--tls` unit is not designed here, so L56 stays open.
- **Stagewatch's existing `PatternsLink` against PlayMap's TCP 9941 (G44's first bullet):** it lives in the WPF app, not the rig,
  and playout-links replaces it. The pairing is proved by S4.1/S4.2 once `PlayoutLink` is pinned. Until then it stays untested.

**G49:**
- **(2) closed, read-only:** one verdict on its own doors; amber never blocks; stuck says so.
- **(3) closed** for verified peers' lines and every desk decision. The record is a sample by design, and the report says so.
  Clock-corrected merging is left: only playout links measure an offset.
- **(4) closed as superseded:** ECOSYSTEM corrected, and REMOTE gives each route's key scope.
- **(1) left:** "link all" is a security design of its own.
- **ECOSYSTEM's remaining roadmap is left:** heartbeat dashboard, failover, the operator's chaos rehearsal (the lane's fault rows
  are its automated first form), the local-model advisor, and one Companion module.

**Findings this area hands to their owners.** Each has a row waiting.

| # | Finding (evidence) | Owner, proposed | Row |
|---|---|---|---|
| F1 | A missing PIN counts toward the lockout, and a keyless Patterns sends one every ~5 s. On one laptop, 127.0.0.1 is locked out of Stagewatch within a minute: the operator's own producer page included, and any keyed app on that laptop (`StagewatchLinkService.cs:347-351,358-380`; `WebServer.cs:444-448,867-883`) | peers-not-screens SW-2 (it already stops counting unknown keys; same function, same reason: a request with no credential is not a guess). Optionally Patterns PT-90.1: no context read without a key | S7.4, S1c.3, S1b.1 |
| F2 | AGMessage never sees a stuck Stagewatch: pongs refresh `lastHeard` (`nodes.js:325-329,348-356`; G04's last paragraph). design/agmessage.md has no unit for it | agmessage: a new unit "a stuck beat holds the link stale; pongs do not refresh it while stuck" | S6.2c |
| F3 | A rejected shared proposal writes no journal row (`MainViewModel.Desk.cs:127-129`) | this area, SW-G49a (in DeskService) | S3.7 |
| F4 | `ToCsv` and the context's `recent` time ignore the culture (`Journal.cs:214`; `ShowContext.cs:136`) | this area, SW-G49a | ReportTests |

---

## 8. Risks

- **Cross-repo coupling.**
  - Stagewatch's CI builds three other repositories. The pins keep it deterministic, and the nightly heads show drift.
  - A sibling's contract change costs a pin-bump commit.
  - Private siblings need `SIBLINGS_READ_TOKEN`. It never persists in a checkout, and forks skip the job with words.
- **Kernel features.**
  - The LAN needs user, network and PID namespaces, bridge and veth, and Python ≥ 3.12.
  - CI relaxes Ubuntu 24.04's AppArmor userns restriction and loads the two modules **on the ephemeral runner only**.
  - A machine without them reports "not run", which fails under REQUIRED.
  - This is the tree's first Python file: `tools/interop/lan.py`, stdlib only, because the bench has no iproute2 and Node cannot
    speak rtnetlink.
- **Time.**
  - About 12–15 min cold: two .NET trees, about 6 min of builds; npm; about 4–5 min of rows per topology.
  - Topologies after the LAN run only in the nightly job until their rows exist.
- **Flakiness.**
  - Deadlines come from the contract with margin, and there are no retries.
  - The recorded times are the evidence for adjusting a deadline.
  - A flaky row is fixed or re-justified, never quarantined.
- **One kernel is not a network.**
  - The LAN shares one clock, one CPU scheduler and one kernel's TCP stack.
  - It proves addressing, broadcast and lockout separation, not radio loss or Windows' stack.
- **The rig is not the app, and the bench is not the exe.**
  - The WPF mirror must stay thin (cross-app-proposals decision 10), or the lane proves less than it seems; UiSmoke alone fences it.
  - `HeadlessDesk` shared with the tests keeps Patterns honest.
- **Clones and live trees.** The default never writes into a sibling tree. `SW_INTEROP_LIVE=1` on the bench hands the one-build
  rule back to the operator.
- **Report readership widens.** Verified peers' lines, which can include a controller's address in Patterns' origins, become
  readable by any read key holder. The §6 sentence says so, and the sweep covers the report.
- **Journal growth.** At most 20 peer lines per post, with no governor (no measured cause). The lane records peer rows per minute
  per app.
- **AGMessage's banner prints its admin token by design.** It is exempt by name. Its own area decides.

---

## 9. What each repo's process asks

**Stagewatch (CLAUDE.md)**
- **Builds:** one `dotnet build` at a time per tree. The lane builds Stagewatch, then each clone, in sequence.
- **Fences:**
  - the banned APIs and `TreatWarningsAsErrors` apply to `src/`;
  - `NetworkPreflight` and `PeerLines` are pure on injected time;
  - formatting is culture-invariant (F4 fixed and tested under `th-TH`).
- **Windows-only checks** go through `Bench.OnWindows` (the Show network header, PC work named as such).
- **Frames:** the operator frame's `network` goes after cross-app-proposals' `desk`, and `JournalEntry`'s `n`, `pt` and `via` go at
  the end.
- **No new `ShowCommandKind`.** The route, the frame field, Companion, MCP `get_preflight`, the producer page, AI facts, help and
  the desk are each followed down their chain. Stagewatch's own context is untouched (one hop).
- **Comments** state the invariants (the bench's two calls, the stall, the holders), with no model or tool names.
- **Papers:**
  - REMOTE, COMPANION, ECOSYSTEM, FEATURES, WHATS_NEXT, BUILD, README;
  - CLAUDE.md: the loop line `tools/test.sh interop` and the where-things-live rows;
  - the SHOWBUS words go to spec-1-3 S1, not edited here.
- **CI:** the `interop` job with `release` needing it, and the heads workflow.
- **Commits:** subjects `Rig: …`, `Interop: …`, `Report: …`, `Preflight: …`, `CI: …`, `Papers: …`. Each pin bump is its own
  commit.

**Patterns (CLAUDE.md)**
- One round, numbered by the maintainer: `Round NN.1: …` and the papers commit, the maintainer's authorship, reasoning in the body.
- **Validation:** `dotnet build Patterns.sln` (the bench is fenced like `src/`), all seven suites and the module's tests, and the
  lifecycle lane's census unchanged.
- **PLAN §NN** opens with the requester and the evidence class, **the maintainer's list**, quoting the ask. It says what the round
  did not do: libVLC, WebView2, the exe's start.
- **REVIEW:** "Walk: none asked". No platform path is touched.
- **OPEN.md:** L47, L50 and L56 annotated, not closed; L40 annotated; one row added.
- **Closing sentence:** the round adds no verb an output can show, so its papers say there is no journal, log or Super Check row
  for one.

**Mapper (CLAUDE.md, round-3 ownership)**
- Nothing is edited.
- A later `SHOWBUS-NODE.md` line goes through the owner's papers lane.
- No lab claim: the lane runs with `NullBackends`, and Mesa only from SW-R6. Nothing about frame lock or latency is claimed.

**AGMessage (no agent brief)**
- Nothing is edited here. F2 is handed to its area.
- Its CI stays Windows-only. This lane is its first Linux run.

---

## 10. The critique answered

| # | Item | Verdict | What changed |
|---|---|---|---|
| 1 | Shared-loopback lockout | **Accepted; one remedy rejected by measurement.** Verified: one table per address, missing PINs and unknown keys counted, success clears (`WebServer.cs:867-883,1068-1090`). Worse than stated: a **keyless** Patterns is counted too (`StagewatchLinkService.cs:347-351`), so "configure no key" alone does not decouple. "127.0.0.x per app" is rejected: measured, Linux dials them all from 127.0.0.1 (`lane-probe2/src.cjs`) | A namespace per app (measured: each dials from its own address); no keys until the rig makes them; `WebServer.AuthFailures()` + rig `auth`; a per-row assertion; F1 handed to peers-not-screens SW-2; S7.4 |
| 2 | Sweep fails today and flags itself | **Accepted.** Verified: settings `.bak`, `backups/`, recovery (+`.bak`); `/api/windows` `path` with the PIN; `contextToken`; the rig's ready line prints window paths | Decision 11's named exemptions and redactions; the rig's `--pin-file`/`--secrets-out`; S7.1 lands only after its first bench run passes; L40 annotated |
| 3 | Peer rows make G15 durable | **Accepted.** Verified (`Desk.cs:65-70`, `ShowBus.cs:56-69`) | SW-G49a depends on peers-not-screens SW-2; only verified contexts are journalled, with `via`; a forged-id test |
| 4 | S6.2's AGMessage half fails today | **Accepted.** Verified (`nodes.js:325-329,348-356`; pongs at `WebServer.cs:1349-1355`). No AGMessage unit owns it | S6.2a/b/c; F2 handed to agmessage |
| 5 | Amber blocks at doors | **Accepted, with a different remedy.** Verified (`ContextBuilder.cs:26-29`) | `ready` = no red; seen-only nodes go amber, never red; no forget/accept verb (designed out, decision 15); S8.0 measures the headless apps' own items before S8.1; S8.4 |
| 6 | The context item breaks one hop | **Accepted.** Verified (`ShowContext.cs:105,115-116`) | Item removed; MCP `get_preflight` replaces that reader |
| 7 | Stale verdict while stuck | **Accepted** | `ready:false,stuck:true` and a red row; module `unknown`; WebV2 and S8.3 |
| 8 | HeadlessDesk vs `BudgetMs` | **Accepted.** Verified (`TestApp.cs:155-164`) | `Create` / `Show`; census unchanged |
| 9 | Unit dependencies | **Accepted** | SW-G49b app-only; SW-G49c after spec-1-3 S2 + peers-not-screens SW-6; SW-G49a after SW-2 and cross-app SW-3, rows in DeskService; `desk` then `network`; `RecentLines` beside `Recent` |
| 10 | SHOWBUS copies | **Accepted; one fact corrected.** Mapper has no copy today (spec-1-3 adds it) | Words handed to spec-1-3 S1; S7.2 after S8 and the copies; `recent` audited (§0) |
| 11 | CI token exposure, fork PRs | **Accepted** | `persist-credentials: false`, `--ignore-scripts` (AGMessage has no install scripts), a fork guard with words |
| 12 | Destructive bench defaults | **Accepted.** Verified (`server.js:45,51`) | Clones by default; `SW_INTEROP_LIVE=1` opt-in; npm only in lane-owned copies |
| 13 | Machine-wide 9928 | **Accepted; recipe corrected.** This bench's kernel has no `dummy`; veth and bridge work (measured) | The one-kernel LAN; PID-namespace reaping (measured); the flock dropped |
| 14 | Fault timing | **Accepted** | SW-ST's `stall` is the one mechanism; 12 s; 9.5 s / 5 s; `at` in the answers |
| 15 | PIN in argv | **Accepted** | `--pin-file`; `pin:"set"/"none"` |
| 16 | The bench's proposal is not the assistant's | **Accepted.** Verified (`AssistantPage.cs:379-413`) | `ApplyProposal(chip)`, refused before the call with no desk; S3.3 asserts EDIT SAFE and the air unchanged |
| 17 | No row for a rejection | **Accepted** (F3) | A `Desk` row on every decide path |
| 18 | Culture | **Accepted** (F4), widened to `ShowContext.cs:136` | Invariant formatting; a `th-TH` test |
| 19 | Report holes | **Accepted** | Both roads via DeskService; per-node high-water; no backlog; gap rows; "a sample" said |
| 20 | Wrong STATE path; proof | **Accepted.** Verified | `air.blackout`/`air.patternsBlackout`; tokens normalised; the proof as `Pairing.Proof` |
| 21 | Over-building | **Partly accepted.** `DISPLAY`, Mesa and Playwright wait for SW-R6; the checks join as their inputs land; the context item is gone. **Rejected:** shipping the verdict to only some readers. Stagewatch's rule is every reader in the same round, and the readers serve the operator, not the lane | — |
| 22 | Patterns' evidence class | **Accepted** | "the maintainer's list", the ask quoted |

**The missing items:**
- lockout coupling: item 1;
- isolation and orphans: item 13 (the PID namespace, measured);
- the complete exemption list: item 2;
- G15 dependency and attribution: item 3;
- copies with a version: item 10;
- stuck semantics: item 7;
- operator actions: item 5 (designed out);
- the rejection row: item 17;
- the order with DeskService and the frame: item 9;
- the HeadlessDesk seam: item 8;
- CI hardening: item 11;
- G44's first bullet: §3 S4 and §7;
- the headless apps' own items: S8.0.

---

## Appendix: the probes

All under `<scratch>/`.

**`lane-probe/`** (first round)
- `udp.cjs` … `udp4.cjs` and `loup.py`: broadcast, 127.0.0.1 and 127.255.255.255 on the host and under `unshare -rn`.

**`lane-probe2/`**
- `src.cjs`, `src2.cjs`: which source address a server sees for 127.0.0.x and for local addresses. Result: 127.0.0.2, 127.0.0.3
  and 127.1.2.3 all arrive from 127.0.0.1.
- `netns.py` + `probe.cjs` (`unshare -rn`): a veth at 10.99.0.1/24 with secondaries .2–.5. Results:
  - without a default route, 255.255.255.255 is `ENETUNREACH`;
  - with one, all four listeners hear it from 10.99.0.1;
  - same-subnet secondaries arrive from the primary;
  - /32 primaries arrive from themselves.

**`lane-probe3/`**
- `lan.py` + `host.cjs` + `run.sh` (`unshare -rn`): bridge `br0` at 10.99.0.1 and four host namespaces at .2–.5. Results:
  - cables in: each host heard the other three's broadcasts from their own addresses and its own 127.0.0.1 copy only, and each
    server saw .2, .3, .4 and .5;
  - PlayMap's cable pulled: PlayMap heard only itself, and the other three heard each other and not PlayMap.
- `pidns.sh` (`unshare -rnpf --mount-proc`): a process left inside a host namespace and the holders were gone when the namespace's
  first process exited. `timeout 2 unshare -rnpf --mount-proc --kill-child …` left nothing behind.
