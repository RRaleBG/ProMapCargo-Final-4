using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using ProMapCargo.Api.Data;
using ProMapCargo.Api.Models;
using ProMapCargo.Api.Routing;
using ProMapCargo.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// ============================================================
// ASP.NET CORE
// ============================================================

builder.Services.AddControllers();
builder.Services.AddMemoryCache();
builder.Services.AddRazorPages();
builder.Services.AddProblemDetails();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSignalR();


// ============================================================
// DATABASE CONNECTION
// ============================================================

var connectionString =
    builder.Configuration.GetConnectionString("Postgres")
    ?? "Host=localhost;Port=5432;Database=promapcargo;Username=promap;Password=promap_dev_change_me";


// ============================================================
// NPGSQL DATA SOURCE
// ============================================================

builder.Services.AddSingleton<NpgsqlDataSource>(_ =>
{
    var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
    dataSourceBuilder.UseNetTopologySuite();
    return dataSourceBuilder.Build();
});


// ============================================================
// ENTITY FRAMEWORK CORE
// ============================================================

builder.Services.AddDbContext<ProMapCargoDbContext>(
    options =>
        options
            .UseNpgsql(
                connectionString,
                npgsql => npgsql.UseNetTopologySuite())
            .UseSnakeCaseNamingConvention());


// ============================================================
// ASP.NET CORE IDENTITY
// ============================================================

builder.Services
    .AddIdentity<ApplicationUser, ApplicationRole>(
        options =>
        {
            options.User.RequireUniqueEmail = false;
            options.Password.RequireDigit = true;
            options.Password.RequireLowercase = true;
            options.Password.RequireUppercase = true;
            options.Password.RequireNonAlphanumeric = false;
            options.Password.RequiredLength = 8;
            options.SignIn.RequireConfirmedAccount = false;
        })
    .AddEntityFrameworkStores<ProMapCargoDbContext>()
    .AddDefaultTokenProviders();

builder.Services.Configure<MobileAuthOptions>(builder.Configuration.GetSection("MobileAuth"));
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<MobileAuthOptions>>().Value);

var mobileAuthOptions = builder.Configuration.GetSection("MobileAuth").Get<MobileAuthOptions>() ?? new MobileAuthOptions();
var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(mobileAuthOptions.SigningKey));

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = IdentityConstants.ApplicationScheme;
        options.DefaultChallengeScheme = IdentityConstants.ApplicationScheme;
    })
    .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = mobileAuthOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = mobileAuthOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = signingKey,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(2)
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("MobileBearer", policy =>
    {
        policy.AuthenticationSchemes.Add(JwtBearerDefaults.AuthenticationScheme);
        policy.RequireAuthenticatedUser();
    });

    options.AddPolicy("AppOrMobile", policy =>
    {
        policy.AuthenticationSchemes.Add(IdentityConstants.ApplicationScheme);
        policy.AuthenticationSchemes.Add(JwtBearerDefaults.AuthenticationScheme);
        policy.RequireAuthenticatedUser();
    });
});


// ============================================================
// APPLICATION SERVICES
// ============================================================

builder.Services.AddScoped<ICurrentUserContext, CurrentUserContext>();
builder.Services.AddScoped<IUserClaimsPrincipalFactory<ApplicationUser>, IdentityClaimsFactory>();
builder.Services.AddScoped<BusinessService>();
builder.Services.AddScoped<MobileTokenService>();


// ============================================================
// GEOCODING & ROUTING HTTP CLIENTS
// ============================================================

builder.Services.AddHttpClient<IGeocodingService, NominatimGeocodingService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(15);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("ProMapCargo/1.0");
});

builder.Services.AddHttpClient<IRoutingService, OsrmRoutingService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("ProMapCargo/1.0");
});

builder.Services.AddHttpClient("MapTiles", client =>
{
    client.Timeout = TimeSpan.FromSeconds(15);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("ProMapCargo/1.0");
});


// ============================================================
// ROAD RESTRICTIONS & POSTGIS ROUTING
// ============================================================

builder.Services.AddScoped<IPostgresRestrictionRepository, PostgresRestrictionRepository>();
builder.Services.AddScoped<IRestrictionEngine, PostgresRestrictionEngine>();

builder.Services.AddScoped<PostGisRoutingRepository>();
builder.Services.AddScoped<TurnRestrictionMatcher>();
builder.Services.AddScoped<ManeuverBuilder>();
builder.Services.AddScoped<TruckEdgeEvaluator>();
builder.Services.AddScoped<EdgeSnapper>();
builder.Services.AddScoped<PostGisAStarRouter>();
builder.Services.AddScoped<IPostGisRoutingService, PostGisRoutingService>();


// ============================================================
// CORS
// ============================================================

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        var origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>();

        if (origins is not null && origins.Length > 0)
        {
            policy.WithOrigins(origins)
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials();
            return;
        }

        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();


// ============================================================
// ERROR HANDLING
// ============================================================

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler();
}


// ============================================================
// SECURITY HEADERS (CSP)
// ============================================================

app.Use(async (context, next) =>
{
    // Dozvoljava samohostovane skripte/stilove, MapLibre Web Workers i lokalne font/glyph assete.
    context.Response.Headers.Append("Content-Security-Policy",
        "default-src 'self'; " +
        "script-src 'self' 'unsafe-inline' 'unsafe-eval'; " +
        "style-src 'self' 'unsafe-inline'; " +
        "font-src 'self' data:; " +
        "img-src 'self' data: blob:; " +
        "connect-src 'self' ws: wss: http: https:; " +
        "worker-src 'self' blob:; " +
        "child-src 'self' blob:;");

    await next();
});


