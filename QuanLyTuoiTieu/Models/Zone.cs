namespace QuanLyTuoiTieu.Models;

/// <summary>Một khu vực canh tác (một ô trong ma trận 12 x 12).</summary>
public class Zone
{
    public const int MaxRows = 12;      // hàng A..L
    public const int MaxColumns = 12;   // cột 01..12
    public const int MaxZones = MaxRows * MaxColumns; // 144

    /// <summary>Mã khu vực, duy nhất toàn hệ thống. Ví dụ: A01, B07, L12.</summary>
    public string Code { get; set; } = "";

    /// <summary>Hàng trong ma trận (1 = A ... 12 = L).</summary>
    public int Row { get; set; }

    /// <summary>Cột trong ma trận (1 ... 12).</summary>
    public int Column { get; set; }

    public string Name { get; set; } = "";

    public string? Description { get; set; }
}