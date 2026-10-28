using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using QuanLyTuoiTieu.Data;
using QuanLyTuoiTieu.Models;
using QuanLyTuoiTieu.Services;
using QuanLyTuoiTieu.ViewModels;
using QuanLyTuoiTieu.Views;

namespace QuanLyTuoiTieu;

public partial class MainWindow : Window
{
    /// <summary>Cứ mỗi khoảng này, tính lại trạng thái khu vực theo giờ hiện tại (dữ liệu "già" đi theo thời gian).</summary>
    private static readonly TimeSpan StatusRecalcInterval = TimeSpan.FromSeconds(30);

    /// <summary>Chiều cao một hàng ô: ô cao 60 + lề 3 phía trên + 3 phía dưới (khớp ZoneCellStyle và Height="60" trong XAML).</summary>
    private const double MatrixRowPitch = 66;

    /// <summary>Số hàng tối thiểu luôn hiển thị, kể cả khi cửa sổ rất thấp.</summary>
    private const int MinVisibleRows = 3;

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
    private bool _applyingInterval;              // tránh áp dụng chu kỳ chồng lặp (Enter rồi mất tiêu điểm)
    private double _wheelRemainder;              // gom các nấc lăn chuột nhỏ (bàn di chuột) thành từng hàng

    public MainWindow()
    {
        InitializeComponent();

        _simulation = _dataSource as ISimulationControls;

        _syncService = new SyncService(_dataSource);
        _syncService.StateChanged += (_, _) => UpdateSyncUi();
        _syncService.SyncCompleted += OnSyncCompleted;

        // Nhãn, nguồn dữ liệu và menu thử nghiệm do nguồn dữ liệu quyết định
        SimulatedBadge.Visibility = _syncService.IsSimulated ? Visibility.Visible : Visibility.Collapsed;
        TestMenuButton.Visibility = _simulation != null ? Visibility.Visible : Visibility.Collapsed;
        SourceText.Text = _syncService.IsSimulated
            ? "Nguồn: dữ liệu mô phỏng"
            : $"Nguồn: {_syncService.SourceName}";
        SourceText.ToolTip = _syncService.IsSimulated ? "Dữ liệu mô phỏng, chưa kết nối máy chủ" : null;

        // Chu kỳ đồng bộ đã lưu (phút hoặc giờ)
        UnitCombo.SelectedIndex = SyncSettings.UseHours ? 1 : 0;   // _isReady còn false nên chưa chạy xử lý
        ShowInterval();

        // Tính lại trạng thái khu vực theo thời gian
        _statusTimer = new DispatcherTimer { Interval = StatusRecalcInterval };
        _statusTimer.Tick += (_, _) => RecalculateStatusesIfChanged();
        _statusTimer.Start();

        LoadView();                                                        // hiển thị ngay dữ liệu hiện có
        _syncService.SetAutoSyncEnabled(AutoSyncCheck.IsChecked == true);  // bật hẹn giờ theo ô tích
        _isReady = true;

        Loaded += (_, _) => UpdateMatrixHeight();                          // đặt số hàng ma trận nguyên hàng
        Loaded += async (_, _) => await _syncService.SyncAsync();          // tải dữ liệu khi khởi động
        Closed += (_, _) =>
        {
            _statusTimer.Stop();
            _syncService.Shutdown();                                       // dừng hẹn giờ, hủy đồng bộ đang chạy
        };
    }

    // ===== Kích thước ma trận: luôn hiển thị số NGUYÊN hàng =====

