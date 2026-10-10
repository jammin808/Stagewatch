using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace Stagewatch.Remote;

public sealed record OscMessage(string Address, IReadOnlyList<object> Args);

/// <summary>
/// Minimal OSC 1.0 codec: messages and (nested) bundles; argument types
/// i f s b h d T F N I. Malformed input is rejected, never thrown past the caller.
/// </summary>
public static class OscCodec
{
    private const int MaxBundleDepth = 4;
    private static readonly byte[] BundleTag = "#bundle\0"u8.ToArray();

    public static List<OscMessage> Decode(ReadOnlySpan<byte> packet)
    {
        var result = new List<OscMessage>();
        try { DecodeInto(packet, result, 0); }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or FormatException) { }
        return result;
    }

    private static void DecodeInto(ReadOnlySpan<byte> p, List<OscMessage> into, int depth)
    {
        if (p.Length < 4 || into.Count > 64) return;
        if (p.Length >= 16 && p[..8].SequenceEqual(BundleTag))
        {
            if (depth >= MaxBundleDepth) return;
            var pos = 16; // tag + 64-bit timetag (we execute immediately)
            while (pos + 4 <= p.Length)
            {
                var size = BinaryPrimitives.ReadInt32BigEndian(p[pos..]);
                pos += 4;
                if (size <= 0 || pos + size > p.Length || size % 4 != 0) return;
                DecodeInto(p.Slice(pos, size), into, depth + 1);
                pos += size;
            }
            return;
        }
        if (p[0] != (byte)'/') return;

        var at = 0;
        var address = ReadString(p, ref at);
        var args = new List<object>();
        if (at < p.Length && p[at] == (byte)',')
        {
            var tags = ReadString(p, ref at);
            foreach (var tag in tags.AsSpan(1))
            {
                switch (tag)
                {
                    case 'i': args.Add(BinaryPrimitives.ReadInt32BigEndian(p[at..])); at += 4; break;
                    case 'f': args.Add(BinaryPrimitives.ReadSingleBigEndian(p[at..])); at += 4; break;
                    case 'h': args.Add(BinaryPrimitives.ReadInt64BigEndian(p[at..])); at += 8; break;
                    case 'd': args.Add(BinaryPrimitives.ReadDoubleBigEndian(p[at..])); at += 8; break;
                    case 's': case 'S': args.Add(ReadString(p, ref at)); break;
                    case 'b':
                        var len = BinaryPrimitives.ReadInt32BigEndian(p[at..]);
                        if (len < 0 || at + 4 + len > p.Length) throw new ArgumentException("blob");
                        at += 4 + Pad4(len);
                        args.Add(Array.Empty<byte>());
                        break;
                    case 'T': args.Add(true); break;
                    case 'F': args.Add(false); break;
                    case 'N': case 'I': break;
                    default: throw new FormatException("unsupported OSC type " + tag);
                }
            }
        }
        into.Add(new OscMessage(address, args));
    }

    private static string ReadString(ReadOnlySpan<byte> p, ref int at)
    {
        var rest = p[at..];
        var end = rest.IndexOf((byte)0);
        if (end < 0) throw new ArgumentException("unterminated OSC string");
        var s = Encoding.UTF8.GetString(rest[..end]);
        at += Pad4(end + 1);
        return s;
    }

    private static int Pad4(int n) => (n + 3) & ~3;

    /// <summary>OSC argument to the invariant string the command grammar expects.</summary>
    public static string ArgToString(object a) => a switch
    {
        bool b => b ? "1" : "0",
        float f => f.ToString("R", CultureInfo.InvariantCulture),
        double d => d.ToString("R", CultureInfo.InvariantCulture),
        IFormattable n => n.ToString(null, CultureInfo.InvariantCulture),
        _ => a.ToString() ?? ""
    };

    // ---- encoding (feedback) ----

    public static byte[] EncodeMessage(string address, params object[] args)
    {
        using var ms = new MemoryStream(64);
        WriteMessage(ms, address, args);
        return ms.ToArray();
    }

    public static byte[] EncodeBundle(IReadOnlyList<(string Address, object Arg)> messages)
    {
        using var ms = new MemoryStream(256);
        ms.Write(BundleTag);
        Span<byte> tt = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(tt, 1); // "immediately"
        ms.Write(tt);
        Span<byte> size = stackalloc byte[4];
        foreach (var (address, arg) in messages)
        {
            var msg = EncodeMessage(address, arg);
            BinaryPrimitives.WriteInt32BigEndian(size, msg.Length);
            ms.Write(size);
            ms.Write(msg);
        }
        return ms.ToArray();
    }

    private static void WriteMessage(Stream s, string address, object[] args)
    {
        WriteString(s, address);
        var tags = new StringBuilder(",");
        foreach (var a in args)
            tags.Append(a switch { int => 'i', float => 'f', string => 's', bool b => b ? 'T' : 'F', _ => throw new ArgumentException("type") });
        WriteString(s, tags.ToString());
        Span<byte> buf = stackalloc byte[4];
        foreach (var a in args)
        {
            switch (a)
            {
                case int i: BinaryPrimitives.WriteInt32BigEndian(buf, i); s.Write(buf); break;
                case float f: BinaryPrimitives.WriteSingleBigEndian(buf, f); s.Write(buf); break;
                case string str: WriteString(s, str); break;
            }
        }
    }

    private static void WriteString(Stream s, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        s.Write(bytes);
        var pad = Pad4(bytes.Length + 1) - bytes.Length;
        for (int i = 0; i < pad; i++) s.WriteByte(0);
    }
}
