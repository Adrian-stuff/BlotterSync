using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Text;
using BlotterSync.Models;
using BlotterSync.Profiles;

using Npgsql;

// Enable Npgsql legacy timestamp behavior so DateTime.Now works seamlessly with PostgreSQL
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var builder = WebApplication.CreateBuilder(args);

// Register Database Context (Supports Supabase PostgreSQL & SQL Server)
var rawConnectionString = Environment.GetEnvironmentVariable("DATABASE_URL")
    ?? builder.Configuration.GetConnectionString("DefaultConnection")
    ?? builder.Configuration.GetConnectionString("SupabaseConnection");

var configuredProvider = builder.Configuration.GetValue<string>("DatabaseProvider");
bool isPostgreSql = IsPostgreSql(configuredProvider, rawConnectionString);

if (isPostgreSql)
{
    var pgConnectionString = FormatNpgsqlConnectionString(rawConnectionString!);
    builder.Services.AddDbContext<BlotterSyncContext>(options =>
    {
        options.UseNpgsql(pgConnectionString, npgsqlOptions =>
        {
            npgsqlOptions.EnableRetryOnFailure(
                maxRetryCount: 5,
                maxRetryDelay: TimeSpan.FromSeconds(10),
                errorCodesToAdd: null);
        });
    });
}
else
{
    builder.Services.AddDbContext<BlotterSyncContext>(options =>
        options.UseSqlServer(rawConnectionString));
}

// Register AutoMapper
builder.Services.AddAutoMapper(config =>
{
    config.AddProfile<BlotterProfile>();
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", builder =>
        builder.AllowAnyOrigin()
               .AllowAnyMethod()
               .AllowAnyHeader());
});
// Configure JWT Authentication Middleware
var jwtSettings = builder.Configuration.GetSection("Jwt");
var jwtKey = jwtSettings["Key"];
// HMAC-SHA256 needs at least a 256-bit key; fail at startup instead of on the first login
if (string.IsNullOrWhiteSpace(jwtKey) || Encoding.UTF8.GetByteCount(jwtKey) < 32)
{
    throw new InvalidOperationException(
        "Jwt:Key must be configured with at least 32 bytes. Set it via user-secrets or the Jwt__Key environment variable.");
}
var key = Encoding.UTF8.GetBytes(jwtKey);

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSettings["Issuer"],
        ValidAudience = jwtSettings["Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(key),
        ClockSkew = TimeSpan.FromMinutes(1)
    };
});

builder.Services.AddScoped<IPasswordHasher<Officer>, PasswordHasher<Officer>>();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Configure Swagger to accept JWT Tokens
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "BlotterSync API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "Enter JWT Bearer token here",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            new string[] {}
        }
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Automatically ensure tables exist if configured (helpful for first-time Supabase setup)
if (app.Configuration.GetValue<bool>("Database:EnsureCreated", false))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<BlotterSyncContext>();
    try
    {
        db.Database.EnsureCreated();

        // Deduplicate and unify categories if duplicates exist from classmate scripts
        var duplicatesToMerge = new (string CanonicalName, int CanonicalSeverity, string[] DuplicateNames)[]
        {
            ("Theft / Robbery", 3, new[] { "Theft", "Theft / Robbery" }),
            ("Physical Injury / Assault", 4, new[] { "Physical Assault", "Physical Injury / Assault" }),
            ("Vandalism / Property Damage", 2, new[] { "Vandalism & Property Damage", "Vandalism / Property Damage" }),
            ("Disturbance of Peace / Noise", 1, new[] { "Noise Complaint", "Disturbance of Peace / Noise" })
        };

        foreach (var (canonicalName, severity, dupNames) in duplicatesToMerge)
        {
            var matching = db.Categories.Where(c => dupNames.Contains(c.Name)).ToList();
            if (matching.Count > 1)
            {
                var keep = matching.First();
                keep.Name = canonicalName;
                keep.SeverityLevel = severity;
                var toRemove = matching.Skip(1).ToList();
                foreach (var rem in toRemove)
                {
                    // Reassign any blotter records pointing to duplicate category
                    var recordsToReassign = db.BlotterRecords.Where(r => r.CategoryId == rem.CategoryId).ToList();
                    foreach (var rec in recordsToReassign)
                    {
                        rec.CategoryId = keep.CategoryId;
                    }
                    db.Categories.Remove(rem);
                }
                db.SaveChanges();
            }
            else if (matching.Count == 1 && matching[0].Name != canonicalName)
            {
                matching[0].Name = canonicalName;
                matching[0].SeverityLevel = severity;
                db.SaveChanges();
            }
        }

        // Ensure all standard incident categories are present
        var standardCategories = new (string Name, int Severity)[]
        {
            ("Theft / Robbery", 3),
            ("Physical Injury / Assault", 4),
            ("Domestic Dispute", 3),
            ("Vandalism / Property Damage", 2),
            ("Disturbance of Peace / Noise", 1),
            ("Harassment", 2),
            ("Trespassing", 2),
            ("Fraud / Estafa", 2),
            ("Lost and Found", 1),
            ("Boundary Dispute", 1),
            ("Cyberbullying / Online Threats", 2),
            ("Other / Miscellaneous", 1)
        };

        var existingCategoryNames = db.Categories.Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool addedAny = false;
        foreach (var cat in standardCategories)
        {
            if (!existingCategoryNames.Contains(cat.Name))
            {
                db.Categories.Add(new Category { Name = cat.Name, SeverityLevel = cat.Severity });
                addedAny = true;
            }
        }
        if (addedAny)
        {
            db.SaveChanges();
            app.Logger.LogInformation("Successfully verified and seeded all unique incident categories in the database.");
        }
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Failed to auto-create or seed database tables on startup. Check your connection string and network permissions.");
    }
}

