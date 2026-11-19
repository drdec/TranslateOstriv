using System.Buffers.Binary;
using System.Text;

namespace OstrivTranslate.Core;

public sealed record PeSection(string Name, int HeaderOffset, int Rva, int VirtualSize, int RawOffset, int RawSize, uint Flags);

public sealed class PeImage
{
    public byte[] Bytes { get; }
    public int PeOffset { get; }
    public int OptionalOffset { get; }
    public int SectionTable { get; }
    public ulong ImageBase { get; }
    public int SectionAlignment { get; }
    public int FileAlignment { get; }
    public List<PeSection> Sections { get; } = [];
    public static readonly Encoding Utf16 = new UnicodeEncoding(false, false, true);

    public PeImage(byte[] bytes)
    {
        Bytes = bytes;
        try
        {
            if (bytes.Length < 256 || bytes[0] != 'M' || bytes[1] != 'Z') throw new FormatException();
            PeOffset = I32(0x3c);
            if (U32(PeOffset) != 0x4550 || U16(PeOffset + 4) != 0x8664) throw new FormatException();
            OptionalOffset = PeOffset + 24;
            if (U16(OptionalOffset) != 0x20b || U16(PeOffset + 20) < 240) throw new FormatException();
            ImageBase = U64(OptionalOffset + 24);
            SectionAlignment = I32(OptionalOffset + 32);
            FileAlignment = I32(OptionalOffset + 36);
            if (SectionAlignment <= 0 || FileAlignment <= 0 || (FileAlignment & (FileAlignment - 1)) != 0 || SectionAlignment < FileAlignment) throw new FormatException();
            SectionTable = OptionalOffset + U16(PeOffset + 20);
            for (var i = 0; i < U16(PeOffset + 6); i++)
            {
                var o = checked(SectionTable + i * 40);
                var s = new PeSection(Encoding.ASCII.GetString(bytes, o, 8).TrimEnd('\0'), o, I32(o + 12), I32(o + 8), I32(o + 20), I32(o + 16), U32(o + 36));
                if (s.Rva < 0 || s.VirtualSize < 0 || s.RawOffset < 0 || s.RawSize < 0 || (long)s.RawOffset + s.RawSize > bytes.Length) throw new FormatException();
                Sections.Add(s);
            }
            if (Sections.Count == 0) throw new FormatException();
        }
        catch (Exception e) when (e is ArgumentException or OverflowException or FormatException or IndexOutOfRangeException)
        { throw new TranslationException("InvalidPe"); }
    }

    public ushort U16(int o) => BinaryPrimitives.ReadUInt16LittleEndian(Bytes.AsSpan(o, 2));
    public uint U32(int o) => BinaryPrimitives.ReadUInt32LittleEndian(Bytes.AsSpan(o, 4));
    public int I32(int o) => BinaryPrimitives.ReadInt32LittleEndian(Bytes.AsSpan(o, 4));
    public ulong U64(int o) => BinaryPrimitives.ReadUInt64LittleEndian(Bytes.AsSpan(o, 8));
    public int RvaToOffset(int rva)
    {
        var s = Sections.FirstOrDefault(s => rva >= s.Rva && (long)rva < (long)s.Rva + s.RawSize);
        return s is null ? -1 : checked(s.RawOffset + rva - s.Rva);
    }
    public int OffsetToRva(int offset)
    {
        var s = Sections.FirstOrDefault(s => offset >= s.RawOffset && (long)offset < (long)s.RawOffset + s.RawSize);
        return s is null ? -1 : checked(s.Rva + offset - s.RawOffset);
    }
    public string? ReadUtf16(int offset)
    {
        var s = Sections.FirstOrDefault(s => offset >= s.RawOffset && offset < (long)s.RawOffset + s.RawSize);
        if (s is null || offset % 2 != 0) return null;
        var end = offset;
        while (end + 1 < (long)s.RawOffset + s.RawSize && end - offset <= 131072)
        {
            if (Bytes[end] == 0 && Bytes[end + 1] == 0)
            {
                try { return Utf16.GetString(Bytes, offset, end - offset); }
                catch (DecoderFallbackException) { return null; }
            }
            end += 2;
        }
        return null;
    }
    public string? ReadAscii(int offset)
    {
        if (offset < 0 || offset >= Bytes.Length) return null;
        var end = offset;
        while (end < Bytes.Length && end - offset < 65536 && Bytes[end] != 0)
        {
            if (Bytes[end] < 32 && Bytes[end] is not (9 or 10 or 13) || Bytes[end] > 126) return null;
            end++;
        }
        return end < Bytes.Length && Bytes[end] == 0 ? Encoding.ASCII.GetString(Bytes, offset, end - offset) : null;
    }
    public static int Align(int value, int alignment) => checked((value + alignment - 1) / alignment * alignment);
    public static void Write32(byte[] bytes, int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset, 4), value);
    public static void Write64(byte[] bytes, int offset, ulong value) => BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(offset, 8), value);
}
