using QuanLyTuoiTieu.Models;
using QuanLyTuoiTieu.Services;

namespace QuanLyTuoiTieu.Data;

/// <summary>
/// DỮ LIỆU MÔ PHỎNG (GIẢ LẬP) dùng trong giai đoạn làm giao diện.
/// KHÔNG phải dữ liệu đo thật. Giá trị cố định, không ngẫu nhiên.
/// Cấu hình (khu vực, cảm biến, ESP32) giữ nguyên khi làm mới; chỉ giá trị đo được tạo lại.
/// </summary>
public class MockFarmDataSource : IFarmDataSource, ISimulationControls
{
    private enum Feed { Fresh, Stale, None, Error }

    /// <summary>Kịch bản dữ liệu mô phỏng của một cảm biến mẫu.</summary>
    private sealed record SeedInfo(Feed Feed, int Kind, int ZoneIndex);

    // 4 loại cảm biến có trong mỗi khu vực mô phỏng (chỉ số 0..3)
    private static readonly (string TypeId, string Abbr, string Label)[] PerZoneKinds =
    {
        ("SOIL_MOISTURE", "SM", "Độ ẩm đất"),
        ("SOIL_EC",       "EC", "EC đất"),
        ("AIR_TEMP",      "TA", "Nhiệt độ"),
        ("AIR_HUMIDITY",  "AH", "Độ ẩm không khí"),
    };

    /// <summary>Thời gian giả lập chờ mạng khi đồng bộ (mô phỏng độ trễ của máy chủ).</summary>
    public static readonly TimeSpan SimulatedLatency = TimeSpan.FromSeconds(1.5);

    private readonly List<Zone> _zones = new();
    private readonly List<Esp32Node> _nodes = new();
    private readonly List<SensorType> _sensorTypes = new();
    private readonly List<Sensor> _sensors = new();
    private readonly Dictionary<string, SeedInfo> _seeds = new();

    // Cảm biến đã bị gỡ trong lúc chạy: giữ nguyên giá trị đo cuối cùng, không tạo giá trị mới.
    private readonly HashSet<string> _frozenSensorIds = new();

    private Dictionary<string, Measurement> _latest = new();
    private long _measurementId;

    public MockFarmDataSource()
    {
        BuildAll();
    }

    public IReadOnlyList<Zone> GetZones() => _zones;
    public IReadOnlyList<Esp32Node> GetNodes() => _nodes;
    public IReadOnlyList<SensorType> GetSensorTypes() => _sensorTypes;
    public IReadOnlyList<Sensor> GetSensors() => _sensors;
    public IReadOnlyDictionary<string, Measurement> GetLatestMeasurements() => _latest;

    public string SourceName => "Dữ liệu mô phỏng";

    public bool IsSimulated => true;

    // ===== Công cụ thử nghiệm (ISimulationControls) =====

    public bool SimulateDisconnected { get; set; }

    public void AgeMeasurements(TimeSpan span)
    {
        foreach (Measurement m in _latest.Values)
            m.MeasuredAt -= span;
    }

    public void ResetSampleData()
    {
        BuildAll();
    }

    public void FillAllZones()
    {
        DateTime now = DateTime.UtcNow;
        int sensorCountBefore = _sensors.Count;
        var existingCodes = _zones.Select(z => z.Code).ToHashSet();

        for (int row = 1; row <= Zone.MaxRows; row++)
        {
            for (int col = 1; col <= Zone.MaxColumns; col++)
            {
                string code = FarmOverviewService.MakeCode(row, col);
                if (existingCodes.Contains(code)) continue;   // giữ nguyên khu vực đã có

                int zoneIndex = (row - 1) * Zone.MaxColumns + (col - 1);
                BuildExtraZone(row, col, zoneIndex, now);
            }
        }

        // Chỉ tạo giá trị đo cho cảm biến MỚI, không đụng tới dữ liệu đang có
        for (int i = sensorCountBefore; i < _sensors.Count; i++)
        {
            Sensor sensor = _sensors[i];
            if (!_seeds.TryGetValue(sensor.Id, out SeedInfo? seed)) continue;

            Measurement? m = CreateMeasurement(sensor.Id, seed, now);
            if (m != null) _latest[sensor.Id] = m;
        }
    }

    // ===== Đồng bộ =====

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        if (SimulateDisconnected)
        {
            // Không có phản hồi từ "máy chủ": chờ cho tới khi bị hủy (hết thời gian chờ).
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        // Giả lập độ trễ khi lấy dữ liệu. Không chặn giao diện.
        await Task.Delay(SimulatedLatency, cancellationToken);

        // Chỉ thay dữ liệu SAU KHI "tải" thành công.
        // Đoạn này chạy trên luồng giao diện nên không cần khóa.
        RefreshCore();
    }

