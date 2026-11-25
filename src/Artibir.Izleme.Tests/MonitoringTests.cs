using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Xunit;
using Artibir.Izleme.Api;
using Artibir.Izleme.Auth;
using Artibir.Izleme.Data;
using Artibir.Izleme.Excel;
using Artibir.Izleme.Ingest;
using Artibir.Izleme.Options;
using ClosedXML.Excel;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Artibir.Izleme.Tests;

public class AccessTests
{
    [Fact]
    public void ExecutiveSeesEveryPlant()
    {
        var id = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, "Executive") }, "test");
        var user = new ClaimsPrincipal(id);
        Assert.True(AccessEvaluator.CanSeePlant(user, "gop"));
        Assert.True(AccessEvaluator.CanSeePlant(user, "kucukkoy"));
        Assert.True(AccessEvaluator.IsExecutive(user));
    }

    [Fact]
    public void PlantGroupIsLimited()
    {
        var id = new ClaimsIdentity(new[] { new Claim(AccessEvaluator.PlantClaim, "gop") }, "test");
        var user = new ClaimsPrincipal(id);
        Assert.True(AccessEvaluator.CanSeePlant(user, "GOP"));
        Assert.False(AccessEvaluator.CanSeePlant(user, "yenikoy"));
        Assert.False(AccessEvaluator.IsExecutive(user));
    }

    [Fact]
    public void GroupsMapToClaims()
    {
        var id = new ClaimsIdentity("test");
        var map = new AuthorizationMapOptions
        {
            ExecutiveGroupId = "exec-guid",
            PlantGroupIds = new Dictionary<string, string> { ["gop"] = "gop-guid", ["yilmaz"] = "" }
        };
        AccessEvaluator.MapGroups(id, new[] { "gop-guid", "other" }, map);
        var user = new ClaimsPrincipal(id);
        Assert.False(AccessEvaluator.IsExecutive(user));
        Assert.True(AccessEvaluator.CanSeePlant(user, "gop"));
        Assert.False(AccessEvaluator.CanSeePlant(user, "yilmaz"));
    }

    [Fact]
    public void KeyMatchIsExact()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ingest:Keys:gop"] = "secret-gop",
                ["Ingest:Keys:yilmaz"] = ""
            })
            .Build();
        Assert.Equal("gop", IngestKeyMatcher.MatchPlantCode(config, "secret-gop"));
        Assert.Null(IngestKeyMatcher.MatchPlantCode(config, "secret-gop "));
        Assert.Null(IngestKeyMatcher.MatchPlantCode(config, "nope"));
        Assert.True(IngestKeyMatcher.AnyConfigured(config));
    }
}

public class WorkbookTests
{
    [Fact]
    public void RealMonthlyFile_ParsesJanuary2025()
    {
        var path = FindUp("data/ornek/GOP-aylik-ornek.xlsx");
        using var stream = File.OpenRead(path);
        var parsed = TurbineWorkbookParser.Parse(stream, 2025, 1);
        Assert.True(parsed.Success, parsed.Error);
        Assert.Equal(2025, parsed.PrimaryYear);
        Assert.Equal(1, parsed.PrimaryMonth);
        Assert.False(parsed.MultiMonth);
        Assert.DoesNotContain(parsed.Warnings, w => w.Contains("uyuşmazlığı", StringComparison.Ordinal));

        var t1 = parsed.Rows.Where(r => r.Series == ProductionSeries.Turbine && r.TurbineCode == "T01").OrderBy(r => r.HourStartLocal).ToList();
        Assert.Equal(744, t1.Count);
        Assert.Equal(new DateTime(2025, 1, 1, 0, 0, 0), t1[0].HourStartLocal);
        Assert.Equal(new DateTime(2025, 1, 31, 23, 0, 0), t1[^1].HourStartLocal);
        Assert.InRange(t1[0].ProductionMwh!.Value, 0.959, 0.961);
        Assert.Equal(66077997d, t1[0].CounterKwh);

        var meter = parsed.Rows.Single(r => r.Series == ProductionSeries.Meter && r.HourStartLocal == new DateTime(2025, 1, 1));
        Assert.InRange(meter.ProductionMwh!.Value, 15.8, 16.0);
        var total = parsed.Rows.Single(r => r.Series == ProductionSeries.TurbineTotal && r.HourStartLocal == new DateTime(2025, 1, 1));
        Assert.InRange(total.ProductionMwh!.Value, 16.1, 16.3);
    }

