using Artibir.Izleme.Data;
using Artibir.Izleme.Excel;
using Artibir.Izleme.Models;
using Artibir.Izleme.Options;
using Artibir.Izleme.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Artibir.Izleme.Api;

public class PlantQueries(AppDbContext db, IOptions<RetentionOptions> retention)
{
    public async Task<object> PlantCardsAsync(IReadOnlyCollection<Plant> plants, CancellationToken ct)
    {
        var ids = plants.Select(p => p.Id).ToArray();
        var latest = await (
            from l in db.LatestReadings.AsNoTracking()
            join s in db.Signals.AsNoTracking() on l.SignalId equals s.Id
            where ids.Contains(l.PlantId)
            select new ReadingRow(l.PlantId, s.ItemId, s.SignalName, s.TurbineId, l.NumericValue, l.TextValue, l.IsGood, l.CapturedAtLocal)
        ).ToListAsync(ct);

        var byPlant = latest.GroupBy(r => r.PlantId).ToDictionary(g => g.Key, g => g.ToList());
        var turbines = await db.Turbines.AsNoTracking().Where(t => ids.Contains(t.PlantId)).ToListAsync(ct);
        var now = TurkeyTime.NowUnspecified();
        return plants.Select(p =>
        {
            byPlant.TryGetValue(p.Id, out var rows);
            rows ??= new List<ReadingRow>();
            var headline = Headline(p, turbines.Where(t => t.PlantId == p.Id).ToList(), rows, now);
            return new
            {
                p.Code,
                p.Name,
                p.Kind,
                p.Location,
                p.Technology,
                p.TurbineCount,
                p.SampleOnly,
                p.HasLiveFeed,
                live = headline
            };
        }).ToList();
    }

    public async Task<object?> LiveAsync(Plant plant, CancellationToken ct)
    {
        var rows = await (
            from l in db.LatestReadings.AsNoTracking()
            join s in db.Signals.AsNoTracking() on l.SignalId equals s.Id
            where l.PlantId == plant.Id
            select new ReadingRow(l.PlantId, s.ItemId, s.SignalName, s.TurbineId, l.NumericValue, l.TextValue, l.IsGood, l.CapturedAtLocal)
        ).ToListAsync(ct);
        var turbines = await db.Turbines.AsNoTracking().Where(t => t.PlantId == plant.Id).OrderBy(t => t.SortOrder).ToListAsync(ct);
        var batch = await db.IngestBatches.AsNoTracking()
            .Where(b => b.PlantId == plant.Id)
            .OrderByDescending(b => b.ReaderLocalTime)
            .ThenByDescending(b => b.Id)
            .Select(b => new
            {
                b.ReaderLocalTime,
                b.ReceivedAtUtc,
                b.Server,
                b.Status,
                b.ReadMethod,
                b.ReaderVersion,
                b.ReadCount,
                b.GoodCount,
                b.Cycle,
                b.ClockFallback
            })
            .FirstOrDefaultAsync(ct);
        var now = TurkeyTime.NowUnspecified();
        var cards = turbines.Select(t => TurbineCard(plant, t, rows)).ToList();
        var pom = Pom(rows);
        var connection = Connection(rows);
        var running = cards.Count(c => c.Status == "running");
        var maintenance = cards.Count(c => c.Status == "maintenance");
        var winds = cards.Where(c => c.Wind != null).Select(c => c.Wind!.Value).ToList();
        var captured = rows.Count == 0 ? (DateTime?)null : rows.Max(r => r.CapturedAtLocal);
        var openFaults = await db.Faults.CountAsync(f => f.PlantId == plant.Id && f.Status != "Kapalı", ct);
        var todayFrom = now.Date;
        var todayTo = todayFrom.AddDays(1);
        var today = await TodayEnergyAsync(plant.Id, todayFrom, todayTo, ct);

        return new
        {
            plant = new { plant.Code, plant.Name, plant.Kind, plant.Location, plant.Technology, plant.SampleOnly, plant.HasLiveFeed, plant.TurbineCount },
            snapshot = batch == null ? null : new
            {
                readerLocalTime = batch.ReaderLocalTime,
                receivedAtUtc = batch.ReceivedAtUtc,
                server = batch.Server,
                status = batch.Status,
                method = batch.ReadMethod,
                version = batch.ReaderVersion,
                readCount = batch.ReadCount,
                goodCount = batch.GoodCount,
                cycle = batch.Cycle,
                clockFallback = batch.ClockFallback,
                stale = captured != null && captured < now.AddMinutes(-2)
            },
            capturedAt = captured,
            pom,
            connection,
            turbines = cards,
            running,
            maintenance,
            nodata = cards.Count(c => c.Status == "nodata"),
            windMin = winds.Count == 0 ? (double?)null : winds.Min(),
            windMax = winds.Count == 0 ? (double?)null : winds.Max(),
            turbineSumKw = Math.Round(cards.Where(c => c.PowerKw != null).Sum(c => c.PowerKw!.Value), 1),
            openFaults,
            today
        };
    }

