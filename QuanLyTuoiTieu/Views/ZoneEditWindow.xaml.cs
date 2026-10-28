using System.Windows;
using QuanLyTuoiTieu.Models;
using QuanLyTuoiTieu.Services;

namespace QuanLyTuoiTieu.Views;

/// <summary>
/// Hộp thoại thêm khu vực (editingZone = null) hoặc sửa khu vực (editingZone != null).
/// Trả về DialogResult = true nếu đã lưu thành công.
/// </summary>
public partial class ZoneEditWindow : Window
{
    private readonly ZoneManagementService _service;
    private readonly Zone? _editingZone;

    public ZoneEditWindow(ZoneManagementService service, Zone? editingZone, string? presetCode = null)
    {
        InitializeComponent();

        _service = service;
        _editingZone = editingZone;

        if (editingZone == null)
        {
            Title = "Thêm khu vực";
            HeaderText.Text = "THÊM KHU VỰC MỚI";

            List<string> free = service.GetFreeCodes();
            CodeCombo.ItemsSource = free;
            CodeHint.Text = $"Còn {free.Count} ô trống trong sơ đồ.";

            if (presetCode != null && free.Contains(presetCode))
                CodeCombo.SelectedItem = presetCode;
            else if (free.Count > 0)
                CodeCombo.SelectedIndex = 0;
        }
        else
        {
            Title = $"Sửa khu vực {editingZone.Code}";
            HeaderText.Text = $"SỬA KHU VỰC {editingZone.Code}";

            CodeCombo.Visibility = Visibility.Collapsed;
            CodeText.Visibility = Visibility.Visible;
            CodeText.Text = editingZone.Code;
            CodeHint.Text = "Mã khu vực không thể thay đổi.";

            NameBox.Text = editingZone.Name;
            DescriptionBox.Text = editingZone.Description ?? "";
        }

        NameBox.Focus();
        NameBox.SelectAll();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        string? error;

        if (_editingZone == null)
        {
            string code = CodeCombo.SelectedItem as string ?? "";
            error = _service.AddZone(code, NameBox.Text, DescriptionBox.Text);
        }
        else
        {
            error = _service.UpdateZone(_editingZone.Code, NameBox.Text, DescriptionBox.Text);
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