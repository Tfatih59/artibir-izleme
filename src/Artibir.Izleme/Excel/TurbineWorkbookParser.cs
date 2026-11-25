using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ExcelDataReader;

namespace Artibir.Izleme.Excel;

public static class ProductionSeries
{
    public const string Turbine = "turbine";
    public const string TurbineTotal = "turbine_total";
    public const string Meter = "meter";
    public const string Difference = "difference";
}

public sealed class HourRow
{
    public DateTime HourStartLocal { get; init; }
    public string Series { get; init; } = "";
    public string TurbineCode { get; init; } = "";
    public double? ProductionMwh { get; init; }
    public double? CounterKwh { get; init; }
}

public sealed class MonthCoverage
{
    public int Year { get; init; }
    public int Month { get; init; }
    public int Hours { get; init; }
}

public sealed class ParsedWorkbook
{
    public List<HourRow> Rows { get; init; } = new();
    public List<MonthCoverage> Months { get; init; } = new();
    public int? PrimaryYear { get; init; }
    public int? PrimaryMonth { get; init; }
    public bool MultiMonth { get; init; }
    public List<string> Warnings { get; init; } = new();
    public string? Error { get; init; }
    public bool Success => Error == null;

    public static ParsedWorkbook Fail(string error) => new() { Error = error };
}

public static partial class TurbineWorkbookParser
{
    static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    public static ParsedWorkbook Parse(Stream stream, int? expectedYear = null, int? expectedMonth = null)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        using var reader = ExcelReaderFactory.CreateReader(stream);
        ParsedWorkbook? parsed = null;
        do
        {
            if (IsTurbineSheet(reader.Name))
            {
                parsed = ParseSheet(reader, expectedYear, expectedMonth);
                break;
            }
        } while (reader.NextResult());

