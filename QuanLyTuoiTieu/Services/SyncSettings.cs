using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace QuanLyTuoiTieu.Services;

/// <summary>
/// Cài đặt: chu kỳ lấy dữ liệu (tự chọn), ngưỡng dữ liệu cũ = chu kỳ + 1 giờ, và ngưỡng cảnh báo cụm
/// cảm biến bị di chuyển. Lưu ra tệp nhỏ %LocalAppData%\QuanLyTuoiTieu\settings.json.
/// </summary>
public static class SyncSettings
{
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromMinutes(20);
    public static readonly TimeSpan MinInterval = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan MaxInterval = TimeSpan.FromHours(24);

    /// <summary>Dữ liệu bị coi là cũ khi đã quá chu kỳ đồng bộ cộng thêm khoảng này.</summary>
    public static readonly TimeSpan StaleExtra = TimeSpan.FromHours(1);

    /// <summary>Ngưỡng lệch vị trí so với mốc lắp đặt để cảnh báo cụm cảm biến bị di chuyển (mét).</summary>
    public const double DefaultMoveThresholdMeters = 30;
    public const double MinMoveThresholdMeters = 5;
    public const double MaxMoveThresholdMeters = 10000;

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "QuanLyTuoiTieu", "settings.json");

    /// <summary>Nội dung tệp cài đặt.</summary>
    private sealed class SettingsData
    {
        public double IntervalMinutes { get; set; }
        public bool UseHours { get; set; }
        public double MoveThresholdMeters { get; set; }
    }

    // Tự nạp cài đặt đã lưu ngay lần đầu có ai dùng lớp này
    static SyncSettings()
    {
        Load();
    }

    /// <summary>Chu kỳ lấy dữ liệu (đồng bộ tự động).</summary>
    public static TimeSpan Interval { get; private set; } = DefaultInterval;

    /// <summary>true = ô chu kỳ hiển thị theo giờ; false = theo phút.</summary>
    public static bool UseHours { get; private set; }

    /// <summary>Ngưỡng dữ liệu cũ = chu kỳ + 1 giờ.</summary>
    public static TimeSpan StaleAfter => Interval + StaleExtra;

    /// <summary>Ngưỡng cảnh báo di chuyển (mét).</summary>
    public static double MoveThresholdMeters { get; private set; } = DefaultMoveThresholdMeters;

    /// <summary>Đặt chu kỳ mới (đã kiểm tra khoảng hợp lệ ở nơi gọi) và lưu.</summary>
    public static void SetInterval(TimeSpan interval)
    {
        Interval = interval;
        Save();
    }

    public static void SetUseHours(bool useHours)
    {
        UseHours = useHours;
        Save();
    }

    /// <summary>Đặt ngưỡng cảnh báo di chuyển (đã kiểm tra khoảng hợp lệ ở nơi gọi) và lưu.</summary>
    public static void SetMoveThreshold(double meters)
    {
        MoveThresholdMeters = meters;
        Save();
    }

    private static void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;

            SettingsData? data = JsonSerializer.Deserialize<SettingsData>(File.ReadAllText(FilePath));
            if (data == null) return;

            double minutes = Math.Round(data.IntervalMinutes);
            if (minutes >= MinInterval.TotalMinutes && minutes <= MaxInterval.TotalMinutes)
                Interval = TimeSpan.FromMinutes(minutes);

            UseHours = data.UseHours;

            if (data.MoveThresholdMeters >= MinMoveThresholdMeters
                && data.MoveThresholdMeters <= MaxMoveThresholdMeters)
            {
                MoveThresholdMeters = data.MoveThresholdMeters;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);   // tệp hỏng hoặc không đọc được: dùng giá trị mặc định
        }
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);

            var data = new SettingsData
            {
                IntervalMinutes = Interval.TotalMinutes,
                UseHours = UseHours,
                MoveThresholdMeters = MoveThresholdMeters
            };
            File.WriteAllText(FilePath,
                JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);   // không lưu được thì bỏ qua, ứng dụng vẫn chạy bình thường
        }
    }
}