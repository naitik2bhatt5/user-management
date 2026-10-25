using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using UserManagement.Models;
using UserManagement.Services;

namespace UserManagement.Tests;

public class AmazonLocationAddressServiceTests
{
    private static (AmazonLocationAddressService Service, FakeHandler Handler) Create(string body, HttpStatusCode status = HttpStatusCode.OK)
    {
        var handler = new FakeHandler(body, status);
        var service = new AmazonLocationAddressService(new HttpClient(handler), "test-key", "eu-north-1", NullLogger.Instance);
        return (service, handler);
    }

    // ---------------- Autocomplete ----------------

    [Fact]
    public async Task Autocomplete_BuildsStreetAndCityLines_AndSkipsNonAddresses()
    {
        var (service, handler) = Create("""
        {
          "ResultItems": [
            { "PlaceId": "p1", "PlaceType": "PointAddress", "Title": "350 5th Ave, New York, NY 10118, United States",
              "Address": { "Label": "350 5th Ave, New York, NY 10118, United States", "AddressNumber": "350", "Street": "5th Ave",
                           "Locality": "New York", "PostalCode": "10118", "Region": { "Code": "NY" } },
              "Highlights": { "Title": [ { "StartIndex": 0, "EndIndex": 3 } ] } },
            { "PlaceId": "p2", "PlaceType": "Country", "Title": "United States", "Address": { "Label": "United States" } },
            { "PlaceId": "p3", "PlaceType": "PointAddress", "Title": "United States", "Address": { "Label": "United States" } },
            { "PlaceId": "p4", "PlaceType": "Locality", "Title": "New York, NY, United States", "Address": { "Label": "New York, NY, United States" } }
          ]
        }
        """);

        var result = await service.AutocompleteAsync("350 5th", null, CancellationToken.None);

        var only = Assert.Single(result);
        Assert.Equal("p1", only.PlaceId);
        Assert.Equal("350 5th Ave", only.MainText);
        Assert.Equal("New York, NY 10118", only.SecondaryText);
        Assert.Equal(new TextMatch(0, 3), Assert.Single(only.MainMatches));

        Assert.Equal("places.geo.eu-north-1.amazonaws.com", handler.LastRequest!.RequestUri!.Host);
        Assert.Contains("key=test-key", handler.LastRequest.RequestUri.Query);
        Assert.Contains("USA", handler.LastRequestBody);
    }

    [Fact]
    public async Task Autocomplete_FallsBackToLabel_WhenNumberAndStreetAreMissing()
    {
        var (service, _) = Create("""
        { "ResultItems": [ { "PlaceId": "p1", "PlaceType": "PointAddress", "Title": "x",
            "Address": { "Label": "233 S Wacker Dr, Chicago, IL 60606, United States" } } ] }
        """);

        var result = await service.AutocompleteAsync("233 s", null, CancellationToken.None);

        var item = Assert.Single(result);
        Assert.Equal("233 S Wacker Dr", item.MainText);
        Assert.Equal("Chicago, IL 60606", item.SecondaryText);
    }

