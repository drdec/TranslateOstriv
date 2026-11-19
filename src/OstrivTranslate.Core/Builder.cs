using System.Buffers.Binary;
using System.Text;

namespace OstrivTranslate.Core;

public sealed record PatchChange(string Id, string Original, string Translation, int SourceOffset, int TargetRva, int ReferenceCount);
public sealed record BuildResult(byte[] Bytes, IReadOnlyList<PatchChange> Changes, string SourceHash, string OutputHash, BuildMode Mode);
public sealed record BuildReceipt(string OutputPath, string OutputHash, int Count, string? ReportError);

public static class Builder
{
    public static BuildResult Build(TranslationProject project, Extraction fresh, string language, BuildMode mode)
    {
        if (fresh.Project.SourceHash != project.SourceHash || Extractor.Hash(fresh.Image.Bytes) != project.SourceHash) throw new TranslationException("SourceChanged");
        if (fresh.Project.SourceHash != Extractor.SupportedHash) throw new TranslationException("UnsupportedBuild");
        if (!project.Languages.Any(l => l.Id == language)) throw new TranslationException("InvalidLanguage");
        var originals = fresh.Project.Entries.ToDictionary(e => e.Id);
        var planned = new List<(TranslationEntry Entry, string Text)>();
        foreach (var saved in project.Entries)
        {
            var text = saved.GetTranslation(language);
            if (text.Length == 0 || text == saved.Original) continue;
            if (!originals.TryGetValue(saved.Id, out var entry) || entry.Offset != saved.Offset || entry.Original != saved.Original)
                throw new TranslationException("EntryMismatch", saved.Id);
            // Never trust offsets, confidence flags, contexts, or reference information imported from JSON.
            var effective = new TranslationEntry { Original = entry.Original, Confirmed = entry.Confirmed, Approved = saved.Approved, RelocationIssue = entry.RelocationIssue };
            var errors = Validator.Validate(effective, text, mode).Where(i => !i.Warning).ToList();
            if (errors.Count > 0) throw new TranslationException(errors[0].Code, saved.Id + " " + errors[0].Detail);
            planned.Add((entry, text));
        }
        if (planned.Count == 0) throw new TranslationException("NoTranslations");
        planned.Sort((a, b) => a.Entry.Offset.CompareTo(b.Entry.Offset));
        for (var i = 1; i < planned.Count; i++)
            if (planned[i].Entry.Offset < planned[i - 1].Entry.Offset + planned[i - 1].Entry.OriginalBytes + 2)
                throw new TranslationException("OverlappingStrings", planned[i].Entry.Id);

        var pe = fresh.Image;
        var result = (byte[])pe.Bytes.Clone();
        var allowed = new List<(int Start, int Length)>();
        var changes = new List<PatchChange>();
        var longStrings = planned.Where(p => p.Text.Length > p.Entry.Original.Length).ToList();
        var newRva = 0; var newRaw = 0;
        if (longStrings.Count > 0)
        {
            if (mode != BuildMode.Extended) throw new TranslationException("TooLong");
            var size = checked(longStrings.Sum(p => PeImage.Utf16.GetByteCount(p.Text) + 2));
            var newHeader = checked(pe.SectionTable + pe.Sections.Count * 40);
            var firstData = pe.Sections.Where(s => s.RawSize > 0).Min(s => s.RawOffset);
            if (newHeader + 40 > firstData || pe.Bytes.AsSpan(newHeader, 40).ContainsAnyExcept((byte)0)) throw new TranslationException("NoSectionSpace");
            if (pe.U32(pe.OptionalOffset + 112 + 4 * 8) != 0) throw new TranslationException("SignedExecutable");
            newRva = PeImage.Align(pe.Sections.Max(s => checked(s.Rva + Math.Max(s.VirtualSize, s.RawSize))), pe.SectionAlignment);
            newRaw = PeImage.Align(result.Length, pe.FileAlignment);
            var rawSize = PeImage.Align(size, pe.FileAlignment);
            Array.Resize(ref result, checked(newRaw + rawSize));
            Encoding.ASCII.GetBytes(".otrl").CopyTo(result, newHeader);
            PeImage.Write32(result, newHeader + 8, size);
            PeImage.Write32(result, newHeader + 12, newRva);
            PeImage.Write32(result, newHeader + 16, rawSize);
            PeImage.Write32(result, newHeader + 20, newRaw);
            PeImage.Write32(result, newHeader + 36, 0x40000040);
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(pe.PeOffset + 6, 2), checked((ushort)(pe.Sections.Count + 1)));
            PeImage.Write32(result, pe.OptionalOffset + 8, checked(pe.I32(pe.OptionalOffset + 8) + rawSize));
            PeImage.Write32(result, pe.OptionalOffset + 56, PeImage.Align(checked(newRva + size), pe.SectionAlignment));
            PeImage.Write32(result, pe.OptionalOffset + 64, 0);
            allowed.AddRange([(newHeader, 40), (pe.PeOffset + 6, 2), (pe.OptionalOffset + 8, 4), (pe.OptionalOffset + 56, 4), (pe.OptionalOffset + 64, 4)]);
        }

