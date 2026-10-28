using QuanLyTuoiTieu.Models;

namespace QuanLyTuoiTieu.ViewModels;

/// <summary>Toàn bộ dữ liệu hiển thị của cửa sổ chi tiết một khu vực.</summary>
public class ZoneDetail
{
    public string Code { get; init; } = "";
    public string Name { get; init; } = "";
    public string DescriptionText { get; init; } = "";

    public ZoneStatus Status { get; init; }
    public string StatusText => StatusDisplay.ZoneStatusText(Status);
    public string StatusIcon => StatusDisplay.ZoneStatusIcon(Status);
    public string StatusLabel => $"{StatusIcon} {StatusText}";

    public string NodesText { get; init; } = "";

    /// <summary>Số cảm biến đang quản lý (không tính cảm biến đã gỡ).</summary>
    public int ActiveSensorCount { get; init; }

    public string DataSummaryText { get; init; } = "";

    /// <summary>Mỗi loại cảm biến một thẻ (sáng hoặc mờ).</summary>
    public IReadOnlyList<SensorTile> Tiles { get; init; } = Array.Empty<SensorTile>();

    /// <summary>Vị trí cụm cảm biến, mốc lắp đặt và khoảng lệch.</summary>
    public ZonePositionInfo Position { get; init; } = new();
}