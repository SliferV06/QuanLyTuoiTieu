using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using QuanLyTuoiTieu.Data;
using QuanLyTuoiTieu.Services;
using QuanLyTuoiTieu.ViewModels;
using QuanLyTuoiTieu.Views;

namespace QuanLyTuoiTieu;

public partial class MainWindow : Window
{
    /// <summary>Cứ mỗi khoảng này, tính lại trạng thái khu vực theo giờ hiện tại (dữ liệu "già" đi theo thời gian).</summary>
    private static readonly TimeSpan StatusRecalcInterval = TimeSpan.FromSeconds(30);

    private static readonly Brush NormalStatusBrush = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80));
    private static readonly Brush FailedStatusBrush = new SolidColorBrush(Color.FromRgb(0x99, 0x1B, 0x1B));

    // MÔ PHỎNG: tạo một lần, giữ cấu hình khu vực/cảm biến qua các lần làm mới.
    // Sau này thay dòng này bằng nguồn dữ liệu từ máy chủ.
    private readonly IFarmDataSource _dataSource = new MockFarmDataSource();

    private readonly ISimulationControls? _simulation;   // null nếu nguồn dữ liệu không phải mô phỏng
    private readonly SyncService _syncService;
    private readonly DispatcherTimer _statusTimer;

    private List<ZoneCell> _cells = new();
    private bool _isReady;                       // tránh xử lý sự kiện khi cửa sổ đang khởi tạo

    public MainWindow()
    {
        InitializeComponent();

        _simulation = _dataSource as ISimulationControls;

        _syncService = new SyncService(_dataSource);
        _syncService.StateChanged += (_, _) => UpdateSyncUi();
        _syncService.SyncCompleted += OnSyncCompleted;

        // Nhãn, dòng nguồn dữ liệu và công cụ thử nghiệm do nguồn dữ liệu quyết định
        SimulatedBadge.Visibility = _syncService.IsSimulated ? Visibility.Visible : Visibility.Collapsed;
        SimulationPanel.Visibility = _simulation != null ? Visibility.Visible : Visibility.Collapsed;
        SourceText.Text = _syncService.IsSimulated
            ? $"Nguồn dữ liệu: {_syncService.SourceName.ToLowerInvariant()} (chưa kết nối máy chủ)"
            : $"Nguồn dữ liệu: {_syncService.SourceName}";
        AutoSyncCheck.Content =
            $"Tự động đồng bộ mỗi {(int)SyncService.DefaultAutoSyncInterval.TotalMinutes} phút";

        // Tính lại trạng thái khu vực theo thời gian
        _statusTimer = new DispatcherTimer { Interval = StatusRecalcInterval };
        _statusTimer.Tick += (_, _) => RecalculateStatusesIfChanged();
        _statusTimer.Start();

        LoadView();                                                        // hiển thị ngay dữ liệu hiện có
        _syncService.SetAutoSyncEnabled(AutoSyncCheck.IsChecked == true);  // bật hẹn giờ theo ô tích
        _isReady = true;

        Loaded += async (_, _) => await _syncService.SyncAsync();          // tải dữ liệu khi khởi động
        Closed += (_, _) =>
        {
            _statusTimer.Stop();
            _syncService.Shutdown();                                       // dừng hẹn giờ, hủy đồng bộ đang chạy
        };
    }

    // ===== Dựng lại sơ đồ và thống kê từ dữ liệu hiện có =====

    private void LoadView()
    {
        var (cells, summary) = new FarmOverviewService(_dataSource).Build();
        ShowBuilt(cells, summary);
    }

    private void ShowBuilt(List<ZoneCell> cells, FarmSummary summary)
    {
        _cells = cells;
        DataContext = summary;               // cho 7 thẻ thống kê
        ZoneMatrix.ItemsSource = _cells;     // cho ma trận 144 ô

        ApplyFilter();
        UpdateSyncUi();                      // "Đo gần nhất" phụ thuộc cảm biến hiện có
    }

    /// <summary>
    /// Tính lại trạng thái theo giờ hiện tại. Chỉ vẽ lại sơ đồ khi có ô đổi trạng thái,
    /// để không làm giật hoặc nhảy thanh cuộn mỗi lần hẹn giờ.
    /// </summary>
    private void RecalculateStatusesIfChanged()
    {
        try
        {
            var (cells, summary) = new FarmOverviewService(_dataSource).Build();
            if (SameStatuses(_cells, cells)) return;

            ShowBuilt(cells, summary);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);   // bỏ qua lần này, lần hẹn sau tính lại
        }
    }

    private static bool SameStatuses(List<ZoneCell> a, List<ZoneCell> b)
    {
        if (a.Count != b.Count) return false;

        for (int i = 0; i < a.Count; i++)
        {
            if (a[i].Code != b[i].Code
                || a[i].Status != b[i].Status
                || a[i].SensorCount != b[i].SensorCount)
            {
                return false;
            }
        }
        return true;
    }

    // ===== Đồng bộ =====

    /// <summary>Sau mỗi lần đồng bộ (thủ công hoặc tự động): thành công thì dựng lại sơ đồ.</summary>
    private void OnSyncCompleted(object? sender, bool success)
    {
        if (!success) return;   // thất bại: giữ nguyên dữ liệu đang hiển thị

        try
        {
            LoadView();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Không thể hiển thị dữ liệu mới: {ex.Message}", "Lỗi",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void UpdateSyncUi()
    {
        bool syncing = _syncService.IsSyncing;

        RefreshButton.IsEnabled = !syncing;
        RefreshButton.Content = syncing ? "Đang đồng bộ..." : "Làm mới dữ liệu";

        NextSyncText.Text = !_syncService.AutoSyncEnabled
            ? "Tự động đồng bộ: đang tắt"
            : syncing
                ? ""
                : $"Lần tự động tiếp theo: {FormatLocal(_syncService.NextAutoSyncAtUtc)}";

        if (syncing)
        {
            SyncStatusText.Text = "Đang đồng bộ...";
            SyncStatusText.Foreground = NormalStatusBrush;
            return;
        }

        SyncStatusText.Text =
            $"Đồng bộ thành công gần nhất: {FormatLocal(_syncService.LastSuccessAtUtc)} | " +
            $"Đo gần nhất: {FormatLocal(_syncService.GetLatestMeasuredAtUtc())} | " +
            $"Lần thử gần nhất: {DescribeLastAttempt()}";
        SyncStatusText.Foreground = _syncService.LastAttemptSucceeded == false
            ? FailedStatusBrush
            : NormalStatusBrush;
    }

    private string DescribeLastAttempt() => _syncService.LastAttemptSucceeded switch
    {
        null => "chưa đồng bộ",
        true => "thành công",
        false => $"THẤT BẠI ({_syncService.LastErrorMessage}), đang giữ dữ liệu cũ"
    };

    private static string FormatLocal(DateTime? utc) =>
        utc == null ? "chưa có" : utc.Value.ToLocalTime().ToString("HH:mm:ss dd/MM/yyyy");

    // ===== Tìm kiếm và lọc =====

    private void ApplyFilter()
    {
        string keyword = SearchBox.Text.Trim();
        string filterKey = (StatusFilter.SelectedItem as ComboBoxItem)?.Tag as string ?? "All";

        int matched = 0;
        foreach (ZoneCell cell in _cells)
        {
            cell.IsMatch = IsCellMatch(cell, keyword, filterKey);
            if (cell.IsMatch) matched++;
        }

        bool isFiltering = keyword.Length > 0 || filterKey != "All";
        int zoneCount = _cells.Count(c => c.HasZone);

        ResultText.Text = isFiltering
            ? $"Khớp {matched} / {_cells.Count} ô"
            : $"{zoneCount} khu vực / {_cells.Count} ô";
    }

    private static bool IsCellMatch(ZoneCell cell, string keyword, string filterKey)
    {
        bool statusOk = filterKey switch
        {
            "All" => true,
            "Empty" => !cell.HasZone,
            _ => cell.Status?.ToString() == filterKey   // Tag trùng tên ZoneStatus
        };
        if (!statusOk) return false;

        if (keyword.Length == 0) return true;

        return cell.Code.Contains(keyword, StringComparison.OrdinalIgnoreCase)
               || (cell.Zone?.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isReady) return;
        ApplyFilter();
    }

    private void StatusFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isReady) return;
        ApplyFilter();
    }

    private void ClearFilter_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Clear();
        StatusFilter.SelectedIndex = 0;   // tự gọi ApplyFilter qua sự kiện
        ApplyFilter();
    }

    // ===== Nút trên thanh công cụ =====

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await _syncService.SyncAsync();
    }

    private void AddZone_Click(object sender, RoutedEventArgs e)
    {
        OpenAddZoneDialog(null);
    }

    private void SyncInfo_Click(object sender, RoutedEventArgs e)
    {
        string autoText = _syncService.AutoSyncEnabled
            ? $"bật, mỗi {_syncService.AutoSyncInterval.TotalMinutes:0} phút, lần tới lúc {FormatLocal(_syncService.NextAutoSyncAtUtc)}"
            : "tắt";

        string message =
            $"Nguồn dữ liệu: {_syncService.SourceName}" +
            (_syncService.IsSimulated ? " (chưa kết nối máy chủ)" : "") + "\n" +
            $"Trạng thái: {(_syncService.IsSyncing ? "đang đồng bộ" : "rảnh")}\n" +
            $"Lần thử gần nhất lúc: {FormatLocal(_syncService.LastAttemptAtUtc)} - {DescribeLastAttempt()}\n" +
            $"Đồng bộ thành công gần nhất: {FormatLocal(_syncService.LastSuccessAtUtc)}\n" +
            $"Thời điểm đo gần nhất: {FormatLocal(_syncService.GetLatestMeasuredAtUtc())}\n\n" +
            $"Tự động đồng bộ: {autoText}\n" +
            "Chu kỳ đo của node ESP32 (giả định): 10 phút\n" +
            $"Thời gian chờ tối đa mỗi lần đồng bộ: {SyncService.Timeout.TotalSeconds:0} giây\n" +
            $"Dữ liệu coi là cũ sau: {FarmOverviewService.StaleAfter.TotalMinutes:0} phút\n" +
            $"Trạng thái khu vực được tính lại mỗi {StatusRecalcInterval.TotalSeconds:0} giây theo giờ hiện tại";

        MessageBox.Show(message, "Thông tin đồng bộ", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void NodeList_Click(object sender, RoutedEventArgs e)
    {
        var window = new NodeListWindow(_dataSource) { Owner = this };
        window.ShowDialog();
    }

    // ===== Đồng bộ tự động và công cụ thử nghiệm =====

    private void AutoSync_Changed(object sender, RoutedEventArgs e)
    {
        if (!_isReady) return;
        _syncService.SetAutoSyncEnabled(AutoSyncCheck.IsChecked == true);
    }

    private void Simulation_Changed(object sender, RoutedEventArgs e)
    {
        if (!_isReady || _simulation == null) return;

        _simulation.SimulateSyncError = SimErrorCheck.IsChecked == true;
        _simulation.SimulateDisconnected = SimDisconnectedCheck.IsChecked == true;
    }

    private void AgeData_Click(object sender, RoutedEventArgs e)
    {
        if (_simulation == null) return;

        _simulation.AgeMeasurements(TimeSpan.FromHours(1));
        LoadView();   // tính lại trạng thái ngay với dữ liệu "già" thêm 1 giờ
    }

    private void FillZones_Click(object sender, RoutedEventArgs e)
    {
        if (_simulation == null) return;

        MessageBoxResult answer = MessageBox.Show(
            "Tạo khu vực mẫu cho mọi ô còn trống để đủ 144 khu vực, kèm ESP32 và cảm biến mô phỏng?\n\n" +
            "Khu vực, cảm biến và ESP32 bạn đã tạo được giữ nguyên.",
            "Tạo đủ 144 khu vực mẫu", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;

        _simulation.FillAllZones();
        LoadView();
    }

    private void ResetSample_Click(object sender, RoutedEventArgs e)
    {
        if (_simulation == null) return;

        MessageBoxResult answer = MessageBox.Show(
            "Đặt lại về 48 khu vực mẫu ban đầu?\n\n" +
            "Mọi khu vực, cảm biến và ESP32 bạn đã thêm hoặc sửa sẽ MẤT.",
            "Đặt lại dữ liệu mẫu", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;

        _simulation.ResetSampleData();
        LoadView();
    }

    // ===== Thêm khu vực =====

    private void OpenAddZoneDialog(string? presetCode)
    {
        var service = new ZoneManagementService(_dataSource);

        if (service.GetFreeCodes().Count == 0)
        {
            MessageBox.Show("Đã đủ 144 khu vực, không thể thêm khu vực mới.", "Thêm khu vực",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new ZoneEditWindow(service, null, presetCode) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            LoadView();
        }
    }

    // ===== Nhấn vào ô khu vực =====

    private void ZoneCell_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ZoneCell cell }) return;

        if (!cell.HasZone)
        {
            OpenAddZoneDialog(cell.Code);   // ô trống: mở hộp thoại thêm, chọn sẵn mã ô này
            return;
        }

        var window = new ZoneDetailWindow(_dataSource, cell.Code) { Owner = this };
        window.ShowDialog();

        if (window.DataChanged)
        {
            LoadView();   // khu vực hoặc cảm biến vừa được thay đổi
        }
    }
}