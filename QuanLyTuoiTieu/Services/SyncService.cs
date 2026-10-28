using System.Windows.Threading;
using QuanLyTuoiTieu.Models;

namespace QuanLyTuoiTieu.Services;

/// <summary>
/// Điều phối việc đồng bộ dữ liệu từ nguồn: không chạy chồng, có thời gian chờ tối đa,
/// giữ dữ liệu cũ khi lỗi, tự đồng bộ định kỳ (chu kỳ tự chọn), và ghi nhớ kết quả để hiển thị.
/// Mọi thành viên được dùng trên luồng giao diện.
/// </summary>
public class SyncService
{
    /// <summary>Thời gian chờ tối đa cho một lần đồng bộ.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private readonly IFarmDataSource _dataSource;
    private readonly DispatcherTimer _autoTimer;
    private readonly CancellationTokenSource _shutdownCts = new();

    public SyncService(IFarmDataSource dataSource)
    {
        _dataSource = dataSource;

        AutoSyncInterval = SyncSettings.Interval;
        _autoTimer = new DispatcherTimer { Interval = AutoSyncInterval };
        _autoTimer.Tick += OnAutoTick;
    }

    /// <summary>Xảy ra khi trạng thái thay đổi (bắt đầu/kết thúc đồng bộ, bật/tắt tự động, đổi chu kỳ).</summary>
    public event EventHandler? StateChanged;

    /// <summary>Xảy ra sau mỗi lần đồng bộ kết thúc (tham số: true = thành công).</summary>
    public event EventHandler<bool>? SyncCompleted;

    public string SourceName => _dataSource.SourceName;
    public bool IsSimulated => _dataSource.IsSimulated;

    public bool IsSyncing { get; private set; }

    /// <summary>Thời điểm lần đồng bộ THÀNH CÔNG gần nhất (UTC). null = chưa có.</summary>
    public DateTime? LastSuccessAtUtc { get; private set; }

    /// <summary>Thời điểm bắt đầu lần thử gần nhất (UTC), thành công hay không.</summary>
    public DateTime? LastAttemptAtUtc { get; private set; }

    /// <summary>Kết quả lần thử gần nhất: true = thành công, false = thất bại, null = chưa thử.</summary>
    public bool? LastAttemptSucceeded { get; private set; }

    public string LastErrorMessage { get; private set; } = "";

    // ===== Đồng bộ tự động =====

    /// <summary>Chu kỳ đồng bộ tự động hiện tại (người dùng tự chọn).</summary>
    public TimeSpan AutoSyncInterval { get; private set; }

    public bool AutoSyncEnabled { get; private set; }

    /// <summary>Thời điểm đồng bộ tự động tiếp theo (UTC). null nếu đang tắt.</summary>
    public DateTime? NextAutoSyncAtUtc { get; private set; }

    public void SetAutoSyncEnabled(bool enabled)
    {
        AutoSyncEnabled = enabled;
        ScheduleNextAutoSync();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Đổi chu kỳ đồng bộ tự động. Mốc tiếp theo được tính lại từ bây giờ.</summary>
    public void SetAutoSyncInterval(TimeSpan interval)
    {
        AutoSyncInterval = interval;
        ScheduleNextAutoSync();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Hẹn lại lần đồng bộ tự động tiếp theo: một chu kỳ tính từ BÂY GIỜ.</summary>
    private void ScheduleNextAutoSync()
    {
        _autoTimer.Stop();   // Stop rồi Start để đếm lại từ đầu

        if (!AutoSyncEnabled || _shutdownCts.IsCancellationRequested)
        {
            NextAutoSyncAtUtc = null;
            return;
        }

        _autoTimer.Interval = AutoSyncInterval;
        _autoTimer.Start();
        NextAutoSyncAtUtc = DateTime.UtcNow + AutoSyncInterval;
    }

    private async void OnAutoTick(object? sender, EventArgs e)
    {
        if (IsSyncing) return;   // đang đồng bộ: bỏ qua lần này, không chạy chồng

        try
        {
            await SyncAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
        }
    }

    /// <summary>Dừng hẹn giờ và hủy đồng bộ đang chạy. Gọi khi đóng ứng dụng.</summary>
    public void Shutdown()
    {
        _autoTimer.Stop();
        NextAutoSyncAtUtc = null;
        _shutdownCts.Cancel();
    }

    // ===== Đồng bộ một lần =====

    /// <summary>Thời điểm đo của bản ghi mới nhất trong các cảm biến đang quản lý (UTC).</summary>
    public DateTime? GetLatestMeasuredAtUtc()
    {
        var activeIds = _dataSource.GetSensors()
            .Where(s => s.Status != SensorStatus.Removed)
            .Select(s => s.Id)
            .ToHashSet();

        DateTime? latest = null;
        foreach (var pair in _dataSource.GetLatestMeasurements())
        {
            if (!activeIds.Contains(pair.Key)) continue;
            if (latest == null || pair.Value.MeasuredAt > latest) latest = pair.Value.MeasuredAt;
        }
        return latest;
    }

    /// <summary>
    /// Đồng bộ một lần. Trả về true nếu thành công.
    /// Trả về false nếu thất bại, ứng dụng đang đóng, hoặc đang có một lần đồng bộ khác
    /// (bỏ qua, không chạy chồng).
    /// </summary>
    public async Task<bool> SyncAsync()
    {
        if (IsSyncing || _shutdownCts.IsCancellationRequested) return false;

        IsSyncing = true;
        LastAttemptAtUtc = DateTime.UtcNow;
        StateChanged?.Invoke(this, EventArgs.Empty);

        bool success = false;
        bool cancelledByShutdown = false;

        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(_shutdownCts.Token);
            linked.CancelAfter(Timeout);

            await _dataSource.RefreshAsync(linked.Token);

            LastSuccessAtUtc = DateTime.UtcNow;
            LastAttemptSucceeded = true;
            LastErrorMessage = "";
            success = true;
        }
        catch (OperationCanceledException) when (_shutdownCts.IsCancellationRequested)
        {
            // Ứng dụng đang đóng: không cập nhật giao diện nữa
            cancelledByShutdown = true;
        }
        catch (OperationCanceledException)
        {
            SetFailed($"hết thời gian chờ {Timeout.TotalSeconds:0} giây");
        }
        catch (Exception ex)
        {
            SetFailed(ex.Message);
        }
        finally
        {
            IsSyncing = false;
        }

        if (cancelledByShutdown) return false;

        ScheduleNextAutoSync();   // lần tự động tiếp theo: một chu kỳ sau lần thử này
        SyncCompleted?.Invoke(this, success);
        StateChanged?.Invoke(this, EventArgs.Empty);
        return success;
    }

    private void SetFailed(string message)
    {
        LastAttemptSucceeded = false;
        LastErrorMessage = message;
    }
}