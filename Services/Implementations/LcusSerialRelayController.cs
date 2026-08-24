using System;
using System.IO;
using System.IO.Ports;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Relay.Services.Abstractions;

namespace Relay.Services.Implementations
{
    public class LcusSerialRelayController : IRelayController
    {
        private SerialPort? _serialPort;
        private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);
        private readonly bool[] _states = new bool[4];

        public string DeviceId { get; private set; } = string.Empty;
        public bool IsConnected => _serialPort != null && _serialPort.IsOpen;
        public int SupportedChannels { get; private set; } = 4;

        public async Task<bool> ConnectAsync(string portName)
        {
            await _lock.WaitAsync();
            try
            {
                DisconnectInternal();
                DeviceId = portName;

                _serialPort = new SerialPort(portName, 9600, Parity.None, 8, StopBits.One)
                {
                    ReadTimeout = 250,
                    WriteTimeout = 250
                };
                _serialPort.Open();

                // Chờ phần cứng/cổng ảo sẵn sàng
                await Task.Delay(30);

                var states = await QueryAllStatesInternalAsync(250);
                return states != null;
            }
            catch
            {
                DisconnectInternal();
                return false;
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task<bool> SetChannelStateAsync(byte channel, bool state)
        {
            if (!IsConnected || _serialPort == null) return false;

            byte stateByte = (byte)(state ? 0x01 : 0x00);
            byte checksum = (byte)((0xA0 + channel + stateByte) & 0xFF);
            byte[] command = { 0xA0, channel, stateByte, checksum };

            await _lock.WaitAsync();
            try
            {
                if (_serialPort == null || !_serialPort.IsOpen) return false;

                _serialPort.DiscardInBuffer();
                _serialPort.DiscardOutBuffer();

                // 1. Gửi lệnh điều khiển
                _serialPort.Write(command, 0, command.Length);

                // 2. Gửi ngay byte 0xFF để yêu cầu phản hồi trạng thái xác thực
                _serialPort.Write(new byte[] { 0xFF }, 0, 1);

                string response = await ReadUntilPromptAsync(150);
                if (string.IsNullOrEmpty(response))
                {
                    DisconnectInternal();
                    return false;
                }

                ParseStates(response);
                return true;
            }
            catch
            {
                DisconnectInternal();
                return false;
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task<bool[]?> QueryAllStatesAsync()
        {
            await _lock.WaitAsync();
            try
            {
                return await QueryAllStatesInternalAsync(150);
            }
            finally
            {
                _lock.Release();
            }
        }

        private async Task<bool[]?> QueryAllStatesInternalAsync(int timeoutMs)
        {
            if (_serialPort == null || !_serialPort.IsOpen) return null;

            try
            {
                _serialPort.DiscardInBuffer();
                _serialPort.DiscardOutBuffer();
                _serialPort.Write(new byte[] { 0xFF }, 0, 1);

                string response = await ReadUntilPromptAsync(timeoutMs);
                if (string.IsNullOrEmpty(response)) return null;

                ParseStates(response);
                return (bool[])_states.Clone();
            }
            catch
            {
                return null;
            }
        }

        private Task<string> ReadUntilPromptAsync(int timeoutMs)
        {
            return Task.Run(() =>
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var sb = new StringBuilder();

                while (sw.ElapsedMilliseconds < timeoutMs)
                {
                    try
                    {
                        if (_serialPort != null && _serialPort.IsOpen && _serialPort.BytesToRead > 0)
                        {
                            byte[] buf = new byte[_serialPort.BytesToRead];
                            int count = _serialPort.Read(buf, 0, buf.Length);
                            sb.Append(Encoding.ASCII.GetString(buf, 0, count));

                            string current = sb.ToString();
                            if (current.Contains("CH1:")) return current;
                        }
                    }
                    catch { break; }
                    Thread.Sleep(10);
                }
                return string.Empty;
            });
        }

        private void ParseStates(string data)
        {
            bool hasCh3 = data.Contains("CH3:", StringComparison.OrdinalIgnoreCase);
            bool hasCh4 = data.Contains("CH4:", StringComparison.OrdinalIgnoreCase);
            SupportedChannels = (hasCh3 && hasCh4) ? 4 : 2;

            for (int i = 1; i <= SupportedChannels; i++)
            {
                string tag = $"CH{i}:";
                int pos = data.IndexOf(tag, StringComparison.OrdinalIgnoreCase);
                if (pos != -1)
                {
                    string sub = data.Substring(pos + 4, Math.Min(3, data.Length - (pos + 4)));
                    _states[i - 1] = sub.StartsWith("ON", StringComparison.OrdinalIgnoreCase);
                }
            }
        }

        private void DisconnectInternal()
        {
            try
            {
                if (_serialPort != null)
                {
                    if (_serialPort.IsOpen)
                    {
                        try { _serialPort.Close(); } catch { }
                    }
                    _serialPort.Dispose();
                    _serialPort = null;
                }
            }
            catch { }
        }

        public void Disconnect()
        {
            _lock.Wait();
            try { DisconnectInternal(); }
            finally { _lock.Release(); }
        }

        public void Dispose()
        {
            Disconnect();
            _lock.Dispose();
        }
    }
}