using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.GeoJsonObjectModel;
using transdb_geocoding.Models;

namespace transdb_geocoding.Services;

public class DatabaseService
{
    private readonly IMongoDatabase _database;
    private readonly ILogger<DatabaseService> _logger;

    public DatabaseService(IOptions<MongoDbSettings> settings, ILogger<DatabaseService> logger)
    {
        _logger = logger;
        var client = new MongoClient(settings.Value.ConnectionString);
        _database = client.GetDatabase(settings.Value.DatabaseName);
        _logger.LogInformation("Connected to MongoDB database '{Database}'", settings.Value.DatabaseName);
    }

    public IMongoCollection<LocationDocument> Locations =>
        _database.GetCollection<LocationDocument>("locations");
    
    public async Task EnsureIndexesAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Ensuring MongoDB indexes on 'locations' collection...");

        var indexModels = new List<CreateIndexModel<LocationDocument>>
        {
            // 2dsphere index for coordinate-based $near queries
            new(Builders<LocationDocument>.IndexKeys.Geo2DSphere(l => l.Location),
                new CreateIndexOptions { Name = "location_2dsphere" }),

            // Text index for name / alternate-name search
            new(Builders<LocationDocument>.IndexKeys
                    .Text(l => l.Name)
                    .Text(l => l.AsciiName)
                    .Text("AlternateNames"),
                new CreateIndexOptions
                {
                    Name = "text_search",
                    DefaultLanguage = "none" // language-agnostic; supports all country names
                }),

            // Compound index: country filter + postal code lookup.
            // Covers both "countryCode == X" and "countryCode == X AND postalCodes contains Y" queries.
            new(Builders<LocationDocument>.IndexKeys
                    .Ascending(l => l.CountryCode)
                    .Ascending(l => l.PostalCodes),
                new CreateIndexOptions { Name = "country_postal_codes" }),
        };

        await Locations.Indexes.CreateManyAsync(indexModels, cancellationToken: ct);
        _logger.LogInformation("Indexes verified/created successfully");
    }
    
    public async Task<bool> PingAsync(CancellationToken ct = default)
    {
        try
        {
            await _database.RunCommandAsync<BsonDocument>(
                new BsonDocument("ping", 1), cancellationToken: ct);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Returns the count of documents for a given country.
    /// Used to determine whether import should run.
    /// </summary>
    public async Task<long> CountByCountryAsync(string countryCode, CancellationToken ct = default)
    {
        return await Locations.CountDocumentsAsync(
            Builders<LocationDocument>.Filter.Eq(l => l.CountryCode, countryCode),
            cancellationToken: ct);
    }
}
