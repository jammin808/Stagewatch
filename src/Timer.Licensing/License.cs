using System.Text.Json.Serialization;
using Stagewatch.Core;

namespace Stagewatch.Licensing;

/// <summary>
/// The plaintext license payload (before encryption + signing).
///
/// Wire format on disk (in %APPDATA%\Stagewatch\license.dat):
/// - Encrypted blob (AES-GCM with key derived from a per-build pepper + hardware ID)
/// - Inside: JSON of <see cref="License"/>
/// - Each license carries an ECDSA P-256 signature over its canonical JSON,
///   verified against the public key embedded in the app.
///
/// "Dual encryption" = AES-GCM at rest + ECDSA P-256 signature for integrity/authenticity.
/// See LICENSING.md for the threat model and the honest limits of this approach.
/// </summary>
public sealed class License
{
    [JsonPropertyName("v")]      public int Version { get; set; } = 1;
    [JsonPropertyName("id")]     public string LicenseId { get; set; } = "";
    [JsonPropertyName("tier")]   public FeatureTier Tier { get; set; }
    [JsonPropertyName("email")]  public string Email { get; set; } = "";
    [JsonPropertyName("hwid")]   public string HardwareId { get; set; } = "";
    [JsonPropertyName("issued")] public long IssuedAtUnix { get; set; }
    [JsonPropertyName("exp")]    public long? ExpiresAtUnix { get; set; }
    [JsonPropertyName("master")] public bool IsMaster { get; set; }

    // Added with the Production tier. Omitted from the JSON when unset, so licences issued before
    // they existed keep exactly the signed bytes they were issued with.

    /// <summary>
    /// Updates are included for builds released up to this time. Null = lifetime updates
    /// (early adopters, and Standard / Pro licences).
    /// </summary>
    [JsonPropertyName("upd")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? UpdatesUntilUnix { get; set; }

    /// <summary>Which machine of a multi-machine licence this file is for: 1 = main, 2 = backup (0 = single).</summary>
    [JsonPropertyName("seat")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int Seat { get; set; }

    [JsonPropertyName("sig")]    public string SignatureBase64 { get; set; } = "";

    public DateTime IssuedAt => DateTimeOffset.FromUnixTimeSeconds(IssuedAtUnix).UtcDateTime;
    public DateTime? ExpiresAt => ExpiresAtUnix is null ? null : DateTimeOffset.FromUnixTimeSeconds(ExpiresAtUnix.Value).UtcDateTime;
    public DateTime? UpdatesUntil => UpdatesUntilUnix is null ? null : DateTimeOffset.FromUnixTimeSeconds(UpdatesUntilUnix.Value).UtcDateTime;
}
