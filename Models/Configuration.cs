namespace transdb_geocoding.Models;

public class MongoDbSettings
{
    public string ConnectionString { get; set; } = "mongodb://localhost:27017";
    public string DatabaseName { get; set; } = "geocoding";
}

public class ApiKeySettings
{
    public string Directory { get; set; } = "/app/keys";
}

public class ApiLimitationSettings
{
    public int MaxLocationLimit { get; set; } = 3;
}

public class CacheSettings
{
    public TimeSpan Ttl { get; set; } = TimeSpan.FromHours(1);
}

public class GeoDataSettings
{
    public string DataDirectory { get; set; } = "./geodata";

    /// <summary>
    /// URL template for the GeoNames places dump. Use {code} as the country code placeholder.
    /// Example: "https://download.geonames.org/export/dump/{code}.zip"
    /// </summary>
    public string PlacesUrl { get; set; } = "https://download.geonames.org/export/dump/{code}.zip";

    /// <summary>
    /// URL template for the GeoNames postal codes dump. Use {code} as the country code placeholder.
    /// Example: "https://download.geonames.org/export/zip/{code}.zip"
    /// </summary>
    public string PostalCodesUrl { get; set; } = "https://download.geonames.org/export/zip/{code}.zip";

    /// <summary>ISO 3166-1 alpha-2 country codes to import.</summary>
    public List<string> Countries { get; set; } = [];
}
