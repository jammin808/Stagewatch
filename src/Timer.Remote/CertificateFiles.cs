using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Stagewatch.Core;

namespace Stagewatch.Remote;

/// <summary>
/// What a read of this install's certificate came to: the certificate served this run (null: none, so HTTPS is off until a start
/// that has one), why a kept one was replaced (null when none was), and what went wrong (null when nothing did). Every word is
/// public: a fingerprint's first hex at most, never a password or a key.
/// </summary>
public sealed record CertificateRead(X509Certificate2? Certificate, string? Replaced, string? Problem)
{
    /// <summary>The show log's words for this read; null when there is nothing to say (kept as it was, or a first one made and saved).</summary>
    public string? Note => Replaced is not null && Certificate is { } made
        ? CertificateLife.ReplacedWords(Replaced, TlsPins.Fingerprint(made.RawData)) + (Problem is null ? "" : ". " + Problem)
        : Problem;

    /// <summary>The desk's status line for this read, shorter than <see cref="Note"/>: what went wrong first; null when Note is.</summary>
    public string? Line => Problem ?? (Replaced is not null && Certificate is { } made
        ? CertificateLife.ReplacedLine(Replaced, TlsPins.Fingerprint(made.RawData))
        : null);
}

/// <summary>
/// This install's certificate on disk (Show Bus §10): <c>node.pfx</c>, a PKCS#12, beside <c>node.key</c>, its random password
/// sealed by <c>protect</c> (DPAPI for this Windows user on the desk). It keeps, replaces or makes one as <see cref="CertificateLife"/>
/// says, and never leaves the node without a certificate it could have, nor its peers with a pin it could have kept:
/// <list type="bullet">
/// <item>a kept certificate is replaced only once the new one is on the disk, and a replacement keeps the stored password, so it is
/// one atomic swap of <c>node.pfx</c> and never a torn pair; a new one that cannot be made or saved leaves a kept certificate that
/// has not ended serving, said, and the next start tries again;</item>
/// <item>a stored pair that cannot be opened (a pair torn by an older writer, another user's seal, damage) is replaced as a first start
/// would, never left to fail at every start;</item>
/// <item>a new certificate that cannot be saved still serves this run, said;</item>
/// <item>files that cannot be read at all (locked, denied) are never overwritten: no certificate this run, said, and the next start
/// reads them again.</item>
/// </list>
/// Every outcome is a <see cref="CertificateRead"/>. Time, reading and saving are seams, so the suite runs this on every platform.
/// </summary>
public sealed class CertificateFiles(string folder, Func<byte[], byte[]> protect, Func<byte[], byte[]> unprotect, Func<DateTimeOffset, X509Certificate2> make)
{
    /// <summary>Now, for keeping and making.</summary>
    public Func<DateTimeOffset> Clock { get; init; } = () => DateTimeOffset.UtcNow;

    /// <summary>Reads a whole file.</summary>
    public Func<string, byte[]> Read { get; init; } = File.ReadAllBytes;

    /// <summary>Replaces a whole file atomically: the old one or the new one, never half of one.</summary>
    public Action<string, byte[]> Save { get; init; } = AtomicFile.Write;

    public string PfxPath { get; } = Path.Combine(folder, "node.pfx");
    public string KeyPath { get; } = Path.Combine(folder, "node.key");

    public CertificateRead Load()
    {
        var now = Clock();
        string? why = null;
        string? password = null;   // the stored one, when its seal opens: a replacement keeps it, so only node.pfx is swapped
        X509Certificate2? kept = null;   // a stored certificate to be replaced that may still serve while it cannot be
        if (File.Exists(PfxPath) && File.Exists(KeyPath))
        {
            byte[] sealedKey, pfx;
            try { (sealedKey, pfx) = (Read(KeyPath), Read(PfxPath)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return new(null, null, "No certificate this run: it could not be read (" + ex.Message + "). HTTPS is off; the next start reads it again");
            }
            try
            {
                password = Encoding.UTF8.GetString(unprotect(sealedKey));
                var loaded = X509CertificateLoader.LoadPkcs12(pfx, password, X509KeyStorageFlags.UserKeySet);
                // X.509 dates come back as local times; their UTC instants are compared, never the wall clock's.
                var (notBefore, notAfter) = (Utc(loaded.NotBefore), Utc(loaded.NotAfter));
                why = loaded.HasPrivateKey ? CertificateLife.Why(notBefore, notAfter, now) : "it had no private key";
                if (why is null) return new(loaded, null, null);
                if (loaded.HasPrivateKey && notAfter > now) kept = loaded;
                else loaded.Dispose();
            }
            catch (CryptographicException ex)
            {
                why = "the stored one could not be opened (" + ex.Message + ")";
            }
        }

        X509Certificate2 fresh;
        byte[] freshPfx;
        var reuse = password is not null;
        var pw = password ?? Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
        try
        {
            using (var made = make(now))
                freshPfx = made.Export(X509ContentType.Pkcs12, pw);
            // Loaded back from the PKCS#12, not used as made: Windows' TLS can't use an ephemeral key.
            fresh = X509CertificateLoader.LoadPkcs12(freshPfx, pw, X509KeyStorageFlags.UserKeySet);
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException or SocketException)
        {
            if (kept is not null) return new(kept, null, KeptWords(why!, "a new one could not be made (" + ex.Message + ")"));
            return new(null, null, "No certificate this run: " + (why is null ? "" : why + ", and ") + "one could not be made (" + ex.Message
                                   + "). HTTPS is off until a start can make one");
        }

        try
        {
            Directory.CreateDirectory(folder);
            if (!reuse) Save(KeyPath, protect(Encoding.UTF8.GetBytes(pw)));
            Save(PfxPath, freshPfx);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            if (kept is not null)
            {
                fresh.Dispose();
                return new(kept, null, KeptWords(why!, "the new one could not be saved (" + ex.Message + ")"));
            }
            return new(fresh, why, "Certificate made for this run only: it could not be saved (" + ex.Message + "). The next start makes another, "
                                   + "which every app and presenter PC that pins this one sees as CERTIFICATE CHANGED");
        }
        kept?.Dispose();
        return new(fresh, why, null);
    }

    private static DateTimeOffset Utc(DateTime local) => new(local.ToUniversalTime(), TimeSpan.Zero);

    private static string KeptWords(string why, string what)
        => "Certificate kept, not replaced (" + why + "): " + what + ". Every peer keeps the one it pinned; the next start tries again";
}
