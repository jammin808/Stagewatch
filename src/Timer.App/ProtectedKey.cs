namespace Stagewatch.App.ViewModels;

/// <summary>
/// Secrets the settings keep — API keys, playout link tokens, a presenter PC's PIN, an HTTP trigger's secret — are stored encrypted
/// for this Windows user (DPAPI), never in clear text. One that cannot be read here (another user's, another machine's, a damaged
/// one) is "" — no secret — never an exception. Plain data, so the suite runs it on Windows.
/// </summary>
public static class ProtectedKey
{
    private static readonly byte[] Entropy = "Stagewatch assistant key"u8.ToArray();

    public static string Protect(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return "";
        var bytes = System.Security.Cryptography.ProtectedData.Protect(System.Text.Encoding.UTF8.GetBytes(key.Trim()), Entropy,
            System.Security.Cryptography.DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(bytes);
    }

    public static string Unprotect(string stored)
    {
        if (string.IsNullOrWhiteSpace(stored)) return "";
        try
        {
            var bytes = System.Security.Cryptography.ProtectedData.Unprotect(Convert.FromBase64String(stored), Entropy,
                System.Security.Cryptography.DataProtectionScope.CurrentUser);
            return System.Text.Encoding.UTF8.GetString(bytes);
        }
        catch (Exception ex) when (ex is FormatException or System.Security.Cryptography.CryptographicException) { return ""; }
    }
}
