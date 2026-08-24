using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HidSharp;
using Relay.Models;
using Relay.Services.Abstractions;
using Relay.Services.Implementations;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;

namespace Relay.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly IDeviceScanner _scanner;
        private IRelayController? _activeController;
        private CancellationTokenSource? _scanCts;

        [ObservableProperty]
        private ObservableCollection<DeviceInfo> _devices = new();

        [ObservableProperty]
        private DeviceInfo? _selectedDevice;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ConnectButtonText))]
        [NotifyPropertyChangedFor(nameof(ConnectButtonBrush))]
        private bool _isConnected;

        [ObservableProperty]
        private bool _isScanning;

        [ObservableProperty]
        private string _statusMessage = "Sẵn sàng. Bấm 'Quét & Auto Connect' để tự động tìm thiết bị.";

        [ObservableProperty]
        private Brush _statusBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E293B"));

        public ObservableCollection<RelayChannelViewModel> Channels { get; } = new();

        public string ConnectButtonText => IsConnected ? "Ngắt kết nối" : "Kết nối";

        public Brush ConnectButtonBrush => IsConnected
            ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DC2626"))
            : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#16A34A"));

        public MainViewModel()
        {
            _scanner = new DeviceScanner();

            // Khởi tạo 4 kênh mặc định
            Channels.Add(new RelayChannelViewModel(1, "NGUỒN", ToggleRelayAsync));
            Channels.Add(new RelayChannelViewModel(2, "BOOT", ToggleRelayAsync));
            Channels.Add(new RelayChannelViewModel(3, "IGN", ToggleRelayAsync));
            Channels.Add(new RelayChannelViewModel(4, "CH4", ToggleRelayAsync));

            _ = RefreshDeviceListAsync();
        }

        public void Log(string message, LogLevel level)
        {
            StatusMessage = message;
            StatusBrush = level switch
            {
                LogLevel.Success => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#16A34A")),
                LogLevel.Warning => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D97706")),
                LogLevel.Error => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DC2626")),
                _ => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E293B"))
            };
        }

        public async Task RefreshDeviceListAsync()
        {
            var list = await _scanner.ScanDevicesAsync();
            Devices.Clear();
            foreach (var dev in list)
            {
                Devices.Add(dev);
            }

            if (Devices.Count > 0 && SelectedDevice == null)
            {
                SelectedDevice = Devices[0];
            }
        }

        [RelayCommand]
        private async Task ScanAndAutoConnectAsync()
        {
            IsScanning = true;
            _scanCts?.Cancel();
            _scanCts?.Dispose();
            _scanCts = new CancellationTokenSource();

            DisconnectCurrent();
            Log("Đang quét các thiết bị USB HID và Serial...", LogLevel.Info);

            try
            {
                var (success, device, controller, initialStates) = await _scanner.AutoConnectAsync(_scanCts.Token);

                if (success && device != null && controller != null)
                {
                    _activeController = controller;
                    await RefreshDeviceListAsync();
                    SelectedDevice = Devices.FirstOrDefault(d => d.DeviceId == device.DeviceId) ?? device;

                    IsConnected = true;
                    ApplyHardwareStates(controller.SupportedChannels, initialStates);
                    Log($"⚡ Đã kết nối tự động: {device.DisplayName}", LogLevel.Success);
                }
                else
                {
                    await RefreshDeviceListAsync();
                    Log("Không tìm thấy Relay phản hồi. Vui lòng kiểm tra cáp cắm.", LogLevel.Error);
                }
            }
            catch (OperationCanceledException)
            {
                Log("Đã hủy thao tác quét.", LogLevel.Info);
            }
            finally
            {
                IsScanning = false;
            }
        }

        [RelayCommand]
        private async Task ToggleConnectAsync()
        {
            if (IsConnected)
            {
                DisconnectCurrent();
                Log("Đã ngắt kết nối.", LogLevel.Info);
                return;
            }

            if (SelectedDevice == null)
            {
                Log("⚠️ Vui lòng chọn một thiết bị trước.", LogLevel.Warning);
                return;
            }

            IRelayController controller = SelectedDevice.ProtocolType switch
            {
                DeviceProtocolType.UsbHid => new HidCustomRelayController(),
                _ => new LcusSerialRelayController()
            };

            bool ok = await controller.ConnectAsync(SelectedDevice.DeviceId);
            if (ok)
            {
                _activeController = controller;
                IsConnected = true;
                var states = await controller.QueryAllStatesAsync();
                ApplyHardwareStates(controller.SupportedChannels, states);
                Log($"Đã kết nối thành công tới {SelectedDevice.DisplayName}", LogLevel.Success);
            }
            else
            {
                controller.Dispose();
                DisconnectCurrent();
                Log($"Kết nối thất bại tới {SelectedDevice.DisplayName}.", LogLevel.Error);
            }
        }

        private async Task<bool> ToggleRelayAsync(byte channel, bool targetState)
        {
            if (!IsConnected || _activeController == null)
            {
                Log("⚠️ Thao tác bị từ chối: Chưa kết nối tới Relay!", LogLevel.Warning);
                return false;
            }

            bool ok = await _activeController.SetChannelStateAsync(channel, targetState);
            if (ok)
            {
                Log($"✔ Gửi lệnh thành công: CH{channel} -> {(targetState ? "BẬT" : "TẮT")}", LogLevel.Success);
                return true;
            }
            else
            {
                DisconnectCurrent();
                Log("⚠️ Mất kết nối trong lúc truyền dữ liệu!", LogLevel.Error);
                return false;
            }
        }

        private void ApplyHardwareStates(int supportedChannels, bool[]? states)
        {
            for (int i = 0; i < Channels.Count; i++)
            {
                bool isChannelSupported = (i + 1) <= supportedChannels;
                Channels[i].IsAvailable = isChannelSupported;
                Channels[i].IsOn = isChannelSupported && states != null && states.Length > i && states[i];
            }
        }

        public void DisconnectCurrent()
        {
            _activeController?.Dispose();
            _activeController = null;
            IsConnected = false;

            foreach (var ch in Channels)
            {
                ch.IsOn = false;
            }
        }

        public void HandleHardwareUnplugged()
        {
            if (_activeController != null && !_activeController.IsConnected)
            {
                DisconnectCurrent();
                Log("⚠️ Thiết bị đã bị ngắt kết nối vật lý!", LogLevel.Error);
            }
            _ = RefreshDeviceListAsync();
        }
    }
}