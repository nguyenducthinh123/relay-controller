using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Relay.Models;

namespace Relay.Services.Abstractions
{
    public interface IDeviceScanner
    {
        /// <summary>
        /// Quét toàn bộ danh sách thiết bị HID và Serial COM đang cắm
        /// </summary>
        Task<List<DeviceInfo>> ScanDevicesAsync();

        /// <summary>
        /// Quét và tự động thử bắt tay (handshake) với thiết bị phản hồi hợp lệ
        /// </summary>
        Task<(bool Success, DeviceInfo? Device, IRelayController? Controller, bool[]? InitialStates)> AutoConnectAsync(CancellationToken token);
    }
}