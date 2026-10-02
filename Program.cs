using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
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
using System.Data;
using System.Text;

var dotEnv = LoadDotEnvConfiguration();
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
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});


// ============================================================
// DATABASE CONNECTION
// ============================================================

var connectionString =
    builder.Configuration.GetConnectionString("Postgres")
    ?? builder.Configuration["ConnectionStrings:Postgres"]
    ?? builder.Configuration["ConnectionStrings__Postgres"]
    ?? builder.Configuration["PROMAP_POSTGRES"]
    ?? GetDotEnvValue(dotEnv, "ConnectionStrings__Postgres")
    ?? GetDotEnvValue(dotEnv, "PROMAP_POSTGRES");

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "PostgreSQL connection string is not configured. Configure User Secrets or the .env file.");
}

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
            .UseSnakeCaseNamingConvention()
            .LogTo(
                Console.WriteLine,
                [DbLoggerCategory.Database.Command.Name],
                LogLevel.Information));


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

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.ReturnUrlParameter = "returnUrl";
    options.SlidingExpiration = true;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.Cookie.Name = "ProMapCargo.Identity";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
});

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

var permissionPolicies = new[]
{
    "users.read",
    "users.manage",
    "roles.manage",
    "permissions.manage",
    "dispatch.manage",
    "trips.manage",
    "drivers.manage",
    "vehicles.manage",
    "orders.manage",
    "navigation.live",
    "alerts.manage",
    "compliance.manage",
    "finance.manage",
    "audit.read",
    "settings.manage"
};

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

    foreach (var permission in permissionPolicies)
    {
        options.AddPolicy(permission, policy =>
            policy.RequireAssertion(context =>
                context.User.IsInRole("Administrator") ||
                context.User.HasClaim("permission", permission)));
    }
});


// ============================================================
// APPLICATION SERVICES
// ============================================================

builder.Services.AddScoped<ICurrentUserContext, CurrentUserContext>();
builder.Services.AddSingleton<UserPresenceTracker>();
builder.Services.AddScoped<IUserClaimsPrincipalFactory<ApplicationUser>, IdentityClaimsFactory>();
builder.Services.AddSingleton<IIdentityEmailSender, LoggingIdentityEmailSender>();
builder.Services.AddScoped<BusinessService>();
builder.Services.AddScoped<MobileTokenService>();
builder.Services.AddScoped<IIdentityEmailSender, LoggingIdentityEmailSender>();
builder.Services.AddHttpClient<IIPAddressLocationService, IpAddressLocationService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(8);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("ProMapCargo/1.0");
});

// adding admin services
builder.Services.AddScoped<IAccountAdminService, AccountAdminService>();


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
    client.Timeout = TimeSpan.FromSeconds(40);
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
builder.Services.AddScoped<BusinessService>();


// ============================================================
// CORS
// ============================================================

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        policy.WithOrigins("http://localhost:3000", "http://localhost:5173");
        policy.AllowAnyMethod();
        policy.AllowAnyHeader();
        policy.AllowCredentials();
    });
});


var app = builder.Build();
var startupLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup.Database");
startupLogger.LogInformation("Resolved PostgreSQL target: {DatabaseTarget}", DescribeConnectionTarget(connectionString));
await InitializeDatabaseAsync(app);
await SeedDefaultMobileUserAsync(app);
await SeedAdministratorUserAsync(app);

// ============================================================
// MIDDLEWARE
// ============================================================

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.UseExceptionHandler("/error");
app.UseStatusCodePages();
app.UseHsts();
app.UseForwardedHeaders();

// ============================================================
// STATIC FILES
// ============================================================

var provider = new FileExtensionContentTypeProvider();
provider.Mappings[".pmtiles"] = "application/octet-stream";
provider.Mappings[".pbf"] = "application/octet-stream";

app.UseStaticFiles(new StaticFileOptions
{
    ContentTypeProvider = provider
});

// ============================================================
// ROUTING, CORS & AUTH
// ============================================================

app.UseCors("Frontend");
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

// ============================================================
// MIDDLEWARE - Razor Pages & SignalR
// ============================================================

app.MapControllers();
app.MapRazorPages();
app.MapHub<NavigationHub>("/hubs/navigation-telemetry");

// ============================================================
// PMTiles Serving
// ============================================================

app.Map("/pmtiles/{*path}", pmTilesApp =>
{
    pmTilesApp.Run(async (context) =>
    {
        var path = context.Request.RouteValues["path"]?.ToString() ?? "";
        var filePath = Path.Combine(AppContext.BaseDirectory, "map", path);

        if (!System.IO.File.Exists(filePath))
        {
            context.Response.StatusCode = 404;
            await context.Response.WriteAsync("PMTiles file not found");
            return;
        }

        context.Response.ContentType = "application/octet-stream";
        await context.Response.SendFileAsync(filePath);
    });
});

