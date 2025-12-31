using System;
using ReactiveUI;

namespace Vaani.Models;

public class MessageBubble : ReactiveObject
{
    private string _originalText = string.Empty;
    private string _translatedText = string.Empty;
    private bool _isRecognizing;
    private bool _isSystemMessage;
    private bool _isSynthesizing;

    public string OriginalText
    {
        get => _originalText;
        set => this.RaiseAndSetIfChanged(ref _originalText, value);
    }

    public string TranslatedText
    {
        get => _translatedText;
        set => this.RaiseAndSetIfChanged(ref _translatedText, value);
    }

    public bool IsFromMeeting { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
    
    public bool IsRecognizing
    {
        get => _isRecognizing;
        set => this.RaiseAndSetIfChanged(ref _isRecognizing, value);
    }
    
    public bool IsSystemMessage
    {
        get => _isSystemMessage;
        set => this.RaiseAndSetIfChanged(ref _isSystemMessage, value);
    }
    
    public bool IsSynthesizing
    {
        get => _isSynthesizing;
        set => this.RaiseAndSetIfChanged(ref _isSynthesizing, value);
    }
}
