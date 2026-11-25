using System.Globalization;

namespace Artibir.Izleme.Api;

public sealed class LogWriteRequest
{
    public string? TurbineCode { get; set; }
    public string? OccurredAt { get; set; }
    public string? Description { get; set; }
    public string? AuthorName { get; set; }
    public string? Status { get; set; }
}

public sealed class StatusPatch
{
    public string? Status { get; set; }
}

public static class LogValidation
{
    public static readonly string[] Statuses = ["Açık", "İnceleniyor", "Kapalı"];

    public static string? NormalizeStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
            return "Açık";
        var found = Statuses.FirstOrDefault(s => string.Equals(s, status.Trim(), StringComparison.OrdinalIgnoreCase));
        return found;
    }

    public static bool TryParseLocal(string? text, out DateTime value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        var formats = new[] { "yyyy-MM-ddTHH:mm", "yyyy-MM-dd HH:mm", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd HH:mm:ss" };
        if (!DateTime.TryParseExact(text.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
            && !DateTime.TryParse(text.Trim(), CultureInfo.GetCultureInfo("tr-TR"), DateTimeStyles.None, out dt))
            return false;
        value = DateTime.SpecifyKind(dt, DateTimeKind.Unspecified);
        return true;
    }

    public static string? CleanCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return null;
        var t = code.Trim();
        return t.Length > 32 ? t[..32] : t;
    }

    public static string? Validate(LogWriteRequest body, out DateTime when, out string description, out string status)
    {
        when = default;
        description = "";
        status = "Açık";
        if (body.Description == null || body.Description.Trim().Length < 3)
            return "Açıklama en az 3 karakter olmalı.";
        description = body.Description.Trim();
        if (description.Length > 2000)
            description = description[..2000];
        if (!TryParseLocal(body.OccurredAt, out when))
            return "Tarih YYYY-MM-DD HH:mm biçiminde olmalı (Türkiye saati).";
        var st = NormalizeStatus(body.Status);
        if (st == null)
            return "Durum Açık, İnceleniyor veya Kapalı olmalı.";
        status = st;
        return null;
    }
}
