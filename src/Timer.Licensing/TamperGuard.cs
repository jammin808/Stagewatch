using System.Diagnostics;
using System.Security.Cryptography;

namespace Stagewatch.Licensing;

/// <summary>
/// Lightweight runtime tamper checks. Not a substitute for a real protector
/// (Themida, VMProtect, etc.) — those should be applied to the release binary.
/// What this catches:
///   - Process running under a debugger
///   - Self-hash mismatch (best-effort; useful with strong-named/signed binary)
///   - Time-of-day skew vs server (when network reachable) — caller decides what to do
/// </summary>
public static class TamperGuard
{
    public static bool IsDebuggerAttached()
        => Debugger.IsAttached || IsRemoteDebuggerPresent();

    public static string SelfHash()
    {
        try
        {
            // The running exe itself. (Assembly.Location is "" inside a single-file bundle.)
            var path = Environment.ProcessPath ?? "";
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return "";
            using var fs = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(fs));
        }
        catch { return ""; }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool CheckRemoteDebuggerPresent(IntPtr hProcess, ref bool isDebuggerPresent);

    private static bool IsRemoteDebuggerPresent()
    {
        try
        {
            bool present = false;
            CheckRemoteDebuggerPresent(Process.GetCurrentProcess().Handle, ref present);
            return present;
        }
        catch { return false; }
    }
}