        var cursor = 0;
        foreach (var (entry, text) in planned)
        {
            var before = PeImage.Utf16.GetBytes(entry.Original + "\0");
            if (!pe.Bytes.AsSpan(entry.Offset, before.Length).SequenceEqual(before)) throw new TranslationException("EntryMismatch", entry.Id);
            var encoded = PeImage.Utf16.GetBytes(text + "\0");
            var sourceRva = pe.OffsetToRva(entry.Offset);
            var targets = fresh.References.Where(r => r.TargetRva == sourceRva).ToList();
            var targetRva = sourceRva;
            if (text.Length <= entry.Original.Length)
            {
                result.AsSpan(entry.Offset, before.Length).Clear();
                encoded.CopyTo(result, entry.Offset);
                allowed.Add((entry.Offset, before.Length));
            }
            else
            {
                if (targets.Count == 0) throw new TranslationException("NoReferences", entry.Id);
                targetRva = checked(newRva + cursor);
                encoded.CopyTo(result, newRaw + cursor);
                cursor = checked(cursor + encoded.Length);
                foreach (var r in targets)
                {
                    if (r.Kind == ReferenceKind.RipRelative)
                    {
                        var displacement = checked(targetRva - r.InstructionEndRva);
                        PeImage.Write32(result, r.PatchOffset, displacement);
                        allowed.Add((r.PatchOffset, 4));
                    }
                    else
                    {
                        // Existing DIR64 relocation remains attached to the same pointer field.
                        PeImage.Write64(result, r.PatchOffset, pe.ImageBase + (uint)targetRva);
                        allowed.Add((r.PatchOffset, 8));
                    }
                }
            }
            changes.Add(new(entry.Id, entry.Original, text, entry.Offset, targetRva, targets.Count));
        }
        Verify(pe, result, allowed, changes, fresh.References);
        return new(result, changes, project.SourceHash, Extractor.Hash(result), mode);
    }

    private static void Verify(PeImage original, byte[] result, List<(int Start, int Length)> allowed, List<PatchChange> changes, IReadOnlyList<StringReference> references)
    {
        var mask = new bool[original.Bytes.Length];
        foreach (var (start, length) in allowed) Array.Fill(mask, true, start, length);
        for (var i = 0; i < original.Bytes.Length; i++)
            if (original.Bytes[i] != result[i] && !mask[i]) throw new TranslationException("VerificationFailed", $"0x{i:x}");
        var output = new PeImage(result);
        foreach (var change in changes)
        {
            if (output.ReadUtf16(output.RvaToOffset(change.TargetRva)) != change.Translation) throw new TranslationException("VerificationFailed", change.Id);
            var sourceRva = original.OffsetToRva(change.SourceOffset);
            foreach (var r in references.Where(r => r.TargetRva == sourceRva))
            {
                var observed = r.Kind == ReferenceKind.RipRelative ? (long)output.I32(r.PatchOffset) + r.InstructionEndRva : checked((long)(output.U64(r.PatchOffset) - output.ImageBase));
                if (observed != change.TargetRva) throw new TranslationException("VerificationFailed", change.Id);
            }
        }
    }

    public static BuildReceipt BuildFile(TranslationProject project, string language, BuildMode mode, string output)
    {
        output = Path.GetFullPath(output);
        AtomicFiles.EnsureDifferent(project.SourcePath, output);
        if (!string.Equals(Path.GetExtension(output), ".exe", StringComparison.OrdinalIgnoreCase)) throw new TranslationException("ExeRequired");
        using var source = new FileStream(project.SourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var buffer = new MemoryStream(); source.CopyTo(buffer);
        var fresh = Extractor.Extract(buffer.ToArray(), project.SourcePath);
        var built = Build(project, fresh, language, mode);
        AtomicFiles.Write(output, built.Bytes, project.SourcePath);
        string? reportError = null;
        try
        {
            var report = new { schemaVersion = 1, built.SourceHash, built.OutputHash, language, mode = mode.ToString(), built.Changes,
                runtimeVerification = "Not performed. Test the generated executable in the game.", generatedUtc = DateTime.UtcNow };
            AtomicFiles.WriteJson(output + ".build.json", report, project.SourcePath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or TranslationException) { reportError = e.Message; }
        return new(output, built.OutputHash, built.Changes.Count, reportError);
    }
}