app.Run();

static async Task InitializeDatabaseAsync(WebApplication app)
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<ProMapCargoDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
        .CreateLogger("DatabaseInitialization");

    var hasMigrations = db.Database.GetMigrations().Any();
    var appliedMigrations = hasMigrations
        ? (await db.Database.GetAppliedMigrationsAsync().ConfigureAwait(false)).Any()
        : false;
    var schemaExists = await SchemaExistsAsync(db).ConfigureAwait(false);

    if (hasMigrations && (appliedMigrations || !schemaExists))
    {
        await db.Database.MigrateAsync().ConfigureAwait(false);
    }
    else if (!schemaExists)
    {
        await db.Database.EnsureCreatedAsync().ConfigureAwait(false);
    }
    else if (hasMigrations)
    {
        // The schema predates migrations (created by EnsureCreated). Applying
        // migrations here would attempt to recreate existing objects, so the
        // migration history has to be baselined manually instead.
        logger.LogWarning(
            "Skipping MigrateAsync: the database schema already exists but has no migration history. Baseline the migration history before enabling migrations.");
    }

    await db.Database.ExecuteSqlRawAsync("""
        ALTER TABLE IF EXISTS "AspNetUsers"
        ADD COLUMN IF NOT EXISTS profile_image_url text;

        ALTER TABLE IF EXISTS asp_net_users
        ADD COLUMN IF NOT EXISTS profile_image_url text;
        """).ConfigureAwait(false);
}

