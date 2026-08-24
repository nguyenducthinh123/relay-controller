using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HidSharp;
using Relay.Services.Abstractions;

namespace Relay.Services.Implementations
{
    public class HidCustomRelayController : IRelayController
    {
        private const int VendorId = 0x16C0;
        private const int ProductId = 0x05DF;

        private HidDevice? _hidDevice;
        private HidStream? _hidStream;
        private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);
        private readonly bool[] _states = new bool[4];

        public string DeviceId { get; private set; } = string.Empty;
        public bool IsConnected => _hidStream != null && _hidDevice != null;
        public int SupportedChannels { get; private set; } = 2;

        public async Task<bool> ConnectAsync(string devicePath)
        {
            await _lock.WaitAsync();
            try
            {
                DisconnectInternal();
                DeviceId = devicePath;

                _hidDevice = DeviceList.Local.GetHidDevices(VendorId, ProductId)
                                            .FirstOrDefault(d => string.IsNullOrEmpty(devicePath) || d.DevicePath == devicePath);

                if (_hidDevice == null) return false;

                if (!_hidDevice.TryOpen(out _hidStream)) return false;

                var states = await QueryAllStatesInternalAsync();
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
            if (!IsConnected || _hidStream == null) return false;

            byte[] report = new byte[9];
            report[0] = 0x00; // Report ID
            report[1] = (byte)(state ? 0xFF : 0xFD); // BẬT = 0xFF, TẮT = 0xFD
            report[2] = channel;

            await _lock.WaitAsync();
            try
            {
                if (_hidStream == null) return false;

                _hidStream.SetFeature(report);
                _states[channel - 1] = state;
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
                return await QueryAllStatesInternalAsync();
            }
            finally
            {
                _lock.Release();
            }
        }

        private Task<bool[]?> QueryAllStatesInternalAsync()
        {
            return Task.Run(() =>
            {
                if (_hidStream == null) return null;
                try
                {
                    byte[] report = new byte[9];
                    report[0] = 0x00;
                    _hidStream.GetFeature(report);

                    byte stateMask = report[7] != 0 ? report[7] : report[8];

                    for (int i = 0; i < 4; i++)
                    {
                        _states[i] = (stateMask & (1 << i)) != 0;
                    }
                    return (bool[])_states.Clone();
                }
                catch
                {
                    return (bool[])_states.Clone();
                }
            });
        }

        private void DisconnectInternal()
        {
            try
            {
                _hidStream?.Dispose();
                _hidStream = null;
                _hidDevice = null;
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