using QuanLyTuoiTieu.Models;

namespace QuanLyTuoiTieu.Services;

/// <summary>
/// Kết quả ước tính. Value = null nghĩa là không ước tính được (Message nêu lý do).
/// SourceZones: mã các khu vực dùng làm nguồn, xếp theo khoảng cách gần đến xa.
/// </summary>
public sealed record EstimateResult(double? Value, IReadOnlyList<string> SourceZones, string? Message);

/// <summary>
/// Ước tính giá trị của một cảm biến lỗi bằng TRUNG BÌNH các cảm biến cùng loại ở khu vực lân cận.
/// Giá trị này chỉ để hiển thị tham khảo: KHÔNG ghi vào dữ liệu đo và không đổi trạng thái khu vực.
/// </summary>
public class NeighborEstimator
{
    /// <summary>Số nguồn tối thiểu để được ước tính.</summary>
    public const int MinSources = 4;

    /// <summary>Số nguồn tối đa (lấy các khu vực gần nhất).</summary>
    public const int MaxSources = 8;

    /// <summary>Chỉ xét khu vực cách tối đa số ô này (theo hàng và cột) trên sơ đồ.</summary>
    public const int MaxRadiusCells = 2;

    private readonly IReadOnlyList<Zone> _zones;
    private readonly ILookup<string, Sensor> _sensorsByZone;
    private readonly IReadOnlyDictionary<string, Measurement> _latest;
    private readonly DateTime _nowUtc;

    public NeighborEstimator(IFarmDataSource dataSource, DateTime nowUtc)
    {
        _zones = dataSource.GetZones();
        _sensorsByZone = dataSource.GetSensors().ToLookup(s => s.ZoneCode);
        _latest = dataSource.GetLatestMeasurements();
        _nowUtc = nowUtc;
    }

    /// <summary>Ước tính cảm biến loại sensorTypeId (đơn vị unit) của khu vực zoneCode.</summary>
    public EstimateResult Estimate(string zoneCode, string sensorTypeId, string unit)
    {
        Zone? target = _zones.FirstOrDefault(z => z.Code == zoneCode);
        if (target == null)
            return new EstimateResult(null, Array.Empty<string>(), "Không ước tính được: không tìm thấy khu vực.");

        var candidates = new List<(string Code, double Distance, double Value)>();

        foreach (Zone other in _zones)
        {
            if (other.Code == zoneCode) continue;

            int dRow = other.Row - target.Row;
            int dCol = other.Column - target.Column;
            if (Math.Max(Math.Abs(dRow), Math.Abs(dCol)) > MaxRadiusCells) continue;

            double? value = GetValidValue(other.Code, sensorTypeId, unit);
            if (value == null) continue;

            candidates.Add((other.Code, Math.Sqrt(dRow * dRow + dCol * dCol), value.Value));
        }

        // Gần nhất trước; cùng khoảng cách thì theo mã để kết quả luôn cố định
        var chosen = candidates
            .OrderBy(c => c.Distance)
            .ThenBy(c => c.Code, StringComparer.Ordinal)
            .Take(MaxSources)
            .ToList();

        var codes = chosen.Select(c => c.Code).ToList();

        if (chosen.Count < MinSources)
        {
            string message = chosen.Count == 0
                ? $"Không ước tính được: không có khu vực lân cận nào có dữ liệu hợp lệ (cần ít nhất {MinSources})."
                : $"Không ước tính được: chỉ có {chosen.Count} khu vực lân cận có dữ liệu hợp lệ (cần ít nhất {MinSources}).";
            return new EstimateResult(null, codes, message);
        }

        return new EstimateResult(chosen.Average(c => c.Value), codes, null);
    }

    /// <summary>
    /// Giá trị đo THẬT của cảm biến cùng loại trong khu vực, nếu hợp lệ:
    /// đang hoạt động (không lỗi, không gỡ), cùng đơn vị, dữ liệu mới và không phải bản ghi lỗi.
    /// </summary>
    private double? GetValidValue(string zoneCode, string sensorTypeId, string unit)
    {
        foreach (Sensor sensor in _sensorsByZone[zoneCode])
        {
            if (sensor.SensorTypeId != sensorTypeId) continue;
            if (sensor.Status != SensorStatus.Active) continue;
            if (!string.Equals(sensor.Unit, unit, StringComparison.OrdinalIgnoreCase)) continue;
            if (!_latest.TryGetValue(sensor.Id, out Measurement? m)) continue;

            // Chỉ nhận dữ liệu mới (không lỗi, không cũ)
            if (FarmOverviewService.GetDataStatus(_latest, sensor.Id, _nowUtc) != DataStatus.Fresh) continue;

            if (m.Value is double value) return value;
        }
        return null;
    }
}