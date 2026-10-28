using QuanLyTuoiTieu.Models;

namespace QuanLyTuoiTieu.ViewModels;

/// <summary>Thông tin vị trí cụm cảm biến của một khu vực, đã định dạng sẵn để hiển thị.</summary>
public class ZonePositionInfo
{
    /// <summary>Vị trí GPS mới nhất do cụm cảm biến báo về. null = chưa có tín hiệu.</summary>
    public GpsPosition? Current { get; init; }

    /// <summary>Mốc lắp đặt (vị trí đặt khối cảm biến). null = chưa có mốc.</summary>
    public GpsPosition? Home { get; init; }

    public bool HasPosition => Current != null;
    public bool HasHome => Home != null;

    /// <summary>Khoảng cách (mét) từ vị trí hiện tại tới mốc. null nếu thiếu một trong hai.</summary>
    public double? DistanceMeters { get; init; }

    /// <summary>true = lệch khỏi mốc quá ngưỡng cảnh báo.</summary>
    public bool IsMoved { get; init; }

    /// <summary>true = có cả vị trí và mốc, và vẫn trong ngưỡng (đúng vị trí).</summary>
    public bool IsOk { get; init; }

    public string PositionText { get; init; } = "";
    public string UpdatedText { get; init; } = "";
    public string HomeText { get; init; } = "";
    public string StatusText { get; init; } = "";
}