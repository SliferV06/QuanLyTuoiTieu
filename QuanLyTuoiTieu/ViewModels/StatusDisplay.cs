using QuanLyTuoiTieu.Models;

namespace QuanLyTuoiTieu.ViewModels;

/// <summary>Chữ và biểu tượng hiển thị cho các trạng thái. Dùng chung cho mọi màn hình.</summary>
public static class StatusDisplay
{
    public static string ZoneStatusText(ZoneStatus? status) => status switch
    {
        ZoneStatus.NotConfigured => "Chưa cấu hình",
        ZoneStatus.Normal => "Bình thường",
        ZoneStatus.Warning => "Cảnh báo",
        ZoneStatus.Disconnected => "Mất kết nối",
        ZoneStatus.Disabled => "Ngừng hoạt động",
        _ => "Trống"
    };

    public static string ZoneStatusIcon(ZoneStatus? status) => status switch
    {
        ZoneStatus.NotConfigured => "○",
        ZoneStatus.Normal => "✔",
        ZoneStatus.Warning => "⚠",
        ZoneStatus.Disconnected => "✖",
        ZoneStatus.Disabled => "⊘",
        _ => ""
    };

    public static string SensorStatusText(SensorStatus status) => status switch
    {
        SensorStatus.Active => "Đang hoạt động",
        SensorStatus.Faulty => "Có lỗi",
        SensorStatus.Removed => "Đã gỡ",
        _ => ""
    };

    public static string DataStatusText(DataStatus status) => status switch
    {
        DataStatus.NoData => "Chưa có dữ liệu",
        DataStatus.Fresh => "Dữ liệu mới",
        DataStatus.Stale => "Dữ liệu đã cũ",
        DataStatus.Error => "Lỗi dữ liệu",
        _ => ""
    };

    public static string DataStatusIcon(DataStatus status) => status switch
    {
        DataStatus.NoData => "○",
        DataStatus.Fresh => "✔",
        DataStatus.Stale => "⚠",
        DataStatus.Error => "✖",
        _ => ""
    };
}