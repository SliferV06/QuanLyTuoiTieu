namespace QuanLyTuoiTieu.Models;

/// <summary>Một thiết bị cảm biến cụ thể. Không chứa lịch sử đo (lịch sử nằm ở Measurement).</summary>
public class Sensor
{
    /// <summary>Mã cảm biến, duy nhất toàn hệ thống.</summary>
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    /// <summary>Tham chiếu tới SensorType.Id.</summary>
    public string SensorTypeId { get; set; } = "";

    /// <summary>Khu vực lắp đặt, tham chiếu tới Zone.Code.</summary>
    public string ZoneCode { get; set; } = "";

    /// <summary>ESP32 phụ trách, tham chiếu tới Esp32Node.Id.</summary>
    public string NodeId { get; set; } = "";

    public string Unit { get; set; } = "";

    public SensorStatus Status { get; set; } = SensorStatus.Active;

    /// <summary>Ghi chú kỹ thuật.</summary>
    public string? Notes { get; set; }

    /// <summary>Thời điểm đăng ký trong phần mềm (UTC).</summary>
    public DateTime InstalledAt { get; set; } = DateTime.UtcNow;

    /// <summary>Thời điểm gỡ khỏi phần mềm (UTC). null = chưa gỡ.</summary>
    public DateTime? RemovedAt { get; set; }

    /// <summary>Lý do gỡ bỏ.</summary>
    public string? RemovalReason { get; set; }

    /// <summary>Nếu được thay thế, mã của cảm biến thay thế.</summary>
    public string? ReplacedBySensorId { get; set; }
}