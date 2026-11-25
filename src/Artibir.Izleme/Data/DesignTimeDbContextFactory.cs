using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Artibir.Izleme.Data;

public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    static DesignTimeDbContextFactory()
    {
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
    }

    public AppDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
        if (string.IsNullOrWhiteSpace(cs))
            cs = "Host=localhost;Database=artibir;Username=artibir;Password=artibir";
        var options = new DbContextOptionsBuilder<AppDbContext>();
        options.UseNpgsql(cs).UseSnakeCaseNamingConvention();
        return new AppDbContext(options.Options);
    }
}
