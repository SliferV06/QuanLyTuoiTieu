using System.Windows;
using System.Windows.Controls;
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
    private string _autoName = "";   // tên gợi ý tự điền, để biết người dùng đã tự sửa tên chưa

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

    private void CodeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CodeCombo.SelectedItem is not string code) return;

        // Tự điền tên gợi ý, trừ khi người dùng đã tự gõ tên khác
        string newAuto = $"Khu vực {code}";
        if (NameBox.Text.Length == 0 || NameBox.Text == _autoName)
            NameBox.Text = newAuto;
        _autoName = newAuto;
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