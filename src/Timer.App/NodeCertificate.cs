using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Stagewatch.Remote;

namespace Stagewatch.App;

/// <summary>
/// This install's TLS certificate (Show Bus §10): self-signed, ECDSA P-256, living as <see cref="CertificateLife"/> says — 825
/// days in all, a day's backdating included (Apple devices refuse longer) — made on first use and again at a start when
/// <see cref="CertificateLife.NeedsRenewal"/> says so (fewer than 30 days left, or a span longer than 825 days). It is kept
/// beside the settings by <see cref="CertificateFiles"/> as a PKCS#12 file whose random password is sealed for this Windows user
/// (DPAPI); this class gives it only the seal and the making. Never copied between machines; its fingerprint is what other apps pin
/// and what a screen checks before trusting it, so each new one is a CERTIFICATE CHANGED for every peer that pinned the one before.
/// </summary>
public static class NodeCertificate
{
    private static readonly byte[] Entropy = "Stagewatch node certificate"u8.ToArray();

    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Stagewatch", "tls");

    /// <summary>
    /// The certificate kept, replaced or made now, with why a kept one was replaced and what went wrong (<see cref="CertificateFiles.Load"/>):
    /// a null certificate leaves the server HTTP only, and says why.
    /// </summary>
    public static CertificateRead LoadOrCreate(string name, string? folder = null)
        => new CertificateFiles(folder ?? Folder,
            secret => ProtectedData.Protect(secret, Entropy, DataProtectionScope.CurrentUser),
            sealedSecret => ProtectedData.Unprotect(sealedSecret, Entropy, DataProtectionScope.CurrentUser),
            now => Create(name, now)).Load();

    /// <summary>A fresh self-signed certificate for this machine's names and addresses, its span <see cref="CertificateLife.Window"/>'s from <paramref name="now"/>.</summary>
    public static X509Certificate2 Create(string name, DateTimeOffset now)
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
        var (notBefore, notAfter) = CertificateLife.Window(now);
        return req.CreateSelfSigned(notBefore, notAfter);
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
