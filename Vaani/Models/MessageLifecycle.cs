
namespace vconsole.Models
{
    public class MessageLifecycle
    {
        public string Id { get; } = Guid.NewGuid().ToString("N")[..8];
        public DateTime CaptureTime { get; set; }
        public DateTime RecognizedTime { get; set; }
        public DateTime TranslatedTime { get; set; }
        public DateTime QueuedTime { get; set; }
        public DateTime SynthesisStartTime { get; set; }
        public DateTime SynthesisEndTime { get; set; }
        public DateTime PlaybackStartTime { get; set; }
        public DateTime PlaybackEndTime { get; set; }
        public string OriginalText { get; set; } = "";
        public string TranslatedText { get; set; } = "";
        public int AudioDataSize { get; set; }
        public bool SynthesisSuccess { get; set; }
        public bool PlaybackSuccess { get; set; }
        public string? ErrorMessage { get; set; }

        public void LogSummary(Action<string> logger)
        {
            var totalTime = (PlaybackEndTime - CaptureTime).TotalMilliseconds;
            var recognitionTime = (RecognizedTime - CaptureTime).TotalMilliseconds;
            var translationTime = (TranslatedTime - RecognizedTime).TotalMilliseconds;
            var queueWaitTime = (SynthesisStartTime - QueuedTime).TotalMilliseconds;
            var synthesisTime = (SynthesisEndTime - SynthesisStartTime).TotalMilliseconds;
            var playbackTime = (PlaybackEndTime - PlaybackStartTime).TotalMilliseconds;

            logger($"═══════════════════════════════════════════════════════════════");
            logger($"📊 MESSAGE LIFECYCLE SUMMARY [ID: {Id}]");
            logger($"═══════════════════════════════════════════════════════════════");
            logger($"   Original:   '{OriginalText.Substring(0, Math.Min(50, OriginalText.Length))}'");
            logger($"   Translated: '{TranslatedText.Substring(0, Math.Min(50, TranslatedText.Length))}'");
            logger($"───────────────────────────────────────────────────────────────");
            logger($"   ⏱️  TIMING BREAKDOWN:");
            logger($"      1️⃣  Capture → Recognized:  {recognitionTime:F0}ms");
            logger($"      2️⃣  Translation:            {translationTime:F0}ms");
            logger($"      3️⃣  Queue Wait:             {queueWaitTime:F0}ms");
            logger($"      4️⃣  Synthesis:              {synthesisTime:F0}ms");
            logger($"      5️⃣  Playback:               {playbackTime:F0}ms");
            logger($"      ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            logger($"      🎯 TOTAL END-TO-END:        {totalTime:F0}ms");
            logger($"───────────────────────────────────────────────────────────────");
            logger($"   📦 Audio Size: {AudioDataSize} bytes");
            logger($"   ✅ Synthesis: {(SynthesisSuccess ? "SUCCESS" : "FAILED")}");
            logger($"   ✅ Playback:  {(PlaybackSuccess ? "SUCCESS" : "FAILED")}");
            if (!string.IsNullOrEmpty(ErrorMessage))
            {
                logger($"   ❌ Error: {ErrorMessage}");
            }
            logger($"═══════════════════════════════════════════════════════════════");
        }
    }

}
