namespace QuanLyTuoiTieu.Models;

/// <summary>Một vị trí GPS (độ thập phân) và thời điểm ghi nhận (UTC).</summary>
public sealed record GpsPosition(double Latitude, double Longitude, DateTime ReportedAtUtc);