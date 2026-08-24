using System;
using System.Threading.Tasks;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Relay.ViewModels
{
    public partial class RelayChannelViewModel : ObservableObject
    {
        private readonly Func<byte, bool, Task<bool>> _toggleAction;

        public byte ChannelNumber { get; }

        [ObservableProperty]
        private string _name = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(StatusText))]
        [NotifyPropertyChangedFor(nameof(ButtonText))]
        [NotifyPropertyChangedFor(nameof(StatusBrush))]
        [NotifyPropertyChangedFor(nameof(ButtonBrush))]
        private bool _isOn;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(StatusText))]
        [NotifyPropertyChangedFor(nameof(ButtonText))]
        [NotifyPropertyChangedFor(nameof(StatusBrush))]
        [NotifyPropertyChangedFor(nameof(ButtonBrush))]
        [NotifyPropertyChangedFor(nameof(IsButtonEnabled))]
        private bool _isAvailable = true;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsButtonEnabled))]
        private bool _isBusy;

        public bool IsButtonEnabled => IsAvailable && !IsBusy;

        public string StatusText => !IsAvailable ? "⛔ DISABLED (2CH MODE)"
                                  : (IsOn ? "● ACTIVE (ON)" : "○ INACTIVE (OFF)");

        public string ButtonText => !IsAvailable ? "KHÔNG KHẢ DỤNG"
                                  : (IsOn ? $"TẮT {Name}" : $"BẬT {Name}");

        public Brush StatusBrush => !IsAvailable
            ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8"))
            : (IsOn ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#16A34A"))
                    : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B")));

        public Brush ButtonBrush => !IsAvailable
            ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CBD5E1"))
            : (IsOn ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DC2626"))
                    : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#475569")));

        public RelayChannelViewModel(byte channelNumber, string name, Func<byte, bool, Task<bool>> toggleAction)
        {
            ChannelNumber = channelNumber;
            Name = name;
            _toggleAction = toggleAction;
        }

        [RelayCommand]
        private async Task ToggleAsync()
        {
            if (!IsAvailable || IsBusy) return;

            IsBusy = true;
            try
            {
                bool targetState = !IsOn;
                bool ok = await _toggleAction(ChannelNumber, targetState);
                if (ok)
                {
                    IsOn = targetState;
                }
            }
            finally
            {
                IsBusy = false;
            }
        }
    }
}