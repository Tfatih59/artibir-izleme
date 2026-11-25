namespace Artibir.Izleme.Models;

public class Plant
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "RES";
    public string? Location { get; set; }
    public string? Technology { get; set; }
    public int? TurbineCount { get; set; }
    public bool SampleOnly { get; set; }
    public bool HasLiveFeed { get; set; }
    public string? OpcPrefix { get; set; }
    public List<Turbine> Turbines { get; set; } = new();
}

public class Turbine
{
    public int Id { get; set; }
    public int PlantId { get; set; }
    public Plant? Plant { get; set; }
    public string Code { get; set; } = "";
    public string WtgCode { get; set; } = "";
    public int SortOrder { get; set; }
}

public class SignalDefinition
{
    public int Id { get; set; }
    public int PlantId { get; set; }
    public int? TurbineId { get; set; }
    public string ItemId { get; set; } = "";
    public string GroupName { get; set; } = "";
    public string SignalName { get; set; } = "";
    public string? Unit { get; set; }
}

public class IngestBatch
{
    public long Id { get; set; }
    public int PlantId { get; set; }
    public DateTime ReaderLocalTime { get; set; }
    public int Cycle { get; set; }
    public string? Server { get; set; }
    public string? Status { get; set; }
    public string? Error { get; set; }
    public string? ReadMethod { get; set; }
    public string? ReaderVersion { get; set; }
    public int ReadCount { get; set; }
    public int GoodCount { get; set; }
    public int StoredCount { get; set; }
    public string ContentHash { get; set; } = "";
    public bool ClockFallback { get; set; }
    public DateTime ReceivedAtUtc { get; set; }
}

public class ReadingRaw
{
    public long Id { get; set; }
    public long BatchId { get; set; }
    public int PlantId { get; set; }
    public int SignalId { get; set; }
    public int? TurbineId { get; set; }
    public DateTime CapturedAtLocal { get; set; }
    public DateTime? SourceTimestampLocal { get; set; }
    public double? NumericValue { get; set; }
    public string? TextValue { get; set; }
    public int QualityRaw { get; set; }
    public bool IsGood { get; set; }
}

public class ReadingTenMin
{
    public long Id { get; set; }
    public int PlantId { get; set; }
    public int SignalId { get; set; }
    public int? TurbineId { get; set; }
    public DateTime BucketStartLocal { get; set; }
    public double? AvgValue { get; set; }
    public double? MinValue { get; set; }
    public double? MaxValue { get; set; }
    public int SampleCount { get; set; }
    public int GoodCount { get; set; }
    public int RunningSamples { get; set; }
    public int MaintenanceSamples { get; set; }
}

public class LatestReading
{
    public int SignalId { get; set; }
    public int PlantId { get; set; }
    public int? TurbineId { get; set; }
    public double? NumericValue { get; set; }
    public string? TextValue { get; set; }
    public int QualityRaw { get; set; }
    public bool IsGood { get; set; }
    public DateTime CapturedAtLocal { get; set; }
    public DateTime? SourceTimestampLocal { get; set; }
    public long BatchId { get; set; }
}

public class HourlyProduction
{
    public long Id { get; set; }
    public int PlantId { get; set; }
    public string Series { get; set; } = "";
    public string TurbineCode { get; set; } = "";
    public DateTime HourStartLocal { get; set; }
    public double? ProductionMwh { get; set; }
    public double? CounterKwh { get; set; }
    public string Source { get; set; } = "excel";
    public long? ImportBatchId { get; set; }
}

public class ImportBatch
{
    public long Id { get; set; }
    public int PlantId { get; set; }
    public string FileName { get; set; } = "";
    public string FileHash { get; set; } = "";
    public int? DetectedYear { get; set; }
    public int? DetectedMonth { get; set; }
    public bool MonthMismatch { get; set; }
    public string? Warnings { get; set; }
    public int RowCount { get; set; }
    public DateTime ImportedAtUtc { get; set; }
    public string? ImportedBy { get; set; }
}

public class ShiftLogEntry
{
    public long Id { get; set; }
    public int PlantId { get; set; }
    public string? TurbineCode { get; set; }
    public DateTime OccurredAtLocal { get; set; }
    public string Description { get; set; } = "";
    public string? AuthorName { get; set; }
    public string Status { get; set; } = "Açık";
    public string Source { get; set; } = "manuel";
    public DateTime CreatedAtUtc { get; set; }
    public string? CreatedBy { get; set; }
}

public class FaultRecord
{
    public long Id { get; set; }
    public int PlantId { get; set; }
    public string? TurbineCode { get; set; }
    public DateTime OccurredAtLocal { get; set; }
    public string Description { get; set; } = "";
    public string? Responsible { get; set; }
    public string Status { get; set; } = "Açık";
    public string Source { get; set; } = "manuel";
    public DateTime CreatedAtUtc { get; set; }
    public string? CreatedBy { get; set; }
}

public class AppUser
{
    public int Id { get; set; }
    public string ExternalId { get; set; } = "";
    public string? Email { get; set; }
    public string DisplayName { get; set; } = "";
    public DateTime LastSeenUtc { get; set; }
    public List<UserRoleAssignment> Roles { get; set; } = new();
}

public class UserRoleAssignment
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public AppUser? User { get; set; }
    public string Role { get; set; } = "";
    public string? PlantCode { get; set; }
}
