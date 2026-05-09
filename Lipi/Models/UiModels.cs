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
    public string Id { get; set; } = string.Empty;
    public int? DeviceNumber { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public bool IsBluetoothOrHeadset { get; set; }
}

public enum LipiConnectionMode
{
    Server = 1,
    DirectAzure = 2
}

public class ConnectionModeOption
{
    public LipiConnectionMode Mode { get; set; }
    public string DisplayName { get; set; } = string.Empty;
}

public class TranslationLine : ReactiveObject
{
    private string _text = string.Empty;
    private double _displayFontSize = 24;

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

    public double DisplayFontSize
    {
        get => _displayFontSize;
        set => this.RaiseAndSetIfChanged(ref _displayFontSize, value);
    }
}

public class SubtitleLanguageLine : ReactiveObject
{
    private string _olderRecognizedText = string.Empty;
    private string _latestText = string.Empty;
    private bool _isLatestRecognizing;
    private double _displayFontSize = 24;
    private string _subtitleBackgroundBrush = "#80161616";

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

    public string OlderRecognizedText
    {
        get => _olderRecognizedText;
        set
        {
            this.RaiseAndSetIfChanged(ref _olderRecognizedText, value);
            this.RaisePropertyChanged(nameof(HasOlderRecognizedText));
        }
    }

    public string LatestText
    {
        get => _latestText;
        set
        {
            this.RaiseAndSetIfChanged(ref _latestText, value);
            this.RaisePropertyChanged(nameof(HasLatestText));
            this.RaisePropertyChanged(nameof(IsLatestRecognized));
        }
    }

    public bool IsLatestRecognizing
    {
        get => _isLatestRecognizing;
        set
        {
            this.RaiseAndSetIfChanged(ref _isLatestRecognizing, value);
            this.RaisePropertyChanged(nameof(IsLatestRecognized));
        }
    }

    public double DisplayFontSize
    {
        get => _displayFontSize;
        set => this.RaiseAndSetIfChanged(ref _displayFontSize, value);
    }

    public string SubtitleBackgroundBrush
    {
        get => _subtitleBackgroundBrush;
        set => this.RaiseAndSetIfChanged(ref _subtitleBackgroundBrush, value);
    }

    public bool HasOlderRecognizedText => !string.IsNullOrWhiteSpace(OlderRecognizedText);
    public bool HasLatestText => !string.IsNullOrWhiteSpace(LatestText);
    public bool IsLatestRecognized => HasLatestText && !IsLatestRecognizing;
}