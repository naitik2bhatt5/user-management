using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using UserManagement.Data;
using UserManagement.Models;
using UserManagement.Services;

namespace UserManagement.Tests;

public class AddressProviderResolverTests
{
    private sealed class FakeRepository : IGeoProviderRepository
    {
        public List<GeoProvider> Rows { get; } = new();
        public int ActiveCalls { get; private set; }

        public Task<IReadOnlyList<GeoProvider>> GetAllAsync() => Task.FromResult<IReadOnlyList<GeoProvider>>(Rows);
        public Task<GeoProvider?> GetAsync(int id) => Task.FromResult(Rows.FirstOrDefault(r => r.ProviderId == id));
        public Task<GeoProvider?> GetActiveAsync() { ActiveCalls++; return Task.FromResult(Rows.FirstOrDefault(r => r.IsActive)); }
        public Task<bool> IsEmptyAsync() => Task.FromResult(Rows.Count == 0);
        public Task<int> CreateAsync(GeoProvider provider) => throw new NotSupportedException();
        public Task UpdateAsync(GeoProvider provider) => throw new NotSupportedException();
        public Task SetActiveAsync(int id, bool active) => throw new NotSupportedException();
        public Task DeleteAsync(int id) => throw new NotSupportedException();
    }

    private sealed class FakeHttpFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new FakeHandler("{}"));
    }

    private static (AddressProviderResolver Resolver, FakeRepository Repo) Create(string? legacyGoogleKey = null)
    {
        var repo = new FakeRepository();
        var resolver = new AddressProviderResolver(repo, new FakeHttpFactory(),
            Options.Create(new GoogleMapsOptions { ApiKey = legacyGoogleKey }),
            new MemoryCache(new MemoryCacheOptions()), NullLoggerFactory.Instance);
        return (resolver, repo);
    }

    private static GeoProvider Row(int id, string type, bool active, string? key = "k") =>
        new() { ProviderId = id, ProviderType = type, Name = type + id, ApiKey = key, Region = "eu-north-1", IsActive = active };

    [Fact]
    public async Task NothingConfigured_AndNoLegacyKey_MeansOff()
    {
        var (resolver, _) = Create();
        Assert.Null(await resolver.GetActiveAsync());
    }

    [Fact]
    public async Task EmptyTable_FallsBackToTheLegacyGoogleSetting()
    {
        var (resolver, _) = Create(legacyGoogleKey: "AIza-test");
        var provider = await resolver.GetActiveAsync();
        Assert.Equal("Google", provider?.Name);
    }

    [Fact]
    public async Task TableHasRows_ButNoneActive_MeansOff_EvenWithALegacyKey()
    {
        var (resolver, repo) = Create(legacyGoogleKey: "AIza-test");
        repo.Rows.Add(Row(1, GeoProviderTypes.Google, active: false));
        Assert.Null(await resolver.GetActiveAsync());
    }

    [Fact]
    public async Task ActiveAmazonRow_IsUsed()
    {
        var (resolver, repo) = Create(legacyGoogleKey: "AIza-test");
        repo.Rows.Add(Row(1, GeoProviderTypes.Google, active: false));
        repo.Rows.Add(Row(2, GeoProviderTypes.AmazonLocation, active: true));
        Assert.Equal("Amazon", (await resolver.GetActiveAsync())?.Name);
    }

    [Fact]
    public async Task ActiveRowWithUnreadableKey_MeansOff()
    {
        var (resolver, repo) = Create();
        repo.Rows.Add(Row(1, GeoProviderTypes.AmazonLocation, active: true, key: null));
        Assert.Null(await resolver.GetActiveAsync());
    }

    [Fact]
    public async Task Result_IsCached_UntilInvalidated()
    {
        var (resolver, repo) = Create();
        repo.Rows.Add(Row(1, GeoProviderTypes.Google, active: true));

        await resolver.GetActiveAsync();
        await resolver.GetActiveAsync();
        Assert.Equal(1, repo.ActiveCalls);

        repo.Rows[0].IsActive = false;
        Assert.Equal("Google", (await resolver.GetActiveAsync())?.Name); // still the cached provider

        resolver.Invalidate();
        Assert.Null(await resolver.GetActiveAsync());
        Assert.Equal(2, repo.ActiveCalls);
    }

    [Fact]
    public void Create_UnknownType_Throws()
    {
        var (resolver, _) = Create();
        Assert.Throws<NotSupportedException>(() => resolver.Create(new GeoProvider { ProviderType = "Nope", ApiKey = "k" }));
    }
}