    [Fact]
    public void ComputesFarkWhenCacheMissing_AndFlagsMonths()
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("TÜRBİN DATA");
        ws.Cell(1, 1).Value = "GÜN";
        ws.Cell(1, 2).Value = "SAAT";
        ws.Cell(1, 3).Value = "T 1";
        ws.Cell(1, 4).Value = "FARK\nMWh";
        ws.Cell(2, 2).Value = new DateTime(1900, 1, 1, 0, 0, 0);
        ws.Cell(2, 3).Value = 1000d;
        ws.Cell(3, 1).Value = new DateTime(2024, 5, 1);
        ws.Cell(3, 2).Value = new DateTime(1900, 1, 1, 1, 0, 0);
        ws.Cell(3, 3).Value = 2500d;
        ws.Cell(4, 2).Value = " 05:00";
        ws.Cell(4, 3).Value = 4000d;
        ws.Cell(5, 1).Value = new DateTime(2024, 6, 2);
        ws.Cell(5, 2).Value = new DateTime(1900, 1, 1, 1, 0, 0);
        ws.Cell(5, 3).Value = 5000d;

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        ms.Position = 0;
        var parsed = TurbineWorkbookParser.Parse(ms, 2024, 5);
        Assert.True(parsed.Success, parsed.Error);
        Assert.True(parsed.MultiMonth);
        Assert.Contains(parsed.Warnings, w => w.Contains("uyuşmazlığı", StringComparison.Ordinal));
        var first = parsed.Rows.Single(r => r.TurbineCode == "T01" && r.HourStartLocal == new DateTime(2024, 5, 1, 0, 0, 0));
        Assert.Equal(1.5, first.ProductionMwh);
        var fifth = parsed.Rows.Single(r => r.TurbineCode == "T01" && r.HourStartLocal == new DateTime(2024, 5, 1, 4, 0, 0));
        Assert.Equal(1.5, fifth.ProductionMwh);
    }

    static string FindUp(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException(relative);
    }
}

public class IngestAndImportTests : IAsyncLifetime
{
    SqliteConnection _conn = null!;
    AppDbContext _db = null!;

