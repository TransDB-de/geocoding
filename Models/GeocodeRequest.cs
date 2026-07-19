using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace transdb_geocoding.Models;

/// <summary>
/// Geocoding lookup request bound from query parameters.
/// Either <see cref="Q"/> OR both <see cref="Lat"/> and <see cref="Lon"/> must be provided.
/// When coordinates are supplied they take precedence over the text query.
/// </summary>
public class GeocodeRequest : IValidatableObject
{
    /// <summary>Free-text query: city name, district, or postal code.</summary>
    [StringLength(100)]
    public string? Q { get; set; }

    /// <summary>Latitude in decimal degrees (WGS84).</summary>
    [Range(-90, 90, ErrorMessage = "'lat' must be between -90 and 90.")]
    public double? Lat { get; set; }

    /// <summary>Longitude in decimal degrees (WGS84).</summary>
    [Range(-180, 180, ErrorMessage = "'lon' must be between -180 and 180.")]
    public double? Lon { get; set; }

    /// <summary>ISO 3166-1 alpha-2 country code. Defaults to "DE".</summary>
    [StringLength(2)]
    public string? Country { get; set; } = null;

    public int Limit { get; set; } = 3;
    
    [JsonIgnore]
    public bool HasCoordinates => Lat.HasValue && Lon.HasValue;
    [JsonIgnore]
    public bool HasTextQuery => !string.IsNullOrWhiteSpace(Q);
    
    public IEnumerable<ValidationResult> Validate(ValidationContext ctx)
    {
        // lat and lon must be supplied as a pair
        if (Lat.HasValue != Lon.HasValue)
            yield return new ValidationResult(
                "'lat' and 'lon' must be provided together.",
                [nameof(Lat), nameof(Lon)]);

        // At least one search mode must be specified
        if (!HasTextQuery && !HasCoordinates)
            yield return new ValidationResult(
                "Provide either 'q' (text query) or both 'lat' and 'lon' (coordinates).",
                [nameof(Q), nameof(Lat), nameof(Lon)]);

        var options = ctx.GetService(typeof(IOptionsSnapshot<GeoDataSettings>))
            as IOptionsSnapshot<GeoDataSettings>;
        
        if (Country != null && !options.Value.Countries.Contains(Country))
        {
            yield return new ValidationResult(
                "Requested country not available", [nameof(Country)]);
        }
    }
    
    /// <summary>
    /// Builds a privacy-safe cache key by SHA256-hashing the full query fingerprint.
    /// Coordinates, postal codes, and text queries are never stored in plain text in memory keys.
    /// </summary>
    public string GetCacheKey()
    {
        var country = !string.IsNullOrWhiteSpace(this.Country) ? this.Country.ToUpperInvariant() : string.Empty;
        
        var fingerprint = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{this.Q?.Trim().ToLowerInvariant()}|{this.Lat:F6}|{this.Lon:F6}|{country}");

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint));
        return Convert.ToHexString(hash);
    }
}
