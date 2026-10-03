namespace UserManagement.Services;

/// <summary>Bound from the "GoogleMaps" section of appsettings.json / user secrets.</summary>
public class GoogleMapsOptions
{
    /// <summary>Server-side key with "Places API (New)" and "Address Validation API" enabled. Leave empty to turn the features off.</summary>
    public string? ApiKey { get; set; }

    // Overridable so the app can be pointed at a test double.
    public string PlacesBaseUrl { get; set; } = "https://places.googleapis.com";
    public string AddressValidationBaseUrl { get; set; } = "https://addressvalidation.googleapis.com";

    public bool Enabled => !string.IsNullOrWhiteSpace(ApiKey);
}
