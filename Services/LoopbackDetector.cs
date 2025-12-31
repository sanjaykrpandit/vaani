using System.Text.RegularExpressions;
using vconsole.Services.Logger;

namespace Vaani.Services;

/// <summary>
/// Detects acoustic loopback using multi-layer fuzzy text matching with language awareness.
/// Optimized for Hindi, English, and Japanese with language-specific normalization and thresholds.
/// NOW WITH BIDIRECTIONAL TRACKING to prevent meeting echo loops.
/// </summary>
public class LoopbackDetector : IDisposable
{
    private readonly Dictionary<string, (string normalizedText, DateTime timestamp, string language, int playCount, string direction)> _recentTranslations = new();
    private readonly ReaderWriterLockSlim _lock = new();
    private readonly TranslationLogger? _logger;

    // ✅ Language-specific regex patterns
    private static readonly Regex _punctuationRegex = new(@"[\p{P}\p{S}]", RegexOptions.Compiled);
    private static readonly Regex _repeatedCharsRegex = new(@"(.)\1{2,}", RegexOptions.Compiled);
    private static readonly Regex _whitespaceRegex = new(@"\s+", RegexOptions.Compiled);

    private const int MaxAgeSeconds = 10;
    private const double DefaultSimilarityThreshold = 0.70; // ✅ LOWERED even more
    private const double StrictSimilarityThreshold = 0.80; // ✅ LOWERED even more
    private const double RecentTimeWindowSeconds = 3.0; // ✅ INCREASED from 2.0
    private const double MaxTimeWindowSeconds = 8.0; // ✅ INCREASED from 5.0
    private const int MinLengthForSubstring = 4; // ✅ LOWERED from 5
    private const int MaxTrackedTranslations = 30; // ✅ INCREASED from 25

    // ✅ Language-specific filler words
    private static readonly Dictionary<string, HashSet<string>> _languageFillers = new()
    {
        ["en"] = new(StringComparer.OrdinalIgnoreCase)
        {
            "um", "uh", "er", "ah", "like", "you know", "i mean", "actually", "basically",
            "so", "well", "right", "okay", "ok", "yeah", "yes", "no"
        },
        ["hi"] = new(StringComparer.OrdinalIgnoreCase)
        {
            "उम", "आ", "ए", "हाँ", "ठीक है", "अच्छा", "तो", "वो", "है ना", "जी"
        },
        ["ja"] = new(StringComparer.OrdinalIgnoreCase)
        {
            "あの", "えっと", "その", "まあ", "ね", "よ", "さ", "はい", "いいえ"
        }
    };

    // ✅ Language-specific similarity thresholds (VERY AGGRESSIVE)
    private static readonly Dictionary<string, double> _languageThresholds = new()
    {
        ["en"] = 0.70,  // ✅ English: Very aggressive
        ["hi"] = 0.60,  // ✅ Hindi: Extremely aggressive (Devanagari variations)
        ["ja"] = 0.65   // ✅ Japanese: Very aggressive (particle variations)
    };

