using System.Security.Cryptography;
using Iced.Intel;

namespace OstrivTranslate.Core;

public static class Extractor
{
    public const string SupportedHash = "448f226f40cf4a2bf591818a8d2604ee1cb470aeb0ee2adc3f37a9e4aa448779";
    public static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    public static Extraction Extract(string path) => Extract(File.ReadAllBytes(path), Path.GetFullPath(path));

    public static Extraction Extract(byte[] bytes, string path = "")
    {
        var pe = new PeImage(bytes);
        var hash = Hash(bytes);
        var supported = hash == SupportedHash;
        var project = new TranslationProject { SourceHash = hash, SourcePath = path, Profile = supported ? "ostriv-448f226f-v1" : "unverified" };
        var refs = new List<StringReference>();
        var entries = new Dictionary<int, TranslationEntry>();
        var pending = new Queue<Instruction>();
        foreach (var s in CodeRanges(pe))
        {
            var decoder = Decoder.Create(64, new ByteArrayCodeReader(bytes, s.RawOffset, s.RawSize));
            decoder.IP = pe.ImageBase + (uint)s.Rva;
            var end = decoder.IP + (uint)s.RawSize;
            pending.Clear();
            while (decoder.IP < end)
            {
                decoder.Decode(out var ins);
                if (ins.Code == Code.INVALID) { pending.Clear(); continue; }
                if (ins.IsIPRelativeMemoryOperand && ins.IPRelativeMemoryAddress >= pe.ImageBase && ins.IPRelativeMemoryAddress - pe.ImageBase < int.MaxValue)
                {
                    var target = (int)(ins.IPRelativeMemoryAddress - pe.ImageBase);
                    var constants = decoder.GetConstantOffsets(ins);
                    if (constants.DisplacementSize == 4)
                        refs.Add(new(pe.RvaToOffset((int)(ins.IP - pe.ImageBase)) + constants.DisplacementOffset,
                            (int)(ins.NextIP - pe.ImageBase), target, ReferenceKind.RipRelative));
                }
                pending.Enqueue(ins);
                if (pending.Count > 24) pending.Dequeue();
                // Exact build's std::wstring construction followed by the bilingual registration call.
                // Requiring both constructors and their stack arguments prevents identifying unrelated strings as UI.
                if (supported && ins.Mnemonic == Mnemonic.Call && (ins.NearBranchTarget == pe.ImageBase + 0x29fc10 || ins.NearBranchTarget == pe.ImageBase + 0x215610))
                {
                    var window = pending.ToArray();
                    var ukCall = Array.FindLastIndex(window, x => x.Mnemonic == Mnemonic.Call && x.NearBranchTarget == pe.ImageBase + 0x4066d0);
                    var enCall = Array.FindLastIndex(window, x => x.Mnemonic == Mnemonic.Call && x.NearBranchTarget == pe.ImageBase + 0x407970);
                    if (ukCall >= 2 && enCall >= 2 && Math.Abs(ukCall - enCall) <= 5 && window.Length - 1 - Math.Max(ukCall, enCall) <= 6
                        && !window.Skip(Math.Max(ukCall, enCall) + 1).SkipLast(1).Any(x => x.Mnemonic == Mnemonic.Call))
                    {
                        var ukLoad = window[ukCall - 2]; var enLoad = window[enCall - 2];
                        if (IsStringLoad(ukLoad) && IsStringLoad(enLoad))
                        {
                            var ukOff = pe.RvaToOffset((int)(ukLoad.IPRelativeMemoryAddress - pe.ImageBase));
                            var enOff = pe.RvaToOffset((int)(enLoad.IPRelativeMemoryAddress - pe.ImageBase));
                            var original = pe.ReadUtf16(ukOff); var english = pe.ReadAscii(enOff);
                            if (original is { Length: > 0 } && english is { Length: > 0 } && original.All(c => !char.IsControl(c) || c is '\n' or '\r' or '\t'))
                            {
                                if (!entries.TryGetValue(ukOff, out var entry))
                                    entries[ukOff] = entry = MakeEntry(ukOff, original, true, english);
                                else if (!entry.Context.Split(" | ").Contains(english)) entry.Context += " | " + english;
                            }
                        }
                    }
                }
            }
        }
        ReadAbsoluteReferences(pe, refs);
        var refTargets = refs.Select(r => r.TargetRva).ToHashSet();
        // Scan complete NUL-terminated strings in read-only data, never minimum-length word fragments.
        foreach (var s in pe.Sections.Where(s => s.Name == ".rdata"))
        {
            var start = s.RawOffset;
            for (var o = s.RawOffset; o + 1 < s.RawOffset + s.RawSize; o += 2)
            {
                if (bytes[o] != 0 || bytes[o + 1] != 0) continue;
                if (o > start && !entries.ContainsKey(start))
                {
                    var value = pe.ReadUtf16(start);
                    var referenced = refTargets.Contains(pe.OffsetToRva(start));
                    if (value is { Length: > 0 } && IsText(value) && (value.Any(IsUkrainianLetter) || referenced))
                        entries[start] = MakeEntry(start, value, false, "");
                }
                start = o + 2;
            }
        }
        // Include referenced suffixes and strings whose physical predecessor is unrelated data.
        foreach (var target in refTargets)
        {
            var off = pe.RvaToOffset(target);
            if (off < 0 || entries.ContainsKey(off)) continue;
            if (!pe.Sections.Any(s => s.Name == ".rdata" && off >= s.RawOffset && off < s.RawOffset + s.RawSize)) continue;
            var value = pe.ReadUtf16(off);
            if (value is { Length: > 0 } && IsText(value) && value.Any(IsUkrainianLetter))
                entries[off] = MakeEntry(off, value, false, "");
        }
        foreach (var entry in entries.Values)
        {
            var rva = pe.OffsetToRva(entry.Offset);
            entry.ReferenceCount = refs.Count(r => r.TargetRva == rva);
            if (entry.ReferenceCount == 0) entry.RelocationIssue = "NoReferences";
            else if (refs.Any(r => r.TargetRva > rva && r.TargetRva <= rva + entry.OriginalBytes)) entry.RelocationIssue = "InteriorReference";
        }
        foreach (var entry in entries.Values.Where(e => e.RelocationIssue.Length == 0))
            if (entries.Values.Any(other => other.Offset < entry.Offset && other.Offset + other.OriginalBytes >= entry.Offset && other.ReferenceCount > 0))
                entry.RelocationIssue = "SharedStorage";
        project.Entries = entries.Values.OrderBy(e => !e.Confirmed).ThenBy(e => e.Offset).ToList();
        return new(project, pe, refs);
    }

