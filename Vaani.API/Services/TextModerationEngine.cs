using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Vaani.API.Services;

internal sealed class TextModerationEngine
{
    private readonly string _contentModerationMode;
    private readonly bool _enableSecondaryContentModeration;
    private readonly List<Regex> _blockedPatterns;
    private readonly string[] _blockedCanonicalTerms;
    private readonly Dictionary<string, List<Regex>> _blockedPatternsByLanguage;
    private readonly Dictionary<string, string[]> _blockedCanonicalTermsByLanguage;
    private readonly ILogger _logger;

    private static readonly Regex SecondaryTokenRegex = new(@"[\p{L}\p{M}\p{Nd}_'-]+", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public TextModerationEngine(IConfiguration configuration, ILogger logger)
    {
        _logger = logger;
        _contentModerationMode = (configuration.GetValue<string>("Translation:ContentModerationMode", "Remove") ?? "Remove").Trim();
        _enableSecondaryContentModeration = configuration.GetValue<bool>("Translation:EnableSecondaryContentModeration", true);

        var localTermsByLanguage = LoadLocalBlockedTermsByLanguage(configuration);
        var localTerms = localTermsByLanguage.Values.SelectMany(static terms => terms).ToArray();
        var mergedTerms = localTerms
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        _blockedPatterns = mergedTerms
            .Select(BuildBlockedRegex)
            .Where(r => r != null)
            .Cast<Regex>()
            .ToList();

        _blockedCanonicalTerms = mergedTerms
            .Select(CanonicalizeForModeration)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(t => t.Length)
            .ToArray();

        _blockedPatternsByLanguage = localTermsByLanguage.ToDictionary(
            pair => pair.Key,
            pair => pair.Value
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(BuildBlockedRegex)
                .Where(r => r != null)
                .Cast<Regex>()
                .ToList(),
            StringComparer.OrdinalIgnoreCase);

        _blockedCanonicalTermsByLanguage = localTermsByLanguage.ToDictionary(
            pair => pair.Key,
            pair => pair.Value
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(CanonicalizeForModeration)
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Distinct(StringComparer.Ordinal)
                .OrderByDescending(t => t.Length)
                .ToArray(),
            StringComparer.OrdinalIgnoreCase);

        _logger.LogInformation("Moderation dictionaries loaded: {LanguageCount} language files, {PatternCount} patterns.",
            _blockedPatternsByLanguage.Count,
            _blockedPatterns.Count);
    }

    public (string Text, bool Flagged, bool Blocked) ModerateText(string text)
        => ModerateText(text, null);

    public (string Text, bool Flagged, bool Blocked) ModerateText(string text, IEnumerable<string>? languages)
    {
        if (string.IsNullOrWhiteSpace(text))
            return (text, false, false);

        var scopedPatterns = _blockedPatterns;
        var scopedCanonicalTerms = _blockedCanonicalTerms;

        var languageKeys = ResolveLanguageKeys(languages);
        if (languageKeys.Count > 0)
        {
            var languagePatterns = new List<Regex>();
            var languageCanonicalTerms = new List<string>();

            foreach (var key in languageKeys)
            {
                if (_blockedPatternsByLanguage.TryGetValue(key, out var patterns))
                    languagePatterns.AddRange(patterns);

                if (_blockedCanonicalTermsByLanguage.TryGetValue(key, out var canonicalTerms))
                    languageCanonicalTerms.AddRange(canonicalTerms);
            }

            if (languagePatterns.Count > 0)
                scopedPatterns = languagePatterns;

            if (languageCanonicalTerms.Count > 0)
                scopedCanonicalTerms = languageCanonicalTerms
                    .Distinct(StringComparer.Ordinal)
                    .OrderByDescending(static t => t.Length)
                    .ToArray();
        }

        var output = text;
        var flagged = false;

        foreach (var regex in scopedPatterns)
        {
            if (!regex.IsMatch(output))
                continue;

            flagged = true;

            if (_contentModerationMode.Equals("Block", StringComparison.OrdinalIgnoreCase))
                return (string.Empty, true, true);

            if (_contentModerationMode.Equals("Remove", StringComparison.OrdinalIgnoreCase))
            {
                output = regex.Replace(output, string.Empty);
            }
            else if (_contentModerationMode.Equals("Mask", StringComparison.OrdinalIgnoreCase))
            {
                output = regex.Replace(output, "***");
            }
            else
            {
                output = regex.Replace(output, string.Empty);
            }
        }

        if (_enableSecondaryContentModeration && ContainsBlockedCanonicalTerm(output, scopedCanonicalTerms))
        {
            flagged = true;

            if (_contentModerationMode.Equals("Block", StringComparison.OrdinalIgnoreCase))
                return (string.Empty, true, true);

            output = ApplySecondaryTokenModeration(output, scopedCanonicalTerms);
        }

        output = Regex.Replace(output, "\\s+", " ").Trim();
        output = RemoveStandalonePunctuationTokens(output);
        output = Regex.Replace(output, "\\s+", " ").Trim();
        return (output, flagged, false);
    }

    private string ApplySecondaryTokenModeration(string text, IReadOnlyCollection<string> canonicalTerms)
    {
        if (string.IsNullOrWhiteSpace(text) || canonicalTerms.Count == 0)
            return text;

        var remove = !_contentModerationMode.Equals("Mask", StringComparison.OrdinalIgnoreCase);
        return SecondaryTokenRegex.Replace(text, m =>
        {
            if (!ContainsBlockedCanonicalTerm(m.Value, canonicalTerms))
                return m.Value;

            return remove ? string.Empty : "***";
        });
    }

    private bool ContainsBlockedCanonicalTerm(string value, IReadOnlyCollection<string> canonicalTerms)
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

    private Dictionary<string, List<string>> LoadLocalBlockedTermsByLanguage(IConfiguration configuration)
    {
        try
        {
            var folder = (configuration.GetValue<string>("Translation:BlockedTermsDirectory", "Moderation/BlockedTerms")
                ?? "Moderation/BlockedTerms")
                .Replace('\\', Path.DirectorySeparatorChar)
                .Replace('/', Path.DirectorySeparatorChar);

            var candidates = new[]
            {
                Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, folder)),
                Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), folder))
            }
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

            var fullPath = candidates.FirstOrDefault(Directory.Exists);

            if (string.IsNullOrWhiteSpace(fullPath))
            {
                _logger.LogWarning("Blocked terms directory not found. Checked: {Candidates}", string.Join(", ", candidates));
                return new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            }

            var files = Directory.GetFiles(fullPath, "*.txt", SearchOption.TopDirectoryOnly);
            var termsByLanguage = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var file in files)
            {
                var languageKey = Path.GetFileNameWithoutExtension(file).Trim().ToLowerInvariant();
                if (string.IsNullOrWhiteSpace(languageKey))
                    continue;

                if (!termsByLanguage.TryGetValue(languageKey, out var terms))
                {
                    terms = [];
                    termsByLanguage[languageKey] = terms;
                }

                foreach (var line in File.ReadLines(file))
                {
                    var trimmed = line.Trim();
                    if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith('#'))
                        continue;
                    terms.Add(trimmed);
                }
            }

            _logger.LogInformation("Loaded {FileCount} blocked term files from {Path}", files.Length, fullPath);
            return termsByLanguage;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed loading local blocked terms.");
            return new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        }
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

    private static string RemoveStandalonePunctuationTokens(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        return Regex.Replace(text, @"(?<=^|\s)[\p{P}\p{S}]+(?=\s|$)", string.Empty);
    }
}
