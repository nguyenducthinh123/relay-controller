namespace Relay.Models
{
    public enum LogLevel
    {
        Info,       // Đen / Xám đậm
        Success,    // Xanh lá
        Warning,    // Vàng cam
        Error       // Đỏ
    }
    public enum DeviceProtocolType
    {
        SerialLcus, // Mạch Serial chuẩn LCUS (cổng COM)
        UsbHid      // Mạch USB HID (VID: 0x16C0, PID: 0x05DF)
    }

    public class DeviceInfo
    {
        public string DisplayName { get; set; } = string.Empty;
        public string DeviceId { get; set; } = string.Empty; // Chuỗi PortName (COMx) hoặc DevicePath của HID
        public DeviceProtocolType ProtocolType { get; set; }

        public override string ToString() => DisplayName;
    }
}