using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Security.Claims;
using System.Text.RegularExpressions;
using KCTools.LicenseServer.Configuration;
using KCTools.LicenseServer.Data;
using KCTools.LicenseServer.Logging;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using OpenIddict.Server.AspNetCore;
using OpenIddict.Validation.AspNetCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using KCTools.LicenseServer.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Console;

// ---------------------------------------------------------------------------
// KC Tools lisans sunucusu (referans uygulama)
//
//   HTTPS :5443   (varsayılan; dotnet dev-certs https --trust gerekir)
//   HTTP  :5080   (yalnızca yerel geliştirme; KCTOOLS_LICENSE_HTTP=1)
//
// Ortam değişkenleri:
//   KCTOOLS_JWT_SECRET    JWT imza anahtarı (üretimde ZORUNLU)
//   KCTOOLS_LICENSE_PORT  dinlenecek port (varsayılan 5443)
//
// Uç noktalar:
//   POST /api/v1/auth/activate    { hwid, licenseKey, appVersion, machineName }
//   GET  /api/v1/license/status   Authorization: Bearer <jwt>
//   POST /api/v1/license/consume  { amount, operation }
//   GET  /api/v1/health
// ---------------------------------------------------------------------------

var builder = WebApplication.CreateBuilder(args);

// Windows EventLog kaynağına yazma izni olmayan ortamlarda (container/servis)
// çökmemek için yalnızca konsol günlüğü kullanılır.
builder.Logging.ClearProviders();

if (builder.Environment.IsProduction())
{
    builder.Logging.AddConsoleFormatter<RedactingConsoleFormatter, ConsoleFormatterOptions>();
    builder.Logging.AddConsole(options =>
        options.FormatterName = RedactingConsoleFormatter.FormatterName);
}
else
{
    builder.Logging.AddConsole();
}

// Tüm yapılandırma tek noktadan okunur; mevcut ortam değişkeni adları korunur.
var configuration = LicenseServerConfiguration.Load(
    builder.Configuration, builder.Environment.ContentRootPath);

if (builder.Environment.IsProduction())
{
    var configurationErrors = configuration.ValidateForProduction();

    if (configurationErrors.Count > 0)
    {
        Console.Error.WriteLine("Üretim yapılandırması geçersiz:");

        foreach (var error in configurationErrors)
        {
            Console.Error.WriteLine(" - " + error);
        }

        throw new InvalidOperationException(
            "Üretim yapılandırması geçersiz: " + string.Join("; ", configurationErrors));
    }
}

var connectionString = configuration.ConnectionString;
var useDatabase = configuration.HasDatabase;
var adminKey = configuration.AdminKey;
var useSqlite = configuration.IsSqlite;

if (useDatabase)
{
    builder.Services.AddDbContext<LicenseDbContext>(options =>
    {
        if (useSqlite)
        {
            options.UseSqlite(connectionString);
        }
        else
        {
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.EnableRetryOnFailure(
                    maxRetryCount: 5,
                    maxRetryDelay: TimeSpan.FromSeconds(10),
                    errorCodesToAdd: null));
        }
    });

    builder.Services.AddScoped<LicenseRepository>();
    builder.Services.AddControllersWithViews();

    builder.Services.AddScoped(serviceProvider => new DatabaseReadinessChecker(
        serviceProvider.GetRequiredService<LicenseDbContext>(),
        serviceProvider.GetRequiredService<ILogger<DatabaseReadinessChecker>>(),
        configuration.DatabaseRetryCount,
        configuration.DatabaseRetryBaseDelayMs));

    builder.Services.AddOpenIddict()
        .AddCore(options => options.UseEntityFrameworkCore().UseDbContext<LicenseDbContext>())
        .AddServer(options =>
        {
            options.SetAuthorizationEndpointUris("/connect/authorize")
                .SetTokenEndpointUris("/connect/token");

            options.AllowAuthorizationCodeFlow().AllowRefreshTokenFlow();
            options.RequireProofKeyForCodeExchange();

            options.RegisterScopes(
                OpenIddictConstants.Scopes.OpenId, OpenIddictConstants.Scopes.Profile, OpenIddictConstants.Scopes.Email, OpenIddictConstants.Scopes.OfflineAccess,
                "kctools.license");

            options.AddEphemeralEncryptionKey();
            options.AddEphemeralSigningKey();

            var aspNetCore = options.UseAspNetCore().EnableAuthorizationEndpointPassthrough();

            if (configuration.AllowHttp)
            {
                aspNetCore.DisableTransportSecurityRequirement();
            }
        })
        .AddValidation(options =>
        {
            options.UseLocalServer();
            options.UseAspNetCore();
        });

    builder.Services
        .AddIdentity<AppUser, AppRole>(options =>
        {
            options.Password.RequiredLength = 10;
            options.Password.RequireDigit = true;
            options.Password.RequireUppercase = true;
            options.Password.RequireLowercase = true;
            options.Password.RequireNonAlphanumeric = true;
            options.User.RequireUniqueEmail = true;

            // Kaba kuvvet koruması: 5 hatalı deneme → 15 dakika kilit.
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            options.Lockout.AllowedForNewUsers = true;
        })
        .AddEntityFrameworkStores<LicenseDbContext>()
        .AddDefaultTokenProviders();

    builder.Services.ConfigureApplicationCookie(options =>
    {
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = configuration.AllowHttp
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
        options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
        options.SlidingExpiration = true;
        options.LoginPath = "/account/login";
        options.AccessDeniedPath = "/account/denied";
    });

    builder.Services.AddAuthorization();
}

