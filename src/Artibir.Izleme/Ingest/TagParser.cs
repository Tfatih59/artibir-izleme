using System.Globalization;

namespace Artibir.Izleme.Ingest;

public readonly record struct TagParts(string ItemId, string? WtgCode, string GroupName, string SignalName, string? UnitHint);

public static class TagParser
{
    public static TagParts Parse(string itemId)
    {
        var parts = itemId.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return new TagParts(itemId, null, "", itemId, null);

        string? wtg = null;
        var groupStart = 1;
        if (parts.Length >= 2 && parts[1].StartsWith("WTG", StringComparison.OrdinalIgnoreCase))
        {
            wtg = parts[1].ToUpperInvariant();
            groupStart = 2;
        }

        var signal = parts[^1];
        var group = groupStart <= parts.Length - 2
            ? string.Join('.', parts[groupStart..^1])
            : "";
        return new TagParts(itemId, wtg, group, signal, UnitFor(group, signal));
    }

    public static string? TurbineCodeFromWtg(string? wtg)
    {
        if (string.IsNullOrEmpty(wtg))
            return null;
        var digits = new string(wtg.Where(char.IsDigit).ToArray());
        if (!int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
            return null;
        return "T" + n.ToString("00", CultureInfo.InvariantCulture);
    }

    public static bool TryParseNumber(string? text, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        var s = text.Trim();
        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            return true;
        return double.TryParse(s, NumberStyles.Float, CultureInfo.GetCultureInfo("tr-TR"), out value);
    }

    public static bool IsGood(string? qualityText, int qualityRaw)
    {
        if (!string.IsNullOrEmpty(qualityText) && qualityText.Contains("Bad", StringComparison.OrdinalIgnoreCase))
            return false;
        if (!string.IsNullOrEmpty(qualityText) && qualityText.Contains("Good", StringComparison.OrdinalIgnoreCase))
            return true;
        return (qualityRaw & 0xC0) == 0xC0;
    }

    public static DateTime? ParseLocalTimestamp(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var formats = new[]
        {
            "yyyy-MM-dd HH:mm:ss",
            "yyyy-MM-ddTHH:mm:ss",
            "yyyy-MM-dd HH:mm",
            "yyyy-MM-ddTHH:mm"
        };
        if (DateTime.TryParseExact(text.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return DateTime.SpecifyKind(dt, DateTimeKind.Unspecified);
        return null;
    }

    static string? UnitFor(string group, string signal)
    {
        var s = signal.ToLowerInvariant();
        var g = group.ToLowerInvariant();
        if (s.Contains("power") && !s.Contains("factor"))
            return s.Contains("reactive") ? "kVAr" : "kW";
        if (s.Contains("windspeed")) return "m/s";
        if (s.Contains("winddir") || s.Contains("pitch") || s == "direction") return "°";
        if (s.Contains("temperature") || s.Contains("temperatur")) return "°C";
        if (s.Contains("rpm")) return "d/d";
        if (s.Contains("frequency")) return "Hz";
        if (s.Contains("voltage")) return "V";
        if (s.Contains("current")) return "A";
        if (s.Contains("powerfactor") || s == "cosphi") return "";
        if (g.Contains("counter") && s.Contains("production")) return "kWh";
        if (s.Contains("operationstate")) return "kod";
        return null;
    }
}
