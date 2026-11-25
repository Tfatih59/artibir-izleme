using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace Artibir.Izleme.Ingest;

public static class IngestKeyMatcher
{
    public static bool AnyConfigured(IConfiguration config) =>
        config.GetSection("Ingest:Keys").GetChildren().Any(c => !string.IsNullOrEmpty(c.Value));

    public static string? MatchPlantCode(IConfiguration config, string? presented)
    {
        if (string.IsNullOrEmpty(presented))
            return null;
        foreach (var child in config.GetSection("Ingest:Keys").GetChildren())
        {
            var expected = child.Value;
            if (string.IsNullOrEmpty(expected))
                continue;
            if (FixedEquals(expected, presented))
                return child.Key.Trim().ToLowerInvariant();
        }
        return null;
    }

    public static bool FixedEquals(string expected, string presented)
    {
        var a = Encoding.UTF8.GetBytes(expected);
        var b = Encoding.UTF8.GetBytes(presented);
        if (a.Length != b.Length)
        {
            CryptographicOperations.FixedTimeEquals(a, a);
            return false;
        }
        return CryptographicOperations.FixedTimeEquals(a, b);
    }
}