    public async Task<object> PowerSeriesAsync(Plant plant, DateTime from, DateTime to, string resolution, CancellationToken ct)
    {
        if (to <= from)
            return new { error = "Aralık geçersiz." };
        if ((to - from).TotalDays > 14)
            return new { error = "Güç serisi en fazla 14 gün. Daha uzun dönem için üretim (MWh) sorgusu kullanın." };

        var signal = await db.Signals.AsNoTracking()
            .Where(s => s.PlantId == plant.Id && EF.Functions.ILike(s.ItemId, "%POM01.Electrical.ActivePower"))
            .Select(s => new { s.Id, s.ItemId })
            .FirstOrDefaultAsync(ct);
        if (signal == null)
            return new { from, to, resolution, unit = "kW", itemId = (string?)null, points = Array.Empty<object>(), note = "POM aktif güç etiketi henüz yok." };

        var res = resolution == "hour" ? "hour" : "10min";
        var points = await AggregateSignalAsync(plant.Id, signal.Id, from, to, res, ct);
        return new
        {
            from,
            to,
            resolution = res,
            unit = "kW",
            itemId = signal.ItemId,
            points = points.Select(p => new { t = p.Bucket, value = p.Value, samples = p.Samples }),
            note = points.Count == 0 ? "Bu aralıkta güç okuması yok." : "POM / santral sayacı. 10 sn ham veri 7 gün tutulur; daha eskisi 10 dk ortalamadır."
        };
    }

