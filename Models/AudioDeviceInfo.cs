using ReactiveUI;

namespace Vaani.Models;

public class AudioDeviceInfo : ReactiveObject
{
    private bool _isSelected;

    public string Id { get; set; } = string.Empty;
    public string FriendlyName { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public bool IsCableDevice { get; set; }

    public bool IsSelected
    {
        get => _isSelected;
        set => this.RaiseAndSetIfChanged(ref _isSelected, value);
    }

    public override string ToString() => FriendlyName;
}
