using Artibir.Izleme.Data;
using Artibir.Izleme.Options;
using Artibir.Izleme.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Artibir.Izleme.Ingest;

/// <summary>
/// 7 günden eski 10 sn ham okumaları 10 dakikalık ortalamaya katlar ve ham satırları siler.
/// </summary>
public class ReadingRollupService(
    IServiceScopeFactory scopes,
    IOptions<RetentionOptions> options,
    ILogger<ReadingRollupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(45), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RollOnce(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "10 dakikalık özet işi başarısız.");
            }

            var minutes = Math.Clamp(options.Value.RollupIntervalMinutes, 1, 120);
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(minutes), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    public async Task<int> RollOnce(CancellationToken ct)
    {
        var days = Math.Clamp(options.Value.RawDays, 1, 60);
        var cutoff = TurkeyTime.NowUnspecified().AddDays(-days);
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var pending = await db.ReadingRaws.AsNoTracking().AnyAsync(r => r.CapturedAtLocal < cutoff, ct);
        if (!pending)
            return 0;

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO reading_ten_mins
                (plant_id, signal_id, turbine_id, bucket_start_local, avg_value, min_value, max_value,
                 sample_count, good_count, running_samples, maintenance_samples)
            SELECT plant_id,
                   signal_id,
                   MAX(turbine_id),
                   date_trunc('hour', captured_at_local)
                       + (floor(EXTRACT(minute FROM captured_at_local) / 10) * INTERVAL '10 minutes'),
                   AVG(numeric_value) FILTER (WHERE is_good AND numeric_value IS NOT NULL),
                   MIN(numeric_value) FILTER (WHERE is_good AND numeric_value IS NOT NULL),
                   MAX(numeric_value) FILTER (WHERE is_good AND numeric_value IS NOT NULL),
                   COUNT(*)::int,
                   COUNT(*) FILTER (WHERE is_good)::int,
                   COUNT(*) FILTER (WHERE is_good AND numeric_value = 3)::int,
                   COUNT(*) FILTER (WHERE is_good AND numeric_value = 2)::int
            FROM reading_raws
            WHERE captured_at_local < {cutoff}
            GROUP BY plant_id, signal_id,
                     date_trunc('hour', captured_at_local)
                       + (floor(EXTRACT(minute FROM captured_at_local) / 10) * INTERVAL '10 minutes')
            ON CONFLICT (plant_id, signal_id, bucket_start_local)
            DO UPDATE SET
                turbine_id = COALESCE(EXCLUDED.turbine_id, reading_ten_mins.turbine_id),
                avg_value = CASE
                    WHEN reading_ten_mins.good_count + EXCLUDED.good_count = 0 THEN NULL
                    ELSE (
                        COALESCE(reading_ten_mins.avg_value, 0) * reading_ten_mins.good_count
                        + COALESCE(EXCLUDED.avg_value, 0) * EXCLUDED.good_count
                    ) / (reading_ten_mins.good_count + EXCLUDED.good_count)
                END,
                min_value = CASE
                    WHEN reading_ten_mins.min_value IS NULL THEN EXCLUDED.min_value
                    WHEN EXCLUDED.min_value IS NULL THEN reading_ten_mins.min_value
                    ELSE LEAST(reading_ten_mins.min_value, EXCLUDED.min_value)
                END,
                max_value = CASE
                    WHEN reading_ten_mins.max_value IS NULL THEN EXCLUDED.max_value
                    WHEN EXCLUDED.max_value IS NULL THEN reading_ten_mins.max_value
                    ELSE GREATEST(reading_ten_mins.max_value, EXCLUDED.max_value)
                END,
                sample_count = reading_ten_mins.sample_count + EXCLUDED.sample_count,
                good_count = reading_ten_mins.good_count + EXCLUDED.good_count,
                running_samples = reading_ten_mins.running_samples + EXCLUDED.running_samples,
                maintenance_samples = reading_ten_mins.maintenance_samples + EXCLUDED.maintenance_samples
            """, ct);

        var deleted = 0;
        while (!ct.IsCancellationRequested)
        {
            var n = await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                DELETE FROM reading_raws
                WHERE id IN (
                    SELECT id FROM reading_raws
                    WHERE captured_at_local < {cutoff}
                    LIMIT 20000
                )
                """, ct);
            deleted += n;
            if (n == 0)
                break;
        }

        await tx.CommitAsync(ct);
        if (deleted > 0)
            logger.LogInformation("{Count} ham okuma 10 dk özete alındı ve silindi (eşik {Cutoff:yyyy-MM-dd HH:mm}).", deleted, cutoff);
        return deleted;
    }
}
