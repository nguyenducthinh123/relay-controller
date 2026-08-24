using System;
using System.Threading.Tasks;

namespace Relay.Services.Abstractions
{
    public interface IRelayController : IDisposable
    {
        string DeviceId { get; }
        bool IsConnected { get; }
        int SupportedChannels { get; }

        /// <summary>
        /// Mở kết nối tới phần cứng
        /// </summary>
        Task<bool> ConnectAsync(string deviceId);

        /// <summary>
        /// Đóng kết nối và giải phóng tài nguyên
        /// </summary>
        void Disconnect();

        /// <summary>
        /// Điều khiển bật/tắt một kênh cụ thể
        /// </summary>
        Task<bool> SetChannelStateAsync(byte channel, bool state);

        /// <summary>
        /// Đọc lại toàn bộ trạng thái thực tế của các kênh từ phần cứng
        /// </summary>
        Task<bool[]?> QueryAllStatesAsync();
    }
}