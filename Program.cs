using Microsoft.AspNetCore.Authentication;
using transdb_geocoding.Authentication;
using transdb_geocoding.Models;
using transdb_geocoding.Services;

var builder = WebApplication.CreateBuilder(args);

// ── Configuration ────────────────────────────────────────────────────────────
builder.Services.Configure<MongoDbSettings>(
    builder.Configuration.GetSection("MongoDB"));

builder.Services.Configure<GeoDataSettings>(
    builder.Configuration.GetSection("GeoData"));

builder.Services.Configure<CacheSettings>(
    builder.Configuration.GetSection("Cache"));

builder.Services.Configure<ApiKeySettings>(
    builder.Configuration.GetSection("ApiKeys"));

builder.Services.Configure<ApiLimitationSettings>(
    builder.Configuration.GetSection("ApiLimitation"));

// ── Infrastructure ───────────────────────────────────────────────────────────
builder.Services.AddSingleton<DatabaseService>();
builder.Services.AddSingleton<ApiKeyService>();
builder.Services.AddMemoryCache();

// ── Services ─────────────────────────────────────────────────────────────────
builder.Services.AddHttpClient<DataImportService>();
builder.Services.AddScoped<IDataImportService, DataImportService>();
builder.Services.AddScoped<IGeocodeService, GeocodeService>();

// Readiness tracking + background import
builder.Services.AddSingleton<ReadinessService>();
builder.Services.AddHostedService<DataImportHostedService>();

// ── ASP.NET Core ─────────────────────────────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddOpenApi();

builder.Services
    .AddAuthentication(ApiKeyAuthenticationDefaults.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
        ApiKeyAuthenticationDefaults.SchemeName, _ => { });

builder.Services.AddAuthorization();

// ── Logging ──────────────────────────────────────────────────────────────────
// Console logging is configured via appsettings.json / env vars (Logging:LogLevel)
builder.Logging.AddConsole();

var app = builder.Build();

app.Logger.LogInformation("transdb-geocoding starting up...");

// Eagerly instantiate singletons that perform work at construction time.
app.Services.GetRequiredService<ApiKeyService>();

// ── Middleware pipeline ───────────────────────────────────────────────────────
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
