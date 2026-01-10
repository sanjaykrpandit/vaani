# Latency Optimization Configuration Guide

## Recommended Settings Adjustments

Based on the latency analysis, consider tuning these Azure Speech Recognition parameters:

### 1. **Reduce End-of-Speech Detection Timeout**

**Current Settings:**
```csharp
// OutgoingFlow (line ~410)
config.SetProperty(PropertyId.SpeechServiceConnection_EndSilenceTimeoutMs, "500");
config.SetProperty(PropertyId.Speech_SegmentationSilenceTimeoutMs, "500");

// IncomingFlow (line ~555)
config.SetProperty(PropertyId.SpeechServiceConnection_EndSilenceTimeoutMs, "300");
config.SetProperty(PropertyId.Speech_SegmentationSilenceTimeoutMs, "300");
```

**Issue:** 500ms silence timeout = 500ms delay after user stops speaking before recognition completes

**Recommendation:**
```csharp
// Reduce to 300ms for faster recognition completion
config.SetProperty(PropertyId.SpeechServiceConnection_EndSilenceTimeoutMs, "300");
config.SetProperty(PropertyId.Speech_SegmentationSilenceTimeoutMs, "300");

// For incoming CABLE audio (may need higher tolerance)
config.SetProperty(PropertyId.SpeechServiceConnection_EndSilenceTimeoutMs, "250");
config.SetProperty(PropertyId.Speech_SegmentationSilenceTimeoutMs, "250");
```

**Tradeoff:** May cause more intermediate recognitions (garbage text), mitigated by deduplication logic

---

### 2. **Reduce Initial Silence Timeout**

**Current Setting:**
```csharp
// IncomingFlow (line ~565)
config.SetProperty(PropertyId.SpeechServiceConnection_InitialSilenceTimeoutMs, "5000");
```

**Issue:** 5-second wait before recognition starts if no speech detected = user perceives 5s latency before app responds

**Recommendation:**
```csharp
// Reduce to 1-2 seconds
config.SetProperty(PropertyId.SpeechServiceConnection_InitialSilenceTimeoutMs, "2000");
```

**Tradeoff:** May trigger false recognitions on silence, but rare with good audio input

---

### 3. **Queue Processing Optimization**

**Current Buffer Settings:**
```csharp
private static class AudioSettings
{
    public const int NormalBufferMs = 50;        // Single message
    public const int MediumQueueBufferMs = 20;   // Queue size 2-3
    public const int LargeQueueBufferMs = 0;     // Queue size >3
    
    public const int IncomingNormalBufferMs = 20;
    public const int IncomingMediumBufferMs = 10;
    public const int IncomingLargeBufferMs = 0;
}
```

**Recommendation for lower latency:**
```csharp
private static class AudioSettings
{
    // Aggressive: minimize buffer delays
    public const int NormalBufferMs = 20;        // ? Reduced from 50
    public const int MediumQueueBufferMs = 0;    // ? Reduced from 20
    public const int LargeQueueBufferMs = 0;     // Unchanged
    
    public const int IncomingNormalBufferMs = 10; // ? Reduced from 20
    public const int IncomingMediumBufferMs = 0;  // ? Reduced from 10
    public const int IncomingLargeBufferMs = 0;   // Unchanged
}
```

**Benefit:** 30-50ms latency reduction  
**Risk:** May cause audio artifacts if queue backs up

---

### 4. **Synthesis Retry Strategy**

**Current Settings:**
```csharp
public const int SynthesisRetryAttempts = 3;
public const int SynthesisRetryDelayMs = 100;
```

**Current Behavior:**
```
Attempt 1 fails ? wait 100ms
Attempt 2 fails ? wait 200ms  
Attempt 3 fails ? wait 400ms
Total: 700ms delay
```

**Recommendation for lower latency:**
```csharp
// More aggressive: fail fast and drop message
public const int SynthesisRetryAttempts = 2;      // ? Fewer attempts
public const int SynthesisRetryDelayMs = 50;      // ? Shorter delay
```

**Benefit:** Failed synthesis completes in 100ms instead of 700ms  
**Risk:** Occasional messages may fail to synthesize (acceptable in real-time scenario)

---

### 5. **Echo Prevention Window Tuning**

**Current Setting:**
```csharp
public const double EchoWindowSeconds = 0.3;  // 300ms
```

**With our fix, check time window too:**
```csharp
// Line ~625
isEcho = timeSincePlayback < (AudioSettings.EchoWindowSeconds * 2) && timeSincePlayback >= 0;
```

**Effective Echo Window:** 0.6 seconds

**Recommendations:**

**Scenario 1: High echo issues (e.g., conference call with feedback)**
```csharp
public const double EchoWindowSeconds = 0.5;  // ? Increased to 500ms
// With multiplier ? 1000ms (1 second) echo window
```

**Scenario 2: Low echo issues (separate audio/speaker)**
```csharp
public const double EchoWindowSeconds = 0.2;  // ? Reduced to 200ms
// With multiplier ? 400ms echo window
```

**Scenario 3: Disabled (for testing)**
```csharp
public const double EchoWindowSeconds = 0.0;  // Disables echo detection
// BUT: Remove the time window check in code, check only exact match
```

---

### 6. **Queue Size Limits**

**Current Settings:**
```csharp
public const int MaxQueueSize = 10;            // Drop messages if queue > 10
public const int LargeQueueThreshold = 3;      // Apply aggressive buffer above 3
```

