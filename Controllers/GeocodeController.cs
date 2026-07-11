using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using transdb_geocoding.Models;
using transdb_geocoding.Services;

namespace transdb_geocoding.Controllers;

[ApiController]
[Authorize]
public class GeocodeController(IGeocodeService geocodeService, IMemoryCache cache,
    IOptions<CacheSettings> cacheSettings, IOptions<ApiLimitationSettings> limitSettings, IOptions<GeoDataSettings> geodataSettings) : ControllerBase
{
    /// <summary>
    /// Get all countries available for geocoding
    /// </summary>
    /// <returns>object containing list of country codes</returns>
    [HttpGet("countries")]
    [ProducesResponseType<GetAvailableCountriesResult>(StatusCodes.Status200OK)]
    public async Task<ActionResult<GetAvailableCountriesResult>> GetAvailableCountries()
    {
        return Ok(new GetAvailableCountriesResult(geodataSettings.Value.Countries));
    }
    
    /// <summary>
    /// Geocoding lookup. Supply either a text query (<paramref name="request.Q"/>) or
    /// coordinates (<paramref name="request.Lat"/> + <paramref name="request.Lon"/>).
    /// Coordinates take precedence when both are provided.
    /// </summary>
    /// <returns>Up to 3 matching locations, sorted by match quality descending.</returns>
    [HttpGet("geocode")]
    [ProducesResponseType<List<GeocodeResult>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<List<GeocodeResult>>> Geocode([FromQuery] GeocodeRequest request, CancellationToken ct = default)
    {
        if (request.Limit > limitSettings.Value.MaxLocationLimit)
        {
            ModelState.AddModelError(
                nameof(request.Limit),
                "Maximum allowed limit is " + limitSettings.Value.MaxLocationLimit);

            return ValidationProblem(ModelState);
        }

        if (!string.IsNullOrEmpty(request.Country))
        {
            request.Country = request.Country.ToUpperInvariant();
        }
        
        var cachedData = cache.Get<List<GeocodeResult>>(request.GetCacheKey());

        if (cachedData != null)
        {
            return Ok(cachedData);
        }
        
        var results = await geocodeService.SearchAsync(request, ct);
        
        cache.Set(request.GetCacheKey(), results, cacheSettings.Value.Ttl);
        
        return Ok(results);
    }
}
