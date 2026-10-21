using QuanLyTuoiTieu.Models;

namespace QuanLyTuoiTieu.Services;

/// <summary>
/// Thêm, sửa, ngừng sử dụng khu vực. Kiểm tra dữ liệu đầu vào trước khi ghi.
/// Các phương thức trả về null nếu thành công, hoặc câu thông báo lỗi tiếng Việt.
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

    /// <summary>Danh sách mã ô còn trống trong sơ đồ 12 x 12 (A01 ... L12).</summary>
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

    /// <summary>Mã hợp lệ: chữ cái A-L và số 01-12, ví dụ A01, B07, L12.</summary>
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
                Name = name!.Trim(),
                Description = CleanDescription(description),
                IsActive = true
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

        return Save(new Zone
        {
            Code = code,
            Name = name!.Trim(),
            Description = CleanDescription(description),
            IsActive = existing.IsActive
        });
    }

    /// <summary>isActive = false: ngừng sử dụng khu vực. true: dùng lại. Không xóa dữ liệu.</summary>
    public string? SetActive(string code, bool isActive)
    {
        Zone? existing = _dataSource.GetZones().FirstOrDefault(z => z.Code == code);
        if (existing == null) return $"Không tìm thấy khu vực {code}.";

        return Save(new Zone
        {
            Code = code,
            Name = existing.Name,
            Description = existing.Description,
            IsActive = isActive
        });
    }

    private string? Save(Zone zone)
    {
        try
        {
            _dataSource.UpdateZone(zone);
            return null;
        }
        catch (InvalidOperationException ex)
        {
            return ex.Message;
        }
    }

    private static string? ValidateText(string? name, string? description)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Vui lòng nhập tên khu vực.";
        if (name.Trim().Length > MaxNameLength) return $"Tên khu vực tối đa {MaxNameLength} ký tự.";
        if ((description ?? "").Trim().Length > MaxDescriptionLength) return $"Mô tả tối đa {MaxDescriptionLength} ký tự.";
        return null;
    }

    private static string? CleanDescription(string? description)
    {
        string text = (description ?? "").Trim();
        return text.Length == 0 ? null : text;
    }
}