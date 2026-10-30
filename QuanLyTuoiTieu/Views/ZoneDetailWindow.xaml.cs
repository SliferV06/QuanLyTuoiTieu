using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using QuanLyTuoiTieu.Models;
using QuanLyTuoiTieu.Services;
using QuanLyTuoiTieu.ViewModels;

namespace QuanLyTuoiTieu.Views;

public partial class ZoneDetailWindow : Window
{
    private readonly IFarmDataSource _dataSource;
    private readonly string _zoneCode;
    private ZoneDetail _detail = null!;
    private bool _applyingThreshold;   // tránh áp dụng ngưỡng chồng lặp (Enter rồi mất tiêu điểm)

    /// <summary>true nếu dữ liệu đã thay đổi (để cửa sổ chính tải lại sơ đồ và thống kê).</summary>
    public bool DataChanged { get; private set; }

    /// <summary>Chế độ mô phỏng: cho phép nhấp thẻ để kết nối thử, giả lập di chuyển cụm.</summary>
    private bool IsSimulationMode => _dataSource.IsSimulated && _dataSource is ISimulationControls;

    public ZoneDetailWindow(IFarmDataSource dataSource, string zoneCode)
    {
        InitializeComponent();

        _dataSource = dataSource;
        _zoneCode = zoneCode;

        Title = $"Chi tiết khu vực {zoneCode}";
        FooterNote.Text = $"Dữ liệu được coi là cũ sau {FarmOverviewService.StaleAfter.TotalMinutes:0} phút.";
        HintText.Text = IsSimulationMode
            ? "Chế độ mô phỏng: nhấn thẻ mờ để kết nối thử, nhấn thẻ sáng để ngắt kết nối thử."
            : "";
        SimMoveButton.Visibility = IsSimulationMode ? Visibility.Visible : Visibility.Collapsed;
        EstimateStaleCheck.IsChecked = SyncSettings.EstimateStaleEnabled;

        ShowThreshold();
        ReloadDetail();
    }

    /// <summary>Dựng lại toàn bộ nội dung cửa sổ từ nguồn dữ liệu.</summary>
    private void ReloadDetail()
    {
        // Cụm cảm biến báo vị trí lần đầu thì ghi nhận làm mốc lắp đặt
        new PositionService(_dataSource).RecordMissingHomes();

        ZoneDetail detail = new ZoneDetailService(_dataSource).Build(_zoneCode)
            ?? throw new InvalidOperationException($"Không tìm thấy khu vực {_zoneCode}.");

        _detail = detail;

        foreach (SensorTile tile in detail.Tiles)
            tile.IsClickable = IsSimulationMode;

        DataContext = detail;

        // Tên trạng thái trùng tiền tố tên màu trong App.xaml (Normal -> NormalBg/NormalFg/NormalBorder...)
        string key = detail.Status.ToString();
        StatusBadge.Background = (Brush)FindResource(key + "Bg");
        StatusBadge.BorderBrush = (Brush)FindResource(key + "Border");
        StatusBadgeText.Foreground = (Brush)FindResource(key + "Fg");

        MapButton.IsEnabled = detail.Position.HasPosition;
        SetHomeButton.IsEnabled = detail.Position.HasPosition;
    }

    // ===== Khu vực =====

