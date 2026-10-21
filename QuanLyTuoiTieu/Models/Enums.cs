namespace QuanLyTuoiTieu.Models;

/// <summary>Trạng thái hiển thị của khu vực (được TÍNH từ cảm biến và dữ liệu, không nhập tay).</summary>
public enum ZoneStatus
{
    NotConfigured,  // Chưa cấu hình (chưa có cảm biến)
    Normal,         // Đang hoạt động bình thường
    Warning,        // Có cảnh báo
    Disconnected,   // Mất kết nối / không nhận được dữ liệu mới
    Disabled        // Ngừng hoạt động (người dùng đặt IsActive = false)
}

/// <summary>Trạng thái của thiết bị cảm biến.</summary>
public enum SensorStatus
{
    Active,   // Đang hoạt động
    Faulty,   // Có lỗi
    Removed   // Đã gỡ / ngừng hoạt động (vẫn giữ lịch sử)
}

/// <summary>Trạng thái dữ liệu đo gần nhất của một cảm biến.</summary>
public enum DataStatus
{
    NoData, // Chưa có dữ liệu
    Fresh,  // Dữ liệu mới
    Stale,  // Dữ liệu đã cũ
    Error   // Bản ghi gần nhất là lỗi
}