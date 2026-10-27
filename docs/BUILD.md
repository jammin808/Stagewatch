# Build & ship

## Prerequisites

| Tool | Why |
|---|---|
| **.NET 10 SDK** (LTS) | All non-VSTO projects |
| **Visual Studio Build Tools 2022** with the "Office/SharePoint development" workload | Required for VSTO (`Timer.PowerPoint`). NuGet alone can't build a VSTO add-in. |
| **NDI Tools / NDI Runtime** (https://ndi.video/tools/) | Optional, on the machine that *runs* Stagewatch. No build-time dependency: the runtime DLL is loaded at run time. |
| **Inno Setup 6** (https://jrsoftware.org/isinfo.php) | Builds the installer in `installer/`. |
| **Code-signing cert** (EV preferred) | Avoids SmartScreen warnings on first run. |

## Repo layout

See `README.md`.

## Quick build

```pwsh
dotnet restore
dotnet build -c Release
```

That builds Core / Licensing / Companion / Output / App / KeyGen. **Timer.PowerPoint is intentionally excluded** from the basic CLI build — open `Timer.sln` in Visual Studio with the Office workload installed, or run `MSBuild` after installing the VSTO targets.

## NDI

Nothing to build. `Timer.Output/NdiNative.cs` loads `Processing.NDI.Lib.x64.dll` at run time from
`NDI_RUNTIME_DIR_V6` / `_V5` / `_V4` (set by the free NDI Runtime / NDI Tools installer), the app folder, or the
DLL search path. Without it the NDI button explains what to install. Don't redistribute the runtime unless
you follow the NDI SDK licence; pointing users at NDI Tools is simpler. Display "NDI® is a registered trademark
of Vizrt NDI AB" (already in Settings → About).

## Licensing keys

Already set up for this repo (2026-10-08): a fresh build pepper, and an ECDSA P-256 public key in
`CryptoEnvelope.cs` whose private key is at `%USERPROFILE%\.stagewatch\license-signing-key.pem`.
See `docs/LICENSING.md` for issuing licenses and master serials, and for key rotation.

## Tests

```pwsh
dotnet run --project tests/Stagewatch.Tests      # engine, schedule, CSV, commands, OSC, web, windows, Companion, licensing
cd companion-module; npm test                      # Companion module
```
Both exit non-zero on any failure. The positive licence-signing tests run only where the private key exists.

### The Linux bench (cloud sessions, CI's Linux job)

Everything but WPF, WMI, COM and PowerPoint also builds and runs on Linux (the root `Directory.Build.props` sets
`EnableWindowsTargeting` off Windows):

```bash
tools/setup-dev.sh            # .NET 10 SDK, Node packages, Chromium's headless shell (Playwright)
tools/test.sh                 # build, unit, module, holo, e2e — or name lanes: tools/test.sh unit e2e
tools/test.sh pageshot        # PageShot's screenshots into artifacts/pages
tools/test.sh rig             # the headless rig in the foreground: open its pages in a browser
tools/test.sh interop         # not a default lane: the real apps at their pins on one LAN, then on one show laptop (below)
tools/test.sh help            # what each lane does, and the variables a person sets for them
```

- The suite names the checks it leaves to the Windows job (`Bench.OnWindows`): today the WMI hardware id, the crafted
  master file, the report file names and the slides agent's PowerPoint loop. On Windows it names the one it leaves to the
  Linux bench (`Bench.OffWindows`): `tools/showbus-copies.sh` run by bash. Neither is ever counted as passed.
- The suite, the browser workflows and the interop lane remove the temporary folders and files they make. The suite makes
  each through `Scratch`, named `sw-<kind>-<run>-<id>` with the run's tag, lists the temp folder's `sw-*` entries as it
  starts and as it ends, and fails on an entry of its own still there (its summary's `temp:` line counts them; a removal a
  process still holds is retried, then said there). An e2e file fails when a folder it made (`tests/e2e/temp.cjs`) stands
  at its end, and the rig runs there with the .NET runtime's diagnostics off, so a rig ended with SIGKILL leaves no pipe or
  socket behind. The interop lane removes each app's temporary folder once the apps have stopped, whatever a stop did, and
  names any it could not in its matrix (`tempFoldersLeft`). An entry made outside those makers would carry no run tag, so
  the suite also fails when any of its files but `Scratch.cs`, or the code the suite and the lanes run (`src/`, the rig,
  the scripted peer, the copies script), names the temp folder. An earlier run's leftovers, and what another run made,
  are never counted or touched.
- `tools/Stagewatch.Rig` is the real web server, Companion server and OSC server around a simulated show (timer,
  messages, blackout, a holding image, Next/Previous/GoTo, windows, screen commands, the contexts show apps post and
  their node links); anything else is refused with words. A message that asks for its acknowledgement (`ack`) is the
  app's, on the app's own signal board: its receipt's `refs.messageId` names the signal, frames carry `messageId`,
  `messageAck` (`sent`, then `done`: a message completes on its first answer, never `seen`) and `messageAckBy` and
  list it in `signals`; `ack <id>` answers it from a page, a show app or the operator, a newer message cancels it and
  `clearmessage` clears it, as the app's do. One the PIN does not speak for is named by its name and the device's
  address, the name cut to fit 40 characters, never the address. An ACK that would change nothing is refused: with a
  403 at the door once the cue has left the frame's `signals` (cancelled, or 30 s after its answer), with a 409
  receipt while it is listed but already answered. The three fields go back to `0`, `""`, `""` 30 s after the answer
  or the cancel, the message maybe still on stage, so a peer keeps `done` for the `messageId` it sent. The rig always
  makes the signal; the app, under a licence with no crew cues, puts the message up alone, and an `applied` receipt
  without `refs.messageId` means no answer will follow. A receipt's `effect` is free words (the rig's own; the app's
  is the status line it made, often empty for a remote message): a peer shows and asserts none of it. Its first output
  line is JSON with its ports, its node id and the node keys made in this run (`--port`, `--companion`, `--osc`,
  `--pin`, `--bind`, `--state`, `--view`, `--room`, `--node-keys`, `--exit-after`). For the interop lane:
  `--node-id <id>` keeps one id across restarts;
  `--bus PORT [--bus-to ADDRESS:PORT]` puts it on the Show Bus: it hears hellos on that port, joins the nodes it hears
  into its context as the app does, and says the app's own hello to Show Bus §2's targets on that port, or only to
  `--bus-to` (nothing while `--bind` is local, as the app; the ready line's `bus` is the port, or 0);
  `--tls <folder>` serves its certificate on its port beside plain HTTP, as the app serves its own (a connection that
  opens with a TLS handshake is encrypted): the app's kind, made in the folder on the first start and served again by every
  later one, so a peer that pinned it pins it again after a restart; off Windows the key's password is not sealed, so the
  folder is the rig's own: one not there yet is made the user's alone, and one already there is taken only when it holds
  nothing but the certificate's files and no other user may open it, else refused, never narrowed; the ready line's `tls` is
  its fingerprint (or null), and the hello names it with `https`;
  `--journal <file>|-` keeps the show journal as the app does (a file it can't open for appending is refused), and
  `/report` and `/api/report.csv` answer from it (404 "no report" while its show thread is stalled, as the app's);
  `--pin-file <file>` takes the PIN from a file's first line (never with `--pin`); `--secrets-out <file>` writes the
  PIN, the windows' addresses and the keys' secrets to a file of the user's own, so standard output and error carry none
  (each start rewrites it with that start's secrets, as its ready line would, so a key made on an earlier start isn't in
  it: read it after each start); `--exit-on-eof` stops it when its input ends. Lines on its standard input: `stall <ms>`
  stops its show thread that long, as a stuck show (`stalled`, then `running`); `auth` reads the lockout tables, each
  address in its open minute (`addresses` for wrong PINs, `viewPasses` for wrong window addresses, viewing codes and
  crew links, so a check that nothing is locked out reads both); `heard` reads its hello table, the hellos it hears with
  `--bus` and nothing else (each id's kind, address, age and conflict mark); `nodes` reads its own record of the show
  apps' node links on `/ws` (each by the id it named, what speaks for that id, key, operator or none, the key that admitted
  it by its id, never its secret, and its words for one it verifies none; a socket that only claims an id is not one);
  `exit` stops it as Ctrl+C does; anything else is
  refused and a blank line is ignored. It exits 0 when stopped, 2 on an option it can't take, 3 when a server can't
  listen and 4 when the secrets file can't be written; on 3 and 4 the node keys made in that start aren't kept.
  `tools/test.sh rig` passes `$SW_RIG_ARGS` on to it (`SW_RIG_ARGS="--node-keys 2"`).
- `tools/BusCarrierProbe` is lab line LB-1, not a test: the build lane builds it and a person runs it
  (`dotnet run --project tools/BusCarrierProbe -c Debug --no-build`). It binds six listeners on UDP 9928 as the apps
  bind theirs (every interface, 127.0.0.1 twice, an adapter's own address), sends to 127.0.0.1, 127.255.255.255 and
  255.255.255.255 from an unbound socket, as Stagewatch's hello leaves, and to 239.255.99.28 out of 127.0.0.1, and prints
  who heard what as Markdown, for `docs/WHATS_NEXT.md`'s LB-1 records (`--port`, `--count`, `--wait-ms`, `--layout`). A
  socket on the port that is not its own, from the machine's table, leaves a record unclaimed. The suite references it:
  `BusCarrierProbeTests` hold its rules and run it on a free port, so the Windows job compiles it too.
- `tests/e2e` drives it with Playwright: the window workflows (swap only when drawn, hold/cue/release, a page that won't
  load, link loss and a restarted rig, revoke), every public page drawing a real frame alone and inside a window, the
  same commands over HTTP, Companion TCP, OSC and MCP, show apps' node links on /ws (`nodes.e2e.cjs`), a stuck
  show's exact beat (`stall.e2e.cjs`), the speaker's message and its acknowledgement as a show app sends and answers it,
  the bytes Patterns' fake Stagewatch is to copy (`message.e2e.cjs`), and the rig as the interop lane drives it
  (`rig-faults.e2e.cjs`: its id across a restart, no secret on its output, `auth`, its input's end, the journal and
  report, no key kept by a start that failed). `SW_E2E_OUT=<folder>` keeps screenshots.
- `tools/showbus-copies.sh` is Show Bus 1.3's comparison of the trees (`docs/SHOWBUS.md` §14.5): it holds the master's own
  header and §14 to its body and its scripted peer (`tools/showbus/peerlink-peer.mjs`), then each tree the header's Landed
  line names — at `../Patterns`, `../Mapper`, `../AGMessage`, or `$SW_PATTERNS_DIR`, `$SW_MAPPER_DIR`, `$SW_AGMESSAGE_DIR`,
  or `Patterns=<dir>` — to it, and names each stale copy (`bash tools/showbus-copies.sh`; an absent tree is "not run", a
  failure with `SW_INTEROP_REQUIRED=1`). A tree the Landed line does not name is not compared: until then its copy is held by
  its own tree's test. The suite's `ShowBusCopiesTests` run it against this tree and trees made for it. It needs bash 4 and
  GNU sed, grep and coreutils: Linux, or Git Bash on Windows by hand.

### The interop lane (`tools/test.sh interop`)

Every other test plays the other apps with a script. The interop lane runs the real ones against each other: Stagewatch's
rig, Patterns' bench host, PlayMap's node and AGMessage's server, the three siblings at commits this tree pins, each app in
a network namespace of its own on one Linux kernel. It writes one matrix row per cross-app fact, pass or fail, with what it
read and how long it took. It is not a default lane: it builds three other repositories and runs for minutes.

```bash
tools/setup-dev.sh                                # once: Python 3.12+, unshare, nsenter, mount, the driver's packages
tools/test.sh interop                             # the build lane, the self-check, the siblings' builds, each topology
SW_INTEROP_TOPOLOGY=laptop tools/test.sh interop  # one topology: lan or laptop
tools/test.sh help                                # every lane, and every variable a person sets for it
```

**The four apps.**

| App | What runs | Built from |
|---|---|---|
| Stagewatch | the rig (`tools/Stagewatch.Rig`, above): the real web, Companion and OSC servers around a simulated show; on the LAN it is on the Show Bus port, serves its certificate beside plain HTTP and makes three node keys, one handed to Patterns and one to PlayMap in their own settings, one to nobody | this tree, as the build lane built it (the matrix says how many uncommitted files it included) |
| Patterns | its bench host (`tools/Patterns.Bench`): the real desk's services booted headless, its clips synthetic, control lines on its input | the Patterns repository at its pin |
| PlayMap | its node host (`tools/PlayMap.Node`, `engine --headless`) on null backends: it plays and answers, and draws nothing | the Mapper repository at its pin |
| AGMessage | its server (`app/server.js`), plain HTTP, on a Node its `engines` field accepts; given no key | the AGMessage repository at its pin |

**The clones and the pins.**
- `tools/interop/siblings.json` pins each sibling to one full commit, never a branch or a tag, and names what the lane
  builds there. `tools/interop/pins.py` is its one reader, for the lane and CI's workflows alike, and refuses anything else.
- `tools/interop/build.sh` makes a clone of the lane's own for each sibling, `artifacts/interop/trees/<app>` (`git clone
  --shared --no-checkout` from the sibling's repository, then a detached checkout of the pin). It never builds, checks out
  or writes in a sibling's working tree or its repository, and builds one tree at a time: Patterns and PlayMap with `dotnet
  build -c Release --disable-build-servers`, AGMessage with `npm ci --ignore-scripts --omit=dev`.
- A pin names a pushed commit only. One the sibling's repository holds must be on one of its remote branches; one it does
  not hold is fetched from its origin into the clone, never into the sibling. A commit the sibling's repository holds on
  none of its remote branches is refused, even if it was pushed since from elsewhere: `git fetch` in that repository's
  `.git` brings its remote branches up to date and changes no working tree.
- When the machine's Node is not one AGMessage's `engines` field accepts, the Node `build.sh` pins is fetched once into
  `artifacts/interop/node`, its SHA-256 checked, and never installed on the machine (`SW_INTEROP_AGMESSAGE_NODE` names
  another).
- `artifacts/interop/builds.json` then says what was built, at which commit, and on which branch each pin was seen pushed;
  the driver learns where each app is from it alone.
- The siblings' repositories are looked for at `../Patterns`, `../Mapper` and `../AGMessage`, or where `SW_PATTERNS_DIR`,
  `SW_MAPPER_DIR` and `SW_AGMESSAGE_DIR` say.
- A pin moves by hand, to a pushed commit at which every row of the lane passes; a row that needs a sibling's later work
  comes with the pin that brings it. `SW_INTEROP_HEADS=1` runs each sibling at the commit its repository has checked out
  instead (still pushed only), and `builds.json` and the matrix say how far each is from its pin: the nightly drift report,
  which moves no pin.

**The topologies.** `tools/test.sh interop` starts `tools/interop/run.sh <topology>` once per topology inside `unshare
--user --map-root-user --net --pid --fork --mount-proc --kill-child`, with 20 minutes to finish. The run's processes live in
user, network, PID and mount namespaces of their own: no network socket of the machine's or of another session's (TCP, UDP,
an abstract Unix socket) is reachable from the run, no proxy reaches in, no setting of the machine's changes, and every
process of the run ends with it, a crashed driver's included. The namespaces do not hide the machine's files: the run sees
them as its user does, so a Unix socket bound to a path (a build server's pipe, a session bus) stays reachable. No app is
pointed at one: each app's environment is the lane's own (its `PATH`, and a home and a temporary folder of its own), with no
session bus or display in it. `tools/interop/lan.py` builds the topology with rtnetlink, and only in a fresh namespace made
for the run.

| Topology | What it is | Its driver |
|---|---|---|
| `lan` | separate hosts on one switch: a bridge at 10.99.0.1 (the driver, which never listens on the Show Bus port) and a namespace per app joined to it by a veth, each with its own address and loopback: the rig 10.99.0.2, Patterns 10.99.0.3, PlayMap 10.99.0.4, AGMessage 10.99.0.5. A broadcast crosses the bridge as on a real LAN, and a cable is pulled and put back at the bridge's end, so the host keeps its addresses as a machine with a pulled cable does | `tests/interop/lan.interop.cjs` |
| `laptop` | the maintainer's show laptop: the four apps on one host, its Wi-Fi a veth at 10.99.0.2 with a default route, the access point at 10.99.0.1; each app is typed the others' addresses as on one machine, 127.0.0.1 and the port | `tests/interop/laptop.interop.cjs` |
| `offline` | one host with loopback only: `lan.py offline` builds it, and no driver runs on it yet (its rows are named in `docs/WHATS_NEXT.md`) | none yet |

**Secrets.** Every secret the run makes or learns is in the run's register: the rig's PIN and node keys, its certificate's
password (kept unsealed beside the key off Windows), each app's token, and AGMessage's admin and context tokens. The driver
hands a secret to an app through an owner-only file in the run's secrets folder, a tmpfs of the run's own mount namespace
that is gone with the run; AGMessage's admin token through its environment, its one door for it; and PlayMap's own token,
Patterns' token and PlayMap's node key through an owner-only settings file in PlayMap's home (`playmap.node.json`, its door
for them), on the disk; and, in row S2.1b, two of the rig's keys (the free key, then PlayMap's own again) through
owner-only files in the secrets folder to a helper started in PlayMap's namespace, which pairs by proof with PlayMap's
token (from a file there too) and says each key to PlayMap's wire at 127.0.0.1 (`LINK STAGEWATCH`), as its operator types
it at its own machine, so no key crosses the run's LAN; the lane keeps what that wire says back, never what it sends. None
goes on a command line, and the one line an app prints a secret in by design
(AGMessage's start banner, with its admin token) is redacted by name before it is kept; every other line is kept as said.
The files an app keeps a secret in by design are on the disk, owner-only in the run's owner-only folder, until the app has
stopped and the driver removes them: PlayMap's settings and its keys, Patterns' settings, recovery record and certificate,
the rig's state and its certificate's folder (its key, and the key's password unsealed off Windows), and AGMessage's
`data/secret.key` and `keys`. A run cut short by its 20 minutes or by Ctrl+C does not get to remove them: they stay until
that topology's next run removes its folder, and `publish.py` never copies them. Row S7.1 reads every file the run kept,
once those are removed, for every secret of the register, in either case. A row's words name a secret and never say it.
`run.sh` hands the driver its topology in `SW_INTEROP_RUN_TOPOLOGY`, which nobody sets by hand.

**The rows.**
- Each row is one cross-app fact: a `node:test` test that writes its matrix row. The rows run in order: S0.1 starts the
  apps, the rows that take an app down come last but one, and S7.1 last, since it stops the apps to sweep what they left.
- A row is added only once it passes against the real apps. One that cannot pass yet is not in the lane, and
  `docs/WHATS_NEXT.md` names it with what it waits on.
- Each deadline is the contract's number × 1.5 + 2 s: a hello every 2 s, so heard within 5 s; stale after 6 s, said within
  11 s; gone after 12 s, within 20 s; a stuck show thread said within 9.5 s; a context within 9.5 s. It counts from the
  moment the app that caused the event says it happened, else from the driver's act, and a hello from the moment the last
  app said it was ready. A row that only gets to read after its deadline fails rather than claim it.
- After every row both of the rig's lockout tables (wrong PINs; wrong view passes) are read, and no address of the run is
  counted in either: on the LAN the switch's and every app's, on the laptop 127.0.0.1 and its own address. A control from
  those addresses shows each table's reader sees them.

| Row | On | What it holds |
|---|---|---|
| S0.1 | lan | each app is ready in its own namespace, within 60 s of its start |
| S1.1 | lan | Patterns hears PlayMap and AGMessage, each from its own address |
| S1.2 | lan | PlayMap hears Patterns and AGMessage, each from its own address where its NODES says one |
| S1.3 | lan | AGMessage hears Patterns from its own address |
| S1.4 | lan | the rig's hello table lists Patterns, PlayMap and AGMessage, each from its own address, live and unmarked, and its context's peers are each live |
| S1.5 | lan | every app hears the rig: kind `main`, its node id, from the rig's own address where the reader says one |
| S1.6 | lan | one id is one peer: read every 500 ms for 20 s, the rig, Patterns and PlayMap (its NODES, its STATE's peers and its context's) each list every show app they hear once; PlayMap reads the rig's Show Bus 1.3 hello as live and unmarked at the rig's one address at every read, and marks an id two senders say in turn (a control: hellos of the driver's own id, sent to PlayMap alone) |
| S2.1 | lan | PlayMap's context reaches the rig: its link paired with the rig's certificate pinned, the rig's row for it proved by its own key, and a change of it there and back within 9.5 s |
| S2.1b | lan | PlayMap reads the rig's verdict on its node link from both ends, its link and the rig's own record (`nodes`): its link taken off until the rig holds none for it, then given the free key, which speaks for no node, at its own machine, it is refused in the rig's own words and the rig's record says that key's link verified none in them; its link live and never paired through a 7 s watch (amber in its context, no proven link in the rig's row or record); given its own key again, it is paired again with the rig's certificate still pinned, the rig's record its key's alone |
| S2.3 | lan | Patterns' context reaches the rig: its link over TLS with its key, the rig's row for it proved by that key, and a change of it there and back within 9.5 s |
| S2.4 | lan | Patterns reads the rig's context: its STATE says the rig's own headline, and a change of it there and back within 9.5 s |
| S2.6 | lan | PlayMap's, Patterns' and the rig's own contexts are each under 64 KiB as carried |
| S2.7 | lan | a node key cannot speak for another node: each such post is refused 403 with Stagewatch's words, no forged word reaches the rig's context, and every key stays bound as it was |
| S4.5 | lan | PlayMap follows Patterns' clip and blackout, paired by proof |
| S6.1 | lan | the rig killed and restarted: every link lets go, then is gone, then is live again |
| S6.1b | lan | the rig's cable pulled: every link goes stale, then gone, through silence; put back, every link is live again |
| S6.2a | lan | a stuck Stagewatch: PlayMap's link stays live and shows no time, for Stagewatch's own reason, and the rig's running time again once the show turns over |
| S7.1 | lan, laptop | no secret of the register in any log, frame, STATE, context, report or capture the run kept |
| S7.3 | lan | unknown words are refused by Patterns, PlayMap and the rig |
| S1c.1 | laptop | on one laptop every app hears every other from the laptop's own addresses, and every link to the rig is live at 127.0.0.1 |
| S1c.2 | laptop | PlayMap follows Patterns at 127.0.0.1, paired by proof |

The suite's `InteropDocTests` hold this table to the drivers, so a row a driver adds or drops fails the suite until the
table says so: a driver's rows are its top-level `test(…, row({ id: '…', … }))` calls, one per id, and a driver that skips,
marks as to do or singles out a test is refused. They also hold that this section names every variable the lane's scripts
and drivers read, and that `tools/test.sh help` prints its header, which names every lane it runs and every variable a
person sets for the lane (every one the lane reads but `SW_INTEROP_RUN_TOPOLOGY`, which `run.sh` sets).

**What a run leaves** (in `artifacts/interop`, which git ignores, or the folder `SW_INTEROP_OUT` names):
- `builds.json`, and per topology `matrix.json` and `matrix.md`: the machine, each build's commit and the words each app
  said of itself, each row's verdict, its time against its deadline, the lockout counts read after it, its evidence files
  and what it read;
- `<topology>/run`, owner-only: each app's output and error, its home, the wires' lines and the driver's captures of the
  rig's doors;
- `python3 -I tools/interop/summary.py` prints what was built and each topology's rows; `tools/interop/publish.py` copies
  what may leave the machine (CI's artifact): `builds.json`, the matrices and, only for a topology whose sweep passed in
  that run, its logs, its captures and the apps' journals and logs.

**Its verdict.** The lane passes when every row of every topology passes, and fails when one fails. First
`tools/interop/selfcheck.py` checks the lane's own scripts (`pins.py`, `build.sh` at pins and at heads, `summary.py`,
`publish.py`) on fixtures of their own, with no network, and the lane does not run when one fails. Where a sibling's
repository is absent, a pin cannot be fetched, no Node for AGMessage is found or fetched, or the kernel gives this user no
user, network and PID namespaces, bridge or veth, the lane is "not run", said with why, and never counted as passed;
`SW_INTEROP_REQUIRED=1`, as CI sets it, makes that a failure. The lane changes no setting of the machine's to run: where
unprivileged user namespaces are confined (Ubuntu 24.04's AppArmor), it is "not run" until that machine's owner allows them.

**In CI.** The `interop` job of `.github/workflows/ci.yml` runs the lane at the pins with `SW_INTEROP_REQUIRED=1` on this
repository's pushes to main, its pull requests, its tags and its runs by hand, and a release waits for it.
`.github/workflows/interop-heads.yml` runs it nightly at the siblings' heads and gates nothing. Both read the siblings with
the repository secret `SIBLINGS_READ_TOKEN`, and on their throwaway runner alone load `bridge` and `veth` and lift Ubuntu's
AppArmor confinement of unprivileged user namespaces. A fork's pull request gets no secret, and the Linux job says "interop:
not run". `docs/WHATS_NEXT.md` has the job's details and the maintainer's actions.

**What it proves.** That the four apps, at the pinned commits, built from their own sources and run as they run:
- find each other on the Show Bus by real hellos on UDP 9928, each from its own address, one peer per id, PlayMap's reader
  marking an id two senders say;
- link with real keys, tokens and proofs: PlayMap paired with Patterns by proof, Patterns' link to the rig over TLS with its
  key, PlayMap's with the rig's certificate pinned, paired by the rig's verdict for its own key and never by a live link
  alone (a key that speaks for no node refused in the rig's words);
- carry PlayMap's and Patterns' contexts to the rig and the rig's to Patterns, within Show Bus §14's bounds, and refuse
  unknown words and a key speaking for another node;
- let every link to the rig go when the rig is killed or its cable pulled, and come back; PlayMap shows no time while the
  rig's show thread is stuck, and the rig's time again after;
- lock no address of the run out of Stagewatch, and leave no secret of the run's register in any log, frame, STATE,
  context, report or capture the run keeps (a secret an app makes for itself, such as its own certificate's key or
  AGMessage's `data/secret.key`, is not in the register and is not looked for);
- on separate hosts on one switch, and, for hearing each other, PlayMap following Patterns, the lockout and the sweep, on
  one show laptop; each run from a clean start, with each fact's measured time.

**What it does not prove.**
- **Two machines and a real network.** One kernel is not two machines: a firewall's prompts, a machine with two networks,
  Wi-Fi lost and found, and Windows' own UDP stack are walks on the rig (Patterns' QUALIFICATION §26 and §27, its OPEN rows
  L47, L50 and L56; LB-1 above for one machine with no network).
- **Windows.** The WPF desk (its Show network line, its mirror, its right-click menus: the UI smoke and the PC's walk),
  WebView2 (the design's S5.1w, Patterns showing a granted page: a Windows job or the walk), DPAPI (the rig's certificate
  password is unsealed off Windows) and PlayMap's Direct3D 12 never run on the lane.
- **The desk itself.** The rig is Stagewatch's servers around a simulated show, not the WPF app, so the app's own link to
  Patterns (`PlayoutLink`) is held by the suite against scripted desks and the scripted peer, not by the lane.
- **Pictures, media and timing.** Patterns' clips are synthetic and PlayMap draws nothing: no decoder, renderer or output
  runs, and the deadlines are the contract's bounds with slack, not a show machine's latency.
- **Browsers and AI.** No browser runs on the lane yet (windows, pages and grants come with the rows that need one, and
  Xvfb, Mesa and Playwright with them), and no model runs: nothing of the assistant or the copilot is in a row.
- **Certificates over time.** A certificate replaced, or a tablet that trusts one, is in no row.
- **Each app's own promises.** That neither Patterns nor PlayMap sends its key in clear off its own machine is held by its
  own tests (Patterns' Round 89.5, PlayMap's PM-2), not read by the lane.
- **The rows that wait.** AGMessage's keyed rows, the rows that need a sibling's later work, the offline laptop, and the
  design's desk, VT, windows and doors rows are not in the lane until they pass; `docs/WHATS_NEXT.md` names each with what
  it waits on.

## Single-file publish

```pwsh
dotnet publish src/Timer.App -c Release -r win-x64 -p:PublishSingleFile=true --self-contained false -o publish/
```

Self-contained also works (`--self-contained true`); larger but no .NET runtime install needed on target.

## Sign

`tools
elease.ps1` signs the exe and the installer (SHA-256, RFC 3161 timestamp, then verifies) when
`STAGEWATCH_SIGN_THUMBPRINT` holds the thumbprint of a code-signing certificate in your certificate store
(an EV or OV certificate from a CA; for cloud HSM certificates, install the vendor's signing tool so the
certificate appears in the store). Without it, the build is unsigned and says so.

```pwsh
$env:STAGEWATCH_SIGN_THUMBPRINT = '0123456789ABCDEF0123456789ABCDEF01234567'
powershell -ExecutionPolicy Bypass -File tools
elease.ps1 -Bump patch
```

Needs `signtool.exe` from the Windows 10/11 SDK.

## Installer

The installer asks "only for me" (no admin rights, installs under the user profile) or "all users"
(admin, Program Files). It registers the `.stagewatch` show-file type for that scope. Silent per-user install:
`Stagewatch-Setup.exe /VERYSILENT /CURRENTUSER`.

Open `installer/Stagewatch.iss` in Inno Setup. Adjust the version, the source paths, and the VSTO add-in flag. Build → produces `installer\Output\Stagewatch-Setup.exe`.

## Reproducible builds

`Deterministic=true` is set in every csproj; combined with single-file publish on a fixed .NET 10 SDK version this gives a byte-identical output for a given commit. Useful when validating that a release binary corresponds to a specific source tag.

## Smoke test checklist

After a build:

- Launch the exe, verify the trial counter shows "Trial — 14 day(s) left" the first time, then loads instantly.
- Click START — timer counts down. Try -1m, +1m nudges while running.
- Press 1, 2, 3 — presets load.
- Hit "Open output" — appears on the configured monitor in full screen.
- "Cycle mode" → keyed full screen → blue background.
- "Cycle key" → green/magenta/black.
- Send a message "TEST", set Critical + Fast flash — output flashes.
- `telnet 127.0.0.1 9923` — connect, send `START\n`, see `TICK` events.
- Open PowerPoint, start a slideshow — overlay appears lower-left, ticks live.
