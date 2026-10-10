using System.Security.Cryptography;
using System.Text;
using Stagewatch.Core;

namespace Stagewatch.Licensing;

/// <summary>
/// Tracks the 14-day trial. The trial start is anchored in three places to make
/// resetting the clock harder:
///
///   1. %APPDATA%\Stagewatch\trial.dat (encrypted with hwid)
///   2. HKCU\Software\Stagewatch\T (encrypted)
///   3. NTFS alternate data stream on the exe (best-effort)
///
/// Two of three must agree. Earliest discovered date wins (so deleting one
/// store doesn't extend the trial).
/// </summary>
public static class TrialTracker
{
    public static readonly TimeSpan TrialDuration = FeatureGate.TrialLength;

    private const string RegPath = @"Software\Stagewatch";
    private const string RegName = "T";

    public static DateTime EnsureStartedUtc()
    {
        var hwid = HardwareId.Get();
        var anchors = ReadAnchors(hwid);

        if (anchors.Count == 0)
        {
            var now = DateTime.UtcNow;
            WriteAllAnchors(now, hwid);
            return now;
        }

        // Earliest wins.
        var earliest = anchors.Min();

        // If only one source has a date, write it back to the others.
        if (anchors.Count < 3) WriteAllAnchors(earliest, hwid);
        return earliest;
    }

    public static TimeSpan Remaining()
    {
        var start = EnsureStartedUtc();
        var elapsed = DateTime.UtcNow - start;
        return TrialDuration - elapsed;
    }

    public static bool IsActive() => Remaining() > TimeSpan.Zero;

    private static List<DateTime> ReadAnchors(string hwid)
    {
        var list = new List<DateTime>();

        // 1. file
        try
        {
            var path = TrialFilePath();
            if (File.Exists(path))
            {
                var pt = CryptoEnvelope.Decrypt(File.ReadAllBytes(path), hwid);
                if (pt is not null && TryParseUnix(pt, out var t)) list.Add(t);
            }
        } catch { }

        // 2. registry
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RegPath);
            var data = key?.GetValue(RegName) as byte[];
            if (data is not null)
            {
                var pt = CryptoEnvelope.Decrypt(data, hwid);
                if (pt is not null && TryParseUnix(pt, out var t)) list.Add(t);
            }
        } catch { }

        // 3. ADS — best effort
        try
        {
            var exe = Environment.ProcessPath;
            if (exe is not null)
            {
                var ads = exe + ":sw_t";
                if (File.Exists(ads))
                {
                    var pt = CryptoEnvelope.Decrypt(File.ReadAllBytes(ads), hwid);
                    if (pt is not null && TryParseUnix(pt, out var t)) list.Add(t);
                }
            }
        } catch { }

        return list;
    }

    private static void WriteAllAnchors(DateTime utc, string hwid)
    {
        var unix = new DateTimeOffset(utc, TimeSpan.Zero).ToUnixTimeSeconds();
        var pt = Encoding.UTF8.GetBytes(unix.ToString());
        var blob = CryptoEnvelope.Encrypt(pt, hwid);

        // 1. file
        try
        {
            var path = TrialFilePath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, blob);
        } catch { }

        // 2. registry
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RegPath);
            key?.SetValue(RegName, blob, Microsoft.Win32.RegistryValueKind.Binary);
        } catch { }

        // 3. ADS
        try
        {
            var exe = Environment.ProcessPath;
            if (exe is not null)
                File.WriteAllBytes(exe + ":sw_t", blob);
        } catch { }
    }

    private static bool TryParseUnix(byte[] pt, out DateTime utc)
    {
        utc = default;
        var s = Encoding.UTF8.GetString(pt);
        if (!long.TryParse(s, out var unix)) return false;
        utc = DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime;
        return true;
    }

    private static string TrialFilePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "Stagewatch", "trial.dat");
    }
}
