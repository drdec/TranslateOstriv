using System.Text;

namespace OstrivTranslate.Core;

public static class CsvExchange
{
    private static string Quote(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";
    public static byte[] Export(TranslationProject project)
    {
        var output = new StringBuilder("id,language,original,translation\r\n");
        foreach (var e in project.Entries)
        foreach (var l in project.Languages)
            output.AppendLine(string.Join(",", new[] { e.Id, l.Id, e.Original, e.GetTranslation(l.Id) }.Select(Quote)));
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(output.ToString())).ToArray();
    }
    public static List<ImportChange> Preview(string csv, TranslationProject project)
    {
        var rows = Parse(csv.TrimStart('\ufeff'));
        if (rows.Count == 0 || !rows[0].SequenceEqual(new[] { "id", "language", "original", "translation" })) throw new TranslationException("CsvHeader");
        var entries = project.Entries.ToDictionary(e => e.Id);
        var result = new List<ImportChange>(); var seen = new HashSet<(string, string)>();
        foreach (var row in rows.Skip(1))
        {
            if (row.Length != 4 || !ProjectStore.ValidLanguageId(row[1])) throw new TranslationException("InvalidCsv");
            if (!seen.Add((row[0], row[1].ToLowerInvariant()))) throw new TranslationException("DuplicateCsv", row[0]);
            if (!entries.TryGetValue(row[0], out var e) || e.Original != row[2])
            { result.Add(new() { Language = row[1], Original = row[2], Translation = row[3], Reason = "Unmatched" }); continue; }
            var existing = e.GetTranslation(row[1]);
            if (existing == row[3]) continue;
            var conflict = existing.Length > 0;
            var target = new ImportTarget(e.Id, e.Id + " • " + e.Context);
            result.Add(new() { Apply = !conflict, Language = row[1], Original = e.Original, Existing = existing, Translation = row[3], Reason = conflict ? "ImportConflict" : "ImportNew", Target = target, Choices = [target] });
        }
        return result;
    }
    public static List<string[]> Parse(string input)
    {
        var rows = new List<string[]>(); var row = new List<string>(); var field = new StringBuilder();
        var quoted = false; var closed = false; var atStart = true;
        for (var i = 0; i < input.Length; i++)
        {
            var c = input[i];
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < input.Length && input[i + 1] == '"') { field.Append('"'); i++; }
                    else { quoted = false; closed = true; }
                }
                else field.Append(c);
                continue;
            }
            if (c == '"' && atStart) { quoted = true; atStart = false; continue; }
            if (c is ',' or '\r' or '\n')
            {
                row.Add(field.ToString()); field.Clear(); atStart = true; closed = false;
                if (c != ',')
                {
                    rows.Add(row.ToArray()); row.Clear();
                    if (c == '\r' && i + 1 < input.Length && input[i + 1] == '\n') i++;
                }
            }
            else
            {
                if (closed || c == '"') throw new TranslationException("InvalidCsv");
                field.Append(c); atStart = false;
            }
        }
        if (quoted) throw new TranslationException("InvalidCsv");
        if (field.Length > 0 || row.Count > 0 || closed) { row.Add(field.ToString()); rows.Add(row.ToArray()); }
        return rows;
    }
}