    public LoopbackDetector(TranslationLogger? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Tracks a recently played translation for loopback detection.
    /// </summary>
    /// <param name="text">The text that was played</param>
    /// <param name="language">The language of the played audio</param>
    /// <param name="direction">"outgoing" (to meeting) or "incoming" (to speaker)</param>
    public void TrackPlayedTranslation(string text, string language, string direction = "incoming")
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        var normalized = NormalizeText(text, language);
        if (string.IsNullOrEmpty(normalized)) return;

        _lock.EnterWriteLock();
        try
        {
            var key = $"{normalized}_{direction}"; // ✅ Separate keys by direction

            // ✅ Track play count for frequently repeated phrases
            if (_recentTranslations.TryGetValue(key, out var existing))
            {
                _recentTranslations[key] = (normalized, DateTime.UtcNow, language, existing.playCount + 1, direction);
                _logger?.Debug(LogCategory.Playback,
                    $"🛡️ Re-tracking [{direction.ToUpper()}]: '{text}' ({language}) - played {existing.playCount + 1}x");
            }
            else
            {
                _recentTranslations[key] = (normalized, DateTime.UtcNow, language, 1, direction);
                _logger?.Debug(LogCategory.Playback,
                    $"🛡️ Tracking [{direction.ToUpper()}]: '{text}' ({language}) → '{normalized}'");
            }

            // ✅ Memory limit - remove oldest
            if (_recentTranslations.Count > MaxTrackedTranslations)
            {
                var oldest = _recentTranslations.OrderBy(kvp => kvp.Value.timestamp).First();
                _recentTranslations.Remove(oldest.Key);
                _logger?.Debug(LogCategory.Playback, "🧹 Removed oldest tracked translation");
            }
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Checks if the given text is likely acoustic loopback from recently played audio.
    /// </summary>
    public bool IsLoopback(string text, string detectedLanguage, double threshold = DefaultSimilarityThreshold)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        var normalized = NormalizeText(text, detectedLanguage);
        if (string.IsNullOrEmpty(normalized)) return false;

        // ✅ Ignore very short phrases (likely noise)
        if (normalized.Length < 2) // ✅ LOWERED from 3 to 2
        {
            _logger?.Debug(LogCategory.Recognition, $"⚠️ Ignoring very short text: '{text}'");
            return false;
        }

        _lock.EnterReadLock();
        try
        {
            CleanupOldEntries();

            // ✅ Get language-specific threshold
            var langCode = detectedLanguage.Split('-')[0].ToLowerInvariant();
            var effectiveThreshold = _languageThresholds.ContainsKey(langCode)
                ? _languageThresholds[langCode]
                : threshold;

            _logger?.Debug(LogCategory.Recognition,
                $"🔍 Loopback check: text='{text}' → normalized='{normalized}', lang={detectedLanguage}, threshold={effectiveThreshold:P0}, tracking {_recentTranslations.Count} items");

            foreach (var kvp in _recentTranslations.Values)
            {
                var (recentText, timestamp, recentLanguage, playCount, direction) = kvp;
                var timeSincePlayback = (DateTime.UtcNow - timestamp).TotalSeconds;

                _logger?.Debug(LogCategory.Recognition,
                    $"   📋 Comparing [{direction}]: '{recentText}' (lang={recentLanguage}, age={timeSincePlayback:F1}s, count={playCount}x)");

                // ✅ RULE 1: Only check same-language matches
                if (!IsSameLanguageFamily(detectedLanguage, recentLanguage))
                {
                    _logger?.Debug(LogCategory.Recognition,
                        $"      ❌ Language mismatch: {detectedLanguage} ≠ {recentLanguage}");
                    continue;
                }

                // ✅ RULE 2: Time window (EXTENDED)
                if (timeSincePlayback > MaxTimeWindowSeconds)
                {
                    _logger?.Debug(LogCategory.Recognition,
                        $"      ❌ Too old: {timeSincePlayback:F1}s > {MaxTimeWindowSeconds}s");
                    continue;
                }

                // ✅ OPTIMIZATION: Stricter threshold for frequently played phrases
                var adjustedThreshold = playCount > 2
                    ? Math.Min(0.95, effectiveThreshold + 0.10) // ✅ More aggressive adjustment
                    : effectiveThreshold;

                // ✅ RULE 3: Exact match
                if (normalized == recentText)
                {
                    _logger?.Warning(LogCategory.Recognition,
                        $"✅ LOOPBACK EXACT [{direction}]: '{text}' == '{recentText}' (played {timeSincePlayback:F1}s ago, {playCount}x)");
                    return true;
                }

                // ✅ RULE 4: Word-based similarity (2+ words) - LOWERED from 3
                if (normalized.Split(' ').Length >= 2 && recentText.Split(' ').Length >= 2)
                {
                    var wordSim = CalculateWordSimilarity(normalized, recentText);
                    _logger?.Debug(LogCategory.Recognition,
                        $"      📊 Word similarity: {wordSim:P0}");

                    if (wordSim >= 0.60) // ✅ LOWERED from 0.65 to 0.60
                    {
                        _logger?.Warning(LogCategory.Recognition,
                            $"✅ LOOPBACK WORD [{direction}]: '{text}' ≈ '{recentText}' ({wordSim:P0} match, {timeSincePlayback:F1}s ago)");
                        return true;
                    }
                }

                // ✅ RULE 5: Enhanced substring match
                if (normalized.Length >= MinLengthForSubstring && recentText.Length >= MinLengthForSubstring)
                {
                    var containment = CalculateContainmentRatio(normalized, recentText);
                    _logger?.Debug(LogCategory.Recognition,
                        $"      📊 Containment: {containment:P0}");

                    if (containment >= 0.55) // ✅ LOWERED from 0.60 to 0.55
                    {
                        _logger?.Warning(LogCategory.Recognition,
                            $"✅ LOOPBACK SUBSTRING [{direction}]: '{text}' ⊆ '{recentText}' ({containment:P0} overlap, {timeSincePlayback:F1}s ago)");
                        return true;
                    }
                }

                // ✅ RULE 6: Fuzzy match with time-based threshold
                var requiredSimilarity = timeSincePlayback < RecentTimeWindowSeconds
                    ? adjustedThreshold
                    : StrictSimilarityThreshold;

                var similarity = CalculateSimilarity(normalized, recentText);
                _logger?.Debug(LogCategory.Recognition,
                    $"      📊 Levenshtein: {similarity:P0} (need {requiredSimilarity:P0})");

                if (similarity >= requiredSimilarity)
                {
                    _logger?.Warning(LogCategory.Recognition,
                        $"✅ LOOPBACK FUZZY [{direction}]: '{text}' ≈ '{recentText}' ({similarity:P0}, threshold: {requiredSimilarity:P0}, {timeSincePlayback:F1}s ago)");
                    return true;
                }
            }

            _logger?.Debug(LogCategory.Recognition,
                $"❌ NOT loopback: '{text}' - no matches found");
            return false;
        }
        finally
        {
            if (_lock.IsReadLockHeld)
                _lock.ExitReadLock();
        }
    }

    /// <summary>
    /// Clears all tracked translations.
    /// </summary>
    public void Clear()
    {
        _lock.EnterWriteLock();
        try
        {
            _recentTranslations.Clear();
            _logger?.Debug(LogCategory.Playback, "🧹 Cleared all loopback tracking");
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Gets the current count of tracked translations.
    /// </summary>
    public int TrackedCount
    {
        get
        {
            _lock.EnterReadLock();
            try
            {
                return _recentTranslations.Count;
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }
    }

    private void CleanupOldEntries()
    {
        var cutoff = DateTime.UtcNow.AddSeconds(-MaxAgeSeconds);
        var expiredKeys = _recentTranslations
            .Where(kvp => kvp.Value.timestamp < cutoff)
            .Select(kvp => kvp.Key)
            .ToList();

        if (expiredKeys.Count > 0)
        {
            _lock.ExitReadLock();
            _lock.EnterWriteLock();
            try
            {
                foreach (var key in expiredKeys)
                    _recentTranslations.Remove(key);
                _logger?.Debug(LogCategory.Playback, $"🧹 Cleaned {expiredKeys.Count} old translations");
            }
            finally
            {
                _lock.ExitWriteLock();
                _lock.EnterReadLock();
            }
        }
    }

    // ✅ Language-aware text normalization
    private static string NormalizeText(string text, string language)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var langCode = language.Split('-')[0].ToLowerInvariant();

        // Remove punctuation
        var normalized = _punctuationRegex.Replace(text, "");

        // ✅ Hindi-specific: Normalize zero-width characters
        if (langCode == "hi")
        {
            normalized = normalized.Replace("\u200c", "").Replace("\u200d", "");
        }

        // ✅ Normalize repeated characters (e.g., "hellooo" → "helloo")
        normalized = _repeatedCharsRegex.Replace(normalized, "$1$1");

        // Collapse whitespace
        normalized = _whitespaceRegex.Replace(normalized, " ");

        // ✅ Remove language-specific filler words
        if (_languageFillers.TryGetValue(langCode, out var fillers))
        {
            var words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(w => !fillers.Contains(w))
                .ToArray();
            normalized = string.Join(" ", words);
        }

        return normalized.Trim().ToLowerInvariant();
    }

    private static bool IsSameLanguageFamily(string lang1, string lang2)
    {
        if (string.IsNullOrEmpty(lang1) || string.IsNullOrEmpty(lang2))
            return false;

        var code1 = lang1.Split('-')[0].ToLowerInvariant();
        var code2 = lang2.Split('-')[0].ToLowerInvariant();

        return code1 == code2;
    }

    // ✅ Word-based similarity (Jaccard index)
    private static double CalculateWordSimilarity(string s1, string s2)
    {
        var words1 = s1.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var words2 = s2.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();

        if (words1.Count == 0 || words2.Count == 0) return 0.0;

        var intersection = words1.Intersect(words2).Count();
        var union = words1.Union(words2).Count();

        return (double)intersection / union;
    }

    // ✅ Containment ratio (partial overlap)
    private static double CalculateContainmentRatio(string s1, string s2)
    {
        var shorter = s1.Length <= s2.Length ? s1 : s2;
        var longer = s1.Length > s2.Length ? s1 : s2;

        if (shorter.Length == 0) return 0.0;

        // Simple substring containment
        if (longer.Contains(shorter))
            return 1.0;

        // Find longest common substring
        int maxMatch = 0;
        for (int i = 0; i < shorter.Length; i++)
        {
            for (int len = Math.Min(shorter.Length - i, longer.Length); len > maxMatch; len--)
            {
                if (i + len > shorter.Length) continue;
                var substring = shorter.Substring(i, len);
                if (longer.Contains(substring))
                {
                    maxMatch = len;
                    break;
                }
            }
        }

        return (double)maxMatch / shorter.Length;
    }

    private static double CalculateSimilarity(string s1, string s2)
    {
        if (string.IsNullOrEmpty(s1) || string.IsNullOrEmpty(s2))
            return 0.0;

        var distance = LevenshteinDistance(s1, s2);
        var maxLen = Math.Max(s1.Length, s2.Length);
        return 1.0 - ((double)distance / maxLen);
    }

    private static int LevenshteinDistance(string s1, string s2)
    {
        var len1 = s1.Length;
        var len2 = s2.Length;

        if (len1 == 0) return len2;
        if (len2 == 0) return len1;
        if (s1 == s2) return 0;

        var previous = new int[len2 + 1];
        var current = new int[len2 + 1];

        for (int j = 0; j <= len2; j++)
            previous[j] = j;

        for (int i = 1; i <= len1; i++)
        {
            current[0] = i;

            for (int j = 1; j <= len2; j++)
            {
                var cost = (s1[i - 1] == s2[j - 1]) ? 0 : 1;
                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + cost
                );
            }

            var temp = previous;
            previous = current;
            current = temp;
        }

        return previous[len2];
    }

    public void Dispose()
    {
        _lock?.Dispose();
    }
}