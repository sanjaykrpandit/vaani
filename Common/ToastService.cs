using System;
using ReactiveUI;
using Avalonia.Threading;

namespace Vaani.Common
{
    public class ToastService : ReactiveObject
    {
        private string? _message;
        private bool _isVisible;
        private DispatcherTimer? _timer;
        private int _durationSeconds = 3;

        public string? Message
        {
            get => _message;
            private set => this.RaiseAndSetIfChanged(ref _message, value);
        }

        public bool IsVisible
        {
            get => _isVisible;
            private set => this.RaiseAndSetIfChanged(ref _isVisible, value);
        }

        public int DurationSeconds
        {
            get => _durationSeconds;
            set => _durationSeconds = value;
        }

        public void Show(string message, int? durationSeconds = null)
        {
            Message = message;
            IsVisible = true;
            _timer?.Stop();
            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(durationSeconds ?? _durationSeconds)
            };
            _timer.Tick += (s, e) => Hide();
            _timer.Start();
        }

        public void Hide()
        {
            IsVisible = false;
            Message = null;
            _timer?.Stop();
            _timer = null;
        }
    }
}
