using System.Management;
using System.Security.Cryptography;
using System.Text;

namespace Stagewatch.Licensing;

/// <summary>
/// Stable per-machine identifier. Used to bind licenses to hardware.
///
/// v2 = SHA-256 of (Windows MachineGuid | motherboard UUID). Deliberately NOT network adapters:
/// "which adapters are up" changes when a laptop docks, plugs into venue Ethernet, starts a VPN or
/// drops Wi-Fi — and a licence that silently stops matching mid-show would watermark the stage.
/// Both inputs survive reboots, docking, network changes and app reinstalls; they change only on a
/// Windows reinstall or a motherboard swap (→ licence transfer, see LICENSING.md).
/// </summary>
public static class HardwareId
{
    private static string? _cached;

    public static string Get()
    {
        if (_cached is not null) return _cached;

        var machineGuid = "";
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            machineGuid = key?.GetValue("MachineGuid")?.ToString() ?? "";
        }
        catch { }

        // Last good board UUID is remembered, so a WMI hiccup at startup can't change the ID.
        // (Copying this file to another PC gains nothing: its MachineGuid still differs.)
        var cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Stagewatch", "hw.cache");
        var boardUuid = ReadBoardUuid();
        try
        {
            if (boardUuid is null)
                boardUuid = File.Exists(cache) ? File.ReadAllText(cache).Trim() : "";
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(cache)!);
                if (!File.Exists(cache) || File.ReadAllText(cache).Trim() != boardUuid) File.WriteAllText(cache, boardUuid);
            }
        }
        catch (IOException) { boardUuid ??= ""; }
        catch (UnauthorizedAccessException) { boardUuid ??= ""; }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes("hwid-v2|" + machineGuid + "|" + boardUuid));
        _cached = Convert.ToHexString(bytes)[..32];
        return _cached;
    }

    /// <summary>
    /// WMI can be slow or briefly unavailable right after boot; an empty read would change the ID,
    /// so retry a few times. Null = couldn't read (caller falls back to the cached value).
    /// Placeholder UUIDs some OEMs ship are normalised to "".
    /// </summary>
    private static string? ReadBoardUuid()
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                using var s = new ManagementObjectSearcher("SELECT UUID FROM Win32_ComputerSystemProduct");
                foreach (var mo in s.Get())
                {
                    var uuid = mo["UUID"]?.ToString()?.Trim().ToUpperInvariant() ?? "";
                    if (uuid.Length > 0 && uuid != "FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF" && uuid != "00000000-0000-0000-0000-000000000000")
                        return uuid;
                    return ""; // a real but useless value: stable, so use "" consistently
                }
            }
            catch (ManagementException) { }
            catch (System.Runtime.InteropServices.COMException) { }
            #pragma warning disable RS0030 // a short retry while WMI starts, once at licence check
            Thread.Sleep(200);
            #pragma warning restore RS0030
        }
        return null;
    }
}
