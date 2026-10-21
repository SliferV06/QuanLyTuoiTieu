namespace QuanLyTuoiTieu.Models;

/// <summary>Một bản ghi đo của một cảm biến tại một thời điểm.</summary>
public class Measurement
{
    public long Id { get; set; }

    /// <summary>Tham chiếu tới Sensor.Id.</summary>
    public string SensorId { get; set; } = "";

    /// <summary>Thời điểm node đo (UTC).</summary>
    public DateTime MeasuredAt { get; set; }

    /// <summary>Thời điểm ứng dụng nhận được bản ghi (UTC), nếu biết.</summary>
    public DateTime? ReceivedAt { get; set; }

    /// <summary>Giá trị đo. null khi lỗi hoặc không có giá trị (KHÔNG thay bằng 0).</summary>
    public double? Value { get; set; }

    /// <summary>Có nội dung = bản ghi lỗi.</summary>
    public string? ErrorMessage { get; set; }

    public bool IsError => !string.IsNullOrEmpty(ErrorMessage);
}