using System.Text.RegularExpressions;

namespace Vaani.Services;

/// <summary>
/// Provides text processing utilities for translation and synthesis.
/// </summary>
public static class TextProcessingHelper
{

    private static readonly Regex AsterisksRegex = new(@"\*+", RegexOptions.Compiled);
    private static readonly Regex MultiSpaceRegex = new(@"\s{2,}", RegexOptions.Compiled);
    private static readonly Regex SpaceRegex = new(@"\s+", RegexOptions.Compiled);

    public static string CleanTextForSynthesis(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        // ✅ Use pre-compiled regex
        var cleaned = AsterisksRegex.Replace(text, "");
        cleaned = MultiSpaceRegex.Replace(cleaned, " ").Trim();

        return cleaned;
    }
    /// <summary>
    /// Cleans masked profanity from text to ensure smooth speech synthesis.
    /// Removes asterisks (****) that Azure profanity filter adds, preventing synthesis errors.
    /// </summary>
    //public static string CleanTextForSynthesis(string text)
    //{
    //    if (string.IsNullOrWhiteSpace(text))
    //        return text;

    //    var original = text;

    //    // Remove masked profanity (asterisks) - cleanest audio output
    //    var cleaned = Regex.Replace(text, @"\*+", "");

    //    // Remove multiple spaces left behind and trim
    //    cleaned = Regex.Replace(cleaned, @"\s{2,}", " ").Trim();

    //    return cleaned;
    //}

    /// <summary>
    /// Normalizes transcript text for duplicate detection, handling number words and variations.
    /// </summary>
    public static string NormalizeTranscript(string text)
    {
        var normalized = text.ToLowerInvariant();

        // Comprehensive number word normalization
        normalized = normalized
            // Basic 1-10
            .Replace("one", "1")
            .Replace("two", "2")
            .Replace("three", "3")
            .Replace("four", "4")
            .Replace("five", "5")
            .Replace("six", "6")
            .Replace("seven", "7")
            .Replace("eight", "8")
            .Replace("nine", "9")
            .Replace("ten", "10")
            // Teens
            .Replace("eleven", "11")
            .Replace("twelve", "12")
            .Replace("thirteen", "13")
            .Replace("fourteen", "14")
            .Replace("fifteen", "15")
            .Replace("sixteen", "16")
            .Replace("seventeen", "17")
            .Replace("eighteen", "18")
            .Replace("nineteen", "19")
            // Tens
            .Replace("twenty", "20")
            .Replace("thirty", "30")
            .Replace("forty", "40")
            .Replace("fifty", "50")
            .Replace("sixty", "60")
            .Replace("seventy", "70")
            .Replace("eighty", "80")
            .Replace("ninety", "90")
            // Common compounds
            .Replace("twenty-one", "21")
            .Replace("twenty one", "21")
            // Large numbers
            .Replace("hundred", "100")
            .Replace("thousand", "1000");

        // Remove extra whitespace     
        normalized = SpaceRegex.Replace(normalized, " ").Trim();

        return normalized;
    }

    /// <summary>
    /// Masks a sensitive key for logging purposes.
    /// </summary>
    public static string MaskKey(string key)
    {
        if (string.IsNullOrEmpty(key)) return "[EMPTY]";
        if (key.Length <= 8) return new string('*', key.Length);
        return $"{key[..4]}...{key[^4..]}";
    }


}