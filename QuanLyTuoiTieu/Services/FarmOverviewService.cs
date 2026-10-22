using QuanLyTuoiTieu.Models;
using QuanLyTuoiTieu.ViewModels;

namespace QuanLyTuoiTieu.Services;

/// <summary>Dựng ma trận 156 ô và số liệu thống kê từ nguồn dữ liệu.</summary>
public class FarmOverviewService
{
    /// <summary>
    /// Dữ liệu cũ hơn mức này bị coi là "đã cũ". Đổi con số ở đây nếu cần.
    /// Lưu ý: node đo mỗi 10 phút, ứng dụng đồng bộ mỗi 20 phút, nên dữ liệu có thể già tới khoảng 30 phút
    /// trước lần đồng bộ kế tiếp; ngưỡng thấp hơn 30 phút có thể báo "cũ" trong lúc vẫn bình thường.
    /// </summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(25);

    private readonly IFarmDataSource _dataSource;

    public FarmOverviewService(IFarmDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    /// <summary>Mã khu vực từ hàng/cột. Ví dụ (1,1) -> A01; (13,12) -> M12.</summary>
    public static string MakeCode(int row, int column) => $"{(char)('A' + row - 1)}{column:00}";

    public (List<ZoneCell> Cells, FarmSummary Summary) Build()
    {
        DateTime nowUtc = DateTime.UtcNow;
        var zones = _dataSource.GetZones();
        var sensors = _dataSource.GetSensors();
        var latest = _dataSource.GetLatestMeasurements();

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

                var activeSensors = sensorsByZone[code].ToList();
                ZoneStatus status = CalculateZoneStatus(zone, activeSensors, latest, nowUtc);

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
                    SensorCount = activeSensors.Count
                });
            }
        }

        var managed = sensors.Where(s => s.Status != SensorStatus.Removed).ToList();
        summary.TotalSensors = managed.Count;
        summary.ActiveSensors = managed.Count(s => s.Status == SensorStatus.Active);
        summary.FaultySensors = managed.Count(s => s.Status == SensorStatus.Faulty);

        return (cells, summary);
    }

    public static ZoneStatus CalculateZoneStatus(
        Zone zone,
        IReadOnlyList<Sensor> activeSensors,
        IReadOnlyDictionary<string, Measurement> latest,
        DateTime nowUtc)
    {
        if (activeSensors.Count == 0) return ZoneStatus.NotConfigured;

        var dataStatuses = activeSensors
            .Select(s => GetDataStatus(latest, s.Id, nowUtc))
            .ToList();

        // Không có dữ liệu mới từ bất kỳ cảm biến nào
        if (dataStatuses.All(d => d == DataStatus.NoData || d == DataStatus.Stale))
            return ZoneStatus.Disconnected;

        bool hasProblem = activeSensors.Any(s => s.Status == SensorStatus.Faulty)
                          || dataStatuses.Any(d => d != DataStatus.Fresh);

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