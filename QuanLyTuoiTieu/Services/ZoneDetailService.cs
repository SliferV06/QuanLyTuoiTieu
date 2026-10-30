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
        var nodeById = _dataSource.GetNodes().ToDictionary(n => n.Id);
        var estimator = new NeighborEstimator(_dataSource, nowUtc);
        bool estimateStale = SyncSettings.EstimateStaleEnabled;

        var zoneSensors = _dataSource.GetSensors().Where(s => s.ZoneCode == zoneCode).ToList();
        var activeSensors = zoneSensors.Where(s => s.Status != SensorStatus.Removed).ToList();

        ZonePositionInfo position = new PositionService(_dataSource).GetInfo(zoneCode, nowUtc);

        // Trạng thái khu vực luôn tính theo dữ liệu THẬT (giá trị ước tính không làm đổi trạng thái)
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
            .Select(type => BuildTile(type, zoneCode, zoneSensors, latest, nowUtc, estimator, estimateStale))
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
        IReadOnlyDictionary<string, Measurement> latest, DateTime nowUtc,
        NeighborEstimator estimator, bool estimateStale)
    {
        var ofType = zoneSensors.Where(s => s.SensorTypeId == type.Id).ToList();
        var active = ofType.Where(s => s.Status != SensorStatus.Removed).OrderBy(s => s.Id).ToList();

        // Thẻ sáng: có cảm biến đang quản lý (kể cả khi chưa gửi dữ liệu)
        if (active.Count > 0)
        {
            Sensor sensor = active.FirstOrDefault(s => latest.ContainsKey(s.Id)) ?? active[0];
            return BuildLitTile(type, zoneCode, sensor, latest, nowUtc, estimator, estimateStale);
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
        IReadOnlyDictionary<string, Measurement> latest, DateTime nowUtc,
        NeighborEstimator estimator, bool estimateStale)
    {
        bool sensorFault = sensor.Status == SensorStatus.Faulty;
        string footer = $"ESP32: {sensor.NodeId}";
        string unit = string.IsNullOrWhiteSpace(sensor.Unit) ? "" : sensor.Unit;

        // Đã đăng ký nhưng chưa gửi dữ liệu: vẫn sáng, giá trị "—" (không ước tính)
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
        bool isStale = ds == DataStatus.Stale;

        // Hiển thị mặc định theo bản ghi THẬT
        string valueText;
        string unitText = "";
        string measured;

        if (m.IsError)
        {
            valueText = "Lỗi";   // không dùng số 0
            measured = $"Bản ghi lỗi lúc {TimeText(m.MeasuredAt)} · {AgeText(nowUtc - m.MeasuredAt)}";
        }
        else
        {
            bool hasValue = m.Value.HasValue;
            valueText = hasValue ? m.Value!.Value.ToString("0.##", CultureInfo.CurrentCulture) : "—";
            unitText = hasValue ? unit : "";
            measured = $"Đo lúc {TimeText(m.MeasuredAt)} · {AgeText(nowUtc - m.MeasuredAt)}";
        }

        // Ước tính từ khu vực lân cận:
        //  - Cảm biến LỖI hoặc báo hỏng: luôn ước tính, cho tới khi cảm biến hoạt động lại.
        //  - Cảm biến có dữ liệu CŨ: chỉ khi bật công tắc, cho tới khi cảm biến nhận giá trị mới.
        bool isFaulty = m.IsError || sensorFault;
        bool isEstimated = false;
        string estimateStatus = "";
        string estimateTooltip = "";

        if (isFaulty || (isStale && estimateStale))
        {
            EstimateResult estimate = estimator.Estimate(zoneCode, type.Id, sensor.Unit);

            if (estimate.Value is double estimated)
            {
                // Dữ liệu cũ: vẫn cho biết số đo thật cuối cùng
                if (isStale && !m.IsError && m.Value is double lastReal)
                {
                    string lastText = lastReal.ToString("0.##", CultureInfo.CurrentCulture);
                    measured = $"Số đo cuối {lastText}{(unit.Length > 0 ? " " + unit : "")} lúc " +
                               $"{TimeText(m.MeasuredAt)} · {AgeText(nowUtc - m.MeasuredAt)}";
                }

                valueText = "≈ " + estimated.ToString("0.##", CultureInfo.CurrentCulture);
                unitText = unit;
                isEstimated = true;
                estimateStatus = $" · ước tính từ {estimate.SourceZones.Count} khu vực";

                string until = isFaulty
                    ? "cho tới khi cảm biến hoạt động lại"
                    : "cho tới khi cảm biến nhận giá trị mới";
                estimateTooltip =
                    $"Giá trị ước tính = trung bình cảm biến cùng loại ở {estimate.SourceZones.Count} khu vực " +
                    $"lân cận ({string.Join(", ", estimate.SourceZones)}), không phải số đo thật. Hiển thị {until}.";
            }
            else
            {
                estimateStatus = " · không ước tính được (thiếu khu vực lân cận)";
                estimateTooltip = estimate.Message ?? "";
            }
        }

        string statusText = $"{StatusDisplay.DataStatusIcon(ds)} {StatusDisplay.DataStatusText(ds)}{estimateStatus}";
        if (sensorFault) statusText += " · cảm biến báo lỗi";

        if (m.IsError && !string.IsNullOrWhiteSpace(m.ErrorMessage)) footer += $" · {m.ErrorMessage}";

        string tooltip = $"{type.Name} - khu vực {zoneCode}\n{measured}\n{statusText}\n{footer}";
        if (estimateTooltip.Length > 0) tooltip += $"\n{estimateTooltip}";

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
            IsEstimated = isEstimated,
            TooltipText = tooltip
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