var port = configuration.Port;
var allowHttp = configuration.AllowHttp;

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(port, listen =>
    {
        if (!allowHttp)
        {
            listen.UseHttps();
        }
    });
});

var offlineKeyPath = configuration.OfflineKeyPath;

builder.Services.AddSingleton(new OfflineEntitlementService(offlineKeyPath));

var loaderRoot = Environment.GetEnvironmentVariable("KCTOOLS_LOADER_ROOT")
    ?? Path.Combine(builder.Environment.ContentRootPath, "loaders");

builder.Services.AddSingleton(serviceProvider => new LoaderCatalogService(
    loaderRoot,
    serviceProvider.GetRequiredService<ILogger<LoaderCatalogService>>()));

var app = builder.Build();

// Migration üretimde uygulama başlangıcından bağımsızdır; yalnızca geliştirme
// ortamında açıkça istenirse uygulanır.
if (useDatabase && configuration.AutoMigrate && !builder.Environment.IsProduction())
{
    using (var scope = app.Services.CreateScope())
    {
        var database = scope.ServiceProvider.GetRequiredService<LicenseDbContext>();

        if (useSqlite)
        {
            await database.Database.EnsureCreatedAsync();
            app.Logger.LogInformation("SQLite şeması hazırlandı.");
        }
        else
        {
            await database.Database.MigrateAsync();
            app.Logger.LogInformation("Veritabanı migration'ları uygulandı.");
        }
    }
}
if (useDatabase)
{
    using (var scope = app.Services.CreateScope())
    {
        try
        {
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<AppRole>>();

            foreach (var roleName in new[] { AppRoles.Admin, AppRoles.Customer })
            {
                if (!await roles.RoleExistsAsync(roleName))
                {
                    await roles.CreateAsync(new AppRole(roleName));
                }
            }
            var applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();

            if (await applications.FindByClientIdAsync("kctools-desktop") == null)
            {
                await applications.CreateAsync(new OpenIddictApplicationDescriptor
                {
                    ClientId = "kctools-desktop",
                    ClientType = OpenIddictConstants.ClientTypes.Public,
                    ConsentType = OpenIddictConstants.ConsentTypes.Implicit,
                    DisplayName = "KC Tools Desktop",
                    RedirectUris = { new Uri("http://127.0.0.1:45999/callback") },
                    Permissions =
                    {
                        OpenIddictConstants.Permissions.Endpoints.Authorization,
                        OpenIddictConstants.Permissions.Endpoints.Token,
                        OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode,
                        OpenIddictConstants.Permissions.GrantTypes.RefreshToken,
                        OpenIddictConstants.Permissions.ResponseTypes.Code,
                        OpenIddictConstants.Permissions.Scopes.Profile,
                        OpenIddictConstants.Permissions.Scopes.Email,
                        OpenIddictConstants.Permissions.Prefixes.Scope + "kctools.license",
                        OpenIddictConstants.Permissions.Prefixes.Scope + OpenIddictConstants.Scopes.OfflineAccess
                    },
                    Requirements = { OpenIddictConstants.Requirements.Features.ProofKeyForCodeExchange }
                });
            }

            var adminEmail = configuration.BootstrapAdminEmail;
            var adminPassword = configuration.BootstrapAdminPassword;

            if (!string.IsNullOrWhiteSpace(adminEmail) && !string.IsNullOrWhiteSpace(adminPassword))
            {
                var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
                var admin = await users.FindByEmailAsync(adminEmail);

                if (admin == null)
                {
                    admin = new AppUser
                    {
                        UserName = adminEmail,
                        Email = adminEmail,
                        EmailConfirmed = true,
                        DisplayName = "Bootstrap Admin"
                    };

                    var created = await users.CreateAsync(admin, adminPassword);

                    if (created.Succeeded)
                    {
                        await users.AddToRoleAsync(admin, AppRoles.Admin);
                        app.Logger.LogInformation("Bootstrap admin oluşturuldu: {Email}", adminEmail);
                    }
                    else
                    {
                        app.Logger.LogWarning(
                            "Bootstrap admin oluşturulamadı: {Errors}",
                            string.Join(" | ", created.Errors.Select(item => item.Description)));
                    }
                }
                else if (!await users.IsInRoleAsync(admin, AppRoles.Admin))
                {
                    await users.AddToRoleAsync(admin, AppRoles.Admin);
                }
            }
        }
        catch (Exception ex)
        {
            app.Logger.LogWarning(ex, "Identity seed atlandı (veritabanı hazır değil olabilir).");
        }
    }
}

app.UseStaticFiles();

if (useDatabase)
{
    app.UseAuthentication();
    app.UseAuthorization();

    app.MapControllerRoute(
        name: "default",
        pattern: "{controller=Home}/{action=Index}/{id?}");
}

