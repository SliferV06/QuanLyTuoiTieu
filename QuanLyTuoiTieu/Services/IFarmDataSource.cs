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

    /// <summary>
    /// Bản ghi đo HỢP LỆ gần nhất của từng cảm biến (có giá trị, không phải bản ghi lỗi).
    /// Dùng để hiển thị dữ liệu cũ khi bản ghi mới nhất là lỗi. Cảm biến chưa từng có số đo hợp lệ
    /// thì không có trong từ điển.
    /// </summary>
    IReadOnlyDictionary<string, Measurement> GetLastValidMeasurements();

    /// <summary>
    /// Vị trí GPS mới nhất do cụm cảm biến của từng khu vực báo về (khóa = mã khu vực).
    /// Khu vực chưa có tín hiệu thì không có trong từ điển.
    /// </summary>
    IReadOnlyDictionary<string, GpsPosition> GetZonePositions();

    /// <summary>Mốc lắp đặt (vị trí đặt khối cảm biến) của từng khu vực (khóa = mã khu vực).</summary>
    IReadOnlyDictionary<string, GpsPosition> GetZoneHomes();

    /// <summary>Đặt hoặc đổi mốc lắp đặt của khu vực. Ném InvalidOperationException nếu không tìm thấy khu vực.</summary>
    void SetZoneHome(string zoneCode, GpsPosition home);

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

    // ===== Khu vực =====

    /// <summary>Thêm khu vực mới. Ném InvalidOperationException nếu mã trùng hoặc đã đủ số khu vực tối đa.</summary>
    void AddZone(Zone zone);

    /// <summary>Cập nhật tên, mô tả của khu vực có cùng mã.</summary>
    void UpdateZone(Zone zone);

    /// <summary>
    /// Xóa khu vực (ô trở thành trống). Ném InvalidOperationException nếu không tìm thấy
    /// hoặc khu vực còn cảm biến (phải xóa cảm biến trước).
    /// </summary>
    void DeleteZone(string zoneCode);

    // ===== Cảm biến =====

    /// <summary>Thêm cảm biến mới. Ném InvalidOperationException nếu mã cảm biến đã tồn tại.</summary>
    void AddSensor(Sensor sensor);

    /// <summary>Cập nhật tên, loại, khu vực, ESP32, đơn vị, trạng thái, ghi chú của cảm biến có cùng mã.</summary>
    void UpdateSensor(Sensor sensor);

    /// <summary>
    /// Đánh dấu cảm biến là đã gỡ (chưa xóa dữ liệu).
    /// replacedBySensorId: mã cảm biến thay thế (nếu có).
    /// removedWithNodeId: mã ESP32 nếu cảm biến bị gỡ cùng lúc ESP32 ngừng sử dụng (để khôi phục khi dùng lại).
    /// </summary>
    void RemoveSensor(string sensorId, DateTime removedAtUtc, string? replacedBySensorId,
                      string? removedWithNodeId = null);

    /// <summary>Khôi phục cảm biến đã gỡ, ghi historyNote vào ghi chú để giữ dấu vết.</summary>
    void RestoreSensor(string sensorId, string historyNote);

    /// <summary>Xóa HẲN cảm biến cùng giá trị đo của nó (không khôi phục được).</summary>
    void DeleteSensor(string sensorId);

    // ===== ESP32 =====

    /// <summary>Thêm ESP32 mới. Ném InvalidOperationException nếu mã đã tồn tại.</summary>
    void AddNode(Esp32Node node);

    /// <summary>Cập nhật tên, ghi chú, trạng thái sử dụng của ESP32 có cùng mã.</summary>
    void UpdateNode(Esp32Node node);

    /// <summary>
    /// Xóa HẲN ESP32. Ném InvalidOperationException nếu không tìm thấy
    /// hoặc ESP32 còn cảm biến gắn với nó (phải xóa cảm biến trước).
    /// </summary>
    void DeleteNode(string nodeId);
}