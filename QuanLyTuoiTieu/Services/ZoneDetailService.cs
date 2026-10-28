using System.Globalization;
using QuanLyTuoiTieu.Models;
using QuanLyTuoiTieu.ViewModels;

namespace QuanLyTuoiTieu.Services;

/// <summary>Dựng dữ liệu chi tiết của một khu vực (kể cả các thẻ cảm biến) từ nguồn dữ liệu.</summary>
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
        var nodeById = _dataSource.GetNodes().ToDictionary(n => n.Id);
        var estimator = new NeighborEstimator(_dataSource, nowUtc);

        var zoneSensors = _dataSource.GetSensors().Where(s => s.ZoneCode == zoneCode).ToList();
        var activeSensors = zoneSensors.Where(s => s.Status != SensorStatus.Removed).ToList();

        ZoneStatus status = FarmOverviewService.CalculateZoneStatus(zone, activeSensors, latest, nowUtc);

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
            .Select(type => BuildTile(type, zoneCode, zoneSensors, latest, nowUtc, estimator))
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
            Tiles = tiles
        };
    }

    private static SensorTile BuildTile(
        SensorType type, string zoneCode, IReadOnlyList<Sensor> zoneSensors,
        IReadOnlyDictionary<string, Measurement> latest, DateTime nowUtc, NeighborEstimator estimator)
    {
        var ofType = zoneSensors.Where(s => s.SensorTypeId == type.Id).ToList();
        var active = ofType.Where(s => s.Status != SensorStatus.Removed).OrderBy(s => s.Id).ToList();

        // Thẻ sáng: cảm biến đang quản lý và đã có bản ghi đo
        Sensor? connected = active.FirstOrDefault(s => latest.ContainsKey(s.Id));
        if (connected != null)
            return BuildLitTile(type, zoneCode, connected, latest[connected.Id], latest, nowUtc, estimator);

        // Thẻ mờ: nêu lý do
        string reason;
        if (active.Count > 0)
        {
            reason = "Chưa có dữ liệu";   // đã đăng ký nhưng chưa gửi dữ liệu
        }
        else
        {
            Sensor? removed = ofType
                .Where(s => s.Status == SensorStatus.Removed)
                .OrderByDescending(s => s.RemovedAt)
                .FirstOrDefault();

            reason = removed == null
                ? "Chưa kết nối"
                : removed.RemovedWithNodeId != null
                    ? $"ESP32 {removed.RemovedWithNodeId} ngừng sử dụng"
                    : "Đã gỡ";
        }

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
        SensorType type, string zoneCode, Sensor sensor, Measurement m,
        IReadOnlyDictionary<string, Measurement> latest, DateTime nowUtc, NeighborEstimator estimator)
    {
        DataStatus ds = FarmOverviewService.GetDataStatus(latest, sensor.Id, nowUtc);

        // Mặc định: không bao giờ thay dữ liệu lỗi hoặc thiếu bằng số 0
        bool hasValue = !m.IsError && m.Value.HasValue;
        string valueText = m.IsError
            ? "Lỗi"
            : hasValue ? m.Value!.Value.ToString("0.##", CultureInfo.CurrentCulture) : "—";
        string unitText = hasValue && !string.IsNullOrWhiteSpace(sensor.Unit) ? sensor.Unit : "";

        // Cảm biến lỗi hoặc hỏng: hiển thị giá trị ƯỚC TÍNH từ khu vực lân cận (nếu bật và đủ nguồn)
        string notice = "";
        bool isEstimated = false;
        bool sensorFault = sensor.Status == SensorStatus.Faulty;

        if ((m.IsError || sensorFault) && SyncSettings.EstimateEnabled)
        {
            EstimateResult estimate = estimator.Estimate(zoneCode, type.Id, sensor.Unit);

            if (estimate.Value is double estimated)
            {
                valueText = "≈ " + estimated.ToString("0.##", CultureInfo.CurrentCulture);
                unitText = string.IsNullOrWhiteSpace(sensor.Unit) ? "" : sensor.Unit;
                isEstimated = true;
                notice = $"Cảm biến lỗi: giá trị ước tính từ {estimate.SourceZones.Count} khu vực lân cận " +
                         $"({string.Join(", ", estimate.SourceZones)}), không phải số đo thật.";
            }
            else
            {
                notice = estimate.Message ?? "";
            }
        }

        // Vẫn thông báo lỗi cảm biến như trước
        string statusText = $"{StatusDisplay.DataStatusIcon(ds)} {StatusDisplay.DataStatusText(ds)}";
        if (sensorFault) statusText += " · cảm biến báo lỗi";

        string footer = $"ESP32: {sensor.NodeId}";
        if (m.IsError && !string.IsNullOrWhiteSpace(m.ErrorMessage)) footer += $" · {m.ErrorMessage}";

        DateTime local = m.MeasuredAt.ToLocalTime();
        string time = local.Date == DateTime.Today
            ? local.ToString("HH:mm:ss", CultureInfo.InvariantCulture)
            : local.ToString("HH:mm:ss dd/MM/yyyy", CultureInfo.InvariantCulture);
        string measured = $"Đo lúc {time} · {AgeText(nowUtc - m.MeasuredAt)}";

        string tooltip = $"{type.Name} - khu vực {zoneCode}\n{measured}\n{statusText}\n{footer}";
        if (notice.Length > 0) tooltip += $"\n{notice}";

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
            NoticeText = notice,
            IsEstimated = isEstimated,
            TooltipText = tooltip
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