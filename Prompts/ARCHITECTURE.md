    # VAANI - Real-Time Bidirectional Translation System

## 📋 Project Overview

**VAANI** is a real-time bidirectional translation application that enables seamless English ↔ Hindi communication during virtual meetings (Teams, Zoom, etc.). It uses Azure Cognitive Services for speech recognition, translation, and text-to-speech synthesis, combined with VB-CABLE virtual audio routing.

### Key Features
- ✅ **Bidirectional Translation**: English → Hindi and Hindi → English simultaneously
- ✅ **Real-Time Processing**: Continuous speech recognition with minimal latency
- ✅ **Dual Azure Pipelines**: Two independent translation flows running in parallel
- ✅ **Echo Prevention**: Smart audio routing prevents feedback loops
- ✅ **Noise Suppression**: Built-in Azure audio processing with beamforming
- ✅ **Comprehensive Logging**: Timestamped diagnostic logs for troubleshooting
- ✅ **UTF-8 Support**: Proper Devanagari script display in console

---

## 🏗️ Architecture

### Dual Pipeline System

The application uses **two completely independent Azure pipelines** running simultaneously:

#### **Pipeline #1: OUTGOING Flow** (You → Meeting)

1. **Speech Recognition**: Captures your English speech and converts it to text.
2. **Translation**: Translates the English text to Hindi.
3. **Text-to-Speech (TTS)**: Converts the Hindi text to speech.
4. **Audio Output**: Sends the Hindi speech to the meeting as audio.

#### **Pipeline #2: INCOMING Flow** (Meeting → You)

1. **Speech Recognition**: Captures the Hindi speech from the meeting.
2. **Translation**: Translates the Hindi text to English.
3. **Text-to-Speech (TTS)**: Converts the English text to speech.
4. **Audio Output**: Sends the English speech to you as audio.

---

## 🛠️ Technical Implementation

### Technologies Used

| Component | Technology | Purpose |
|-----------|-----------|---------|
| **Speech Recognition** | Azure Cognitive Services Speech SDK | Speech-to-Text (STT) |
| **Translation** | Azure Translator (integrated in Speech SDK) | Text translation |
| **Text-to-Speech** | Azure Neural TTS | Natural voice synthesis |
| **Audio Capture** | NAudio.Wave | Audio device management |
| **Audio Routing** | VB-CABLE Virtual Audio Driver | Virtual audio routing |
| **Device Enumeration** | NAudio.CoreAudioApi | Windows audio device discovery |
| **Framework** | .NET 8 / C# 12.0 | Application framework |

### Audio Configuration

#### Audio Formats
- **Capture Format**: 16kHz, 16-bit, mono PCM
- **Azure Processing**: 16kHz, 16-bit, mono
- **Synthesis Output**: 16kHz, 16-bit, mono PCM (Raw)
- **Buffer Size**: 100ms chunks

#### Device Requirements
**Meeting Application Settings:**
- Microphone: `CABLE Output` (VB-Audio Virtual Cable)
- Speaker: `CABLE Input` (VB-Audio Virtual Cable)

**Windows Default Devices:**
- Default Microphone: Your physical headset/microphone
- Default Speaker: Your physical headset/speakers

### Azure Services Configuration

#### Resource Setup
1. Create an Azure Cognitive Services resource in your Azure subscription.
2. Obtain the API key and endpoint URL from the Azure portal.

#### Configuration Steps
1. In the Azure portal, navigate to your Cognitive Services resource.
2. Under **Resource Management**, select **Keys and Endpoint**.
3. Copy the **KEY1** value and the **Endpoint** URL.

### Application Settings

#### Configuration File (`appsettings.json`)
```json
{
  "Azure": {
    "CognitiveServices": {
      "Endpoint": "YOUR_ENDPOINT_URL",
      "ApiKey": "YOUR_API_KEY"
    }
  },
  "Audio": {
    "CaptureDevice": "Your Capture Device Name",
    "RenderDevice": "Your Render Device Name"
  }
}
```

#### Environment Variables
- `AZURE_COGNITIVE_SERVICES_ENDPOINT`: Your Azure Endpoint URL
- `AZURE_COGNITIVE_SERVICES_API_KEY`: Your Azure API Key
- `AUDIO_CAPTURE_DEVICE`: The name of your audio capture device
- `AUDIO_RENDER_DEVICE`: The name of your audio render device