app.UseHttpsRedirection();
app.UseCors("AllowAll");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.Run();

// Helper functions for database provider and connection string formatting
static bool IsPostgreSql(string? provider, string? conn)
{
    if (string.Equals(provider, "PostgreSQL", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(provider, "Supabase", StringComparison.OrdinalIgnoreCase))
    {
        return true;
    }
    if (string.Equals(provider, "SqlServer", StringComparison.OrdinalIgnoreCase))
    {
        return false;
    }
    if (string.IsNullOrWhiteSpace(conn)) return false;

    return conn.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
        || conn.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase)
        || conn.Contains("Host=", StringComparison.OrdinalIgnoreCase)
        || conn.Contains("Port=5432", StringComparison.OrdinalIgnoreCase)
        || conn.Contains("Port=6543", StringComparison.OrdinalIgnoreCase)
        || conn.Contains("supabase.co", StringComparison.OrdinalIgnoreCase)
        || conn.Contains("pooler.supabase.com", StringComparison.OrdinalIgnoreCase)
        || conn.Contains("Username=postgres", StringComparison.OrdinalIgnoreCase)
        || conn.Contains("User Id=postgres", StringComparison.OrdinalIgnoreCase);
}

static string FormatNpgsqlConnectionString(string rawConnection)
{
    if (string.IsNullOrWhiteSpace(rawConnection)) return rawConnection;

    try
    {
        // Parse Supabase URI format: postgresql://user:password@host:port/database
        if (rawConnection.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase) ||
            rawConnection.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase))
        {
            var uri = new Uri(rawConnection);
            var userInfo = uri.UserInfo.Split(':');
            var username = userInfo.Length > 0 ? Uri.UnescapeDataString(userInfo[0]) : "";
            var password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : "";
            var database = uri.AbsolutePath.TrimStart('/');
            if (string.IsNullOrEmpty(database)) database = "postgres";
            var port = uri.Port > 0 ? uri.Port : 5432;

            var builder = new NpgsqlConnectionStringBuilder
            {
                Host = uri.Host,
                Port = port,
                Database = database,
                Username = username,
                Password = password,
                SslMode = SslMode.Require
            };

            // Supabase transaction pooler (port 6543) requires disabling multiplexing / statement caching
            if (port == 6543)
            {
                builder.Multiplexing = false;
            }

            return builder.ConnectionString;
        }

        var npgsqlBuilder = new NpgsqlConnectionStringBuilder(rawConnection);

        // Supabase requires SSL Mode = Require
        if (!rawConnection.Contains("SSL Mode", StringComparison.OrdinalIgnoreCase) &&
            !rawConnection.Contains("SslMode", StringComparison.OrdinalIgnoreCase))
        {
            npgsqlBuilder.SslMode = SslMode.Require;
        }

        if (npgsqlBuilder.Port == 6543)
        {
            npgsqlBuilder.Multiplexing = false;
        }

        return npgsqlBuilder.ConnectionString;
    }
    catch
    {
        return rawConnection;
    }
}