namespace transdb_geocoding.Models;

/// <summary>
/// GeoJSON Point for API responses. Serializes as {"type":"Point","coordinates":[lon,lat]}.
/// </summary>
public class GeoJsonPoint
{
    public string Type => "Point";
    public double[] Coordinates { get; init; } = [];
}

/// <summary>
/// A single geocoding result returned to the caller.
/// </summary>
public class GeocodeResult
{
    /// <summary>Human-readable location name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>ISO 3166-1 alpha-2 country code, e.g. "DE".</summary>
    public string CountryCode { get; set; } = string.Empty;

    /// <summary>
    /// GeoJSON Point representing the location.
    /// Coordinates are [longitude, latitude] per the GeoJSON spec.
    /// </summary>
    public GeoJsonPoint Location { get; set; } = null!;
}

/// <summary>
/// Object wrapper for available countries list
/// </summary>
/// <param name="Countries">list of iso country codes</param>
public record GetAvailableCountriesResult(List<string> Countries);