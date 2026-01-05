using Microsoft.CognitiveServices.Speech;

namespace Vaani.Services;

/// <summary>
/// Handles Azure speech synthesis operations with retry logic and warmup.
/// </summary>
public static class AzureSpeechSynthesisHelper
{
    /// <summary>
    /// Pre-warms Azure Speech SDK connections to eliminate cold start latency.
    /// </summary>
    public static async Task<bool> WarmupAzureConnection(SpeechSynthesizer synthesizer, CancellationToken ct, Action<string>? logger = null)
    {
        try
        {
            logger?.Invoke("⏳ Warming up Azure connection...");
            var warmupStartTime = DateTime.UtcNow;

            var warmupText = "Hello";
            var warmupResult = await synthesizer.SpeakTextAsync(warmupText);

            var warmupDuration = (DateTime.UtcNow - warmupStartTime).TotalMilliseconds;

            if (warmupResult?.Reason == ResultReason.SynthesizingAudioCompleted)
            {
                logger?.Invoke($"✅ Azure connection warmed up in {warmupDuration:F0}ms (first translation will be fast)");
                return true;
            }
            else
            {
                logger?.Invoke($"⚠️ Warmup completed with reason: {warmupResult?.Reason} ({warmupDuration:F0}ms)");
                return false;
            }
        }
        catch (Exception ex)
        {
            logger?.Invoke($"⚠️ Warmup error (non-critical): {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Synthesizes speech with automatic retry logic.
    /// </summary>
    public static async Task<SpeechSynthesisResult?> SynthesizeWithRetry(SpeechSynthesizer synthesizer, string text, CancellationToken ct, Action<string>? logger = null)
    {
        // ✅ OPTIMIZED: Create preview once outside the loop
        var textPreview = text.Length > 30 ? text[..30] : text;

        // ✅ OPTIMIZED: Only log if logger is not null
        //if (logger != null)
        //{
        //    logger($"[SYNTHESIS] 🎤 Attempting synthesis: '{textPreview}...' (length: {text.Length} chars)");
        //}

        for (int attempt = 0; attempt < AudioConfiguration.SynthesisRetryAttempts; attempt++)
        {
            try
            {
                var synthStartTime = DateTime.UtcNow;
                var result = await synthesizer.SpeakTextAsync(text);
                var synthDuration = (DateTime.UtcNow - synthStartTime).TotalMilliseconds;

                if (result.Reason == ResultReason.SynthesizingAudioCompleted)
                {
                    //logger?.Invoke($"[SYNTHESIS] ✅ Success on attempt {attempt + 1} ({synthDuration:F0}ms, {result.AudioData.Length} bytes)");
                    return result;
                }

                //if (attempt < AudioConfiguration.SynthesisRetryAttempts - 1)
                //{
                //    logger?.Invoke($"[SYNTHESIS] ⚠️ Attempt {attempt + 1} failed with reason: {result.Reason}, retrying...");
                //}
                //else
                //{
                //    logger?.Invoke($"[SYNTHESIS] ❌ Final attempt {attempt + 1} failed with reason: {result.Reason}");
                //}
            }
            catch (Exception ex) when (attempt < AudioConfiguration.SynthesisRetryAttempts - 1)
            {
                // ✅ OPTIMIZED: Use bit shift instead of Math.Pow
                var retryDelay = AudioConfiguration.SynthesisRetryDelayMs << attempt;
                logger?.Invoke($"[SYNTHESIS] ❌ Attempt {attempt + 1} exception: {ex.Message}, waiting {retryDelay}ms before retry");
                await Task.Delay(retryDelay, ct);
            }
            catch (Exception ex)
            {
                logger?.Invoke($"[SYNTHESIS] ❌ Final attempt {attempt + 1} exception: {ex.Message}");
            }
        }
        // ✅ FIXED: textPreview is now in scope
        logger?.Invoke($"[SYNTHESIS] ❌ Speech synthesis failed after {AudioConfiguration.SynthesisRetryAttempts} attempts for: '{textPreview}...'");
        return null;
    }
}