using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Stagewatch.Core;
using Stagewatch.Integrations;
using Stagewatch.Show;

namespace Stagewatch.App.ViewModels;

/// <summary>
/// The playout links — Patterns desks and PlayMap nodes — as the desk runs them: each link Settings switches on (under the licence)
/// runs on its own, its clip on air may feed the VT countdown beside vMix, and <c>playout</c> and <c>patterns</c> lines reach them
/// through <see cref="PlayoutDirector"/>, the one executor the rig runs too. The show thread never touches a socket: it asks, the link's
/// lane writes, and the peer's own answer comes back on the status line, in the show log and in the sender's receipt.
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>The playout links running now, by name (the supervisor starts and retires each).</summary>
    private readonly PlayoutLinks _playout = new();

    /// <summary>The clips' ends and the running order's one move per item execution (owner thread).</summary>
    private readonly PlayoutClipEnds _clipEnds = new();

    /// <summary>The link names the last reconcile pass started or kept: one that leaves Settings is retired by name.</summary>
    private HashSet<string> _playoutNames = new(StringComparer.Ordinal);

    /// <summary>The link that fed the VT on the last look (owner thread): a clip that just ended may have stopped feeding it.</summary>
    private string? _vtWas;

    private PlayoutDirector? _director;

    /// <summary>
    /// The executor of <c>playout</c> and <c>patterns</c> lines: Settings' links, the licence, the lead; its rows and status lines are
    /// taken onto the owner thread, each row at the link's own time and against the item its command was asked for.
    /// </summary>
    private PlayoutDirector Director => _director ??= new PlayoutDirector(_playout, () => Settings.Integrations.PlayoutLinks,
        () => FeatureGate.AllowsPlayout(_license.Tier), () => Settings.Integrations.PlayoutLeadMs,
        (at, mark, row) => _ui.BeginInvoke(() => _journal.Add(at.UtcDateTime, JournalKind.Playout, mark, row)),
        line => _ui.BeginInvoke(() => StatusLine = line), TimeProvider.System, () => ShowClock);

    /// <summary>
    /// The amber advice before the next item to start (the item loaded and not started, else the one after it): each of its playout
    /// cues that would be skipped, and why (<see cref="PlayoutDirector.CueAdvice"/>).
    /// </summary>
    private IReadOnlyList<Advice> CueAdvice()
    {
        var items = Settings.Rundown.Items;
        var next = !_show.RundownActive || _show.CurrentItem < 0 ? 0 : _show.ItemStarted ? _show.CurrentItem + 1 : _show.CurrentItem;
        return next >= 0 && next < items.Count ? Director.CueAdvice(items[next], next) : [];
    }

    /// <summary>
    /// The clip feeding the VT countdown (<see cref="PlayoutDirector.PickVt"/>): of the links that may feed it, in Settings' order, the
    /// first whose clip runs, else vMix's running clip, else the first with a clip, else vMix's.
    /// </summary>
    private PlayoutState? PickPlayout() => PlayoutDirector.PickVt(Settings.Integrations.PlayoutLinks, name => _playout[name]?.Value, _vmix?.Value);

    /// <summary>The playout link whose clip feeds the VT countdown now, by name, or null (none, or vMix's).</summary>
    private string? VtLink() => PickPlayout() is { Link.Length: > 0 } vt ? vt.Link : null;

    /// <summary>
    /// "Last 10 s" can work: the link its press reaches (<see cref="Last10Args"/>, routed as the press routes it) is connected with a
    /// clip on air that counts down — the button is lit only where its press would go.
    /// </summary>
    [ObservableProperty] private bool _patternsCanJump;

    /// <summary>
    /// What "Last 10 s" asks for: the clip feeding the VT jumps to its last 10 s, over its own link; with none feeding it, link
    /// patterns', else the only link switched on — the one target the button lights for and its press reaches.
    /// </summary>
    private PlayoutArgs Last10Args(string? vt) => new(vt ?? PlayoutRules.PatternsTarget(Settings.Integrations.PlayoutLinks), "end", "", "", false);

    /// <summary>Called once a frame (owner thread) to keep the button's state current.</summary>
    private void RefreshPatternsButton()
    {
        var vt = VtLink();
        var (target, _) = PlayoutRules.Route(Settings.Integrations.PlayoutLinks, Last10Args(vt), FeatureGate.AllowsPlayout(_license.Tier), name => _playout[name] is not null, vt);
        PatternsCanJump = target is not null && _playout[target.Name] is { Connected: true } p && p.Video is { CountsDown: true };
    }

    /// <summary>"Last 10 s": the clip feeding the VT jumps to its last 10 s, over its own link.</summary>
    [RelayCommand]
    private void PatternsLast10()
    {
        var vt = VtLink();
        Ran(Director.Playout(Last10Args(vt), 10, _show.RundownActive ? _show.CurrentItem : -1, vt));
    }

    /// <summary>
    /// 4 Hz (owner thread): a link's clip on air reached its end — once per clip run. It goes in the show log and fires the "clip ends"
    /// trigger, and when the link fed the VT at that moment, its next-on-end is on and the item is a video item, the running order
    /// moves on — at most once an item execution (<see cref="PlayoutClipEnds.MayMoveOn"/>), however many links end. A clip rewound or
    /// restarted can end again.
    /// </summary>
    private void WatchClipEnd()
    {
        var vt = VtLink();
        foreach (var s in Settings.Integrations.PlayoutLinks)
        {
            if (_playout[s.Name] is not { } link || link.Video is not { } v || !_clipEnds.Ended(s.Name, v)) continue;
            var item = _show.RundownActive ? _show.CurrentItem : -1;
            _journal.Add(DateTime.UtcNow, JournalKind.Playout, item, $"{link.Peer} clip ended: {v.Title}");
            FireTrigger(TriggerEvent.ClipEnd, v.Tag.Length > 0 ? v.Tag : v.File, v.Title);
            var items = Settings.Rundown.Items;
            var isVideo = item >= 0 && item < items.Count && (items[item].Kind == SegmentKind.Video || items[item].Labels.Any(l => l.Equals("VT", StringComparison.OrdinalIgnoreCase)));
            var fedVt = s.Name == vt || s.Name == _vtWas;
            if (fedVt && s.NextOnEnd && isVideo && item + 1 < items.Count && _clipEnds.MayMoveOn(_show.Execution))
            {
                StatusLine = $"VT {v.Title} ended — next item";
                _show.Next();
            }
            else StatusLine = $"VT {v.Title} ended";
        }
        _clipEnds.Keep(_playout.All.Select(l => l.LinkName));
        _vtWas = vt;
    }

    /// <summary><c>patterns &lt;end|restart|play|pause|stop|seek&gt; …</c>: the clip on link "patterns", else on the only link. Owner thread.</summary>
    private void PatternsVideoCommand(string what, int n)
        => Ran(Director.Patterns(what, n, _show.RundownActive ? _show.CurrentItem : -1, VtLink()));

    /// <summary><c>playout [&lt;link&gt;] &lt;action&gt; …</c> (owner thread).</summary>
    private void PlayoutCommand(ShowCommand c) => RunPlayout(c.Get<PlayoutArgs>(), c.Number);

    private void RunPlayout(PlayoutArgs a, double number) => Ran(Director.Playout(a, number, _show.RundownActive ? _show.CurrentItem : -1, VtLink()));

    /// <summary>
    /// What a playout command became: refused with why (nothing sent), or on its way — never that it was done. The peer's own answer
    /// comes back on the status line, in the show log once it settles, and in the sender's receipt (<see cref="ReceiptPeer"/>).
    /// </summary>
    private void Ran(PlayoutStep step)
    {
        if (step.Error is { } why)
        {
            Refuse(why);
            return;
        }
        StatusLine = $"{step.Peer}: {step.Asked} — on its way, waiting for its answer" + (step.Note.Length > 0 ? " · " + step.Note : "");
        ReceiptPeer(step);
    }

    /// <summary>
    /// The playout links a peer or an AI may propose lines for, for the context's commands: the ones
    /// running under the licence — moving the clip on air on each, and cueing and starting on one whose kind its STATE said — never one
    /// whose peer answers as another kind than its settings name, which is sent nothing.
    /// </summary>
    private IReadOnlyList<PlayoutRow> PlayoutRows()
    {
        if (!FeatureGate.AllowsPlayout(_license.Tier)) return [];
        return Settings.Integrations.PlayoutLinks
            .Where(s => s.Enabled)
            .Select(s => _playout[s.Name] is { WrongKind: null } l ? new PlayoutRow(s.Name, l.Peer, l.Kind ?? "", Starts: l.Kind is not null) : null)
            .OfType<PlayoutRow>().ToList();
    }

    /// <summary>
    /// Each running link as its context items take it (Show Bus 13.16.4): its record's light and flags — a level-0 link is amber,
    /// <c>legacy</c>, and plain off this PC — beside the link's own words, and its clock.
    /// </summary>
    private IEnumerable<PlayoutLinkFact> PlayoutLinkFacts()
    {
        foreach (var s in Settings.Integrations.PlayoutLinks)
        {
            if (_playout[s.Name] is not { } link) continue;
            var record = link.Record;
            var clock = link.Clock;
            yield return new PlayoutLinkFact(s.Name, link.Peer, record.Light, link.Status, record.Flags, clock?.OffsetMs, clock?.ErrMs, link.ClockApart);
        }
    }

    /// <summary>The playout links as the copilot sees them (<see cref="PlayoutFact"/>): what a line it proposes can name.</summary>
    private IReadOnlyList<PlayoutFact> PlayoutFactsForAi()
    {
        var lead = Settings.Integrations.PlayoutLeadMs;
        var facts = new List<PlayoutFact>();
        foreach (var s in Settings.Integrations.PlayoutLinks)
        {
            if (_playout[s.Name] is not { } link) continue;
            // A peer that answers as another kind than its settings name feeds no reader: its status says why, and nothing else of it.
            var state = link.WrongKind is null ? link.Reading?.State : null;
            var who = link.Whoami is { } w ? $"role {w.Role}" + (w.Armed ? ", armed" : "") + (w.MayDrive ? "" : ", lets this PC only read") : "";
            facts.Add(new PlayoutFact(s.Name, link.Kind ?? "", link.Peer, link.Pairing != PlayoutPairing.None, link.CanSchedule(PlayoutDirector.ToleranceMs(lead)),
                link.Record.Light, link.Status, who,
                (state?.Playlists ?? []).Take(AiContext.PlayoutPlaylistsMax).Select(p => new PlayoutPlaylistFact(p.Id, p.Name, p.State,
                    p.ItemId.Length > 0 ? $"{p.ItemId} · {p.ItemLabel}" : "", p.NextId.Length > 0 ? $"{p.NextId} · {p.NextLabel}" : "")).ToList(),
                state?.Cuestack is { StandbyId.Length: > 0 } c ? $"{c.StandbyId} · {c.StandbyName}" : "",
                link.WrongKind is null ? link.Index?.Lines(AiContext.PlayoutListedMax) ?? [] : [],
                AiContext.PlayoutCues(Settings.Rundown.Items, s.Name)));
        }
        return facts;
    }

    /// <summary>
    /// The links remotes may name (the config's <c>playoutLinks</c>), under the licence: each switched on, its kind as heard (or as set),
    /// and its peer as people read it.
    /// </summary>
    private IReadOnlyList<(string Name, string Kind, string Label)> PlayoutConfig()
    {
        if (!FeatureGate.AllowsPlayout(_license.Tier)) return [];
        return Settings.Integrations.PlayoutLinks.Where(s => s.Enabled).Select(s =>
        {
            var link = _playout[s.Name];
            var kind = link?.Kind ?? (s.Kind == PlayoutRules.Auto ? "" : s.Kind);
            return (s.Name, kind, link?.Peer ?? PlayoutRules.Peer(kind, s.Name));
        }).ToList();
    }

    /// <summary>The config's links as last sent; a kind heard since sends the config again (owner thread, once a second).</summary>
    private string _playoutConfigSaid = "";

    private void RefreshPlayoutConfig()
    {
        var now = string.Join(",", PlayoutConfig().Select(l => l.Name + ":" + l.Kind + ":" + l.Label));
        if (now == _playoutConfigSaid) return;
        _playoutConfigSaid = now;
        BumpConfig();
    }

    /// <summary>
    /// The context a playout link pulled for the node with this id, as a proposal's risk may take it (<see cref="PlayoutLink.PulledContext"/>:
    /// read over this Stagewatch's own link to the address the operator set, from a peer of the kind its settings name, for the id its
    /// STATE gives), or null. <see cref="PeerContexts.RiskFor"/> counts it only while no key speaks for that id.
    /// </summary>
    private string? LinkedContext(string nodeId)
        => PlayoutLinks.PulledFor(OrderedLinks(), nodeId);

    /// <summary>The playout links running now, in Settings' order: the first that gives a node id speaks for it.</summary>
    private IEnumerable<PlayoutLink> OrderedLinks()
        => Settings.Integrations.PlayoutLinks.Select(s => _playout[s.Name]).OfType<PlayoutLink>();

    /// <summary>
    /// The certificates of the playout links to a Show network row's node that wait for a person on this desk (Show Bus 13.8.2–3): a
    /// changed one, which nothing sends to until a person types at least 8 hex of it, or one pinned first, which one press beside its
    /// short form trusts. A link is the row's when the certificate's pin names its id or the link's STATE does.
    /// </summary>
    public IReadOnlyList<CertificateWaiting> CertificatesWaitingFor(PeerRow row)
        => row.NodeId.Length == 0 ? [] : CertificatesWaiting().Where(w => w.Wait.NodeId == row.NodeId || w.NodeId == row.NodeId).ToList();

    /// <summary>
    /// Every playout link's certificate that waits for a person now, in Settings' order: what the status line's right-click offers, so a
    /// link whose node has no Show network row (its hello not heard, a pin kept without its id) is trusted from the desk all the same.
    /// Each is drawn once, bound to the fingerprint it shows: a press on it trusts that certificate or nothing.
    /// </summary>
    public IReadOnlyList<CertificateWaiting> CertificatesWaiting()
        => OrderedLinks().Where(l => l.TrustWaiting is not null)
            .Select(l => new CertificateWaiting(l.LinkName, l.Peer, l.TrustWaiting!, l.NodeId ?? "", l.OverTls)).ToList();

    /// <summary>
    /// A person on this desk trusts a playout link's certificate (<see cref="PlayoutLink.Trust"/>): from the Show network row's button,
    /// its right-click entry or the status line's, beside the fingerprint, and nowhere else — never a line, so never a deck, a trigger,
    /// the wire or the assistant. After CERTIFICATE CHANGED <paramref name="hex"/> is what the person typed into an entry left empty for
    /// them (<paramref name="typed"/>), at least 8 hex of the new certificate; for one pinned first, the whole fingerprint the press was
    /// drawn with — so a certificate pinned in its place since is never trusted by it. The status line and the show log say what
    /// happened, trusted or not, and never as a recorded command. Show thread.
    /// </summary>
    public (bool Trusted, string Words) TrustPlayoutCertificate(string linkName, string hex, bool typed)
    {
        if (_playout[linkName] is not { } link)
            return (false, StatusLine = $"Not trusted: the playout link {PlayoutRules.Label(linkName)} isn't running.");
        var (trusted, words) = link.Trust(hex, typed);
        StatusLine = trusted ? words : "Not trusted: " + words;
        _journal.Add(DateTime.UtcNow, JournalKind.Playout, _show.RundownActive ? _show.CurrentItem : -1,
            $"{link.Peer} link: {(trusted ? "" : "certificate not trusted — ")}{words}");
        return (trusted, StatusLine);
    }
}

