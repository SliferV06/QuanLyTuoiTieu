using QuanLyTuoiTieu.Models;

namespace QuanLyTuoiTieu.ViewModels;

/// <summary>
/// Một thẻ cảm biến trong cửa sổ chi tiết khu vực (mỗi loại cảm biến một thẻ).
/// Thẻ sáng: cảm biến đang quản lý và đã có dữ liệu. Thẻ mờ: còn lại.
/// </summary>
public class SensorTile
{
    /// <summary>Mã loại cảm biến (dùng nội bộ để kết nối thử, không hiển thị).</summary>
    public string TypeId { get; init; } = "";

    public string TypeName { get; init; } = "";

    /// <summary>true = thẻ sáng (đã kết nối, hiện thông số). false = thẻ mờ.</summary>
    public bool IsLit { get; init; }

    /// <summary>Giá trị hiển thị: số đo, "Lỗi", hoặc "≈ số" khi là giá trị ước tính.</summary>
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

    /// <summary>
    /// Thông báo thêm: giá trị này là ước tính từ khu vực lân cận, hoặc vì sao chưa ước tính được.
    /// </summary>
    public string NoticeText { get; init; } = "";

    public bool HasNotice => !string.IsNullOrEmpty(NoticeText);

    /// <summary>true = giá trị hiển thị là ƯỚC TÍNH, không phải số đo thật.</summary>
    public bool IsEstimated { get; init; }

    public string TooltipText { get; init; } = "";

    /// <summary>true khi đang ở chế độ mô phỏng (thẻ bấm được để kết nối thử hoặc ngắt).</summary>
    public bool IsClickable { get; set; }
}