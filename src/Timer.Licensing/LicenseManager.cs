using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Stagewatch.Core;

namespace Stagewatch.Licensing;

/// <summary>
/// Loads, verifies, and tier-resolves the active license. Public entry point for the app.
///
/// Resolution order on startup:
///   1. Master serial in env var STAGEWATCH_MASTER (owner-only)
///   2. Stored license file in %APPDATA%\Stagewatch\license.dat
///   3. Trial (auto-created on first run, 14 days)
///   4. Locked
/// </summary>
public sealed class LicenseManager
{
    /// <summary>Master serial format: <c>SWM2.&lt;id&gt;.&lt;base64url ECDSA signature&gt;</c>.</summary>
    public const string MasterSerialPrefix = "SWM2.";
    private const string MasterMessagePrefix = "stagewatch-master-v2:";

    public LicenseStatus CurrentStatus { get; private set; } = LicenseStatus.Locked();

    public LicenseStatus LoadOnStartup()
    {
        // 1. Master serial via environment variable (owner-only path)
        var envMaster = Environment.GetEnvironmentVariable("STAGEWATCH_MASTER");
        if (!string.IsNullOrWhiteSpace(envMaster) && IsValidMasterSerial(envMaster))
            return CurrentStatus = LicenseStatus.MasterFromSerial(envMaster);

        // 2. Stored license
        var stored = TryLoadStored();
        if (stored is not null)
        {
            var st = ResolveStatus(stored);
            if (st.Tier != FeatureTier.Locked) return CurrentStatus = st;
        }

        // 3. Trial
        var rem = TrialTracker.Remaining();
        if (rem > TimeSpan.Zero)
            return CurrentStatus = LicenseStatus.Trial(rem);

        // 4. Locked
        return CurrentStatus = LicenseStatus.Locked();
    }

    /// <summary>Activate via a master serial typed in the UI.</summary>
    public LicenseStatus TryActivateMasterSerial(string serial)
    {
        if (!IsValidMasterSerial(serial))
            return CurrentStatus = CurrentStatus with { LastError = "Invalid master serial" };

        // Persist as a synthetic license so subsequent runs don't need the env var.
        var lic = new License
        {
            Version = 1,
            LicenseId = "MASTER-" + Guid.NewGuid().ToString("N")[..8],
            Tier = FeatureTier.Master,
            Email = "owner@stagewatch",
            HardwareId = HardwareId.Get(),
            IssuedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ExpiresAtUnix = null,
            IsMaster = true,
            // The serial itself is the signed proof; it is re-verified on every load, so a
            // hand-crafted license.dat claiming "master" is worthless.
            SignatureBase64 = "MASTER:" + serial.Trim()
        };
        SaveLicense(lic);
        return CurrentStatus = LicenseStatus.MasterFromSerial(serial);
    }

    /// <summary>Activate via a signed license blob pasted/installed by the user.</summary>
    public LicenseStatus TryInstallLicenseFile(byte[] envelopeBytes)
    {
        var hwid = HardwareId.Get();
        var pt = CryptoEnvelope.Decrypt(envelopeBytes, hwid);
        if (pt is null)
            return CurrentStatus = CurrentStatus with { LastError = "License file is invalid or for a different machine" };

        License? lic;
        try { lic = JsonSerializer.Deserialize<License>(pt); }
        catch { return CurrentStatus = CurrentStatus with { LastError = "License file is malformed" }; }
        if (lic is null) return CurrentStatus = CurrentStatus with { LastError = "License file is empty" };

        if (lic.HardwareId != hwid)
            return CurrentStatus = CurrentStatus with { LastError = "License is bound to a different machine" };

        // Full verification (signature, or re-verified master serial, plus expiry) BEFORE saving.
        var resolved = ResolveStatus(lic);
        if (resolved.Tier == FeatureTier.Locked)
            return CurrentStatus = CurrentStatus with { LastError = resolved.LastError ?? "License signature is invalid" };

        SaveLicense(lic);
        return CurrentStatus = resolved;
    }

