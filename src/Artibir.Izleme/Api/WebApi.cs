using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Artibir.Izleme.Auth;
using Artibir.Izleme.Data;
using Artibir.Izleme.Excel;
using Artibir.Izleme.Ingest;
using Artibir.Izleme.Models;
using Artibir.Izleme.Options;
using Artibir.Izleme.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Artibir.Izleme.Api;

public static class WebApi
{
    public static void MapArtibirApi(this WebApplication app)
    {
        app.MapGet("/health", async (AppDbContext db, CancellationToken ct) =>
        {
            var ok = await db.Database.CanConnectAsync(ct);
            return ok
                ? Results.Ok(new { status = "ok" })
                : Results.Json(new { status = "db" }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }).AllowAnonymous();

        app.MapGet("/api/auth/mode", (IOptions<AuthOptions> auth, IOptions<AzureAdOptions> azure, IHostEnvironment env) =>
        {
            var a = auth.Value;
            return Results.Ok(new
            {
                mode = a.IsDev ? "Dev" : "Entra",
                entraReady = !a.IsDev && azure.Value.IsConfigured,
                development = env.IsDevelopment()
            });
        }).AllowAnonymous();

        app.MapPost("/api/auth/dev-login", async (DevLogin body, HttpContext ctx, IOptions<AuthOptions> auth, IHostEnvironment env) =>
        {
            var opt = auth.Value;
            if (!opt.IsDev)
                return Results.NotFound();
            var name = (body.Username ?? "").Trim();
            var user = opt.DevUsers.FirstOrDefault(u => string.Equals(u.Username, name, StringComparison.OrdinalIgnoreCase));
            if (user == null || !DevPasswordOk(opt, body.Password, env))
                return Results.Json(new { error = "Kullanıcı adı veya parola hatalı." }, statusCode: StatusCodes.Status401Unauthorized);

            var claims = new List<Claim>
            {
                new(ClaimTypes.Name, string.IsNullOrWhiteSpace(user.DisplayName) ? user.Username : user.DisplayName),
                new("oid", "dev:" + user.Username.ToLowerInvariant()),
                new("preferred_username", user.Username)
            };
            if (user.Executive)
                claims.Add(new Claim(ClaimTypes.Role, "Executive"));
            foreach (var plant in user.Plants.Where(p => !string.IsNullOrWhiteSpace(p)))
                claims.Add(new Claim(AccessEvaluator.PlantClaim, plant.Trim().ToLowerInvariant()));

            var id = new ClaimsIdentity(claims, Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
            await ctx.SignInAsync(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(id));
            return Results.Ok(new { ok = true });
        }).AllowAnonymous();

        app.MapGet("/api/auth/signin", (HttpContext ctx, string? returnUrl, IOptions<AuthOptions> auth, IOptions<AzureAdOptions> azure) =>
        {
            if (auth.Value.IsDev)
                return Results.Redirect("/");
            if (!azure.Value.IsConfigured)
                return Results.Problem("Entra kimliği yapılandırılmadı. AzureAd__TenantId, AzureAd__ClientId ve AzureAd__ClientSecret gerekli.", statusCode: 503);
            var back = SafeReturn(returnUrl);
            return Results.Challenge(
                new Microsoft.AspNetCore.Authentication.AuthenticationProperties { RedirectUri = back },
                new[] { Microsoft.AspNetCore.Authentication.OpenIdConnect.OpenIdConnectDefaults.AuthenticationScheme });
        }).AllowAnonymous();

        app.MapGet("/api/auth/signout", async (HttpContext ctx, IOptions<AuthOptions> auth) =>
        {
            await ctx.SignOutAsync(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
            if (!auth.Value.IsDev && ctx.RequestServices.GetService<Microsoft.AspNetCore.Authentication.IAuthenticationSchemeProvider>() is { } schemes)
            {
                var scheme = await schemes.GetSchemeAsync(Microsoft.AspNetCore.Authentication.OpenIdConnect.OpenIdConnectDefaults.AuthenticationScheme);
                if (scheme != null)
                {
                    await ctx.SignOutAsync(Microsoft.AspNetCore.Authentication.OpenIdConnect.OpenIdConnectDefaults.AuthenticationScheme,
                        new Microsoft.AspNetCore.Authentication.AuthenticationProperties { RedirectUri = "/" });
                    return Results.Empty;
                }
            }
            return Results.Redirect("/");
        });

        app.MapPost("/api/ingest", Ingest).AllowAnonymous().DisableAntiforgery();

        var api = app.MapGroup("/api").RequireAuthorization();
        api.MapGet("/me", Me);
        api.MapGet("/plants", Plants);
        api.MapGet("/plants/{code}/live", Live);
        api.MapGet("/plants/{code}/power", Power);
        api.MapGet("/plants/{code}/production", Production);
        api.MapGet("/plants/{code}/availability", Availability);
        api.MapGet("/plants/{code}/signals", Signals);
        api.MapGet("/plants/{code}/finance", Finance);
        api.MapGet("/plants/{code}/shifts", (string code, HttpContext ctx, AppDbContext db, CancellationToken ct) =>
            ListLogs(code, ctx, db, ct, shifts: true));
        api.MapPost("/plants/{code}/shifts", (string code, LogWriteRequest body, HttpContext ctx, AppDbContext db, CancellationToken ct) =>
            CreateLogs(code, new[] { body }, ctx, db, ct, shifts: true));
        api.MapPost("/plants/{code}/shifts/bulk", (string code, List<LogWriteRequest> body, HttpContext ctx, AppDbContext db, CancellationToken ct) =>
            CreateLogs(code, body, ctx, db, ct, shifts: true));
        api.MapPatch("/plants/{code}/shifts/{id:long}", (string code, long id, StatusPatch body, HttpContext ctx, AppDbContext db, CancellationToken ct) =>
            PatchStatus(code, id, body, ctx, db, ct, shifts: true));
        api.MapGet("/plants/{code}/faults", (string code, HttpContext ctx, AppDbContext db, CancellationToken ct) =>
            ListLogs(code, ctx, db, ct, shifts: false));
        api.MapPost("/plants/{code}/faults", (string code, LogWriteRequest body, HttpContext ctx, AppDbContext db, CancellationToken ct) =>
            CreateLogs(code, new[] { body }, ctx, db, ct, shifts: false));
        api.MapPost("/plants/{code}/faults/bulk", (string code, List<LogWriteRequest> body, HttpContext ctx, AppDbContext db, CancellationToken ct) =>
            CreateLogs(code, body, ctx, db, ct, shifts: false));
        api.MapPatch("/plants/{code}/faults/{id:long}", (string code, long id, StatusPatch body, HttpContext ctx, AppDbContext db, CancellationToken ct) =>
            PatchStatus(code, id, body, ctx, db, ct, shifts: false));
        api.MapPost("/admin/excel", ImportExcel).DisableAntiforgery();
        api.MapGet("/admin/imports", ListImports);
    }

    static async Task<IResult> Ingest(HttpRequest req, AppDbContext db, IngestService ingest, IConfiguration config, ILoggerFactory logs, CancellationToken ct)
    {
        var log = logs.CreateLogger("Ingest");
        if (!IngestKeyMatcher.AnyConfigured(config))
        {
            log.LogWarning("Ingest reddedildi: Ingest:Keys boş.");
            return Results.Json(new { status = "no_keys", message = "Ingest anahtarları yapılandırılmadı." }, statusCode: 503);
        }

        var presented = req.Headers["X-Api-Key"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(presented))
            presented = req.Query["key"];
        var code = IngestKeyMatcher.MatchPlantCode(config, presented);
        if (code == null)
            return Results.Json(new { status = "unauthorized" }, statusCode: 401);

        var plant = await db.Plants.AsNoTracking().FirstOrDefaultAsync(p => p.Code == code, ct);
        if (plant == null)
            return Results.Json(new { status = "unauthorized" }, statusCode: 401);

        if (req.ContentLength > 20_000_000)
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        using var buffer = new MemoryStream();
        await req.Body.CopyToAsync(buffer, ct);
        if (buffer.Length > 20_000_000)
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

        var outcome = await ingest.AcceptAsync(plant, buffer.ToArray(), ct);
        log.LogInformation("Ingest {Plant} {Status} batch {Batch} readings {Readings}", plant.Code, outcome.Status, outcome.BatchId, outcome.Readings);
        return Results.Json(new
        {
            status = outcome.Status switch
            {
                IngestStatus.Stored => "stored",
                IngestStatus.Duplicate => "duplicate",
                _ => "error"
            },
            batchId = outcome.BatchId,
            readings = outcome.Readings,
            message = outcome.Message
        }, statusCode: outcome.HttpStatus);
    }

    static IResult Me(HttpContext ctx)
    {
        var user = ctx.User;
        var name = user.Identity?.Name ?? "";
        return Results.Ok(new
        {
            name,
            username = user.FindFirstValue("preferred_username") ?? name,
            executive = AccessEvaluator.IsExecutive(user),
            plants = AccessEvaluator.PlantCodes(user),
            groupsMissing = user.HasClaim(AccessEvaluator.GroupsMissingClaim, "1")
        });
    }

    static async Task<IResult> Plants(HttpContext ctx, AppDbContext db, PlantQueries queries, CancellationToken ct)
    {
        var all = await db.Plants.AsNoTracking().OrderBy(p => p.Id).ToListAsync(ct);
        var visible = all.Where(p => AccessEvaluator.CanSeePlant(ctx.User, p.Code)).ToList();
        var cards = await queries.PlantCardsAsync(visible, ct);
        return Results.Ok(new
        {
            executive = AccessEvaluator.IsExecutive(ctx.User),
            plants = cards
        });
    }

    static async Task<IResult> Live(string code, HttpContext ctx, AppDbContext db, PlantQueries queries, CancellationToken ct)
    {
        var gate = await LoadPlant(code, ctx, db, execOnly: false, ct);
        if (gate.Error != null) return gate.Error;
        return Results.Ok(await queries.LiveAsync(gate.Plant!, ct));
    }

    static async Task<IResult> Power(string code, string? range, string? from, string? to, string? resolution, HttpContext ctx, AppDbContext db, PlantQueries queries, CancellationToken ct)
    {
        var gate = await LoadPlant(code, ctx, db, false, ct);
        if (gate.Error != null) return gate.Error;
        if (!TryRange(range ?? "today", from, to, out var start, out var end, out var error))
            return Results.BadRequest(new { error });
        return Results.Ok(await queries.PowerSeriesAsync(gate.Plant!, start, end, resolution ?? "10min", ct));
    }

    static async Task<IResult> Production(string code, string? range, string? from, string? to, string? bucket, HttpContext ctx, AppDbContext db, PlantQueries queries, CancellationToken ct)
    {
        var gate = await LoadPlant(code, ctx, db, false, ct);
        if (gate.Error != null) return gate.Error;
        if (!TryRange(range ?? "month", from, to, out var start, out var end, out var error))
            return Results.BadRequest(new { error });
        return Results.Ok(await queries.ProductionAsync(gate.Plant!, start, end, bucket ?? DefaultBucket(range), ct));
    }

    static async Task<IResult> Availability(string code, string? range, string? from, string? to, HttpContext ctx, AppDbContext db, PlantQueries queries, CancellationToken ct)
    {
        var gate = await LoadPlant(code, ctx, db, false, ct);
        if (gate.Error != null) return gate.Error;
        if (!TryRange(range ?? "week", from, to, out var start, out var end, out var error))
            return Results.BadRequest(new { error });
        return Results.Ok(await queries.AvailabilityAsync(gate.Plant!, start, end, ct));
    }

    static async Task<IResult> Signals(string code, HttpContext ctx, AppDbContext db, PlantQueries queries, CancellationToken ct)
    {
        var gate = await LoadPlant(code, ctx, db, false, ct);
        if (gate.Error != null) return gate.Error;
        return Results.Ok(await queries.SignalsAsync(gate.Plant!, ct));
    }

    static async Task<IResult> Finance(string code, string? from, string? to, HttpContext ctx, AppDbContext db, PlantQueries queries, CancellationToken ct)
    {
        var gate = await LoadPlant(code, ctx, db, execOnly: true, ct);
        if (gate.Error != null) return gate.Error;
        if (!TryRange("years", from, to, out var start, out var end, out var error))
            return Results.BadRequest(new { error });
        return Results.Ok(await queries.FinanceAsync(gate.Plant!, start, end, ct));
    }

    static async Task<IResult> ListLogs(string code, HttpContext ctx, AppDbContext db, CancellationToken ct, bool shifts)
    {
        var gate = await LoadPlant(code, ctx, db, false, ct);
        if (gate.Error != null) return gate.Error;
        if (shifts)
        {
            var rows = await db.ShiftLogs.AsNoTracking()
                .Where(s => s.PlantId == gate.Plant!.Id)
                .OrderByDescending(s => s.OccurredAtLocal)
                .Take(500)
                .ToListAsync(ct);
            return Results.Ok(new { entries = rows.Select(s => new { id = s.Id, display = "VRD-" + s.Id, s.TurbineCode, occurredAt = s.OccurredAtLocal, s.Description, s.AuthorName, s.Status, s.Source, s.CreatedBy }) });
        }
        var faults = await db.Faults.AsNoTracking()
            .Where(s => s.PlantId == gate.Plant!.Id)
            .OrderByDescending(s => s.OccurredAtLocal)
            .Take(500)
            .ToListAsync(ct);
        return Results.Ok(new { entries = faults.Select(s => new { id = s.Id, display = "ARZ-" + s.Id, s.TurbineCode, occurredAt = s.OccurredAtLocal, s.Description, responsible = s.Responsible, s.Status, s.Source, s.CreatedBy }) });
    }

    static async Task<IResult> CreateLogs(string code, IReadOnlyList<LogWriteRequest> body, HttpContext ctx, AppDbContext db, CancellationToken ct, bool shifts)
    {
        var gate = await LoadPlant(code, ctx, db, false, ct);
        if (gate.Error != null) return gate.Error;
        if (body.Count == 0)
            return Results.BadRequest(new { error = "Kayıt yok." });
        if (body.Count > 1000)
            return Results.BadRequest(new { error = "En fazla 1000 satır." });

        var errors = new List<string>();
        var authorFallback = ctx.User.Identity?.Name;
        var prepared = new List<(DateTime When, string Description, string Status, string? Turbine, string? Author)>();
        for (var i = 0; i < body.Count; i++)
        {
            var err = LogValidation.Validate(body[i], out var when, out var description, out var status);
            if (err != null)
            {
                errors.Add(body.Count == 1 ? err : $"Satır {i + 1}: {err}");
                continue;
            }
            var author = string.IsNullOrWhiteSpace(body[i].AuthorName) ? authorFallback : body[i].AuthorName!.Trim();
            prepared.Add((when, description, status, LogValidation.CleanCode(body[i].TurbineCode), author));
        }
        if (errors.Count > 0)
            return Results.BadRequest(new { errors });

        if (shifts)
        {
            foreach (var row in prepared)
            {
                db.ShiftLogs.Add(new ShiftLogEntry
                {
                    PlantId = gate.Plant!.Id,
                    TurbineCode = row.Turbine,
                    OccurredAtLocal = row.When,
                    Description = row.Description,
                    AuthorName = row.Author,
                    Status = row.Status,
                    Source = body.Count > 1 ? "csv" : "manuel",
                    CreatedAtUtc = DateTime.UtcNow,
                    CreatedBy = authorFallback
                });
            }
        }
        else
        {
            foreach (var row in prepared)
            {
                db.Faults.Add(new FaultRecord
                {
                    PlantId = gate.Plant!.Id,
                    TurbineCode = row.Turbine,
                    OccurredAtLocal = row.When,
                    Description = row.Description,
                    Responsible = row.Author,
                    Status = row.Status,
                    Source = body.Count > 1 ? "csv" : "manuel",
                    CreatedAtUtc = DateTime.UtcNow,
                    CreatedBy = authorFallback
                });
            }
        }
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { created = prepared.Count });
    }

    static async Task<IResult> PatchStatus(string code, long id, StatusPatch body, HttpContext ctx, AppDbContext db, CancellationToken ct, bool shifts)
    {
        var gate = await LoadPlant(code, ctx, db, false, ct);
        if (gate.Error != null) return gate.Error;
        var status = LogValidation.NormalizeStatus(body.Status);
        if (status == null)
            return Results.BadRequest(new { error = "Durum Açık, İnceleniyor veya Kapalı olmalı." });
        if (shifts)
        {
            var row = await db.ShiftLogs.FirstOrDefaultAsync(s => s.Id == id && s.PlantId == gate.Plant!.Id, ct);
            if (row == null) return Results.NotFound();
            row.Status = status;
        }
        else
        {
            var row = await db.Faults.FirstOrDefaultAsync(s => s.Id == id && s.PlantId == gate.Plant!.Id, ct);
            if (row == null) return Results.NotFound();
            row.Status = status;
        }
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { id, status });
    }

    static async Task<IResult> ImportExcel(HttpRequest req, HttpContext ctx, AppDbContext db, ExcelImportService imports, CancellationToken ct)
    {
        if (!AccessEvaluator.IsExecutive(ctx.User))
            return Results.Json(new { error = "Excel aktarımı yalnız üst yönetim içindir." }, statusCode: 403);
        if (!req.HasFormContentType)
            return Results.BadRequest(new { error = "multipart/form-data gerekli." });
        var form = await req.ReadFormAsync(ct);
        var code = form["plant"].ToString().Trim().ToLowerInvariant();
        if (!AccessEvaluator.CanSeePlant(ctx.User, code))
            return Results.Json(new { error = "Bu santrale yetkin yok." }, statusCode: 403);
        var plant = await db.Plants.FirstOrDefaultAsync(p => p.Code == code, ct);
        if (plant == null)
            return Results.NotFound(new { error = "Santral yok." });
        int? year = int.TryParse(form["year"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var y) ? y : null;
        int? month = int.TryParse(form["month"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var m) && m is >= 1 and <= 12 ? m : null;
        if (form.Files.Count == 0)
            return Results.BadRequest(new { error = "Dosya seçilmedi." });
        if (form.Files.Count > 80)
            return Results.BadRequest(new { error = "Tek seferde en fazla 80 dosya." });

        var results = new List<ImportFileResult>();
        foreach (var file in form.Files)
        {
            if (file.Length > ExcelImportService.MaxFileBytes)
            {
                results.Add(new ImportFileResult(file.FileName, false, false, 0, null, null, true, Array.Empty<string>(), "Dosya 40 MB sınırını aşıyor."));
                continue;
            }
            using var ms = new MemoryStream();
            await file.CopyToAsync(ms, ct);
            results.Add(await imports.ImportAsync(plant, file.FileName, ms.ToArray(), year, month, ctx.User.Identity?.Name, ct));
        }
        return Results.Ok(new { results });
    }

    static async Task<IResult> ListImports(string? plant, HttpContext ctx, AppDbContext db, CancellationToken ct)
    {
        if (!AccessEvaluator.IsExecutive(ctx.User))
            return Results.Json(new { error = "Yalnız üst yönetim." }, statusCode: 403);
        var q = db.ImportBatches.AsNoTracking().OrderByDescending(b => b.ImportedAtUtc).Take(200);
        if (!string.IsNullOrWhiteSpace(plant))
        {
            var p = await db.Plants.AsNoTracking().FirstOrDefaultAsync(x => x.Code == plant.Trim().ToLowerInvariant(), ct);
            if (p == null)
                return Results.Ok(new { imports = Array.Empty<object>() });
            var rows = await db.ImportBatches.AsNoTracking().Where(b => b.PlantId == p.Id).OrderByDescending(b => b.ImportedAtUtc).Take(200).ToListAsync(ct);
            return Results.Ok(new { imports = rows });
        }
        var all = await q.ToListAsync(ct);
        return Results.Ok(new { imports = all });
    }

    static async Task<(Plant? Plant, IResult? Error)> LoadPlant(string code, HttpContext ctx, AppDbContext db, bool execOnly, CancellationToken ct)
    {
        if (ctx.User.Identity?.IsAuthenticated != true)
            return (null, Results.Unauthorized());
        if (execOnly && !AccessEvaluator.IsExecutive(ctx.User))
            return (null, Results.Json(new { error = "Bu sayfa yalnız üst yönetim içindir." }, statusCode: 403));
        if (!AccessEvaluator.CanSeePlant(ctx.User, code))
            return (null, Results.Json(new { error = "Bu santrale yetkin yok." }, statusCode: 403));
        var plant = await db.Plants.AsNoTracking().FirstOrDefaultAsync(p => p.Code == code.Trim().ToLowerInvariant(), ct);
        if (plant == null)
            return (null, Results.NotFound(new { error = "Santral yok." }));
        return (plant, null);
    }

    public static bool TryRange(string? range, string? from, string? to, out DateTime start, out DateTime end, out string? error)
    {
        error = null;
        var now = TurkeyTime.NowUnspecified();
        if (!string.IsNullOrWhiteSpace(from) || !string.IsNullOrWhiteSpace(to))
        {
            if (!LogValidation.TryParseLocal(from, out start))
            {
                end = default;
                error = "Başlangıç tarihi geçersiz.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(to))
                end = now.Date.AddDays(1);
            else if (!LogValidation.TryParseLocal(to, out end))
            {
                error = "Bitiş tarihi geçersiz.";
                return false;
            }
            else if (end.TimeOfDay == TimeSpan.Zero)
                end = end.Date.AddDays(1);
            return true;
        }

        end = now.Date.AddDays(1);
        start = (range ?? "today") switch
        {
            "today" => now.Date,
            "week" => end.AddDays(-7),
            "month" => end.AddDays(-31),
            "year" => end.AddDays(-366),
            "years" => new DateTime(2015, 1, 1),
            _ => now.Date
        };
        return true;
    }

    static string DefaultBucket(string? range) => range switch
    {
        "today" or "week" => "hour",
        "month" => "day",
        "year" => "month",
        "years" => "year",
        _ => "day"
    };

    static string SafeReturn(string? returnUrl)
    {
        if (string.IsNullOrEmpty(returnUrl) || !returnUrl.StartsWith('/') || returnUrl.StartsWith("//"))
            return "/";
        return returnUrl;
    }

    static bool DevPasswordOk(AuthOptions opt, string? password, IHostEnvironment env)
    {
        var expected = opt.DevPassword;
        if (string.IsNullOrEmpty(expected))
        {
            if (!env.IsDevelopment())
                return false;
            expected = "artibir-dev";
        }
        if (string.IsNullOrEmpty(password))
            return false;
        return IngestKeyMatcher.FixedEquals(expected, password);
    }

    public sealed class DevLogin
    {
        public string? Username { get; set; }
        public string? Password { get; set; }
    }
}