var secret = configuration.JwtSecret;

if (string.IsNullOrWhiteSpace(secret))
{
    secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    app.Logger.LogWarning(
        "KCTOOLS_JWT_SECRET tanımlı değil; geçici anahtar üretildi. " +
        "Sunucu yeniden başlarsa eski token'lar geçersiz olur.");
}

var store = new LicenseStore();
store.Seed("KC-DEMO-DEMO-DEMO", "PRO", 100);
store.Seed("KC-TEST-TEST-TEST", "PRO", 25);

app.MapGet("/api/v1/health", () => Results.Ok(new
{
    status = "ok",
    service = "kctools-license",
    utc = DateTimeOffset.UtcNow,
    https = !allowHttp
}));

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    service = "kctools-license",
    utc = DateTimeOffset.UtcNow,
    https = !allowHttp
}));

app.MapGet("/api/v1/health/ready", async Task<IResult> (HttpContext http) =>
{
    var checker = http.RequestServices.GetService<DatabaseReadinessChecker>();

    if (checker == null)
    {
        return Results.Json(new
        {
            status = "not_ready",
            database = "not-configured"
        }, statusCode: 503);
    }

    var report = await checker.CheckAsync(http.RequestAborted);

    return Results.Json(report, statusCode: report.Ready ? 200 : 503);
});