    private void MatrixHost_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateMatrixHeight();
    }

    /// <summary>
    /// Tính số hàng vừa với chiều cao còn lại (chiều cao vùng chứa trừ phần tiêu đề, thanh công cụ của khung),
    /// rồi đặt chiều cao ma trận đúng bằng số hàng x 66 px. Nhờ đó không bao giờ thấy nửa hàng.
    /// </summary>
    private void UpdateMatrixHeight()
    {
        if (MatrixHost.ActualHeight <= 0 || MatrixFrame.ActualHeight <= 0) return;

        // Phần của khung không phải ma trận (tiêu đề sơ đồ, thanh công cụ, viền, đệm)
        double chrome = MatrixFrame.ActualHeight - ZoneMatrix.ActualHeight;
        double available = MatrixHost.ActualHeight - chrome;

        int rows = (int)Math.Floor((available + 0.5) / MatrixRowPitch);
        rows = Math.Clamp(rows, MinVisibleRows, Zone.MaxRows);

        double target = rows * MatrixRowPitch;
        if (double.IsNaN(ZoneMatrix.Height) || Math.Abs(ZoneMatrix.Height - target) > 0.5)
        {
            ZoneMatrix.Height = target;
        }
    }

    /// <summary>ScrollViewer nằm trong khuôn mẫu của ma trận (tên MatrixScroll).</summary>
    private ScrollViewer? GetMatrixScrollViewer()
    {
        ZoneMatrix.ApplyTemplate();
        return ZoneMatrix.Template.FindName("MatrixScroll", ZoneMatrix) as ScrollViewer;
    }

    /// <summary>Mỗi nấc lăn chuột dịch đúng một hàng.</summary>
    private void ZoneMatrix_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        ScrollViewer? scroll = GetMatrixScrollViewer();
        if (scroll == null) return;

        _wheelRemainder += e.Delta;
        while (_wheelRemainder >= 120)
        {
            scroll.LineUp();
            _wheelRemainder -= 120;
        }
        while (_wheelRemainder <= -120)
        {
            scroll.LineDown();
            _wheelRemainder += 120;
        }

        e.Handled = true;
    }

    // ===== Dựng lại sơ đồ và thống kê từ dữ liệu hiện có =====

    private void LoadView()
    {
        // Cụm cảm biến báo vị trí lần đầu thì ghi nhận làm mốc lắp đặt
        new PositionService(_dataSource).RecordMissingHomes();

        var (cells, summary) = new FarmOverviewService(_dataSource).Build();
        ShowBuilt(cells, summary);
    }

    private static List<ZoneRow> BuildRows(List<ZoneCell> cells) =>
        cells.Chunk(Zone.MaxColumns).Select(chunk => new ZoneRow(chunk)).ToList();

    private void ShowBuilt(List<ZoneCell> cells, FarmSummary summary)
    {
        _cells = cells;
        DataContext = summary;               // cho 7 thẻ thống kê

        // Giữ nguyên hàng đang cuộn tới khi dựng lại sơ đồ
        ScrollViewer? scroll = GetMatrixScrollViewer();
        double offset = scroll?.VerticalOffset ?? 0;

        ZoneMatrix.ItemsSource = BuildRows(_cells);

        if (scroll != null && offset > 0)
        {
            ZoneMatrix.UpdateLayout();
            scroll.ScrollToVerticalOffset(offset);
        }

        ApplyFilter();
        UpdateSyncUi();                      // "Đo gần nhất" phụ thuộc cảm biến hiện có
    }

    /// <summary>
    /// Tính lại trạng thái theo giờ hiện tại. Chỉ vẽ lại sơ đồ khi có ô đổi trạng thái,
    /// để không làm giật hoặc nhảy vị trí cuộn mỗi lần hẹn giờ.
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
                 || a[i].SensorCount != b[i].SensorCount
                 || a[i].ClusterMoved != b[i].ClusterMoved)
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
            ? "Đang tắt"
            : syncing
                ? ""
                : $"Lần tới: {FormatTime(_syncService.NextAutoSyncAtUtc)}";

        if (syncing)
        {
            SyncStatusText.Text = "Đang đồng bộ...";
            SyncStatusText.Foreground = NormalStatusBrush;
            return;
        }

        string summary =
            $"Đồng bộ lúc {FormatShort(_syncService.LastSuccessAtUtc)}  ·  " +
            $"Đo gần nhất {FormatShort(_syncService.GetLatestMeasuredAtUtc())}";

        SyncStatusText.Text = _syncService.LastAttemptSucceeded switch
        {
            null => "Chưa đồng bộ",
            true => summary + "  ·  Thành công",
            false => $"THẤT BẠI ({_syncService.LastErrorMessage}), đang giữ dữ liệu cũ  ·  " + summary
        };
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

    private static string FormatTime(DateTime? utc) =>
        utc == null ? "chưa có" : utc.Value.ToLocalTime().ToString("HH:mm:ss");

    /// <summary>Giờ rút gọn: chỉ giờ nếu là hôm nay, kèm ngày nếu là ngày khác.</summary>
    private static string FormatShort(DateTime? utc)
    {
        if (utc == null) return "chưa có";

        DateTime local = utc.Value.ToLocalTime();
        return local.Date == DateTime.Today
            ? local.ToString("HH:mm:ss")
            : local.ToString("HH:mm:ss dd/MM/yyyy");
    }

    // ===== Chu kỳ đồng bộ tự chọn (ô số + phút/giờ) =====

    private bool UseHours => UnitCombo.SelectedIndex == 1;

    /// <summary>Hiển thị chu kỳ đang dùng trong ô số, theo đơn vị đang chọn.</summary>
    private void ShowInterval()
    {
        double minutes = SyncSettings.Interval.TotalMinutes;
        double shown = UseHours ? minutes / 60.0 : minutes;
        IntervalBox.Text = shown.ToString("0.##", CultureInfo.CurrentCulture);
    }

    /// <summary>Đọc ô số, kiểm tra và áp dụng. Giá trị sai thì báo lỗi và trả ô về giá trị đang dùng.</summary>
    private void ApplyIntervalFromBox()
    {
        if (_applyingInterval) return;
        _applyingInterval = true;

        try
        {
            // Chấp nhận cả dấu phẩy và dấu chấm thập phân
            string text = IntervalBox.Text.Trim().Replace(',', '.');
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                || double.IsNaN(value) || double.IsInfinity(value))
            {
                ShowIntervalError();
                return;
            }

            double minutes = Math.Round(value * (UseHours ? 60 : 1));
            if (minutes < SyncSettings.MinInterval.TotalMinutes || minutes > SyncSettings.MaxInterval.TotalMinutes)
            {
                ShowIntervalError();
                return;
            }

            TimeSpan interval = TimeSpan.FromMinutes(minutes);
            if (interval == SyncSettings.Interval)
            {
                ShowInterval();   // không đổi, chỉ chuẩn hóa chữ hiển thị
                return;
            }

            SyncSettings.SetInterval(interval);            // đổi và lưu tệp cài đặt
            _syncService.SetAutoSyncInterval(interval);    // hẹn lại mốc đồng bộ tự động
            ShowInterval();
            RecalculateStatusesIfChanged();                // ngưỡng dữ liệu cũ đổi theo chu kỳ
        }
        finally
        {
            _applyingInterval = false;
        }
    }

    private void ShowIntervalError()
    {
        MessageBox.Show("Chu kỳ phải từ 1 phút đến 24 giờ (ví dụ 20 phút hoặc 1,5 giờ).",
            "Chu kỳ không hợp lệ", MessageBoxButton.OK, MessageBoxImage.Warning);
        ShowInterval();
    }

    private void IntervalBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;

        ApplyIntervalFromBox();
        IntervalBox.SelectAll();
        e.Handled = true;
    }

    private void IntervalBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (!_isReady) return;
        ApplyIntervalFromBox();
    }

    private void UnitCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isReady) return;

        // Đổi đơn vị: giữ nguyên khoảng thời gian, chỉ đổi cách hiển thị
        SyncSettings.SetUseHours(UseHours);
        ShowInterval();
    }

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

        ClearFilterButton.Visibility = isFiltering ? Visibility.Visible : Visibility.Collapsed;
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

    // ===== Các nút =====

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await _syncService.SyncAsync();
    }

    private void AddZone_Click(object sender, RoutedEventArgs e)
    {
        OpenAddZoneDialog(null);
    }

    private void NodeList_Click(object sender, RoutedEventArgs e)
    {
        var window = new NodeListWindow(_dataSource) { Owner = this };
        window.ShowDialog();

        if (window.DataChanged)
        {
            LoadView();   // ngừng, dùng lại hoặc xóa ESP32 làm thay đổi số cảm biến
        }
    }

    private void SyncStatus_Click(object sender, MouseButtonEventArgs e)
    {
        ShowSyncInfo();
    }

    private void ShowSyncInfo()
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
            $"Dữ liệu coi là cũ sau: {FarmOverviewService.StaleAfter.TotalMinutes:0} phút (chu kỳ đồng bộ + 1 giờ)\n" +
            $"Trạng thái khu vực được tính lại mỗi {StatusRecalcInterval.TotalSeconds:0} giây theo giờ hiện tại";

        MessageBox.Show(message, "Thông tin đồng bộ", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // ===== Đồng bộ tự động =====

    private void AutoSync_Changed(object sender, RoutedEventArgs e)
    {
        if (!_isReady) return;
        _syncService.SetAutoSyncEnabled(AutoSyncCheck.IsChecked == true);
    }

    // ===== Menu thử nghiệm (chỉ có với dữ liệu mô phỏng) =====

    private void TestMenuButton_Click(object sender, RoutedEventArgs e)
    {
        TestMenu.PlacementTarget = TestMenuButton;
        TestMenu.Placement = PlacementMode.Bottom;
        TestMenu.IsOpen = true;
    }

    private void Simulation_Changed(object sender, RoutedEventArgs e)
    {
        if (!_isReady || _simulation == null) return;

        bool disconnected = SimDisconnectedItem.IsChecked;
        _simulation.SimulateDisconnected = disconnected;
        SimWarnBadge.Visibility = disconnected ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AgeData_Click(object sender, RoutedEventArgs e)
    {
        if (_simulation == null) return;

        // Tua vượt đúng ngưỡng dữ liệu cũ hiện tại (chu kỳ + 1 giờ) cộng 1 phút
        _simulation.AgeMeasurements(FarmOverviewService.StaleAfter + TimeSpan.FromMinutes(1));
        LoadView();   // tính lại trạng thái ngay với dữ liệu đã "già"
    }

    private void FillZones_Click(object sender, RoutedEventArgs e)
    {
        if (_simulation == null) return;

        MessageBoxResult answer = MessageBox.Show(
            $"Tạo khu vực mẫu cho mọi ô còn trống để đủ {Zone.MaxZones} khu vực, kèm ESP32 và cảm biến mô phỏng?\n\n" +
            "Khu vực, cảm biến và ESP32 bạn đã tạo được giữ nguyên.",
            "Tạo đủ khu vực mẫu", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
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
            MessageBox.Show($"Đã đủ {Zone.MaxZones} khu vực, không thể thêm khu vực mới.", "Thêm khu vực",
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