using System.Security.Cryptography;
using System.Text.Json;
using Stagewatch.Core;
using Stagewatch.Licensing;

// Owner-only utility. Never ship it, never commit the private key.
//   dotnet run --project tools/KeyGen -- <command> [args]
//
// Commands:
//   keypair [--force]        Create the license-signing key pair (once). The private key is saved to
//                            %USERPROFILE%\.stagewatch\license-signing-key.pem (or $STAGEWATCH_SIGNING_KEY);
//                            the printed public key goes into CryptoEnvelope.PublicKeySpki.
//   public-key               Print the public key for the existing private key.
//   master-serial [id]       Issue an owner master serial (works on any machine running this build).
//   license <standard|pro|production> <hwid>[,<hwid2>] <email> [days] [--updates lifetime|<years>|<yyyy-mm-dd>]
//                            Issue machine-bound license file(s). HWID: customer's Settings → About.
//                            Production covers 2 machines (main + backup): give both HWIDs, comma separated,
//                            and one file is written per machine. [days] makes it expire (event pass: 30).
//                            --updates: Production update entitlement. Default "lifetime" (early adopters);
//                            later sales use 1 (year); renewals use a date, e.g. --updates 2028-10-09.
//   event-pass <hwid> <email>
//                            30-day Production event pass (same as: license production <hwid> <email> 30).
//   verify <license.dat>     Decrypt and check a license file (set HWID env var).
//   pepper                   Print a fresh 32-byte build pepper.

if (args.Length == 0) { PrintHelp(); return 0; }

try
{
    switch (args[0])
    {
        case "keypair":
            return CreateKeyPair(args.Contains("--force"));
        case "public-key":
            using (var key = LoadPrivateKey()) Console.WriteLine(Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()));
            return 0;
        case "master-serial":
            return IssueMasterSerial(args.Length >= 2 ? args[1] : "OWNER-" + DateTime.UtcNow.ToString("yyyyMMdd"));
        case "license" when args.Length >= 4:
        {
            var rest = args.Skip(4).ToList();
            var updates = Option(rest, "--updates");
            int? days = rest.FirstOrDefault(a => !a.StartsWith("--")) is { } d ? int.Parse(d) : null;
            return IssueLicense(args[1], args[2], args[3], days, updates);
        }
        case "event-pass" when args.Length >= 3:
            return IssueLicense("production", args[1], args[2], Pricing.EventPassDays, "lifetime");
        case "verify" when args.Length >= 2:
            return VerifyFile(args[1]);
        case "pepper":
            Console.WriteLine(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
            return 0;
        default:
            PrintHelp();
            return 1;
    }
}
catch (Exception ex) when (ex is IOException or CryptographicException or ArgumentException or FormatException)
{
    Console.Error.WriteLine("Error: " + ex.Message);
    return 2;
}

static string KeyPath() =>
    Environment.GetEnvironmentVariable("STAGEWATCH_SIGNING_KEY") is { Length: > 0 } p
        ? p
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".stagewatch", "license-signing-key.pem");

static ECDsa LoadPrivateKey()
{
    var path = KeyPath();
    if (!File.Exists(path))
        throw new IOException($"No signing key at {path}. Run 'keypair' first (or set STAGEWATCH_SIGNING_KEY).");
    var key = ECDsa.Create();
    key.ImportFromPem(File.ReadAllText(path));
    // Refuse to sign with a key the shipped build won't accept.
    var pub = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
    if (pub != CryptoEnvelope.PublicKeySpki)
        throw new CryptographicException("This private key does not match CryptoEnvelope.PublicKeySpki in this build.");
    return key;
}

static int CreateKeyPair(bool force)
{
    var path = KeyPath();
    if (File.Exists(path) && !force)
    {
        Console.Error.WriteLine($"A key already exists at {path}. Replacing it invalidates every license and master serial");
        Console.Error.WriteLine("issued with it. Re-run with --force only if that is really what you want.");
        return 3;
    }
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    File.WriteAllText(path, key.ExportPkcs8PrivateKeyPem());
    Console.WriteLine($"Private key written to {path}");
    Console.WriteLine("BACK IT UP somewhere safe and offline. Without it you cannot issue licenses for builds that trust it.");
    Console.WriteLine();
    Console.WriteLine("Public key (paste into CryptoEnvelope.PublicKeySpki):");
    Console.WriteLine(Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()));
    return 0;
}

