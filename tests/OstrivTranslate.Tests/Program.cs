using OstrivTranslate.Core;
using System.Text;
using System.Text.Json;
using System.Runtime.InteropServices;

if (args.Length == 4 && args[0] == "--validate-project")
{
    var saved = ProjectStore.Load(args[1]);
    var originalHash = Extractor.Hash(File.ReadAllBytes(saved.SourcePath));
    var extraction = Extractor.Extract(saved.SourcePath);
    var project = ProjectStore.BindToSource(saved, extraction);
    var language = project.Languages.Single().Id;
    var translated = project.Entries.Where(e => e.GetTranslation(language).Length > 0).ToList();
    var issues = translated.SelectMany(e => Validator.Validate(e, e.GetTranslation(language), BuildMode.Extended)
        .Select(issue => new { e.Id, issue.Code, issue.Detail, issue.Warning })).ToList();
    if (issues.Any(i => !i.Warning)) throw new Exception(JsonSerializer.Serialize(issues));
    if (project.Entries.Any(e => !e.Confirmed && (e.Approved || e.Translations.Count != 0)))
        throw new Exception("Draft must not enable or translate unconfirmed candidates");
    if (translated.Count != project.Entries.Count(e => e.Confirmed)) throw new Exception("Draft is incomplete");
    var empty = JsonSerializer.Deserialize<TranslationProject>(JsonSerializer.Serialize(project))!;
    foreach (var e in empty.Entries) e.Translations.Clear();
    var csv = File.ReadAllText(args[2], Encoding.UTF8);
    var preview = CsvExchange.Preview(csv, empty);
    if (preview.Count != translated.Count || preview.Any(c => !c.Apply)) throw new Exception("CSV preview mismatch");
    Importer.Apply(empty, preview);
    foreach (var e in project.Entries)
        if (empty.Entries.Single(r => r.Id == e.Id).GetTranslation(language) != e.GetTranslation(language))
            throw new Exception("CSV roundtrip mismatch: " + e.Id);
    var conflicting = empty.Entries.First(e => e.Confirmed);
    conflicting.Translations[language] = "Existing user edit";
    var conflicts = CsvExchange.Preview(csv, empty);
    if (conflicts.Count != 1 || conflicts[0].Apply || conflicts[0].Reason != "ImportConflict")
        throw new Exception("CSV did not protect an existing edit");
    var roundtripPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[3]))!, "work", "validated-roundtrip.json");
    ProjectStore.Save(roundtripPath, project);
    var roundtrip = ProjectStore.Load(roundtripPath);
    if (JsonSerializer.Serialize(roundtrip) != JsonSerializer.Serialize(project)) throw new Exception("JSON roundtrip mismatch");
    var built = Builder.Build(project, extraction, language, BuildMode.Extended);
    var output = new PeImage(built.Bytes);
    if (originalHash != Extractor.Hash(File.ReadAllBytes(saved.SourcePath))) throw new Exception("Original changed");
    var report = new
    {
        Language = language, project.SourceHash, ProjectSha256 = Extractor.Hash(File.ReadAllBytes(args[1])),
        CsvSha256 = Extractor.Hash(File.ReadAllBytes(args[2])),
        Confirmed = project.Entries.Count(e => e.Confirmed), Translated = translated.Count,
        Candidates = project.Entries.Count(e => !e.Confirmed),
        IdenticalToOriginal = translated.Count(e => e.GetTranslation(language) == e.Original),
        LongerThanOriginal = translated.Count(e => e.GetTranslation(language).Length > e.Original.Length),
        ValidationIssues = issues, JsonRoundtrip = "PASS", CsvRoundtrip = "PASS", CsvConflictProtection = "PASS",
        ExtendedBuild = "PASS (in memory, no EXE written)", ChangedStrings = built.Changes.Count,
        RelocatedStrings = built.Changes.Count(c => c.TargetRva != extraction.Image.OffsetToRva(c.SourceOffset)),
        OriginalBytes = extraction.Image.Bytes.Length, OutputBytes = built.Bytes.Length, built.OutputHash,
        NewSection = output.Sections.Last().Name,
        OriginalUnchanged = true, GameVisualVerification = "NOT PERFORMED"
    };
    AtomicFiles.WriteJson(args[3], report, saved.SourcePath);
    Console.WriteLine(JsonSerializer.Serialize(report, AtomicFiles.JsonOptions));
    return;
}

