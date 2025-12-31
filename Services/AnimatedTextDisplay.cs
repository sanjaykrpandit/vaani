using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace Vaani.Services;

/// <summary>
/// Provides smooth, word-by-word text display animations for chat/transcription UIs.
/// Mimics natural reading speed and reduces visual jarring from sudden text appearance.
/// Industry standard: ~80ms per word = 12 words/second = natural reading pace.
/// </summary>
public class AnimatedTextDisplay
{
    /// <summary>
    /// Milliseconds delay between each word appearing.
    /// 80ms = ~12 words/second (natural reading speed)
    /// 50ms = faster (for quick speech)
    /// 120ms = slower (for emphasis/important text)
    /// </summary>
    public int WordDelayMs { get; set; } = 80;

    /// <summary>
    /// Displays text word-by-word with natural timing.
    /// Used for: Recognized text and Translated text.
    /// </summary>
    /// <param name="fullText">Complete text to display</param>
    /// <param name="onWordAdded">Callback invoked for each word: (currentText, isComplete)</param>
    /// <param name="ct">Cancellation token</param>
    public async Task DisplayProgressivelyAsync(
        string fullText,
        Action<string, bool> onWordAdded,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(fullText))
            return;

        var words = fullText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var displayedText = string.Empty;

        for (int i = 0; i < words.Length; i++)
        {
            if (ct.IsCancellationRequested)
                break;

            displayedText += (i > 0 ? " " : "") + words[i];
            bool isLastWord = i == words.Length - 1;

            // Marshal to UI thread
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                onWordAdded(displayedText, isLastWord);
            });

            // Don't delay after the last word
            if (!isLastWord)
                await Task.Delay(WordDelayMs, ct);
        }
    }

    /// <summary>
    /// Instantly displays recognizing text (interim results).
    /// No animation for recognizing since it updates frequently.
    /// </summary>
    public void DisplayRecognizingText(string text, Action<string> onTextUpdated)
    {
        Dispatcher.UIThread.Post(() => onTextUpdated(text));
    }
}
