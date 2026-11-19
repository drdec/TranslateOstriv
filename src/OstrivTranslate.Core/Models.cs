using System.Text.Json.Serialization;

namespace OstrivTranslate.Core;

public sealed class TranslationProject
{
    public int SchemaVersion { get; set; } = 1;
    public string SourcePath { get; set; } = "";
    public string SourceHash { get; set; } = "";
    public string Profile { get; set; } = "";
    public List<TargetLanguage> Languages { get; set; } = [];
    public List<TranslationEntry> Entries { get; set; } = [];
}

public sealed class TargetLanguage
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public override string ToString() => Name;
}

public sealed class TranslationEntry
{
    public string Id { get; set; } = "";
    public int Offset { get; set; }
    public string Original { get; set; } = "";
    public string Context { get; set; } = "";
    public bool Confirmed { get; set; }
    public bool Approved { get; set; }
    public int ReferenceCount { get; set; }
    public string RelocationIssue { get; set; } = "";
    public Dictionary<string, string> Translations { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    [JsonIgnore] public int OriginalBytes => checked(Original.Length * 2);
    public string GetTranslation(string language) => Translations.GetValueOrDefault(language, "");
}

public enum BuildMode { Extended, Strict }
public sealed record ValidationIssue(string Code, string Detail = "", bool Warning = false);
public sealed class TranslationException(string code, string detail = "") : Exception(code + (detail.Length > 0 ? ": " + detail : ""))
{
    public string Code { get; } = code;
    public string Detail { get; } = detail;
}

public enum ReferenceKind { RipRelative, Absolute64 }
public sealed record StringReference(int PatchOffset, int InstructionEndRva, int TargetRva, ReferenceKind Kind);
public sealed record Extraction(TranslationProject Project, PeImage Image, IReadOnlyList<StringReference> References);
