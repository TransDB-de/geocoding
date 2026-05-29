using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using transdb_geocoding.Models;

namespace transdb_geocoding.Authentication;

/// <summary>
/// Loads API keys from files in the configured directory at startup.
/// Each file's content (trimmed) is one valid key.
/// If the directory is empty, a cryptographically random key is generated,
/// written to "default.key", and logged so the operator can retrieve it.
/// </summary>
public class ApiKeyService
{
    public IReadOnlySet<string> Keys { get; }

    public ApiKeyService(IOptions<ApiKeySettings> settings, ILogger<ApiKeyService> logger)
    {
        var dir = settings.Value.Directory;
        Directory.CreateDirectory(dir);

        var keys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in Directory.GetFiles(dir))
        {
            var key = File.ReadAllText(file).Trim();
            if (!string.IsNullOrWhiteSpace(key))
            {
                keys.Add(key);
            }
        }

        if (keys.Count == 0)
        {
            var key = GenerateKey();
            var path = Path.Combine(dir, "default.key");
            File.WriteAllText(path, key);
            keys.Add(key);
            logger.LogWarning("No API key files found in '{Directory}' - generated a new key and saved to {Path}",
                dir, path);
            logger.LogWarning("Generated API key: {Key}", key);
        }
        else
        {
            logger.LogInformation("Loaded {Count} API key(s) from '{Directory}'", keys.Count, dir);
        }

        Keys = keys;
    }

    private static string GenerateKey()
    {
        return Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    }
}
