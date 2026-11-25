using System.Security.Cryptography;
using System.Text.Json;
using Artibir.Izleme.Data;
using Artibir.Izleme.Models;
using Artibir.Izleme.Time;
using Microsoft.EntityFrameworkCore;

namespace Artibir.Izleme.Ingest;

public class IngestService(AppDbContext db)
{
    public const int MaxItems = 20000;
    static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public async Task<IngestOutcome> AcceptAsync(Plant plant, byte[] body, CancellationToken ct)
    {
        if (body.Length == 0)
            return new IngestOutcome(IngestStatus.BadRequest, null, 0, "Boş gövde.");

        ReaderPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<ReaderPayload>(body, JsonOpts);
        }
        catch (JsonException ex)
        {
            return new IngestOutcome(IngestStatus.BadRequest, null, 0, "JSON okunamadı: " + ex.Message);
        }
        if (payload == null)
            return new IngestOutcome(IngestStatus.BadRequest, null, 0, "JSON boş.");

        var items = payload.Okunanlar ?? new List<ReaderItem>();
        if (items.Count > MaxItems)
            return new IngestOutcome(IngestStatus.BadRequest, null, 0, "Etiket sayısı sınırı aşıldı.");

        var hash = Convert.ToHexString(SHA256.HashData(body)).ToLowerInvariant();
        var existingId = await db.IngestBatches.AsNoTracking()
            .Where(b => b.PlantId == plant.Id && b.ContentHash == hash)
            .Select(b => (long?)b.Id)
            .FirstOrDefaultAsync(ct);
        if (existingId != null)
            return new IngestOutcome(IngestStatus.Duplicate, existingId, 0, "Aynı okuma daha önce alınmış.");

        var clockFallback = false;
        var captured = TagParser.ParseLocalTimestamp(payload.Zaman);
        if (captured == null)
        {
            captured = TurkeyTime.NowUnspecified();
            clockFallback = true;
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var turbines = await db.Turbines.AsNoTracking()
            .Where(t => t.PlantId == plant.Id)
            .ToDictionaryAsync(t => t.Code, t => t.Id, StringComparer.OrdinalIgnoreCase, ct);

        var signals = await db.Signals
            .Where(s => s.PlantId == plant.Id)
            .ToDictionaryAsync(s => s.ItemId, StringComparer.OrdinalIgnoreCase, ct);

        var prepared = new List<Prepared>(items.Count);
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.ItemId))
                continue;
            var itemId = item.ItemId.Trim();
            if (!signals.TryGetValue(itemId, out var signal))
            {
                var parts = TagParser.Parse(itemId);
                int? turbineId = null;
                var tcode = TagParser.TurbineCodeFromWtg(parts.WtgCode);
                if (tcode != null && turbines.TryGetValue(tcode, out var tid))
                    turbineId = tid;
                signal = new SignalDefinition
                {
                    PlantId = plant.Id,
                    TurbineId = turbineId,
                    ItemId = itemId,
                    GroupName = parts.GroupName,
                    SignalName = parts.SignalName,
                    Unit = parts.UnitHint
                };
                db.Signals.Add(signal);
                signals[itemId] = signal;
            }
            prepared.Add(new Prepared(item, signal));
        }

        if (db.ChangeTracker.Entries<SignalDefinition>().Any(e => e.State == EntityState.Added))
            await db.SaveChangesAsync(ct);

        var batch = new IngestBatch
        {
            PlantId = plant.Id,
            ReaderLocalTime = captured.Value,
            Cycle = payload.Dongu,
            Server = Trim(payload.Sunucu, 160),
            Status = Trim(payload.Durum, 64),
            Error = Trim(string.IsNullOrWhiteSpace(payload.Hata) ? null : payload.Hata, 500),
            ReadMethod = Trim(payload.OkumaYontemi, 80),
            ReaderVersion = Trim(payload.Surum, 32),
            ReadCount = payload.OkunanAdet > 0 ? payload.OkunanAdet : prepared.Count,
            GoodCount = payload.GoodAdet,
            StoredCount = prepared.Count,
            ContentHash = hash,
            ClockFallback = clockFallback,
            ReceivedAtUtc = DateTime.UtcNow
        };
        db.IngestBatches.Add(batch);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            await tx.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            var again = await db.IngestBatches.AsNoTracking()
                .Where(b => b.PlantId == plant.Id && b.ContentHash == hash)
                .Select(b => (long?)b.Id)
                .FirstOrDefaultAsync(ct);
            if (again != null)
                return new IngestOutcome(IngestStatus.Duplicate, again, 0, "Aynı okuma daha önce alınmış.");
            throw;
        }

        var signalIds = prepared.Select(p => p.Signal.Id).Distinct().ToList();
        var latest = await db.LatestReadings
            .Where(l => signalIds.Contains(l.SignalId))
            .ToDictionaryAsync(l => l.SignalId, ct);

        foreach (var row in prepared)
        {
            var item = row.Item;
            var signal = row.Signal;
            var good = TagParser.IsGood(item.Kalite, item.KaliteHam);
            double? number = null;
            string? text = null;
            if (TagParser.TryParseNumber(item.Deger, out var n))
                number = n;
            else if (!string.IsNullOrWhiteSpace(item.Deger))
                text = Trim(item.Deger.Trim(), 200);

            var sourceTs = TagParser.ParseLocalTimestamp(item.Zaman);
            db.ReadingRaws.Add(new ReadingRaw
            {
                BatchId = batch.Id,
                PlantId = plant.Id,
                SignalId = signal.Id,
                TurbineId = signal.TurbineId,
                CapturedAtLocal = captured.Value,
                SourceTimestampLocal = sourceTs,
                NumericValue = number,
                TextValue = text,
                QualityRaw = item.KaliteHam,
                IsGood = good
            });

            if (!latest.TryGetValue(signal.Id, out var current))
            {
                current = new LatestReading { SignalId = signal.Id, PlantId = plant.Id };
                db.LatestReadings.Add(current);
                latest[signal.Id] = current;
            }
            else if (current.CapturedAtLocal > captured.Value)
            {
                continue;
            }

            current.PlantId = plant.Id;
            current.TurbineId = signal.TurbineId;
            current.NumericValue = number;
            current.TextValue = text;
            current.QualityRaw = item.KaliteHam;
            current.IsGood = good;
            current.CapturedAtLocal = captured.Value;
            current.SourceTimestampLocal = sourceTs;
            current.BatchId = batch.Id;
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        db.ChangeTracker.Clear();
        var note = clockFallback ? "Okuyucu zamanı yok; sunucu saati (TR) kullanıldı." : null;
        return new IngestOutcome(IngestStatus.Stored, batch.Id, prepared.Count, note);
    }

    static string? Trim(string? value, int max)
    {
        if (string.IsNullOrEmpty(value))
            return value;
        return value.Length <= max ? value : value[..max];
    }

    readonly record struct Prepared(ReaderItem Item, SignalDefinition Signal);
}
