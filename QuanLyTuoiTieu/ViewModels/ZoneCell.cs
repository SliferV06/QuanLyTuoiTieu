using System.ComponentModel;
using QuanLyTuoiTieu.Models;

namespace QuanLyTuoiTieu.ViewModels;

/// <summary>Một ô trong ma trận 12 x 12. Ô chưa có khu vực thì Zone = null.</summary>
public class ZoneCell : INotifyPropertyChanged
{
    public string Code { get; init; } = "";
    public Zone? Zone { get; init; }
    public ZoneStatus? Status { get; init; }
    public int SensorCount { get; init; }

    public bool HasZone => Zone != null;

    private bool _isMatch = true;

    /// <summary>true = ô khớp tìm kiếm/lọc hiện tại. false = ô bị làm mờ.</summary>
    public bool IsMatch
    {
        get => _isMatch;
        set
        {
            if (_isMatch == value) return;
            _isMatch = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsMatch)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string StatusText => StatusDisplay.ZoneStatusText(Status);

    public string StatusIcon => StatusDisplay.ZoneStatusIcon(Status);

    public string ToolTipText => Zone == null
        ? $"{Code}: chưa có khu vực\nNhấn để thêm khu vực"
        : $"{Code} - {Zone.Name}\nTrạng thái: {StatusText}\nSố cảm biến: {SensorCount}";
}