**Recommendation:**
```csharp
// If you want faster processing, accept dropping messages:
public const int MaxQueueSize = 5;             // ? More aggressive, drop earlier

// If you want reliability, be more lenient:
public const int MaxQueueSize = 15;            // ? More lenient
```

**Tradeoff:** Lower queue = faster response but more dropped messages

---

## Configuration Presets

### ?? **LOW LATENCY PRESET** (Best for conference calls)
```csharp
// Azure Speech Config
config.SetProperty(PropertyId.SpeechServiceConnection_EndSilenceTimeoutMs, "250");
config.SetProperty(PropertyId.Speech_SegmentationSilenceTimeoutMs, "250");
config.SetProperty(PropertyId.SpeechServiceConnection_InitialSilenceTimeoutMs, "1000");

// Audio Settings
NormalBufferMs = 20;
MediumQueueBufferMs = 0;
LargeQueueBufferMs = 0;
MaxQueueSize = 8;
LargeQueueThreshold = 4;

// Synthesis
SynthesisRetryAttempts = 2;
SynthesisRetryDelayMs = 50;

// Echo
EchoWindowSeconds = 0.3;
```

**Expected latency:** 300-400ms  
**Trade-off:** More intermediate recognitions, occasional dropped messages

---

### ?? **BALANCED PRESET** (Current configuration, improved)
```csharp
// Azure Speech Config
config.SetProperty(PropertyId.SpeechServiceConnection_EndSilenceTimeoutMs, "300");
config.SetProperty(PropertyId.Speech_SegmentationSilenceTimeoutMs, "300");
config.SetProperty(PropertyId.SpeechServiceConnection_InitialSilenceTimeoutMs, "2000");

// Audio Settings
NormalBufferMs = 30;
MediumQueueBufferMs = 10;
LargeQueueBufferMs = 0;
MaxQueueSize = 10;
LargeQueueThreshold = 3;

// Synthesis
SynthesisRetryAttempts = 3;
SynthesisRetryDelayMs = 75;

// Echo
EchoWindowSeconds = 0.3;
```

**Expected latency:** 400-600ms  
**Trade-off:** Balanced between performance and reliability

---

### ?? **HIGH RELIABILITY PRESET** (Best for noisy environments)
```csharp
// Azure Speech Config
config.SetProperty(PropertyId.SpeechServiceConnection_EndSilenceTimeoutMs, "400");
config.SetProperty(PropertyId.Speech_SegmentationSilenceTimeoutMs, "400");
config.SetProperty(PropertyId.SpeechServiceConnection_InitialSilenceTimeoutMs, "3000");

// Audio Settings
NormalBufferMs = 50;
MediumQueueBufferMs = 20;
LargeQueueBufferMs = 10;
MaxQueueSize = 15;
LargeQueueThreshold = 5;

// Synthesis
SynthesisRetryAttempts = 5;
SynthesisRetryDelayMs = 100;

// Echo
EchoWindowSeconds = 0.5;
```

**Expected latency:** 600-800ms  
**Trade-off:** Higher reliability, all messages guaranteed to process

---

## Monitoring & Diagnostics

### Metrics to Watch

From `LogMetrics()` at session end:

```
Outgoing Queue Peak: X
  < 2  ? Good: Queue not backing up
  2-4  ??  OK: Some queue buildup
  > 4  ?? Bad: Recognition slower than playback

Incoming Queue Peak: X
  < 2  ? Good
  2-4  ??  OK
  > 4  ?? Bad

Messages Skipped: X
  0-2   ? Good: No dropped messages
  3-10  ??  Some losses but acceptable
  > 10  ?? Too many drops
```

### Log Patterns to Investigate

**High latency indicator:**
```
[OUT-RECOGNIZED] Hello
[OUT-TRANSLATED] Bonjour
[PAUSE------------------------]
[OUTGOING] Queued message. Queue size: 5  ? Should be 1-2
[OUTGOING] Synthesizing queued: Bonjour
<wait 200ms for synthesis>
[OUTGOING] Playing queued audio (500ms)
[OUTGOING] Playback completed
```

**False positive indicator:**
```
[INCOMING] ?? Echo detected, skipping: Hello  ? Happens when it shouldn't
```

**Lock contention indicator:**
```
High time gaps between consecutive log entries
Check if multiple [OUTGOING] and [INCOMING] events are interspersed
```

---

## Testing Protocol

1. **Baseline Test (Before Changes)**
   - Record latency metrics
   - Count false positives
   - Measure memory usage

2. **Optimization Test (After Changes)**
   - Repeat same test
   - Compare metrics
   - Identify remaining bottlenecks

3. **Configuration Tuning**
   - Apply LOW LATENCY preset
   - Test in your specific environment
   - Adjust thresholds based on results

4. **Production Deployment**
   - Start with BALANCED preset
   - Monitor metrics for 24 hours
   - Adjust based on real-world usage

---

## Common Issues & Solutions

**Issue:** Still experiencing 1s+ latency  
**Solution:**
1. Check Azure subscription quota/throttling
2. Measure network latency to Azure region
3. Consider using local speech API (requires on-premise setup)

**Issue:** False positives still occurring**  
**Solution:**
1. Reduce `EchoWindowSeconds` to 0.15
2. Increase minimum text length before adding to echo list
3. Log all echo detections to analyze patterns

**Issue:** Messages being dropped (Queue Peak > 4)**  
**Solution:**
1. Increase `MaxQueueSize` to 15
2. Reduce synthesis retry attempts
3. Profile to find CPU bottleneck

---

**Last Updated:** December 11, 2025  
**Configuration Version:** v1.0
