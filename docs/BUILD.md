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
```

- The suite names the checks it leaves to the Windows job (`Bench.OnWindows`): today the WMI hardware id, the crafted
  master file, the report file names and the slides agent's PowerPoint loop. On Windows it names the one it leaves to the
  Linux bench (`Bench.OffWindows`): `tools/showbus-copies.sh` run by bash. Neither is ever counted as passed.
- `tools/Stagewatch.Rig` is the real web server, Companion server and OSC server around a simulated show (timer,
  messages, blackout, a holding image, Next/Previous/GoTo, windows, screen commands, the contexts show apps post and
  their node links); anything else is refused with words. Its first output line is JSON with its ports, its node id and
  the node keys made in this run (`--port`, `--companion`, `--osc`, `--pin`, `--state`, `--view`, `--node-keys`,
  `--exit-after`). For the interop lane: `--node-id <id>` keeps one id across restarts; `--journal <file>|-` keeps the
  show journal as the app does (a file it can't open for appending is refused), and `/report` and `/api/report.csv`
  answer from it (404 "no report" while its show thread is stalled, as the app's); `--pin-file <file>` takes the PIN
  from a file's first line (never with `--pin`); `--secrets-out <file>` writes the PIN, the windows' addresses and the
  keys' secrets to a file of the user's own, so standard output and error carry none (each start rewrites it with that
  start's secrets, as its ready line would, so a key made on an earlier start isn't in it: read it after each start);
  `--exit-on-eof` stops it when its input ends. Lines on its standard input: `stall <ms>` stops its show thread that
  long, as a stuck show (`stalled`, then `running`); `auth` reads the lockout tables, each address in its open minute
  (`addresses` for wrong PINs, `viewPasses` for wrong window addresses, viewing codes and crew links, so a check that
  nothing is locked out reads both); `exit` stops it as Ctrl+C does; anything else is refused and a blank line is
  ignored. It exits 0 when stopped, 2 on an option it can't take, 3 when a server can't listen and 4 when the secrets
  file can't be written; on 3 and 4 the node keys made in that start aren't kept. `tools/test.sh rig` passes
  `$SW_RIG_ARGS` on to it (`SW_RIG_ARGS="--node-keys 2"`).
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
  show's exact beat (`stall.e2e.cjs`) and the rig as the interop lane drives it (`rig-faults.e2e.cjs`: its id across a
  restart, no secret on its output, `auth`, its input's end, the journal and report, no key kept by a start that
  failed). `SW_E2E_OUT=<folder>` keeps screenshots.
- `tools/showbus-copies.sh` is Show Bus 1.3's comparison of the trees (`docs/SHOWBUS.md` §14.5): it holds the master's own
  header and §14 to its body and its scripted peer (`tools/showbus/peerlink-peer.mjs`), then each tree the header's Landed
  line names — at `../Patterns`, `../Mapper`, `../AGMessage`, or `$SW_PATTERNS_DIR`, `$SW_MAPPER_DIR`, `$SW_AGMESSAGE_DIR`,
  or `Patterns=<dir>` — to it, and names each stale copy (`bash tools/showbus-copies.sh`; an absent tree is "not run", a
  failure with `SW_INTEROP_REQUIRED=1`). A tree the Landed line does not name is not compared: until then its copy is held by
  its own tree's test. The suite's `ShowBusCopiesTests` run it against this tree and trees made for it. It needs bash 4 and
  GNU sed, grep and coreutils: Linux, or Git Bash on Windows by hand.

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
