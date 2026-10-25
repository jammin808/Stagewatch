using System.Globalization;
using System.Runtime.CompilerServices;

namespace Stagewatch.Core;

/// <summary>
/// How strongly a request proved which node it speaks for (Show Bus §11). Ordered: a stronger proof is never replaced by a
/// weaker one.
/// </summary>
public enum NodeProof
{
    /// <summary>Nothing that names a node: an open Stagewatch, a read or MCP key. The id is only claimed.</summary>
    None = 0,
    /// <summary>The operator's own credential (the PIN or a control key): trusted, but not one node's.</summary>
    Operator = 1,
    /// <summary>A node key bound to this id: the node itself.</summary>
    Key = 2,
}

/// <summary>
/// What a request carries when it speaks for a node: a node key (by its id), the operator's credential, or nothing. The
/// default is <see cref="None"/>, the weakest, so a credential nobody set proves nothing.
/// </summary>
public readonly record struct NodeCredential
{
    private NodeCredential(string? keyId, NodeProof proof) { KeyId = keyId; Proof = proof; }

    /// <summary>The node key's id when <see cref="Proof"/> is <see cref="NodeProof.Key"/>, else null.</summary>
    public string? KeyId { get; }
    public NodeProof Proof { get; }

    public static NodeCredential None => default;
    public static NodeCredential Operator => new(null, NodeProof.Operator);

    /// <summary>A node key; an empty id proves nothing.</summary>
    public static NodeCredential Key(string? keyId) => string.IsNullOrEmpty(keyId) ? default : new(keyId, NodeProof.Key);
}

/// <summary>What the bindings made of a request: allowed with its proof, or refused with words a person can act on.</summary>
public readonly record struct NodeVerdict(bool Ok, NodeProof Proof, string? Error)
{
    public static NodeVerdict Allowed(NodeProof proof) => new(true, proof, null);
    public static NodeVerdict Refused(string error) => new(false, NodeProof.None, error);
}

/// <summary>
/// A key now speaks for a node: on its first proven use, when that use first names the node's kind, or by the operator's
/// choice. <see cref="PreviousId"/> is what this key spoke for before (null when nothing); <see cref="TakenFromKeyId"/> is
/// the key the id was moved off (the operator's choice only). Ids and names only, never a secret.
/// </summary>
public readonly record struct NodeBound(string KeyId, string Kind, string Id, string? PreviousKind, string? PreviousId, string? TakenFromKeyId, bool ByOperator);

/// <summary>The last time a node key was refused, and for which node: what the operator's "make a key" and "let it speak" offer.</summary>
public readonly record struct NodeRefusal(string KeyId, string Kind, string Id, DateTime AtUtc, string Words);

/// <summary>
/// One node key speaks for one node id, and an id that has a key is spoken for only with that key (Show Bus §11). A key
/// bound to nothing is bound by its first proven use; afterwards it is refused for any other id, and every other
/// credential (another key, the PIN, a control key, none) is refused for its id. Only the operator moves a binding
/// (<see cref="Rebind"/>). Checked before anything is stored, queued or claimed. Thread-safe: one lock, events raised
/// outside it on the caller's thread.
/// </summary>
public sealed class NodeBindings
{
    private static readonly ConditionalWeakTable<object, NodeBindings> Owned = new();

    private readonly object _lock = new();
    // Callers pass only keys the server accepted, so these hold at most the keys in force (ApiKeys.Max) plus any revoked
    // since the last Keep.
    private readonly Dictionary<string, (string Kind, string Id)> _byKey = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _keyById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, NodeRefusal> _refused = new(StringComparer.Ordinal);
    private readonly HashSet<string> _revoked = new(StringComparer.Ordinal);
    private readonly Queue<string> _revokedOrder = new();
    private HashSet<string>? _live;

    /// <summary>Empty bindings (first proven use wins), or seeded from the keys in force and the pairs saved on them.</summary>
    public NodeBindings(IEnumerable<ApiKey>? keys = null)
    {
        if (keys is not null) Keep(keys);
    }

    /// <summary>
    /// The fail-closed default for a surface that keeps no bindings of its own: one instance per owner, still
    /// first-bind-wins, so a fake or a bare host can't let one key speak for two nodes.
    /// </summary>
    public static NodeBindings For(object owner) => Owned.GetValue(owner, _ => new NodeBindings());

    /// <summary>Raised when a key comes to speak for a node (first use, kind first named, or the operator's choice).</summary>
    public event Action<NodeBound>? Bound;

    /// <summary>A node id as Show Bus names it: 1–40 ASCII letters, digits, '-' or '_'.</summary>
    public static bool ValidId(string? id) => id is { Length: > 0 and <= 40 } && id.All(Token);

    /// <summary>A node kind as a hello names it (patterns, messenger, playmap…): 1–16 ASCII letters, digits, '-' or '_'.</summary>
    public static bool ValidKind(string? kind) => kind is { Length: > 0 and <= 16 } && kind.All(Token);

    private static bool Token(char c) => char.IsAsciiLetterOrDigit(c) || c is '-' or '_';

