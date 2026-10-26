using System.Globalization;
using Stagewatch.Core;

namespace Stagewatch.Remote;

/// <summary>
/// How long this install's TLS certificate lives (Show Bus §10, §14's <c>cert.days</c>, <c>cert.backdate.hours</c> and
/// <c>cert.renew.days</c>). Its whole span — NotBefore to NotAfter, the backdating for a skewed clock included — is
/// <see cref="Span"/>, because Apple devices refuse a TLS server certificate valid for longer, even one the user chose to trust.
/// NotBefore is <see cref="Backdate"/> before it was made, less the fraction of a second X.509 does not keep, and NotAfter is
/// counted from NotBefore, so the span is exactly <see cref="Span"/> whatever the time it was made. At a start a certificate is kept
/// only while at least <see cref="RenewDays"/> days remain and its span is no more than <see cref="Span"/>: one made longer by an
/// older Stagewatch is replaced once, and the new one, being within the span, is kept until it nears its end. Pure.
/// </summary>
public static class CertificateLife
{
    /// <summary>The whole span, NotBefore to NotAfter: §14's <c>cert.days</c>.</summary>
    public static readonly TimeSpan Span = TimeSpan.FromDays(825);

    /// <summary>How far before its making a certificate's NotBefore lies, for a peer whose clock is behind: within <c>cert.backdate.hours</c>.</summary>
    public static readonly TimeSpan Backdate = TimeSpan.FromDays(1);

    /// <summary>A new certificate is made at a start when fewer days than this remain: §14's <c>cert.renew.days</c>.</summary>
    public const int RenewDays = 30;

    /// <summary>
    /// NotBefore and NotAfter for a certificate made at <paramref name="now"/>: whole seconds, exactly <see cref="Span"/> apart, and
    /// NotBefore no more than <see cref="Backdate"/> before <paramref name="now"/> — a fraction of a second is rounded up, never down.
    /// </summary>
    public static (DateTimeOffset NotBefore, DateTimeOffset NotAfter) Window(DateTimeOffset now)
    {
        var ticks = now.UtcTicks;
        var past = ticks % TimeSpan.TicksPerSecond;
        var notBefore = new DateTimeOffset(past == 0 ? ticks : ticks - past + TimeSpan.TicksPerSecond, TimeSpan.Zero) - Backdate;
        return (notBefore, notBefore + Span);
    }

    /// <summary>Whether a certificate valid from <paramref name="notBefore"/> to <paramref name="notAfter"/> is replaced at a start at <paramref name="now"/>.</summary>
    public static bool NeedsRenewal(DateTimeOffset notBefore, DateTimeOffset notAfter, DateTimeOffset now) => Why(notBefore, notAfter, now) is not null;

    /// <summary>
    /// Why a certificate valid from <paramref name="notBefore"/> to <paramref name="notAfter"/> is replaced at a start at
    /// <paramref name="now"/>, in words for the journal; null when it is kept. An over-long span is named first: replacing it is
    /// what every peer that pinned it sees as CERTIFICATE CHANGED once.
    /// </summary>
    public static string? Why(DateTimeOffset notBefore, DateTimeOffset notAfter, DateTimeOffset now)
    {
        var span = notAfter - notBefore;
        if (span > Span)
            return "it spanned " + Number(Math.Ceiling(span.TotalDays)) + " days, more than the " + Number(Span.TotalDays) + " Apple devices and Show Bus 1.3 allow";
        var left = notAfter - now;
        if (left >= TimeSpan.FromDays(RenewDays)) return null;
        if (left <= TimeSpan.Zero) return "it had ended";
        // Whole days only, so the count never reaches the limit it is said to be under.
        var days = Math.Floor(left.TotalDays);
        return (days < 1 ? "it had less than a day left" : "it had " + Number(days) + (days < 2 ? " day" : " days") + " left")
            + ", fewer than " + Number(RenewDays);
    }

    /// <summary>
    /// The show log's words for a kept certificate replaced because <paramref name="why"/> (<see cref="Why"/>'s words), the new one's
    /// fingerprint <paramref name="fingerprint"/> (<c>sha256:</c> and 64 hex, which is public): its first 8 hex grouped as
    /// <see cref="TlsPins.Shown"/> groups them, as Settings → Remote, a presenter PC's row and a pinned peer show them and as 13.8.3's
    /// Accept certificate asks them typed.
    /// </summary>
    public static string ReplacedWords(string why, string fingerprint)
        => "Certificate replaced: " + why + ". The new one's fingerprint starts " + TlsPins.Shown(fingerprint) + " …, as Settings → Remote "
           + "shows it (its tooltip has all of it): every app and presenter PC that pinned the old one says CERTIFICATE CHANGED once, "
           + "until its operator accepts the new one there";

    /// <summary>The desk's status line for the same replacement: shorter than <see cref="ReplacedWords"/>, the same 8 hex.</summary>
    public static string ReplacedLine(string why, string fingerprint)
        => "Certificate replaced (" + why + "): apps and presenter PCs that pinned the old one say CERTIFICATE CHANGED once — the new one "
           + "starts " + TlsPins.Shown(fingerprint) + " (Settings → Remote)";

    private static string Number(double whole) => ((long)whole).ToString(CultureInfo.InvariantCulture);
}
