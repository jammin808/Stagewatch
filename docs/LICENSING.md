# Licensing & threat model

## Tiers

Prices and terms live in code in `Pricing` (`src/Timer.Core/FeatureTier.cs`); the app's Help → License → Plans
tab is generated from it. Every licence is one-off and works offline, for ever, on the machines it was issued for.

| Tier | Price | Machines | Highlights |
|---|---|---|---|
| **Trial** | Free | — | 14 days, every feature, no watermark |
| **Free** | Free | — | After the trial: basic timer (countdown / count-up / clock, nudge, typed messages, 2 presets, full-screen output). Every output carries "StageWatch by Ben Rohleder". |
| **Standard** | £9 one-off | 1 | The core timer: all presets, end-at timing, alerts, quick messages, NDI, keyed outputs, cue ladders |
| **Pro** | £29 one-off | 1 | One operator, one room: + running order, browser remotes and roles, HTTP / OSC, Companion, PowerPoint, Q&A, triggers, rehearsal ghost, CSV report |
| **Production** | £79 one-off (launch £59) | 2 (main + backup) | The crew loop and multi-room kit: + multi-room dashboard, DMX / sACN / Hue cue lights, acknowledged cues, departments, green-room calls, pocket buzz, forecast and curfew trim suggestions, LTC and time-server drift check, vMix VT, full printable report, speech pace, producer assistant, MCP |
| **Production event pass** | £15 / 30 days | 1 | Everything in Production for one event; credited towards a full Production licence |
| **Master** | — | — | Owner / testing only |

**Nonprofit pricing** — charities, churches, schools and universities get 30% off everything: Standard £6,
Pro £20, Production £55, event pass £11 (the licence dialog shows these, from `Pricing.Nonprofit`). Rental
houses: 5-packs at 20% off (issue five licences; note the order in your sales log).

**Event passes never cut an event short.** A pass warns in the status bar for its last 3 days, then keeps
working for 2 days' grace after its end date (`Pricing.EventPassGraceDays`) with a notice, and only then
drops to Free. The licence is checked when Stagewatch starts, so a running show is never interrupted.
Issue one with `KeyGen event-pass <hwid> <email>`.

### Production updates

- **Early adopters: lifetime updates.** Issued with no `--updates` option (the default).
- **Later:** a Production licence includes one year of updates (`--updates 1`); after that, updates are
  **£20 / year** — renew by issuing replacement files with `--updates <new end date>`.
- **Nothing ever stops working.** The licence carries an update end date (`upd`, signed). Each build carries its
  release date (`AppInfo.ReleaseDate`, stamped by `tools\release.ps1` on a version bump). A build released *after*
  the update window runs the licence as **Pro** and says why (Help → License and the status line); any build
  released inside the window keeps running as Production for ever.
- Standard and Pro licences include updates for their tier for life.

## How a license is verified

```
license.dat (on disk)
   │
   ▼ AES-GCM(decrypt with HKDF(BuildPepper, salt=HWID))   ← Layer 2: at-rest
   ▼
   License JSON {tier, hwid, email, exp, upd?, seat?, sig}
   │
   ▼ ECDSA-P256-SHA256.Verify(publicKey, canonical(license), sig)   ← Layer 1: integrity/auth
   ▼
   if hwid == this machine && exp > now → grant tier
   (Production with upd < this build's release date → Pro, with a renewal notice)
```

Both layers must pass. A stored master license is accepted only if the master serial inside it
verifies against the public key again on every launch — a hand-made `license.dat` claiming
"master" (anyone can encrypt one, the pepper ships in the exe) unlocks nothing.

## Keys

| Item | Where | Notes |
|---|---|---|
| Signing **private** key | `%USERPROFILE%\.stagewatch\license-signing-key.pem` (or `$STAGEWATCH_SIGNING_KEY`) | Owner only. **Back it up offline.** Never commit it, never ship it. Lose it and you can't issue licenses for builds that trust it. |
| Signing **public** key | `CryptoEnvelope.PublicKeySpki` | In the binary. Safe to publish. |
| Build pepper | `CryptoEnvelope.BuildPepper` | At-rest binding only, not a secret. Changing it invalidates existing `license.dat` / trial files. |

ECDSA P-256 is built into .NET, so the trust path has no third-party dependency.

## Master serial

```
SWM2.<id>.<base64url ECDSA signature of "stagewatch-master-v2:<id>">
```

- Only the owner's private key can produce one; nothing in the binary can mint it.
- It is not machine-bound: it unlocks Master on any PC running a build that trusts this public key.
- Issue: `dotnet run --project tools/KeyGen -- master-serial [id]` (default id `OWNER-yyyyMMdd`).
- Use: Help → License → Enter master serial, or env var `STAGEWATCH_MASTER`.
- Your current owner serial is saved in `%USERPROFILE%\.stagewatch\master-serial.txt` — **never** in this repo.