    /// <summary>"patterns 9f8e7d6c", or "node 9f8e7d6c" while the kind is not known.</summary>
    public static string Describe(string? kind, string id) => string.IsNullOrEmpty(kind) ? "node " + id : kind + " " + id;

    /// <summary>A proof as the wire says it (a context's <c>verified</c>): key, operator or none.</summary>
    public static string Word(NodeProof proof) => proof switch { NodeProof.Key => "key", NodeProof.Operator => "operator", _ => "none" };

    /// <summary>
    /// May <paramref name="credential"/> speak for the node (<paramref name="kind"/>, <paramref name="id"/>)? A node key
    /// bound to nothing is bound to it now, when the id is free. An empty kind (a collect names only the id) matches any
    /// kind, and a binding made without one takes the first kind named later. Refusals by a key are remembered (the
    /// latest per key) for the operator.
    /// </summary>
    public NodeVerdict Speak(NodeCredential credential, string? kind, string id, DateTime nowUtc) => Decide(credential, kind, id, nowUtc, bind: true);

    /// <summary>
    /// What <see cref="Speak"/> answers now, binding nothing: the gate a server runs on what a request shows before the node
    /// route's store is called, so no store can skip a refusal. Refusals are remembered as Speak's are. A key bound to
    /// nothing stays unbound (allowed with its key's proof) until a store takes its request: then the store speaks, and the
    /// server speaks again for what was taken (a no-op after the store), so a request the store refuses (malformed, no
    /// room) binds no key and a store that never speaks still can't let one key speak for two nodes.
    /// </summary>
    public NodeVerdict Check(NodeCredential credential, string? kind, string id, DateTime nowUtc) => Decide(credential, kind, id, nowUtc, bind: false);

    private NodeVerdict Decide(NodeCredential credential, string? kind, string id, DateTime nowUtc, bool bind)
    {
        kind ??= "";
        if (!ValidId(id)) return NodeVerdict.Refused("a node id is 1-40 letters, digits, - or _");
        if (kind.Length > 0 && !ValidKind(kind)) return NodeVerdict.Refused("a node kind is 1-16 letters, digits, - or _");
        NodeBound? bound = null;
        NodeVerdict verdict;
        lock (_lock)
        {
            var holder = _keyById.TryGetValue(id, out var h) ? h : null;
            if (credential.Proof != NodeProof.Key || credential.KeyId is not { } keyId)
            {
                verdict = holder is null ? NodeVerdict.Allowed(credential.Proof) : NodeVerdict.Refused(OwnKey(_byKey[holder].Kind, id));
            }
            else if (_revoked.Contains(keyId))
            {
                verdict = NodeVerdict.Refused("unknown or revoked API key");
            }
            else if (_byKey.TryGetValue(keyId, out var mine))
            {
                if (mine.Id != id || (kind.Length > 0 && mine.Kind.Length > 0 && !string.Equals(mine.Kind, kind, StringComparison.Ordinal)))
                    verdict = Refuse(keyId, kind, id, nowUtc, $"this key speaks for {Describe(mine.Kind, mine.Id)} — make a key for {Describe(kind, id)}");
                else
                {
                    if (bind && mine.Kind.Length == 0 && kind.Length > 0)
                    {
                        _byKey[keyId] = (kind, id);
                        bound = new NodeBound(keyId, kind, id, "", id, null, false);
                    }
                    verdict = NodeVerdict.Allowed(NodeProof.Key);
                }
            }
            else if (holder is not null)
            {
                verdict = Refuse(keyId, kind, id, nowUtc, OwnKey(_byKey[holder].Kind, id));
            }
            else
            {
                if (bind)
                {
                    _byKey[keyId] = (kind, id);
                    _keyById[id] = keyId;
                    bound = new NodeBound(keyId, kind, id, null, null, null, false);
                }
                verdict = NodeVerdict.Allowed(NodeProof.Key);
            }
        }
        if (bound is { } b) Bound?.Invoke(b);
        return verdict;
    }

    /// <summary>
    /// The operator lets a key speak for a node: the id moves off any other key (which then speaks for nothing), and the
    /// key's own earlier id is freed. Returns the change, or why not.
    /// </summary>
    public (NodeBound? Bound, string? Error) Rebind(string keyId, string kind, string id)
    {
        if (string.IsNullOrEmpty(keyId)) return (null, "which key?");
        if (!ValidId(id)) return (null, "a node id is 1-40 letters, digits, - or _");
        if (!ValidKind(kind)) return (null, "a node kind is 1-16 letters, digits, - or _");
        NodeBound change;
        lock (_lock)
        {
            if (_revoked.Contains(keyId)) return (null, "that key was revoked");
            var had = _byKey.TryGetValue(keyId, out var mine) ? mine : ((string Kind, string Id)?)null;
            var takenFrom = _keyById.TryGetValue(id, out var holder) && holder != keyId ? holder : null;
            if (had is { } old && old.Id == id && old.Kind == kind) return (null, $"it already speaks for {Describe(kind, id)}");
            if (takenFrom is not null) _byKey.Remove(takenFrom);
            if (had is { } prev) _keyById.Remove(prev.Id);
            _byKey[keyId] = (kind, id);
            _keyById[id] = keyId;
            if (_refused.TryGetValue(keyId, out var r) && r.Id == id) _refused.Remove(keyId);
            change = new NodeBound(keyId, kind, id, had?.Kind, had?.Id, takenFrom, true);
        }
        Bound?.Invoke(change);
        return (change, null);
    }

