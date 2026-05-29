using System.IO.Compression;
using System.Text;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.GeoJsonObjectModel;
using transdb_geocoding.Models;

namespace transdb_geocoding.Services;

public interface IDataImportService
{
    /// <summary>
    /// Checks each configured country and runs the GeoNames import
    /// if no data exists yet. Blocks until complete.
    /// </summary>
    Task EnsureDataImportedAsync(CancellationToken ct = default);
}

public class DataImportService(
    DatabaseService db,
    IOptions<GeoDataSettings> settings,
    ILogger<DataImportService> logger,
    HttpClient http)
    : IDataImportService
{
    // GeoNames feature codes for inhabited places
    private static readonly HashSet<string> PlaceFeatureCodes =
    [
        "PPL",   // Populated place
        "PPLA",  // Seat of first-order admin div (state capital)
        "PPLA2", // Seat of second-order admin div
        "PPLA3", // Seat of third-order admin div
        "PPLA4", // Seat of fourth-order admin div
        "PPLC",  // Capital of a political entity
        "PPLF",  // Farm village
        "PPLL",  // Populated locality
        "PPLR",  // Religious populated place
        "PPLS",  // Populated places
        "PPLX",  // Section of populated place (city districts)
    ];

    // GeoNames column indices (allCountries format, tab-separated)
    private static class PlaceColumns
    {
        public const int GeonameId      = 0;
        public const int Name           = 1;
        public const int AsciiName      = 2;
        public const int AlternateNames = 3;
        public const int Latitude       = 4;
        public const int Longitude      = 5;
        public const int FeatureClass   = 6;
        public const int FeatureCode    = 7;
        public const int Population     = 14;
        public const int MinCount       = 19;
    }

    // GeoNames column indices (postal code format, tab-separated)
    private static class PostalColumns
    {
        public const int PostalCode = 1;
        public const int PlaceName  = 2;
        public const int Latitude   = 9;
        public const int Longitude  = 10;
        public const int MinCount   = 11;
    }

    // Intermediate parse records
    private sealed record PlaceRow(
        string GeonameId,
        string Name,
        string AsciiName,
        IReadOnlyList<string> AlternateNames,
        double Latitude,
        double Longitude,
        string FeatureCode,
        long Population);

    private sealed record PostalRow(
        string PostalCode,
        string PlaceName,
        double Latitude,
        double Longitude);

    private readonly GeoDataSettings _settings = settings.Value;

    /// <inheritdoc/>
    public async Task EnsureDataImportedAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(_settings.DataDirectory);

        foreach (var code in _settings.Countries)
        {
            var existing = await db.CountByCountryAsync(code, ct);
            if (existing > 0)
            {
                logger.LogInformation(
                    "Country {Code}: {Count} locations already in DB - skipping import",
                    code, existing);
                continue;
            }

            logger.LogInformation("Country {Code}: starting GeoNames import...", code);
            await ImportCountryAsync(code, ct);
        }

        await db.EnsureIndexesAsync(ct);
    }

    private async Task ImportCountryAsync(string code, CancellationToken ct)
    {
        var placesZip = Path.Combine(_settings.DataDirectory, $"{code}_places.zip");
        var postalZip = Path.Combine(_settings.DataDirectory, $"{code}_postal.zip");

        var placesUrl = _settings.PlacesUrl.Replace("{code}", code, StringComparison.OrdinalIgnoreCase);
        var postalUrl = _settings.PostalCodesUrl.Replace("{code}", code, StringComparison.OrdinalIgnoreCase);

        await DownloadFileAsync(placesUrl, placesZip, ct);
        await DownloadFileAsync(postalUrl, postalZip, ct);

        var places = EnrichDistrictNames(ParsePlaces(placesZip, code));
        logger.LogInformation("Country {Code}: parsed {Count} place entries", code, places.Count);

        var postalRows = ParsePostalCodes(postalZip);
        logger.LogInformation("Country {Code}: parsed {Count} postal code entries", code, postalRows.Count);

        var merged = MergePostalCodes(places, postalRows, code);
        logger.LogInformation(
            "Country {Code}: merge complete —{Total} total documents ({New} postal-only entries added)",
            code, merged.Count, merged.Count - places.Count);

        await BulkInsertAsync(merged, code, ct);
    }

    private async Task DownloadFileAsync(string url, string destinationPath, CancellationToken ct)
    {
        if (File.Exists(destinationPath)) return;

        logger.LogInformation("Downloading {Url}...", url);

        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var tempPath = destinationPath + ".tmp";
        await using var fileStream = File.Create(tempPath);
        await using var httpStream = await response.Content.ReadAsStreamAsync(ct);
        await httpStream.CopyToAsync(fileStream, ct);

        File.Move(tempPath, destinationPath, overwrite: true);
        logger.LogInformation("Download complete: {Path}", destinationPath);
    }

    private List<LocationDocument> ParsePlaces(string zipPath, string countryCode)
    {
        var rows = ReadZipLines(zipPath)
            .Select(TryParsePlaceRow)
            .OfType<PlaceRow>()
            .Where(r => PlaceFeatureCodes.Contains(r.FeatureCode));

        return rows.Select(r => MapPlaceToDocument(r, countryCode)).ToList();
    }

    private List<PostalRow> ParsePostalCodes(string zipPath)
    {
        return ReadZipLines(zipPath)
            .Select(TryParsePostalRow)
            .OfType<PostalRow>()
            .ToList();
    }

    /// <summary>
    /// Reads all non-empty, non-comment lines from the first .txt entry in a zip archive.
    /// </summary>
    private IEnumerable<string[]> ReadZipLines(string zipPath)
    {
        using var archive = ZipFile.OpenRead(zipPath);

        var entry = archive.Entries.FirstOrDefault(e =>
            e.Name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
            && !e.Name.StartsWith("readme", StringComparison.OrdinalIgnoreCase));

        if (entry is null)
        {
            logger.LogError("No .txt data file found in {ZipPath}", zipPath);
            yield break;
        }

        using var stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8);

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
                continue;

            yield return line.Split('\t');
        }
    }

    private static PlaceRow? TryParsePlaceRow(string[] cols)
    {
        if (cols.Length < PlaceColumns.MinCount) return null;

        if (!double.TryParse(cols[PlaceColumns.Latitude],
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var lat) ||
            !double.TryParse(cols[PlaceColumns.Longitude],
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var lon))
            return null;

        long.TryParse(cols[PlaceColumns.Population], out var population);

        var alternateNames = new List<string>();

        if (!string.IsNullOrWhiteSpace(cols[PlaceColumns.AlternateNames]))
        {
            alternateNames = cols[PlaceColumns.AlternateNames]
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(name => name.All(c => c < 128)) // ASCII-only: drops CJK/Arabic/etc., keeps umlauts via AsciiName
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        // Feature class filter is applied here so the caller doesn't need to know column indices
        if (cols[PlaceColumns.FeatureClass] != "P") return null;

        return new PlaceRow(
            GeonameId:      cols[PlaceColumns.GeonameId],
            Name:           cols[PlaceColumns.Name],
            AsciiName:      cols[PlaceColumns.AsciiName],
            AlternateNames: alternateNames,
            Latitude:       lat,
            Longitude:      lon,
            FeatureCode:    cols[PlaceColumns.FeatureCode],
            Population:     population);
    }

    private static PostalRow? TryParsePostalRow(string[] cols)
    {
        if (cols.Length < PostalColumns.MinCount)
            return null;

        if (!double.TryParse(cols[PostalColumns.Latitude],
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var lat) ||
            !double.TryParse(cols[PostalColumns.Longitude],
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var lon))
            return null;

        return new PostalRow(
            PostalCode: cols[PostalColumns.PostalCode].Trim(),
            PlaceName:  cols[PostalColumns.PlaceName].Trim(),
            Latitude:   lat,
            Longitude:  lon);
    }

    private static LocationDocument MapPlaceToDocument(PlaceRow row, string countryCode) =>
        new()
        {
            Id             = ObjectId.GenerateNewId(),
            Name           = row.Name,
            AsciiName      = row.AsciiName,
            AlternateNames = [.. row.AlternateNames],
            Location       = GeoJson.Point(GeoJson.Geographic(row.Longitude, row.Latitude)),
            CountryCode    = countryCode,
            FeatureCode    = row.FeatureCode,
            PostalCodes    = [],
            Population     = row.Population,
        };

    /// <summary>
    /// For each PPLX place (city district), finds the nearest city-level place within 30 km
    /// and rewrites its Name to "District, City" (e.g. "Innenstadt, Karlsruhe").
    /// Non-PPLX places are returned unchanged. AsciiName is left unmodified (used for search matching).
    /// </summary>
    private static IReadOnlyList<LocationDocument> EnrichDistrictNames(
        IReadOnlyList<LocationDocument> places)
    {
        // Build a grid containing only city-level places — everything except PPLX
        var cityGrid = BuildSpatialGrid(places.Where(p => p.FeatureCode != "PPLX").ToList());

        return places.Select(place =>
        {
            if (place.FeatureCode != "PPLX") return place;

            var city = FindNearest(cityGrid,
                place.Location.Coordinates.Latitude,
                place.Location.Coordinates.Longitude,
                maxDistanceKm: 30.0);

            // Guard against "Innenstadt, Innenstadt" if district and city share the same name
            if (city is null || city.Name == place.Name) return place;

            return place with { Name = $"{place.Name}, {city.Name}" };
        }).ToList();
    }
    
    /// <summary>
    /// Merges postal code rows into the place list.
    /// Returns a new list — inputs are not modified.
    ///
    /// Matching strategy (two-pass per row):
    ///   1. Name match — find a place whose Name/AsciiName equals the postal row's PlaceName
    ///      and lies within 15 km. This correctly assigns "22941 Hammoor" to the Hammoor
    ///      place even though Bargteheide is the nearest neighbour by centroid distance.
    ///   2. Distance fallback — nearest place within 2 km, regardless of name.
    ///      Catches postal rows for hamlets too small to appear in the places dataset.
    ///
    /// Unmatched rows become standalone POST entries.
    /// </summary>
    private IReadOnlyList<LocationDocument> MergePostalCodes(
        IReadOnlyList<LocationDocument> places,
        IReadOnlyList<PostalRow> postalRows,
        string countryCode)
    {
        var grid = BuildSpatialGrid(places);

        // Name index: normalized place name → list of candidates.
        // Indexed by both Name and AsciiName so "München"/"Muenchen" both resolve.
        var nameIndex = new Dictionary<string, List<LocationDocument>>(StringComparer.OrdinalIgnoreCase);
        foreach (var place in places)
        {
            AddToNameIndex(nameIndex, place.Name, place);
            if (!string.Equals(place.Name, place.AsciiName, StringComparison.OrdinalIgnoreCase))
            {
                AddToNameIndex(nameIndex, place.AsciiName, place);
            }
                
        }

        // Map each place id to the postal codes matched to it.
        var postalsByPlaceId = new Dictionary<ObjectId, HashSet<string>>();
        var standalonePostalDocs = new List<LocationDocument>();

        foreach (var row in postalRows)
        {
            // Pass 1: name match within 15 km
            LocationDocument? matched = null;
            if (nameIndex.TryGetValue(row.PlaceName, out var candidates))
            {
                double bestDist = double.MaxValue;
                foreach (var candidate in candidates)
                {
                    var dist = HaversineKm(
                        row.Latitude, row.Longitude,
                        candidate.Location.Coordinates.Latitude,
                        candidate.Location.Coordinates.Longitude);

                    if (dist <= 15.0 && dist < bestDist)
                    {
                        bestDist = dist;
                        matched = candidate;
                    }
                }
            }

            // Pass 2: distance fallback
            matched ??= FindNearest(grid, row.Latitude, row.Longitude, maxDistanceKm: 2.0);

            if (matched is not null)
            {
                if (!postalsByPlaceId.TryGetValue(matched.Id, out var codes))
                {
                    codes = new HashSet<string>(StringComparer.Ordinal);
                    postalsByPlaceId[matched.Id] = codes;
                }
                codes.Add(row.PostalCode);
            }
            else
            {
                standalonePostalDocs.Add(new LocationDocument
                {
                    Id             = ObjectId.GenerateNewId(),
                    Name           = row.PlaceName,
                    AsciiName      = row.PlaceName,
                    AlternateNames = [],
                    Location       = GeoJson.Point(GeoJson.Geographic(row.Longitude, row.Latitude)),
                    CountryCode    = countryCode,
                    FeatureCode    = "POST",
                    PostalCodes    = [row.PostalCode],
                    Population     = 0,
                });
            }
        }

        // Produce new LocationDocument instances with postal codes applied — no mutation of inputs
        var mergedPlaces = places.Select(place =>
            postalsByPlaceId.TryGetValue(place.Id, out var codes)
                ? place with { PostalCodes = [.. codes] }
                : place);

        return [.. mergedPlaces, .. standalonePostalDocs];
    }

    private static void AddToNameIndex(
        Dictionary<string, List<LocationDocument>> index, string name, LocationDocument place)
    {
        if (!index.TryGetValue(name, out var list))
            index[name] = list = [];
        list.Add(place);
    }

    // ── Spatial grid helpers ──────────────────────────────────────────────────

    private const double GridCellSizeDegrees = 0.02; // ~2 km per cell

    private static Dictionary<(int Row, int Col), List<LocationDocument>> BuildSpatialGrid(
        IReadOnlyList<LocationDocument> places)
    {
        var grid = new Dictionary<(int, int), List<LocationDocument>>();

        foreach (var place in places)
        {
            var key = GridKey(place.Location.Coordinates.Latitude, place.Location.Coordinates.Longitude);
            if (!grid.TryGetValue(key, out var bucket))
            {
                bucket = [];
                grid[key] = bucket;
            }
            bucket.Add(place);
        }

        return grid;
    }

    private static LocationDocument? FindNearest(
        Dictionary<(int, int), List<LocationDocument>> grid,
        double lat, double lon,
        double maxDistanceKm)
    {
        var center = GridKey(lat, lon);
        LocationDocument? best = null;
        double bestDist = double.MaxValue;

        for (int dr = -1; dr <= 1; dr++)
        for (int dc = -1; dc <= 1; dc++)
        {
            if (!grid.TryGetValue((center.Row + dr, center.Col + dc), out var bucket))
                continue;

            foreach (var place in bucket)
            {
                var dist = HaversineKm(
                    lat, lon,
                    place.Location.Coordinates.Latitude,
                    place.Location.Coordinates.Longitude);

                if (dist < bestDist && dist <= maxDistanceKm)
                {
                    bestDist = dist;
                    best = place;
                }
            }
        }

        return best;
    }

    private static (int Row, int Col) GridKey(double lat, double lon) =>
        ((int)Math.Floor(lat / GridCellSizeDegrees), (int)Math.Floor(lon / GridCellSizeDegrees));

    private static double HaversineKm(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371.0;
        var dLat = ToRad(lat2 - lat1);
        var dLon = ToRad(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                + Math.Cos(ToRad(lat1)) * Math.Cos(ToRad(lat2))
                * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    private static double ToRad(double deg) => deg * Math.PI / 180.0;

    // ── Bulk insert ───────────────────────────────────────────────────────────

    private async Task BulkInsertAsync(
        IReadOnlyList<LocationDocument> locations, string countryCode, CancellationToken ct)
    {
        const int batchSize = 500;
        int total = locations.Count;
        int inserted = 0;

        logger.LogInformation(
            "Country {Code}: inserting {Total} documents in batches of {Batch}...",
            countryCode, total, batchSize);

        for (int i = 0; i < total; i += batchSize)
        {
            var batch = locations.Skip(i).Take(batchSize).ToList();

            try
            {
                await db.Locations.InsertManyAsync(
                    batch,
                    new InsertManyOptions { IsOrdered = false },
                    ct);
                inserted += batch.Count;
            }
            catch (MongoBulkWriteException ex)
            {
                inserted += batch.Count - ex.WriteErrors.Count;
                logger.LogWarning(
                    "Country {Code}: batch had {Errors} write error(s)",
                    countryCode, ex.WriteErrors.Count);
            }

            logger.LogInformation(
                "Country {Code}: inserted {Inserted}/{Total} documents",
                countryCode, inserted, total);
        }

        logger.LogInformation(
            "Country {Code}: import complete — {Inserted} documents inserted",
            countryCode, inserted);
    }
}