    public async Task<object> ProductionAsync(Plant plant, DateTime from, DateTime to, string bucket, CancellationToken ct)
    {
        if (to <= from)
            return new { error = "Aralık geçersiz." };
        bucket = bucket switch
        {
            "hour" or "day" or "month" or "year" => bucket,
            _ => "day"
        };
        if (bucket == "hour" && (to - from).TotalDays > 62)
            return new { error = "Saatlik döküm en fazla 62 gün. Gün, ay veya yıl kovası kullanın." };

        var query = db.HourlyProductions.AsNoTracking()
            .Where(h => h.PlantId == plant.Id && h.HourStartLocal >= from && h.HourStartLocal < to && h.ProductionMwh != null);

        List<ProdPoint> points;
        if (bucket == "hour")
        {
            points = await query
                .OrderBy(h => h.HourStartLocal)
                .Select(h => new ProdPoint(h.Series, h.TurbineCode, h.HourStartLocal, h.ProductionMwh!.Value))
                .ToListAsync(ct);
        }
        else if (bucket == "day")
        {
            var raw = await query
                .GroupBy(h => new { h.Series, h.TurbineCode, h.HourStartLocal.Year, h.HourStartLocal.Month, h.HourStartLocal.Day })
                .Select(g => new
                {
                    g.Key.Series,
                    g.Key.TurbineCode,
                    g.Key.Year,
                    g.Key.Month,
                    g.Key.Day,
                    Mwh = g.Sum(x => x.ProductionMwh!.Value)
                })
                .ToListAsync(ct);
            points = raw.Select(r => new ProdPoint(r.Series, r.TurbineCode, new DateTime(r.Year, r.Month, r.Day), r.Mwh)).ToList();
        }
        else if (bucket == "month")
        {
            var raw = await query
                .GroupBy(h => new { h.Series, h.TurbineCode, h.HourStartLocal.Year, h.HourStartLocal.Month })
                .Select(g => new
                {
                    g.Key.Series,
                    g.Key.TurbineCode,
                    g.Key.Year,
                    g.Key.Month,
                    Mwh = g.Sum(x => x.ProductionMwh!.Value)
                })
                .ToListAsync(ct);
            points = raw.Select(r => new ProdPoint(r.Series, r.TurbineCode, new DateTime(r.Year, r.Month, 1), r.Mwh)).ToList();
        }
        else
        {
            var raw = await query
                .GroupBy(h => new { h.Series, h.TurbineCode, h.HourStartLocal.Year })
                .Select(g => new
                {
                    g.Key.Series,
                    g.Key.TurbineCode,
                    g.Key.Year,
                    Mwh = g.Sum(x => x.ProductionMwh!.Value)
                })
                .ToListAsync(ct);
            points = raw.Select(r => new ProdPoint(r.Series, r.TurbineCode, new DateTime(r.Year, 1, 1), r.Mwh)).ToList();
        }

        var bounds = await db.HourlyProductions.AsNoTracking()
            .Where(h => h.PlantId == plant.Id)
            .GroupBy(_ => 1)
            .Select(g => new { Min = g.Min(x => x.HourStartLocal), Max = g.Max(x => x.HourStartLocal) })
            .FirstOrDefaultAsync(ct);

        return new
        {
            from,
            to,
            bucket,
            source = "excel",
            availableFrom = bounds?.Min,
            availableTo = bounds?.Max,
            series = points
                .GroupBy(p => (p.Series, p.Turbine))
                .OrderBy(g => g.Key.Series)
                .ThenBy(g => g.Key.Turbine)
                .Select(g => new
                {
                    series = g.Key.Series,
                    turbine = g.Key.Turbine,
                    points = g.OrderBy(p => p.At).Select(p => new { t = p.At, mwh = Math.Round(p.Mwh, 3) })
                }),
            note = points.Count == 0
                ? "Bu aralıkta saatlik üretim yok. Aylık Excel dosyaları yönetim ekranından aktarılır."
                : "Kaynak: TÜRBİN DATA. Türbin üretimi sayaç farkından (MWh); sayaç ve türbin-sayaç farkı ayrı seridir."
        };
    }

