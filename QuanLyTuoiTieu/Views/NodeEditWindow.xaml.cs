using System.Windows;
using System.Windows.Media;
using QuanLyTuoiTieu.Models;
using QuanLyTuoiTieu.Services;

namespace QuanLyTuoiTieu.Views;

/// <summary>
/// Hộp thoại thêm ESP32 (editingNode = null) hoặc sửa ESP32 (editingNode != null).
/// Trả về DialogResult = true nếu đã lưu thành công.
/// </summary>
public partial class NodeEditWindow : Window
{
    private readonly NodeManagementService _service;
    private readonly Esp32Node? _editingNode;

    /// <summary>Mã ESP32 vừa lưu (để danh sách chọn lại đúng dòng).</summary>
    public string? SavedNodeId { get; private set; }

    public NodeEditWindow(NodeManagementService service, Esp32Node? editingNode)
    {
        InitializeComponent();

        _service = service;
        _editingNode = editingNode;

        if (editingNode == null)
        {
            Title = "Thêm ESP32";
            HeaderText.Text = "THÊM ESP32 MỚI";

            var (id, name) = service.SuggestNode();
            CodeBox.Text = id;
            NameBox.Text = name;
            CodeHint.Text =
                "Chữ không dấu, số, dấu gạch ngang (-), gạch dưới (_); dài 3 đến 20 ký tự. Mã không được trùng.";

            CodeBox.Focus();
            CodeBox.SelectAll();
        }
        else
        {
            Title = $"Sửa ESP32 {editingNode.Id}";
            HeaderText.Text = $"SỬA ESP32 {editingNode.Id}";

            CodeBox.Text = editingNode.Id;
            CodeBox.IsReadOnly = true;
            CodeBox.Background = new SolidColorBrush(Color.FromRgb(0xE5, 0xE7, 0xEB));
            CodeHint.Text = "Mã ESP32 không thể thay đổi.";

            NameBox.Text = editingNode.Name;
            NotesBox.Text = editingNode.Notes ?? "";

            NameBox.Focus();
            NameBox.SelectAll();
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        string? error;

        if (_editingNode == null)
            error = _service.AddNode(CodeBox.Text, NameBox.Text, NotesBox.Text);
        else
            error = _service.UpdateNode(_editingNode.Id, NameBox.Text, NotesBox.Text);

        if (error != null)
        {
            ErrorText.Text = error;
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        SavedNodeId = _editingNode?.Id ?? CodeBox.Text.Trim().ToUpperInvariant();
        DialogResult = true;
    }
}