    public void SignOut()
    {
        try { File.Delete(LicensePath()); } catch { }
        CurrentStatus = LicenseStatus.Locked();
    }

    private License? TryLoadStored()
    {
        try
        {
            var path = LicensePath();
            if (!File.Exists(path)) return null;
            var hwid = HardwareId.Get();
            var pt = CryptoEnvelope.Decrypt(File.ReadAllBytes(path), hwid);
            if (pt is null) return null;
            return JsonSerializer.Deserialize<License>(pt);
        }
        catch { return null; }
    }

    private void SaveLicense(License lic)
    {
        var path = LicensePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var pt = JsonSerializer.SerializeToUtf8Bytes(lic);
        var blob = CryptoEnvelope.Encrypt(pt, HardwareId.Get());
        File.WriteAllBytes(path, blob);
    }

    private LicenseStatus ResolveStatus(License lic)
    {
        if (lic.IsMaster && lic.SignatureBase64.StartsWith("MASTER:", StringComparison.Ordinal))
        {
            return IsValidMasterSerial(lic.SignatureBase64["MASTER:".Length..])
                ? LicenseStatus.MasterFromFile(lic)
                : LicenseStatus.Locked() with { LastError = "Stored master license is not valid for this build" };
        }
        return Evaluate(lic, HardwareId.Get(), DateTime.UtcNow, AppInfo.ReleaseDateUtc);
    }

    /// <summary>
    /// Pure licence evaluation (signature, machine, expiry, update entitlement) — testable without
    /// touching this PC's licence file.
    ///
    /// Update entitlement: a licence whose updates ended before this build's release date still works,
    /// one tier down (Production → Pro), and says so; reinstalling a build released before that date
    /// runs it fully, for ever. Lifetime-update licences (no "upd") are never affected.
    /// </summary>
    public static LicenseStatus Evaluate(License lic, string hardwareId, DateTime nowUtc, DateTime releaseUtc)
    {
        if (lic.IsMaster)
            return LicenseStatus.Locked() with { LastError = "Master licences are verified from their serial" };

        if (!CryptoEnvelope.VerifySignature(lic))
            return LicenseStatus.Locked() with { LastError = "License signature failed" };

        if (lic.HardwareId != hardwareId)
            return LicenseStatus.Locked() with { LastError = "License is for a different machine" };

        string? passNotice = null;
        if (lic.ExpiresAt is { } exp)
        {
            // An event pass never stops mid-event: it keeps working for a grace period after it ends, and warns first.
            var graceEnd = exp.AddDays(Pricing.EventPassGraceDays);
            if (graceEnd < nowUtc)
                return LicenseStatus.Locked() with { LastError = $"License expired {exp:yyyy-MM-dd}" };
            if (exp < nowUtc)
                passNotice = $"Your event pass ended {exp.ToLocalTime():d MMM HH:mm}. It keeps working until {graceEnd.ToLocalTime():d MMM HH:mm} so your event isn't cut short — " +
                             $"the £{Pricing.EventPass} you paid counts towards a full licence.";
            else if (exp - nowUtc < TimeSpan.FromDays(Pricing.EventPassWarnDays))
                passNotice = $"Your event pass ends {exp.ToLocalTime():ddd d MMM HH:mm} (then {Pricing.EventPassGraceDays} days' grace). " +
                             $"The £{Pricing.EventPass} you paid counts towards a full licence.";
        }

        if (lic.Tier is FeatureTier.Master or FeatureTier.Trial or FeatureTier.Locked)
            return LicenseStatus.Locked() with { LastError = "License tier is not valid" };

        if (lic.UpdatesUntil is { } until && until < releaseUtc && lic.Tier == FeatureTier.Production)
        {
            var notice = $"Updates ended {until:d MMM yyyy}; this version was released {releaseUtc:d MMM yyyy}, so it runs as Pro. " +
                         $"Renew updates (£{Pricing.UpdateRenewalPerYear}/year) for Production, or reinstall a version released before {until:d MMM yyyy}.";
            return LicenseStatus.FromTier(FeatureTier.Pro, lic) with { Notice = notice };
        }

        return LicenseStatus.FromTier(lic.Tier, lic) with { Notice = passNotice };
    }

