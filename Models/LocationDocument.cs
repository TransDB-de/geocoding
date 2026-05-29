using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver.GeoJsonObjectModel;

namespace transdb_geocoding.Models;

/// <summary>
/// Represents a geographic location stored in MongoDB.
/// </summary>
public record LocationDocument
{
    [BsonId]
    public ObjectId Id { get; init; }

    /// <summary>Display name of the location (UTF-8 original).</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>ASCII-safe name, used for text matching.</summary>
    public string AsciiName { get; init; } = string.Empty;

    /// <summary>All known alternate names / aliases for this location.</summary>
    public List<string> AlternateNames { get; init; } = [];

    /// <summary>GeoJSON Point — coordinates are [longitude, latitude].</summary>
    public GeoJsonPoint<GeoJson2DGeographicCoordinates> Location { get; init; } = null!;

    /// <summary>ISO 3166-1 alpha-2 country code, e.g. "DE".</summary>
    public string CountryCode { get; init; } = string.Empty;

    /// <summary>
    /// GeoNames feature code (e.g. PPL, PPLX, PPLC) or "POST" for postal-only entries.
    /// </summary>
    public string FeatureCode { get; init; } = string.Empty;

    /// <summary>Postal codes that reference this place.</summary>
    public List<string> PostalCodes { get; init; } = [];

    /// <summary>Population figure, used to break score ties (larger = higher priority).</summary>
    public long Population { get; init; }
}
