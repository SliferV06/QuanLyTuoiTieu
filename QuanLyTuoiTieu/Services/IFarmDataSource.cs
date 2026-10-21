using QuanLyTuoiTieu.Models;

namespace QuanLyTuoiTieu.Services;

/// <summary>
/// Nguồn dữ liệu của trang trại. Giao diện chỉ làm việc với interface này,
/// nên sau này có thể thay dữ liệu mô phỏng bằng dữ liệu từ máy chủ.
/// </summary>
public interface IFarmDataSource
{
    IReadOnlyList<Zone> GetZones();
    IReadOnlyList<Esp32Node> GetNodes();
    IReadOnlyList<SensorType> GetSensorTypes();
    IReadOnlyList<Sensor> GetSensors();

    /// <summary>
    /// Bản ghi đo gần nhất của từng cảm biến (khóa = Sensor.Id).
    /// Cảm biến chưa có dữ liệu thì không có trong từ điển.
    /// </summary>
    IReadOnlyDictionary<string, Measurement> GetLatestMeasurements();

    /// <summary>Tên nguồn dữ liệu hiển thị cho người dùng, ví dụ "Dữ liệu mô phỏng".</summary>
    string SourceName { get; }

    /// <summary>true nếu đây là dữ liệu mô phỏng (không phải dữ liệu đo thật).</summary>
    bool IsSimulated { get; }

    /// <summary>
    /// Tải lại CÁC GIÁ TRỊ ĐO từ nguồn mà không chặn giao diện.
    /// Chỉ thay dữ liệu hiện có khi tải THÀNH CÔNG; nếu lỗi thì ném ngoại lệ và giữ nguyên dữ liệu cũ.
    /// Không làm mất cấu hình (khu vực, cảm biến, ESP32) đã chỉnh sửa.
    /// </summary>
    Task RefreshAsync(CancellationToken cancellationToken);

    /// <summary>Thêm khu vực mới. Ném InvalidOperationException nếu mã trùng hoặc đã đủ 144.</summary>
    void AddZone(Zone zone);

    /// <summary>Cập nhật tên, mô tả, trạng thái sử dụng của khu vực có cùng mã.</summary>
    void UpdateZone(Zone zone);

    /// <summary>Thêm cảm biến mới. Ném InvalidOperationException nếu mã cảm biến đã tồn tại.</summary>
    void AddSensor(Sensor sensor);

    /// <summary>
    /// Cập nhật tên, loại, khu vực, ESP32, đơn vị, trạng thái, ghi chú của cảm biến có cùng mã.
    /// Ném InvalidOperationException nếu không tìm thấy cảm biến.
    /// </summary>
    void UpdateSensor(Sensor sensor);

    /// <summary>
    /// Đánh dấu cảm biến là đã gỡ (KHÔNG xóa dữ liệu, giữ lịch sử).
    /// replacedBySensorId: mã cảm biến thay thế (nếu có).
    /// Ném InvalidOperationException nếu không tìm thấy hoặc đã gỡ trước đó.
    /// </summary>
    void RemoveSensor(string sensorId, DateTime removedAtUtc, string? replacedBySensorId);

    /// <summary>
    /// Khôi phục cảm biến đã gỡ: đặt lại "Đang hoạt động", xóa thông tin gỡ,
    /// và ghi historyNote vào ghi chú để giữ dấu vết.
    /// Ném InvalidOperationException nếu không tìm thấy hoặc cảm biến chưa bị gỡ.
    /// </summary>
    void RestoreSensor(string sensorId, string historyNote);

    /// <summary>Thêm ESP32 mới. Ném InvalidOperationException nếu mã đã tồn tại.</summary>
    void AddNode(Esp32Node node);

    /// <summary>Cập nhật tên, ghi chú, trạng thái sử dụng của ESP32 có cùng mã.</summary>
    void UpdateNode(Esp32Node node);
}