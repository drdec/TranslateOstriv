using System.Text;
using System.Text.RegularExpressions;

namespace OstrivTranslate.Core;

public static partial class Validator
{
    [GeneratedRegex(@"%(?:\d+\$)?[-+ #0]*(?:\d+|\*)?(?:\.(?:\d+|\*))?(?:hh|ll|I64|[hljztL])?[diuoxXfFeEgGaAcCsSpn%]|\{\d+(?:[^{}]*)\}|\[(?:/?[A-Za-z][^\]\r\n]*)\]|</?[A-Za-z][^>\r\n]*>")]
    private static partial Regex Tokens();
    public static List<ValidationIssue> Validate(TranslationEntry entry, string translation, BuildMode mode)
    {
        var issues = new List<ValidationIssue>();
        if (translation.Length == 0 || translation == entry.Original) return issues;
        if (translation.Contains('\0')) issues.Add(new("EmbeddedNull"));
        try { PeImage.Utf16.GetByteCount(translation); }
        catch (EncoderFallbackException) { issues.Add(new("InvalidUnicode")); }
        if (!Tokens().Matches(entry.Original).Select(m => m.Value).SequenceEqual(Tokens().Matches(translation).Select(m => m.Value))) issues.Add(new("TokensChanged"));
        if (mode == BuildMode.Strict && translation.Length > entry.Original.Length) issues.Add(new("TooLong", $"{translation.Length * 2} / {entry.OriginalBytes}"));
        if (!entry.Confirmed && !entry.Approved) issues.Add(new("Unconfirmed"));
        if (entry.RelocationIssue == "InteriorReference") issues.Add(new("InteriorReference"));
        if (entry.RelocationIssue == "SharedStorage") issues.Add(new("SharedStorage"));
        if (translation.Length > entry.Original.Length && entry.RelocationIssue.Length > 0 && entry.RelocationIssue is not ("InteriorReference" or "SharedStorage")) issues.Add(new(entry.RelocationIssue));
        return issues;
    }
}