    /// <summary>Tạo lại giá trị đo mô phỏng theo thời điểm hiện tại. Không đụng tới cấu hình.</summary>
    private void RefreshCore()
    {
        DateTime now = DateTime.UtcNow;
        var newLatest = new Dictionary<string, Measurement>();

        foreach (Sensor sensor in _sensors)
        {
            // Cảm biến đã gỡ: giữ lại bản ghi đo cuối cùng (lịch sử), không tạo bản ghi mới
            if (_frozenSensorIds.Contains(sensor.Id))
            {
                if (_latest.TryGetValue(sensor.Id, out Measurement? last))
                    newLatest[sensor.Id] = last;
                continue;
            }

            // Cảm biến không có kịch bản (ví dụ cảm biến người dùng thêm sau này) thì chưa có dữ liệu.
            if (!_seeds.TryGetValue(sensor.Id, out SeedInfo? seed)) continue;

            Measurement? m = CreateMeasurement(sensor.Id, seed, now);
            if (m != null) newLatest[sensor.Id] = m;
        }

        _latest = newLatest;
    }

    // ===== Khu vực =====

    public void AddZone(Zone zone)
    {
        if (_zones.Any(z => z.Code == zone.Code))
            throw new InvalidOperationException($"Mã khu vực {zone.Code} đã tồn tại.");
        if (_zones.Count >= Zone.MaxZones)
            throw new InvalidOperationException($"Đã đủ {Zone.MaxZones} khu vực, không thể thêm.");

        _zones.Add(zone);
    }

    public void UpdateZone(Zone zone)
    {
        Zone? existing = _zones.FirstOrDefault(z => z.Code == zone.Code);
        if (existing == null)
            throw new InvalidOperationException($"Không tìm thấy khu vực {zone.Code}.");

        existing.Name = zone.Name;
        existing.Description = zone.Description;
    }

    public void DeleteZone(string zoneCode)
    {
        Zone? existing = _zones.FirstOrDefault(z => z.Code == zoneCode);
        if (existing == null)
            throw new InvalidOperationException($"Không tìm thấy khu vực {zoneCode}.");
        if (_sensors.Any(s => s.ZoneCode == zoneCode))
            throw new InvalidOperationException($"Khu vực {zoneCode} còn cảm biến, cần xóa cảm biến trước.");

        _zones.Remove(existing);
    }

    // ===== Cảm biến =====

