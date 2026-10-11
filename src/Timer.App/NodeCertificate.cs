using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Stagewatch.App;

/// <summary>
/// This install's TLS certificate (Show Bus §10): self-signed, ECDSA P-256, 825 days (Apple devices refuse longer), made on
/// first use — and again 30 days before it ends — and kept
/// beside the settings as a PKCS#12 file whose random password is protected for this Windows user (DPAPI). Never copied
/// between machines; its fingerprint is what other apps pin and what a screen checks before trusting it.
/// </summary>
public static class NodeCertificate
{
    private static readonly byte[] Entropy = "Stagewatch node certificate"u8.ToArray();

    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Stagewatch", "tls");

    /// <summary>The certificate (made now if there isn't one), or null when it can't be made or read — the server then stays HTTP only.</summary>
    public static X509Certificate2? LoadOrCreate(string name, string? folder = null)
    {
        folder ??= Folder;
        var pfxPath = Path.Combine(folder, "node.pfx");
        var keyPath = Path.Combine(folder, "node.key");
        try
        {
            if (File.Exists(pfxPath) && File.Exists(keyPath))
            {
                var password = System.Text.Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(keyPath), Entropy, DataProtectionScope.CurrentUser));
                var loaded = X509CertificateLoader.LoadPkcs12(File.ReadAllBytes(pfxPath), password, X509KeyStorageFlags.UserKeySet);
                if (loaded.HasPrivateKey && loaded.NotAfter > DateTime.Now.AddDays(30)) return loaded;
                loaded.Dispose();
            }
            Directory.CreateDirectory(folder);
            var pw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
            var pfx = Create(name).Export(X509ContentType.Pkcs12, pw);
            File.WriteAllBytes(keyPath, ProtectedData.Protect(System.Text.Encoding.UTF8.GetBytes(pw), Entropy, DataProtectionScope.CurrentUser));
            File.WriteAllBytes(pfxPath, pfx);
            // Loaded back from the PKCS#12, not used as created: Windows' TLS can't use an ephemeral key.
            return X509CertificateLoader.LoadPkcs12(pfx, pw, X509KeyStorageFlags.UserKeySet);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            return null;
        }
    }

    /// <summary>A fresh self-signed certificate for this machine's names and addresses.</summary>
    public static X509Certificate2 Create(string name)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var clean = new string((name.Length > 0 ? name : Environment.MachineName).Where(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_').ToArray()).Trim();
        var req = new CertificateRequest($"CN=Stagewatch {(clean.Length > 0 ? clean : "node")}", key, HashAlgorithmName.SHA256);
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("localhost");
        san.AddDnsName(Dns.GetHostName());
        san.AddIpAddress(IPAddress.Loopback);
        foreach (var ip in LanAddresses()) san.AddIpAddress(ip);
        req.CertificateExtensions.Add(san.Build());
        req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: true));
        req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, critical: false));   // server auth
        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, critical: true));
        return req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(825));
    }

    private static IEnumerable<IPAddress> LanAddresses()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Select(a => a.Address)
                .Where(a => a.AddressFamily == AddressFamily.InterNetwork)
                .Distinct().Take(8).ToList();
        }
        catch (NetworkInformationException) { return Array.Empty<IPAddress>(); }
    }
}
