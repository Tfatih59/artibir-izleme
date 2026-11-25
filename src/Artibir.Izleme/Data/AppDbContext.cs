using Artibir.Izleme.Models;
using Microsoft.EntityFrameworkCore;

namespace Artibir.Izleme.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Plant> Plants => Set<Plant>();
    public DbSet<Turbine> Turbines => Set<Turbine>();
    public DbSet<SignalDefinition> Signals => Set<SignalDefinition>();
    public DbSet<IngestBatch> IngestBatches => Set<IngestBatch>();
    public DbSet<ReadingRaw> ReadingRaws => Set<ReadingRaw>();
    public DbSet<ReadingTenMin> ReadingTenMins => Set<ReadingTenMin>();
    public DbSet<LatestReading> LatestReadings => Set<LatestReading>();
    public DbSet<HourlyProduction> HourlyProductions => Set<HourlyProduction>();
    public DbSet<ImportBatch> ImportBatches => Set<ImportBatch>();
    public DbSet<ShiftLogEntry> ShiftLogs => Set<ShiftLogEntry>();
    public DbSet<FaultRecord> Faults => Set<FaultRecord>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<UserRoleAssignment> UserRoles => Set<UserRoleAssignment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Plant>(e =>
        {
            e.ToTable("plants");
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Code).HasMaxLength(32);
            e.Property(x => x.Name).HasMaxLength(160);
            e.Property(x => x.Kind).HasMaxLength(8);
            e.Property(x => x.Location).HasMaxLength(160);
            e.Property(x => x.Technology).HasMaxLength(200);
            e.Property(x => x.OpcPrefix).HasMaxLength(64);
        });

        modelBuilder.Entity<Turbine>(e =>
        {
            e.ToTable("turbines");
            e.HasIndex(x => new { x.PlantId, x.Code }).IsUnique();
            e.Property(x => x.Code).HasMaxLength(16);
            e.Property(x => x.WtgCode).HasMaxLength(16);
            e.HasOne(x => x.Plant).WithMany(x => x.Turbines).HasForeignKey(x => x.PlantId);
        });

        modelBuilder.Entity<SignalDefinition>(e =>
        {
            e.ToTable("signals");
            e.HasIndex(x => new { x.PlantId, x.ItemId }).IsUnique();
            e.Property(x => x.ItemId).HasMaxLength(240);
            e.Property(x => x.GroupName).HasMaxLength(160);
            e.Property(x => x.SignalName).HasMaxLength(120);
            e.Property(x => x.Unit).HasMaxLength(32);
        });

        modelBuilder.Entity<IngestBatch>(e =>
        {
            e.ToTable("ingest_batches");
            e.HasIndex(x => new { x.PlantId, x.ContentHash }).IsUnique();
            e.HasIndex(x => new { x.PlantId, x.ReaderLocalTime });
            e.Property(x => x.ContentHash).HasMaxLength(64);
            e.Property(x => x.Server).HasMaxLength(160);
            e.Property(x => x.Status).HasMaxLength(64);
            e.Property(x => x.Error).HasMaxLength(500);
            e.Property(x => x.ReadMethod).HasMaxLength(80);
            e.Property(x => x.ReaderVersion).HasMaxLength(32);
        });

        modelBuilder.Entity<ReadingRaw>(e =>
        {
            e.ToTable("reading_raws");
            e.HasIndex(x => new { x.PlantId, x.SignalId, x.CapturedAtLocal });
            e.HasIndex(x => x.CapturedAtLocal);
            e.Property(x => x.TextValue).HasMaxLength(200);
        });

        modelBuilder.Entity<ReadingTenMin>(e =>
        {
            e.ToTable("reading_ten_mins");
            e.HasIndex(x => new { x.PlantId, x.SignalId, x.BucketStartLocal }).IsUnique();
        });

        modelBuilder.Entity<LatestReading>(e =>
        {
            e.ToTable("latest_readings");
            e.HasKey(x => x.SignalId);
            e.HasIndex(x => x.PlantId);
            e.Property(x => x.TextValue).HasMaxLength(200);
        });

        modelBuilder.Entity<HourlyProduction>(e =>
        {
            e.ToTable("hourly_productions");
            e.HasIndex(x => new { x.PlantId, x.Series, x.TurbineCode, x.HourStartLocal }).IsUnique();
            e.HasIndex(x => new { x.PlantId, x.HourStartLocal });
            e.Property(x => x.Series).HasMaxLength(32);
            e.Property(x => x.TurbineCode).HasMaxLength(16);
            e.Property(x => x.Source).HasMaxLength(16);
        });

        modelBuilder.Entity<ImportBatch>(e =>
        {
            e.ToTable("import_batches");
            e.HasIndex(x => new { x.PlantId, x.FileHash });
            e.Property(x => x.FileName).HasMaxLength(260);
            e.Property(x => x.FileHash).HasMaxLength(64);
            e.Property(x => x.ImportedBy).HasMaxLength(200);
            e.Property(x => x.Warnings).HasMaxLength(4000);
        });

        modelBuilder.Entity<ShiftLogEntry>(e =>
        {
            e.ToTable("shift_logs");
            e.HasIndex(x => new { x.PlantId, x.OccurredAtLocal });
            e.Property(x => x.TurbineCode).HasMaxLength(32);
            e.Property(x => x.Description).HasMaxLength(2000);
            e.Property(x => x.AuthorName).HasMaxLength(160);
            e.Property(x => x.Status).HasMaxLength(32);
            e.Property(x => x.Source).HasMaxLength(16);
            e.Property(x => x.CreatedBy).HasMaxLength(200);
        });

        modelBuilder.Entity<FaultRecord>(e =>
        {
            e.ToTable("faults");
            e.HasIndex(x => new { x.PlantId, x.OccurredAtLocal });
            e.Property(x => x.TurbineCode).HasMaxLength(32);
            e.Property(x => x.Description).HasMaxLength(2000);
            e.Property(x => x.Responsible).HasMaxLength(160);
            e.Property(x => x.Status).HasMaxLength(32);
            e.Property(x => x.Source).HasMaxLength(16);
            e.Property(x => x.CreatedBy).HasMaxLength(200);
        });

        modelBuilder.Entity<AppUser>(e =>
        {
            e.ToTable("app_users");
            e.HasIndex(x => x.ExternalId).IsUnique();
            e.Property(x => x.ExternalId).HasMaxLength(80);
            e.Property(x => x.Email).HasMaxLength(200);
            e.Property(x => x.DisplayName).HasMaxLength(200);
        });

        modelBuilder.Entity<UserRoleAssignment>(e =>
        {
            e.ToTable("user_roles");
            e.HasIndex(x => new { x.UserId, x.Role, x.PlantCode }).IsUnique();
            e.Property(x => x.Role).HasMaxLength(32);
            e.Property(x => x.PlantCode).HasMaxLength(32);
            e.HasOne(x => x.User).WithMany(x => x.Roles).HasForeignKey(x => x.UserId);
        });
    }
}