static byte[] Sign(ECDsa key, byte[] message) =>
    key.SignData(message, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

static int IssueMasterSerial(string id)
{
    if (id.Contains('.') || id.Length is < 1 or > 64) throw new ArgumentException("id must be 1-64 chars without '.'");
    using var key = LoadPrivateKey();
    var serial = LicenseManager.MasterSerialPrefix + id + "." + LicenseManager.Base64UrlEncode(Sign(key, LicenseManager.MasterMessage(id)));
    if (!LicenseManager.IsValidMasterSerial(serial)) throw new CryptographicException("Self-check failed: serial does not verify.");
    Console.WriteLine("Master serial (keep private — it unlocks everything on any PC):");
    Console.WriteLine("  " + serial);
    Console.WriteLine();
    Console.WriteLine("Paste it in Stagewatch → Help → Enter master serial, or set env var STAGEWATCH_MASTER.");
    return 0;
}

static string? Option(List<string> args, string name)
{
    var i = args.IndexOf(name);
    if (i < 0) return null;
    if (i + 1 >= args.Count) throw new ArgumentException($"{name} needs a value");
    var v = args[i + 1];
    args.RemoveRange(i, 2);
    return v;
}

/// <summary>"lifetime" → null; "1" → one year from now; "2028-10-09" → that date (end of day UTC).</summary>
static long? ParseUpdates(string? value, FeatureTier tier)
{
    if (tier != FeatureTier.Production)
    {
        if (value is not null && value != "lifetime") throw new ArgumentException("--updates applies to Production licences only");
        return null;
    }
    if (value is null || value.Equals("lifetime", StringComparison.OrdinalIgnoreCase)) return null;
    if (int.TryParse(value, out var years) && years is > 0 and <= 10)
        return DateTimeOffset.UtcNow.AddYears(years).ToUnixTimeSeconds();
    if (DateTime.TryParseExact(value, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var date))
        return new DateTimeOffset(date.AddDays(1).AddSeconds(-1), TimeSpan.Zero).ToUnixTimeSeconds();
    throw new ArgumentException("--updates takes lifetime, a number of years (1-10) or a date yyyy-mm-dd");
}

static int IssueLicense(string tierName, string hwids, string email, int? days, string? updates)
{
    var tier = tierName.ToLowerInvariant() switch
    {
        "standard" => FeatureTier.Standard,
        "pro" => FeatureTier.Pro,
        "production" => FeatureTier.Production,
        _ => throw new ArgumentException("tier must be standard, pro or production")
    };

    var machines = hwids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    var allowed = tier == FeatureTier.Production ? Pricing.ProductionMachines : 1;
    if (machines.Length == 0 || machines.Length > allowed)
        throw new ArgumentException(tier == FeatureTier.Production
            ? $"Production covers up to {allowed} machines (main + backup): give 1 or 2 hardware IDs, comma separated"
            : $"{tier} covers one machine: give one hardware ID");
    if (machines.Distinct().Count() != machines.Length) throw new ArgumentException("the hardware IDs must differ");

    // One licence id for the purchase; one signed, machine-encrypted file per machine.
    var licenseId = "L-" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
    var issued = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    long? expires = days is null ? null : DateTimeOffset.UtcNow.AddDays(days.Value).ToUnixTimeSeconds();
    var updatesUntil = ParseUpdates(updates, tier);

    using var key = LoadPrivateKey();
    for (int i = 0; i < machines.Length; i++)
    {
        var lic = new License
        {
            Version = 1,
            LicenseId = licenseId,
            Tier = tier,
            Email = email,
            HardwareId = machines[i],
            IssuedAtUnix = issued,
            ExpiresAtUnix = expires,
            IsMaster = false,
            UpdatesUntilUnix = updatesUntil,
            Seat = machines.Length > 1 ? i + 1 : 0
        };
        lic.SignatureBase64 = Convert.ToBase64String(Sign(key, CryptoEnvelope.CanonicalBytesForSigning(lic)));
        if (!CryptoEnvelope.VerifySignature(lic)) throw new CryptographicException("Self-check failed: license does not verify.");

        var blob = CryptoEnvelope.Encrypt(JsonSerializer.SerializeToUtf8Bytes(lic), lic.HardwareId);
        var suffix = lic.Seat switch { 1 => "-main", 2 => "-backup", _ => "" };
        var outPath = $"license-{licenseId}{suffix}.dat";
        File.WriteAllBytes(outPath, blob);
        Console.WriteLine($"Wrote {outPath}  ({LicenseStatus.Describe(tier, lic)})");
    }
    Console.WriteLine($"Tier: {tier}, Email: {email}, Expires: {(expires is null ? "never" : DateTimeOffset.FromUnixTimeSeconds(expires.Value).ToString("u"))}, " +
                      $"Updates: {(updatesUntil is null ? "lifetime" : "until " + DateTimeOffset.FromUnixTimeSeconds(updatesUntil.Value).ToString("yyyy-MM-dd"))}");
    return 0;
}

static int VerifyFile(string path)
{
    var hwid = Environment.GetEnvironmentVariable("HWID") ?? "";
    if (string.IsNullOrEmpty(hwid))
    {
        Console.Error.WriteLine("Set the HWID env var to the target machine's hardware ID before verify.");
        return 2;
    }
    var pt = CryptoEnvelope.Decrypt(File.ReadAllBytes(path), hwid);
    if (pt is null) { Console.WriteLine("DECRYPT FAILED — wrong HWID or a different build pepper"); return 1; }
    var lic = JsonSerializer.Deserialize<License>(pt)!;
    Console.WriteLine($"Tier: {lic.Tier}");
    Console.WriteLine($"HWID: {lic.HardwareId}");
    Console.WriteLine($"Email: {lic.Email}");
    Console.WriteLine($"Issued: {lic.IssuedAt:u}");
    Console.WriteLine($"Expires: {lic.ExpiresAt?.ToString("u") ?? "never"}");
    Console.WriteLine($"Updates: {(lic.UpdatesUntil is { } u ? "until " + u.ToString("yyyy-MM-dd") : "lifetime")}");
    Console.WriteLine($"Seat: {lic.Seat switch { 1 => "main", 2 => "backup", _ => "single machine" }}");
    var ok = CryptoEnvelope.VerifySignature(lic);
    Console.WriteLine($"Signature OK: {ok}");
    return ok ? 0 : 1;
}

static void PrintHelp()
{
    Console.WriteLine("stagewatch-keygen keypair [--force]");
    Console.WriteLine("stagewatch-keygen public-key");
    Console.WriteLine("stagewatch-keygen master-serial [id]");
    Console.WriteLine("stagewatch-keygen license <standard|pro|production> <hwid>[,<hwid2>] <email> [days] [--updates lifetime|<years>|<yyyy-mm-dd>]");
    Console.WriteLine("stagewatch-keygen event-pass <hwid> <email>          (30-day Production pass)");
    Console.WriteLine("stagewatch-keygen verify <license.dat>   (set HWID env var)");
    Console.WriteLine("stagewatch-keygen pepper");
}