    /// <summary>
    /// A master serial is an owner-signed token (issued offline by KeyGen with the private
    /// signing key). Nothing in the binary can mint one, unlike the old HMAC-of-pepper serials.
    /// </summary>
    public static bool IsValidMasterSerial(string serial)
    {
        var t = serial.Trim();
        if (!t.StartsWith(MasterSerialPrefix, StringComparison.Ordinal) || t.Length > 300) return false;
        var parts = t.Split('.');
        if (parts.Length != 3 || parts[1].Length is < 1 or > 64) return false;
        byte[] sig;
        try { sig = Base64UrlDecode(parts[2]); }
        catch (FormatException) { return false; }
        return CryptoEnvelope.VerifyOwnerSignature(MasterMessage(parts[1]), sig);
    }

    /// <summary>The bytes the owner signs for master serial <paramref name="id"/> (shared with KeyGen).</summary>
    public static byte[] MasterMessage(string id) => Encoding.UTF8.GetBytes(MasterMessagePrefix + id);

    public static string Base64UrlEncode(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
    }

    private static string LicensePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "Stagewatch", "license.dat");
    }
}

/// <summary>The resolved license outcome surfaced to the rest of the app.</summary>
public sealed record LicenseStatus(
    FeatureTier Tier,
    string DisplayName,
    TimeSpan? TrialRemaining = null,
    DateTime? ExpiresAt = null,
    string? LastError = null,
    License? RawLicense = null,
    /// <summary>Something the operator should know about the licence (e.g. updates ended).</summary>
    string? Notice = null
)
{
    public bool IsLocked => Tier == FeatureTier.Locked;
    /// <summary>Trial over and no license: every output carries <see cref="FeatureGate.WatermarkText"/>.</summary>
    public bool ShowsWatermark => FeatureGate.ShowsWatermark(Tier);

    public static LicenseStatus Locked() =>
        new(FeatureTier.Locked, "Free — trial ended");

    public static LicenseStatus Trial(TimeSpan remaining) =>
        new(FeatureTier.Trial, $"Trial — {Math.Floor(remaining.TotalDays)} day(s) left", TrialRemaining: remaining);

    public static LicenseStatus FromTier(FeatureTier tier, License lic) =>
        new(tier, Describe(tier, lic), ExpiresAt: lic.ExpiresAt, RawLicense: lic);

    /// <summary>"Production — buyer@example.com · lifetime updates · main PC".</summary>
    public static string Describe(FeatureTier tier, License lic)
    {
        var name = tier == FeatureTier.Production && lic.ExpiresAt is { } pass
            ? $"Production event pass (until {pass:d MMM yyyy})"
            : Pricing.TierName(tier);
        var parts = new List<string> { $"{name} — {lic.Email}" };
        if (lic.Tier == FeatureTier.Production && lic.ExpiresAt is null)
            parts.Add(lic.UpdatesUntil is { } u ? $"updates until {u:d MMM yyyy}" : "lifetime updates");
        if (lic.Seat is 1) parts.Add("main PC");
        else if (lic.Seat is 2) parts.Add("backup PC");
        return string.Join(" · ", parts);
    }

    public static LicenseStatus MasterFromSerial(string serial) =>
        new(FeatureTier.Master, "Master (owner)");

    public static LicenseStatus MasterFromFile(License lic) =>
        new(FeatureTier.Master, "Master (owner)", RawLicense: lic);
}
