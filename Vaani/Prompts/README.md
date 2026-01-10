# VAANI - Real-Time Translation App

?? **Bidirectional English ? Hindi Translation for Virtual Meetings**

## ?? Features

### ? Current Features
- **OUTGOING**: You speak English ? Meeting hears Hindi (TTS)
- **INCOMING**: Meeting speaks Hindi ? You hear English (TTS)
- **Built-in Noise Suppression**: Azure Speech SDK audio processing
- **Echo Prevention**: Smart audio routing to prevent feedback loops
- **UTF-8 Support**: Proper Devanagari script display
- **Comprehensive Logging**: Timestamped diagnostic logs

### ?? Audio Processing

#### Azure Speech SDK Built-in Features:
```csharp
config.SetProperty("SPEECH-AudioProcessingOptions", 
    "BeamformingAngles=180;NoiseSuppression=High");
```

**What's Included:**
- ? **Noise Suppression**: Reduces background noise (keyboard, fans, AC)
- ? **Beamforming**: Focuses on sound from your direction
- ? **Echo Cancellation**: Basic echo reduction
- ? **Automatic Gain Control**: Normalizes volume levels

**Limitations:**
- ?? Cannot suppress very loud background noises
- ?? Best results with headset/microphone

## ?? Improving Noise Suppression

### Option 1: Use Better Hardware
- USB Headset with built-in noise cancellation
- Professional microphone with cardioid pickup
- Noise-canceling headphones

### Option 2: Meeting App Settings (Teams/Zoom)
- ? Noise Suppression: High
- ? Echo Cancellation: Enabled
- ? Background Blur

### Option 3: Windows Audio Enhancements
1. Right-click Speaker icon ? Sound settings
2. Input ? Select microphone ? Device properties
3. Enhancements tab:
   - ? Noise Suppression
   - ? Acoustic Echo Cancellation

### Option 4: External Software
- **RTX Voice** (NVIDIA GPUs) - AI-powered noise removal
- **Krisp** - Professional noise cancellation
- **OBS Studio** - Advanced audio filters

## ?? Setup Requirements

### 1. Install VB-CABLE
Download: https://vb-audio.com/Cable/

### 2. Audio Configuration

**Meeting App (Teams/Zoom):**
- Microphone: **CABLE Output** ?
- Speaker: **CABLE Input** ?

**Windows Default:**
- Microphone: **Your headset/mic** ?
- Speaker: **Your headset/speakers** ?

## ?? Running the App

```bash
dotnet run
```

1. Review device configuration
2. Press ENTER to start
3. Speak or listen to translations
4. Press Ctrl+C to stop

## ?? Troubleshooting

### Meeting hears their own voice
- Use headphones instead of speakers
- Lower speaker volume
- Already has pause mechanism built-in

### No Hindi translation heard
- Check Windows default speaker
- Verify meeting app speaker = CABLE Input

### Too much background noise
- Use headset with noise cancellation
- Enable meeting app noise suppression
- Try RTX Voice or Krisp
