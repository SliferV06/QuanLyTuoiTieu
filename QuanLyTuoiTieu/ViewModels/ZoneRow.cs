namespace QuanLyTuoiTieu.ViewModels;

/// <summary>Một hàng của ma trận (12 ô). Dùng để ma trận cuộn theo từng hàng.</summary>
public class ZoneRow
{
    public ZoneRow(IReadOnlyList<ZoneCell> cells)
    {
        Cells = cells;
    }

    public IReadOnlyList<ZoneCell> Cells { get; }
}