    private static bool IsStringLoad(Instruction i) => i.Mnemonic == Mnemonic.Lea && i.Op0Register == Register.RDX && i.IsIPRelativeMemoryOperand;
    private static IEnumerable<PeSection> CodeRanges(PeImage pe)
    {
        // Resynchronize decoding at every x64 runtime-function boundary. A linear sweep
        // alone can remain misaligned after jump-table data or padding inside .text.
        var boundaries = new HashSet<int>();
        var directory = pe.OptionalOffset + 112 + 3 * 8;
        var table = pe.RvaToOffset(pe.I32(directory)); var size = pe.I32(directory + 4);
        if (table >= 0 && size > 0 && (long)table + size <= pe.Bytes.Length)
            for (var o = table; o + 12 <= table + size; o += 12) { boundaries.Add(pe.I32(o)); boundaries.Add(pe.I32(o + 4)); }
        foreach (var s in pe.Sections.Where(s => (s.Flags & 0x20000000) != 0))
        {
            var points = boundaries.Where(r => r > s.Rva && r < s.Rva + s.RawSize).Append(s.Rva).Append(s.Rva + s.RawSize).Order().ToArray();
            for (var i = 0; i + 1 < points.Length; i++)
                yield return s with { Rva = points[i], RawOffset = s.RawOffset + points[i] - s.Rva, RawSize = points[i + 1] - points[i] };
        }
    }
    private static TranslationEntry MakeEntry(int offset, string text, bool confirmed, string context) => new()
    { Id = $"uk-{offset:x8}", Offset = offset, Original = text, Confirmed = confirmed, Context = context };
    private static bool IsUkrainianLetter(char c) => c is >= 'А' and <= 'я' or 'І' or 'і' or 'Ї' or 'ї' or 'Є' or 'є' or 'Ґ' or 'ґ';
    private static bool IsText(string value) => value.All(c => c is '\n' or '\r' or '\t' || c is >= ' ' and <= '~' || c is >= '\u00a0' and <= '\u024f' || IsUkrainianLetter(c) || c is >= '\u2000' and <= '\u206f') && value.Any(char.IsLetterOrDigit);

    private static void ReadAbsoluteReferences(PeImage pe, List<StringReference> refs)
    {
        var directory = pe.OptionalOffset + 112 + 5 * 8;
        var rva = pe.I32(directory); var size = pe.I32(directory + 4);
        if (rva == 0 || size == 0) return;
        var offset = pe.RvaToOffset(rva);
        if (offset < 0 || size < 0 || (long)offset + size > pe.Bytes.Length) throw new TranslationException("InvalidPe");
        var end = offset + size;
        while (offset + 8 <= end)
        {
            var page = pe.I32(offset); var block = pe.I32(offset + 4);
            if (block < 8 || block % 2 != 0 || (long)offset + block > end) throw new TranslationException("InvalidPe");
            for (var i = offset + 8; i < offset + block; i += 2)
            {
                var item = pe.U16(i);
                if (item >> 12 != 10) continue;
                var field = pe.RvaToOffset(checked(page + (item & 0xfff)));
                if (field < 0 || field + 8 > pe.Bytes.Length) throw new TranslationException("InvalidPe");
                var value = pe.U64(field);
                if (value >= pe.ImageBase && value - pe.ImageBase < int.MaxValue)
                    refs.Add(new(field, 0, (int)(value - pe.ImageBase), ReferenceKind.Absolute64));
            }
            offset += block;
        }
    }
}
