using System.Net.Http.Json;
using System.Text.Json;
using UserManagement.Models;

namespace UserManagement.Services;

/// <summary>
/// Amazon Location Service "Places" API v2 (autocomplete, get place, geocode), called with an API key.
/// Amazon has no USPS-style address validation, so <see cref="ValidateAsync"/> geocodes the address and
/// judges the match quality instead.
/// </summary>
public class AmazonLocationAddressService : IAddressProvider
{
    // Place types that point at one specific address (not a street, city or ZIP code).
    private static readonly HashSet<string> AddressTypes = new()
    {
        "PointAddress", "InterpolatedAddress", "SecondaryAddress", "InferredSecondaryAddress"
    };

    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _baseUrl;
    private readonly ILogger _logger;

    public AmazonLocationAddressService(HttpClient http, string apiKey, string region, ILogger logger, string? baseUrl = null)
    {
        _http = http;
        _apiKey = apiKey;
        _baseUrl = baseUrl ?? $"https://places.geo.{region}.amazonaws.com";
        _logger = logger;
    }

    public string Name => "Amazon";

    // ---------------- Autocomplete ----------------

    public async Task<IReadOnlyList<AddressSuggestion>> AutocompleteAsync(string input, string? sessionToken, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/v2/autocomplete?key={Uri.EscapeDataString(_apiKey)}")
        {
            Content = JsonContent.Create(new
            {
                QueryText = input,
                MaxResults = 8,
                Language = "en",
                Filter = new { IncludeCountries = new[] { "USA" } }
            })
        };

        using var json = await SendAsync(request, ct);
        var suggestions = new List<AddressSuggestion>();
        if (!json.RootElement.TryGetProperty("ResultItems", out var items)) return suggestions;

        foreach (var item in items.EnumerateArray())
        {
            if (!AddressTypes.Contains(Str(item, "PlaceType"))) continue;
            var placeId = Str(item, "PlaceId");
            if (placeId.Length == 0) continue;

            var address = Obj(item, "Address");
            var title = Str(item, "Title");
            var number = Str(address, "AddressNumber");
            var street = Str(address, "Street");
            var main = number.Length > 0 && street.Length > 0 ? $"{number} {street}" : title.Split(',')[0].Trim();

            var secondary = string.Join(", ", new[]
            {
                Str(address, "Locality"),
                $"{StateCode(Obj(address, "Region"))} {Str(address, "PostalCode")}".Trim()
            }.Where(s => s.Length > 0));

            // Highlights are offsets into Title; they only line up if our main text is the start of the title.
            var matches = new List<TextMatch>();
            if (title.StartsWith(main, StringComparison.Ordinal)
                && Obj(item, "Highlights").ValueKind == JsonValueKind.Object
                && Obj(item, "Highlights").TryGetProperty("Title", out var hl) && hl.ValueKind == JsonValueKind.Array)
            {
                foreach (var h in hl.EnumerateArray())
                {
                    var start = h.TryGetProperty("StartIndex", out var s) ? s.GetInt32() : 0;
                    var end = h.TryGetProperty("EndIndex", out var e) ? e.GetInt32() : 0;
                    if (start < main.Length) matches.Add(new TextMatch(start, Math.Min(end, main.Length)));
                }
            }

            suggestions.Add(new AddressSuggestion(placeId, main, secondary, matches));
        }
        return suggestions;
    }

    // ---------------- Place details -> form fields ----------------

