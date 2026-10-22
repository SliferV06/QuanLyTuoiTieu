using System.Globalization;
using System.Text.RegularExpressions;
using QuanLyTuoiTieu.Models;
using QuanLyTuoiTieu.ViewModels;

namespace QuanLyTuoiTieu.Services;

/// <summary>
/// Quản lý ESP32: dựng bảng danh sách, thêm, sửa, ngừng sử dụng, dùng lại, xóa hẳn.
/// Các phương thức thao tác trả về null nếu thành công, hoặc câu thông báo lỗi tiếng Việt.
/// </summary>
public class NodeManagementService
{
    private const int MaxNameLength = 100;
    private const int MaxNotesLength = 500;

    // Mã ESP32 (đã đổi sang chữ in hoa): chữ A-Z, số, gạch ngang, gạch dưới; dài 3-20 ký tự.
    private static readonly Regex IdPattern = new("^[A-Z0-9_-]{3,20}$");
    private static readonly Regex AutoIdPattern = new(@"^ESP-(\d+)$");

    private readonly IFarmDataSource _dataSource;

    public NodeManagementService(IFarmDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    // ===== Dựng dữ liệu hiển thị =====

    public List<NodeRow> BuildNodeRows()
    {
        DateTime nowUtc = DateTime.UtcNow;
        var latest = _dataSource.GetLatestMeasurements();

        // Chỉ tính cảm biến đang quản lý (không tính đã gỡ)
        var sensorsByNode = _dataSource.GetSensors()
            .Where(s => s.Status != SensorStatus.Removed)
            .ToLookup(s => s.NodeId);

        return _dataSource.GetNodes()
            .OrderBy(n => n.Id)
            .Select(n =>
            {
                var mine = sensorsByNode[n.Id].ToList();
                var zoneCodes = mine.Select(s => s.ZoneCode).Distinct().OrderBy(c => c).ToList();

                // Bản ghi đo mới nhất trong các cảm biến của ESP32 (kể cả bản ghi báo lỗi)
                DateTime? lastMeasuredAt = null;
                foreach (Sensor sensor in mine)
                {
                    if (latest.TryGetValue(sensor.Id, out Measurement? m)
                        && (lastMeasuredAt == null || m.MeasuredAt > lastMeasuredAt))
                    {
                        lastMeasuredAt = m.MeasuredAt;
                    }
                }

                string lastDataText = !n.IsActive
                    ? "— (ngừng sử dụng)"
                    : mine.Count == 0
                        ? "— (chưa có cảm biến)"
                        : BuildLastDataText(lastMeasuredAt, nowUtc);

                return new NodeRow
                {
                    Id = n.Id,
                    Name = n.Name,
                    IsActive = n.IsActive,
                    StatusText = n.IsActive ? "✔ Đang hoạt động" : "⊘ Ngừng sử dụng",
                    ZoneCount = zoneCodes.Count,
                    SensorCount = mine.Count,
                    ZonesText = zoneCodes.Count == 0 ? "—" : string.Join(", ", zoneCodes),
                    LastDataText = lastDataText,
                    CreatedAtText = n.CreatedAt.ToLocalTime().ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
                    NotesText = n.Notes ?? ""
                };
            })
            .ToList();
    }

    /// <summary>Cảm biến đang quản lý của ESP32, kèm các cảm biến đã gỡ cùng khi ESP32 ngừng sử dụng.</summary>
    public List<NodeSensorRow> BuildSensorRows(string nodeId)
    {
        var typeById = _dataSource.GetSensorTypes().ToDictionary(t => t.Id);

        return _dataSource.GetSensors()
            .Where(s => s.NodeId == nodeId
                        && (s.Status != SensorStatus.Removed || s.RemovedWithNodeId == nodeId))
            .OrderBy(s => s.Status == SensorStatus.Removed)   // đang quản lý trước, đã gỡ sau
            .ThenBy(s => s.ZoneCode)
            .ThenBy(s => s.Id)
            .Select(s => new NodeSensorRow
            {
                Id = s.Id,
                Name = s.Name,
                ZoneCode = s.ZoneCode,
                TypeName = typeById.TryGetValue(s.SensorTypeId, out SensorType? type)
                    ? type.Name
                    : $"{s.SensorTypeId} (chưa khai báo)",
                StatusText = s.Status == SensorStatus.Removed
                    ? "Đã gỡ cùng ESP32"
                    : StatusDisplay.SensorStatusText(s.Status),
                IsRemoved = s.Status == SensorStatus.Removed
            })
            .ToList();
    }

    private static string BuildLastDataText(DateTime? lastMeasuredAtUtc, DateTime nowUtc)
    {
        if (lastMeasuredAtUtc == null) return "○ Chưa có dữ liệu";

        DateTime at = lastMeasuredAtUtc.Value;
        string time = at.ToLocalTime().ToString("HH:mm:ss dd/MM/yyyy", CultureInfo.InvariantCulture);
        TimeSpan age = nowUtc - at;
        bool stale = age > FarmOverviewService.StaleAfter;

        return stale
            ? $"⚠ {time} ({AgeText(age)}) - đã cũ"
            : $"✔ {time} ({AgeText(age)})";
    }

    private static string AgeText(TimeSpan age)
    {
        if (age < TimeSpan.Zero) age = TimeSpan.Zero;
        if (age.TotalMinutes < 1) return "vừa xong";
        if (age.TotalHours < 1) return $"{(int)age.TotalMinutes} phút trước";
        if (age.TotalDays < 1) return $"{(int)age.TotalHours} giờ trước";
        return $"{(int)age.TotalDays} ngày trước";
    }

    // ===== Thêm / sửa =====

    /// <summary>Gợi ý mã và tên cho ESP32 mới, ví dụ ("ESP-17", "ESP32 số 17").</summary>
    public (string Id, string Name) SuggestNode()
    {
        int max = 0;
        foreach (Esp32Node node in _dataSource.GetNodes())
        {
            Match match = AutoIdPattern.Match(node.Id.ToUpperInvariant());
            if (match.Success && int.TryParse(match.Groups[1].Value, out int number) && number > max)
                max = number;
        }

        int next = max + 1;
        return ($"ESP-{next:00}", $"ESP32 số {next}");
    }

    public string? AddNode(string? id, string? name, string? notes)
    {
        string code = (id ?? "").Trim().ToUpperInvariant();
        if (code.Length == 0) return "Vui lòng nhập mã ESP32.";
        if (!IdPattern.IsMatch(code))
            return "Mã ESP32 không hợp lệ. Chỉ dùng chữ không dấu, số, dấu gạch ngang (-), gạch dưới (_); dài 3 đến 20 ký tự.";
        if (_dataSource.GetNodes().Any(n => string.Equals(n.Id, code, StringComparison.OrdinalIgnoreCase)))
            return $"Mã ESP32 {code} đã tồn tại.";

        string? error = CheckText(name, notes);
        if (error != null) return error;

        try
        {
            _dataSource.AddNode(new Esp32Node
            {
                Id = code,
                Name = name!.Trim(),
                Notes = CleanText(notes),
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
            return null;
        }
        catch (InvalidOperationException ex)
        {
            return ex.Message;
        }
    }

    public string? UpdateNode(string id, string? name, string? notes)
    {
        Esp32Node? existing = _dataSource.GetNodes().FirstOrDefault(n => n.Id == id);
        if (existing == null) return $"Không tìm thấy ESP32 {id}.";

        string? error = CheckText(name, notes);
        if (error != null) return error;

        return Save(new Esp32Node
        {
            Id = id,
            Name = name!.Trim(),
            Notes = CleanText(notes),
            IsActive = existing.IsActive,
            CreatedAt = existing.CreatedAt
        });
    }

    // ===== Ngừng sử dụng / dùng lại =====

    /// <summary>Số cảm biến đang quản lý và các khu vực chứa chúng (để cảnh báo trước khi ngừng sử dụng).</summary>
    public (int SensorCount, List<string> ZoneCodes) GetDisableImpact(string nodeId)
    {
        var active = _dataSource.GetSensors()
            .Where(s => s.NodeId == nodeId && s.Status != SensorStatus.Removed)
            .ToList();

        return (active.Count, active.Select(s => s.ZoneCode).Distinct().OrderBy(c => c).ToList());
    }

    /// <summary>Số cảm biến đã gỡ cùng ESP32 (sẽ được khôi phục khi dùng lại ESP32).</summary>
    public int CountSensorsToRestore(string nodeId) =>
        _dataSource.GetSensors().Count(s =>
            s.NodeId == nodeId && s.Status == SensorStatus.Removed && s.RemovedWithNodeId == nodeId);

    /// <summary>
    /// isActive = false: ngừng sử dụng ESP32, đồng thời gỡ mọi cảm biến đang quản lý của nó.
    /// isActive = true: dùng lại ESP32, đồng thời khôi phục các cảm biến đã gỡ cùng lúc ngừng sử dụng.
    /// </summary>
    public string? SetActive(string id, bool isActive)
    {
        Esp32Node? existing = _dataSource.GetNodes().FirstOrDefault(n => n.Id == id);
        if (existing == null) return $"Không tìm thấy ESP32 {id}.";
        if (existing.IsActive == isActive) return null;

        try
        {
            if (!isActive)
            {
                // Ngừng sử dụng: gỡ cùng mọi cảm biến đang quản lý (ghi nhớ để khôi phục khi dùng lại)
                DateTime now = DateTime.UtcNow;
                foreach (Sensor sensor in _dataSource.GetSensors()
                             .Where(s => s.NodeId == id && s.Status != SensorStatus.Removed)
                             .ToList())
                {
                    _dataSource.RemoveSensor(sensor.Id, now, null, id);
                }

                return Save(new Esp32Node
                {
                    Id = id,
                    Name = existing.Name,
                    Notes = existing.Notes,
                    IsActive = false,
                    CreatedAt = existing.CreatedAt
                });
            }

            // Dùng lại: bật ESP32 trước, rồi khôi phục các cảm biến đã gỡ cùng
            string? error = Save(new Esp32Node
            {
                Id = id,
                Name = existing.Name,
                Notes = existing.Notes,
                IsActive = true,
                CreatedAt = existing.CreatedAt
            });
            if (error != null) return error;

            string today = DateTime.Now.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
            foreach (Sensor sensor in _dataSource.GetSensors()
                         .Where(s => s.NodeId == id
                                     && s.Status == SensorStatus.Removed
                                     && s.RemovedWithNodeId == id)
                         .ToList())
            {
                _dataSource.RestoreSensor(sensor.Id, $"Đã khôi phục ngày {today} cùng với việc dùng lại ESP32 {id}");
            }

            return null;
        }
        catch (InvalidOperationException ex)
        {
            return ex.Message;
        }
    }

    // ===== Xóa hẳn ESP32 =====

    /// <summary>Số cảm biến ĐÃ GỠ còn gắn với ESP32 (sẽ bị xóa hẳn cùng ESP32).</summary>
    public int CountRemovedSensors(string nodeId) =>
        _dataSource.GetSensors().Count(s => s.NodeId == nodeId && s.Status == SensorStatus.Removed);

    /// <summary>Trả về lý do KHÔNG được xóa hẳn ESP32, hoặc null nếu được phép.</summary>
    public string? CanDelete(string nodeId)
    {
        Esp32Node? node = _dataSource.GetNodes().FirstOrDefault(n => n.Id == nodeId);
        if (node == null) return $"Không tìm thấy ESP32 {nodeId}.";
        if (node.IsActive)
            return $"{nodeId} đang hoạt động. Hãy ngừng sử dụng ESP32 trước khi xóa hẳn.";

        int active = _dataSource.GetSensors()
            .Count(s => s.NodeId == nodeId && s.Status != SensorStatus.Removed);
        if (active > 0)
        {
            return $"{nodeId} còn {active} cảm biến đang quản lý. " +
                   "Hãy chuyển chúng sang ESP32 khác hoặc gỡ chúng trước khi xóa hẳn ESP32.";
        }

        return null;
    }

    /// <summary>
    /// Xóa hẳn ESP32 đã ngừng sử dụng. Các cảm biến ĐÃ GỠ còn gắn với ESP32 này cũng bị xóa hẳn.
    /// Không hoàn tác được. Mã ESP32 sau đó dùng lại được.
    /// </summary>
    public string? DeleteNode(string nodeId)
    {
        string? blocked = CanDelete(nodeId);
        if (blocked != null) return blocked;

        try
        {
            foreach (Sensor sensor in _dataSource.GetSensors().Where(s => s.NodeId == nodeId).ToList())
            {
                _dataSource.DeleteSensor(sensor.Id);
            }

            _dataSource.DeleteNode(nodeId);
            return null;
        }
        catch (InvalidOperationException ex)
        {
            return ex.Message;
        }
    }

    // ===== Dùng chung =====

    private string? Save(Esp32Node node)
    {
        try
        {
            _dataSource.UpdateNode(node);
            return null;
        }
        catch (InvalidOperationException ex)
        {
            return ex.Message;
        }
    }

    private static string? CheckText(string? name, string? notes)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Vui lòng nhập tên ESP32.";
        if (name.Trim().Length > MaxNameLength) return $"Tên ESP32 tối đa {MaxNameLength} ký tự.";
        if ((notes ?? "").Trim().Length > MaxNotesLength) return $"Ghi chú tối đa {MaxNotesLength} ký tự.";
        return null;
    }

    private static string? CleanText(string? text)
    {
        string value = (text ?? "").Trim();
        return value.Length == 0 ? null : value;
    }
}