// ============================================================
// STATIC FILES & PMTILES MIME TYPE
// ============================================================

var provider = new FileExtensionContentTypeProvider();
provider.Mappings[".pmtiles"] = "application/vnd.pmtiles";
provider.Mappings[".pbf"] = "application/x-protobuf";

app.UseStaticFiles(new StaticFileOptions
{
    ContentTypeProvider = provider
});

app.UseRouting();
app.UseCors("Frontend");
app.UseAuthentication();
app.UseAuthorization();


// ============================================================
// API CONTROLLERS & ENDPOINTS
// ============================================================

app.MapControllers();
app.MapHub<NavigationHub>("/hubs/navigation");
app.MapRazorPages();


// ============================================================
// DYNAMIC PMTILES ROUTE (Generičko slanje svih PMTiles mapa)
// ============================================================

app.MapGet("/maps/{region}.pmtiles", (string region, IWebHostEnvironment environment) =>
{
    var webRoot = string.IsNullOrWhiteSpace(environment.WebRootPath)
        ? Path.Combine(environment.ContentRootPath, "wwwroot")
        : environment.WebRootPath;

    var fileName = $"{region.ToLowerInvariant()}.pmtiles";
    var pmtilesPath = Path.Combine(webRoot, "maps", fileName);

    if (!File.Exists(pmtilesPath))
    {
        return Results.NotFound(new
        {
            error = $"PMTiles fajl '{fileName}' nije pronađen.",
            searchedPath = pmtilesPath
        });
    }

    return Results.File(
        pmtilesPath,
        contentType: "application/vnd.pmtiles",
        enableRangeProcessing: true);
});


// ============================================================
// DATABASE BOOTSTRAP & RUN
// ============================================================

await BootstrapAsync(app);
await app.RunAsync();


// ============================================================
// HELPER METHODS
// ============================================================

static async Task BootstrapAsync(WebApplication app)
{
    using var scope = app.Services.CreateScope();
    var services = scope.ServiceProvider;
    var db = services.GetRequiredService<ProMapCargoDbContext>();

    try
    {
        var canConnect = await db.Database.CanConnectAsync();
        if (!canConnect)
        {
            app.Logger.LogWarning("PostgreSQL database is not available. Database bootstrap will be skipped.");
            return;
        }

        await db.Database.EnsureCreatedAsync();
        await EnsureMobileRefreshTokenTableAsync(db, app.Logger);
        await ExecuteSqlFileAsync(app, db, "03-routing-graph.sql");
        await ExecuteSqlFileAsync(app, db, "04-operational-indexes.sql");

        var roleManager = services.GetRequiredService<RoleManager<ApplicationRole>>();
        var roles = new[] { "Administrator", "Dispatcher", "Moderator", "Driver", "FleetManager", "Viewer" };

        foreach (var roleName in roles)
        {
            if (await roleManager.RoleExistsAsync(roleName)) continue;

            var result = await roleManager.CreateAsync(new ApplicationRole { Name = roleName });
            if (!result.Succeeded)
            {
                var errors = string.Join(", ", result.Errors.Select(e => $"{e.Code}: {e.Description}"));
                app.Logger.LogWarning("Failed creating role {RoleName}: {Errors}", roleName, errors);
            }
        }

        app.Logger.LogInformation("ProMap Cargo database bootstrap completed.");
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Database bootstrap failed. API can still start; run SQL/migrations before routing.");
    }
}

static async Task EnsureMobileRefreshTokenTableAsync(ProMapCargoDbContext db, ILogger logger)
{
    const string sql = """
        CREATE TABLE IF NOT EXISTS mobile_refresh_tokens (
            id uuid PRIMARY KEY,
            user_id uuid NOT NULL,
            token text NOT NULL,
            expires_at timestamptz NOT NULL,
            created_at timestamptz NOT NULL,
            revoked_at timestamptz NULL,
            CONSTRAINT fk_mobile_refresh_tokens_users FOREIGN KEY (user_id) REFERENCES asp_net_users (id) ON DELETE CASCADE
        );

        CREATE UNIQUE INDEX IF NOT EXISTS ix_mobile_refresh_tokens_token ON mobile_refresh_tokens (token);
        CREATE INDEX IF NOT EXISTS ix_mobile_refresh_tokens_user_id_expires_at ON mobile_refresh_tokens (user_id, expires_at);
        """;

    try
    {
        await db.Database.ExecuteSqlRawAsync(sql);
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Failed creating mobile refresh token table.");
    }
}

static async Task ExecuteSqlFileAsync(WebApplication app, ProMapCargoDbContext db, string fileName)
{
    var sqlPath = Path.Combine(app.Environment.ContentRootPath, "Sql", fileName);

    if (!File.Exists(sqlPath))
    {
        app.Logger.LogWarning("SQL bootstrap file not found: {SqlFile}", sqlPath);
        return;
    }

    try
    {
        var sql = await File.ReadAllTextAsync(sqlPath);
        if (string.IsNullOrWhiteSpace(sql))
        {
            app.Logger.LogWarning("SQL bootstrap file is empty: {SqlFile}", sqlPath);
            return;
        }

        await db.Database.ExecuteSqlRawAsync(sql);
        app.Logger.LogInformation("Executed SQL bootstrap file: {SqlFile}", fileName);
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Failed executing SQL bootstrap file: {SqlFile}", sqlPath);
    }
}