using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Lipi.Services;

public readonly record struct ModerationResult(string Text, bool Flagged, bool Blocked);

/// <summary>
/// Reusable local text moderation engine for transcript/translation streams.
/// Loads blocked terms from `Moderation/BlockedTerms/*.txt` and applies language-scoped removal.
/// </summary>
public sealed class LocalTextModerationEngine
{
    private readonly List<Regex> _globalPatterns;
    private readonly string[] _globalCanonicalTerms;
    private readonly Dictionary<string, List<Regex>> _patternsByLanguage;
    private readonly Dictionary<string, string[]> _canonicalTermsByLanguage;

    private static readonly Regex SecondaryTokenRegex = new(@"[\p{L}\p{M}\p{Nd}_'-]+", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public LocalTextModerationEngine(string blockedTermsRelativePath = "Moderation/BlockedTerms")
    {
        var termsByLanguage = LoadLocalBlockedTermsByLanguage(blockedTermsRelativePath);

        _patternsByLanguage = termsByLanguage.ToDictionary(
            pair => pair.Key,
            pair => pair.Value
                .Where(static t => !string.IsNullOrWhiteSpace(t))
                .Select(static t => t.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(BuildBlockedRegex)
                .Where(static r => r != null)
                .Cast<Regex>()
                .ToList(),
            StringComparer.OrdinalIgnoreCase);

        _canonicalTermsByLanguage = termsByLanguage.ToDictionary(
            pair => pair.Key,
            pair => pair.Value
                .Where(static t => !string.IsNullOrWhiteSpace(t))
                .Select(CanonicalizeForModeration)
                .Where(static t => !string.IsNullOrWhiteSpace(t))
                .Distinct(StringComparer.Ordinal)
                .OrderByDescending(static t => t.Length)
                .ToArray(),
            StringComparer.OrdinalIgnoreCase);

        var merged = termsByLanguage.Values
            .SelectMany(static terms => terms)
            .Where(static t => !string.IsNullOrWhiteSpace(t))
            .Select(static t => t.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        _globalPatterns = merged
            .Select(BuildBlockedRegex)
            .Where(static r => r != null)
            .Cast<Regex>()
            .ToList();

        _globalCanonicalTerms = merged
            .Select(CanonicalizeForModeration)
            .Where(static t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(static t => t.Length)
            .ToArray();
    }

    public ModerationResult Moderate(string text, IEnumerable<string>? languages = null)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new ModerationResult(text, false, false);

        var patterns = _globalPatterns;
        IReadOnlyCollection<string> canonicalTerms = _globalCanonicalTerms;

        var languageKeys = ResolveLanguageKeys(languages);
        if (languageKeys.Count > 0)
        {
            var scopedPatterns = new List<Regex>();
            var scopedCanonicalTerms = new List<string>();

            foreach (var key in languageKeys)
            {
                if (_patternsByLanguage.TryGetValue(key, out var langPatterns))
                    scopedPatterns.AddRange(langPatterns);

                if (_canonicalTermsByLanguage.TryGetValue(key, out var langCanonicalTerms))
                    scopedCanonicalTerms.AddRange(langCanonicalTerms);
            }

            if (scopedPatterns.Count > 0)
                patterns = scopedPatterns;

            if (scopedCanonicalTerms.Count > 0)
            {
                canonicalTerms = scopedCanonicalTerms
                    .Distinct(StringComparer.Ordinal)
                    .OrderByDescending(static t => t.Length)
                    .ToArray();
            }
        }

        var output = text;
        var flagged = false;

        foreach (var regex in patterns)
        {
            if (!regex.IsMatch(output))
                continue;

            flagged = true;
            output = regex.Replace(output, string.Empty);
        }

        if (ContainsBlockedCanonicalTerm(output, canonicalTerms))
        {
            flagged = true;
            output = ApplySecondaryTokenModeration(output, canonicalTerms);
        }

        output = Regex.Replace(output, "\\s+", " ").Trim();
        output = RemoveStandalonePunctuationTokens(output);
        output = Regex.Replace(output, "\\s+", " ").Trim();
        return new ModerationResult(output, flagged, false);
    }

    public Dictionary<string, string> ModerateTranslations(IReadOnlyDictionary<string, string> translations)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in translations)
        {
            var moderated = Moderate(pair.Value ?? string.Empty, [pair.Key]);
            if (!string.IsNullOrWhiteSpace(moderated.Text))
                result[pair.Key] = moderated.Text;
        }

        return result;
    }

    private static HashSet<string> ResolveLanguageKeys(IEnumerable<string>? languages)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (languages == null)
            return result;

        foreach (var language in languages)
        {
            if (string.IsNullOrWhiteSpace(language))
                continue;

            var trimmed = language.Trim().ToLowerInvariant();
            if (trimmed.Length == 0)
                continue;

            result.Add(trimmed);

            var shortCode = trimmed.Split('-', StringSplitOptions.RemoveEmptyEntries)[0];
            if (!string.IsNullOrWhiteSpace(shortCode))
            {
                result.Add(shortCode);
                if (shortCode.Equals("hi", StringComparison.OrdinalIgnoreCase))
                    result.Add("hi-translit");
            }
        }

        return result;
    }

