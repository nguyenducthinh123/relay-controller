using System;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace Relay
{
    public enum LogLevel
    {
        Info,       // Đen / Xám đậm
        Success,    // Xanh lá
        Warning,    // Vàng cam
        Error       // Đỏ
    }

    public partial class MainWindow : Window
    {
        private const int WM_DEVICECHANGE = 0x0219;
        private const int DBT_DEVICEREMOVECOMPLETE = 0x8004;
        private const int DBT_DEVICEARRIVAL = 0x8000;

        private SerialPort? _serialPort;
        private readonly bool[] _relayStates = new bool[4];
        private CancellationTokenSource? _scanCts;
        private readonly SemaphoreSlim _serialLock = new SemaphoreSlim(1, 1);

        public MainWindow()
        {
            InitializeComponent();
            RefreshPortList();
            LogStatus("Sẵn sàng. Bấm 'Quét & Auto Connect' để tự động tìm thiết bị.", LogLevel.Info);
        }

        private void LogStatus(string message, LogLevel level)
        {
            Dispatcher.InvokeAsync(() =>
            {
                txtSystemStatus.Text = message;
                txtSystemStatus.Foreground = level switch
                {
                    LogLevel.Success => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#16A34A")), // Xanh lá đậm
                    LogLevel.Warning => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D97706")), // Vàng cam đậm
                    LogLevel.Error => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DC2626")), // Đỏ tươi đậm
                    _ => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E293B"))  // Đen slate
                };
            });
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var source = PresentationSource.FromVisual(this) as HwndSource;
            source?.AddHook(HwndMessageHook);
        }

        private IntPtr HwndMessageHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_DEVICECHANGE)
            {
                int eventCode = wParam.ToInt32();
                if (eventCode == DBT_DEVICEREMOVECOMPLETE || eventCode == DBT_DEVICEARRIVAL)
                {
                    Dispatcher.InvokeAsync(() =>
                    {
                        RefreshPortList();
                        if (_serialPort != null && !_serialPort.IsOpen)
                        {
                            CloseConnection();
                            UpdateConnectUiState(false);
                            LogStatus("⚠️ Thiết bị đã bị ngắt kết nối vật lý!", LogLevel.Error);
                        }
                    });
                }
            }
            return IntPtr.Zero;
        }

        private void RefreshPortList()
        {
            var ports = SerialPort.GetPortNames();
            cboPorts.ItemsSource = ports;
            if (ports.Length > 0 && (cboPorts.SelectedItem == null || !ports.Contains(cboPorts.SelectedItem.ToString())))
            {
                cboPorts.SelectedIndex = 0;
            }
        }

        private async void btnScan_Click(object sender, RoutedEventArgs e)
        {
            btnScan.IsEnabled = false;

            _scanCts?.Cancel();
            _scanCts?.Dispose();
            _scanCts = new CancellationTokenSource();
            var token = _scanCts.Token;

            CloseConnection();
            UpdateConnectUiState(false);

            RefreshPortList();
            string[] ports = SerialPort.GetPortNames();

            if (ports.Length == 0)
            {
                LogStatus("Không tìm thấy cổng COM nào. Vui lòng kiểm tra kết nối vật lý giữa Relay và PC.", LogLevel.Error);
                btnScan.IsEnabled = true;
                return;
            }

            LogStatus("Đang quét các cổng COM...", LogLevel.Info);

            try
            {
                var scanTasks = ports.Select(port => TryPingPortOptimizedAsync(port, token)).ToList();
                var results = await Task.WhenAll(scanTasks);

                var found = results.FirstOrDefault(r => r.Success);

                if (found.Success && !token.IsCancellationRequested)
                {
                    try
                    {
                        _serialPort = new SerialPort(found.PortName, 9600, Parity.None, 8, StopBits.One)
                        {
                            ReadTimeout = 500,
                            WriteTimeout = 500
                        };
                        _serialPort.Open();
                        cboPorts.SelectedItem = found.PortName;
                        UpdateConnectUiState(true);
                        ParseStatusResponse(found.Response);
                        LogStatus($"⚡ Đã kết nối tự động: {found.PortName}", LogLevel.Success);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        LogStatus($"Cổng {found.PortName} đang bị ứng dụng khác chiếm giữ.", LogLevel.Error);
                    }
                    catch (Exception ex)
                    {
                        LogStatus($"Lỗi mở kết nối {found.PortName}: {ex.Message}", LogLevel.Error);
                    }
                }
                else if (!token.IsCancellationRequested)
                {
                    LogStatus("Không tìm thấy Relay phản hồi. Vui lòng kiểm tra kết nối vật lý giữa Relay và PC.", LogLevel.Error);
                }
            }
            catch (OperationCanceledException)
            {
                LogStatus("Đã hủy thao tác quét.", LogLevel.Info);
            }
            finally
            {
                btnScan.IsEnabled = true;
            }
        }

        private Task<(bool Success, string PortName, string Response)> TryPingPortOptimizedAsync(string portName, CancellationToken token)
        {
            return Task.Run(() =>
            {
                try
                {
                    if (token.IsCancellationRequested) return (false, portName, string.Empty);

                    using var sp = new SerialPort(portName, 9600, Parity.None, 8, StopBits.One)
                    {
                        ReadTimeout = 150,
                        WriteTimeout = 150
                    };

                    sp.Open();
                    Thread.Sleep(30);

                    sp.DiscardInBuffer();
                    sp.DiscardOutBuffer();

                    sp.Write(new byte[] { 0xFF }, 0, 1);

                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    var sb = new StringBuilder();

                    while (sw.ElapsedMilliseconds < 250 && !token.IsCancellationRequested)
                    {
                        if (sp.BytesToRead > 0)
                        {
                            byte[] buf = new byte[sp.BytesToRead];
                            int count = sp.Read(buf, 0, buf.Length);
                            sb.Append(Encoding.ASCII.GetString(buf, 0, count));

                            string currentText = sb.ToString();
                            if (currentText.Contains("CH1:"))
                            {
                                return (true, portName, currentText);
                            }
                        }
                        Thread.Sleep(10);
                    }
                }
                catch { }
                return (false, portName, string.Empty);
            }, token);
        }

        private void btnConnect_Click(object sender, RoutedEventArgs e)
        {
            if (_serialPort != null && _serialPort.IsOpen)
            {
                CloseConnection();
                UpdateConnectUiState(false);
                LogStatus("Đã ngắt kết nối.", LogLevel.Info);
            }
            else
            {
                if (cboPorts.SelectedItem == null)
                {
                    LogStatus("Chưa chọn cổng COM!", LogLevel.Warning);
                    MessageBox.Show("Vui lòng chọn cổng COM trước.", "Cảnh báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                string selectedPort = cboPorts.SelectedItem.ToString()!;
                try
                {
                    _serialPort = new SerialPort(selectedPort, 9600, Parity.None, 8, StopBits.One)
                    {
                        ReadTimeout = 300,
                        WriteTimeout = 300
                    };
                    _serialPort.Open();
                    UpdateConnectUiState(true);

                    _serialPort.DiscardInBuffer();
                    _serialPort.Write(new byte[] { 0xFF }, 0, 1);

                    Thread.Sleep(50);
                    string resp = _serialPort.ReadExisting();
                    if (!string.IsNullOrEmpty(resp))
                    {
                        ParseStatusResponse(resp);
                    }

                    LogStatus($"Đã kết nối thành công tới {selectedPort}.", LogLevel.Success);
                }
                catch (UnauthorizedAccessException)
                {
                    LogStatus($"Cổng {selectedPort} đang bị phần mềm khác sử dụng!", LogLevel.Error);
                    MessageBox.Show($"Cổng {selectedPort} đang bị phần mềm khác sử dụng!", "Cổng bận", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                catch (Exception ex)
                {
                    LogStatus($"Kết nối thất bại: {ex.Message}", LogLevel.Error);
                    MessageBox.Show($"Kết nối thất bại: {ex.Message}\nVui lòng kiểm tra lại kết nối vật lý giữa Relay và PC.", "Lỗi kết nối", MessageBoxButton.OK, MessageBoxImage.Error);
                    CloseConnection();
                    UpdateConnectUiState(false);
                }
            }
        }

        private async void RelayButton_Click(object sender, RoutedEventArgs e)
        {
            if (_serialPort == null || !_serialPort.IsOpen)
            {
                LogStatus("⚠️ Thao tác bị từ chối: Vui lòng kết nối tới cổng COM trước!", LogLevel.Warning);
                MessageBox.Show("Vui lòng kết nối tới cổng COM trước!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var btn = (Button)sender;
            btn.IsEnabled = false;

            byte channel = byte.Parse(btn.Tag.ToString()!);
            int idx = channel - 1;
            bool targetState = !_relayStates[idx];
            byte stateByte = (byte)(targetState ? 0x01 : 0x00);
            byte checksum = (byte)((0xA0 + channel + stateByte) & 0xFF);

            byte[] command = new byte[] { 0xA0, channel, stateByte, checksum };
            string verifiedResponse = string.Empty;

            await _serialLock.WaitAsync();
            try
            {
                if (_serialPort != null && _serialPort.IsOpen)
                {
                    _serialPort.DiscardInBuffer();
                    _serialPort.DiscardOutBuffer();

                    // 1. Gửi lệnh điều khiển
                    _serialPort.Write(command, 0, command.Length);

                    // 2. Gửi ngay byte 0xFF để yêu cầu phần cứng gửi lại bảng trạng thái xác thực
                    _serialPort.Write(new byte[] { 0xFF }, 0, 1);

                    // 3. Đọc dữ liệu phản hồi trong tối đa 150ms
                    verifiedResponse = await Task.Run(() =>
                    {
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        var sb = new StringBuilder();

                        while (sw.ElapsedMilliseconds < 150)
                        {
                            try
                            {
                                if (_serialPort != null && _serialPort.IsOpen && _serialPort.BytesToRead > 0)
                                {
                                    byte[] buf = new byte[_serialPort.BytesToRead];
                                    int count = _serialPort.Read(buf, 0, buf.Length);
                                    sb.Append(Encoding.ASCII.GetString(buf, 0, count));

                                    string text = sb.ToString();
                                    if (text.Contains("CH1:"))
                                    {
                                        return text;
                                    }
                                }
                            }
                            catch { break; }
                            Thread.Sleep(10);
                        }
                        return string.Empty;
                    });

                    if (string.IsNullOrEmpty(verifiedResponse))
                    {
                        throw new TimeoutException("Mạch không phản hồi trạng thái sau khi gửi lệnh.");
                    }

                    // Đồng bộ toàn bộ trạng thái thực tế từ mạch về UI
                    ParseStatusResponse(verifiedResponse);
                    LogStatus($"✔ Gửi lệnh thành công: CH{channel} -> {(targetState ? "BẬT" : "TẮT")}", LogLevel.Success);
                }
                else
                {
                    throw new IOException("Cổng COM không còn khả dụng.");
                }
            }
            catch (Exception ex)
            {
                SafeCloseConnectionInternal();
                UpdateConnectUiState(false);
                LogStatus("⚠️ Mất kết nối. Vui lòng kiểm tra kết nối vật lý giữa Relay và PC.", LogLevel.Error);
                MessageBox.Show($"Mất kết nối với thiết bị ({ex.Message}).\nVui lòng kiểm tra lại kết nối vật lý giữa Relay và PC.",
                                "Lỗi kết nối", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _serialLock.Release();
                if (_serialPort != null && _serialPort.IsOpen)
                {
                    btn.IsEnabled = true;
                }
            }
        }

        private void ParseStatusResponse(string data)
        {
            bool hasCh3 = data.Contains("CH3:", StringComparison.OrdinalIgnoreCase);
            bool hasCh4 = data.Contains("CH4:", StringComparison.OrdinalIgnoreCase);

            for (int i = 1; i <= 2; i++)
            {
                string tag = $"CH{i}:";
                int pos = data.IndexOf(tag, StringComparison.OrdinalIgnoreCase);
                if (pos != -1)
                {
                    string sub = data.Substring(pos + 4, Math.Min(3, data.Length - (pos + 4)));
                    bool isOn = sub.StartsWith("ON", StringComparison.OrdinalIgnoreCase);
                    _relayStates[i - 1] = isOn;
                    SetChannelAvailability(i, true);
                    UpdateRelayCardUI((byte)i, isOn);
                }
            }

            if (hasCh3 && hasCh4)
            {
                SetChannelAvailability(3, true);
                SetChannelAvailability(4, true);

                for (int i = 3; i <= 4; i++)
                {
                    string tag = $"CH{i}:";
                    int pos = data.IndexOf(tag, StringComparison.OrdinalIgnoreCase);
                    if (pos != -1)
                    {
                        string sub = data.Substring(pos + 4, Math.Min(3, data.Length - (pos + 4)));
                        bool isOn = sub.StartsWith("ON", StringComparison.OrdinalIgnoreCase);
                        _relayStates[i - 1] = isOn;
                        UpdateRelayCardUI((byte)i, isOn);
                    }
                }
            }
            else
            {
                SetChannelAvailability(3, false);
                SetChannelAvailability(4, false);
            }
        }

        private void SetChannelAvailability(int channel, bool isAvailable)
        {
            Button btn = channel switch { 1 => btnCh1, 2 => btnCh2, 3 => btnCh3, _ => btnCh4 };
            TextBlock txt = channel switch { 1 => txtStatus1, 2 => txtStatus2, 3 => txtStatus3, _ => txtStatus4 };

            btn.IsEnabled = isAvailable;
            if (!isAvailable)
            {
                btn.Content = "KHÔNG KHẢ DỤNG";
                btn.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CBD5E1"));
                txt.Text = "⛔ DISABLED (2CH MODE)";
                txt.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8"));
                _relayStates[channel - 1] = false;
            }
        }

        private void UpdateRelayCardUI(byte channel, bool isOn)
        {
            Button btn = channel switch { 1 => btnCh1, 2 => btnCh2, 3 => btnCh3, _ => btnCh4 };
            TextBlock txt = channel switch { 1 => txtStatus1, 2 => txtStatus2, 3 => txtStatus3, _ => txtStatus4 };
            string name = channel switch { 1 => "NGUỒN", 2 => "BOOT", 3 => "IGN", _ => "CH4" };

            if (isOn)
            {
                btn.Content = $"TẮT {name}";
                btn.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DC2626"));
                txt.Text = "● ACTIVE (ON)";
                txt.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#16A34A"));
            }
            else
            {
                btn.Content = $"BẬT {name}";
                btn.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#475569"));
                txt.Text = "○ INACTIVE (OFF)";
                txt.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B"));
            }
        }

        private void UpdateConnectUiState(bool isConnected)
        {
            btnConnect.Content = isConnected ? "Ngắt kết nối" : "Kết nối";
            btnConnect.Background = isConnected ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DC2626"))
                                                : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#16A34A"));

            if (!isConnected)
            {
                for (int i = 1; i <= 4; i++)
                {
                    _relayStates[i - 1] = false;
                    UpdateRelayCardUI((byte)i, false);
                }
            }
        }

        private void SafeCloseConnectionInternal()
        {
            try
            {
                if (_serialPort != null)
                {
                    if (_serialPort.IsOpen)
                    {
                        try { _serialPort.Close(); } catch { }
                    }
                    try { _serialPort.Dispose(); } catch { }
                    _serialPort = null;
                }
            }
            catch { }
        }

        private void CloseConnection()
        {
            _serialLock.Wait();
            try
            {
                SafeCloseConnectionInternal();
            }
            finally
            {
                _serialLock.Release();
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _scanCts?.Cancel();
            CloseConnection();
            base.OnClosed(e);
        }
    }
}