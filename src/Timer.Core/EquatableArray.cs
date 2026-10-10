using System.Collections;

namespace Stagewatch.Core;

/// <summary>
/// An immutable array with value equality, so records that hold lists (rundown timings, signals,
/// rooms) still compare by content. That keeps <see cref="ShowSnapshot"/> equality meaning
/// "nothing visible changed", which is what stops frames being re-sent.
/// </summary>
public readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IReadOnlyList<T>
{
    private readonly T[]? _items;

    public EquatableArray(T[] items) { _items = items; }
    public EquatableArray(IEnumerable<T> items) { _items = items.ToArray(); }

    public static readonly EquatableArray<T> Empty = new(Array.Empty<T>());

    public int Count => _items?.Length ?? 0;
    public T this[int index] => (_items ?? Array.Empty<T>())[index];
    public bool IsEmpty => Count == 0;

    public bool Equals(EquatableArray<T> other)
    {
        var a = _items ?? Array.Empty<T>();
        var b = other._items ?? Array.Empty<T>();
        if (ReferenceEquals(a, b)) return true;
        if (a.Length != b.Length) return false;
        var cmp = EqualityComparer<T>.Default;
        for (int i = 0; i < a.Length; i++)
            if (!cmp.Equals(a[i], b[i])) return false;
        return true;
    }

    public override bool Equals(object? obj) => obj is EquatableArray<T> o && Equals(o);

    public override int GetHashCode()
    {
        var h = new HashCode();
        foreach (var x in _items ?? Array.Empty<T>()) h.Add(x);
        return h.ToHashCode();
    }

    public static bool operator ==(EquatableArray<T> a, EquatableArray<T> b) => a.Equals(b);
    public static bool operator !=(EquatableArray<T> a, EquatableArray<T> b) => !a.Equals(b);

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)(_items ?? Array.Empty<T>())).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

public static class EquatableArray
{
    public static EquatableArray<T> ToEquatableArray<T>(this IEnumerable<T> items) => new(items);
}
