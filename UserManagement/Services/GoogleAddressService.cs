using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using UserManagement.Models;

namespace UserManagement.Services;

/// <summary>
/// Thin server-side wrapper over Google Places Autocomplete (New), Place Details (New) and the
/// Address Validation API. Calls are made from the server so the API key never reaches the browser.
/// </summary>
public class GoogleAddressService
{
    private readonly HttpClient _http;
    private readonly GoogleMapsOptions _options;
    private readonly ILogger<GoogleAddressService> _logger;

    public GoogleAddressService(HttpClient http, IOptions<GoogleMapsOptions> options, ILogger<GoogleAddressService> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public bool Enabled => _options.Enabled;

    // ---------------- Autocomplete ----------------

    public async Task<IReadOnlyList<AddressSuggestion>> AutocompleteAsync(string input, string? sessionToken, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_options.PlacesBaseUrl}/v1/places:autocomplete")
        {
            Content = JsonContent.Create(new
            {
                input,
                sessionToken,
                includedRegionCodes = new[] { "us" },
                includedPrimaryTypes = new[] { "street_address", "premise", "subpremise" },
                languageCode = "en-US"
            })
        };
        request.Headers.Add("X-Goog-Api-Key", _options.ApiKey);
        request.Headers.Add("X-Goog-FieldMask",
            "suggestions.placePrediction.placeId,suggestions.placePrediction.structuredFormat");

        using var json = await SendAsync(request, ct);
        var suggestions = new List<AddressSuggestion>();
        if (!json.RootElement.TryGetProperty("suggestions", out var items)) return suggestions;

        foreach (var item in items.EnumerateArray())
        {
            if (!item.TryGetProperty("placePrediction", out var p)) continue;
            var format = p.GetProperty("structuredFormat");
            var main = format.GetProperty("mainText");
            var matches = new List<TextMatch>();
            if (main.TryGetProperty("matches", out var m))
            {
                foreach (var match in m.EnumerateArray())
                {
                    matches.Add(new TextMatch(
                        match.TryGetProperty("startOffset", out var s) ? s.GetInt32() : 0,
                        match.TryGetProperty("endOffset", out var e) ? e.GetInt32() : 0));
                }
            }

            suggestions.Add(new AddressSuggestion(
                p.GetProperty("placeId").GetString()!,
                Str(main, "text"),
                format.TryGetProperty("secondaryText", out var sec) ? Str(sec, "text") : "",
                matches));
        }
        return suggestions;
    }

    // ---------------- Place details -> form fields ----------------

    public async Task<AddressDto?> GetPlaceAddressAsync(string placeId, string? sessionToken, CancellationToken ct)
    {
        var url = $"{_options.PlacesBaseUrl}/v1/places/{Uri.EscapeDataString(placeId)}";
        if (!string.IsNullOrEmpty(sessionToken)) url += $"?sessionToken={Uri.EscapeDataString(sessionToken)}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-Goog-Api-Key", _options.ApiKey);
        request.Headers.Add("X-Goog-FieldMask", "addressComponents");

        using var json = await SendAsync(request, ct);
        if (!json.RootElement.TryGetProperty("addressComponents", out var components)) return null;

        string? Find(string type, bool shortText = false)
        {
            foreach (var c in components.EnumerateArray())
            {
                if (c.GetProperty("types").EnumerateArray().Any(t => t.GetString() == type))
                    return Str(c, shortText ? "shortText" : "longText");
            }
            return null;
        }

