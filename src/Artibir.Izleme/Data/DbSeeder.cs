using Artibir.Izleme.Models;
using Microsoft.EntityFrameworkCore;

namespace Artibir.Izleme.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(AppDbContext db, CancellationToken ct = default)
    {
        if (!await db.Plants.AnyAsync(ct))
        {
            db.Plants.AddRange(
                new Plant
                {
                    Code = "gop",
                    Name = "Gaziosmanpaşa RES",
                    Kind = "RES",
                    Technology = "Vestas V136 · 15 türbin",
                    TurbineCount = 15,
                    SampleOnly = false,
                    HasLiveFeed = true,
                    OpcPrefix = "OZYEL"
                },
                new Plant
                {
                    Code = "yilmaz",
                    Name = "Yılmaz RES",
                    Kind = "RES",
                    Technology = "Rüzgâr · canlı ölçüm bağlı değil",
                    SampleOnly = true,
                    HasLiveFeed = false
                },
                new Plant
                {
                    Code = "yenikoy",
                    Name = "Yeniköy RES",
                    Kind = "RES",
                    Location = "Çanakkale",
                    Technology = "Siemens Gamesa",
                    SampleOnly = true,
                    HasLiveFeed = false
                },
                new Plant
                {
                    Code = "kucukkoy",
                    Name = "Küçükköy GES",
                    Kind = "GES",
                    Technology = "Güneş · canlı ölçüm bağlı değil",
                    SampleOnly = true,
                    HasLiveFeed = false
                });
            await db.SaveChangesAsync(ct);
        }

        var gop = await db.Plants.SingleAsync(p => p.Code == "gop", ct);
        if (!await db.Turbines.AnyAsync(t => t.PlantId == gop.Id, ct))
        {
            for (var i = 1; i <= 15; i++)
            {
                db.Turbines.Add(new Turbine
                {
                    PlantId = gop.Id,
                    Code = "T" + i.ToString("00"),
                    WtgCode = "WTG" + i.ToString("00"),
                    SortOrder = i
                });
            }
            await db.SaveChangesAsync(ct);
        }
    }
}
