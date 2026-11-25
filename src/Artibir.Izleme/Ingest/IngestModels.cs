using System.Text.Json.Serialization;

namespace Artibir.Izleme.Ingest;

public sealed class ReaderPayload
{
    public string? Surum { get; set; }
    public string? Durum { get; set; }
    public string? Hata { get; set; }
    public string? Sunucu { get; set; }
    public string? EtiketDosyasi { get; set; }
    public int Dongu { get; set; }
    public string? SonBasariliZaman { get; set; }
    public string? PostUrl { get; set; }
    public string? TaramaYontemi { get; set; }
    public string? OkumaYontemi { get; set; }
    public string? Zaman { get; set; }
    public string? Filtre { get; set; }
    public int OkunanAdet { get; set; }
    public int GoodAdet { get; set; }
    public List<ReaderItem>? Okunanlar { get; set; }
}

public sealed class ReaderItem
{
    public string? ItemId { get; set; }
    public string? Proje { get; set; }
    public string? Deger { get; set; }
    public string? Kalite { get; set; }
    public int KaliteHam { get; set; }
    public string? Zaman { get; set; }
    public string? Hata { get; set; }
}

public enum IngestStatus { Stored, Duplicate, BadRequest, NoKeys, Unauthorized }

public sealed record IngestOutcome(IngestStatus Status, long? BatchId, int Readings, string? Message)
{
    [JsonIgnore] public int HttpStatus => Status switch
    {
        IngestStatus.Stored => 200,
        IngestStatus.Duplicate => 200,
        IngestStatus.BadRequest => 400,
        IngestStatus.NoKeys => 503,
        IngestStatus.Unauthorized => 401,
        _ => 500
    };
}
