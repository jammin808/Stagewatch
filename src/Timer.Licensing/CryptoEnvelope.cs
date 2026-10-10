using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Stagewatch.Licensing;

/// <summary>
/// Two layers:
///   Layer 1: ECDSA P-256 / SHA-256 signature over the canonical license JSON. Only the
///            public key is in the binary; the private key lives with the owner (KeyGen,
///            %USERPROFILE%\.stagewatch\license-signing-key.pem) and never in this repo.
///   Layer 2: AES-GCM at rest, key = HKDF(BuildPepper || HardwareId).
///            This makes a stolen license file useless on another machine and
///            makes naive tampering visible.
///
/// Honest limits: see LICENSING.md. Signatures stop forged or edited licenses. They do not
/// stop a determined reverse-engineer from patching the binary to skip the check — that is
/// what the commercial protector applied to release builds is for.
/// </summary>
public static class CryptoEnvelope
{
    /// <summary>
    /// Per-build pepper for the at-rest layer. Not a secret in any strong sense (it ships in
    /// the binary); it only binds encrypted files to this product + machine.
    /// </summary>
    public static readonly byte[] BuildPepper = Convert.FromHexString(
        "0FCA4223B6A343DFB0AA52A77FAD0CF4E198F499F818CA9000B6DC3E25A13DC1");

    /// <summary>
    /// License-signing public key (P-256, SubjectPublicKeyInfo, base64). The matching private
    /// key is the owner's alone: lose it and no new licenses can be issued for this build.
    /// </summary>
    public const string PublicKeySpki =
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEN5UqfQj5qchb+tt6917fOxGFLoJ2KANN8fQXzYGpUCDJW6jgj9MN3TdTvU79yXt7gSxbjeAMiFRBBXyU5h3yZA==";

    private static readonly Lazy<ECDsa> Verifier = new(() =>
    {
        var ecdsa = ECDsa.Create();
        ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(PublicKeySpki), out _);
        return ecdsa;
    });

    /// <summary>True if <paramref name="signature"/> (IEEE P1363, 64 bytes) was made by the owner's key.</summary>
    public static bool VerifyOwnerSignature(byte[] message, byte[] signature)
    {
        if (signature.Length != 64) return false;
        try
        {
            lock (Verifier) return Verifier.Value.VerifyData(message, signature, HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (CryptographicException) { return false; }
    }

    private static readonly JsonSerializerOptions CanonicalJson = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = null
    };

    /// <summary>
    /// Canonical JSON for signing — sig field zeroed.
    /// Both KeyGen and the verifier must produce byte-identical output.
    /// </summary>
    public static byte[] CanonicalBytesForSigning(License l)
    {
        var copy = new License
        {
            Version = l.Version,
            LicenseId = l.LicenseId,
            Tier = l.Tier,
            Email = l.Email,
            HardwareId = l.HardwareId,
            IssuedAtUnix = l.IssuedAtUnix,
            ExpiresAtUnix = l.ExpiresAtUnix,
            IsMaster = l.IsMaster,
            UpdatesUntilUnix = l.UpdatesUntilUnix,
            Seat = l.Seat,
            SignatureBase64 = ""
        };
        return JsonSerializer.SerializeToUtf8Bytes(copy, CanonicalJson);
    }

    public static byte[] Encrypt(byte[] plaintext, string hardwareId)
    {
        var key = DeriveKey(hardwareId);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];

        using var gcm = new AesGcm(key, 16);
        gcm.Encrypt(nonce, plaintext, ciphertext, tag);

        // Layout: [magic 4][nonce 12][tag 16][ciphertext]
        var output = new byte[4 + 12 + 16 + ciphertext.Length];
        output[0] = (byte)'S'; output[1] = (byte)'W'; output[2] = (byte)'L'; output[3] = 1;
        Buffer.BlockCopy(nonce, 0, output, 4, 12);
        Buffer.BlockCopy(tag, 0, output, 16, 16);
        Buffer.BlockCopy(ciphertext, 0, output, 32, ciphertext.Length);
        return output;
    }

    public static byte[]? Decrypt(byte[] envelope, string hardwareId)
    {
        if (envelope.Length < 32) return null;
        if (envelope[0] != 'S' || envelope[1] != 'W' || envelope[2] != 'L' || envelope[3] != 1) return null;

        var nonce = new byte[12];
        var tag = new byte[16];
        Buffer.BlockCopy(envelope, 4, nonce, 0, 12);
        Buffer.BlockCopy(envelope, 16, tag, 0, 16);
        var ciphertext = new byte[envelope.Length - 32];
        Buffer.BlockCopy(envelope, 32, ciphertext, 0, ciphertext.Length);

        try
        {
            var key = DeriveKey(hardwareId);
            var plaintext = new byte[ciphertext.Length];
            using var gcm = new AesGcm(key, 16);
            gcm.Decrypt(nonce, ciphertext, tag, plaintext);
            return plaintext;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    private static byte[] DeriveKey(string hardwareId)
    {
        // HKDF-SHA256(BuildPepper, salt=HWID, info="stagewatch-license")
        var salt = Encoding.UTF8.GetBytes(hardwareId);
        var info = Encoding.UTF8.GetBytes("stagewatch-license-v1");
        return HKDF.DeriveKey(HashAlgorithmName.SHA256, BuildPepper, 32, salt, info);
    }

    /// <summary>Verify the owner's signature on a license.</summary>
    public static bool VerifySignature(License l)
    {
        try
        {
            return VerifyOwnerSignature(CanonicalBytesForSigning(l), Convert.FromBase64String(l.SignatureBase64));
        }
        catch (FormatException) { return false; }
    }
}
