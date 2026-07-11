using MongoDB.Driver;
using MongoDB.Driver.GeoJsonObjectModel;
using transdb_geocoding.Models;

namespace transdb_geocoding.Services;

public interface IGeocodeService
{
    /// <summary>
    /// Performs a geocoding lookup and returns up to 3 results.
    /// </summary>
    Task<List<GeocodeResult>> SearchAsync(GeocodeRequest request, CancellationToken ct = default);
}

public class GeocodeService(DatabaseService db) : IGeocodeService {
    private const int MaxResults = 3;

    public async Task<List<GeocodeResult>> SearchAsync(GeocodeRequest request, CancellationToken ct = default)
    {
        List<GeocodeResult> results;

        if (request.HasCoordinates)
        {
            return await SearchByCoordinatesAsync(request, ct);
        }
        
        var postalCodeParse = PostalCodeParser.Extract(request.Q!.Trim());
        
        if (postalCodeParse.Success)
        {
            results = await SearchByPostalCodeAsync(request, postalCodeParse.PostalCode, postalCodeParse.Rest, ct);
        }
        else
        {
            results = await SearchByTextAsync(request, ct);
        }

        return results;
    }
    
    /// <summary>
    /// Search near locations by coordinates
    /// </summary>
    /// <param name="request">parameters from the request</param>
    /// <param name="ct">cancellation token</param>
    /// <returns>list of geocoding results</returns>
    private async Task<List<GeocodeResult>> SearchByCoordinatesAsync(
        GeocodeRequest request, CancellationToken ct)
    {
        var point = GeoJson.Point(GeoJson.Geographic(request.Lon!.Value, request.Lat!.Value));

        var filter = Builders<LocationDocument>.Filter.And(
            Builders<LocationDocument>.Filter.Eq(l => l.CountryCode, request.Country),
            Builders<LocationDocument>.Filter.Near(l => l.Location, point, maxDistance: null, minDistance: null)
        );

        var docs = await db.Locations
            .Find(filter)
            .Limit(MaxResults)
            .ToListAsync(ct);

        return docs.Select(doc => new GeocodeResult
        {
            Name = doc.Name,
            CountryCode = doc.CountryCode,
            Location = new GeoJsonPoint { Coordinates = [doc.Location.Coordinates.Longitude, doc.Location.Coordinates.Latitude] },
        }).ToList();
    }
    
    /// <summary>
    /// Search locations by postal code
    /// Primary sort: population descending (largest place first).
    /// When a name hint is present (e.g. "Hammoor" from "22941 Hammoor"), fetch
    /// extra candidates and promote the one whose name matches the hint to the top.
    /// </summary>
    /// <param name="request">parameters from the request</param>
    /// <param name="postalCode">zip code</param>
    /// <param name="nameHint">if an additional text is provided, use that to get a more precise result</param>
    /// <param name="ct">cancellation token</param>
    /// <returns>list of geocoding results</returns>
    private async Task<List<GeocodeResult>> SearchByPostalCodeAsync(
        GeocodeRequest request, string postalCode, string nameHint, CancellationToken ct)
    {
        // Uppercase so "1234 ab" matches stored "1234 AB" (NL)
        var postal = postalCode.ToUpperInvariant();

        var filter = Builders<LocationDocument>.Filter.And(
            Builders<LocationDocument>.Filter.Eq(l => l.CountryCode, request.Country),
            Builders<LocationDocument>.Filter.AnyEq(l => l.PostalCodes, postal)
        );

        // Fetch extra candidates when a name hint is present so the matching place
        // is not cut off before re-ranking (mirrors the text-search approach).
        var limit = string.IsNullOrWhiteSpace(nameHint) ? MaxResults : MaxResults * 5;

        var docs = await db.Locations
            .Find(filter)
            .SortByDescending(l => l.Population)
            .Limit(limit)
            .ToListAsync(ct);

        // If the user also typed a place name, promote the matching document to the top.
        // LINQ OrderByDescending is stable, so population order is preserved for the rest.
        IEnumerable<LocationDocument> ranked = string.IsNullOrWhiteSpace(nameHint)
            ? docs
            : docs.OrderByDescending(doc =>
                string.Equals(doc.Name, nameHint, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(doc.AsciiName, nameHint, StringComparison.OrdinalIgnoreCase));

        return ranked.Take(MaxResults).Select(doc => new GeocodeResult
        {
            Name = doc.Name,
            CountryCode = doc.CountryCode,
            Location = new GeoJsonPoint { Coordinates = [doc.Location.Coordinates.Longitude, doc.Location.Coordinates.Latitude] },
        }).ToList();
    }

    /// <summary>
    /// Find locations by name
    /// </summary>
    /// <param name="request">parameters from the request</param>
    /// <param name="ct">cancellation token</param>
    /// <returns>list of geocoding results</returns>
    private async Task<List<GeocodeResult>> SearchByTextAsync(
        GeocodeRequest request, CancellationToken ct)
    {
        var q = request.Q!.Trim();

        var filter = Builders<LocationDocument>.Filter.And(
            Builders<LocationDocument>.Filter.Eq(l => l.CountryCode, request.Country),
            Builders<LocationDocument>.Filter.Text(q, new TextSearchOptions { CaseSensitive = false })
        );

        var projection = Builders<LocationDocument>.Projection
            .MetaTextScore("TextScore")
            .Include(l => l.Name)
            .Include(l => l.AsciiName)
            .Include(l => l.CountryCode)
            .Include(l => l.Location)
            .Include(l => l.Population)
            .Exclude("_id");

        // Fetch more candidates than needed so the exact-match promotion has room to work.
        // Sort must be applied before Project in the fluent API (MongoDB.Driver 3.x).
        var rawDocs = await db.Locations
            .Find(filter)
            .Sort(Builders<LocationDocument>.Sort.MetaTextScore("TextScore"))
            .Limit(MaxResults * 5)
            .Project<TextSearchResultProjection>(projection)
            .ToListAsync(ct);

        // MongoDB text score is frequency-based and does not distinguish exact matches from partial ones.
        // Re-sort so documents whose name exactly equals the query bubble to the top,
        // while all other documents remain in their original text-score order (LINQ sort is stable).
        return rawDocs
            .OrderByDescending(doc =>
                string.Equals(doc.Name, q, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(doc.AsciiName, q, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(doc => doc.Population)
            .Take(MaxResults)
            .Select(doc => new GeocodeResult
            {
                Name = doc.Name,
                CountryCode = doc.CountryCode,
                Location = new GeoJsonPoint { Coordinates = [doc.Location.Coordinates.Longitude, doc.Location.Coordinates.Latitude] },
            })
            .ToList();
    }

    private class TextSearchResultProjection
    {
        public string Name { get; set; } = string.Empty;
        public string AsciiName { get; set; } = string.Empty;
        public string CountryCode { get; set; } = string.Empty;
        public GeoJsonPoint<GeoJson2DGeographicCoordinates> Location { get; set; } = null!;
        public double TextScore { get; set; } = -1;
        public long Population { get; set; } = -1;
    }
}