app.MapGet("/api/v1/loaders/{chipset}", (string chipset, LoaderCatalogService catalog) =>
{
    try
    {
        return Results.Ok(catalog.List(chipset));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapGet("/api/v1/loaders/{chipset}/{fileName}",
    (string chipset, string fileName, LoaderCatalogService catalog) =>
{
    try
    {
        var path = catalog.FindFile(chipset, fileName, out var info);

        if (path == null || info == null)
        {
            return Results.NotFound(new { error = "Loader bulunamadı." });
        }

        return Results.File(path, "application/octet-stream", info.FileName);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});
app.MapGet("/health/ready", async Task<IResult> (HttpContext http) =>
{
    var checker = http.RequestServices.GetService<DatabaseReadinessChecker>();

    if (checker == null)
    {
        return Results.Json(new
        {
            status = "not_ready",
            database = "not-configured"
        }, statusCode: 503);
    }

    var report = await checker.CheckAsync(http.RequestAborted);

    return Results.Json(report, statusCode: report.Ready ? 200 : 503);
});

app.MapPost("/api/v1/auth/activate", async Task<IResult> (ActivateRequest request, HttpContext http) =>
{
    if (!KeyPattern().IsMatch(request.LicenseKey ?? string.Empty))
    {
        return Results.BadRequest(new { error = "Geçersiz anahtar biçimi (KC-XXXX-XXXX-XXXX)." });
    }

    if (string.IsNullOrWhiteSpace(request.Hwid))
    {
        return Results.BadRequest(new { error = "HWID gerekli." });
    }

    if (useDatabase)
    {
        var repository = http.RequestServices.GetRequiredService<LicenseRepository>();
        var outcome = await repository.ActivateAsync(
            request.LicenseKey!, request.Hwid!, request.MachineName,
            http.Connection.RemoteIpAddress?.ToString());

        if (!outcome.Success || outcome.License == null || outcome.Device == null)
        {
            return Results.Json(new { error = outcome.Error }, statusCode: outcome.StatusCode);
        }

        var expiresAt = DateTimeOffset.UtcNow.AddHours(2);
        var dbToken = CreateToken(new LicenseRecord
        {
            Key = outcome.License.Key,
            Hwid = outcome.Device.Hwid,
            Plan = outcome.License.Plan,
            Credits = outcome.License.Credits
        }, secret, expiresAt);

        app.Logger.LogInformation(
            "Aktivasyon (DB): {Key} · HWID {Hwid} · {Credits} kredi",
            outcome.License.Key, outcome.Device.Hwid, outcome.License.Credits);

        return Results.Ok(new
        {
            token = dbToken,
            expiresAt,
            plan = outcome.License.Plan,
            credits = outcome.License.Credits,
            message = "Lisans etkinleştirildi."
        });
    }

    if (!store.TryGet(request.LicenseKey!, out var license))
    {
        return Results.NotFound(new { error = "Lisans bulunamadı." });
    }

    lock (license)
    {
        if (!string.IsNullOrWhiteSpace(license.Hwid) &&
            !string.Equals(license.Hwid, request.Hwid, StringComparison.OrdinalIgnoreCase))
        {
            return Results.Conflict(new { error = "Lisans başka bir makineye bağlı." });
        }

        if (license.Credits <= 0)
        {
            return Results.Conflict(new { error = "Kredi tükendi." });
        }

        license.Hwid = request.Hwid;
        license.LastSeen = DateTimeOffset.UtcNow;

        var expires = DateTimeOffset.UtcNow.AddHours(2);
        var token = CreateToken(license, secret, expires);

        app.Logger.LogInformation(
            "Aktivasyon (bellek): {Key} · HWID {Hwid} · {Credits} kredi",
            license.Key, license.Hwid, license.Credits);

        return Results.Ok(new
        {
            token,
            expiresAt = expires,
            plan = license.Plan,
            credits = license.Credits,
            message = "Lisans etkinleştirildi."
        });
    }
});
app.MapGet("/api/v1/license/status", async Task<IResult> (HttpContext http) =>
{
    if (!TryReadToken(http.Request, out var token) || !TryValidate(token, secret, out var payload))
    {
        return Results.Unauthorized();
    }

    var key = payload.TryGetProperty("sub", out var sub) ? sub.GetString() : null;

    if (string.IsNullOrWhiteSpace(key))
    {
        return Results.Unauthorized();
    }

    if (useDatabase)
    {
        var hwid = payload.TryGetProperty("hwid", out var hwidElement)
            ? hwidElement.GetString()
            : null;

        var repository = http.RequestServices.GetRequiredService<LicenseRepository>();
        var outcome = await repository.ValidateAsync(key, hwid ?? string.Empty);

        if (!outcome.Success || outcome.License == null)
        {
            return Results.Json(new { error = outcome.Error }, statusCode: outcome.StatusCode);
        }

        return Results.Ok(new
        {
            valid = true,
            plan = outcome.License.Plan,
            credits = outcome.License.Credits,
            expiresAt = outcome.License.EndsAt,
            hwid = outcome.Device?.Hwid
        });
    }

    if (!store.TryGet(key, out var license))
    {
        return Results.NotFound(new { error = "Lisans bulunamadı." });
    }

    lock (license)
    {
        license.LastSeen = DateTimeOffset.UtcNow;

        var expires = payload.TryGetProperty("exp", out var exp)
            ? DateTimeOffset.FromUnixTimeSeconds(exp.GetInt64())
            : DateTimeOffset.UtcNow.AddHours(2);

        return Results.Ok(new
        {
            valid = true,
            plan = license.Plan,
            credits = license.Credits,
            expiresAt = expires,
            hwid = license.Hwid
        });
    }
});
app.MapPost("/api/v1/license/consume", async Task<IResult> (ConsumeRequest request, HttpContext http) =>
{
    if (!TryReadToken(http.Request, out var token) || !TryValidate(token, secret, out var payload))
    {
        return Results.Unauthorized();
    }

    if (request.Amount <= 0)
    {
        return Results.BadRequest(new { error = "Miktar 0'dan büyük olmalı." });
    }

    var key = payload.TryGetProperty("sub", out var sub) ? sub.GetString() : null;

    if (string.IsNullOrWhiteSpace(key))
    {
        return Results.Unauthorized();
    }

    if (useDatabase)
    {
        var hwid = payload.TryGetProperty("hwid", out var hwidElement)
            ? hwidElement.GetString()
            : null;

        var repository = http.RequestServices.GetRequiredService<LicenseRepository>();
        var outcome = await repository.ConsumeAsync(
            key, hwid ?? string.Empty, request.Amount, request.Operation,
            http.Connection.RemoteIpAddress?.ToString());

        if (!outcome.Success)
        {
            return Results.Json(new { error = outcome.Error, credits = outcome.Credits },
                statusCode: outcome.StatusCode);
        }

        return Results.Ok(new
        {
            credits = outcome.Credits,
            message = request.Operation + " için " + request.Amount + " kredi düşüldü."
        });
    }

    if (!store.TryGet(key, out var license))
    {
        return Results.NotFound(new { error = "Lisans bulunamadı." });
    }

    lock (license)
    {
        if (license.Credits < request.Amount)
        {
            return Results.Conflict(new { error = "Yetersiz kredi.", credits = license.Credits });
        }

        license.Credits -= request.Amount;
        license.LastSeen = DateTimeOffset.UtcNow;

        app.Logger.LogInformation(
            "Kredi: -{Amount} ({Operation}) · {Key} → {Credits}",
            request.Amount, request.Operation, license.Key, license.Credits);

        return Results.Ok(new
        {
            credits = license.Credits,
            message = request.Operation + " için " + request.Amount + " kredi düşüldü."
        });
    }
});
// ----------------------------- OpenID Connect + entitlement -----------------------------

if (useDatabase)
{
app.MapGet("/connect/authorize", async Task<IResult> (HttpContext http) =>
{
    var request = Microsoft.AspNetCore.OpenIddictServerAspNetCoreHelpers
        .GetOpenIddictServerRequest(http);

    if (request == null)
    {
        return Results.BadRequest(new { error = "Geçersiz yetkilendirme isteği." });
    }

    var result = await http.AuthenticateAsync(IdentityConstants.ApplicationScheme);

    if (!result.Succeeded || result.Principal == null)
    {
        return Results.Challenge(
            new AuthenticationProperties
            {
                RedirectUri = http.Request.Path + http.Request.QueryString
            },
            new[] { IdentityConstants.ApplicationScheme });
    }

    var users = http.RequestServices.GetRequiredService<UserManager<AppUser>>();
    var user = await users.GetUserAsync(result.Principal);

    if (user == null || !user.IsActive)
    {
        return Results.Challenge(
            new AuthenticationProperties { RedirectUri = "/Account/Login" },
            new[] { IdentityConstants.ApplicationScheme });
    }

    var identity = new ClaimsIdentity(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    identity.AddClaim(OpenIddictConstants.Claims.Subject, user.Id.ToString());
    identity.AddClaim(OpenIddictConstants.Claims.Name, user.UserName ?? user.Email ?? "user");
    identity.AddClaim(OpenIddictConstants.Claims.Email, user.Email ?? string.Empty);
    identity.AddClaim(OpenIddictConstants.Claims.PreferredUsername, user.UserName ?? string.Empty);

    var principal = new ClaimsPrincipal(identity);
    principal.SetScopes(request.GetScopes());
    principal.SetResources("kctools-api");

    return Results.SignIn(
        principal,
        authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
});
app.MapGet("/api/v1/me",
    async Task<IResult> (ClaimsPrincipal principal, [FromServices] LicenseDbContext database) =>
{
    var subject = principal.FindFirst(OpenIddictConstants.Claims.Subject)?.Value
                  ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    if (!Guid.TryParse(subject, out var userId))
    {
        return Results.Unauthorized();
    }

    var user = await database.Users.FindAsync(userId);

    if (user == null || !user.IsActive)
    {
        return Results.Unauthorized();
    }

    return Results.Ok(new
    {
        id = user.Id,
        email = user.Email,
        name = user.DisplayName ?? user.UserName,
        createdAt = user.CreatedAt
    });
}).RequireAuthorization(policy => policy
    .AddAuthenticationSchemes(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)
    .RequireAuthenticatedUser());

app.MapGet("/api/v1/me/license",
    async Task<IResult> (
        ClaimsPrincipal principal, [FromServices] LicenseDbContext database,
        OfflineEntitlementService offline) =>
{
    var subject = principal.FindFirst(OpenIddictConstants.Claims.Subject)?.Value
                  ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    if (!Guid.TryParse(subject, out var userId))
    {
        return Results.Unauthorized();
    }

    var user = await database.Users.FindAsync(userId);

    if (user == null || !user.IsActive)
    {
        return Results.Unauthorized();
    }

    var licenses = await database.Licenses
        .Include(item => item.Devices)
        .Where(item => item.UserId == userId)
        .ToListAsync();

    var license = licenses.OrderByDescending(item => item.EndsAt).FirstOrDefault();

    if (license == null)
    {
        return Results.Ok(new { allowed = false, status = "none", reason = "Hesaba bağlı lisans yok." });
    }

    var now = DateTimeOffset.UtcNow;

    var status = license.IsRevoked ? "revoked"
        : license.IsFrozen ? "frozen"
        : now > license.EndsAt ? "expired"
        : now < license.StartsAt ? "pending"
        : "active";

    var device = license.Devices.FirstOrDefault(item => item.IsActive);

    var offlineHours = Math.Clamp(configuration.OfflineHours, 1, 168);

    var offlineToken = status == "active"
        ? offline.Issue(license, device, userId, offlineHours)
        : null;

    return Results.Ok(new
    {
        allowed = status == "active",
        status,
        reason = status == "active" ? null : "Lisans kullanılamıyor: " + status,
        user = new { id = user.Id, email = user.Email, name = user.DisplayName },
        license = new
        {
            key = license.Key,
            plan = license.Plan,
            credits = license.Credits,
            startsAt = license.StartsAt,
            endsAt = license.EndsAt,
            maxDevices = license.MaxDevices,
            isRevoked = license.IsRevoked,
            isFrozen = license.IsFrozen
        },
        device = device == null
            ? null
            : new
            {
                deviceId = device.DeviceId,
                hwid = device.Hwid,
                machineName = device.MachineName,
                activatedAt = device.ActivatedAt,
                thumbprint = device.KeyThumbprint
            },
        offlineToken,
        offlineHours
    });
}).RequireAuthorization(policy => policy
    .AddAuthenticationSchemes(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)
    .RequireAuthenticatedUser());

app.MapPost("/api/v1/auth/authorize-job",
    async Task<IResult> (
        JobAuthorizationRequest request, ClaimsPrincipal principal,
        [FromServices] LicenseDbContext database, [FromServices] LicenseRepository repository,
        [FromServices] OfflineEntitlementService offline, HttpContext http) =>
{
    var subject = principal.FindFirst(OpenIddictConstants.Claims.Subject)?.Value
                  ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    if (!Guid.TryParse(subject, out var userId))
    {
        return Results.Unauthorized();
    }

    var user = await database.Users.FindAsync(userId);

    if (user == null || !user.IsActive)
    {
        return Results.Unauthorized();
    }

    var hwid = (request.Hwid ?? string.Empty).Trim();
    var chipset = (request.Chipset ?? string.Empty).Trim().ToUpperInvariant();

    if (hwid.Length < 8 || chipset.Length < 2)
    {
        return Results.BadRequest(new { error = "HWID ve chipset zorunlu." });
    }

    var licenses = await database.Licenses
        .Include(item => item.Devices)
        .Where(item => item.UserId == userId)
        .ToListAsync();

    var now = DateTimeOffset.UtcNow;

    var license = licenses.FirstOrDefault(item =>
        !item.IsRevoked && !item.IsFrozen && now >= item.StartsAt && now <= item.EndsAt &&
        item.Devices.Any(device => device.IsActive &&
            string.Equals(device.Hwid, hwid, StringComparison.OrdinalIgnoreCase)));

    if (license == null)
    {
        return Results.Json(
            new { error = "Aktif lisans ve eşleşen cihaz bulunamadı." },
            statusCode: 403);
    }

    var jobId = Guid.NewGuid();

    var token = offline.IssueJobToken(
        userId, hwid, chipset, request.Operation ?? "flash",
        TimeSpan.FromMinutes(15), out var expiresAt);

    await repository.LogAuditAsync(
        "device:" + hwid, "auth.authorize-job", "License", license.Key,
        chipset + " · " + jobId, http.Connection.RemoteIpAddress?.ToString());

    return Results.Ok(new
    {
        jobId,
        token,
        expiresAt,
        chipset,
        hwid
    });
}).RequireAuthorization(policy => policy
    .AddAuthenticationSchemes(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)
    .RequireAuthenticatedUser());
}

app.MapGet("/api/v1/license/offline-public-key",
    (OfflineEntitlementService offline) => Results.Text(offline.PublicKeyPem, "text/plain"));
// ----------------------------- cihaz anahtarı -----------------------------

app.MapPost("/api/v1/devices/challenge",
    async Task<IResult> (DeviceChallengeRequest request, HttpContext http) =>
{
    if (!useDatabase)
    {
        return Results.Json(new { error = "Veritabanı yapılandırılmadı." }, statusCode: 503);
    }

    var repository = http.RequestServices.GetRequiredService<LicenseRepository>();
    var outcome = await repository.CreateChallengeAsync(
        request.LicenseKey ?? string.Empty, request.DeviceId, request.PublicKey ?? string.Empty);

    if (outcome.Nonce == null)
    {
        return Results.Json(new { error = outcome.Error }, statusCode: outcome.StatusCode);
    }

    return Results.Ok(new
    {
        nonce = outcome.Nonce,
        expiresAt = outcome.ExpiresAt,
        algorithm = DeviceProof.SignatureAlgorithm
    });
});

app.MapPost("/api/v1/devices/activate",
    async Task<IResult> (DeviceActivateRequest request, HttpContext http) =>
{
    if (!useDatabase)
    {
        return Results.Json(new { error = "Veritabanı yapılandırılmadı." }, statusCode: 503);
    }

    var repository = http.RequestServices.GetRequiredService<LicenseRepository>();
    var outcome = await repository.ActivateDeviceAsync(
        request.LicenseKey ?? string.Empty, request.DeviceId, request.Hwid ?? string.Empty,
        request.MachineName, request.PublicKey ?? string.Empty, request.Nonce ?? string.Empty,
        request.Signature ?? string.Empty, http.Connection.RemoteIpAddress?.ToString());

    if (!outcome.Success || outcome.License == null || outcome.Device == null)
    {
        return Results.Json(new { error = outcome.Error }, statusCode: outcome.StatusCode);
    }

    return Results.Ok(new
    {
        deviceId = outcome.Device.DeviceId,
        licenseKey = outcome.License.Key,
        plan = outcome.License.Plan,
        credits = outcome.License.Credits,
        activatedAt = outcome.Device.ActivatedAt,
        endsAt = outcome.License.EndsAt,
        thumbprint = outcome.Device.KeyThumbprint
    });
});

app.MapPost("/api/v1/admin/devices/{deviceId:guid}/reset",
    async Task<IResult> (Guid deviceId, HttpContext http) =>
{
    if (!IsAdmin(http.Request))
    {
        return Results.Unauthorized();
    }

    var repository = http.RequestServices.GetRequiredService<LicenseRepository>();
    var (ok, error) = await repository.ResetDeviceAsync(
        deviceId, "admin", http.Connection.RemoteIpAddress?.ToString());

    return ok ? Results.Ok(new { reset = true }) : Results.NotFound(new { error });
});

app.MapGet("/api/v1/admin/activation-history",
    async Task<IResult> (HttpContext http, string? licenseKey, int take = 100) =>
{
    if (!IsAdmin(http.Request))
    {
        return Results.Unauthorized();
    }

    var repository = http.RequestServices.GetRequiredService<LicenseRepository>();
    var items = await repository.GetActivationHistoryAsync(licenseKey, take);

    return Results.Ok(items.Select(item => new
    {
        item.CreatedAt,
        item.LicenseId,
        item.DeviceId,
        item.Hwid,
        item.Action,
        item.Actor,
        item.Details
    }));
});
// ----------------------------- kullanıcı / token -----------------------------

app.MapPost("/api/v1/auth/register", async Task<IResult> (RegisterRequest request, HttpContext http) =>
{
    if (!useDatabase)
    {
        return Results.Json(new { error = "Veritabanı yapılandırılmadı." }, statusCode: 503);
    }

    var email = (request.Username ?? string.Empty).Trim();

    if (email.Length < 3 || !email.Contains('@'))
    {
        return Results.BadRequest(new { error = "Geçerli bir e-posta girin." });
    }

    var users = http.RequestServices.GetRequiredService<UserManager<AppUser>>();

    if (await users.FindByEmailAsync(email) != null)
    {
        return Results.Conflict(new { error = "Bu e-posta zaten kayıtlı." });
    }

    var user = new AppUser
    {
        UserName = email,
        Email = email,
        DisplayName = email
    };

    var created = await users.CreateAsync(user, request.Password ?? string.Empty);

    if (!created.Succeeded)
    {
        return Results.Conflict(new
        {
            error = string.Join(" ", created.Errors.Select(item => item.Description))
        });
    }

    await users.AddToRoleAsync(user, AppRoles.Customer);

    var repository = http.RequestServices.GetRequiredService<LicenseRepository>();
    await repository.LogAuditAsync(
        "user:" + email, "auth.register", "User", user.Id.ToString(), null,
        http.Connection.RemoteIpAddress?.ToString());

    return Results.Created($"/api/v1/users/{user.Id}", new
    {
        id = user.Id,
        username = user.Email,
        role = AppRoles.Customer
    });
});

app.MapPost("/api/v1/auth/login", async Task<IResult> (LoginRequest request, HttpContext http) =>
{
    if (!useDatabase)
    {
        return Results.Json(new { error = "Veritabanı yapılandırılmadı." }, statusCode: 503);
    }

    var users = http.RequestServices.GetRequiredService<UserManager<AppUser>>();
    var user = await users.FindByNameAsync(request.Username ?? string.Empty)
               ?? await users.FindByEmailAsync(request.Username ?? string.Empty);

    if (user == null || !user.IsActive)
    {
        return Results.Unauthorized();
    }

    if (await users.IsLockedOutAsync(user))
    {
        return Results.Json(
            new { error = "Hesap geçici olarak kilitli. Daha sonra tekrar deneyin." },
            statusCode: 423);
    }

    if (!await users.CheckPasswordAsync(user, request.Password ?? string.Empty))
    {
        await users.AccessFailedAsync(user);
        return Results.Unauthorized();
    }

    await users.ResetAccessFailedCountAsync(user);

    user.LastLoginAt = DateTimeOffset.UtcNow;
    await users.UpdateAsync(user);

    var repository = http.RequestServices.GetRequiredService<LicenseRepository>();
    var ip = http.Connection.RemoteIpAddress?.ToString();
    var (raw, expires, error) = await repository.CreateRefreshTokenAsync(user, ip);

    if (raw == null)
    {
        return Results.Problem(error);
    }

    await repository.LogAuditAsync(
        "user:" + user.Email, "auth.login", "User", user.Id.ToString(), null, ip);

    var roles = await users.GetRolesAsync(user);

    return Results.Ok(new
    {
        userId = user.Id,
        username = user.UserName,
        role = roles.FirstOrDefault() ?? AppRoles.Customer,
        refreshToken = raw,
        refreshExpiresAt = expires
    });
});
app.MapPost("/api/v1/auth/refresh", async Task<IResult> (RefreshRequest request, HttpContext http) =>
{
    if (!useDatabase)
    {
        return Results.Json(new { error = "Veritabanı yapılandırılmadı." }, statusCode: 503);
    }

    var repository = http.RequestServices.GetRequiredService<LicenseRepository>();
    var (user, raw, expires, error) = await repository.RotateRefreshTokenAsync(
        request.RefreshToken ?? string.Empty,
        http.Connection.RemoteIpAddress?.ToString());

    if (user == null || raw == null)
    {
        return Results.Unauthorized();
    }

    return Results.Ok(new
    {
        userId = user.Id,
        refreshToken = raw,
        refreshExpiresAt = expires
    });
});

// ----------------------------- yönetici -----------------------------

app.MapPost("/api/v1/admin/licenses", async Task<IResult> (AdminCreateLicenseRequest request, HttpContext http) =>
{
    if (!IsAdmin(http.Request))
    {
        return Results.Unauthorized();
    }

    var repository = http.RequestServices.GetRequiredService<LicenseRepository>();
    var (license, error) = await repository.CreateLicenseAsync(
        request.Key, request.Plan ?? "PRO", request.Credits, request.Days,
        request.UserId, "admin", http.Connection.RemoteIpAddress?.ToString());

    if (license == null)
    {
        return Results.Conflict(new { error });
    }

    return Results.Created($"/api/v1/admin/licenses/{license.Key}", new
    {
        key = license.Key,
        plan = license.Plan,
        credits = license.Credits,
        startsAt = license.StartsAt,
        endsAt = license.EndsAt,
        maxDevices = license.MaxDevices
    });
});

app.MapPost("/api/v1/admin/licenses/{key}/revoke",
    async Task<IResult> (string key, RevokeRequest request, HttpContext http) =>
{
    if (!IsAdmin(http.Request))
    {
        return Results.Unauthorized();
    }

    var repository = http.RequestServices.GetRequiredService<LicenseRepository>();
    var (ok, error) = await repository.RevokeLicenseAsync(
        key, request.Reason, "admin", http.Connection.RemoteIpAddress?.ToString());

    return ok ? Results.Ok(new { revoked = true }) : Results.NotFound(new { error });
});

app.MapGet("/api/v1/admin/audit", async Task<IResult> (HttpContext http, int take = 100) =>
{
    if (!IsAdmin(http.Request))
    {
        return Results.Unauthorized();
    }

    var repository = http.RequestServices.GetRequiredService<LicenseRepository>();
    var items = await repository.GetAuditAsync(take);

    return Results.Ok(items.Select(item => new
    {
        item.CreatedAt,
        item.Actor,
        item.Action,
        item.EntityType,
        item.EntityId,
        item.Details,
        item.IpAddress
    }));
});

bool IsAdmin(HttpRequest http)
{
    var expected = configuration.AdminKey;

    return !string.IsNullOrWhiteSpace(expected) &&
           http.Headers.TryGetValue("X-Admin-Key", out var provided) &&
           string.Equals(provided.ToString(), expected, StringComparison.Ordinal);
}
app.Logger.LogInformation(
    "KC Tools lisans sunucusu {Scheme} :{Port} üzerinde dinliyor · veritabanı: {Mode}",
    allowHttp ? "HTTP" : "HTTPS", port, useDatabase ? "PostgreSQL" : "bellek içi");

app.Run();

// ----------------------------- yardımcılar -----------------------------

static Regex KeyPattern()
{
    return new Regex(
        "^KC-[A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{4}$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
}

static string CreateToken(LicenseRecord record, string secret, DateTimeOffset expires)
{
    var header = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new { alg = "HS256", typ = "JWT" }));

    var now = DateTimeOffset.UtcNow;

    var payload = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new
    {
        sub = record.Key,
        hwid = record.Hwid,
        plan = record.Plan,
        credits = record.Credits,
        iss = "kctools-license",
        iat = now.ToUnixTimeSeconds(),
        exp = expires.ToUnixTimeSeconds(),
        jti = Guid.NewGuid().ToString("N")
    }));

    var signature = Base64Url(Hmac(secret, header + "." + payload));

    return header + "." + payload + "." + signature;
}

static bool TryValidate(string token, string secret, out JsonElement payload)
{
    payload = default;

    var parts = token.Split('.');

    if (parts.Length != 3)
    {
        return false;
    }

    var expected = Base64Url(Hmac(secret, parts[0] + "." + parts[1]));

    if (!CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(parts[2])))
    {
        return false;
    }

    try
    {
        using var document = JsonDocument.Parse(Base64UrlDecode(parts[1]));
        var root = document.RootElement;

        if (!root.TryGetProperty("exp", out var exp) ||
            exp.GetInt64() < DateTimeOffset.UtcNow.ToUnixTimeSeconds())
        {
            return false;
        }

        payload = root.Clone();
        return true;
    }
    catch (Exception)
    {
        return false;
    }
}

static bool TryReadToken(HttpRequest request, out string token)
{
    token = string.Empty;
    var header = request.Headers.Authorization.ToString();

    if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
    {
        return false;
    }

    token = header.Substring(7).Trim();
    return token.Length > 0;
}

static byte[] Hmac(string secret, string payload)
{
    using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
    return hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
}

static string Base64Url(byte[] data)
{
    return Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

static byte[] Base64UrlDecode(string value)
{
    var text = value.Replace('-', '+').Replace('_', '/');

    switch (text.Length % 4)
    {
        case 2:
            text += "==";
            break;

        case 3:
            text += "=";
            break;
    }

    return Convert.FromBase64String(text);
}

sealed record ActivateRequest(string? Hwid, string? LicenseKey, string? AppVersion, string? MachineName);

sealed record RegisterRequest(string? Username, string? Password);

sealed record LoginRequest(string? Username, string? Password);

sealed record RefreshRequest(string? RefreshToken);

sealed record DeviceChallengeRequest(string? LicenseKey, Guid DeviceId, string? PublicKey);

sealed record JobAuthorizationRequest(
    string? Hwid, string? Serial, string? Chipset, string? Operation);

sealed record DeviceActivateRequest(
    string? LicenseKey, Guid DeviceId, string? Hwid, string? MachineName,
    string? PublicKey, string? Nonce, string? Signature);

sealed record AdminCreateLicenseRequest(
    string? Key, string? Plan, int Credits, int Days, Guid? UserId);

sealed record RevokeRequest(string? Reason);

sealed record ConsumeRequest(int Amount, string? Operation);

sealed class LicenseRecord
{
    public string Key { get; init; } = string.Empty;

    public string Plan { get; init; } = "PRO";

    public int Credits { get; set; }

    public string? Hwid { get; set; }

    public DateTimeOffset? LastSeen { get; set; }
}

sealed class LicenseStore
{
    private readonly ConcurrentDictionary<string, LicenseRecord> _records =
        new ConcurrentDictionary<string, LicenseRecord>(StringComparer.OrdinalIgnoreCase);

    public void Seed(string key, string plan, int credits)
    {
        _records.TryAdd(key, new LicenseRecord { Key = key, Plan = plan, Credits = credits });
    }

    public bool TryGet(string key, out LicenseRecord record)
    {
        if (_records.TryGetValue(key, out var found))
        {
            record = found;
            return true;
        }

        record = null!;
        return false;
    }
}
