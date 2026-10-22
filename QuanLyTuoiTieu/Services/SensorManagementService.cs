using System.Globalization;
using System.Text.RegularExpressions;
using QuanLyTuoiTieu.Models;

namespace QuanLyTuoiTieu.Services;

/// <summary>
/// Quản lý cảm biến: kiểm tra dữ liệu đầu vào rồi thêm/sửa/gỡ/khôi phục/thay thế trong nguồn dữ liệu.
/// Các phương thức thao tác trả về null nếu thành công, hoặc câu thông báo lỗi tiếng Việt.
/// </summary>
public class SensorManagementService
{
    private const int MaxNameLength = 100;
    private const int MaxUnitLength = 20;
    private const int MaxNotesLength = 500;

    /// <summary>Số cảm biến dự kiến tối đa cho mỗi ESP32 (chỉ để nhắc, không chặn).</summary>
    public const int ExpectedMaxSensorsPerNode = 12;

    // Mã cảm biến (đã đổi sang chữ in hoa): chữ A-Z, số, gạch ngang, gạch dưới; dài 3-30 ký tự.
    private static readonly Regex IdPattern = new("^[A-Z0-9_-]{3,30}$");

    private readonly IFarmDataSource _dataSource;

    public SensorManagementService(IFarmDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    // ===== Dữ liệu cho các ô chọn =====

    public List<SensorType> GetSensorTypes() => _dataSource.GetSensorTypes().ToList();

    /// <summary>
    /// Các ESP32 đang hoạt động, dùng cho ô chọn ESP32 phụ trách.
    /// includeNodeId: ESP32 hiện tại của cảm biến đang sửa (luôn có trong danh sách).
    /// </summary>
    public List<Esp32Node> GetNodes(string? includeNodeId = null) =>
        _dataSource.GetNodes()
            .Where(n => n.IsActive || n.Id == includeNodeId)
            .OrderBy(n => n.Id)
            .ToList();

    /// <summary>Các khu vực có thể chuyển cảm biến vào (mọi khu vực hiện có).</summary>
    public List<Zone> GetZonesForSensor(string currentZoneCode) =>
        _dataSource.GetZones()
            .OrderBy(z => z.Code)
            .ToList();

    /// <summary>Số cảm biến đang quản lý (không tính đã gỡ) của một ESP32.</summary>
    public int CountActiveSensorsOfNode(string nodeId) =>
        _dataSource.GetSensors().Count(s => s.NodeId == nodeId && s.Status != SensorStatus.Removed);

    /// <summary>ESP32 đang phụ trách nhiều cảm biến nhất của khu vực. null nếu khu vực chưa có cảm biến.</summary>
    public string? SuggestNodeId(string zoneCode) =>
        _dataSource.GetSensors()
            .Where(s => s.ZoneCode == zoneCode && s.Status != SensorStatus.Removed)
            .GroupBy(s => s.NodeId)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .FirstOrDefault();

    /// <summary>Cảm biến đã có bản ghi đo hay chưa (dùng để cảnh báo khi đổi loại/đơn vị).</summary>
    public bool HasMeasurements(string sensorId) =>
        _dataSource.GetLatestMeasurements().ContainsKey(sensorId);

    // ===== Thêm cảm biến =====

    public string? AddSensor(
        string zoneCode, string? typeId, string? sensorId, string? name,
        string? nodeId, string? unit, string? notes)
    {
        string? error = BuildNewSensor(zoneCode, typeId, sensorId, name, nodeId, unit, notes, out Sensor? sensor);
        if (error != null) return error;

        try
        {
            _dataSource.AddSensor(sensor!);
            return null;
        }
        catch (InvalidOperationException ex)
        {
            return ex.Message;
        }
    }

    // ===== Sửa cảm biến =====

    /// <summary>Sửa thông tin cảm biến. Không đổi được mã. Không dùng để gỡ cảm biến.</summary>
    public string? UpdateSensor(
        string sensorId, string? typeId, string? zoneCode, string? name,
        string? nodeId, string? unit, SensorStatus status, string? notes)
    {
        Sensor? existing = _dataSource.GetSensors().FirstOrDefault(s => s.Id == sensorId);
        if (existing == null) return $"Không tìm thấy cảm biến {sensorId}.";
        if (existing.Status == SensorStatus.Removed) return "Cảm biến đã gỡ không thể chỉnh sửa.";
        if (status == SensorStatus.Removed) return "Trạng thái \"Đã gỡ\" chỉ đặt được bằng chức năng gỡ cảm biến.";

        SensorType? type = _dataSource.GetSensorTypes().FirstOrDefault(t => t.Id == typeId);
        if (type == null) return "Vui lòng chọn loại cảm biến.";

        Zone? zone = _dataSource.GetZones().FirstOrDefault(z => z.Code == zoneCode);
        if (zone == null) return "Vui lòng chọn khu vực lắp đặt.";

        string? error = CheckName(name);
        if (error != null) return error;

        Esp32Node? node = _dataSource.GetNodes()
            .FirstOrDefault(n => n.Id == nodeId && (n.IsActive || n.Id == existing.NodeId));
        if (node == null) return "Vui lòng chọn ESP32 phụ trách.";

        error = CheckUnit(unit);
        if (error != null) return error;

        error = CheckNotes(notes);
        if (error != null) return error;

        try
        {
            _dataSource.UpdateSensor(new Sensor
            {
                Id = existing.Id,
                Name = name!.Trim(),
                SensorTypeId = type.Id,
                ZoneCode = zone.Code,
                NodeId = node.Id,
                Unit = unit!.Trim(),
                Status = status,
                Notes = CleanText(notes)
            });
            return null;
        }
        catch (InvalidOperationException ex)
        {
            return ex.Message;
        }
    }

    // ===== Gỡ cảm biến =====

    /// <summary>Gỡ cảm biến: chỉ đánh dấu "Đã gỡ", giữ lại toàn bộ thông tin và lịch sử.</summary>
    public string? RemoveSensor(string sensorId)
    {
        Sensor? existing = _dataSource.GetSensors().FirstOrDefault(s => s.Id == sensorId);
        if (existing == null) return $"Không tìm thấy cảm biến {sensorId}.";
        if (existing.Status == SensorStatus.Removed) return "Cảm biến này đã được gỡ trước đó.";

        try
        {
            _dataSource.RemoveSensor(existing.Id, DateTime.UtcNow, null);
            return null;
        }
        catch (InvalidOperationException ex)
        {
            return ex.Message;
        }
    }

    // ===== Khôi phục cảm biến đã gỡ =====

    /// <summary>
    /// Lời cảnh báo (nếu có) cần hiện trước khi khôi phục:
    /// cảm biến đã được thay bằng cảm biến khác đang hoạt động.
    /// </summary>
    public string? GetRestoreWarning(Sensor sensor)
    {
        if (string.IsNullOrWhiteSpace(sensor.ReplacedBySensorId)) return null;

        Sensor? replacement = _dataSource.GetSensors()
            .FirstOrDefault(s => s.Id == sensor.ReplacedBySensorId);

        if (replacement is { Status: not SensorStatus.Removed })
        {
            return $"Lưu ý: cảm biến này đã được thay bằng {replacement.Id} (đang hoạt động). " +
                   "Khôi phục sẽ khiến cả hai cùng hoạt động trong khu vực.";
        }
        return null;
    }

    /// <summary>Khôi phục cảm biến đã gỡ về trạng thái "Đang hoạt động".</summary>
    public string? RestoreSensor(string sensorId)
    {
        Sensor? existing = _dataSource.GetSensors().FirstOrDefault(s => s.Id == sensorId);
        if (existing == null) return $"Không tìm thấy cảm biến {sensorId}.";
        if (existing.Status != SensorStatus.Removed) return "Cảm biến này chưa bị gỡ.";

        Zone? zone = _dataSource.GetZones().FirstOrDefault(z => z.Code == existing.ZoneCode);
        if (zone == null) return $"Không tìm thấy khu vực {existing.ZoneCode}.";

        Esp32Node? node = _dataSource.GetNodes().FirstOrDefault(n => n.Id == existing.NodeId);
        if (node is { IsActive: false })
            return $"ESP32 {node.Id} đang ngừng sử dụng. Hãy dùng lại ESP32 này trước khi khôi phục cảm biến.";

        // Ghi lại dấu vết vào ghi chú (thông tin gỡ sẽ bị xóa khi khôi phục)
        string removedDate = existing.RemovedAt?.ToLocalTime()
            .ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "không rõ";
        string today = DateTime.Now.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        string replacedPart = string.IsNullOrWhiteSpace(existing.ReplacedBySensorId)
            ? ""
            : $", đã thay bằng {existing.ReplacedBySensorId}";
        string note = $"Đã khôi phục ngày {today} (trước đó gỡ ngày {removedDate}{replacedPart})";

        try
        {
            _dataSource.RestoreSensor(existing.Id, note);
            return null;
        }
        catch (InvalidOperationException ex)
        {
            return ex.Message;
        }
    }
    // ===== Xóa hẳn cảm biến =====

    /// <summary>Xóa hẳn một cảm biến ĐÃ GỠ (không khôi phục được). Mã cảm biến sau đó dùng lại được.</summary>
    public string? DeleteSensor(string sensorId)
    {
        Sensor? existing = _dataSource.GetSensors().FirstOrDefault(s => s.Id == sensorId);
        if (existing == null) return $"Không tìm thấy cảm biến {sensorId}.";
        if (existing.Status != SensorStatus.Removed)
            return "Chỉ xóa hẳn được cảm biến đã gỡ. Hãy gỡ cảm biến trước.";

        try
        {
            _dataSource.DeleteSensor(existing.Id);
            return null;
        }
        catch (InvalidOperationException ex)
        {
            return ex.Message;
        }
    }
    // ===== Thay thế cảm biến =====

    /// <summary>
    /// Thay thế: thêm cảm biến mới vào cùng khu vực rồi đánh dấu cảm biến cũ "Đã gỡ",
    /// ghi liên kết cũ -> mới. Toàn bộ dữ liệu được kiểm tra trước khi ghi.
    /// </summary>
    public string? ReplaceSensor(
        string oldSensorId, string? typeId, string? newSensorId, string? name,
        string? nodeId, string? unit, string? notes)
    {
        Sensor? old = _dataSource.GetSensors().FirstOrDefault(s => s.Id == oldSensorId);
        if (old == null) return $"Không tìm thấy cảm biến {oldSensorId}.";
        if (old.Status == SensorStatus.Removed) return "Cảm biến này đã được gỡ trước đó.";

        string? error = BuildNewSensor(old.ZoneCode, typeId, newSensorId, name, nodeId, unit, notes, out Sensor? newSensor);
        if (error != null) return error;

        // Ghi chú tự động cho cảm biến mới để truy vết
        string replaceNote = $"Thay thế cho cảm biến {old.Id}";
        newSensor!.Notes = string.IsNullOrWhiteSpace(newSensor.Notes)
            ? replaceNote
            : $"{replaceNote}. {newSensor.Notes}";

        try
        {
            _dataSource.AddSensor(newSensor);
            _dataSource.RemoveSensor(old.Id, DateTime.UtcNow, newSensor.Id);
            return null;
        }
        catch (InvalidOperationException ex)
        {
            return ex.Message;
        }
    }

    // ===== Kiểm tra dữ liệu dùng chung =====

    /// <summary>Kiểm tra dữ liệu và dựng đối tượng cảm biến mới (chưa ghi vào nguồn dữ liệu).</summary>
    private string? BuildNewSensor(
        string zoneCode, string? typeId, string? sensorId, string? name,
        string? nodeId, string? unit, string? notes, out Sensor? sensor)
    {
        sensor = null;

        Zone? zone = _dataSource.GetZones().FirstOrDefault(z => z.Code == zoneCode);
        if (zone == null) return $"Không tìm thấy khu vực {zoneCode}.";

        SensorType? type = _dataSource.GetSensorTypes().FirstOrDefault(t => t.Id == typeId);
        if (type == null) return "Vui lòng chọn loại cảm biến.";

        string id = (sensorId ?? "").Trim().ToUpperInvariant();
        if (id.Length == 0) return "Vui lòng nhập mã cảm biến.";
        if (!IdPattern.IsMatch(id))
            return "Mã cảm biến không hợp lệ. Chỉ dùng chữ không dấu, số, dấu gạch ngang (-), gạch dưới (_); dài 3 đến 30 ký tự.";

        Sensor? sameId = _dataSource.GetSensors()
            .FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
        if (sameId != null)
        {
            return sameId.Status == SensorStatus.Removed
                ? $"Mã {id} đã từng dùng cho một cảm biến đã gỡ. Hãy dùng mã khác để giữ đúng lịch sử."
                : $"Mã cảm biến {id} đã tồn tại.";
        }

        string? error = CheckName(name);
        if (error != null) return error;

        Esp32Node? node = _dataSource.GetNodes().FirstOrDefault(n => n.Id == nodeId && n.IsActive);
        if (node == null) return "Vui lòng chọn ESP32 phụ trách.";

        error = CheckUnit(unit);
        if (error != null) return error;

        error = CheckNotes(notes);
        if (error != null) return error;

        sensor = new Sensor
        {
            Id = id,
            Name = name!.Trim(),
            SensorTypeId = type.Id,
            ZoneCode = zoneCode,
            NodeId = node.Id,
            Unit = unit!.Trim(),
            Status = SensorStatus.Active,
            Notes = CleanText(notes),
            InstalledAt = DateTime.UtcNow
        };
        return null;
    }

    private static string? CheckName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Vui lòng nhập tên cảm biến.";
        if (name.Trim().Length > MaxNameLength) return $"Tên cảm biến tối đa {MaxNameLength} ký tự.";
        return null;
    }

    private static string? CheckUnit(string? unit)
    {
        string text = (unit ?? "").Trim();
        if (text.Length == 0) return "Vui lòng nhập đơn vị đo.";
        if (text.Length > MaxUnitLength) return $"Đơn vị đo tối đa {MaxUnitLength} ký tự.";
        return null;
    }

    private static string? CheckNotes(string? notes)
    {
        if ((notes ?? "").Trim().Length > MaxNotesLength) return $"Ghi chú tối đa {MaxNotesLength} ký tự.";
        return null;
    }

    private static string? CleanText(string? text)
    {
        string value = (text ?? "").Trim();
        return value.Length == 0 ? null : value;
    }
}