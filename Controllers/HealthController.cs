using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using transdb_geocoding.Services;

namespace transdb_geocoding.Controllers;

[ApiController]
[Route("[controller]")]
[AllowAnonymous]
public class HealthController(DatabaseService db, ReadinessService readiness) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var dbAlive = await db.PingAsync(ct);
        var body = new { db = dbAlive, status = readiness.State.ToString().ToLowerInvariant() };
        return dbAlive && readiness.IsReady
            ? Ok(body)
            : StatusCode(StatusCodes.Status503ServiceUnavailable, body);
    }
}
