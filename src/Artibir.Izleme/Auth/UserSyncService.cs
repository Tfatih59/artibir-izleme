using System.Security.Claims;
using Artibir.Izleme.Data;
using Artibir.Izleme.Models;
using Microsoft.EntityFrameworkCore;

namespace Artibir.Izleme.Auth;

public class UserSyncService(AppDbContext db)
{
    public async Task TouchAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        if (user.Identity?.IsAuthenticated != true)
            return;
        var external = user.FindFirstValue("oid")
            ?? user.FindFirstValue("http://schemas.microsoft.com/identity/claims/objectidentifier")
            ?? user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(external))
            return;

        var name = user.Identity?.Name
            ?? user.FindFirstValue("name")
            ?? user.FindFirstValue("preferred_username")
            ?? external;
        var email = user.FindFirstValue(ClaimTypes.Email) ?? user.FindFirstValue("preferred_username");

        var entity = await db.Users.Include(u => u.Roles).FirstOrDefaultAsync(u => u.ExternalId == external, ct);
        if (entity == null)
        {
            entity = new AppUser { ExternalId = Trim(external, 80) };
            db.Users.Add(entity);
        }

        entity.DisplayName = Trim(name, 200);
        entity.Email = email == null ? null : Trim(email, 200);
        entity.LastSeenUtc = DateTime.UtcNow;

        var desired = new List<(string Role, string? Plant)>();
        if (AccessEvaluator.IsExecutive(user))
        {
            desired.Add(("Executive", ""));
            desired.Add(("Finance", ""));
        }
        foreach (var code in AccessEvaluator.PlantCodes(user))
            desired.Add(("PlantViewer", code));

        var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (role, plant) in desired)
        {
            var key = role + "|" + (plant ?? "");
            keep.Add(key);
            if (!entity.Roles.Any(r => r.Role == role && string.Equals(r.PlantCode ?? "", plant ?? "", StringComparison.OrdinalIgnoreCase)))
                entity.Roles.Add(new UserRoleAssignment { Role = role, PlantCode = plant });
        }
        var remove = entity.Roles.Where(r => !keep.Contains(r.Role + "|" + (r.PlantCode ?? ""))).ToList();
        if (remove.Count > 0)
            db.UserRoles.RemoveRange(remove);

        await db.SaveChangesAsync(ct);
    }

    static string Trim(string value, int max) => value.Length <= max ? value : value[..max];
}

public class UserSyncMiddleware(IServiceScopeFactory scopes, ILogger<UserSyncMiddleware> logger) : IMiddleware
{
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTime> Seen = new();

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var key = context.User.FindFirstValue("oid")
                ?? context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? context.User.Identity?.Name
                ?? "";
            var now = DateTime.UtcNow;
            var due = !Seen.TryGetValue(key, out var at) || at < now.AddMinutes(-10);
            if (due && key.Length > 0)
            {
                Seen[key] = now;
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var sync = scope.ServiceProvider.GetRequiredService<UserSyncService>();
                    await sync.TouchAsync(context.User, context.RequestAborted);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Kullanıcı rol eşlemesi yazılamadı.");
                }
            }
        }
        await next(context);
    }
}
