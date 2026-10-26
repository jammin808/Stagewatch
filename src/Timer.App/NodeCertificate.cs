using System.IO;
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

    /// <summary>A fresh certificate for this machine as <see cref="CertificateMaker"/> makes every node's, named for <paramref name="name"/>.</summary>
    public static X509Certificate2 Create(string name, DateTimeOffset now) => CertificateMaker.Create("Stagewatch", name, now);
}