    private void EditZone_Click(object sender, RoutedEventArgs e)
    {
        Zone? zone = _dataSource.GetZones().FirstOrDefault(z => z.Code == _zoneCode);
        if (zone == null) return;

        var dialog = new ZoneEditWindow(new ZoneManagementService(_dataSource), zone) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            DataChanged = true;
            ReloadDetail();
        }
    }

    private void DeleteZone_Click(object sender, RoutedEventArgs e)
    {
        var service = new ZoneManagementService(_dataSource);
        var (active, removed) = service.CountSensors(_zoneCode);

        string sensorLine = active + removed == 0
            ? "Khu vực này chưa có cảm biến."
            : $"Toàn bộ {active + removed} cảm biến của khu vực ({active} đang quản lý, {removed} đã gỡ) " +
              "cũng sẽ bị xóa hẳn cùng giá trị đo.";

        MessageBoxResult answer = MessageBox.Show(
            $"Xóa khu vực {_zoneCode}?\n\n" +
            $"{sensorLine}\n\n" +
            $"Ô {_zoneCode} sẽ trở thành ô trống. Không thể hoàn tác.\n" +
            "Thao tác này KHÔNG tháo cảm biến vật lý ngoài thực địa.",
            "Xác nhận xóa khu vực", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;

        string? error = service.DeleteZone(_zoneCode);
        if (error != null)
        {
            MessageBox.Show(error, "Không thể xóa khu vực", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DataChanged = true;
        Close();   // khu vực không còn nữa, đóng cửa sổ chi tiết
    }

    // ===== Vị trí cụm cảm biến =====

    private void Map_Click(object sender, RoutedEventArgs e)
    {
        GpsPosition? current = _detail.Position.Current;
        if (current == null) return;

        try
        {
            Process.Start(new ProcessStartInfo(PositionService.MapUrl(current)) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Không mở được trình duyệt: {ex.Message}", "Bản đồ",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void SetHome_Click(object sender, RoutedEventArgs e)
    {
        MessageBoxResult answer = MessageBox.Show(
            $"Đặt vị trí hiện tại của cụm cảm biến khu vực {_zoneCode} làm mốc lắp đặt mới?\n\n" +
            "Cảnh báo di chuyển sẽ được tính từ vị trí này. Chỉ làm khi bạn cố ý dời khối cảm biến.",
            "Đặt mốc lắp đặt", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;

        string? error = new PositionService(_dataSource).SetHomeToCurrent(_zoneCode);
        if (error != null)
        {
            MessageBox.Show(error, "Đặt mốc lắp đặt", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DataChanged = true;
        ReloadDetail();
    }

    private void SimMove_Click(object sender, RoutedEventArgs e)
    {
        if (!IsSimulationMode || _dataSource is not ISimulationControls simulation) return;

        simulation.SimulateMove(_zoneCode, northMeters: 0, eastMeters: 50);   // dịch 50 m về phía đông

        DataChanged = true;
        ReloadDetail();
    }

    // ===== Ngưỡng cảnh báo di chuyển =====

    private void ShowThreshold()
    {
        ThresholdBox.Text = SyncSettings.MoveThresholdMeters.ToString("0.##", CultureInfo.CurrentCulture);
    }

    private void ApplyThresholdFromBox()
    {
        if (_applyingThreshold) return;
        _applyingThreshold = true;

        try
        {
            // Chấp nhận cả dấu phẩy và dấu chấm thập phân
            string text = ThresholdBox.Text.Trim().Replace(',', '.');
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                || double.IsNaN(value) || double.IsInfinity(value)
                || value < SyncSettings.MinMoveThresholdMeters
                || value > SyncSettings.MaxMoveThresholdMeters)
            {
                MessageBox.Show(
                    $"Ngưỡng phải từ {SyncSettings.MinMoveThresholdMeters:0} đến {SyncSettings.MaxMoveThresholdMeters:0} mét.",
                    "Ngưỡng không hợp lệ", MessageBoxButton.OK, MessageBoxImage.Warning);
                ShowThreshold();
                return;
            }

            if (Math.Abs(value - SyncSettings.MoveThresholdMeters) < 0.0001)
            {
                ShowThreshold();   // không đổi, chỉ chuẩn hóa chữ hiển thị
                return;
            }

            SyncSettings.SetMoveThreshold(value);   // đổi và lưu tệp cài đặt
            DataChanged = true;                     // trạng thái các khu vực có thể đổi
            ShowThreshold();
            ReloadDetail();
        }
        finally
        {
            _applyingThreshold = false;
        }
    }

    private void ThresholdBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;

        ApplyThresholdFromBox();
        ThresholdBox.SelectAll();
        e.Handled = true;
    }

    private void ThresholdBox_LostFocus(object sender, RoutedEventArgs e)
    {
        ApplyThresholdFromBox();
    }

    // ===== Thẻ cảm biến: chỉ ở chế độ mô phỏng =====

    private void Tile_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: SensorTile tile }) return;
        if (!IsSimulationMode || _dataSource is not ISimulationControls simulation) return;

        string? error;

        if (!tile.IsLit)
        {
            // Thẻ mờ: kết nối thử (tạo cảm biến mô phỏng có dữ liệu)
            error = simulation.ConnectTestSensor(_zoneCode, tile.TypeId);
        }
        else
        {
            MessageBoxResult answer = MessageBox.Show(
                $"Ngắt kết nối thử cảm biến \"{tile.TypeName}\" của khu vực {_zoneCode}?\n\n" +
                "Cảm biến mô phỏng này sẽ bị xóa cùng giá trị đo của nó.",
                "Ngắt kết nối thử", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes) return;

            error = simulation.DisconnectTestSensor(_zoneCode, tile.TypeId);
        }

        if (error != null)
        {
            MessageBox.Show(error, "Thử nghiệm", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        DataChanged = true;   // để cửa sổ chính cập nhật thống kê khi đóng
        ReloadDetail();
    }
    private void EstimateStale_Changed(object sender, RoutedEventArgs e)
    {
        bool enabled = EstimateStaleCheck.IsChecked == true;
        if (enabled == SyncSettings.EstimateStaleEnabled) return;   // chưa đổi (đang nạp trạng thái ban đầu)

        SyncSettings.SetEstimateStaleEnabled(enabled);
        ReloadDetail();   // chỉ đổi cách hiển thị thẻ, không đổi dữ liệu
    }
    // ===== Đóng cửa sổ =====

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
            return;
        }
        base.OnPreviewKeyDown(e);
    }
}