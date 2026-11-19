using System.Text.Json;

namespace OstrivTranslate.Core;

public static class ProjectStore
{
    public static TranslationProject Load(string path)
    {
        try
        {
            if (new FileInfo(path).Length > 128 * 1024 * 1024) throw new TranslationException("InvalidProject");
            var project = JsonSerializer.Deserialize<TranslationProject>(File.ReadAllText(path), AtomicFiles.JsonOptions) ?? throw new TranslationException("InvalidProject");
            Validate(project);
            return project;
        }
        catch (Exception e) when (e is JsonException or NotSupportedException) { throw new TranslationException("InvalidProject"); }
    }
    public static void Save(string path, TranslationProject project)
    {
        Validate(project);
        if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) throw new TranslationException("JsonRequired");
        AtomicFiles.WriteJson(path, project, File.Exists(project.SourcePath) ? project.SourcePath : null);
    }
    public static void Validate(TranslationProject p)
    {
        if (p.SchemaVersion != 1) throw new TranslationException("ProjectVersion");
        if (p.Entries is null || p.Languages is null || p.SourcePath is null || p.SourceHash is null || p.SourceHash.Length != 64 || !p.SourceHash.All(Uri.IsHexDigit)) throw new TranslationException("InvalidProject");
        if (p.Languages.Any(l => l is null || !ValidLanguageId(l.Id) || string.IsNullOrWhiteSpace(l.Name)) || p.Languages.Select(l => l.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != p.Languages.Count) throw new TranslationException("InvalidLanguage");
        if (p.Entries.Any(e => e is null || string.IsNullOrWhiteSpace(e.Id) || e.Offset < 0 || e.Original is null || e.Original.Contains('\0') || e.Context is null || e.Translations is null || e.Translations.Any(t => !p.Languages.Any(l => l.Id == t.Key) || t.Value is null)) || p.Entries.Select(e => e.Id).Distinct().Count() != p.Entries.Count) throw new TranslationException("InvalidProject");
        p.SourceHash = p.SourceHash.ToLowerInvariant();
        foreach (var e in p.Entries)
        {
            e.Translations = new(e.Translations, StringComparer.OrdinalIgnoreCase);
            try { PeImage.Utf16.GetByteCount(e.Original); foreach (var t in e.Translations.Values) PeImage.Utf16.GetByteCount(t); }
            catch (System.Text.EncoderFallbackException) { throw new TranslationException("InvalidUnicode"); }
        }
    }
    public static bool ValidLanguageId(string? id) => !string.IsNullOrWhiteSpace(id) && id.Length <= 40 && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    public static TranslationProject BindToSource(TranslationProject saved, Extraction fresh)
    {
        if (saved.SourceHash != fresh.Project.SourceHash) throw new TranslationException("SourceChanged");
        var source = fresh.Project.Entries.ToDictionary(e => e.Id);
        foreach (var row in saved.Entries)
        {
            if (!source.TryGetValue(row.Id, out var entry) || entry.Offset != row.Offset || entry.Original != row.Original) throw new TranslationException("EntryMismatch", row.Id);
            entry.Translations = new(row.Translations, StringComparer.OrdinalIgnoreCase);
            entry.Approved = row.Approved;
        }
        fresh.Project.Languages = saved.Languages.Select(l => new TargetLanguage { Id = l.Id, Name = l.Name }).ToList();
        return fresh.Project;
    }
}

public sealed class ImportChange
{
    public bool Apply { get; set; }
    public string Language { get; set; } = "";
    public string Original { get; set; } = "";
    public string Translation { get; set; } = "";
    public string Existing { get; set; } = "";
    public string Reason { get; set; } = "";
    public List<ImportTarget> Choices { get; set; } = [];
    public ImportTarget? Target { get; set; }
}
public sealed record ImportTarget(string Id, string Label)
{
    public override string ToString() => Label;
}
public static class Importer
{
    public static List<ImportChange> Migration(TranslationProject old, TranslationProject fresh)
    {
        var changes = new List<ImportChange>();
        foreach (var source in old.Entries)
        foreach (var (language, translation) in source.Translations.Where(t => t.Value.Length > 0))
        {
            var exact = fresh.Entries.Where(e => e.Original == source.Original && e.Context == source.Context).ToList();
            var matches = exact.Count > 0 ? exact : fresh.Entries.Where(e => e.Original == source.Original).ToList();
            var row = new ImportChange { Language = language, Original = source.Original, Translation = translation,
                Reason = matches.Count == 0 ? "Unmatched" : matches.Count == 1 && exact.Count == 1 ? "MigrationExact" : "MigrationAmbiguous",
                Choices = matches.Select(e => new ImportTarget(e.Id, e.Id + " • " + e.Context)).ToList() };
            if (row.Choices.Count == 1) row.Target = row.Choices[0];
            row.Apply = row.Reason == "MigrationExact";
            changes.Add(row);
        }
        return changes;
    }
    public static void Apply(TranslationProject project, IEnumerable<ImportChange> changes)
    {
        var selected = changes.Where(c => c.Apply).ToList();
        var entries = project.Entries.ToDictionary(e => e.Id);
        var seen = new HashSet<(string, string)>();
        foreach (var c in selected)
        {
            if (c.Target is null || !c.Choices.Any(t => t.Id == c.Target.Id) || !entries.TryGetValue(c.Target.Id, out var entry) || entry.Original != c.Original || !ProjectStore.ValidLanguageId(c.Language)) throw new TranslationException("ImportSelection");
            if (!seen.Add((c.Target.Id, c.Language.ToLowerInvariant()))) throw new TranslationException("DuplicateCsv", c.Target.Id);
        }
        foreach (var c in selected)
        {
            var language = project.Languages.FirstOrDefault(l => l.Id.Equals(c.Language, StringComparison.OrdinalIgnoreCase));
            if (language is null) project.Languages.Add(language = new() { Id = c.Language, Name = c.Language });
            entries[c.Target!.Id].Translations[language.Id] = c.Translation;
        }
    }
}
