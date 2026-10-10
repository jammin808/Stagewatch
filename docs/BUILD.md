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
dotnet run --project tests/Stagewatch.Tests      # engine, schedule, CSV, commands, OSC, web, Companion, licensing
cd companion-module; npm test                      # Companion module
```
Both exit non-zero on any failure. The positive licence-signing tests run only where the private key exists.

## Single-file publish

```pwsh
dotnet publish src/Timer.App -c Release -r win-x64 -p:PublishSingleFile=true --self-contained false -o publish/
```

Self-contained also works (`--self-contained true`); larger but no .NET runtime install needed on target.

## Sign

`toolselease.ps1` signs the exe and the installer (SHA-256, RFC 3161 timestamp, then verifies) when
`STAGEWATCH_SIGN_THUMBPRINT` holds the thumbprint of a code-signing certificate in your certificate store
(an EV or OV certificate from a CA; for cloud HSM certificates, install the vendor's signing tool so the
certificate appears in the store). Without it, the build is unsigned and says so.

```pwsh
$env:STAGEWATCH_SIGN_THUMBPRINT = '0123456789ABCDEF0123456789ABCDEF01234567'
powershell -ExecutionPolicy Bypass -File toolselease.ps1 -Bump patch
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