    /// <summary>
    /// The keys in force, after any change to them. A key no longer listed is forgotten with its binding and its refusal,
    /// and refused from then on (a request already past the key check can't bind a revoked key). A pair saved on a key
    /// (<see cref="ApiKey.Node"/>) is put back when neither the key nor the id is bound here: a restart keeps them.
    /// </summary>
    public void Keep(IEnumerable<ApiKey> keys)
    {
        var list = keys.Where(k => k is not null && k.Id.Length > 0).ToList();
        var live = list.Select(k => k.Id).ToHashSet(StringComparer.Ordinal);
        lock (_lock)
        {
            var wasLive = _live;
            var gone = (wasLive ?? Enumerable.Empty<string>()).Concat(_byKey.Keys).Concat(_refused.Keys).Where(id => !live.Contains(id)).Distinct().ToList();
            foreach (var id in gone)
            {
                if (_byKey.Remove(id, out var pair) && _keyById.TryGetValue(pair.Id, out var holder) && holder == id) _keyById.Remove(pair.Id);
                _refused.Remove(id);
                if (wasLive?.Contains(id) == true && _revoked.Add(id)) _revokedOrder.Enqueue(id);
            }
            foreach (var id in live) _revoked.Remove(id);
            // Revoked ids are random and never come back; the newest ApiKeys.Max cover any request still in flight.
            while (_revokedOrder.Count > ApiKeys.Max) _revoked.Remove(_revokedOrder.Dequeue());
            foreach (var k in list)
            {
                if (k.Scope != ApiKeyScope.Node || !ValidId(k.Node) || _byKey.ContainsKey(k.Id) || _keyById.ContainsKey(k.Node!)) continue;
                var kind = ValidKind(k.NodeKind) ? k.NodeKind! : "";
                _byKey[k.Id] = (kind, k.Node!);
                _keyById[k.Node!] = k.Id;
            }
            _live = live;
        }
    }

    /// <summary>The node a key speaks for, or null.</summary>
    public (string Kind, string Id)? NodeOf(string keyId)
    {
        lock (_lock) return _byKey.TryGetValue(keyId, out var n) ? n : null;
    }

    /// <summary>The key that speaks for a node id, or null when the id is free.</summary>
    public string? KeyOf(string id)
    {
        lock (_lock) return _keyById.TryGetValue(id, out var k) ? k : null;
    }

    /// <summary>The node a key was last refused for, and when.</summary>
    public NodeRefusal? LastRefused(string keyId)
    {
        lock (_lock) return _refused.TryGetValue(keyId, out var r) ? r : null;
    }

    /// <summary>Every key's last refusal, newest first.</summary>
    public IReadOnlyList<NodeRefusal> Refusals()
    {
        lock (_lock) return _refused.Values.OrderByDescending(r => r.AtUtc).ToList();
    }

    /// <summary>
    /// The newest refusal for each node id since <paramref name="sinceUtc"/>, newest first, never for
    /// <paramref name="ownId"/>: the nodes the desk's Show network says were refused.
    /// </summary>
    public IReadOnlyList<NodeRefusal> RefusedSince(DateTime sinceUtc, string? ownId)
    {
        lock (_lock)
            return _refused.Values.Where(r => r.AtUtc >= sinceUtc && r.Id != ownId).OrderByDescending(r => r.AtUtc)
                .DistinctBy(r => r.Id, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// The kind a key made for <paramref name="id"/> is saved with: the kind the newest refused request for that id named
    /// (a context's node.t, a proposal's from.t), or null when none named one (a collect names only the id) — then the
    /// binding learns it from the node's own first words. Never a hello's word, nor another node's word for it.
    /// </summary>
    public string? KindToMakeFor(string id)
    {
        lock (_lock)
            return _refused.Values.Where(r => r.Id == id && r.Kind.Length > 0).OrderByDescending(r => r.AtUtc).Select(r => r.Kind).FirstOrDefault();
    }

    private NodeVerdict Refuse(string keyId, string kind, string id, DateTime nowUtc, string words)
    {
        _refused[keyId] = new NodeRefusal(keyId, kind, id, nowUtc, words);
        return NodeVerdict.Refused(words);
    }

    private static string OwnKey(string kind, string id) => $"{Describe(kind, id)} speaks with its own key";

    /// <summary>Whole seconds since <paramref name="thenUtc"/>, for words ("12 s ago").</summary>
    internal static string SecondsSince(DateTime thenUtc, DateTime nowUtc)
        => ((long)Math.Max(0, (nowUtc - thenUtc).TotalSeconds)).ToString(CultureInfo.InvariantCulture);
}
