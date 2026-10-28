using QuanLyTuoiTieu.Models;

namespace QuanLyTuoiTieu.Services;

/// <summary>
/// Thêm, sửa, xóa khu vực. Kiểm tra dữ liệu đầu vào trước khi ghi.
/// Các phương thức thao tác trả về null nếu thành công, hoặc câu thông báo lỗi tiếng Việt.
/// </summary>
public class ZoneManagementService
{
    private const int MaxNameLength = 100;
    private const int MaxDescriptionLength = 500;

    private readonly IFarmDataSource _dataSource;

    public ZoneManagementService(IFarmDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    /// <summary>Danh sách mã ô còn trống trong sơ đồ (A01 ... M12).</summary>
    public List<string> GetFreeCodes()
    {
        var used = _dataSource.GetZones().Select(z => z.Code).ToHashSet();
        var free = new List<string>();

        for (int row = 1; row <= Zone.MaxRows; row++)
        {
            for (int col = 1; col <= Zone.MaxColumns; col++)
            {
                string code = FarmOverviewService.MakeCode(row, col);
                if (!used.Contains(code)) free.Add(code);
            }
        }
        return free;
    }

    /// <summary>Mã hợp lệ: chữ cái A-M và số 01-12, ví dụ A01, B07, M12.</summary>
    public static bool IsValidCode(string code)
    {
        if (code.Length != 3) return false;

        char letter = code[0];
        if (letter < 'A' || letter >= (char)('A' + Zone.MaxRows)) return false;
        if (!int.TryParse(code.Substring(1), out int col)) return false;
        if (col < 1 || col > Zone.MaxColumns) return false;

        return code == FarmOverviewService.MakeCode(letter - 'A' + 1, col);
    }

    public string? AddZone(string? code, string? name, string? description)
    {
        code = (code ?? "").Trim().ToUpperInvariant();

        if (!IsValidCode(code)) return "Mã khu vực không hợp lệ (chỉ từ A01 đến L12).";
        if (_dataSource.GetZones().Any(z => z.Code == code)) return $"Mã khu vực {code} đã tồn tại.";
        if (_dataSource.GetZones().Count >= Zone.MaxZones) return $"Đã đủ {Zone.MaxZones} khu vực, không thể thêm.";

        string? error = ValidateText(name, description);
        if (error != null) return error;

        try
        {
            _dataSource.AddZone(new Zone
            {
                Code = code,
                Row = code[0] - 'A' + 1,
                Column = int.Parse(code.Substring(1)),
                Name = (name ?? "").Trim(),
                Description = CleanDescription(description)
            });
            return null;
        }
        catch (InvalidOperationException ex)
        {
            return ex.Message;
        }
    }

    public string? UpdateZone(string code, string? name, string? description)
    {
        Zone? existing = _dataSource.GetZones().FirstOrDefault(z => z.Code == code);
        if (existing == null) return $"Không tìm thấy khu vực {code}.";

        string? error = ValidateText(name, description);
        if (error != null) return error;

        try
        {
            _dataSource.UpdateZone(new Zone
            {
                Code = code,
                Name = (name ?? "").Trim(),
                Description = CleanDescription(description)
            });
            return null;
        }
        catch (InvalidOperationException ex)
        {
            return ex.Message;
        }
    }

    // ===== Xóa khu vực =====

    /// <summary>Số cảm biến của khu vực: đang quản lý và đã gỡ (cả hai đều bị xóa cùng khu vực).</summary>
    public (int Active, int Removed) CountSensors(string zoneCode)
    {
        var inZone = _dataSource.GetSensors().Where(s => s.ZoneCode == zoneCode).ToList();
        return (inZone.Count(s => s.Status != SensorStatus.Removed),
                inZone.Count(s => s.Status == SensorStatus.Removed));
    }

    /// <summary>
    /// Xóa khu vực: ô trở thành trống. Mọi cảm biến của khu vực (đang quản lý và đã gỡ)
    /// bị xóa hẳn cùng giá trị đo. Không hoàn tác được.
    /// </summary>
    public string? DeleteZone(string zoneCode)
    {
        Zone? existing = _dataSource.GetZones().FirstOrDefault(z => z.Code == zoneCode);
        if (existing == null) return $"Không tìm thấy khu vực {zoneCode}.";

        try
        {
            // ToList() để không sửa danh sách khi đang duyệt
            foreach (Sensor sensor in _dataSource.GetSensors().Where(s => s.ZoneCode == zoneCode).ToList())
            {
                _dataSource.DeleteSensor(sensor.Id);
            }

            _dataSource.DeleteZone(zoneCode);
            return null;
        }
        catch (InvalidOperationException ex)
        {
            return ex.Message;
        }
    }

    private static string? ValidateText(string? name, string? description)
    {
        // Tên không bắt buộc: để trống thì ô trên sơ đồ chỉ hiện mã
        if ((name ?? "").Trim().Length > MaxNameLength) return $"Tên khu vực tối đa {MaxNameLength} ký tự.";
        if ((description ?? "").Trim().Length > MaxDescriptionLength) return $"Mô tả tối đa {MaxDescriptionLength} ký tự.";
        return null;
    }

    private static string? CleanDescription(string? description)
    {
        string text = (description ?? "").Trim();
        return text.Length == 0 ? null : text;
    }
}