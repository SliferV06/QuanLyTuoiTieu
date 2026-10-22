namespace QuanLyTuoiTieu.Services;

/// <summary>
/// Công cụ thử nghiệm, chỉ nguồn dữ liệu MÔ PHỎNG mới có.
/// Giao diện chỉ hiện các công cụ này khi nguồn dữ liệu hiện tại hỗ trợ interface này.
/// </summary>
public interface ISimulationControls
{
    /// <summary>true: lần đồng bộ sau không có phản hồi, chờ cho tới khi hết thời gian chờ.</summary>
    bool SimulateDisconnected { get; set; }

    /// <summary>Làm mọi giá trị đo hiện có "già" thêm một khoảng thời gian (mô phỏng thời gian trôi qua).</summary>
    void AgeMeasurements(TimeSpan span);

    /// <summary>
    /// Tạo khu vực mẫu (kèm ESP32 và cảm biến mô phỏng) cho mọi ô còn trống để đủ số khu vực tối đa.
    /// Khu vực, cảm biến, ESP32 đã có được giữ nguyên.
    /// </summary>
    void FillAllZones();

    /// <summary>Đặt lại toàn bộ về dữ liệu mẫu ban đầu (48 khu vực). Mọi thay đổi của người dùng bị mất.</summary>
    void ResetSampleData();
}