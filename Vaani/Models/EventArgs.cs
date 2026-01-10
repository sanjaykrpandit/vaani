#region Event Args

public enum MessageDirection
{
    Outgoing,
    Incoming
}

public enum MessageType
{
    Recognizing,
    Recognized
}

public enum SystemMessageType
{
    Started,
    Stopped,
    Error
}

public class MessageEventArgs : EventArgs
{
    public MessageDirection Direction { get; set; }
    public string Text { get; set; } = string.Empty;
    public MessageType MessageType { get; set; }
    public bool IsFromMeeting { get; set; }
    public string SessionId { get; set; } = string.Empty;
}

public class TranslationEventArgs : EventArgs
{
    public MessageDirection Direction { get; set; }
    public string OriginalText { get; set; } = string.Empty;
    public string TranslatedText { get; set; } = string.Empty;
    public bool IsFromMeeting { get; set; }
    public string SessionId { get; set; } = string.Empty;
}

public class SystemMessageEventArgs : EventArgs
{
    public string Message { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public SystemMessageType MessageType { get; set; }
}

public class SynthesizingEventArgs : EventArgs
{
    public string OriginalText { get; set; } = string.Empty;
    public string TranslatedText { get; set; } = string.Empty;
    public bool IsFromMeeting { get; set; }
    public bool IsSynthesizing { get; set; }
    public string SessionId { get; set; } = string.Empty;
}

#endregion