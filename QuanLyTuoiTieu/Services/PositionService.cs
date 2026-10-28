using System.Globalization;
using QuanLyTuoiTieu.Models;
using QuanLyTuoiTieu.ViewModels;

namespace QuanLyTuoiTieu.Services;

/// <summary>
/// Vị trí cụm cảm biến của khu vực: so với mốc lắp đặt, cảnh báo di chuyển, đường dẫn bản đồ.
/// Vị trí do cụm cảm biến báo về qua nguồn dữ liệu; mốc là vị trí đặt khối cảm biến.
/// </summary>
public class PositionService
{
    private const double EarthRadiusMeters = 6_371_000.0;

    private readonly IFarmDataSource _dataSource;

    public PositionService(IFarmDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    /// <summary>Khoảng cách giữa hai điểm GPS theo mét (công thức haversine).</summary>
    public static double HaversineMeters(GpsPosition a, GpsPosition b)
    {
        double lat1 = a.Latitude * Math.PI / 180.0;
        double lat2 = b.Latitude * Math.PI / 180.0;
        double dLat = lat2 - lat1;
        double dLon = (b.Longitude - a.Longitude) * Math.PI / 180.0;

        double h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                   + Math.Cos(lat1) * Math.Cos(lat2) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        return 2 * EarthRadiusMeters * Math.Asin(Math.Min(1.0, Math.Sqrt(h)));
    }

    /// <summary>Khoảng cách giữa hai điểm; null nếu thiếu một trong hai.</summary>
    public static double? DistanceBetween(GpsPosition? a, GpsPosition? b) =>
        a == null || b == null ? null : HaversineMeters(a, b);

    /// <summary>Đường dẫn mở vị trí trên bản đồ (trình duyệt). Dùng dấu chấm thập phân bất kể ngôn ngữ máy.</summary>
    public static string MapUrl(GpsPosition position) =>
        "https://www.google.com/maps?q=" +
        position.Latitude.ToString("0.000000", CultureInfo.InvariantCulture) + "," +
        position.Longitude.ToString("0.000000", CultureInfo.InvariantCulture);

    /// <summary>
    /// Khu vực đã có vị trí GPS nhưng chưa có mốc thì ghi nhận vị trí đầu tiên làm mốc lắp đặt.
    /// Trả về số khu vực vừa được ghi nhận mốc.
    /// </summary>
    public int RecordMissingHomes()
    {
        var positions = _dataSource.GetZonePositions();
        var homes = _dataSource.GetZoneHomes();
        int count = 0;

        foreach (var pair in positions)
        {
            if (homes.ContainsKey(pair.Key)) continue;

            _dataSource.SetZoneHome(pair.Key, pair.Value);
            count++;
        }
        return count;
    }

    /// <summary>Đặt vị trí hiện tại của cụm cảm biến làm mốc lắp đặt mới. Trả về null nếu thành công.</summary>
    public string? SetHomeToCurrent(string zoneCode)
    {
        if (!_dataSource.GetZonePositions().TryGetValue(zoneCode, out GpsPosition? current))
            return "Chưa có vị trí GPS để đặt làm mốc.";

        try
        {
            _dataSource.SetZoneHome(zoneCode, new GpsPosition(current.Latitude, current.Longitude, DateTime.UtcNow));
            return null;
        }
        catch (InvalidOperationException ex)
        {
            return ex.Message;
        }
    }

    /// <summary>Dựng thông tin vị trí (đã định dạng sẵn) của một khu vực.</summary>
    public ZonePositionInfo GetInfo(string zoneCode, DateTime nowUtc)
    {
        _dataSource.GetZonePositions().TryGetValue(zoneCode, out GpsPosition? current);
        _dataSource.GetZoneHomes().TryGetValue(zoneCode, out GpsPosition? home);

        double threshold = SyncSettings.MoveThresholdMeters;
        double? distance = DistanceBetween(current, home);
        bool moved = distance is double d && d > threshold;
        bool ok = distance != null && !moved;

        string positionText = current == null
            ? "Chưa nhận được vị trí GPS từ cụm cảm biến"
            : $"Vĩ độ {Coord(current.Latitude)}  ·  Kinh độ {Coord(current.Longitude)}";

        string updatedText = current == null
            ? "Vị trí sẽ hiện khi cụm cảm biến kết nối và có tín hiệu GPS"
            : $"Cập nhật lúc {TimeText(current.ReportedAtUtc)} · {AgeText(nowUtc - current.ReportedAtUtc)}";

        string homeText = home == null
            ? "Mốc lắp đặt: chưa có (ghi nhận khi cụm cảm biến báo vị trí lần đầu)"
            : $"Mốc lắp đặt (vị trí đặt khối cảm biến): {Coord(home.Latitude)}, {Coord(home.Longitude)}";

        string statusText;
        if (distance is double dist)
        {
            statusText = moved
                ? $"⚠ ĐÃ LỆCH {dist:0} m so với mốc (vượt ngưỡng {threshold:0.##} m)"
                : $"✔ Đúng vị trí (lệch {dist:0} m, ngưỡng {threshold:0.##} m)";
        }
        else if (current == null)
        {
            statusText = "Chưa có vị trí hiện tại để so với mốc";
        }
        else
        {
            statusText = "Chưa có mốc để so sánh";
        }

        return new ZonePositionInfo
        {
            Current = current,
            Home = home,
            DistanceMeters = distance,
            IsMoved = moved,
            IsOk = ok,
            PositionText = positionText,
            UpdatedText = updatedText,
            HomeText = homeText,
            StatusText = statusText
        };
    }

    private static string Coord(double value) => value.ToString("0.000000", CultureInfo.InvariantCulture);

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