    public async Task<AddressDto?> GetPlaceAddressAsync(string placeId, string? sessionToken, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"{_baseUrl}/v2/place/{Uri.EscapeDataString(placeId)}?key={Uri.EscapeDataString(_apiKey)}&language=en");
        using var json = await SendAsync(request, ct);
        var address = Obj(json.RootElement, "Address");
        return address.ValueKind == JsonValueKind.Object ? ToDto(address) : null;
    }

    // ---------------- Validation (geocode match) ----------------

    public async Task<AddressValidationResponse> ValidateAsync(AddressDto entered, CancellationToken ct)
    {
        var text = string.Join(", ", new[]
        {
            string.Join(" ", new[] { entered.AddressLine1, entered.AddressLine2 }.Where(s => !string.IsNullOrWhiteSpace(s))),
            entered.City,
            $"{entered.StateCode} {entered.Zip5}".Trim()
        }.Where(s => !string.IsNullOrWhiteSpace(s)));

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/v2/geocode?key={Uri.EscapeDataString(_apiKey)}")
        {
            Content = JsonContent.Create(new
            {
                QueryText = text,
                MaxResults = 1,
                Language = "en",
                Filter = new { IncludeCountries = new[] { "USA" } }
            })
        };

        using var json = await SendAsync(request, ct);
        var response = new AddressValidationResponse();
        var top = json.RootElement.TryGetProperty("ResultItems", out var items) && items.GetArrayLength() > 0 ? items[0] : default;

        if (top.ValueKind != JsonValueKind.Object || !AddressTypes.Contains(Str(top, "PlaceType")))
        {
            response.Verdict = AddressVerdict.Unverified;
            response.Issues.Add("Amazon could not find this exact address. Check the street number and street name.");
            return response;
        }

        var suggested = ToDto(Obj(top, "Address"));
        if (string.IsNullOrEmpty(suggested.AddressLine2)) suggested.AddressLine2 = entered.AddressLine2; // geocode drops units we typed

        // Overall match score is 0..1; a missing score is treated as a decent (not perfect) match.
        var scores = Obj(top, "MatchScores");
        var overall = scores.ValueKind == JsonValueKind.Object && scores.TryGetProperty("Overall", out var o) && o.ValueKind == JsonValueKind.Number
            ? o.GetDouble() : 0.8;

        if (overall < 0.6)
        {
            response.Verdict = AddressVerdict.Unverified;
            response.Issues.Add("Amazon found only a weak match for this address.");
            return response;
        }

        if (GoogleAddressService.SameAddress(suggested, entered) && overall >= 0.9)
        {
            response.Verdict = AddressVerdict.Verified;
            return response;
        }

        response.Verdict = AddressVerdict.Suggested;
        response.Suggested = GoogleAddressService.SameAddress(suggested, entered) ? null : suggested;
        if (response.Suggested is null) response.Verdict = AddressVerdict.Verified;
        return response;
    }

    // ---------------- helpers ----------------

    private static AddressDto ToDto(JsonElement address)
    {
        var (zip5, zip4) = GoogleAddressService.SplitZip(Str(address, "PostalCode"));

        // Secondary components carry the apartment / suite: [{ "Number": "2100", "Designator": "Ste" }]
        string? line2 = null;
        if (address.ValueKind == JsonValueKind.Object && address.TryGetProperty("SecondaryAddressComponents", out var sec) && sec.ValueKind == JsonValueKind.Array)
        {
            var first = sec.EnumerateArray().FirstOrDefault();
            if (first.ValueKind == JsonValueKind.Object)
            {
                line2 = string.Join(" ", new[] { Str(first, "Designator"), Str(first, "Number") }.Where(s => s.Length > 0));
                if (line2.Length == 0) line2 = null;
            }
        }

        var number = Str(address, "AddressNumber");
        var street = Str(address, "Street");
        var locality = Str(address, "Locality");
        return new AddressDto
        {
            AddressLine1 = string.Join(" ", new[] { number, street }.Where(s => s.Length > 0)),
            AddressLine2 = line2,
            City = locality.Length > 0 ? locality : Str(address, "District"),
            StateCode = StateCode(Obj(address, "Region")),
            Zip5 = zip5,
            Zip4 = zip4,
        };
    }

    /// <summary>Region.Code may be "NY" or "US-NY"; the form wants the two letters.</summary>
    private static string StateCode(JsonElement region)
    {
        var code = Str(region, "Code");
        var dash = code.LastIndexOf('-');
        return (dash >= 0 ? code[(dash + 1)..] : code).ToUpperInvariant();
    }

    private async Task<JsonDocument> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        using var response = await _http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Amazon Location {Url} returned {Status}: {Body}",
                request.RequestUri?.GetLeftPart(UriPartial.Path), (int)response.StatusCode, body.Length > 500 ? body[..500] : body);
            throw new HttpRequestException($"Amazon Location returned {(int)response.StatusCode}.");
        }
        return JsonDocument.Parse(body);
    }

    private static JsonElement Obj(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Object ? p : default;

    private static string Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString()! : "";
}