    public async Task InitializeAsync()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        await _conn.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_conn)
            .UseSnakeCaseNamingConvention()
            .Options;
        _db = new AppDbContext(options);
        await _db.Database.EnsureCreatedAsync();
        await DbSeeder.SeedAsync(_db);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _conn.DisposeAsync();
    }

    [Fact]
    public async Task Ingest_IsIdempotent_AndKeepsLatest()
    {
        var plant = await _db.Plants.SingleAsync(p => p.Code == "gop");
        var svc = new IngestService(_db);
        var body = Encoding.UTF8.GetBytes("""
        {"surum":"0.3.0.0","durum":"okundu","zaman":"2026-11-06 15:09:17","dongu":15,"okunanAdet":2,"goodAdet":2,"okumaYontemi":"IOPCItemIO_batch","sunucu":"Vestas.VOBOPCServerDA.3","okunanlar":[
          {"itemId":"OZYEL.WTG07.System.OperationStateInt","deger":"2","kalite":"Good (0x00C0)","kaliteHam":192,"zaman":"2026-11-06 15:09:00"},
          {"itemId":"OZYEL.POM01.Electrical.ActivePower","deger":"7945,5","kalite":"Good (0x00C0)","kaliteHam":192,"zaman":"2026-11-06 15:06:15"},
          {"itemId":"OZYEL.WTG01.Grid.Power","deger":"762.5","kalite":"Bad","kaliteHam":0,"zaman":"2026-11-06 15:09:00"}
        ]}
        """);

        var first = await svc.AcceptAsync(plant, body, CancellationToken.None);
        var second = await svc.AcceptAsync(plant, body, CancellationToken.None);
        Assert.Equal(IngestStatus.Stored, first.Status);
        Assert.Equal(3, first.Readings);
        Assert.Equal(IngestStatus.Duplicate, second.Status);
        Assert.Equal(first.BatchId, second.BatchId);
        Assert.Equal(1, await _db.IngestBatches.CountAsync());
        Assert.Equal(3, await _db.ReadingRaws.CountAsync());

        var pom = await _db.LatestReadings.Join(_db.Signals, l => l.SignalId, s => s.Id, (l, s) => new { l, s })
            .SingleAsync(x => x.s.ItemId.EndsWith("ActivePower"));
        Assert.Equal(7945.5, pom.l.NumericValue);
        Assert.True(pom.l.IsGood);

        var bad = await _db.ReadingRaws.Join(_db.Signals, r => r.SignalId, s => s.Id, (r, s) => new { r, s })
            .SingleAsync(x => x.s.ItemId.EndsWith("Grid.Power"));
        Assert.False(bad.r.IsGood);

        var newer = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(body).Replace("7945,5", "1000").Replace("\"dongu\":15", "\"dongu\":16").Replace("15:09:17", "15:10:17"));
        var third = await svc.AcceptAsync(plant, newer, CancellationToken.None);
        Assert.Equal(IngestStatus.Stored, third.Status);
        var pom2 = await _db.LatestReadings.Join(_db.Signals, l => l.SignalId, s => s.Id, (l, s) => new { l, s })
            .SingleAsync(x => x.s.ItemId.EndsWith("ActivePower"));
        Assert.Equal(1000, pom2.l.NumericValue);

        var older = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(body).Replace("7945,5", "1").Replace("\"dongu\":15", "\"dongu\":1").Replace("15:09:17", "15:00:17"));
        await svc.AcceptAsync(plant, older, CancellationToken.None);
        var pom3 = await _db.LatestReadings.AsNoTracking().Join(_db.Signals, l => l.SignalId, s => s.Id, (l, s) => new { l, s })
            .SingleAsync(x => x.s.ItemId.EndsWith("ActivePower"));
        Assert.Equal(1000, pom3.l.NumericValue);
    }

    [Fact]
    public async Task SampleSnapshot_IngestsOnce()
    {
        var path = FindUp("data/ornek/okuma-2026-11-06-1509.json");
        var body = await File.ReadAllBytesAsync(path);
        using var doc = JsonDocument.Parse(body);
        Assert.Equal(1006, doc.RootElement.GetProperty("okunanlar").GetArrayLength());

        var plant = await _db.Plants.SingleAsync(p => p.Code == "gop");
        var svc = new IngestService(_db);
        var first = await svc.AcceptAsync(plant, body, CancellationToken.None);
        var second = await svc.AcceptAsync(plant, body, CancellationToken.None);
        Assert.Equal(IngestStatus.Stored, first.Status);
        Assert.Equal(1006, first.Readings);
        Assert.Equal(IngestStatus.Duplicate, second.Status);
        Assert.Equal(1006, await _db.ReadingRaws.CountAsync());

        var state = await _db.LatestReadings.Join(_db.Signals, l => l.SignalId, s => s.Id, (l, s) => new { l, s })
            .SingleAsync(x => x.s.ItemId == "OZYEL.WTG07.System.OperationStateInt");
        Assert.Equal(2, state.l.NumericValue);
    }

    [Fact]
    public async Task ExcelImport_ReplacesMonth_AndSkipsSameFile()
    {
        var plant = await _db.Plants.SingleAsync(p => p.Code == "gop");
        var bytes = BuildTiny();
        var svc = new ExcelImportService(_db);
        var first = await svc.ImportAsync(plant, "gop-2024-05.xlsx", bytes, 2024, 5, "test", CancellationToken.None);
        Assert.True(first.Imported, first.Error);
        Assert.False(first.Mismatch);
        var again = await svc.ImportAsync(plant, "gop-2024-05.xlsx", bytes, null, null, "test", CancellationToken.None);
        Assert.True(again.SkippedDuplicate);
        var hours = await _db.HourlyProductions.CountAsync();
        Assert.Equal(first.Rows, hours);

        var edited = BuildTiny(counter: 5000);
        var third = await svc.ImportAsync(plant, "gop-2024-05-duzeltme.xlsx", edited, 2024, 6, "test", CancellationToken.None);
        Assert.True(third.Imported, third.Error);
        Assert.True(third.Mismatch);
        var row = await _db.HourlyProductions.SingleAsync(h => h.TurbineCode == "T01");
        Assert.Equal(4, row.ProductionMwh);
        Assert.Equal(await _db.HourlyProductions.CountAsync(), hours);
    }

    [Fact]
    public void Ranges_CoverWeekMonthAndYears()
    {
        Assert.True(WebApi.TryRange("week", null, null, out var start, out var end, out var error), error);
        Assert.Equal(7, (end - start).TotalDays);
        Assert.True(WebApi.TryRange("years", null, null, out start, out end, out error), error);
        Assert.True(end.Year - start.Year >= 8);
        Assert.True(WebApi.TryRange("month", "2025-01-01", "2025-02-01", out start, out end, out error), error);
        Assert.Equal(new DateTime(2025, 1, 1), start);
        Assert.Equal(new DateTime(2025, 2, 2), end);
    }

    static byte[] BuildTiny(double counter = 2500)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("TÜRBİN DATA");
        ws.Cell(1, 1).Value = "GÜN";
        ws.Cell(1, 2).Value = "SAAT";
        ws.Cell(1, 3).Value = "T 1";
        ws.Cell(1, 4).Value = "FARK\nMWh";
        ws.Cell(1, 5).Value = "SAYAÇ \nMWh";
        ws.Cell(2, 2).Value = new DateTime(1900, 1, 1);
        ws.Cell(2, 3).Value = 1000d;
        ws.Cell(3, 1).Value = new DateTime(2024, 5, 1);
        ws.Cell(3, 2).Value = new DateTime(1900, 1, 1, 1, 0, 0);
        ws.Cell(3, 3).Value = counter;
        ws.Cell(3, 5).Value = 1.2d;
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    static string FindUp(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException(relative);
    }
}