    [Fact]
    public async Task Autocomplete_NoResultItems_ReturnsEmpty()
    {
        var (service, _) = Create("{}");
        Assert.Empty(await service.AutocompleteAsync("zzz", null, CancellationToken.None));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Autocomplete_HttpError_Throws(HttpStatusCode status)
    {
        var (service, _) = Create("""{ "message": "denied" }""", status);
        await Assert.ThrowsAsync<HttpRequestException>(() => service.AutocompleteAsync("350 5th", null, CancellationToken.None));
    }

    // ---------------- Place details ----------------

    [Fact]
    public async Task GetPlace_MapsFormFields_IncludingUnitAndZipPlus4()
    {
        var (service, handler) = Create("""
        { "PlaceId": "p1", "Address": { "AddressNumber": "350", "Street": "5th Ave", "Locality": "New York",
            "Region": { "Code": "US-NY" }, "PostalCode": "10118-0110",
            "SecondaryAddressComponents": [ { "Number": "2100", "Designator": "Ste" } ] } }
        """);

        var address = await service.GetPlaceAddressAsync("p1", null, CancellationToken.None);

        Assert.NotNull(address);
        Assert.Equal("350 5th Ave", address!.AddressLine1);
        Assert.Equal("Ste 2100", address.AddressLine2);
        Assert.Equal("New York", address.City);
        Assert.Equal("NY", address.StateCode);
        Assert.Equal("10118", address.Zip5);
        Assert.Equal("0110", address.Zip4);
        Assert.Contains("/v2/place/p1", handler.LastRequest!.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task GetPlace_NoAddress_ReturnsNull()
    {
        var (service, _) = Create("""{ "PlaceId": "p1" }""");
        Assert.Null(await service.GetPlaceAddressAsync("p1", null, CancellationToken.None));
    }

    // ---------------- Validation ----------------

    private static AddressDto Entered() => new()
    {
        AddressLine1 = "350 5th Ave", City = "New York", StateCode = "NY", Zip5 = "10118"
    };

    /// <summary>A one-result geocode response for "350 5th Ave, New York, NY 10118" (or another street line).</summary>
    private static string Geocode(string placeType, double score, string line = "350 5th Ave")
    {
        var number = line.Split(' ')[0];
        var street = line[(line.IndexOf(' ') + 1)..];
        var overall = score.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return "{ \"ResultItems\": [ { \"PlaceType\": \"" + placeType + "\", "
             + "\"Address\": { \"AddressNumber\": \"" + number + "\", \"Street\": \"" + street + "\", \"Locality\": \"New York\", "
             + "\"Region\": { \"Code\": \"NY\" }, \"PostalCode\": \"10118\" }, "
             + "\"MatchScores\": { \"Overall\": " + overall + " } } ] }";
    }

    [Fact]
    public async Task Validate_ExactMatch_IsVerified()
    {
        var (service, _) = Create(Geocode("PointAddress", 0.99));
        var result = await service.ValidateAsync(Entered(), CancellationToken.None);
        Assert.Equal(AddressVerdict.Verified, result.Verdict);
        Assert.Null(result.Suggested);
    }

    [Fact]
    public async Task Validate_DifferentFormatting_IsSuggested()
    {
        var (service, _) = Create(Geocode("PointAddress", 0.95, line: "350 Fifth Avenue"));
        var result = await service.ValidateAsync(Entered(), CancellationToken.None);
        Assert.Equal(AddressVerdict.Suggested, result.Verdict);
        Assert.Equal("350 Fifth Avenue", result.Suggested!.AddressLine1);
    }

    [Fact]
    public async Task Validate_KeepsUsersZipPlus4_AndUnit()
    {
        var entered = Entered();
        entered.AddressLine2 = "Suite 2100";
        entered.Zip4 = "0110";
        var (service, _) = Create(Geocode("PointAddress", 0.99));

        var result = await service.ValidateAsync(entered, CancellationToken.None);

        Assert.Equal(AddressVerdict.Verified, result.Verdict); // nothing to correct: unit and Zip+4 were kept
    }

    [Theory]
    [InlineData("Locality")]
    [InlineData("Street")]
    [InlineData("PostalCode")]
    public async Task Validate_NotASpecificAddress_IsUnverified(string placeType)
    {
        var (service, _) = Create(Geocode(placeType, 0.99));
        var result = await service.ValidateAsync(Entered(), CancellationToken.None);
        Assert.Equal(AddressVerdict.Unverified, result.Verdict);
        Assert.NotEmpty(result.Issues);
    }

    [Fact]
    public async Task Validate_WeakMatch_IsUnverified()
    {
        var (service, _) = Create(Geocode("PointAddress", 0.3));
        var result = await service.ValidateAsync(Entered(), CancellationToken.None);
        Assert.Equal(AddressVerdict.Unverified, result.Verdict);
    }

    [Fact]
    public async Task Validate_NoResults_IsUnverified()
    {
        var (service, _) = Create("""{ "ResultItems": [] }""");
        var result = await service.ValidateAsync(Entered(), CancellationToken.None);
        Assert.Equal(AddressVerdict.Unverified, result.Verdict);
    }

    [Fact]
    public async Task Validate_SendsFullAddressAsQueryText()
    {
        var entered = Entered();
        entered.AddressLine2 = "Apt 4B";
        var (service, handler) = Create(Geocode("PointAddress", 0.99));

        await service.ValidateAsync(entered, CancellationToken.None);

        Assert.Contains("350 5th Ave Apt 4B, New York, NY 10118", handler.LastRequestBody);
        Assert.Contains("/v2/geocode", handler.LastRequest!.RequestUri!.AbsolutePath);
    }
}
