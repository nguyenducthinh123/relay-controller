using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HidSharp;
using Relay.Models;
using Relay.Services.Abstractions;

namespace Relay.Services.Implementations
{
    public class DeviceScanner : IDeviceScanner
    {
        private const int HidVid = 0x16C0;
        private const int HidPid = 0x05DF;

        public Task<List<DeviceInfo>> ScanDevicesAsync()
        {
            return Task.Run(() =>
            {
                var list = new List<DeviceInfo>();

                // 1. Quét USB HID Devices
                try
                {
                    var hidDevices = DeviceList.Local.GetHidDevices(HidVid, HidPid).ToList();
                    for (int i = 0; i < hidDevices.Count; i++)
                    {
                        list.Add(new DeviceInfo
                        {
                            DisplayName = $"[USB-HID] Relay #{i + 1} (VID:{HidVid:X4} PID:{HidPid:X4})",
                            DeviceId = hidDevices[i].DevicePath,
                            ProtocolType = DeviceProtocolType.UsbHid
                        });
                    }
                }
                catch { }

                // 2. Quét Serial COM Ports
                try
                {
                    var comPorts = SerialPort.GetPortNames();
                    foreach (var port in comPorts)
                    {
                        list.Add(new DeviceInfo
                        {
                            DisplayName = $"[Serial-LCUS] {port}",
                            DeviceId = port,
                            ProtocolType = DeviceProtocolType.SerialLcus
                        });
                    }
                }
                catch { }

                return list;
            });
        }

        public async Task<(bool Success, DeviceInfo? Device, IRelayController? Controller, bool[]? InitialStates)> AutoConnectAsync(CancellationToken token)
        {
            var devices = await ScanDevicesAsync();
            if (devices.Count == 0 || token.IsCancellationRequested)
            {
                return (false, null, null, null);
            }

            // Ưu tiên 1: USB HID
            var hidDevice = devices.FirstOrDefault(d => d.ProtocolType == DeviceProtocolType.UsbHid);
            if (hidDevice != null && !token.IsCancellationRequested)
            {
                var hidCtrl = new HidCustomRelayController();
                if (await hidCtrl.ConnectAsync(hidDevice.DeviceId))
                {
                    var states = await hidCtrl.QueryAllStatesAsync();
                    return (true, hidDevice, hidCtrl, states);
                }
                hidCtrl.Dispose();
            }

            // Ưu tiên 2: Serial COM (quét song song các cổng tìm được)
            var serialDevices = devices.Where(d => d.ProtocolType == DeviceProtocolType.SerialLcus).ToList();
            if (serialDevices.Count > 0 && !token.IsCancellationRequested)
            {
                var scanTasks = serialDevices.Select(async dev =>
                {
                    if (token.IsCancellationRequested) return (Success: false, Device: dev, Controller: (IRelayController?)null);

                    var ctrl = new LcusSerialRelayController();
                    bool ok = await ctrl.ConnectAsync(dev.DeviceId);
                    if (ok)
                    {
                        return (Success: true, Device: dev, Controller: (IRelayController?)ctrl);
                    }
                    ctrl.Dispose();
                    return (Success: false, Device: dev, Controller: (IRelayController?)null);
                }).ToList();

                var results = await Task.WhenAll(scanTasks);
                var valid = results.FirstOrDefault(r => r.Success && r.Controller != null);

                // Dọn dẹp tất cả các controller khác ngoại trừ cái được chọn
                foreach (var r in results)
                {
                    if (r.Controller != null && r.Controller != valid.Controller)
                    {
                        r.Controller.Dispose();
                    }
                }

                if (valid.Success && valid.Controller != null && !token.IsCancellationRequested)
                {
                    var states = await valid.Controller.QueryAllStatesAsync();
                    return (true, valid.Device, valid.Controller, states);
                }
            }

            return (false, null, null, null);
        }
    }
}