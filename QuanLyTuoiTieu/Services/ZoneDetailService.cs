using System.Globalization;
using QuanLyTuoiTieu.Models;
using QuanLyTuoiTieu.ViewModels;

namespace QuanLyTuoiTieu.Services;

/// <summary>Dựng dữ liệu chi tiết của một khu vực (thẻ cảm biến, vị trí cụm) từ nguồn dữ liệu.</summary>
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
        var lastValid = _dataSource.GetLastValidMeasurements();
        var nodeById = _dataSource.GetNodes().ToDictionary(n => n.Id);

        var zoneSensors = _dataSource.GetSensors().Where(s => s.ZoneCode == zoneCode).ToList();
        var activeSensors = zoneSensors.Where(s => s.Status != SensorStatus.Removed).ToList();

        ZonePositionInfo position = new PositionService(_dataSource).GetInfo(zoneCode, nowUtc);

        ZoneStatus status = FarmOverviewService.CalculateZoneStatus(
            zone, activeSensors, latest, nowUtc, position.IsMoved);

        // ESP32 liên quan = các node phụ trách cảm biến đang quản lý của khu vực
        var nodeIds = activeSensors.Select(s => s.NodeId).Distinct().OrderBy(id => id).ToList();
        string nodesText = nodeIds.Count == 0
            ? "Chưa có ESP32 nào"
            : string.Join(", ", nodeIds.Select(id => nodeById.ContainsKey(id) ? id : $"{id} (chưa khai báo)"));

        var dataStatuses = activeSensors
            .Select(s => FarmOverviewService.GetDataStatus(latest, s.Id, nowUtc))
            .ToList();

        string dataSummary = activeSensors.Count == 0
            ? "Chưa có cảm biến"
            : $"Mới: {dataStatuses.Count(d => d == DataStatus.Fresh)} | " +
              $"Cũ: {dataStatuses.Count(d => d == DataStatus.Stale)} | " +
              $"Chưa có dữ liệu: {dataStatuses.Count(d => d == DataStatus.NoData)} | " +
              $"Lỗi: {dataStatuses.Count(d => d == DataStatus.Error)}";

        // Mỗi loại cảm biến một thẻ, theo thứ tự danh mục loại cảm biến
        var tiles = _dataSource.GetSensorTypes()
            .Select(type => BuildTile(type, zoneCode, zoneSensors, latest, lastValid, nowUtc))
            .ToList();

        return new ZoneDetail
        {
            Code = zone.Code,
            Name = string.IsNullOrWhiteSpace(zone.Name) ? "(chưa đặt tên)" : zone.Name,
            DescriptionText = string.IsNullOrWhiteSpace(zone.Description) ? "(không có)" : zone.Description,
            Status = status,
            NodesText = nodesText,
            ActiveSensorCount = activeSensors.Count,
            DataSummaryText = dataSummary,
            Tiles = tiles,
            Position = position
        };
    }

    private static SensorTile BuildTile(
        SensorType type, string zoneCode, IReadOnlyList<Sensor> zoneSensors,
        IReadOnlyDictionary<string, Measurement> latest,
        IReadOnlyDictionary<string, Measurement> lastValid, DateTime nowUtc)
    {
        var ofType = zoneSensors.Where(s => s.SensorTypeId == type.Id).ToList();
        var active = ofType.Where(s => s.Status != SensorStatus.Removed).OrderBy(s => s.Id).ToList();

        // Thẻ sáng: có cảm biến đang quản lý (kể cả khi chưa gửi dữ liệu)
        if (active.Count > 0)
        {
            Sensor sensor = active.FirstOrDefault(s => latest.ContainsKey(s.Id)) ?? active[0];
            return BuildLitTile(type, zoneCode, sensor, latest, lastValid, nowUtc);
        }

        // Thẻ mờ: nêu lý do
        Sensor? removed = ofType
            .Where(s => s.Status == SensorStatus.Removed)
            .OrderByDescending(s => s.RemovedAt)
            .FirstOrDefault();

        string reason = removed == null
            ? "Chưa kết nối"
            : removed.RemovedWithNodeId != null
                ? $"ESP32 {removed.RemovedWithNodeId} ngừng sử dụng"
                : "Đã gỡ";

        return new SensorTile
        {
            TypeId = type.Id,
            TypeName = type.Name,
            IsLit = false,
            ValueText = "—",
            DataStatus = DataStatus.NoData,
            StatusText = reason,
            TooltipText = $"{type.Name}: {reason}"
        };
    }

    private static SensorTile BuildLitTile(
        SensorType type, string zoneCode, Sensor sensor,
        IReadOnlyDictionary<string, Measurement> latest,
        IReadOnlyDictionary<string, Measurement> lastValid, DateTime nowUtc)
    {
        bool sensorFault = sensor.Status == SensorStatus.Faulty;
        string footer = $"ESP32: {sensor.NodeId}";
        string unit = string.IsNullOrWhiteSpace(sensor.Unit) ? "" : sensor.Unit;

        // Đã đăng ký nhưng chưa gửi dữ liệu: vẫn sáng, giá trị "—"
        if (!latest.TryGetValue(sensor.Id, out Measurement? m))
        {
            string noDataStatus =
                $"{StatusDisplay.DataStatusIcon(DataStatus.NoData)} {StatusDisplay.DataStatusText(DataStatus.NoData)}";
            if (sensorFault) noDataStatus += " · cảm biến báo lỗi";

            return new SensorTile
            {
                TypeId = type.Id,
                TypeName = type.Name,
                IsLit = true,
                ValueText = "—",
                MeasuredText = "Chưa nhận được dữ liệu",
                DataStatus = DataStatus.NoData,
                StatusText = noDataStatus,
                FooterText = footer,
                TooltipText = $"{type.Name} - khu vực {zoneCode}\n{noDataStatus}\n{footer}"
            };
        }

        DataStatus ds = FarmOverviewService.GetDataStatus(latest, sensor.Id, nowUtc);

        string valueText;
        string unitText = "";
        string measured;
        bool isOldValue = false;

        if (m.IsError)
        {
            // Bản ghi mới nhất bị lỗi: hiển thị giá trị hợp lệ CUỐI CÙNG (dữ liệu cũ), không dùng số 0
            if (lastValid.TryGetValue(sensor.Id, out Measurement? old) && old.Value is double oldValue)
            {
                valueText = oldValue.ToString("0.##", CultureInfo.CurrentCulture);
                unitText = unit;
                isOldValue = true;
                measured = $"Giá trị hợp lệ cuối lúc {TimeText(old.MeasuredAt)} · {AgeText(nowUtc - old.MeasuredAt)}";
            }
            else
            {
                valueText = "Lỗi";
                measured = $"Bản ghi lỗi lúc {TimeText(m.MeasuredAt)} · {AgeText(nowUtc - m.MeasuredAt)}";
            }
        }
        else
        {
            bool hasValue = m.Value.HasValue;
            valueText = hasValue ? m.Value!.Value.ToString("0.##", CultureInfo.CurrentCulture) : "—";
            unitText = hasValue ? unit : "";
            measured = $"Đo lúc {TimeText(m.MeasuredAt)} · {AgeText(nowUtc - m.MeasuredAt)}";
        }

        string statusText = $"{StatusDisplay.DataStatusIcon(ds)} {StatusDisplay.DataStatusText(ds)}";
        if (isOldValue) statusText += " · hiển thị dữ liệu cũ";
        if (sensorFault) statusText += " · cảm biến báo lỗi";

        if (m.IsError && !string.IsNullOrWhiteSpace(m.ErrorMessage)) footer += $" · {m.ErrorMessage}";

        return new SensorTile
        {
            TypeId = type.Id,
            TypeName = type.Name,
            IsLit = true,
            ValueText = valueText,
            UnitText = unitText,
            MeasuredText = measured,
            DataStatus = ds,
            StatusText = statusText,
            FooterText = footer,
            IsOldValue = isOldValue,
            TooltipText = $"{type.Name} - khu vực {zoneCode}\n{measured}\n{statusText}\n{footer}"
        };
    }

    private static string TimeText(DateTime utc)
    {
        DateTime local = utc.ToLocalTime();
        return local.Date == DateTime.Today
            ? local.ToString("HH:mm:ss", CultureInfo.InvariantCulture)
            : local.ToString("HH:mm:ss dd/MM/yyyy", CultureInfo.InvariantCulture);
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