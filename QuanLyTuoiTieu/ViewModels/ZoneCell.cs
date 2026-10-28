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

    /// <summary>true = cụm cảm biến lệch khỏi mốc lắp đặt quá ngưỡng.</summary>
    public bool ClusterMoved { get; init; }

    /// <summary>Khoảng lệch (mét) khi ClusterMoved = true.</summary>
    public double? MovedMeters { get; init; }

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

    /// <summary>Tên khu vực hiển thị dưới mã trong ô. Không có tên (hoặc ô trống) thì để rỗng, không hiện gì.</summary>
    public string NameText => Zone?.Name.Trim() ?? "";

    public string ToolTipText
    {
        get
        {
            if (Zone == null) return $"{Code}: chưa có khu vực\nNhấn để thêm khu vực";

            string title = NameText.Length == 0 ? Code : $"{Code} - {NameText}";
            string text = $"{title}\nTrạng thái: {StatusText}\nSố cảm biến: {SensorCount}";

            if (ClusterMoved && MovedMeters is double meters)
                text += $"\n⚠ Cụm cảm biến lệch vị trí {meters:0} m so với mốc";

            return text;
        }
    }
}