    public async Task<object> AvailabilityAsync(Plant plant, DateTime from, DateTime to, CancellationToken ct)
    {
        if (to <= from)
            return new { error = "Aralık geçersiz." };
        if ((to - from).TotalDays > 800)
            return new { error = "Aralık çok uzun." };

        var cutoff = TurkeyTime.NowUnspecified().AddDays(-Math.Clamp(retention.Value.RawDays, 1, 60));
        var map = new Dictionary<int, Agg>();

        if (to > cutoff)
        {
            var rawFrom = from > cutoff ? from : cutoff;
            var raw = await db.Database.SqlQuery<AggRow>($"""
                SELECT s.turbine_id AS turbine_id,
                       COUNT(*) FILTER (WHERE r.is_good AND r.numeric_value = 3)::int AS running,
                       COUNT(*) FILTER (WHERE r.is_good AND r.numeric_value = 2)::int AS maintenance,
                       COUNT(*) FILTER (WHERE r.is_good)::int AS samples
                FROM reading_raws r
                JOIN signals s ON s.id = r.signal_id
                WHERE r.plant_id = {plant.Id}
                  AND s.signal_name = 'OperationStateInt'
                  AND s.turbine_id IS NOT NULL
                  AND r.captured_at_local >= {rawFrom}
                  AND r.captured_at_local < {to}
                GROUP BY s.turbine_id
                """).ToListAsync(ct);
            Merge(map, raw);
        }

        if (from < cutoff)
        {
            var oldTo = to < cutoff ? to : cutoff;
            var old = await db.Database.SqlQuery<AggRow>($"""
                SELECT t.turbine_id AS turbine_id,
                       COALESCE(SUM(t.running_samples), 0)::int AS running,
                       COALESCE(SUM(t.maintenance_samples), 0)::int AS maintenance,
                       COALESCE(SUM(t.good_count), 0)::int AS samples
                FROM reading_ten_mins t
                JOIN signals s ON s.id = t.signal_id
                WHERE t.plant_id = {plant.Id}
                  AND s.signal_name = 'OperationStateInt'
                  AND t.turbine_id IS NOT NULL
                  AND t.bucket_start_local >= {from}
                  AND t.bucket_start_local < {oldTo}
                GROUP BY t.turbine_id
                """).ToListAsync(ct);
            Merge(map, old);
        }

        var turbines = await db.Turbines.AsNoTracking().Where(t => t.PlantId == plant.Id).OrderBy(t => t.SortOrder).ToListAsync(ct);
        long run = 0, samples = 0, maint = 0;
        var list = turbines.Select(t =>
        {
            map.TryGetValue(t.Id, out var agg);
            agg ??= new Agg();
            run += agg.Running;
            maint += agg.Maintenance;
            samples += agg.Samples;
            return new
            {
                code = t.Code,
                runningRatio = Ratio(agg.Running, agg.Samples),
                maintenanceRatio = Ratio(agg.Maintenance, agg.Samples),
                samples = agg.Samples
            };
        }).ToList();

        return new
        {
            from,
            to,
            definition = "Çalışır durum oranı: OperationStateInt = 3 olan iyi kaliteli örneklerin oranı. Bakım = 2. Bu, Vestas availability sayacı değildir ve çalışan türbin adedinin anlık oranı da değildir.",
            plantRunningRatio = Ratio(run, samples),
            plantMaintenanceRatio = Ratio(maint, samples),
            samples,
            turbines = list,
            note = samples == 0 ? "Bu aralıkta durum örneği yok." : "Ham örnekler yaklaşık 7 gün tutulur; daha eskisi 10 dk kovalarından sayılır."
        };
    }

    public async Task<object> SignalsAsync(Plant plant, CancellationToken ct)
    {
        var rows = await (
            from s in db.Signals.AsNoTracking()
            where s.PlantId == plant.Id
            join l in db.LatestReadings.AsNoTracking() on s.Id equals l.SignalId into lj
            from l in lj.DefaultIfEmpty()
            orderby s.ItemId
            select new
            {
                s.ItemId,
                s.GroupName,
                s.SignalName,
                s.Unit,
                value = l == null ? null : (l.NumericValue != null ? l.NumericValue.ToString() : l.TextValue),
                good = l != null && l.IsGood,
                capturedAt = l == null ? (DateTime?)null : l.CapturedAtLocal
            }
        ).Take(5000).ToListAsync(ct);
        return new { count = rows.Count, signals = rows };
    }

    public async Task<object> FinanceAsync(Plant plant, DateTime from, DateTime to, CancellationToken ct)
    {
        var rows = await db.HourlyProductions.AsNoTracking()
            .Where(h => h.PlantId == plant.Id
                && h.HourStartLocal >= from && h.HourStartLocal < to
                && (h.Series == ProductionSeries.Meter || h.Series == ProductionSeries.TurbineTotal || h.Series == ProductionSeries.Difference)
                && h.ProductionMwh != null)
            .GroupBy(h => new { h.Series, h.HourStartLocal.Year, h.HourStartLocal.Month })
            .Select(g => new
            {
                g.Key.Series,
                g.Key.Year,
                g.Key.Month,
                Mwh = g.Sum(x => x.ProductionMwh!.Value)
            })
            .ToListAsync(ct);

        var months = rows.GroupBy(r => (r.Year, r.Month)).OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
            .Select(g => new
            {
                year = g.Key.Year,
                month = g.Key.Month,
                meterMwh = Round(g.Where(x => x.Series == ProductionSeries.Meter).Select(x => (double?)x.Mwh).FirstOrDefault()),
                turbineTotalMwh = Round(g.Where(x => x.Series == ProductionSeries.TurbineTotal).Select(x => (double?)x.Mwh).FirstOrDefault()),
                differenceMwh = Round(g.Where(x => x.Series == ProductionSeries.Difference).Select(x => (double?)x.Mwh).FirstOrDefault())
            }).ToList();

        return new
        {
            from,
            to,
            months,
            note = "Enerji mutabakatı: türbin saatlik üretimi ile sayaç (SAYAÇ MWh) ve aralarındaki fark. Tarife tanımlı değil; tutar hesaplanmaz. Bu ekran yalnız üst yönetim içindir."
        };
    }

