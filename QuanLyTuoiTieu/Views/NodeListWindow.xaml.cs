using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using QuanLyTuoiTieu.Models;
using QuanLyTuoiTieu.Services;
using QuanLyTuoiTieu.ViewModels;

namespace QuanLyTuoiTieu.Views;

/// <summary>Danh sách ESP32: xem khu vực và cảm biến phụ trách, thêm, sửa, ngừng sử dụng, dùng lại, xóa hẳn.</summary>
public partial class NodeListWindow : Window
{
    private readonly IFarmDataSource _dataSource;
    private readonly NodeManagementService _service;

    /// <summary>true nếu cảm biến đã thay đổi (ngừng, dùng lại, xóa ESP32) để cửa sổ chính tải lại thống kê.</summary>
    public bool DataChanged { get; private set; }

    public NodeListWindow(IFarmDataSource dataSource)
    {
        InitializeComponent();

        _dataSource = dataSource;
        _service = new NodeManagementService(dataSource);

        LoadNodes(null);
    }

    private NodeRow? SelectedNode => NodeGrid.SelectedItem as NodeRow;

    /// <summary>Nạp lại bảng ESP32 và chọn lại dòng có mã selectId (nếu có).</summary>
    private void LoadNodes(string? selectId)
    {
        List<NodeRow> rows = _service.BuildNodeRows();
        NodeGrid.ItemsSource = rows;

        NodeRow? toSelect = rows.FirstOrDefault(r => r.Id == selectId) ?? rows.FirstOrDefault();
        NodeGrid.SelectedItem = toSelect;
        if (toSelect != null) NodeGrid.ScrollIntoView(toSelect);

        SummaryText.Text = $"{rows.Count} ESP32 ({rows.Count(r => r.IsActive)} đang hoạt động)";
        RefreshSelectionViews();
    }

    /// <summary>Cập nhật các nút và bảng cảm biến theo ESP32 đang chọn.</summary>
    private void RefreshSelectionViews()
    {
        NodeRow? node = SelectedNode;

        EditNodeButton.IsEnabled = node != null;
        ToggleNodeButton.IsEnabled = node != null;
        ToggleNodeButton.Content = node is { IsActive: false } ? "Dùng lại ESP32" : "Ngừng sử dụng ESP32";
        DeleteNodeButton.IsEnabled = node is { IsActive: false };   // chỉ xóa hẳn ESP32 đã ngừng sử dụng

        if (node == null)
        {
            SensorTitle.Text = "CẢM BIẾN CỦA ESP32 ĐANG CHỌN";
            SensorGrid.ItemsSource = null;
            return;
        }

        List<NodeSensorRow> sensors = _service.BuildSensorRows(node.Id);
        SensorGrid.ItemsSource = sensors;

        int activeCount = sensors.Count(s => !s.IsRemoved);
        int removedCount = sensors.Count - activeCount;

        SensorTitle.Text = sensors.Count == 0
            ? $"CẢM BIẾN CỦA {node.Id}: chưa phụ trách cảm biến nào"
            : removedCount == 0
                ? $"CẢM BIẾN CỦA {node.Id} ({activeCount})"
                : $"CẢM BIẾN CỦA {node.Id} ({activeCount} đang quản lý, {removedCount} đã gỡ cùng ESP32)";
    }

    private void NodeGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RefreshSelectionViews();
    }

    private void AddNode_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new NodeEditWindow(_service, null) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            LoadNodes(dialog.SavedNodeId);
        }
    }

    private void EditNode_Click(object sender, RoutedEventArgs e)
    {
        NodeRow? row = SelectedNode;
        if (row == null) return;

        Esp32Node? node = _dataSource.GetNodes().FirstOrDefault(n => n.Id == row.Id);
        if (node == null) return;

        var dialog = new NodeEditWindow(_service, node) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            LoadNodes(row.Id);
        }
    }

    private void ToggleNode_Click(object sender, RoutedEventArgs e)
    {
        NodeRow? node = SelectedNode;
        if (node == null) return;

        bool disabling = node.IsActive;

        if (disabling)
        {
            var (sensorCount, zoneCodes) = _service.GetDisableImpact(node.Id);

            string impact = sensorCount == 0
                ? "ESP32 này chưa phụ trách cảm biến nào."
                : $"{sensorCount} cảm biến đang quản lý của ESP32 này sẽ được gỡ cùng (đánh dấu “Đã gỡ”), " +
                  $"ảnh hưởng {zoneCodes.Count} khu vực: {string.Join(", ", zoneCodes)}.\n" +
                  "Khu vực nào hết cảm biến sẽ chuyển sang “Chưa cấu hình”.";

            MessageBoxResult answer = MessageBox.Show(
                $"Ngừng sử dụng {node.Id}?\n\n{impact}\n\n" +
                "ESP32 sẽ không còn trong danh sách chọn khi thêm cảm biến. " +
                "Khi dùng lại ESP32, các cảm biến này được khôi phục tự động.",
                "Xác nhận ngừng sử dụng ESP32", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes) return;
        }
        else
        {
            int toRestore = _service.CountSensorsToRestore(node.Id);
            if (toRestore > 0)
            {
                MessageBoxResult answer = MessageBox.Show(
                    $"Dùng lại {node.Id}?\n\n" +
                    $"{toRestore} cảm biến đã gỡ cùng khi ngừng sử dụng sẽ được khôi phục.\n" +
                    "Dữ liệu đo của chúng có thể đã cũ cho tới lần làm mới dữ liệu kế tiếp.",
                    "Xác nhận dùng lại ESP32", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.Yes);
                if (answer != MessageBoxResult.Yes) return;
            }
        }

        string? error = _service.SetActive(node.Id, !disabling);
        if (error != null)
        {
            MessageBox.Show(error, "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DataChanged = true;   // số cảm biến thay đổi, cửa sổ chính cần tải lại thống kê
        LoadNodes(node.Id);
    }

    private void DeleteNode_Click(object sender, RoutedEventArgs e)
    {
        NodeRow? node = SelectedNode;
        if (node == null) return;

        string? blocked = _service.CanDelete(node.Id);
        if (blocked != null)
        {
            MessageBox.Show(blocked, "Không thể xóa", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        int removedSensors = _service.CountRemovedSensors(node.Id);
        string sensorLine = removedSensors == 0
            ? ""
            : $"{removedSensors} cảm biến đã gỡ thuộc ESP32 này cũng sẽ bị xóa hẳn.\n\n";

        MessageBoxResult answer = MessageBox.Show(
            $"Xóa hẳn {node.Id} ({node.Name})?\n\n" +
            sensorLine +
            "ESP32 sẽ bị xóa vĩnh viễn khỏi danh sách, không thể khôi phục. Mã ESP32 sau đó có thể dùng lại.\n" +
            "Thao tác này KHÔNG ảnh hưởng thiết bị ESP32 ngoài thực địa.",
            "Xác nhận xóa hẳn ESP32", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;

        string? error = _service.DeleteNode(node.Id);
        if (error != null)
        {
            MessageBox.Show(error, "Không thể xóa", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DataChanged = true;
        LoadNodes(null);   // ESP32 đã xóa, chọn lại dòng đầu
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