    public void AddSensor(Sensor sensor)
    {
        if (_sensors.Any(s => string.Equals(s.Id, sensor.Id, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Mã cảm biến {sensor.Id} đã tồn tại.");

        // Không đăng ký kịch bản dữ liệu mô phỏng (_seeds) cho cảm biến mới:
        // nó sẽ hiện "Chưa có dữ liệu" cho tới khi có nguồn dữ liệu thật.
        _sensors.Add(sensor);
    }

    public void UpdateSensor(Sensor sensor)
    {
        Sensor? existing = _sensors.FirstOrDefault(s => s.Id == sensor.Id);
        if (existing == null)
            throw new InvalidOperationException($"Không tìm thấy cảm biến {sensor.Id}.");

        existing.Name = sensor.Name;
        existing.SensorTypeId = sensor.SensorTypeId;
        existing.ZoneCode = sensor.ZoneCode;
        existing.NodeId = sensor.NodeId;
        existing.Unit = sensor.Unit;
        existing.Status = sensor.Status;
        existing.Notes = sensor.Notes;
        // Không đổi InstalledAt, RemovedAt, RemovalReason, ReplacedBySensorId, RemovedWithNodeId (thuộc về lịch sử).
    }

    public void RemoveSensor(string sensorId, DateTime removedAtUtc, string? replacedBySensorId,
                             string? removedWithNodeId = null)
    {
        Sensor? existing = _sensors.FirstOrDefault(s => s.Id == sensorId);
        if (existing == null)
            throw new InvalidOperationException($"Không tìm thấy cảm biến {sensorId}.");
        if (existing.Status == SensorStatus.Removed)
            throw new InvalidOperationException($"Cảm biến {sensorId} đã được gỡ trước đó.");

        // Chỉ đánh dấu, chưa xóa khỏi danh sách
        existing.Status = SensorStatus.Removed;
        existing.RemovedAt = removedAtUtc;
        existing.RemovalReason = removedWithNodeId == null ? null : $"Ngừng sử dụng ESP32 {removedWithNodeId}";
        existing.ReplacedBySensorId = replacedBySensorId;
        existing.RemovedWithNodeId = removedWithNodeId;

        _frozenSensorIds.Add(sensorId);
    }

    public void RestoreSensor(string sensorId, string historyNote)
    {
        Sensor? existing = _sensors.FirstOrDefault(s => s.Id == sensorId);
        if (existing == null)
            throw new InvalidOperationException($"Không tìm thấy cảm biến {sensorId}.");
        if (existing.Status != SensorStatus.Removed)
            throw new InvalidOperationException($"Cảm biến {sensorId} chưa bị gỡ.");

        existing.Status = SensorStatus.Active;
        existing.RemovedAt = null;
        existing.RemovalReason = null;
        existing.ReplacedBySensorId = null;
        existing.RemovedWithNodeId = null;
        existing.Notes = string.IsNullOrWhiteSpace(existing.Notes)
            ? historyNote
            : $"{existing.Notes}. {historyNote}";

        // Cho phép tạo lại giá trị đo mô phỏng ở lần làm mới tiếp theo
        _frozenSensorIds.Remove(sensorId);
    }

    public void DeleteSensor(string sensorId)
    {
        Sensor? existing = _sensors.FirstOrDefault(s => s.Id == sensorId);
        if (existing == null)
            throw new InvalidOperationException($"Không tìm thấy cảm biến {sensorId}.");

        _sensors.Remove(existing);
        _seeds.Remove(sensorId);
        _frozenSensorIds.Remove(sensorId);
        _latest.Remove(sensorId);

        // Cảm biến khác từng ghi "được thay bằng cảm biến này" thì bỏ liên kết (không còn tồn tại)
        foreach (Sensor other in _sensors.Where(s => s.ReplacedBySensorId == sensorId))
            other.ReplacedBySensorId = null;
    }

    // ===== ESP32 =====

    public void AddNode(Esp32Node node)
    {
        if (_nodes.Any(n => string.Equals(n.Id, node.Id, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Mã ESP32 {node.Id} đã tồn tại.");

        _nodes.Add(node);
    }

    public void UpdateNode(Esp32Node node)
    {
        Esp32Node? existing = _nodes.FirstOrDefault(n => n.Id == node.Id);
        if (existing == null)
            throw new InvalidOperationException($"Không tìm thấy ESP32 {node.Id}.");

        existing.Name = node.Name;
        existing.Notes = node.Notes;
        existing.IsActive = node.IsActive;
    }

    public void DeleteNode(string nodeId)
    {
        Esp32Node? existing = _nodes.FirstOrDefault(n => n.Id == nodeId);
        if (existing == null)
            throw new InvalidOperationException($"Không tìm thấy ESP32 {nodeId}.");
        if (_sensors.Any(s => s.NodeId == nodeId))
            throw new InvalidOperationException($"ESP32 {nodeId} còn cảm biến, cần xóa cảm biến trước.");

        _nodes.Remove(existing);
    }

    // ===== Dựng dữ liệu mẫu =====

    /// <summary>Xóa toàn bộ và dựng lại dữ liệu mẫu ban đầu (48 khu vực, 16 ESP32).</summary>
    private void BuildAll()
    {
        _zones.Clear();
        _nodes.Clear();
        _sensorTypes.Clear();
        _sensors.Clear();
        _seeds.Clear();
        _frozenSensorIds.Clear();
        _latest = new Dictionary<string, Measurement>();

        BuildSensorTypes();
        BuildNodes();
        BuildZonesAndSensors(DateTime.UtcNow);
        RefreshCore();
    }

    private void BuildSensorTypes()
    {
        _sensorTypes.Add(new SensorType { Id = "SOIL_EC", Name = "Độ dẫn điện của đất (EC)", DefaultUnit = "mS/cm" });
        _sensorTypes.Add(new SensorType { Id = "SOIL_MOISTURE", Name = "Độ ẩm đất", DefaultUnit = "%" });
        _sensorTypes.Add(new SensorType { Id = "AIR_TEMP", Name = "Nhiệt độ môi trường", DefaultUnit = "°C" });
        _sensorTypes.Add(new SensorType { Id = "AIR_HUMIDITY", Name = "Độ ẩm không khí", DefaultUnit = "%" });

        // Nitơ, oxy: chưa có thông tin kỹ thuật thực tế nên để trống đơn vị.
        _sensorTypes.Add(new SensorType
        {
            Id = "NITROGEN",
            Name = "Nitơ",
            DefaultUnit = "",
            MeasurementMethod = "Chưa xác định (chờ thông tin kỹ thuật của cảm biến thực tế)"
        });
        _sensorTypes.Add(new SensorType
        {
            Id = "OXYGEN",
            Name = "Oxy",
            DefaultUnit = "",
            MeasurementMethod = "Chưa xác định (chờ thông tin kỹ thuật của cảm biến thực tế)"
        });
    }

    private void BuildNodes()
    {
        // 16 node x 3 khu vực = 48 khu vực
        for (int n = 1; n <= 16; n++)
        {
            _nodes.Add(new Esp32Node
            {
                Id = $"ESP-{n:00}",
                Name = $"ESP32 số {n}",
                Notes = "Dữ liệu mô phỏng",
                CreatedAt = DateTime.UtcNow.AddDays(-30)
            });
        }
    }

    private void BuildZonesAndSensors(DateTime now)
    {
        for (int row = 1; row <= 4; row++)          // hàng A..D
        {
            for (int col = 1; col <= 12; col++)     // cột 01..12
            {
                int zoneIndex = (row - 1) * 12 + (col - 1);
                string code = FarmOverviewService.MakeCode(row, col);

                var zone = new Zone
                {
                    Code = code,
                    Row = row,
                    Column = col,
                    Name = $"Khu vực {code}",
                    Description = "Khu vực mô phỏng"
                };
                _zones.Add(zone);

                // A05, A06: chưa cấu hình, chưa có cảm biến
                if (code is "A05" or "A06") continue;

                string nodeId = $"ESP-{zoneIndex / 3 + 1:00}";
                int kindCount = code == "B07" ? 2 : 4;   // B07 chỉ có 2 cảm biến

                for (int k = 0; k < kindCount; k++)
                {
                    // D11: cảm biến EC cũ đã gỡ và được thay bằng D11-EC-02
                    if (code == "D11" && k == 1)
                    {
                        Sensor old = AddSensor(zone, nodeId, k, zoneIndex, 1, SensorStatus.Removed, Feed.Stale, now);
                        old.RemovedAt = now.AddDays(-2);
                        old.RemovalReason = "Cảm biến hỏng, đã thay thế (mô phỏng)";
                        old.ReplacedBySensorId = "D11-EC-02";
                        AddSensor(zone, nodeId, k, zoneIndex, 2, SensorStatus.Active, Feed.Fresh, now);
                        continue;
                    }

                    var status = SensorStatus.Active;
                    var feed = Feed.Fresh;

                    if (code == "A08" && k == 1) { status = SensorStatus.Faulty; feed = Feed.Error; }  // cảm biến lỗi
                    else if (code == "B03" && k == 2) { feed = Feed.Error; }                          // bản ghi lỗi
                    else if (code == "B09" && k == 3) { feed = Feed.Stale; }                          // một phần dữ liệu cũ
                    else if (code is "C02" or "C03") { feed = Feed.Stale; }                           // mất kết nối (dữ liệu cũ)
                    else if (code == "C10") { feed = Feed.None; }                                     // chưa có dữ liệu

                    AddSensor(zone, nodeId, k, zoneIndex, 1, status, feed, now);
                }
            }
        }
    }

    /// <summary>
    /// Dựng một khu vực mẫu cho các ô E01..L12. Tình huống chọn theo zoneIndex (cố định, không ngẫu nhiên):
    ///  - zoneIndex % 17 == 3  : chưa có cảm biến (Chưa cấu hình)
    ///  - zoneIndex % 13 == 4  : mọi cảm biến có dữ liệu cũ (Mất kết nối)
    ///  - zoneIndex % 29 == 11 : mọi cảm biến chưa có dữ liệu (Mất kết nối)
    ///  - zoneIndex % 20 == 7  : 12 cảm biến (mỗi loại 3 cảm biến)
    ///  - Với khu vực dữ liệu mới: % 11 == 2 có 1 bản ghi lỗi; % 9 == 6 có 1 cảm biến dữ liệu cũ;
    ///    % 23 == 0 có 1 cảm biến "Có lỗi".
    /// </summary>
    private void BuildExtraZone(int row, int col, int zoneIndex, DateTime now)
    {
        string code = FarmOverviewService.MakeCode(row, col);

        var zone = new Zone
        {
            Code = code,
            Row = row,
            Column = col,
            Name = $"Khu vực {code}",
            Description = "Khu vực mô phỏng"
        };
        _zones.Add(zone);

        if (zoneIndex % 17 == 3) return;   // chưa có cảm biến

        int nodeNumber = zoneIndex / 3 + 1;
        string nodeId = $"ESP-{nodeNumber:00}";
        EnsureNode(nodeId, nodeNumber, now);

        Feed baseFeed = Feed.Fresh;
        if (zoneIndex % 13 == 4) baseFeed = Feed.Stale;
        else if (zoneIndex % 29 == 11) baseFeed = Feed.None;

        int copies = zoneIndex % 20 == 7 ? 3 : 1;   // 3 cảm biến mỗi loại = 12 cảm biến

        for (int kind = 0; kind < PerZoneKinds.Length; kind++)
        {
            for (int serial = 1; serial <= copies; serial++)
            {
                var status = SensorStatus.Active;
                Feed feed = baseFeed;

                // Tình huống riêng của từng cảm biến, chỉ áp dụng cho khu vực có dữ liệu mới
                if (baseFeed == Feed.Fresh && serial == 1)
                {
                    if (kind == 2 && zoneIndex % 11 == 2) feed = Feed.Error;
                    else if (kind == 3 && zoneIndex % 9 == 6) feed = Feed.Stale;
                    else if (kind == 1 && zoneIndex % 23 == 0) { status = SensorStatus.Faulty; feed = Feed.Error; }
                }

                Sensor sensor = AddSensor(zone, nodeId, kind, zoneIndex, serial, status, feed, now);
                if (copies > 1) sensor.Name += $" ({serial})";
            }
        }
    }

    /// <summary>
    /// Đảm bảo ESP32 tồn tại và đang hoạt động trước khi gán cảm biến mẫu cho nó
    /// (ESP32 đang ngừng sử dụng sẽ được bật lại, nhưng không tự khôi phục các cảm biến đã gỡ cùng).
    /// </summary>
    private void EnsureNode(string nodeId, int number, DateTime now)
    {
        Esp32Node? node = _nodes.FirstOrDefault(
            n => string.Equals(n.Id, nodeId, StringComparison.OrdinalIgnoreCase));

        if (node == null)
        {
            _nodes.Add(new Esp32Node
            {
                Id = nodeId,
                Name = $"ESP32 số {number}",
                Notes = "Dữ liệu mô phỏng",
                CreatedAt = now.AddDays(-30)
            });
        }
        else
        {
            node.IsActive = true;
        }
    }

    private Sensor AddSensor(Zone zone, string nodeId, int kind, int zoneIndex, int serial,
                             SensorStatus status, Feed feed, DateTime now)
    {
        var (typeId, abbr, label) = PerZoneKinds[kind];
        SensorType type = _sensorTypes.First(t => t.Id == typeId);

        // Tránh trùng mã với cảm biến người dùng đã tự tạo
        string id = $"{zone.Code}-{abbr}-{serial:00}";
        while (_sensors.Any(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase)))
            id += "X";

        var sensor = new Sensor
        {
            Id = id,
            Name = $"{label} {zone.Code}",
            SensorTypeId = typeId,
            ZoneCode = zone.Code,
            NodeId = nodeId,
            Unit = type.DefaultUnit,
            Status = status,
            Notes = "Dữ liệu mô phỏng",
            InstalledAt = now.AddDays(-30)
        };
        _sensors.Add(sensor);
        _seeds[sensor.Id] = new SeedInfo(feed, kind, zoneIndex);   // giá trị đo được tạo trong RefreshCore()
        return sensor;
    }

    private Measurement? CreateMeasurement(string sensorId, SeedInfo seed, DateTime now)
    {
        if (seed.Feed == Feed.None) return null;   // chưa có bản ghi nào

        var m = new Measurement { Id = ++_measurementId, SensorId = sensorId };
        switch (seed.Feed)
        {
            case Feed.Fresh:
                m.MeasuredAt = now.AddMinutes(-(3 + seed.ZoneIndex % 7));
                m.Value = SampleValue(seed.Kind, seed.ZoneIndex);
                break;
            case Feed.Stale:
                m.MeasuredAt = now.AddHours(-3);
                m.Value = SampleValue(seed.Kind, seed.ZoneIndex);
                break;
            case Feed.Error:
                m.MeasuredAt = now.AddMinutes(-5);
                m.Value = null;   // lỗi thì để null, không dùng 0
                m.ErrorMessage = "Không đọc được dữ liệu từ cảm biến (mô phỏng)";
                break;
        }
        return m;
    }

    // Giá trị mô phỏng cố định, chỉ để thử giao diện.
    private static double SampleValue(int kind, int zoneIndex) => kind switch
    {
        0 => 35 + (zoneIndex % 7) * 2,
        1 => Math.Round(1.0 + (zoneIndex % 5) * 0.1, 1),
        2 => 26 + (zoneIndex % 6) * 0.5,
        _ => 70 + (zoneIndex % 8)
    };
}