    async Task<object> TodayEnergyAsync(int plantId, DateTime from, DateTime to, CancellationToken ct)
    {
        var meter = await SumSeries(plantId, ProductionSeries.Meter, from, to, ct);
        var total = await SumSeries(plantId, ProductionSeries.TurbineTotal, from, to, ct);
        if (meter != null)
            return new { mwh = meter, source = "meter", approximate = false, note = "Bugünün aktarılmış sayaç saatleri." };
        if (total != null)
            return new { mwh = total, source = "turbine_total", approximate = false, note = "Bugünün aktarılmış türbin toplamı." };

        var signalId = await db.Signals.AsNoTracking()
            .Where(s => s.PlantId == plantId && EF.Functions.ILike(s.ItemId, "%POM01.Electrical.ActivePower"))
            .Select(s => (int?)s.Id)
            .FirstOrDefaultAsync(ct);
        if (signalId == null)
            return new { mwh = (double?)null, source = "none", approximate = false, note = "Bugünün enerji toplamı için saatlik Excel veya POM güç serisi yok." };

        var points = await AggregateSignalAsync(plantId, signalId.Value, from, to, "10min", ct);
        if (points.Count == 0)
            return new { mwh = (double?)null, source = "none", approximate = false, note = "Bugün için güç örneği yok. Günlük MWh uydurulmaz." };

        var mwh = points.Sum(p => (p.Value ?? 0) * (10d / 60d) / 1000d);
        return new
        {
            mwh = Math.Round(mwh, 3),
            source = "pom_integral",
            approximate = true,
            note = "POM kW ortalamasının 10 dk kovalarından yaklaşık enerji. Sayaç endeksi değildir."
        };
    }

    async Task<double?> SumSeries(int plantId, string series, DateTime from, DateTime to, CancellationToken ct)
    {
        var any = await db.HourlyProductions.AnyAsync(h =>
            h.PlantId == plantId && h.Series == series && h.HourStartLocal >= from && h.HourStartLocal < to && h.ProductionMwh != null, ct);
        if (!any)
            return null;
        var sum = await db.HourlyProductions.Where(h =>
                h.PlantId == plantId && h.Series == series && h.HourStartLocal >= from && h.HourStartLocal < to && h.ProductionMwh != null)
            .SumAsync(h => h.ProductionMwh!.Value, ct);
        return Math.Round(sum, 3);
    }

