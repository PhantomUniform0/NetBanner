using System.Buffers.Binary;
using System.Text;

namespace NetBanner.Core;

/// <summary>One entry of a Group Policy <c>Registry.pol</c> file.</summary>
public sealed record PolEntry(string Key, string ValueName, uint Type, byte[] Data)
{
    public const uint RegSz = 1, RegExpandSz = 2, RegDword = 4, RegMultiSz = 7;

    /// <summary>Special entries like <c>**del.Name</c> or <c>**delvals.</c> (deletion markers written by gpedit).</summary>
    public bool IsDirective => ValueName.StartsWith("**", StringComparison.Ordinal);

    public static PolEntry String(string key, string name, string value) =>
        new(key, name, RegSz, Encoding.Unicode.GetBytes(value + "\0"));

    public static PolEntry Dword(string key, string name, int value)
    {
        var data = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(data, value);
        return new PolEntry(key, name, RegDword, data);
    }

    /// <summary>The value as the registry API would return it (string, int, string[] or bytes).</summary>
    public object Value => Type switch
    {
        RegSz or RegExpandSz => Encoding.Unicode.GetString(Data).TrimEnd('\0'),
        RegDword when Data.Length >= 4 => BinaryPrimitives.ReadInt32LittleEndian(Data),
        RegMultiSz => Encoding.Unicode.GetString(Data).TrimEnd('\0').Split('\0'),
        _ => Data,
    };
}

/// <summary>
/// Reads and writes the Group Policy <c>Registry.pol</c> format: a "PReg" header and version 1, then entries
/// <c>[key;value;type;size;data]</c> with UTF-16 null-terminated strings.
/// </summary>
public static class RegistryPol
{
    private const uint Signature = 0x67655250; // "PReg"
    private const uint Version = 1;

    public static List<PolEntry> Parse(ReadOnlySpan<byte> bytes)
    {
        var entries = new List<PolEntry>();
        if (bytes.Length == 0)
        {
            return entries;
        }

        if (bytes.Length < 8 || BinaryPrimitives.ReadUInt32LittleEndian(bytes) != Signature
            || BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]) != Version)
        {
            throw new InvalidDataException("Not a Registry.pol file (bad PReg header).");
        }

        int pos = 8;
        while (pos < bytes.Length)
        {
            Expect(bytes, ref pos, '[');
            string key = ReadString(bytes, ref pos);
            Expect(bytes, ref pos, ';');
            string name = ReadString(bytes, ref pos);
            Expect(bytes, ref pos, ';');
            uint type = ReadUInt32(bytes, ref pos);
            Expect(bytes, ref pos, ';');
            int size = checked((int)ReadUInt32(bytes, ref pos));
            Expect(bytes, ref pos, ';');
            if (pos + size > bytes.Length)
            {
                throw new InvalidDataException("Registry.pol entry data runs past the end of the file.");
            }

            var data = bytes.Slice(pos, size).ToArray();
            pos += size;
            Expect(bytes, ref pos, ']');
            entries.Add(new PolEntry(key, name, type, data));
        }

        return entries;
    }

    public static byte[] Serialize(IEnumerable<PolEntry> entries)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, Encoding.Unicode);
        w.Write(Signature);
        w.Write(Version);
        foreach (var e in entries)
        {
            w.Write('[');
            w.Write(Encoding.Unicode.GetBytes(e.Key + "\0"));
            w.Write(';');
            w.Write(Encoding.Unicode.GetBytes(e.ValueName + "\0"));
            w.Write(';');
            w.Write(e.Type);
            w.Write(';');
            w.Write((uint)e.Data.Length);
            w.Write(';');
            w.Write(e.Data);
            w.Write(']');
        }

        w.Flush();
        return ms.ToArray();
    }

    private static void Expect(ReadOnlySpan<byte> b, ref int pos, char c)
    {
        if (pos + 2 > b.Length || BinaryPrimitives.ReadUInt16LittleEndian(b[pos..]) != c)
        {
            throw new InvalidDataException($"Registry.pol is malformed at byte {pos} (expected '{c}').");
        }

        pos += 2;
    }

    private static string ReadString(ReadOnlySpan<byte> b, ref int pos)
    {
        var sb = new StringBuilder();
        while (true)
        {
            if (pos + 2 > b.Length)
            {
                throw new InvalidDataException("Registry.pol string runs past the end of the file.");
            }

            char c = (char)BinaryPrimitives.ReadUInt16LittleEndian(b[pos..]);
            pos += 2;
            if (c == '\0')
            {
                return sb.ToString();
            }

            sb.Append(c);
        }
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> b, ref int pos)
    {
        if (pos + 4 > b.Length)
        {
            throw new InvalidDataException("Registry.pol is truncated.");
        }

        uint v = BinaryPrimitives.ReadUInt32LittleEndian(b[pos..]);
        pos += 4;
        return v;
    }
}
