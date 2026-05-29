# Trans\*DB Geocoding Service

A self-contained geocoding microservice built on ASP.NET Core and MongoDB.
On first boot it downloads and imports [GeoNames](https://www.geonames.org/) data and exposes a single endpoint for forward (text/postal) and reverse (coordinate) geocoding.

Intended for machine-to-machine use.

The service is quite performant with response times in sometimes under 100ms and only minor ram usage in the two digit megabyte range.
Accuracy is not down to actual addresses, but villages/city-districts.
This is enough for the intended purpose of the Trans*DB Project to have a distance based sorting mechanism for finding entries near the user.

Of course the more countries you add, the more performance it will eat.

> **Disclaimer:** The development of this service was assisted by generative machine learning. All code was manually reviewed and verified by a skilled human dev.


## Features

- **Forward geocoding** - city/village/district name or postal code → coordinates
- **Reverse geocoding** - coordinates → nearest place
- **Multi-country** - configurable; ships with DE, AT, CH, BE, NL
- **Auto-import** - downloads GeoNames data on first boot, skips on subsequent starts
- **API key auth** - multiple keys supported for zero-downtime rotation
- **Response caching** - in-memory cache with configurable TTL; cache keys are SHA-256 hashed (coordinates and queries are never stored in plain text)
- **Readiness endpoint** - reports startup/import progress for Docker health checks


## API

### Authentication

All requests to `/geocode` require an `X-Api-Key` header:

```
X-Api-Key: your-api-key
```

Requests without a valid key return `401 Unauthorized`.

API keys are loaded from files — see [API Keys](#api-keys) below.


### `GET /geocode`

| Parameter | Type     | Required | Description                                          |
|-----------|----------|----------|------------------------------------------------------|
| `q`       | `string` | either/or| City name, district, or postal code                  |
| `lat`     | `double` | either/or| Latitude in decimal degrees (WGS84), paired with `lon` |
| `lon`     | `double` | either/or| Longitude in decimal degrees (WGS84), paired with `lat`|
| `country` | `string` | no       | ISO 3166-1 alpha-2 country code. Default: `DE`       |

Either `q` or `lat`+`lon` must be provided. When both are given, coordinates take precedence.

**Response** — up to 3 results, ordered by relevance:

```json
[
  {
    "name": "Hamburg",
    "countryCode": "DE",
    "location": {
      "type": "Point",
      "coordinates": [10.0, 53.5753]
    }
  }
]
```

`location` is a GeoJson Point

**Examples:**

```bash
# Text search
curl -H "X-Api-Key: your-key" "http://localhost:8080/geocode?q=Hamburg&country=DE"

# Postal code
curl -H "X-Api-Key: your-key" "http://localhost:8080/geocode?q=10115&country=DE"

# Reverse geocoding
curl -H "X-Api-Key: your-key" "http://localhost:8080/geocode?lat=53.55&lon=10.00&country=DE"
```


### `GET /health`

No authentication required. Used for Docker/Kubernetes liveness and readiness probes and service monitoring.

```json
{ "status": "ready" }
```

| Status       | HTTP | Meaning                              |
|--------------|------|--------------------------------------|
| `starting`   | 503  | App is booting                       |
| `importing`  | 503  | GeoNames import in progress          |
| `ready`      | 200  | Fully operational                    |
| `failed`     | 503  | Import failed; queries return empty  |


## Configuration

All settings can be overridden via environment variables using `__` as the separator (e.g. `MongoDB__ConnectionString`).

### `appsettings.json`

```jsonc
{
  "MongoDB": {
    "ConnectionString": "mongodb://localhost:27017",
    "DatabaseName": "geocoding"
  },

  "ApiKeys": {
    "Directory": "/app/keys"
  },

  "Cache": {
    "Ttl": "01:00:00"   // TimeSpan format: hh:mm:ss
  },

  "GeoData": {
    "DataDirectory": "./geodata",
    "PlacesUrl": "https://download.geonames.org/export/dump/{code}.zip",
    "PostalCodesUrl": "https://download.geonames.org/export/zip/{code}.zip",
    "Countries": [ "DE", "AT", "CH" ]
  }
}
```

### Key environment variables

| Variable                    | Description                                        |
|-----------------------------|----------------------------------------------------|
| `MongoDB__ConnectionString` | MongoDB connection string                          |
| `MongoDB__DatabaseName`     | MongoDB database name                              |
| `ApiKeys__Directory`        | Directory from which API key files are loaded      |
| `Cache__Ttl`                | Cache TTL as TimeSpan string, e.g. `00:30:00`      |
| `GeoData__DataDirectory`    | Path where downloaded zip files are stored         |


## API Keys

API keys are read from files in a configured directory. Each file's content (trimmed) is one valid key. The filename does not matter.

**Auto-generation:** if the directory is empty on startup, a cryptographically random key is generated, saved to `default.key`, and printed to the logs at `Warning` level:

```
warn: Generated API key: aBcDeFgH...
```

**Key rotation:** add a new key file, restart the service, then remove the old file and restart again.

**Local development:** place a key file in `./keys/`:

```bash
mkdir -p keys
echo "my-dev-key" > keys/dev.key
```


## Running with Docker

API keys are provided via a **bind mount** (not a named volume) so you control the files from the host:

```bash
# Create a key file on the host
mkdir -p /etc/myapp/keys
echo "your-secret-key" > /etc/myapp/keys/backend.key

docker run -d \
  -p 8080:8080 \
  -e MongoDB__ConnectionString=mongodb://your-mongo-host:27017 \
  -v /etc/myapp/keys:/app/keys:ro \
  -v /data/geodata:/app/geodata \
  transdb-geocoding
```

The volume mount for `/app/geodata` persists downloaded GeoNames zip files so they are not re-downloaded on container restarts.

### Docker Compose example

```yaml
services:
  geocoding:
    image: transdb-geocoding
    ports:
      - "8080:8080"
    environment:
      MongoDB__ConnectionString: mongodb://mongo:27017
    volumes:
      - /etc/myapp/keys:/app/keys:ro   # bind mount — not a named volume
      - geodata:/app/geodata
    depends_on:
      - mongo
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:8080/health"]
      interval: 30s
      timeout: 5s
      retries: 3
      start_period: 300s  # allow time for initial GeoNames import

  mongo:
    image: mongo:7
    volumes:
      - mongo-data:/data/db

volumes:
  geodata:
  mongo-data:
```

> **Note:** The initial import can take several minutes per country depending on hardware. The `start_period` in the health check should account for this. Downloaded files are cached in `DataDirectory` and the import is skipped on subsequent starts.


## Adding countries

Add the ISO 3166-1 alpha-2 country code to the `GeoData.Countries` array in `appsettings.json`:

```json
"Countries": [ "DE", "AT", "CH", "FR" ]
```

The download URLs are derived automatically from the `PlacesUrl` and `PostalCodesUrl` templates — no further changes needed. On next boot the service will import the new country while leaving existing data untouched.
