using ReactiveUI;
using System.Collections.ObjectModel;

namespace Lipi.Models;

public class SelectableLanguage : ReactiveObject
{
    private bool _isSelected;

    public string Code { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;

    public bool IsSelected
    {
        get => _isSelected;
        set => this.RaiseAndSetIfChanged(ref _isSelected, value);
    }
}

public class TranscriptBubble : ReactiveObject
{
    private string _transcript = string.Empty;
    private bool _isRecognizing;

    public string Transcript
    {
        get => _transcript;
        set => this.RaiseAndSetIfChanged(ref _transcript, value);
    }

    public bool IsRecognizing
    {
        get => _isRecognizing;
        set => this.RaiseAndSetIfChanged(ref _isRecognizing, value);
    }

    public ObservableCollection<TranslationLine> Translations { get; } = [];
}

public class AudioInputDevice
{
    public int DeviceNumber { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class TranslationLine : ReactiveObject
{
    private string _text = string.Empty;

    public string LanguageCode { get; set; } = string.Empty;
    public string LanguageName { get; set; } = string.Empty;

    public string ShortLanguageCode
    {
        get
        {
            if (string.IsNullOrWhiteSpace(LanguageCode))
                return string.Empty;

            var letters = new string(LanguageCode.Where(char.IsLetter).Take(2).ToArray());
            return letters.ToUpperInvariant();
        }
    }

    public string Text
    {
        get => _text;
        set => this.RaiseAndSetIfChanged(ref _text, value);
    }
}