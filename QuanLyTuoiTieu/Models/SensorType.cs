namespace QuanLyTuoiTieu.Models;

/// <summary>
/// Loại cảm biến (danh mục), ví dụ: độ ẩm đất, EC. Có thể thêm loại mới.
/// Khác với Sensor (thiết bị cụ thể).
/// </summary>
public class SensorType
{
    /// <summary>Mã loại, duy nhất. Ví dụ: SOIL_MOISTURE.</summary>
    public string Id { get; set; } = "";

    /// <summary>Tên hiển thị tiếng Việt. Ví dụ: Độ ẩm đất.</summary>
    public string Name { get; set; } = "";

    /// <summary>Đơn vị đo mặc định, có thể sửa theo từng cảm biến.</summary>
    public string DefaultUnit { get; set; } = "";

    /// <summary>Phương pháp đo (chỉ điền khi có thông tin kỹ thuật thực tế, ví dụ với nitơ, oxy).</summary>
    public string? MeasurementMethod { get; set; }
}