        return parsed ?? ParsedWorkbook.Fail("Çalışma kitabında 'TÜRBİN DATA' sayfası yok.");
    }

    static ParsedWorkbook ParseSheet(IExcelDataReader reader, int? expectedYear, int? expectedMonth)
    {
        Col? cols = null;
        DateTime? currentDate = null;
        int? lastHourLabel = null;
        var prevCounters = new Dictionary<int, double>();
        var rows = new Dictionary<string, HourRow>(StringComparer.Ordinal);
        var warnings = new List<string>();
        var negative = false;
        var duplicateHours = false;
        var scanned = 0;

        while (reader.Read())
        {
            scanned++;
            if (cols == null)
            {
                if (scanned > 15)
                    return ParsedWorkbook.Fail("TÜRBİN DATA başlık satırı (GÜN / SAAT / T 1) bulunamadı.");
                if (LooksLikeHeader(reader))
                    cols = MapColumns(reader);
                continue;
            }

            if (cols.Hour < 0 || cols.Turbines.Count == 0)
                return ParsedWorkbook.Fail("Saat veya türbin sütunları eşlenemedi.");

            var dateCell = ReadDate(Cell(reader, cols.Date));
            if (dateCell != null && (currentDate == null || dateCell.Value.Date != currentDate.Value.Date))
            {
                currentDate = dateCell.Value.Date;
                lastHourLabel = null;
            }

            if (!TryReadHour(Cell(reader, cols.Hour), out var hour))
                continue;

            var emit = currentDate != null;
            DateTime hourStart = default;
            if (emit)
            {
                if (hour == 0 && lastHourLabel == 23)
                    hourStart = currentDate!.Value.AddHours(23);
                else if (hour == 0 && lastHourLabel == null)
                    emit = false;
                else if (hour == 0)
                {
                    hourStart = currentDate!.Value.AddHours(23);
                    if (warnings.Count < 20)
                        warnings.Add($"{currentDate:yyyy-MM-dd} gününde 00:00 satırı 23'ten sonra değil; günün son saati kabul edildi.");
                }
                else if (hour is >= 1 and <= 23)
                    hourStart = currentDate!.Value.AddHours(hour - 1);
                else
                    emit = false;
            }

            lastHourLabel = hour;

            foreach (var turbine in cols.Turbines)
            {
                var counter = ReadDouble(Cell(reader, turbine.CounterCol));
                double? fark = turbine.FarkCol >= 0 ? ReadDouble(Cell(reader, turbine.FarkCol)) : null;
                if (fark == null && counter != null && prevCounters.TryGetValue(turbine.Number, out var prev))
                {
                    var delta = counter.Value - prev;
                    if (delta < 0)
                        negative = true;
                    fark = Math.Max(0, delta) / 1000d;
                }

                if (counter != null)
                    prevCounters[turbine.Number] = counter.Value;

                if (!emit || fark == null && counter == null)
                    continue;

                var code = "T" + turbine.Number.ToString("00", CultureInfo.InvariantCulture);
                Add(rows, ref duplicateHours, new HourRow
                {
                    HourStartLocal = hourStart,
                    Series = ProductionSeries.Turbine,
                    TurbineCode = code,
                    ProductionMwh = fark == null ? null : Round(fark.Value),
                    CounterKwh = counter
                });
            }

            if (!emit)
                continue;

            AddPlant(rows, ref duplicateHours, cols.TotalCol, ProductionSeries.TurbineTotal, hourStart, reader);
            AddPlant(rows, ref duplicateHours, cols.MeterCol, ProductionSeries.Meter, hourStart, reader);
            AddPlant(rows, ref duplicateHours, cols.DiffCol, ProductionSeries.Difference, hourStart, reader);
        }

        if (cols == null)
            return ParsedWorkbook.Fail("TÜRBİN DATA başlık satırı bulunamadı.");
        if (rows.Count == 0)
            return ParsedWorkbook.Fail("Sayfada aktarılacak saatlik üretim satırı yok.");

        if (negative)
            warnings.Add("Bazı türbin sayaçları geri gitmiş; ilgili saat üretimi 0 MWh kabul edildi (MAX(0, fark)).");
        if (duplicateHours)
            warnings.Add("Aynı saat birden fazla satırda vardı; son değer kullanıldı.");

        var list = rows.Values.OrderBy(r => r.HourStartLocal).ThenBy(r => r.Series).ThenBy(r => r.TurbineCode).ToList();
        var months = list
            .GroupBy(r => (r.HourStartLocal.Year, r.HourStartLocal.Month))
            .Select(g => new MonthCoverage
            {
                Year = g.Key.Year,
                Month = g.Key.Month,
                Hours = g.Select(x => x.HourStartLocal).Distinct().Count()
            })
            .OrderByDescending(m => m.Hours)
            .ToList();

        var primary = months[0];
        var multi = months.Count > 1;
        if (multi)
            warnings.Add("Dosya birden fazla ay içeriyor: " + string.Join(", ", months.Select(m => $"{m.Year}-{m.Month:00}")));

        if (expectedYear != null && expectedYear != primary.Year)
            warnings.Add($"Beklenen yıl {expectedYear}, verideki baskın yıl {primary.Year}.");
        if (expectedMonth != null && expectedMonth != primary.Month)
            warnings.Add($"Beklenen ay {expectedMonth}, verideki baskın ay {primary.Month}.");

        var mismatch = multi
            || (expectedYear != null && expectedYear != primary.Year)
            || (expectedMonth != null && expectedMonth != primary.Month);

        return new ParsedWorkbook
        {
            Rows = list,
            Months = months,
            PrimaryYear = primary.Year,
            PrimaryMonth = primary.Month,
            MultiMonth = multi,
            Warnings = mismatch
                ? warnings.Prepend("Ay/yıl uyuşmazlığı işaretlendi.").Distinct().ToList()
                : warnings
        };
    }

    static void AddPlant(Dictionary<string, HourRow> rows, ref bool dup, int col, string series, DateTime hourStart, IExcelDataReader reader)
    {
        if (col < 0)
            return;
        var value = ReadDouble(Cell(reader, col));
        if (value == null)
            return;
        Add(rows, ref dup, new HourRow
        {
            HourStartLocal = hourStart,
            Series = series,
            TurbineCode = "",
            ProductionMwh = Round(value.Value)
        });
    }

    static void Add(Dictionary<string, HourRow> rows, ref bool dup, HourRow row)
    {
        var key = row.Series + "|" + row.TurbineCode + "|" + row.HourStartLocal.ToString("yyyyMMddHH", CultureInfo.InvariantCulture);
        if (rows.ContainsKey(key))
            dup = true;
        rows[key] = row;
    }

    static double Round(double value) => Math.Round(value, 6, MidpointRounding.AwayFromZero);

    static bool IsTurbineSheet(string? name) => Fold(name) == "TURBIN DATA";

    static bool LooksLikeHeader(IExcelDataReader reader)
    {
        var hasSaat = false;
        var hasTurbine = false;
        for (var i = 0; i < reader.FieldCount; i++)
        {
            var key = Fold(Cell(reader, i)?.ToString());
            if (key is "SAAT" or "GUN")
                hasSaat = true;
            if (TurbineNo().IsMatch(key))
                hasTurbine = true;
        }
        return hasSaat && hasTurbine;
    }

    static Col MapColumns(IExcelDataReader reader)
    {
        var col = new Col();
        var lastTurbine = -1;
        for (var i = 0; i < reader.FieldCount; i++)
        {
            var key = Fold(Cell(reader, i)?.ToString());
            if (key.Length == 0)
                continue;
            if (key is "GUN" or "TARIH")
            {
                col.Date = i;
                continue;
            }
            if (key == "SAAT")
            {
                col.Hour = i;
                continue;
            }
            var m = TurbineNo().Match(key);
            if (m.Success)
            {
                var n = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                col.Turbines.Add(new TurbineCol(n, i, -1));
                lastTurbine = col.Turbines.Count - 1;
                continue;
            }
            if (key.Contains("FARKI", StringComparison.Ordinal) || (key.Contains("SAYAC", StringComparison.Ordinal) && key.Contains("FARK", StringComparison.Ordinal)))
            {
                col.DiffCol = i;
                continue;
            }
            if (key.Contains("TOPLAM", StringComparison.Ordinal))
            {
                col.TotalCol = i;
                continue;
            }
            if (key.Contains("SAYAC", StringComparison.Ordinal))
            {
                col.MeterCol = i;
                continue;
            }
            if (key.StartsWith("FARK", StringComparison.Ordinal) && lastTurbine >= 0)
            {
                var t = col.Turbines[lastTurbine];
                col.Turbines[lastTurbine] = t with { FarkCol = i };
            }
        }
        return col;
    }

    static object? Cell(IExcelDataReader reader, int index) =>
        index >= 0 && index < reader.FieldCount ? reader.GetValue(index) : null;

    static DateTime? ReadDate(object? value)
    {
        switch (value)
        {
            case DateTime dt when dt.Year >= 2000:
                return dt.Date;
            case double d when d > 2000:
                try
                {
                    var dt = DateTime.FromOADate(d);
                    return dt.Year >= 2000 ? dt.Date : null;
                }
                catch (ArgumentException)
                {
                    return null;
                }
            case string s:
                var t = s.Trim();
                if (t.Length == 0)
                    return null;
                if (DateTime.TryParse(t, Tr, DateTimeStyles.None, out var tr) && tr.Year >= 2000)
                    return tr.Date;
                if (DateTime.TryParse(t, CultureInfo.InvariantCulture, DateTimeStyles.None, out var inv) && inv.Year >= 2000)
                    return inv.Date;
                return null;
            default:
                return null;
        }
    }

    static bool TryReadHour(object? value, out int hour)
    {
        hour = -1;
        switch (value)
        {
            case DateTime dt:
                hour = dt.Hour;
                return true;
            case TimeSpan ts:
                hour = ts.Hours;
                return true;
            case double d:
                if (d >= 0 && d < 1)
                {
                    hour = (int)Math.Round(d * 24, MidpointRounding.AwayFromZero) % 24;
                    return true;
                }
                if (d >= 0 && d <= 24 && Math.Abs(d - Math.Round(d)) < 0.001)
                {
                    hour = (int)Math.Round(d) % 24;
                    return true;
                }
                if (d > 1)
                {
                    try
                    {
                        hour = DateTime.FromOADate(d).Hour;
                        return true;
                    }
                    catch (ArgumentException)
                    {
                        return false;
                    }
                }
                return false;
            case string s:
                var t = s.Trim();
                if (t.Length == 0)
                    return false;
                if (TimeSpan.TryParse(t, CultureInfo.InvariantCulture, out var ts2))
                {
                    hour = ts2.Hours;
                    return true;
                }
                if (DateTime.TryParse(t, Tr, DateTimeStyles.None, out var dt2))
                {
                    hour = dt2.Hour;
                    return true;
                }
                return false;
            default:
                return false;
        }
    }

    static double? ReadDouble(object? value)
    {
        switch (value)
        {
            case null:
                return null;
            case double d when !double.IsNaN(d) && !double.IsInfinity(d):
                return d;
            case float f:
                return f;
            case int i:
                return i;
            case long l:
                return l;
            case decimal m:
                return (double)m;
            case string s:
                if (TagNumber(s, out var n))
                    return n;
                return null;
            default:
                return null;
        }
    }

    static bool TagNumber(string text, out double value)
    {
        var s = text.Trim();
        if (s.StartsWith('='))
        {
            value = 0;
            return false;
        }
        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            return true;
        return double.TryParse(s, NumberStyles.Float, Tr, out value);
    }

    public static string Fold(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "";
        var upper = raw.Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' ').Trim().ToUpper(Tr);
        var sb = new StringBuilder(upper.Length);
        var space = false;
        foreach (var ch in upper)
        {
            if (ch == ' ')
            {
                if (!space && sb.Length > 0)
                    sb.Append(' ');
                space = true;
                continue;
            }
            space = false;
            sb.Append(ch switch
            {
                'İ' or 'I' or 'ı' => 'I',
                'Ş' => 'S',
                'Ğ' => 'G',
                'Ü' => 'U',
                'Ö' => 'O',
                'Ç' => 'C',
                _ => ch
            });
        }
        return sb.ToString().Trim();
    }

    [GeneratedRegex(@"^T\s*(\d{1,2})$", RegexOptions.CultureInvariant)]
    private static partial Regex TurbineNo();

    sealed class Col
    {
        public int Date { get; set; } = -1;
        public int Hour { get; set; } = -1;
        public int TotalCol { get; set; } = -1;
        public int MeterCol { get; set; } = -1;
        public int DiffCol { get; set; } = -1;
        public List<TurbineCol> Turbines { get; } = new();
    }

    readonly record struct TurbineCol(int Number, int CounterCol, int FarkCol);
}
