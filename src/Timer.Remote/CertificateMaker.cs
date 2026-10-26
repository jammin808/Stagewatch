using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Stagewatch.Remote;

/// <summary>
/// A node's TLS certificate as Stagewatch makes one (Show Bus §10): self-signed, ECDSA P-256, a server's and never an authority's,
/// naming localhost, this machine's name, loopback and its IPv4 addresses, its span <see cref="CertificateLife.Window"/>'s from the
/// moment of making. The desk's certificate and the rig's (<c>--tls</c>) are both made here, so the rig serves the app's kind;
/// <see cref="CertificateFiles"/> keeps, replaces and saves either.
/// </summary>
public static class CertificateMaker
{
    /// <summary>The most of this machine's IPv4 addresses a certificate names beside loopback.</summary>
    public const int MaxAddresses = 8;

    /// <summary>
    /// A fresh certificate whose subject is <c>CN=</c><paramref name="prefix"/> and <paramref name="name"/>'s letters, digits,
    /// spaces, '-' and '_' (this machine's name when <paramref name="name"/> is empty, "node" when nothing is left of either), its
    /// span <see cref="CertificateLife.Window"/>'s from <paramref name="now"/>.
    /// </summary>
    public static X509Certificate2 Create(string prefix, string name, DateTimeOffset now)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var clean = new string((name.Length > 0 ? name : Environment.MachineName).Where(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_').ToArray()).Trim();
        var req = new CertificateRequest($"CN={prefix} {(clean.Length > 0 ? clean : "node")}", key, HashAlgorithmName.SHA256);
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

    /// <summary>This machine's IPv4 addresses on interfaces that are up, loopback left out, at most <see cref="MaxAddresses"/>.</summary>
    private static List<IPAddress> LanAddresses()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Select(a => a.Address)
                .Where(a => a.AddressFamily == AddressFamily.InterNetwork)
                .Distinct().Take(MaxAddresses).ToList();
        }
        catch (NetworkInformationException) { return new List<IPAddress>(); }
    }
}