static async Task<bool> SchemaExistsAsync(ProMapCargoDbContext db)
{
    // The connection is taken from the DbContext because
    // Database.GetConnectionString() omits the password.
    var connection = db.Database.GetDbConnection();
    var wasClosed = connection.State != ConnectionState.Open;

    if (wasClosed)
    {
        await connection.OpenAsync().ConfigureAwait(false);
    }

    try
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT to_regclass('public."AspNetUsers"') IS NOT NULL
                OR to_regclass('public.asp_net_users') IS NOT NULL;
            """;

        var result = await command.ExecuteScalarAsync().ConfigureAwait(false);
        return result is bool exists && exists;
    }
    finally
    {
        if (wasClosed)
        {
            await connection.CloseAsync().ConfigureAwait(false);
        }
    }
}

static async Task SeedDefaultMobileUserAsync(WebApplication app)
{
    const string email = "user@user.com";
    const string password = "Abcd1234!";

    using var scope = app.Services.CreateScope();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

    var user = await userManager.FindByEmailAsync(email).ConfigureAwait(false)
               ?? await userManager.FindByNameAsync(email).ConfigureAwait(false);

    if (user is null)
    {
        user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            ProfileImageUrl = null,
            DisplayName = "ProMapCargo Mobile User",
            IsActive = true
        };

        var createResult = await userManager.CreateAsync(user, password).ConfigureAwait(false);
        if (!createResult.Succeeded)
        {
            var errors = string.Join("; ", createResult.Errors.Select(x => x.Description));
            throw new InvalidOperationException($"Failed to seed default mobile user '{email}': {errors}");
        }

        return;
    }

    var shouldUpdateUser = false;

    if (!string.Equals(user.UserName, email, StringComparison.OrdinalIgnoreCase))
    {
        user.UserName = email;
        shouldUpdateUser = true;
    }

    if (!string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase))
    {
        user.Email = email;
        shouldUpdateUser = true;
    }

    if (!user.IsActive)
    {
        user.IsActive = true;
        shouldUpdateUser = true;
    }

    if (shouldUpdateUser)
    {
        var updateResult = await userManager.UpdateAsync(user).ConfigureAwait(false);
        if (!updateResult.Succeeded)
        {
            var errors = string.Join("; ", updateResult.Errors.Select(x => x.Description));
            throw new InvalidOperationException($"Failed to update seeded mobile user '{email}': {errors}");
        }
    }

    var passwordValid = await userManager.CheckPasswordAsync(user, password).ConfigureAwait(false);
    if (passwordValid)
    {
        return;
    }

    var token = await userManager.GeneratePasswordResetTokenAsync(user).ConfigureAwait(false);
    var resetResult = await userManager.ResetPasswordAsync(user, token, password).ConfigureAwait(false);
    if (!resetResult.Succeeded)
    {
        var errors = string.Join("; ", resetResult.Errors.Select(x => x.Description));
        throw new InvalidOperationException($"Failed to reset password for seeded mobile user '{email}': {errors}");
    }
}

static async Task SeedAdministratorUserAsync(WebApplication app)
{
    const string roleName = "Administrator";
    const string email = "admin@promapcargo.com";
    const string password = "Admin1234!";

    using var scope = app.Services.CreateScope();
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

    if (!await roleManager.RoleExistsAsync(roleName).ConfigureAwait(false))
    {
        var roleResult = await roleManager.CreateAsync(new ApplicationRole
        {
            Name = roleName
        }).ConfigureAwait(false);

        if (!roleResult.Succeeded)
        {
            var errors = string.Join("; ", roleResult.Errors.Select(x => x.Description));
            throw new InvalidOperationException($"Failed to seed role '{roleName}': {errors}");
        }
    }

    var user = await userManager.FindByEmailAsync(email).ConfigureAwait(false)
               ?? await userManager.FindByNameAsync(email).ConfigureAwait(false);

    if (user is null)
    {
        user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            DisplayName = "System Administrator",
            IsActive = true
        };

        var createResult = await userManager.CreateAsync(user, password).ConfigureAwait(false);
        if (!createResult.Succeeded)
        {
            var errors = string.Join("; ", createResult.Errors.Select(x => x.Description));
            throw new InvalidOperationException($"Failed to seed admin user '{email}': {errors}");
        }
    }
    else
    {
        var shouldUpdateUser = false;

        if (!string.Equals(user.UserName, email, StringComparison.OrdinalIgnoreCase))
        {
            user.UserName = email;
            shouldUpdateUser = true;
        }

        if (!string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase))
        {
            user.Email = email;
            shouldUpdateUser = true;
        }

        if (!user.IsActive)
        {
            user.IsActive = true;
            shouldUpdateUser = true;
        }

        if (shouldUpdateUser)
        {
            var updateResult = await userManager.UpdateAsync(user).ConfigureAwait(false);
            if (!updateResult.Succeeded)
            {
                var errors = string.Join("; ", updateResult.Errors.Select(x => x.Description));
                throw new InvalidOperationException($"Failed to update admin user '{email}': {errors}");
            }
        }

        var passwordValid = await userManager.CheckPasswordAsync(user, password).ConfigureAwait(false);
        if (!passwordValid)
        {
            var token = await userManager.GeneratePasswordResetTokenAsync(user).ConfigureAwait(false);
            var resetResult = await userManager.ResetPasswordAsync(user, token, password).ConfigureAwait(false);
            if (!resetResult.Succeeded)
            {
                var errors = string.Join("; ", resetResult.Errors.Select(x => x.Description));
                throw new InvalidOperationException($"Failed to reset password for admin user '{email}': {errors}");
            }
        }
    }

    if (!await userManager.IsInRoleAsync(user, roleName).ConfigureAwait(false))
    {
        var addRoleResult = await userManager.AddToRoleAsync(user, roleName).ConfigureAwait(false);
        if (!addRoleResult.Succeeded)
        {
            var errors = string.Join("; ", addRoleResult.Errors.Select(x => x.Description));
            throw new InvalidOperationException($"Failed to assign role '{roleName}' to '{email}': {errors}");
        }
    }
}

static Dictionary<string, string> LoadDotEnvConfiguration()
{
    foreach (var startPath in new[]
             {
                 Directory.GetCurrentDirectory(),
                 AppContext.BaseDirectory
             })
    {
        var dotEnvPath = FindDotEnvPath(startPath);
        if (dotEnvPath is null)
        {
            continue;
        }

        return ParseDotEnv(dotEnvPath);
    }

    return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

static string? FindDotEnvPath(string startPath)
{
    if (string.IsNullOrWhiteSpace(startPath))
    {
        return null;
    }

    var directory = new DirectoryInfo(startPath);
    while (directory is not null)
    {
        var candidate = Path.Combine(directory.FullName, ".env");
        if (File.Exists(candidate))
        {
            return candidate;
        }

        directory = directory.Parent;
    }

    return null;
}

static Dictionary<string, string> ParseDotEnv(string path)
{
    var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    foreach (var rawLine in File.ReadLines(path))
    {
        var line = rawLine.Trim();
        if (line.Length == 0 || line.StartsWith('#'))
        {
            continue;
        }

        var separatorIndex = line.IndexOf('=');
        if (separatorIndex <= 0)
        {
            continue;
        }

        var key = line[..separatorIndex].Trim();
        var value = line[(separatorIndex + 1)..].Trim();

        if (value.Length >= 2)
        {
            var first = value[0];
            var last = value[^1];
            if ((first == '"' && last == '"') || (first == '\'' && last == '\''))
            {
                value = value[1..^1];
            }
        }

        values[key] = value;
    }

    return values;
}

static string? GetDotEnvValue(IReadOnlyDictionary<string, string> values, string key)
{
    return values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
        ? value
        : null;
}

static string DescribeConnectionTarget(string connectionString)
{
    ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

    try
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        return $"Host={builder.Host};Port={builder.Port};Database={builder.Database};Username={builder.Username}";
    }
    catch (ArgumentException)
    {
        return "Invalid connection string format";
    }
}
