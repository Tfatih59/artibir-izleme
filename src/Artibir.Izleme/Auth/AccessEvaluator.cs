using System.Security.Claims;
using Artibir.Izleme.Options;

namespace Artibir.Izleme.Auth;

public static class AccessEvaluator
{
    public const string PlantClaim = "plant";
    public const string GroupsMissingClaim = "groups_missing";

    public static bool IsExecutive(ClaimsPrincipal user) =>
        user.Identity?.IsAuthenticated == true && user.IsInRole("Executive");

    public static bool CanSeePlant(ClaimsPrincipal user, string code)
    {
        if (user.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(code))
            return false;
        if (IsExecutive(user))
            return true;
        return user.Claims.Any(c =>
            c.Type == PlantClaim && string.Equals(c.Value, code, StringComparison.OrdinalIgnoreCase));
    }

    public static IReadOnlyList<string> PlantCodes(ClaimsPrincipal user) =>
        user.Claims.Where(c => c.Type == PlantClaim).Select(c => c.Value.ToLowerInvariant()).Distinct().ToList();

    public static void MapGroups(ClaimsIdentity identity, IEnumerable<string> groupIds, AuthorizationMapOptions map)
    {
        var groups = new HashSet<string>(groupIds.Where(g => !string.IsNullOrWhiteSpace(g)), StringComparer.OrdinalIgnoreCase);
        if (groups.Count == 0)
            identity.AddClaim(new Claim(GroupsMissingClaim, "1"));

        if (!string.IsNullOrWhiteSpace(map.ExecutiveGroupId) && groups.Contains(map.ExecutiveGroupId))
            identity.AddClaim(new Claim(ClaimTypes.Role, "Executive"));

        foreach (var pair in map.PlantGroupIds)
        {
            if (string.IsNullOrWhiteSpace(pair.Value))
                continue;
            if (groups.Contains(pair.Value))
                identity.AddClaim(new Claim(PlantClaim, pair.Key.Trim().ToLowerInvariant()));
        }
    }

    public static IEnumerable<string> ReadGroups(ClaimsPrincipal principal)
    {
        foreach (var c in principal.Claims)
        {
            if (c.Type is "groups" or "http://schemas.microsoft.com/ws/2008/06/identity/claims/groups")
                yield return c.Value;
        }
    }
}