    async Task<List<BucketRow>> AggregateSignalAsync(int plantId, int signalId, DateTime from, DateTime to, string resolution, CancellationToken ct)
    {
        List<BucketRow> raw;
        List<BucketRow> rolled;
        if (resolution == "hour")
        {
            raw = await db.Database.SqlQuery<BucketRow>($"""
                SELECT date_trunc('hour', captured_at_local) AS "Bucket",
                       AVG(numeric_value) AS "Value",
                       COUNT(*)::int AS "Samples"
                FROM reading_raws
                WHERE plant_id = {plantId} AND signal_id = {signalId}
                  AND is_good AND numeric_value IS NOT NULL
                  AND captured_at_local >= {from} AND captured_at_local < {to}
                GROUP BY 1
                ORDER BY 1
                """).ToListAsync(ct);
            rolled = await db.Database.SqlQuery<BucketRow>($"""
                SELECT date_trunc('hour', bucket_start_local) AS "Bucket",
                       AVG(avg_value) AS "Value",
                       SUM(sample_count)::int AS "Samples"
                FROM reading_ten_mins
                WHERE plant_id = {plantId} AND signal_id = {signalId}
                  AND avg_value IS NOT NULL
                  AND bucket_start_local >= {from} AND bucket_start_local < {to}
                GROUP BY 1
                ORDER BY 1
                """).ToListAsync(ct);
        }
        else
        {
            raw = await db.Database.SqlQuery<BucketRow>($"""
                SELECT date_trunc('hour', captured_at_local)
                         + (floor(EXTRACT(minute FROM captured_at_local) / 10) * INTERVAL '10 minutes') AS "Bucket",
                       AVG(numeric_value) AS "Value",
                       COUNT(*)::int AS "Samples"
                FROM reading_raws
                WHERE plant_id = {plantId} AND signal_id = {signalId}
                  AND is_good AND numeric_value IS NOT NULL
                  AND captured_at_local >= {from} AND captured_at_local < {to}
                GROUP BY 1
                ORDER BY 1
                """).ToListAsync(ct);
            rolled = await db.Database.SqlQuery<BucketRow>($"""
                SELECT bucket_start_local AS "Bucket",
                       avg_value AS "Value",
                       sample_count AS "Samples"
                FROM reading_ten_mins
                WHERE plant_id = {plantId} AND signal_id = {signalId}
                  AND avg_value IS NOT NULL
                  AND bucket_start_local >= {from} AND bucket_start_local < {to}
                ORDER BY 1
                """).ToListAsync(ct);
        }

        var map = new Dictionary<DateTime, BucketRow>();
        foreach (var row in rolled)
            map[row.Bucket] = row;
        foreach (var row in raw)
            map[row.Bucket] = row;
        return map.Values.OrderBy(r => r.Bucket).ToList();
    }

    static object? Headline(Plant plant, List<Turbine> turbines, List<ReadingRow> rows, DateTime now)
    {
        if (rows.Count == 0)
            return null;
        var cards = turbines.Select(t => TurbineCard(plant, t, rows)).ToList();
        var pom = Pom(rows);
        var winds = cards.Where(c => c.Wind != null).Select(c => c.Wind!.Value).ToList();
        var captured = rows.Max(r => r.CapturedAtLocal);
        return new
        {
            mw = pom.ActiveKw == null ? (double?)null : Math.Round(pom.ActiveKw.Value / 1000d, 2),
            windMin = winds.Count == 0 ? (double?)null : Math.Round(winds.Min(), 1),
            windMax = winds.Count == 0 ? (double?)null : Math.Round(winds.Max(), 1),
            running = cards.Count(c => c.Status == "running"),
            maintenance = cards.Count(c => c.Status == "maintenance"),
            total = cards.Count,
            capturedAt = captured,
            stale = captured < now.AddMinutes(-2)
        };
    }

