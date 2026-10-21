using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using QuanLyTuoiTieu.Models;
using QuanLyTuoiTieu.Services;

namespace QuanLyTuoiTieu.Views;

/// <summary>
/// Hộp thoại cảm biến, có 3 chế độ:
///  - Thêm:       editingSensor = null, replacingSensor = null
///  - Sửa:        editingSensor != null
///  - Thay thế:   replacingSensor != null (tạo cảm biến mới, đánh dấu cảm biến cũ đã gỡ)
/// Trả về DialogResult = true nếu đã lưu thành công.
/// </summary>
public partial class SensorEditWindow : Window
{
    private readonly SensorManagementService _service;
    private readonly string _zoneCode;
    private readonly Sensor? _editingSensor;
    private readonly Sensor? _replacingSensor;

    private bool _initializing;      // true khi đang nạp dữ liệu ban đầu vào các ô
    private string _autoName = "";   // tên gợi ý tự điền, để biết người dùng đã tự sửa chưa
    private string _autoUnit = "";   // đơn vị gợi ý tự điền

    private bool IsEditMode => _editingSensor != null;

    /// <summary>Khu vực đang chọn (chỉ chế độ sửa mới đổi được khu vực).</summary>
    private string CurrentZoneCode =>
        IsEditMode ? ((ZoneCombo.SelectedItem as Zone)?.Code ?? _zoneCode) : _zoneCode;

    public SensorEditWindow(
        SensorManagementService service, string zoneCode,
        Sensor? editingSensor = null, Sensor? replacingSensor = null)
    {
        InitializeComponent();

        _service = service;
        _zoneCode = zoneCode;
        _editingSensor = editingSensor;
        _replacingSensor = replacingSensor;

        // Không để cửa sổ cao hơn vùng làm việc của màn hình (phần nội dung sẽ cuộn)
        MaxHeight = SystemParameters.WorkArea.Height - 40;

        List<SensorType> types = service.GetSensorTypes();
        TypeCombo.ItemsSource = types;

        if (editingSensor != null) InitEditMode(editingSensor, types);
        else if (replacingSensor != null) InitReplaceMode(replacingSensor, types);
        else InitAddMode(types);
    }

    // ===== Khởi tạo theo từng chế độ =====

    private void InitAddMode(List<SensorType> types)
    {
        Title = $"Thêm cảm biến vào khu vực {_zoneCode}";
        HeaderText.Text = $"THÊM CẢM BIẾN - KHU VỰC {_zoneCode}";
        ZoneText.Text = _zoneCode;
        CodeHint.Text =
            "Ví dụ: A01-SM-01. Chỉ dùng chữ không dấu, số, dấu gạch ngang (-), gạch dưới (_). " +
            "Mã không được trùng với cảm biến nào khác (kể cả đã gỡ).";

        List<Esp32Node> nodes = _service.GetNodes();
        NodeCombo.ItemsSource = nodes;

        // Chọn sẵn ESP32 đang phụ trách các cảm biến khác của khu vực (nếu có)
        string? suggestedNodeId = _service.SuggestNodeId(_zoneCode);
        if (suggestedNodeId != null)
            NodeCombo.SelectedItem = nodes.FirstOrDefault(n => n.Id == suggestedNodeId);

        TypeCombo.Focus();
    }