    private static Dictionary<string, List<string>> LoadLocalBlockedTermsByLanguage(string blockedTermsRelativePath)
    {
        try
        {
            var relativePath = blockedTermsRelativePath
                .Replace('\\', Path.DirectorySeparatorChar)
                .Replace('/', Path.DirectorySeparatorChar);

            var candidates = new[]
            {
                Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, relativePath)),
                Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), relativePath))
            }
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

            var fullPath = candidates.FirstOrDefault(Directory.Exists);
            if (string.IsNullOrWhiteSpace(fullPath))
                return new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            var files = Directory.GetFiles(fullPath, "*.txt", SearchOption.TopDirectoryOnly);
            var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var file in files)
            {
                var languageKey = Path.GetFileNameWithoutExtension(file).Trim().ToLowerInvariant();
                if (string.IsNullOrWhiteSpace(languageKey))
                    continue;

                if (!result.TryGetValue(languageKey, out var terms))
                {
                    terms = [];
                    result[languageKey] = terms;
                }

                foreach (var line in File.ReadLines(file))
                {
                    var trimmed = line.Trim();
                    if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith('#'))
                        continue;
                    terms.Add(trimmed);
                }
            }

            return result;
        }
        catch
        {
            return new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static Regex? BuildBlockedRegex(string term)
    {
        var canonical = new string(term
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());

        if (string.IsNullOrWhiteSpace(canonical))
            return null;

        var letters = canonical.Select(c => Regex.Escape(c.ToString()));
        var fuzzyCore = string.Join("[\\W_]*", letters);

        var pattern = $@"\b{fuzzyCore}[a-z]*\b";
        return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    }

    private static string ApplySecondaryTokenModeration(string text, IReadOnlyCollection<string> canonicalTerms)
    {
        if (string.IsNullOrWhiteSpace(text) || canonicalTerms.Count == 0)
            return text;

        return SecondaryTokenRegex.Replace(text, m =>
            ContainsBlockedCanonicalTerm(m.Value, canonicalTerms) ? string.Empty : m.Value);
    }

    private static string RemoveStandalonePunctuationTokens(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        // Remove punctuation-only tokens left behind after term removal, e.g. "hello . world".
        return Regex.Replace(text, @"(?<=^|\s)[\p{P}\p{S}]+(?=\s|$)", string.Empty);
    }

    private static bool ContainsBlockedCanonicalTerm(string value, IReadOnlyCollection<string> canonicalTerms)
    {
        if (string.IsNullOrWhiteSpace(value) || canonicalTerms.Count == 0)
            return false;

        var canonical = CanonicalizeForModeration(value);
        if (string.IsNullOrWhiteSpace(canonical))
            return false;

        foreach (var term in canonicalTerms)
        {
            if (canonical.Contains(term, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static string CanonicalizeForModeration(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        var normalized = input.Normalize(NormalizationForm.FormKD);
        var sb = new StringBuilder(normalized.Length);

        foreach (var ch in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark)
                continue;

            var mapped = MapConfusableOrLeet(ch);
            if (char.IsLetterOrDigit(mapped))
                sb.Append(char.ToLowerInvariant(mapped));
        }

        return sb.ToString();
    }

    private static char MapConfusableOrLeet(char c)
    {
        return char.ToLowerInvariant(c) switch
        {
            '0' => 'o',
            '1' => 'i',
            '3' => 'e',
            '4' => 'a',
            '5' => 's',
            '7' => 't',
            '@' => 'a',
            '$' => 's',
            _ => c
        };
    }
}
