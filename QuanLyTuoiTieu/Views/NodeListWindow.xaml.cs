using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using QuanLyTuoiTieu.Models;
using QuanLyTuoiTieu.Services;
using QuanLyTuoiTieu.ViewModels;

namespace QuanLyTuoiTieu.Views;

/// <summary>Danh sách ESP32: xem khu vực và cảm biến phụ trách, thêm, sửa, ngừng sử dụng.</summary>
public partial class NodeListWindow : Window
{
    private readonly IFarmDataSource _dataSource;
    private readonly NodeManagementService _service;

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

        if (node == null)
        {
            SensorTitle.Text = "CẢM BIẾN CỦA ESP32 ĐANG CHỌN";
            SensorGrid.ItemsSource = null;
            return;
        }

        List<NodeSensorRow> sensors = _service.BuildSensorRows(node.Id);
        SensorGrid.ItemsSource = sensors;
        SensorTitle.Text = sensors.Count == 0
            ? $"CẢM BIẾN CỦA {node.Id}: chưa phụ trách cảm biến nào"
            : $"CẢM BIẾN CỦA {node.Id} ({sensors.Count})";
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
            // Kiểm tra trước, để không hỏi xác nhận rồi mới báo lỗi
            string? blocked = _service.CanDisable(node.Id);
            if (blocked != null)
            {
                MessageBox.Show(blocked, "Không thể ngừng sử dụng", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            MessageBoxResult answer = MessageBox.Show(
                $"Ngừng sử dụng {node.Id}?\n\n" +
                "ESP32 sẽ không còn trong danh sách chọn khi thêm cảm biến. Có thể dùng lại bất cứ lúc nào.",
                "Xác nhận", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes) return;
        }

        string? error = _service.SetActive(node.Id, !disabling);
        if (error != null)
        {
            MessageBox.Show(error, "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        LoadNodes(node.Id);
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