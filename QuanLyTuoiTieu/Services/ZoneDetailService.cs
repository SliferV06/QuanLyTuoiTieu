using System.Globalization;
using QuanLyTuoiTieu.Models;
using QuanLyTuoiTieu.ViewModels;

namespace QuanLyTuoiTieu.Services;

/// <summary>Dựng dữ liệu chi tiết của một khu vực từ nguồn dữ liệu.</summary>
public class ZoneDetailService
{
    private readonly IFarmDataSource _dataSource;

    public ZoneDetailService(IFarmDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    /// <summary>Trả về null nếu không tìm thấy khu vực.</summary>
    public ZoneDetail? Build(string zoneCode)
    {
        Zone? zone = _dataSource.GetZones().FirstOrDefault(z => z.Code == zoneCode);
        if (zone == null) return null;

        DateTime nowUtc = DateTime.UtcNow;
        var latest = _dataSource.GetLatestMeasurements();
        var typeById = _dataSource.GetSensorTypes().ToDictionary(t => t.Id);
        var nodeById = _dataSource.GetNodes().ToDictionary(n => n.Id);

        var zoneSensors = _dataSource.GetSensors().Where(s => s.ZoneCode == zoneCode).ToList();
        var activeSensors = zoneSensors.Where(s => s.Status != SensorStatus.Removed).ToList();
        var removedSensors = zoneSensors.Where(s => s.Status == SensorStatus.Removed).ToList();

        ZoneStatus status = FarmOverviewService.CalculateZoneStatus(zone, activeSensors, latest, nowUtc);

        var activeRows = activeSensors.Select(s => ToRow(s, typeById, latest, nowUtc)).ToList();
        var removedRows = removedSensors.Select(s => ToRow(s, typeById, latest, nowUtc)).ToList();

        // ESP32 liên quan = các node phụ trách cảm biến đang quản lý của khu vực
        var nodeIds = activeSensors.Select(s => s.NodeId).Distinct().OrderBy(id => id).ToList();
        string nodesText = nodeIds.Count == 0
            ? "Chưa có ESP32 nào"
            : string.Join(", ", nodeIds.Select(id => nodeById.ContainsKey(id) ? id : $"{id} (chưa khai báo)"));

        string dataSummary = activeRows.Count == 0
            ? "Chưa có cảm biến"
            : $"Mới: {activeRows.Count(r => r.DataStatus == DataStatus.Fresh)} | " +
              $"Cũ: {activeRows.Count(r => r.DataStatus == DataStatus.Stale)} | " +
              $"Chưa có dữ liệu: {activeRows.Count(r => r.DataStatus == DataStatus.NoData)} | " +
              $"Lỗi: {activeRows.Count(r => r.DataStatus == DataStatus.Error)}";

        return new ZoneDetail
        {
            Code = zone.Code,
            Name = zone.Name,
            DescriptionText = string.IsNullOrWhiteSpace(zone.Description) ? "(không có)" : zone.Description,
            Status = status,
            NodesText = nodesText,
            ActiveSensorCount = activeRows.Count,
            DataSummaryText = dataSummary,
            ActiveRows = activeRows,
            RemovedRows = removedRows
        };
    }

    private static SensorRow ToRow(
        Sensor sensor,
        IReadOnlyDictionary<string, SensorType> typeById,
        IReadOnlyDictionary<string, Measurement> latest,
        DateTime nowUtc)
    {
        latest.TryGetValue(sensor.Id, out Measurement? m);
        DataStatus dataStatus = FarmOverviewService.GetDataStatus(latest, sensor.Id, nowUtc);
        bool isRemoved = sensor.Status == SensorStatus.Removed;

        // Giá trị: không bao giờ thay dữ liệu thiếu/lỗi bằng số 0
        string valueText;
        if (m == null) valueText = "Chưa có dữ liệu";
        else if (m.IsError) valueText = "Lỗi";
        else if (m.Value is double v) valueText = v.ToString("0.##", CultureInfo.CurrentCulture);
        else valueText = "Chưa có dữ liệu";

        string measuredAtText = m == null
            ? "—"
            : $"{m.MeasuredAt.ToLocalTime().ToString("HH:mm:ss dd/MM/yyyy", CultureInfo.InvariantCulture)} ({AgeText(nowUtc - m.MeasuredAt)})";

        string dataStatusText = isRemoved
            ? "— (đã gỡ)"
            : $"{StatusDisplay.DataStatusIcon(dataStatus)} {StatusDisplay.DataStatusText(dataStatus)}";

        var notes = new List<string>();
        if (!string.IsNullOrWhiteSpace(sensor.Notes)) notes.Add(sensor.Notes);
        if (m is { IsError: true }) notes.Add($"Lỗi: {m.ErrorMessage}");
        if (isRemoved)
        {
            string removedDate = sensor.RemovedAt?.ToLocalTime().ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "không rõ";
            notes.Add($"Đã gỡ ngày {removedDate}");
            if (!string.IsNullOrWhiteSpace(sensor.RemovalReason)) notes.Add($"Lý do: {sensor.RemovalReason}");
            if (!string.IsNullOrWhiteSpace(sensor.ReplacedBySensorId)) notes.Add($"Thay bằng: {sensor.ReplacedBySensorId}");
        }

        return new SensorRow
        {
            Id = sensor.Id,
            Name = sensor.Name,
            TypeName = typeById.TryGetValue(sensor.SensorTypeId, out SensorType? type)
                ? type.Name
                : $"{sensor.SensorTypeId} (chưa khai báo)",
            NodeId = sensor.NodeId,
            ValueText = valueText,
            Unit = string.IsNullOrWhiteSpace(sensor.Unit) ? "—" : sensor.Unit,
            MeasuredAtText = measuredAtText,
            SensorStatusText = StatusDisplay.SensorStatusText(sensor.Status),
            DataStatus = dataStatus,
            DataStatusText = dataStatusText,
            NotesText = string.Join(". ", notes),
            IsRemoved = isRemoved
        };
    }

    private static string AgeText(TimeSpan age)
    {
        if (age < TimeSpan.Zero) age = TimeSpan.Zero;
        if (age.TotalMinutes < 1) return "vừa xong";
        if (age.TotalHours < 1) return $"{(int)age.TotalMinutes} phút trước";
        if (age.TotalDays < 1) return $"{(int)age.TotalHours} giờ trước";
        return $"{(int)age.TotalDays} ngày trước";
    }
}