/// <summary>
/// A playout link's certificate that waits for a person, as the desk offers it: the link, its peer as people read it, the certificate
/// (<see cref="PlayoutTrustWait"/>) as it was when this was drawn — the press is bound to it — the node the link's STATE names ("" for
/// none) and whether the link runs over TLS now. <see cref="Label"/> is the entry's words, the same on the row's button, its
/// right-click menu and the status line's.
/// </summary>
public sealed record CertificateWaiting(string Link, string Peer, PlayoutTrustWait Wait, string NodeId = "", bool OverTls = false)
{
    /// <summary>The certificate's short form, as a person compares it with the node's own screen.</summary>
    public string Short => PlayoutWire.Short(Wait.Fingerprint);

    public string Label => Wait.Changed
        ? $"Trust {Peer}'s new certificate {Short} … (type it)…"
        : $"Trust {Peer}'s certificate {Short} … (compared on its screen)";

    /// <summary>
    /// Why it waits, in words, as the row shows it: CERTIFICATE CHANGED and nothing sent; or a certificate pinned first only — said to
    /// run over TLS only while the link does.
    /// </summary>
    public string Note => Wait.Changed
        ? $"CERTIFICATE CHANGED on {Peer}: it now shows {Short} … — nothing is sent to it until you compare it on its own screen and type at least {PlayoutRules.TrustHexMin.ToString(System.Globalization.CultureInfo.InvariantCulture)} hex of it"
        : $"{Peer}'s certificate is pinned first only ({Short} …){(OverTls ? " and the link runs over TLS with it" : "")} — compare it on its own screen, then trust it";
}
