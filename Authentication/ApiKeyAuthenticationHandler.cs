using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace transdb_geocoding.Authentication;

public static class ApiKeyAuthenticationDefaults
{
    public const string SchemeName = "ApiKey";
}

public class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ApiKeyService apiKeyService)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    private const string ApiKeyHeader = "X-Api-Key";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(ApiKeyHeader, out var providedKey))
            return AuthenticateResult.NoResult();

        if (!apiKeyService.Keys.Contains(providedKey.ToString()))
            return AuthenticateResult.Fail("Invalid API key");

        var principal = new ClaimsPrincipal(
            new ClaimsIdentity([], ApiKeyAuthenticationDefaults.SchemeName));

        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }
}
