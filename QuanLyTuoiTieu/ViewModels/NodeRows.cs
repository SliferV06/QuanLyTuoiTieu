namespace QuanLyTuoiTieu.ViewModels;

/// <summary>Một dòng ESP32 trong bảng danh sách ESP32 (đã định dạng sẵn để hiển thị).</summary>
public class NodeRow
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public bool IsActive { get; init; }
    public string StatusText { get; init; } = "";

    /// <summary>Số khu vực có cảm biến đang quản lý do ESP32 này phụ trách.</summary>
    public int ZoneCount { get; init; }

    /// <summary>Số cảm biến đang quản lý (không tính đã gỡ).</summary>
    public int SensorCount { get; init; }

    public string ZonesText { get; init; } = "";

    /// <summary>Bản ghi đo mới nhất của các cảm biến thuộc ESP32 (đã gồm biểu tượng trạng thái).</summary>
    public string LastDataText { get; init; } = "";

    /// <summary>Ngày đăng ký ESP32 trong phần mềm.</summary>
    public string CreatedAtText { get; init; } = "";

    public string NotesText { get; init; } = "";
}

/// <summary>Một cảm biến do ESP32 đang chọn phụ trách.</summary>
public class NodeSensorRow
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string ZoneCode { get; init; } = "";
    public string TypeName { get; init; } = "";
    public string StatusText { get; init; } = "";
}