    private void InitEditMode(Sensor sensor, List<SensorType> types)
    {
        _initializing = true;

        Title = $"Sửa cảm biến {sensor.Id}";
        HeaderText.Text = $"SỬA CẢM BIẾN {sensor.Id}";

        ZoneText.Visibility = Visibility.Collapsed;
        ZoneCombo.Visibility = Visibility.Visible;
        List<Zone> zones = _service.GetZonesForSensor(sensor.ZoneCode);
        ZoneCombo.ItemsSource = zones;
        ZoneCombo.SelectedItem = zones.FirstOrDefault(z => z.Code == sensor.ZoneCode);

        TypeCombo.SelectedItem = types.FirstOrDefault(t => t.Id == sensor.SensorTypeId);

        CodeBox.Text = sensor.Id;
        CodeBox.IsReadOnly = true;
        CodeBox.Background = new SolidColorBrush(Color.FromRgb(0xE5, 0xE7, 0xEB));
        CodeHint.Text = "Mã cảm biến không thể thay đổi (gắn với lịch sử đo).";

        NameBox.Text = sensor.Name;

        List<Esp32Node> nodes = _service.GetNodes(sensor.NodeId);
        NodeCombo.ItemsSource = nodes;
        NodeCombo.SelectedItem = nodes.FirstOrDefault(n => n.Id == sensor.NodeId);

        UnitBox.Text = sensor.Unit;

        StatusPanel.Visibility = Visibility.Visible;
        StatusCombo.SelectedIndex = sensor.Status == SensorStatus.Faulty ? 1 : 0;

        NotesBox.Text = sensor.Notes ?? "";

        _initializing = false;
        SetAutoValuesFromCurrentType(sensor.ZoneCode);

        NameBox.Focus();
        NameBox.SelectAll();
    }

    private void InitReplaceMode(Sensor old, List<SensorType> types)
    {
        _initializing = true;

        Title = $"Thay thế cảm biến {old.Id}";
        HeaderText.Text = $"THAY THẾ CẢM BIẾN {old.Id}";
        SaveButton.Content = "Thay thế";

        ReplaceInfo.Visibility = Visibility.Visible;
        ReplaceInfoText.Text =
            $"Cảm biến {old.Id} sẽ được đánh dấu \"Đã gỡ\" (vẫn giữ lịch sử) và cảm biến mới được thêm vào " +
            "cùng khu vực. Cảm biến mới chưa có dữ liệu đo. Các ô bên dưới được điền sẵn theo cảm biến cũ, " +
            "bạn chỉ bắt buộc nhập mã MỚI. Đổi loại nếu cảm biến vật lý thay bằng loại khác.";

        ZoneText.Text = _zoneCode;

        CodeHint.Text =
            $"Nhập mã MỚI cho cảm biến thay thế (không dùng lại mã {old.Id}). " +
            "Chỉ dùng chữ không dấu, số, dấu gạch ngang (-), gạch dưới (_).";

        TypeCombo.SelectedItem = types.FirstOrDefault(t => t.Id == old.SensorTypeId);
        NameBox.Text = old.Name;

        List<Esp32Node> nodes = _service.GetNodes();
        NodeCombo.ItemsSource = nodes;
        NodeCombo.SelectedItem = nodes.FirstOrDefault(n => n.Id == old.NodeId);

        UnitBox.Text = old.Unit;

        _initializing = false;
        SetAutoValuesFromCurrentType(_zoneCode);

        CodeBox.Focus();
    }

    /// <summary>Ghi nhớ giá trị "tự điền" theo loại đang chọn, để biết khi nào người dùng đã tự sửa.</summary>
    private void SetAutoValuesFromCurrentType(string zoneCode)
    {
        if (TypeCombo.SelectedItem is SensorType type)
        {
            _autoName = $"{type.Name} {zoneCode}";
            _autoUnit = type.DefaultUnit;
        }
    }

    // ===== Sự kiện các ô chọn =====