        var unit = Find("subpremise");
        var address = new AddressDto
        {
            AddressLine1 = string.Join(" ", new[] { Find("street_number"), Find("route", shortText: true) }.Where(s => !string.IsNullOrEmpty(s))),
            AddressLine2 = string.IsNullOrEmpty(unit) ? null : (Regex.IsMatch(unit, @"^[\w-]+$") ? $"#{unit}" : unit),
            City = Find("locality") ?? Find("sublocality_level_1") ?? Find("postal_town") ?? Find("neighborhood"),
            StateCode = Find("administrative_area_level_1", shortText: true),
            Zip5 = Find("postal_code"),
            Zip4 = Find("postal_code_suffix"),
        };
        return address;
    }

    // ---------------- Address validation ----------------

    public async Task<AddressValidationResponse> ValidateAsync(AddressDto entered, CancellationToken ct)
    {
        var lines = new[] { entered.AddressLine1, entered.AddressLine2 }.Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
        var postal = string.IsNullOrEmpty(entered.Zip4) ? entered.Zip5 : $"{entered.Zip5}-{entered.Zip4}";

        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"{_options.AddressValidationBaseUrl}/v1:validateAddress?key={Uri.EscapeDataString(_options.ApiKey!)}")
        {
            Content = JsonContent.Create(new
            {
                address = new
                {
                    regionCode = "US",
                    addressLines = lines,
                    locality = entered.City,
                    administrativeArea = entered.StateCode,
                    postalCode = postal
                },
                enableUspsCass = true
            })
        };

        using var json = await SendAsync(request, ct);
        return Interpret(json.RootElement.GetProperty("result"), entered);
    }

    /// <summary>Turns Google's verdict into one of: verified / suggested / unverified, plus a suggested address.</summary>
    internal static AddressValidationResponse Interpret(JsonElement result, AddressDto entered)
    {
        var response = new AddressValidationResponse();
        var verdict = result.TryGetProperty("verdict", out var v) ? v : default;
        var address = result.TryGetProperty("address", out var a) ? a : default;

        string granularity = verdict.ValueKind == JsonValueKind.Object ? Str(verdict, "validationGranularity") : "";
        string nextAction = verdict.ValueKind == JsonValueKind.Object ? Str(verdict, "possibleNextAction") : "";
        bool complete = Bool(verdict, "addressComplete");
        bool unconfirmed = Bool(verdict, "hasUnconfirmedComponents");
        bool changed = Bool(verdict, "hasInferredComponents") || Bool(verdict, "hasReplacedComponents") || Bool(verdict, "hasSpellCorrectedComponents");

        response.Suggested = BuildSuggestion(result, address, entered);

        // Explain what Google could not confirm, in plain words.
        if (address.ValueKind == JsonValueKind.Object)
        {
            foreach (var t in Strings(address, "missingComponentTypes").Where(t => t != "subpremise"))
                response.Issues.Add($"Missing {FriendlyComponent(t)}.");
            foreach (var t in Strings(address, "unconfirmedComponentTypes"))
                response.Issues.Add($"Could not confirm the {FriendlyComponent(t)}.");
            foreach (var t in Strings(address, "unresolvedTokens"))
                response.Issues.Add($"Did not recognize \"{t}\".");
        }

        response.NeedsUnitNumber = nextAction == "CONFIRM_ADD_SUBPREMISES"
            || Strings(address, "missingComponentTypes").Contains("subpremise");
        if (response.NeedsUnitNumber)
            response.Issues.Add("This address has multiple units. Add an apartment, suite or unit number in Address Line 2.");

        var premiseLevel = granularity is "PREMISE" or "SUB_PREMISE";
        var acceptable = nextAction is "ACCEPT" or "CONFIRM" or ""
            ? premiseLevel && complete && !unconfirmed
            : false;

        var sameAsEntered = response.Suggested is not null && SameAddress(response.Suggested, entered);

        if (acceptable && sameAsEntered && !response.NeedsUnitNumber)
            response.Verdict = AddressVerdict.Verified;
        else if (premiseLevel && response.Suggested is not null && !sameAsEntered && nextAction != "FIX")
            response.Verdict = AddressVerdict.Suggested;
        else if (acceptable && changed && !sameAsEntered)
            response.Verdict = AddressVerdict.Suggested;
        else
            response.Verdict = AddressVerdict.Unverified;

        if (response.Verdict == AddressVerdict.Unverified && response.Issues.Count == 0)
            response.Issues.Add("Google could not find this address. Check the street number and street name.");

        // Don't offer a "suggestion" that is identical to what was typed.
        if (sameAsEntered && response.Verdict != AddressVerdict.Suggested) response.Suggested = null;
        return response;
    }

    private static AddressDto? BuildSuggestion(JsonElement result, JsonElement address, AddressDto entered)
    {
        // Prefer Google's corrected postal address (proper case); fall back to USPS standardized data.
        if (address.ValueKind == JsonValueKind.Object && address.TryGetProperty("postalAddress", out var pa))
        {
            var lines = Strings(pa, "addressLines");
            var line1 = lines.ElementAtOrDefault(0);
            var line2 = lines.ElementAtOrDefault(1);

            // Google often folds the unit into line 1 ("350 5th Ave Ste 2100"); split it back out.
            if (line2 is null && line1 is not null && address.TryGetProperty("addressComponents", out var comps))
            {
                foreach (var c in comps.EnumerateArray())
                {
                    if (Str(c, "componentType") != "subpremise") continue;
                    var unit = c.TryGetProperty("componentName", out var n) ? Str(n, "text") : "";
                    if (unit.Length > 0 && line1.EndsWith(" " + unit, StringComparison.OrdinalIgnoreCase))
                    {
                        line1 = line1[..^(unit.Length + 1)].TrimEnd(',', ' ');
                        line2 = unit;
                    }
                }
            }

            var (zip5, zip4) = SplitZip(Str(pa, "postalCode"));
            if (string.IsNullOrEmpty(zip4) && result.TryGetProperty("uspsData", out var usps1)
                && usps1.TryGetProperty("standardizedAddress", out var sa1))
            {
                zip4 = NullIfEmpty(Str(sa1, "zipCodeExtension"));
            }

            if (line1 is not null)
            {
                return new AddressDto
                {
                    AddressLine1 = line1,
                    AddressLine2 = line2,
                    City = NullIfEmpty(Str(pa, "locality")) ?? entered.City,
                    StateCode = NullIfEmpty(Str(pa, "administrativeArea"))?.ToUpperInvariant() ?? entered.StateCode,
                    Zip5 = zip5 ?? entered.Zip5,
                    Zip4 = zip4,
                };
            }
        }

        if (result.TryGetProperty("uspsData", out var usps) && usps.TryGetProperty("standardizedAddress", out var sa))
        {
            return new AddressDto
            {
                AddressLine1 = NullIfEmpty(Str(sa, "firstAddressLine")),
                AddressLine2 = NullIfEmpty(Str(sa, "secondAddressLine")),
                City = NullIfEmpty(Str(sa, "city")),
                StateCode = NullIfEmpty(Str(sa, "state")),
                Zip5 = NullIfEmpty(Str(sa, "zipCode")),
                Zip4 = NullIfEmpty(Str(sa, "zipCodeExtension")),
            };
        }
        return null;
    }

    private static bool SameAddress(AddressDto a, AddressDto b)
    {
        static string N(string? s) => Regex.Replace((s ?? "").Trim().ToUpperInvariant(), @"[\s.,#]+", " ").Trim();
        return N(a.AddressLine1) == N(b.AddressLine1)
            && N(a.AddressLine2) == N(b.AddressLine2)
            && N(a.City) == N(b.City)
            && N(a.StateCode) == N(b.StateCode)
            && N(a.Zip5) == N(b.Zip5)
            // Google adding a Zip+4 the user left blank still counts as a suggestion.
            && N(a.Zip4) == N(b.Zip4);
    }

    private static string FriendlyComponent(string type) => type switch
    {
        "street_number" => "street number",
        "route" => "street name",
        "subpremise" => "apartment / suite number",
        "locality" or "postal_town" => "city",
        "administrative_area_level_1" => "state",
        "postal_code" => "zip code",
        "postal_code_suffix" => "Zip+4",
        "point_of_interest" or "premise" => "building",
        _ => type.Replace('_', ' ')
    };

    private static (string? zip5, string? zip4) SplitZip(string postal)
    {
        var m = Regex.Match(postal ?? "", @"^(\d{5})(?:-?(\d{4}))?$");
        return m.Success ? (m.Groups[1].Value, m.Groups[2].Success ? m.Groups[2].Value : null) : (null, null);
    }

    private async Task<JsonDocument> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        using var response = await _http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Google API {Url} returned {Status}: {Body}",
                request.RequestUri?.GetLeftPart(UriPartial.Path), (int)response.StatusCode, body.Length > 500 ? body[..500] : body);
            throw new HttpRequestException($"Google API returned {(int)response.StatusCode}.");
        }
        return JsonDocument.Parse(body);
    }

    private static string Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString()! : "";

    private static bool Bool(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.True;

    private static List<string> Strings(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Array
            ? p.EnumerateArray().Select(x => x.GetString() ?? "").ToList()
            : new List<string>();

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