    static TurbineDto TurbineCard(Plant plant, Turbine turbine, List<ReadingRow> rows)
    {
        ReadingRow? Pick(string signal)
        {
            var suffix = "." + turbine.WtgCode + "." + signal;
            return rows.FirstOrDefault(r => r.ItemId.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
        }

        var power = Pick("Grid.Power");
        var state = Pick("System.OperationStateInt");
        var wind = Pick("Ambient.WindSpeed");
        var dir = Pick("Ambient.WindDir");
        var ambient = Pick("Ambient.Temperature");
        var bearing = Pick("Gear.MainBearing.TemperatureRE");
        var rpm = Pick("RotorSystem.RotorRPM");
        var pitch = Pick("Blades.PitchAngle");
        var nacelle = Pick("Nacelle.Temperature");
        var possible = Pick("Grid.PossiblePower");
        int? stateInt = state?.NumericValue == null ? null : (int)Math.Round(state.NumericValue.Value);
        var any = power != null || state != null || wind != null;
        var good = (power?.IsGood ?? false) || (state?.IsGood ?? false);
        string status;
        if (!any)
            status = "nodata";
        else if (stateInt == 3)
            status = "running";
        else if (stateInt == 2)
            status = "maintenance";
        else if (stateInt == null && !good)
            status = "nodata";
        else
            status = "stopped";

        return new TurbineDto(
            turbine.Code,
            turbine.WtgCode,
            power?.IsGood == true ? power.NumericValue : power?.NumericValue,
            stateInt,
            status,
            wind?.NumericValue,
            dir?.NumericValue,
            ambient?.NumericValue,
            bearing?.NumericValue,
            rpm?.NumericValue,
            pitch?.NumericValue,
            nacelle?.NumericValue,
            possible?.NumericValue,
            good);
    }

    static PomDto Pom(List<ReadingRow> rows)
    {
        double? N(string suffix) => rows.FirstOrDefault(r => r.ItemId.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && r.IsGood)?.NumericValue;
        var active = rows.FirstOrDefault(r => r.ItemId.EndsWith("POM01.Electrical.ActivePower", StringComparison.OrdinalIgnoreCase));
        return new PomDto(
            active?.NumericValue,
            N("POM01.Electrical.ApparentPower"),
            N("POM01.Electrical.ReActivePower"),
            N("POM01.Electrical.Frequency"),
            N("POM01.Electrical.PowerFactor"),
            N("POM01.Electrical.CurrentL1"),
            N("POM01.Electrical.CurrentL2"),
            N("POM01.Electrical.CurrentL3"),
            N("POM01.Electrical.VoltageL1L2"),
            N("POM01.Electrical.VoltageL2L3"),
            N("POM01.Electrical.VoltageL3L1"),
            active?.CapturedAtLocal);
    }

    static object Connection(List<ReadingRow> rows)
    {
        string? T(string suffix)
        {
            var row = rows.FirstOrDefault(r => r.ItemId.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && r.ItemId.Count(c => c == '.') <= 2);
            if (row == null)
                row = rows.FirstOrDefault(r => string.Equals(r.SignalName, suffix, StringComparison.OrdinalIgnoreCase) && r.TurbineId == null);
            if (row == null)
                return null;
            return row.TextValue ?? row.NumericValue?.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        var connectedText = T("IsConnected");
        bool? connected = connectedText == null
            ? null
            : connectedText is "1" or "true" or "True" or "TRUE";
        return new
        {
            connected,
            status = T("ConnectionStatus"),
            ftp = T("FTPStatus"),
            lastError = T("LastError")
        };
    }

    static void Merge(Dictionary<int, Agg> map, IEnumerable<AggRow> rows)
    {
        foreach (var row in rows)
        {
            if (!map.TryGetValue(row.TurbineId, out var agg))
            {
                agg = new Agg();
                map[row.TurbineId] = agg;
            }
            agg.Running += row.Running;
            agg.Maintenance += row.Maintenance;
            agg.Samples += row.Samples;
        }
    }

    public static double? Ratio(long part, long samples) =>
        samples <= 0 ? null : Math.Round((double)part / samples, 4);

    static double? Round(double? value) => value == null ? null : Math.Round(value.Value, 3);

    sealed record ReadingRow(int PlantId, string ItemId, string SignalName, int? TurbineId, double? NumericValue, string? TextValue, bool IsGood, DateTime CapturedAtLocal);
    public sealed record TurbineDto(string Code, string Wtg, double? PowerKw, int? State, string Status, double? Wind, double? WindDir, double? Ambient, double? Bearing, double? Rpm, double? Pitch, double? Nacelle, double? PossibleKw, bool Good);
    public sealed record PomDto(double? ActiveKw, double? ApparentKva, double? ReactiveKvar, double? Frequency, double? PowerFactor, double? IL1, double? IL2, double? IL3, double? V12, double? V23, double? V31, DateTime? CapturedAt);
    sealed record ProdPoint(string Series, string Turbine, DateTime At, double Mwh);
    sealed class Agg { public long Running; public long Maintenance; public long Samples; }
    sealed class AggRow { public int TurbineId { get; set; } public int Running { get; set; } public int Maintenance { get; set; } public int Samples { get; set; } }
    sealed class BucketRow { public DateTime Bucket { get; set; } public double? Value { get; set; } public int Samples { get; set; } }
}