    private void TypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TypeCombo.SelectedItem is not SensorType type) return;

        string hint = type.DefaultUnit.Length > 0
            ? $"Đơn vị mặc định: {type.DefaultUnit} (có thể sửa)."
            : "Loại này chưa có đơn vị mặc định. Hãy nhập đơn vị theo tài liệu kỹ thuật của cảm biến thực tế.";
        if (!string.IsNullOrWhiteSpace(type.MeasurementMethod))
            hint += $"\nPhương pháp đo: {type.MeasurementMethod}.";
        TypeHint.Text = hint;

        if (_initializing) return;

        // Tự điền tên và đơn vị gợi ý, trừ khi người dùng đã tự gõ nội dung khác
        string newName = $"{type.Name} {CurrentZoneCode}";
        if (NameBox.Text.Length == 0 || NameBox.Text == _autoName) NameBox.Text = newName;
        _autoName = newName;

        if (UnitBox.Text.Length == 0 || UnitBox.Text == _autoUnit) UnitBox.Text = type.DefaultUnit;
        _autoUnit = type.DefaultUnit;
    }

    private void ZoneCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing || TypeCombo.SelectedItem is not SensorType type) return;

        // Đổi khu vực thì tên tự điền đổi theo (nếu người dùng chưa tự sửa tên)
        string newName = $"{type.Name} {CurrentZoneCode}";
        if (NameBox.Text == _autoName) NameBox.Text = newName;
        _autoName = newName;
    }

    private void NodeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (NodeCombo.SelectedItem is not Esp32Node node) return;

        int count = _service.CountActiveSensorsOfNode(node.Id);
        string text = $"{node.Id} đang phụ trách {count} cảm biến (dự kiến khoảng 10–12 cảm biến mỗi ESP32).";

        // Giữ nguyên ESP32 cũ (sửa hoặc thay thế) thì không nhắc vượt mức: số đếm đã gồm cảm biến này
        string? currentNodeId = _editingSensor?.NodeId ?? _replacingSensor?.NodeId;
        bool keepsSameNode = currentNodeId == node.Id;

        if (!keepsSameNode && count >= SensorManagementService.ExpectedMaxSensorsPerNode)
        {
            text += IsEditMode
                ? " Chuyển cảm biến sang ESP32 này sẽ vượt mức dự kiến (không bị chặn)."
                : " Thêm cảm biến này sẽ vượt mức dự kiến (không bị chặn).";
        }
        NodeHint.Text = text;
    }

    // ===== Lưu =====

    /// <summary>
    /// Đổi loại hoặc đơn vị của cảm biến đã có dữ liệu đo thì hỏi xác nhận.
    /// Trả về false nếu người dùng chọn không tiếp tục.
    /// </summary>
    private bool ConfirmHistoryImpact(string? newTypeId)
    {
        if (_editingSensor == null || newTypeId == null) return true;

        bool changed = newTypeId != _editingSensor.SensorTypeId
                       || UnitBox.Text.Trim() != _editingSensor.Unit;
        if (!changed || !_service.HasMeasurements(_editingSensor.Id)) return true;

        MessageBoxResult answer = MessageBox.Show(
            $"Cảm biến {_editingSensor.Id} đã có dữ liệu đo.\n\n" +
            "Đổi loại hoặc đơn vị đo sẽ làm các giá trị đo cũ được hiểu theo loại/đơn vị mới.\n" +
            "Nếu cảm biến vật lý đã được thay bằng loại khác, nên dùng chức năng Thay thế cảm biến.\n\n" +
            "Vẫn lưu thay đổi?",
            "Xác nhận", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);

        return answer == MessageBoxResult.Yes;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        string? typeId = (TypeCombo.SelectedItem as SensorType)?.Id;
        string? nodeId = (NodeCombo.SelectedItem as Esp32Node)?.Id;
        string? error;

        if (_editingSensor != null)
        {
            if (!ConfirmHistoryImpact(typeId)) return;

            SensorStatus status = (StatusCombo.SelectedItem as ComboBoxItem)?.Tag as string == "Faulty"
                ? SensorStatus.Faulty
                : SensorStatus.Active;

            error = _service.UpdateSensor(
                _editingSensor.Id, typeId, CurrentZoneCode, NameBox.Text, nodeId,
                UnitBox.Text, status, NotesBox.Text);
        }
        else if (_replacingSensor != null)
        {
            error = _service.ReplaceSensor(
    _replacingSensor.Id, typeId, CodeBox.Text, NameBox.Text, nodeId,
    UnitBox.Text, NotesBox.Text);
        }
        else
        {
            error = _service.AddSensor(
                _zoneCode, typeId, CodeBox.Text, NameBox.Text, nodeId, UnitBox.Text, NotesBox.Text);
        }

        if (error != null)
        {
            ErrorText.Text = error;
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        DialogResult = true;
    }
}