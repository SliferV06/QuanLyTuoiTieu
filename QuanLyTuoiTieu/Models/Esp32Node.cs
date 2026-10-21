namespace QuanLyTuoiTieu.Models;

/// <summary>Một node ESP32. Một node có thể phụ trách nhiều khu vực và nhiều cảm biến.</summary>
public class Esp32Node
{
    /// <summary>Mã node, duy nhất. Ví dụ: ESP-01. Khác mã khu vực và mã cảm biến.</summary>
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    public string? Notes { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Thời điểm đăng ký ESP32 trong phần mềm (UTC).</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}