The old `STAGEWATCH-MASTER-…` serials (HMAC of the pepper, including the public dev serial) are no longer accepted.

## Issuing a customer license

```pwsh
# Customer sends you their hardware ID (Help → License → Plans → Copy, or Settings → About)
dotnet run --project tools/KeyGen -- license standard <HWID> customer@example.com
dotnet run --project tools/KeyGen -- license pro <HWID> customer@example.com
# → writes license-L-XXXX.dat

# Production: main + backup PC (one licence id, one file per machine), lifetime updates (early adopters)
dotnet run --project tools/KeyGen -- license production <HWID-MAIN>,<HWID-BACKUP> customer@example.com
# → license-L-XXXX-main.dat, license-L-XXXX-backup.dat

# Production after the early-adopter period: one year of updates
dotnet run --project tools/KeyGen -- license production <HWID-MAIN>,<HWID-BACKUP> customer@example.com --updates 1
# Renewal (£20/year): reissue with the new end date
dotnet run --project tools/KeyGen -- license production <HWID-MAIN>,<HWID-BACKUP> customer@example.com --updates 2028-10-09

# 30-day Production event pass (credit the £15 if they buy Production)
dotnet run --project tools/KeyGen -- event-pass <HWID> customer@example.com

# A trailing number of days makes any licence expire (e.g. a time-limited evaluation)
dotnet run --project tools/KeyGen -- license pro <HWID> customer@example.com 30

# Email the .dat. Customer installs via Help → Install license file.
```

## Threat model (the honest part)

> Anyone who tells you "this can't be reverse engineered" is selling you something.

Anything that decides "is this user paid?" on the user's machine can, in principle, be patched to always say yes. Our goal is not to make it impossible — it's to **make casual cracking expensive enough that paying is easier**, and to detect and revoke abuse for serious cases.

What this build does to raise the bar:

| Layer | Mechanism | What it stops |
|---|---|---|
| **Anti-tamper at rest** | AES-GCM with HWID-derived key | Sharing a license file across machines |
| **Authenticity** | ECDSA P-256 signature on canonical JSON (owner-held private key) | Forging or editing a license, minting master serials |
| **Hardware binding** | License embeds a SHA-256 of (Windows MachineGuid, motherboard UUID). No network adapters: docking, venue Ethernet, VPNs and Wi-Fi changes must never invalidate a licence mid-show | Bulk reuse across a "license server" |
| **Trial binding** | Same hwid binding + 3-anchor agreement (file + registry + ADS) | Trivial trial extension by deleting one file |
| **Tamper detection** | Self-hash check + debugger detection on startup | Patched-binary attempts in a debugger |
| **Obfuscation** | Apply a commercial protector (Themida, Eazfuscator, .NET Reactor) to the **release** binary | Static analysis of the verify code path |
| **Online check-in (optional, Pro)** | Periodic POST to a license endpoint with a nonce; server can revoke | Long-tail abuse from leaked Pro keys |
| **Watermark** | After the trial (Free mode) every output — output window, NDI, browser viewer — shows "StageWatch by Ben Rohleder", even during blackout | Production use of an unlicensed install |

What this build **does not** claim:

- **It is not unbreakable.** A motivated reverse engineer with a debugger and 1-2 days will defeat any client-side licensing scheme. The defense-in-depth here just makes the cracker's time cost > the licence price for any rational attacker.
- **Code obfuscation is applied at ship time, not source time.** Don't rely on the source repo for protection — apply Themida/Reactor to the published binary as part of release.

## What "dual encryption" means here

Two independently keyed primitives that both have to pass:

1. **AES-GCM** at rest, key = `HKDF(BuildPepper, salt=HWID, info="stagewatch-license-v1")`.
2. **ECDSA P-256 / SHA-256** signature over the canonical license JSON, verified against an owner-controlled public key embedded in the binary.

This is not "encrypt-then-encrypt" (which is meaningless if both keys are accessible). It is **encryption + authenticated signing**, which is the right shape for license files.

## Key rotation

If your private signing key is exposed:
1. `stagewatch-keygen keypair --force`, paste the new public key into `CryptoEnvelope.PublicKeySpki`.
2. Ship a new release. Every old license and master serial stops verifying — reissue customer licenses out-of-band.

If a master serial is leaked: master serials can't be revoked individually without a key rotation, so
rotate the key pair as above and issue a new owner serial. (Keep master serials to yourself.)

## Moving to a new PC

The hardware ID changes only on a Windows reinstall or a motherboard swap. Ask the customer for the new
Hardware ID (Settings → About) and issue a fresh licence with KeyGen; keep a note of the old licence ID.

## Signing out

Help → Sign out removes `license.dat`. The trial cannot be re-extended on the same machine — the trial anchors are still present and earliest wins.
