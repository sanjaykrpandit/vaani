# Translation Service Logging Guide

## Overview

The Translation Service now includes a **structured logging system** with filtering capabilities to help you debug and monitor the application effectively.

## Log Levels

The system supports 5 log levels (from most to least verbose):

| Level | Icon | Description | Use Case |
|-------|------|-------------|----------|
| `Debug` | ?? | Detailed diagnostic info | Troubleshooting, development |
| `Info` | ?? | Normal operation messages | Production monitoring |
| `Warning` | ?? | Potentially problematic situations | Issues that don't stop operation |
| `Error` | ? | Failures and exceptions | Problems that need attention |
| `Critical` | ?? | Critical errors | Immediate action required |

## Log Categories

Logs are organized into 12 categories for easy filtering:

| Category | Prefix | Description |
|----------|--------|-------------|
| `System` | [SYS] | General system status |
| `Outgoing` | [OUT] | Your speech ? meeting |
| `Incoming` | [IN] | Meeting ? your speakers |
| `Recognition` | [REC] | Speech recognition events |
| `Synthesis` | [SYN] | Speech synthesis events |
| `Playback` | [PLY] | Audio playback operations |
| `AudioCapture` | [AUD] | Audio capture monitoring |
| `Duplicate` | [DUP] | Duplicate detection |
| `Metrics` | [MET] | Performance metrics |
| `Queue` | [QUE] | Queue management |
| `Device` | [DEV] | Device detection |
| `Connection` | [CON] | Azure connection |

## Usage Examples

### Example 1: Default Configuration (Info Level)
```csharp
var translationService = new TranslationService();
// Default: Info level, all categories enabled
// You'll see: normal operations, warnings, errors
```

**Output:**
```
[10:30:45.123] ?? [SYS] Starting Bidirectional Translation
[10:30:45.234] ?? [DEV] ?? Using physical microphone: Realtek Audio
[10:30:46.456] ?? [REC] [OUT] You said: Hello everyone
[10:30:46.567] ?? [REC] [OUT] Translated: Hola a todos
[10:30:46.789] ?? [SYN] [OUT] Completed in 234ms
[10:30:47.001] ?? [PLY] [OUT] Playing audio (1500ms) to meeting...
```

### Example 2: Debug Mode (Full Verbose Logging)
```csharp
var translationService = new TranslationService();
translationService.SetLogLevel(LogLevel.Debug);
// Shows everything including confidence scores, queue sizes, audio levels
```

**Output:**
```
[10:30:45.123] ?? [SYS] Starting Bidirectional Translation
[10:30:45.134] ?? [SYS] Azure Key: ab12...xy89
[10:30:45.234] ?? [DEV] ?? Using physical microphone: Realtek Audio
[10:30:46.123] ?? [REC] [OUT] Recognizing: Hello
[10:30:46.234] ?? [REC] [OUT] Recognizing: Hello everyone
[10:30:46.456] ?? [REC] [OUT] You said: Hello everyone
[10:30:46.467] ?? [REC] [OUT] Confidence JSON: {"confidence":0.95}
[10:30:46.567] ?? [REC] [OUT] Translated: Hola a todos
[10:30:46.578] ?? [SYN] [SYNTHESIS] ?? Attempting synthesis: 'Hola a todos' (length: 13 chars)
[10:30:46.789] ?? [SYN] [SYNTHESIS] ? Success on attempt 1 (211ms, 48000 bytes)
[10:30:46.790] ?? [SYN] [OUT] Completed in 234ms
[10:30:46.791] ?? [QUE] [OUT] Queue size: 1
[10:30:47.001] ?? [PLY] [OUT] Playing audio (1500ms) to meeting...
[10:30:47.234] ?? [AUD] [IN] Level: 0.125, Block rate: 12.3%
```

### Example 3: Filter Specific Categories
```csharp
var translationService = new TranslationService();
// Only show Recognition, Synthesis, and Playback logs
translationService.SetLogCategories(
    LogCategory.Recognition, 
    LogCategory.Synthesis, 
    LogCategory.Playback
);
```

**Output:**
```
[10:30:46.456] ?? [REC] [OUT] You said: Hello everyone
[10:30:46.567] ?? [REC] [OUT] Translated: Hola a todos
[10:30:46.789] ?? [SYN] [OUT] Completed in 234ms
[10:30:47.001] ?? [PLY] [OUT] Playing audio (1500ms) to meeting...
```

### Example 4: Production Mode (Warnings and Errors Only)
```csharp
var translationService = new TranslationService();
translationService.SetLogLevel(LogLevel.Warning);
// Only show warnings, errors, and critical messages
```

**Output:**
```
[10:30:45.234] ?? [DEV] Physical microphone not found, using default
[10:32:15.789] ? [SYN] [OUT] Synthesis error: Network timeout
```

