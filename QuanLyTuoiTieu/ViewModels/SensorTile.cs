using QuanLyTuoiTieu.Models;

namespace QuanLyTuoiTieu.ViewModels;

/// <summary>
/// Một thẻ cảm biến trong cửa sổ chi tiết khu vực (mỗi loại cảm biến một thẻ).
/// Thẻ sáng: có cảm biến đang quản lý (kể cả chưa gửi dữ liệu). Thẻ mờ: chưa kết nối, đã gỡ, ESP32 ngừng.
/// </summary>
public class SensorTile
{
    /// <summary>Mã loại cảm biến (dùng nội bộ để kết nối thử, không hiển thị).</summary>
    public string TypeId { get; init; } = "";

    public string TypeName { get; init; } = "";

    /// <summary>true = thẻ sáng. false = thẻ mờ.</summary>
    public bool IsLit { get; init; }

    /// <summary>Giá trị hiển thị: số đo, "—" (chưa có dữ liệu) hoặc "Lỗi".</summary>
    public string ValueText { get; init; } = "—";
    public string UnitText { get; init; } = "";

    /// <summary>Ví dụ: "Đo lúc 10:42:11 · 3 phút trước".</summary>
    public string MeasuredText { get; init; } = "";

    /// <summary>Dùng để chọn màu viền và chữ trạng thái của thẻ sáng.</summary>
    public DataStatus DataStatus { get; init; }

    /// <summary>Thẻ sáng: trạng thái dữ liệu. Thẻ mờ: lý do chưa kết nối.</summary>
    public string StatusText { get; init; } = "";

    /// <summary>Dòng nhỏ cuối thẻ: ESP32 phụ trách, lý do lỗi.</summary>
    public string FooterText { get; init; } = "";

    /// <summary>true = đang hiển thị giá trị hợp lệ CUỐI CÙNG (dữ liệu cũ) vì bản ghi mới nhất bị lỗi.</summary>
    public bool IsOldValue { get; init; }

    public string TooltipText { get; init; } = "";

    /// <summary>true khi đang ở chế độ mô phỏng (thẻ bấm được để kết nối thử hoặc ngắt).</summary>
    public bool IsClickable { get; set; }
}