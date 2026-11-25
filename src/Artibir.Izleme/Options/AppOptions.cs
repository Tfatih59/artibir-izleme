namespace Artibir.Izleme.Options;

public class AuthOptions
{
    public const string Section = "Auth";
    public string Mode { get; set; } = "Entra";
    public string? DevPassword { get; set; }
    public List<DevUserOptions> DevUsers { get; set; } = new();
    public bool IsDev => string.Equals(Mode, "Dev", StringComparison.OrdinalIgnoreCase);
}

public class DevUserOptions
{
    public string Username { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public bool Executive { get; set; }
    public string[] Plants { get; set; } = [];
}

public class AuthorizationMapOptions
{
    public const string Section = "Authorization";
    public string? ExecutiveGroupId { get; set; }
    public Dictionary<string, string> PlantGroupIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public class RetentionOptions
{
    public const string Section = "Retention";
    public int RawDays { get; set; } = 7;
    public int RollupIntervalMinutes { get; set; } = 10;
}

public class AzureAdOptions
{
    public string? Instance { get; set; }
    public string? TenantId { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string CallbackPath { get; set; } = "/signin-oidc";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(TenantId) &&
        !string.IsNullOrWhiteSpace(ClientId) &&
        !string.IsNullOrWhiteSpace(ClientSecret);
}