if (args.Length > 0 && args[0] == "--inspect")
{
    var extracted = Extractor.Extract(args[1]);
    Console.WriteLine($"Hash: {extracted.Project.SourceHash}\nConfirmed: {extracted.Project.Entries.Count(e => e.Confirmed)}\nCandidates: {extracted.Project.Entries.Count(e => !e.Confirmed)}\nReferences: {extracted.References.Count}");
    foreach (var text in new[] { "Українська", "Колодязь", "Криниця", "Вода (для населення)" })
        foreach (var e in extracted.Project.Entries.Where(e => e.Original == text)) Console.WriteLine($"{e.Id}: {e.Original} | {e.Context} | confirmed={e.Confirmed} refs={e.ReferenceCount} {e.RelocationIssue}");
    extracted.Project.Languages.Add(new() { Id = "be", Name = "Беларуская" });
    var selected = extracted.Project.Entries.Where(e => e.Confirmed && e.RelocationIssue == "" && e.Original is "Українська" or "Колодязь" or "Криниця").Concat(extracted.Project.Entries.Where(e => e.Confirmed && e.RelocationIssue == "" && e.Original.Length > 500).Take(4)).ToList();
    foreach (var e in selected) e.Translations["be"] = e.Original + " — праверка доўгага перакладу";
    var built = Builder.Build(extracted.Project, extracted, "be", BuildMode.Extended);
    Console.WriteLine($"Extended verified: {built.Changes.Count} strings; {extracted.Image.Bytes.Length} -> {built.Bytes.Length} bytes; {built.OutputHash}");
    foreach (var c in built.Changes) Console.WriteLine($"Moved {c.Id} to RVA 0x{c.TargetRva:x} ({c.Translation.Length} UTF-16 units, {c.ReferenceCount} references)");
    if (args.Length > 2) { AtomicFiles.Write(args[2], built.Bytes, args[1]); Console.WriteLine("Saved: " + args[2]); }
    return;
}
var passed = 0; var failed = 0;
var folder = Path.Combine(Environment.CurrentDirectory, "artifacts", "tests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
void Test(string name, Action test)
{
    try { test(); Console.WriteLine("PASS " + name); passed++; }
    catch (Exception ex) { Console.WriteLine("FAIL " + name + "\n" + ex); failed++; }
}
void Check(bool condition, string message = "Assertion failed") { if (!condition) throw new Exception(message); }
void Reject(string code, Action test)
{
    try { test(); } catch (TranslationException ex) when (ex.Code == code) { return; }
    throw new Exception("Expected rejection: " + code);
}
TranslationEntry Entry(string text = "Вода") => new() { Id = "uk-00000020", Offset = 32, Original = text, Context = "Water", Confirmed = true, ReferenceCount = 1 };
TranslationProject Project() => new() { SourceHash = new string('a', 64), Languages = [new() { Id = "be", Name = "Беларуская" }, new() { Id = "pl", Name = "Polski" }], Entries = [Entry()] };
TranslationProject Clone(TranslationProject p) => JsonSerializer.Deserialize<TranslationProject>(JsonSerializer.Serialize(p))!;
Test("Empty translation preserves original", () => Check(Validator.Validate(Entry(), "", BuildMode.Strict).Count == 0));
Test("Equal byte length accepted", () => Check(Validator.Validate(Entry(), "Вада", BuildMode.Strict).Count == 0));
Test("Strict UTF-16 boundary", () => Check(Validator.Validate(Entry(), "Вада!", BuildMode.Strict).Any(i => i.Code == "TooLong")));
Test("Surrogate pair counts as two UTF-16 units", () => Check(Validator.Validate(Entry("a"), "😀", BuildMode.Strict).Any(i => i.Code == "TooLong")));
Test("Valid non-BMP text accepted in extended mode", () => Check(Validator.Validate(Entry(), "Hello 😀 中文 العربية", BuildMode.Extended).Count == 0));
Test("Unpaired surrogate rejected", () => Check(Validator.Validate(Entry(), "\ud800", BuildMode.Extended).Any(i => i.Code == "InvalidUnicode")));
Test("Embedded NUL rejected", () => Check(Validator.Validate(Entry(), "a\0b", BuildMode.Extended).Any(i => i.Code == "EmbeddedNull")));
Test("Format tokens and tags preserved", () => Check(Validator.Validate(Entry("[b]%d[/b] {0:N2}"), "[b]%d[/b] {0:N2} вада", BuildMode.Extended).Count == 0));
Test("Format mismatch rejected", () => Check(Validator.Validate(Entry("%d [b]a[/b]"), "%s [b]b[/b]", BuildMode.Extended).Any(i => i.Code == "TokensChanged")));
Test("Interior references block short patches too", () => { var e = Entry(); e.RelocationIssue = "InteriorReference"; Check(Validator.Validate(e, "В", BuildMode.Extended).Any(i => i.Code == "InteriorReference")); });
Test("Shared suffix cannot silently change another label", () => { var e = Entry(); e.RelocationIssue = "SharedStorage"; Check(Validator.Validate(e, "В", BuildMode.Strict).Any(i => i.Code == "SharedStorage")); Check(Validator.Validate(e, "Доўгі пераклад", BuildMode.Extended).Any(i => i.Code == "SharedStorage")); });
Test("Unconfirmed candidate blocked", () => { var e = Entry(); e.Confirmed = false; Check(Validator.Validate(e, "Вада", BuildMode.Strict).Any(i => i.Code == "Unconfirmed")); e.Approved = true; Check(Validator.Validate(e, "Вада", BuildMode.Strict).Count == 0); });
Test("Unknown references block extension", () => { var e = Entry(); e.RelocationIssue = "NoReferences"; Check(Validator.Validate(e, "Пітная вада", BuildMode.Extended).Any(i => i.Code == "NoReferences")); });
Test("CSV multilingual roundtrip with quotes and newlines", () =>
{
    var p = Project(); p.Entries[0].Original = "Вода, \"тест\"\r\nрядок"; p.Entries[0].Translations["be"] = "Вада, \"тэст\"\nновы радок"; p.Entries[0].Translations["pl"] = "Woda";
    var fresh = Clone(p); fresh.Entries[0].Translations.Clear(); var preview = CsvExchange.Preview(Encoding.UTF8.GetString(CsvExchange.Export(p)), fresh);
    Check(preview.Count == 2 && preview.All(c => c.Apply)); Importer.Apply(fresh, preview); Check(fresh.Entries[0].GetTranslation("be") == p.Entries[0].GetTranslation("be"));
});
Test("CSV conflicts require explicit selection", () => { var p = Project(); p.Entries[0].Translations["be"] = "Вада"; var incoming = Clone(p); incoming.Entries[0].Translations["be"] = "Вадзіца"; var changes = CsvExchange.Preview(Encoding.UTF8.GetString(CsvExchange.Export(incoming)), p); Check(changes.Count == 1 && !changes[0].Apply && changes[0].Reason == "ImportConflict"); Importer.Apply(p, changes); Check(p.Entries[0].GetTranslation("be") == "Вада"); });
Test("CSV mismatched source cannot be forced", () => { var p = Project(); var changes = CsvExchange.Preview("id,language,original,translation\nuk-00000020,be,wrong,translated", p); Check(changes[0].Reason == "Unmatched"); changes[0].Apply = true; Reject("ImportSelection", () => Importer.Apply(p, changes)); });
Test("CSV duplicate language IDs rejected case-insensitively", () => Reject("DuplicateCsv", () => CsvExchange.Preview("id,language,original,translation\na,be,a,b\na,BE,a,c", Project())));
Test("CSV malformed quote rejected", () => Reject("InvalidCsv", () => CsvExchange.Parse("a,\"unterminated")));
Test("CSV garbage after quote rejected", () => Reject("InvalidCsv", () => CsvExchange.Parse("\"a\"oops,b")));
Test("CSV last empty cell preserved", () => Check(CsvExchange.Parse("a,b,")[0].SequenceEqual(new[] { "a", "b", "" })));
Test("CSV header checked", () => Reject("CsvHeader", () => CsvExchange.Preview("original,translation", Project())));
Test("Atomic project save and backup", () => { var p = Project(); var path = Path.Combine(folder, "project.json"); ProjectStore.Save(path, p); p.Entries[0].Translations["be"] = "Вада"; ProjectStore.Save(path, p); Check(ProjectStore.Load(path).Entries[0].GetTranslation("be") == "Вада"); Check(Directory.GetFiles(folder, "project.json.bak-*").Length == 1); });
Test("Corrupt JSON rejected", () => { var path = Path.Combine(folder, "corrupt.json"); File.WriteAllText(path, "{broken"); Reject("InvalidProject", () => ProjectStore.Load(path)); });
Test("Unknown schema rejected", () => { var p = Project(); p.SchemaVersion = 99; Reject("ProjectVersion", () => ProjectStore.Validate(p)); });
Test("Duplicate project IDs rejected", () => { var p = Project(); p.Entries.Add(Entry()); Reject("InvalidProject", () => ProjectStore.Validate(p)); });
Test("Invalid Unicode cannot be silently replaced on save", () => { var p = Project(); p.Entries[0].Translations["be"] = "\ud800"; Reject("InvalidUnicode", () => ProjectStore.Validate(p)); });
Test("Automatic migration requires exact context", () => { var old = Project(); old.Entries[0].Translations["be"] = "Вада"; var fresh = Project(); fresh.Entries[0].Id = "new"; var changes = Importer.Migration(old, fresh); Check(changes.Count == 1 && changes[0].Apply); Importer.Apply(fresh, changes); Check(fresh.Entries[0].GetTranslation("be") == "Вада"); });
Test("Ambiguous migration keeps choices unchecked", () => { var old = Project(); old.Entries[0].Translations["be"] = "Вада"; var fresh = Project(); fresh.Entries[0].Context = "other"; var other = Entry(); other.Id = "other"; other.Context = "another"; fresh.Entries.Add(other); var changes = Importer.Migration(old, fresh); Check(!changes[0].Apply && changes[0].Target is null && changes[0].Choices.Count == 2); changes[0].Target = changes[0].Choices[1]; changes[0].Apply = true; Importer.Apply(fresh, changes); Check(fresh.Entries[1].GetTranslation("be") == "Вада"); });
Test("Unmatched migration is visible", () => { var old = Project(); old.Entries[0].Translations["be"] = "Вада"; var fresh = Project(); fresh.Entries.Clear(); var changes = Importer.Migration(old, fresh); Check(changes.Count == 1 && changes[0].Reason == "Unmatched" && !changes[0].Apply); });
Test("Invalid PE rejected", () => Reject("InvalidPe", () => new PeImage(new byte[512])));
Test("Source path cannot be overwritten", () => { var path = Path.Combine(folder, "original.exe"); File.WriteAllText(path, "original"); Reject("OriginalProtected", () => AtomicFiles.Write(path, [1], path)); Check(File.ReadAllText(path) == "original"); });
if (OperatingSystem.IsWindows())
{
    Test("Hard link to original cannot be overwritten", () =>
    {
        var source = Path.Combine(folder, "hardlink-source.exe"); var alias = Path.Combine(folder, "hardlink-output.exe"); File.WriteAllText(source, "original");
        Check(Native.CreateHardLink(alias, source, IntPtr.Zero), "CreateHardLink failed: " + Marshal.GetLastWin32Error());
        Reject("OriginalProtected", () => AtomicFiles.Write(alias, [1, 2], source)); Check(File.ReadAllText(source) == "original");
    });
    Test("Locked output leaves previous file intact", () =>
    {
        var path = Path.Combine(folder, "locked.exe"); File.WriteAllText(path, "old");
        using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var rejected = false; try { AtomicFiles.Write(path, [1, 2]); } catch (IOException) { rejected = true; }
            Check(rejected);
        }
        Check(File.ReadAllText(path) == "old"); Check(Directory.GetFiles(folder, "locked.exe.tmp-*").Length == 0);
    });
}
var game = args.FirstOrDefault() ?? @"E:\steam\steamapps\common\Ostriv\ostriv.exe";
if (File.Exists(game))
{
    var originalHash = Extractor.Hash(File.ReadAllBytes(game));
    var extracted = Extractor.Extract(game);
    TranslationProject GameProject() { var p = Clone(extracted.Project); p.Languages.Add(new() { Id = "be", Name = "Беларуская" }); return p; }
    Test("Real extraction confirms UI and long descriptions", () =>
    {
        Check(extracted.Project.Entries.Count(e => e.Confirmed) >= 2085);
        Check(extracted.Project.Entries.Any(e => e.Confirmed && e.Original == "Колодязь" && e.Context == "Stone well"));
        Check(extracted.Project.Entries.Any(e => e.Confirmed && e.Original.Length > 2000));
        Check(extracted.Project.Entries.Any(e => e.Original.Length < 7));
        Check(extracted.Project.Entries.Any(e => e.Original.Contains('\n')));
        Check(!extracted.Project.Entries.Any(e => e.Original == "Працівники" && e.Offset == 0x4f43ac));
    });
    Test("Strict patch preserves size, outside bytes and terminator", () =>
    {
        var p = GameProject(); var e = p.Entries.First(e => e.Original == "Колодязь"); e.Translations["be"] = "Вада";
        var built = Builder.Build(p, extracted, "be", BuildMode.Strict);
        Check(built.Bytes.Length == extracted.Image.Bytes.Length);
        Check(new PeImage(built.Bytes).ReadUtf16(e.Offset) == "Вада");
        for (var i = 0; i < built.Bytes.Length; i++) if (i < e.Offset || i >= e.Offset + e.OriginalBytes + 2) Check(built.Bytes[i] == extracted.Image.Bytes[i]);
    });
    Test("Extended relocation of labels, long paragraphs and multiple references", () =>
    {
        var p = GameProject();
        var targets = p.Entries.Where(e => e.Confirmed && e.Original is "Українська" or "Колодязь" or "Криниця").Concat(p.Entries.Where(e => e.Confirmed && e.RelocationIssue == "" && e.Original.Length > 1000).Take(3)).ToList();
        foreach (var e in targets) e.Translations["be"] = e.Original + " — праверка перакладу: ў і ё 😀";
        var built = Builder.Build(p, extracted, "be", BuildMode.Extended); var pe = new PeImage(built.Bytes);
        Check(built.Bytes.Length > extracted.Image.Bytes.Length && pe.Sections.Last().Name == ".otrl");
        Check(built.Changes.Count == targets.Count && built.Changes.Any(c => c.ReferenceCount > 1));
        foreach (var c in built.Changes) { Check(pe.ReadUtf16(pe.RvaToOffset(c.TargetRva)) == c.Translation); Check(pe.ReadUtf16(c.SourceOffset) == c.Original); }
        var repeated = Builder.Build(p, extracted, "be", BuildMode.Extended); Check(built.Bytes.SequenceEqual(repeated.Bytes), "Build must be deterministic");
        var receipt = Builder.BuildFile(p, "be", BuildMode.Extended, Path.Combine(folder, "ostriv_extended_test.exe"));
        Check(File.Exists(receipt.OutputPath + ".build.json") && receipt.ReportError is null);
    });
    Test("Strict mode rejects long real translation", () => { var p = GameProject(); p.Entries.First(e => e.Original == "Колодязь").Translations["be"] = "Каменны калодзеж"; Reject("TooLong", () => Builder.Build(p, extracted, "be", BuildMode.Strict)); });
    Test("Forged source hash rejected", () => { var p = GameProject(); p.SourceHash = new string('0', 64); Reject("SourceChanged", () => Builder.Build(p, extracted, "be", BuildMode.Extended)); });
    Test("Imported offsets cannot redirect writes", () => { var p = GameProject(); var e = p.Entries.First(e => e.Original == "Колодязь"); e.Translations["be"] = "Вада"; e.Offset++; Reject("EntryMismatch", () => Builder.Build(p, extracted, "be", BuildMode.Strict)); });
    Test("Imported confidence cannot approve a candidate", () => { var p = GameProject(); var e = p.Entries.First(e => !e.Confirmed); e.Translations["be"] = "a"; e.Confirmed = true; Reject("Unconfirmed", () => Builder.Build(p, extracted, "be", BuildMode.Strict)); });
    Test("Bind ignores imported reference data", () => { var p = GameProject(); var entry = p.Entries.First(e => e.Original == "Колодязь"); entry.ReferenceCount = 999; var fresh = Extractor.Extract(game); var bound = ProjectStore.BindToSource(p, fresh); Check(bound.Entries.First(e => e.Id == entry.Id).ReferenceCount == 1); });
    Test("Original game hash unchanged", () => Check(Extractor.Hash(File.ReadAllBytes(game)) == originalHash));
    Console.WriteLine($"Real EXE: {extracted.Project.Entries.Count(e => e.Confirmed)} confirmed; {extracted.Project.Entries.Count(e => !e.Confirmed)} candidates.");
}
else Console.WriteLine("SKIP real EXE tests: pass the original ostriv.exe path to enable them.");
Console.WriteLine($"\n{passed} passed, {failed} failed. Artifacts: {folder}\nGame runtime: NOT TESTED by this harness.");
Environment.ExitCode = failed == 0 ? 0 : 1;

static class Native
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CreateHardLink(string fileName, string existingFileName, IntPtr securityAttributes);
}
