using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows.Media;
using OstrivTranslate.Core;

namespace OstrivTranslate.App;

public sealed class EntryRow(TranslationEntry entry, Func<string> language, Func<BuildMode> mode, Action changed) : INotifyPropertyChanged
{
    public TranslationEntry Entry { get; } = entry;
    public string OriginalPreview => Preview(Entry.Original);
    public string TranslationPreview => Preview(Translation);
    private static string Preview(string text) => text.Replace("\r", "").Replace("\n", " ↵ ");
    public string Translation
    {
        get => Entry.GetTranslation(language());
        set
        {
            if (language().Length == 0 || value == Translation) return;
            Entry.Translations[language()] = value;
            Refresh(); changed();
        }
    }
    public bool Approved
    {
        get => Entry.Confirmed || Entry.Approved;
        set { if (Entry.Confirmed || Entry.Approved == value) return; Entry.Approved = value; Refresh(); changed(); }
    }
    public List<ValidationIssue> Issues => Validator.Validate(Entry, Translation, mode());
    public bool HasErrors => Issues.Any(i => !i.Warning);
    public string LengthText => $"{Translation.Length} / {Entry.Original.Length}";
    public string Status => HasErrors ? "⚠ " + L.T("Errors") : !Entry.Confirmed && !Entry.Approved ? L.T("Candidate") : Translation.Length == 0 ? L.T("Untranslated") : Translation.Length > Entry.Original.Length ? L.T("NeedsRelocation") : L.T("Translated");
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
}

public sealed class FontCoverage
{
    private readonly List<(string Name, HashSet<int> Characters)> fonts = [];
    public bool Available { get; private set; }
    public void Load(string executable)
    {
        fonts.Clear(); Available = true;
        var folder = Path.Combine(Path.GetDirectoryName(executable)!, "fonts");
        try
        {
            foreach (var file in Directory.EnumerateFiles(folder, "*.ttf"))
            {
                try { var typeface = new GlyphTypeface(new Uri(file)); fonts.Add((Path.GetFileNameWithoutExtension(file), typeface.CharacterToGlyphMap.Keys.ToHashSet())); }
                catch (Exception e) when (e is IOException or ArgumentException or NotSupportedException) { Available = false; }
            }
            Available &= fonts.Count > 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Available = false; }
    }
    public List<string> Missing(string value)
    {
        var characters = value.EnumerateRunes().Where(r => !Rune.IsControl(r) && !Rune.IsWhiteSpace(r)).Distinct().ToList();
        return fonts.Select(f => (f.Name, Missing: characters.Where(r => !f.Characters.Contains(r.Value)).ToList()))
            .Where(f => f.Missing.Count > 0).Select(f => f.Name + ": " + string.Join(" ", f.Missing.Take(30).Select(r => $"{r} (U+{r.Value:X4})")) + (f.Missing.Count > 30 ? " …" : "")).ToList();
    }
}
