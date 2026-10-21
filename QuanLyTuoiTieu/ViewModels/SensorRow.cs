using QuanLyTuoiTieu.Models;

namespace QuanLyTuoiTieu.ViewModels;

/// <summary>Một dòng cảm biến trong bảng của cửa sổ chi tiết (toàn bộ là chữ đã định dạng sẵn).</summary>
public class SensorRow
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string TypeName { get; init; } = "";
    public string NodeId { get; init; } = "";

    public string ValueText { get; init; } = "";
    public string Unit { get; init; } = "";
    public string MeasuredAtText { get; init; } = "";

    public string SensorStatusText { get; init; } = "";

    /// <summary>Dùng để chọn màu cho cột trạng thái dữ liệu.</summary>
    public DataStatus DataStatus { get; init; }
    public string DataStatusText { get; init; } = "";

    public string NotesText { get; init; } = "";

    /// <summary>true = cảm biến đã gỡ (hiển thị chữ xám).</summary>
    public bool IsRemoved { get; init; }
}