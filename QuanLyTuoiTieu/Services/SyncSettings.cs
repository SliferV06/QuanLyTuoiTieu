using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace QuanLyTuoiTieu.Services;

/// <summary>
/// Cài đặt: chu kỳ lấy dữ liệu (tự chọn), ngưỡng dữ liệu cũ = chu kỳ + 1 giờ, và công tắc ước tính
/// từ vùng lân cận. Lưu ra tệp nhỏ %LocalAppData%\QuanLyTuoiTieu\settings.json.
/// </summary>
public static class SyncSettings
{
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromMinutes(20);
    public static readonly TimeSpan MinInterval = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan MaxInterval = TimeSpan.FromHours(24);

    /// <summary>Dữ liệu bị coi là cũ khi đã quá chu kỳ đồng bộ cộng thêm khoảng này.</summary>
    public static readonly TimeSpan StaleExtra = TimeSpan.FromHours(1);

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "QuanLyTuoiTieu", "settings.json");

    /// <summary>Nội dung tệp cài đặt.</summary>
    private sealed class SettingsData
    {
        public double IntervalMinutes { get; set; }
        public bool UseHours { get; set; }

        // Đặt tên "tắt" để tệp cài đặt cũ (chưa có trường này) vẫn cho kết quả mặc định là BẬT
        public bool EstimateDisabled { get; set; }
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

    /// <summary>true = khi cảm biến lỗi, hiển thị giá trị ước tính từ khu vực lân cận (mặc định bật).</summary>
    public static bool EstimateEnabled { get; private set; } = true;

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

    public static void SetEstimateEnabled(bool enabled)
    {
        EstimateEnabled = enabled;
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
            EstimateEnabled = !data.EstimateDisabled;
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
                EstimateDisabled = !EstimateEnabled
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