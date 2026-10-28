using QuanLyTuoiTieu.Models;
using QuanLyTuoiTieu.ViewModels;

namespace QuanLyTuoiTieu.Services;

/// <summary>Dựng ma trận 144 ô và số liệu thống kê từ nguồn dữ liệu.</summary>
public class FarmOverviewService
{
    /// <summary>Dữ liệu cũ hơn mức này bị coi là "đã cũ": chu kỳ đồng bộ + 1 giờ (xem SyncSettings).</summary>
    public static TimeSpan StaleAfter => SyncSettings.StaleAfter;

    private readonly IFarmDataSource _dataSource;

    public FarmOverviewService(IFarmDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    /// <summary>Mã khu vực từ hàng/cột. Ví dụ (1,1) -> A01; (12,12) -> L12.</summary>
    public static string MakeCode(int row, int column) => $"{(char)('A' + row - 1)}{column:00}";

    public (List<ZoneCell> Cells, FarmSummary Summary) Build()
    {
        DateTime nowUtc = DateTime.UtcNow;
        var zones = _dataSource.GetZones();
        var sensors = _dataSource.GetSensors();
        var latest = _dataSource.GetLatestMeasurements();
        var positions = _dataSource.GetZonePositions();
        var homes = _dataSource.GetZoneHomes();
        double moveThreshold = SyncSettings.MoveThresholdMeters;

        var zoneByCode = zones.ToDictionary(z => z.Code);   // báo lỗi nếu có mã khu vực trùng
        var sensorsByZone = sensors
            .Where(s => s.Status != SensorStatus.Removed)
            .ToLookup(s => s.ZoneCode);

        var summary = new FarmSummary { TotalZones = zones.Count };
        var cells = new List<ZoneCell>(Zone.MaxZones);

        for (int row = 1; row <= Zone.MaxRows; row++)
        {
            for (int col = 1; col <= Zone.MaxColumns; col++)
            {
                string code = MakeCode(row, col);

                if (!zoneByCode.TryGetValue(code, out Zone? zone))
                {
                    cells.Add(new ZoneCell { Code = code });   // ô trống
                    continue;
                }

                // Cụm cảm biến lệch khỏi mốc lắp đặt quá ngưỡng?
                positions.TryGetValue(code, out GpsPosition? position);
                homes.TryGetValue(code, out GpsPosition? home);
                double? distance = PositionService.DistanceBetween(position, home);
                bool clusterMoved = distance is double d && d > moveThreshold;

                var activeSensors = sensorsByZone[code].ToList();
                ZoneStatus status = CalculateZoneStatus(zone, activeSensors, latest, nowUtc, clusterMoved);

                switch (status)
                {
                    case ZoneStatus.Normal: summary.NormalZones++; break;
                    case ZoneStatus.Warning: summary.WarningZones++; break;
                    case ZoneStatus.Disconnected: summary.DisconnectedZones++; break;
                }

                cells.Add(new ZoneCell
                {
                    Code = code,
                    Zone = zone,
                    Status = status,
                    SensorCount = activeSensors.Count,
                    ClusterMoved = clusterMoved,
                    MovedMeters = clusterMoved ? distance : null
                });
            }
        }

        var managed = sensors.Where(s => s.Status != SensorStatus.Removed).ToList();
        summary.TotalSensors = managed.Count;
        summary.ActiveSensors = managed.Count(s => s.Status == SensorStatus.Active);
        summary.FaultySensors = managed.Count(s => s.Status == SensorStatus.Faulty);

        return (cells, summary);
    }

    /// <summary>
    /// Trạng thái khu vực. clusterMoved = true (cụm cảm biến lệch khỏi mốc) đưa khu vực
    /// từ "Bình thường" sang "Cảnh báo"; không đổi "Chưa cấu hình" và "Mất kết nối".
    /// </summary>
    public static ZoneStatus CalculateZoneStatus(
        Zone zone,
        IReadOnlyList<Sensor> activeSensors,
        IReadOnlyDictionary<string, Measurement> latest,
        DateTime nowUtc,
        bool clusterMoved = false)
    {
        if (activeSensors.Count == 0) return ZoneStatus.NotConfigured;

        var dataStatuses = activeSensors
            .Select(s => GetDataStatus(latest, s.Id, nowUtc))
            .ToList();

        // Không có dữ liệu mới từ bất kỳ cảm biến nào
        if (dataStatuses.All(d => d == DataStatus.NoData || d == DataStatus.Stale))
            return ZoneStatus.Disconnected;

        bool hasProblem = activeSensors.Any(s => s.Status == SensorStatus.Faulty)
                          || dataStatuses.Any(d => d != DataStatus.Fresh)
                          || clusterMoved;

        return hasProblem ? ZoneStatus.Warning : ZoneStatus.Normal;
    }

    public static DataStatus GetDataStatus(
        IReadOnlyDictionary<string, Measurement> latest, string sensorId, DateTime nowUtc)
    {
        if (!latest.TryGetValue(sensorId, out Measurement? m)) return DataStatus.NoData;
        if (m.IsError) return DataStatus.Error;
        if (nowUtc - m.MeasuredAt > StaleAfter) return DataStatus.Stale;
        return DataStatus.Fresh;
    }
}