### Example 5: Focus on Audio Issues
```csharp
var translationService = new TranslationService();
translationService.SetLogLevel(LogLevel.Debug);
translationService.SetLogCategories(
    LogCategory.AudioCapture,
    LogCategory.Playback,
    LogCategory.Device
);
// Perfect for diagnosing audio routing issues
```

**Output:**
```
[10:30:45.234] ?? [DEV] ?? Using physical microphone: Realtek Audio
[10:30:45.345] ?? [DEV] ?? CABLE device found: CABLE Input (VB-Audio Virtual Cable)
[10:30:46.789] ?? [AUD] [IN] Audio capture started
[10:30:47.001] ?? [AUD] [IN] Level: 0.125, Block rate: 12.3%
[10:30:48.234] ?? [PLY] [OUT] Playing audio (1500ms) to meeting...
```

### Example 6: Monitor Queue and Performance
```csharp
var translationService = new TranslationService();
translationService.SetLogLevel(LogLevel.Debug);
translationService.SetLogCategories(
    LogCategory.Queue,
    LogCategory.Metrics,
    LogCategory.Synthesis
);
// Track performance bottlenecks
```

**Output:**
```
[10:30:46.789] ?? [SYN] [OUT] Completed in 234ms
[10:30:46.790] ?? [QUE] [OUT] Queue size: 1
[10:30:47.123] ?? [SYN] [IN] Completed in 189ms
[10:30:47.124] ?? [QUE] [IN] Queue size: 2
[10:35:00.000] ?? [MET] Translation Session Metrics:
[10:35:00.001] ?? [MET]    Outgoing Queue Peak: 3
[10:35:00.002] ?? [MET]    Incoming Queue Peak: 4
```

## Common Debugging Scenarios

### Scenario 1: "No audio reaching the meeting"
```csharp
translationService.SetLogLevel(LogLevel.Debug);
translationService.SetLogCategories(
    LogCategory.Device,
    LogCategory.Playback,
    LogCategory.Synthesis
);
```
**Look for:** CABLE device detection, synthesis completion, playback messages

### Scenario 2: "Meeting audio not being captured"
```csharp
translationService.SetLogLevel(LogLevel.Debug);
translationService.SetLogCategories(
    LogCategory.Device,
    LogCategory.AudioCapture,
    LogCategory.Recognition
);
```
**Look for:** Audio level readings, block rates, recognition events

### Scenario 3: "Translation is slow"
```csharp
translationService.SetLogLevel(LogLevel.Debug);
translationService.SetLogCategories(
    LogCategory.Synthesis,
    LogCategory.Queue,
    LogCategory.Metrics
);
```
**Look for:** Synthesis timing, queue buildup, connection warmup

### Scenario 4: "Getting duplicate translations"
```csharp
translationService.SetLogLevel(LogLevel.Debug);
translationService.SetLogCategories(
    LogCategory.Duplicate,
    LogCategory.Recognition
);
```
**Look for:** Duplicate detection messages, transcript normalization

## Recommended Configurations

### Development
```csharp
translationService.SetLogLevel(LogLevel.Debug);
// All categories enabled (default)
```

### Production
```csharp
translationService.SetLogLevel(LogLevel.Info);
// All categories enabled (default)
```

### Troubleshooting
```csharp
translationService.SetLogLevel(LogLevel.Debug);
// Enable specific categories based on the issue
```

### Minimal (Performance)
```csharp
translationService.SetLogLevel(LogLevel.Warning);
// Only show problems
```

## Dynamic Configuration

You can change logging settings at runtime:

```csharp
var translationService = new TranslationService();

// Start with minimal logging
translationService.SetLogLevel(LogLevel.Warning);

// ... app runs ...

// User reports audio issue - enable detailed audio logging
translationService.SetLogLevel(LogLevel.Debug);
translationService.SetLogCategories(
    LogCategory.AudioCapture,
    LogCategory.Playback,
    LogCategory.Device
);

// Issue resolved - back to normal
translationService.SetLogLevel(LogLevel.Info);
translationService.SetLogCategories(null); // Re-enable all
```

## Tips

1. **Start with Info level** in production - it shows important events without overwhelming detail
2. **Use Debug level** when troubleshooting - you'll see recognizing events, confidence scores, audio levels
3. **Filter by category** to focus on specific subsystems
4. **Watch for warning icons (??)** - they indicate potential issues
5. **Check Metrics logs** at session end for performance summary
6. **Audio level logs (Debug)** help diagnose microphone/speaker problems
7. **Queue size logs (Debug)** help identify performance bottlenecks

## Migration from Old Logging

Old code:
```csharp
Log("Some message");
```

Now automatically mapped to:
```csharp
_logger.Info(LogCategory.System, "Some message");
```

No code changes needed - backward compatible!
