using System.Security.Cryptography;
using System.Text;
using Artibir.Izleme.Data;
using Artibir.Izleme.Models;
using Microsoft.EntityFrameworkCore;

namespace Artibir.Izleme.Excel;

public sealed record ImportFileResult(
    string File,
    bool Imported,
    bool SkippedDuplicate,
    int Rows,
    int? Year,
    int? Month,
    bool Mismatch,
    IReadOnlyList<string> Warnings,
    string? Error);

public class ExcelImportService(AppDbContext db)
{
    public const long MaxFileBytes = 40L * 1024 * 1024;

    public async Task<ImportFileResult> ImportAsync(
        Plant plant,
        string fileName,
        byte[] content,
        int? expectedYear,
        int? expectedMonth,
        string? importedBy,
        CancellationToken ct)
    {
        if (content.Length == 0)
            return new ImportFileResult(fileName, false, false, 0, null, null, true, Array.Empty<string>(), "Dosya boş.");
        if (content.Length > MaxFileBytes)
            return new ImportFileResult(fileName, false, false, 0, null, null, true, Array.Empty<string>(), "Dosya 40 MB sınırını aşıyor.");

        var hash = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        var existing = await db.ImportBatches.AsNoTracking()
            .Where(b => b.PlantId == plant.Id && b.FileHash == hash)
            .Select(b => new { b.RowCount, b.DetectedYear, b.DetectedMonth, b.MonthMismatch })
            .FirstOrDefaultAsync(ct);
        if (existing != null)
        {
            return new ImportFileResult(
                fileName, false, true, existing.RowCount, existing.DetectedYear, existing.DetectedMonth,
                existing.MonthMismatch, new[] { "Aynı dosya daha önce aktarılmış; atlandı." }, null);
        }

        ParsedWorkbook parsed;
        try
        {
            using var stream = new MemoryStream(content, writable: false);
            parsed = TurbineWorkbookParser.Parse(stream, expectedYear, expectedMonth);
        }
        catch (Exception ex)
        {
            return new ImportFileResult(fileName, false, false, 0, null, null, true, Array.Empty<string>(), "Excel okunamadı: " + ex.Message);
        }

        if (!parsed.Success)
            return new ImportFileResult(fileName, false, false, 0, parsed.PrimaryYear, parsed.PrimaryMonth, true, parsed.Warnings, parsed.Error);

        var nameWarning = FilenameHint(fileName, parsed.PrimaryYear, parsed.PrimaryMonth);
        var warnings = parsed.Warnings.ToList();
        if (nameWarning != null)
            warnings.Add(nameWarning);

        var mismatch = parsed.MultiMonth
            || (expectedYear != null && expectedYear != parsed.PrimaryYear)
            || (expectedMonth != null && expectedMonth != parsed.PrimaryMonth);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var batch = new ImportBatch
        {
            PlantId = plant.Id,
            FileName = Path.GetFileName(fileName),
            FileHash = hash,
            DetectedYear = parsed.PrimaryYear,
            DetectedMonth = parsed.PrimaryMonth,
            MonthMismatch = mismatch,
            Warnings = warnings.Count == 0 ? null : Trim(string.Join("\n", warnings), 4000),
            RowCount = parsed.Rows.Count,
            ImportedAtUtc = DateTime.UtcNow,
            ImportedBy = importedBy
        };
        db.ImportBatches.Add(batch);
        await db.SaveChangesAsync(ct);

        foreach (var month in parsed.Months)
        {
            var start = new DateTime(month.Year, month.Month, 1, 0, 0, 0, DateTimeKind.Unspecified);
            var end = start.AddMonths(1);
            await db.HourlyProductions
                .Where(h => h.PlantId == plant.Id && h.Source == "excel" && h.HourStartLocal >= start && h.HourStartLocal < end)
                .ExecuteDeleteAsync(ct);
        }

        const int chunk = 2000;
        for (var i = 0; i < parsed.Rows.Count; i += chunk)
        {
            var slice = parsed.Rows.Skip(i).Take(chunk).Select(r => new HourlyProduction
            {
                PlantId = plant.Id,
                Series = r.Series,
                TurbineCode = r.TurbineCode,
                HourStartLocal = DateTime.SpecifyKind(r.HourStartLocal, DateTimeKind.Unspecified),
                ProductionMwh = r.ProductionMwh,
                CounterKwh = r.CounterKwh,
                Source = "excel",
                ImportBatchId = batch.Id
            });
            db.HourlyProductions.AddRange(slice);
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
        }

        await tx.CommitAsync(ct);
        return new ImportFileResult(fileName, true, false, parsed.Rows.Count, parsed.PrimaryYear, parsed.PrimaryMonth, mismatch, warnings, null);
    }

    static string? FilenameHint(string fileName, int? year, int? month)
    {
        if (year == null || month == null)
            return null;
        var m = System.Text.RegularExpressions.Regex.Match(fileName, @"(20\d{2})[-_](\d{1,2})");
        if (!m.Success)
            return null;
        var y = int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        var mo = int.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
        if (y != year || mo != month)
            return $"Dosya adındaki {y}-{mo:00} ile verideki {year}-{month:00} farklı.";
        return null;
    }

    static string Trim(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
