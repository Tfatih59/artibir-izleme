using System.Security.Claims;
using Artibir.Izleme.Api;
using Artibir.Izleme.Auth;
using Artibir.Izleme.Data;
using Artibir.Izleme.Excel;
using Artibir.Izleme.Ingest;
using Artibir.Izleme.Options;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Web;

AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = 220_000_000);
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o =>
{
    o.MultipartBodyLengthLimit = 220_000_000;
    o.ValueCountLimit = 4096;
});

builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection(AuthOptions.Section));
builder.Services.PostConfigure<AuthOptions>(opt =>
{
    if (!opt.IsDev || opt.DevUsers.Count > 0)
        return;
    opt.DevUsers.Add(new DevUserOptions { Username = "deniz", DisplayName = "Deniz", Executive = true, Plants = ["gop", "yilmaz", "yenikoy", "kucukkoy"] });
    opt.DevUsers.Add(new DevUserOptions { Username = "ahmet", DisplayName = "Ahmet", Executive = true, Plants = ["gop", "yilmaz", "yenikoy", "kucukkoy"] });
    opt.DevUsers.Add(new DevUserOptions { Username = "fatih", DisplayName = "Fatih", Executive = false, Plants = ["gop"] });
});
builder.Services.Configure<AuthorizationMapOptions>(builder.Configuration.GetSection(AuthorizationMapOptions.Section));
builder.Services.Configure<RetentionOptions>(builder.Configuration.GetSection(RetentionOptions.Section));
builder.Services.Configure<AzureAdOptions>(builder.Configuration.GetSection("AzureAd"));

var authMode = builder.Configuration["Auth:Mode"] ?? "Entra";
var devMode = string.Equals(authMode, "Dev", StringComparison.OrdinalIgnoreCase);
var azure = builder.Configuration.GetSection("AzureAd").Get<AzureAdOptions>() ?? new AzureAdOptions();

if (devMode || !azure.IsConfigured)
{
    builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
        .AddCookie(ConfigureCookie);
}
else
{
    builder.Services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
        .AddMicrosoftIdentityWebApp(options =>
        {
            builder.Configuration.Bind("AzureAd", options);
            options.Events ??= new OpenIdConnectEvents();
            var previous = options.Events.OnTokenValidated;
            options.Events.OnTokenValidated = async ctx =>
            {
                if (previous != null)
                    await previous(ctx);
                if (ctx.Principal?.Identity is ClaimsIdentity identity)
                {
                    var map = ctx.HttpContext.RequestServices.GetRequiredService<Microsoft.Extensions.Options.IOptions<AuthorizationMapOptions>>().Value;
                    AccessEvaluator.MapGroups(identity, AccessEvaluator.ReadGroups(ctx.Principal), map);
                }
            };
        });
    builder.Services.Configure<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme, ConfigureCookie);
}

builder.Services.AddAuthorization();
builder.Services.AddHttpContextAccessor();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException("ConnectionStrings:DefaultConnection tanımlı değil. Ortam değişkeni ConnectionStrings__DefaultConnection olarak verin.");

builder.Services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connectionString).UseSnakeCaseNamingConvention());
builder.Services.AddScoped<IngestService>();
builder.Services.AddScoped<ExcelImportService>();
builder.Services.AddScoped<PlantQueries>();
builder.Services.AddScoped<UserSyncService>();
builder.Services.AddSingleton<UserSyncMiddleware>();
builder.Services.AddHostedService<ReadingRollupService>();

var keyRing = builder.Configuration["DataProtection:Path"];
if (string.IsNullOrWhiteSpace(keyRing))
    keyRing = ResolveKeyRing();
Directory.CreateDirectory(keyRing);
builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keyRing));

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
});

var app = builder.Build();

var forwarded = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
};
forwarded.KnownNetworks.Clear();
forwarded.KnownProxies.Clear();
app.UseForwardedHeaders(forwarded);

if (!app.Environment.IsDevelopment())
    app.UseHsts();

app.UseExceptionHandler(handler =>
{
    handler.Run(async ctx =>
    {
        var feature = ctx.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();
        var logger = ctx.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Hata");
        logger.LogError(feature?.Error, "İstek başarısız: {Path}", ctx.Request.Path);
        ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
        if (ctx.Request.Path.StartsWithSegments("/api"))
            await ctx.Response.WriteAsJsonAsync(new { error = "Sunucu hatası." });
    });
});

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseMiddleware<UserSyncMiddleware>();
app.UseAuthorization();
app.MapArtibirApi();
app.MapFallbackToFile("index.html");

await MigrateAndSeedAsync(app);
app.Run();

static void ConfigureCookie(CookieAuthenticationOptions options)
{
    options.Cookie.Name = "artibir.auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.SlidingExpiration = true;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.Events.OnRedirectToLogin = ctx =>
    {
        if (ctx.Request.Path.StartsWithSegments("/api"))
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        }
        ctx.Response.Redirect("/");
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = ctx =>
    {
        if (ctx.Request.Path.StartsWithSegments("/api"))
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        }
        ctx.Response.Redirect("/");
        return Task.CompletedTask;
    };
}

static string ResolveKeyRing()
{
    var candidates = new[]
    {
        "/home/artibir-dataprotection",
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".artibir-dataprotection")
    };
    foreach (var path in candidates)
    {
        try
        {
            Directory.CreateDirectory(path);
            var probe = Path.Combine(path, ".write-probe");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return path;
        }
        catch (Exception)
        {
            // App Service'te /home yazılır; yerel makinede kullanıcı dizinine düşülür.
        }
    }
    var fallback = Path.Combine(Path.GetTempPath(), "artibir-dataprotection");
    Directory.CreateDirectory(fallback);
    return fallback;
}

static async Task MigrateAndSeedAsync(WebApplication app)
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Migrate");
    for (var attempt = 1; ; attempt++)
    {
        try
        {
            await db.Database.MigrateAsync();
            await DbSeeder.SeedAsync(db);
            logger.LogInformation("Veritabanı hazır.");
            return;
        }
        catch (Exception ex) when (attempt < 8)
        {
            logger.LogWarning(ex, "Veritabanına bağlanılamadı ({Attempt}/8). Yeniden denenecek.", attempt);
            await Task.Delay(TimeSpan.FromSeconds(Math.Min(20, attempt * 3)));
        }
    }
}
