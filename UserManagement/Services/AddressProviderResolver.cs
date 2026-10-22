using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using UserManagement.Data;
using UserManagement.Models;

namespace UserManagement.Services;

public interface IAddressProviderResolver
{
    /// <summary>The provider the app should use right now, or null when address suggestions are off.</summary>
    Task<IAddressProvider?> GetActiveAsync();

    /// <summary>Builds a provider for a specific config row (used by the "Test" button on the admin screen).</summary>
    IAddressProvider Create(GeoProvider config);

    /// <summary>Forget the cached active provider; call after the master config changes.</summary>
    void Invalidate();
}

/// <summary>
/// Chooses the address API from the master config (dbo.GeoProviders). Until anything has been configured there,
/// it falls back to the old "GoogleMaps:ApiKey" setting so existing installs keep working. Once the table has
/// rows, the table is the only source: with every row inactive, address suggestions are off.
/// </summary>
public class AddressProviderResolver : IAddressProviderResolver
{
    private const string CacheKey = "ActiveAddressProvider";
    private static readonly TimeSpan CacheTime = TimeSpan.FromSeconds(60);

    private readonly IGeoProviderRepository _repository;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IOptions<GoogleMapsOptions> _legacyGoogle;
    private readonly IMemoryCache _cache;
    private readonly ILoggerFactory _loggers;

    public AddressProviderResolver(IGeoProviderRepository repository, IHttpClientFactory httpFactory,
        IOptions<GoogleMapsOptions> legacyGoogle, IMemoryCache cache, ILoggerFactory loggers)
    {
        _repository = repository;
        _httpFactory = httpFactory;
        _legacyGoogle = legacyGoogle;
        _cache = cache;
        _loggers = loggers;
    }

    // The cache holds a small wrapper so "no provider" (null) can be cached too.
    private sealed record Cached(IAddressProvider? Provider);

    public async Task<IAddressProvider?> GetActiveAsync()
    {
        if (_cache.TryGetValue(CacheKey, out Cached? hit) && hit is not null) return hit.Provider;

        IAddressProvider? provider = null;
        var active = await _repository.GetActiveAsync();
        if (active is not null && !string.IsNullOrWhiteSpace(active.ApiKey))
        {
            provider = Create(active);
        }
        else if (active is null && await _repository.IsEmptyAsync() && _legacyGoogle.Value.Enabled)
        {
            provider = Create(new GeoProvider { ProviderType = GeoProviderTypes.Google, ApiKey = _legacyGoogle.Value.ApiKey });
        }

        _cache.Set(CacheKey, new Cached(provider), CacheTime);
        return provider;
    }

    public IAddressProvider Create(GeoProvider config)
    {
        var http = _httpFactory.CreateClient("address");
        switch (config.ProviderType)
        {
            case GeoProviderTypes.AmazonLocation:
                var region = string.IsNullOrWhiteSpace(config.Region) ? "us-east-1" : config.Region.Trim();
                return new AmazonLocationAddressService(http, config.ApiKey ?? "", region, _loggers.CreateLogger<AmazonLocationAddressService>());

            case GeoProviderTypes.Google:
                var options = new GoogleMapsOptions { ApiKey = config.ApiKey };
                // Keep the base URLs from appsettings (they exist so tests can point at a fake server).
                options.PlacesBaseUrl = _legacyGoogle.Value.PlacesBaseUrl;
                options.AddressValidationBaseUrl = _legacyGoogle.Value.AddressValidationBaseUrl;
                return new GoogleAddressService(http, Options.Create(options), _loggers.CreateLogger<GoogleAddressService>());

            default:
                throw new NotSupportedException($"Unknown provider type '{config.ProviderType}'.");
        }
    }

    public void Invalidate() => _cache.Remove(CacheKey);
}
