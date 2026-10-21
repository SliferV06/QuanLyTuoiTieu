using System.Windows;
using System.Windows.Controls;
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

    /// <summary>true nếu người dùng đã sửa hoặc ngừng/dùng lại khu vực (để cửa sổ chính tải lại sơ đồ).</summary>
    public bool DataChanged { get; private set; }

    public ZoneDetailWindow(IFarmDataSource dataSource, string zoneCode)
    {
        InitializeComponent();

        _dataSource = dataSource;
        _zoneCode = zoneCode;

        Title = $"Chi tiết khu vực {zoneCode}";
        FooterNote.Text =
            $"Dữ liệu được coi là cũ sau {FarmOverviewService.StaleAfter.TotalMinutes:0} phút. " +
             "Cảm biến đã gỡ chỉ bị đánh dấu, lịch sử vẫn được giữ lại.";

        ReloadDetail();
    }

    /// <summary>Dựng lại toàn bộ nội dung cửa sổ từ nguồn dữ liệu.</summary>
    private void ReloadDetail()
    {
        ZoneDetail detail = new ZoneDetailService(_dataSource).Build(_zoneCode)
            ?? throw new InvalidOperationException($"Không tìm thấy khu vực {_zoneCode}.");

        _detail = detail;
        DataContext = detail;

        // Tên trạng thái trùng tiền tố tên màu trong App.xaml (Normal -> NormalBg/NormalFg/NormalBorder...)
        string key = detail.Status.ToString();
        StatusBadge.Background = (Brush)FindResource(key + "Bg");
        StatusBadge.BorderBrush = (Brush)FindResource(key + "Border");
        StatusBadgeText.Foreground = (Brush)FindResource(key + "Fg");

        ShowRemovedCheck.Content = $"Hiển thị cả cảm biến đã gỡ ({detail.RemovedRows.Count})";
        ShowRemovedCheck.IsEnabled = detail.RemovedRows.Count > 0;

        NoSensorText.Visibility = detail.ActiveRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        ToggleActiveButton.Content = detail.Status == ZoneStatus.Disabled
            ? "Dùng lại khu vực"
            : "Ngừng sử dụng khu vực";

        AddSensorButton.IsEnabled = detail.Status != ZoneStatus.Disabled;
        AddSensorButton.ToolTip = detail.Status == ZoneStatus.Disabled
            ? "Khu vực đang ngừng sử dụng. Hãy dùng lại khu vực trước khi thêm cảm biến."
            : null;

        UpdateSensorList();
    }

    private void UpdateSensorList()
    {
        bool showRemoved = ShowRemovedCheck.IsChecked == true;

        SensorGrid.ItemsSource = showRemoved
            ? _detail.ActiveRows.Concat(_detail.RemovedRows).ToList()
            : _detail.ActiveRows;

        UpdateSensorButtons();
    }

    private void ShowRemoved_Changed(object sender, RoutedEventArgs e)
    {
        UpdateSensorList();
    }

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

    private void ToggleActive_Click(object sender, RoutedEventArgs e)
    {
        bool disabling = _detail.Status != ZoneStatus.Disabled;

        if (disabling)
        {
            MessageBoxResult answer = MessageBox.Show(
                $"Ngừng sử dụng khu vực {_zoneCode}?\n\n" +
                "Khu vực sẽ chuyển sang trạng thái \"Ngừng hoạt động\". " +
                "Cảm biến và lịch sử đo vẫn được giữ nguyên, có thể dùng lại bất cứ lúc nào.",
                "Xác nhận", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (answer != MessageBoxResult.Yes) return;
        }

        string? error = new ZoneManagementService(_dataSource).SetActive(_zoneCode, isActive: !disabling);
        if (error != null)
        {
            MessageBox.Show(error, "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DataChanged = true;
        ReloadDetail();
    }
    private void AddSensor_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SensorEditWindow(new SensorManagementService(_dataSource), _zoneCode) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            DataChanged = true;   // để cửa sổ chính cập nhật thống kê khi đóng
            ReloadDetail();
        }
    }
    private void SensorGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateSensorButtons();
    }

    /// <summary>
    /// Sửa, thay thế, gỡ dùng được khi đang chọn một cảm biến CHƯA gỡ.
    /// Khôi phục dùng được khi đang chọn một cảm biến ĐÃ gỡ.
    /// Dòng gợi ý cạnh tiêu đề cho biết đang chọn gì.
    /// </summary>
    private void UpdateSensorButtons()
    {
        SensorRow? row = SensorGrid.SelectedItem as SensorRow;
        bool isActiveRow = row is { IsRemoved: false };
        bool isRemovedRow = row is { IsRemoved: true };

        EditSensorButton.IsEnabled = isActiveRow;
        ReplaceSensorButton.IsEnabled = isActiveRow;
        RemoveSensorButton.IsEnabled = isActiveRow;
        RestoreSensorButton.IsEnabled = isRemovedRow;

        SelectionHintRun.Text = row == null
            ? "  -  chọn một dòng để thao tác"
            : isRemovedRow
                ? $"  -  đã chọn {row.Id} (đã gỡ, chỉ có thể khôi phục)"
                : $"  -  đã chọn {row.Id}";
    }

    /// <summary>Cảm biến (chưa gỡ) đang được chọn trong bảng, hoặc null.</summary>
    private Sensor? GetSelectedActiveSensor()
    {
        if (SensorGrid.SelectedItem is not SensorRow { IsRemoved: false } row) return null;

        Sensor? sensor = _dataSource.GetSensors().FirstOrDefault(s => s.Id == row.Id);
        return sensor is { Status: not SensorStatus.Removed } ? sensor : null;
    }

    private void SensorGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Chỉ mở khi nhấp đúp vào một dòng (không phải tiêu đề cột hay thanh cuộn)
        if (e.OriginalSource is DependencyObject source
            && ItemsControl.ContainerFromElement(SensorGrid, source) is DataGridRow)
        {
            OpenEditSensor();
        }
    }

    private void EditSensor_Click(object sender, RoutedEventArgs e)
    {
        OpenEditSensor();
    }

    private void OpenEditSensor()
    {
        if (SensorGrid.SelectedItem is not SensorRow row) return;

        Sensor? sensor = _dataSource.GetSensors().FirstOrDefault(s => s.Id == row.Id);
        if (sensor == null) return;

        if (sensor.Status == SensorStatus.Removed)
        {
            MessageBox.Show("Cảm biến đã gỡ không thể chỉnh sửa.", "Sửa cảm biến",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SensorEditWindow(new SensorManagementService(_dataSource), sensor.ZoneCode, sensor)
        {
            Owner = this
        };
        if (dialog.ShowDialog() == true)
        {
            DataChanged = true;   // để cửa sổ chính cập nhật thống kê khi đóng
            ReloadDetail();
        }
    }
    private void RemoveSensor_Click(object sender, RoutedEventArgs e)
    {
        Sensor? sensor = GetSelectedActiveSensor();
        if (sensor == null) return;

        MessageBoxResult answer = MessageBox.Show(
            $"Gỡ cảm biến {sensor.Id} ({sensor.Name})?\n\n" +
            "Cảm biến sẽ được đánh dấu \"Đã gỡ\" trong phần mềm: không còn tính vào thống kê và trạng thái khu vực. " +
            "Thông tin và lịch sử đo vẫn được giữ lại, và có thể khôi phục sau.\n\n" +
            "Thao tác này KHÔNG tháo cảm biến vật lý ngoài thực địa.",
            "Xác nhận gỡ cảm biến", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;

        string? error = new SensorManagementService(_dataSource).RemoveSensor(sensor.Id);
        if (error != null)
        {
            MessageBox.Show(error, "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DataChanged = true;   // để cửa sổ chính cập nhật thống kê khi đóng
        ReloadDetail();
    }

    /// <summary>Cảm biến ĐÃ gỡ đang được chọn trong bảng, hoặc null.</summary>
    private Sensor? GetSelectedRemovedSensor()
    {
        if (SensorGrid.SelectedItem is not SensorRow { IsRemoved: true } row) return null;

        Sensor? sensor = _dataSource.GetSensors().FirstOrDefault(s => s.Id == row.Id);
        return sensor is { Status: SensorStatus.Removed } ? sensor : null;
    }

    private void RestoreSensor_Click(object sender, RoutedEventArgs e)
    {
        Sensor? sensor = GetSelectedRemovedSensor();
        if (sensor == null) return;

        var service = new SensorManagementService(_dataSource);

        string message =
            $"Khôi phục cảm biến {sensor.Id} ({sensor.Name})?\n\n" +
            "Cảm biến sẽ hoạt động trở lại và được tính vào thống kê. " +
            "Dữ liệu đo của cảm biến có thể đã cũ cho tới khi có dữ liệu mới.";

        string? warning = service.GetRestoreWarning(sensor);
        if (warning != null) message += "\n\n" + warning;

        MessageBoxResult answer = MessageBox.Show(
            message, "Xác nhận khôi phục cảm biến",
            MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;

        string? error = service.RestoreSensor(sensor.Id);
        if (error != null)
        {
            MessageBox.Show(error, "Không thể khôi phục", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DataChanged = true;
        ReloadDetail();
    }

    private void ReplaceSensor_Click(object sender, RoutedEventArgs e)
    {
        Sensor? sensor = GetSelectedActiveSensor();
        if (sensor == null) return;

        var dialog = new SensorEditWindow(
            new SensorManagementService(_dataSource), sensor.ZoneCode, replacingSensor: sensor)
        {
            Owner = this
        };
        if (dialog.ShowDialog() == true)
        {
            DataChanged = true;
            ReloadDetail();
        }
    }
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