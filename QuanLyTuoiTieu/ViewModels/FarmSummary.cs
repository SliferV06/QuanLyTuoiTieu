namespace QuanLyTuoiTieu.ViewModels;

/// <summary>Số liệu thống kê tổng quan, được TÍNH từ dữ liệu (không ghi cứng).</summary>
public class FarmSummary
{
    public int TotalZones { get; set; }
    public int NormalZones { get; set; }
    public int WarningZones { get; set; }
    public int DisconnectedZones { get; set; }

    public int TotalSensors { get; set; }
    public int ActiveSensors { get; set; }
    public int FaultySensors { get; set; }
}