using System.ComponentModel.DataAnnotations;

namespace UserManagement.Models;

public static class GeoProviderTypes
{
    public const string Google = "Google";
    public const string AmazonLocation = "AmazonLocation";

    public static readonly string[] All = { Google, AmazonLocation };

    public static string Display(string type) => type switch
    {
        Google => "Google Maps Platform",
        AmazonLocation => "Amazon Location Service",
        _ => type
    };

    /// <summary>Short name used in the address UI ("Address filled in from ...").</summary>
    public static string ShortName(string type) => type == AmazonLocation ? "Amazon" : "Google";
}

/// <summary>One row of dbo.GeoProviders. <see cref="ApiKey"/> is the plain key, only held in memory.</summary>
public class GeoProvider
{
    public int ProviderId { get; set; }

    [Required, Display(Name = "Provider")]
    public string ProviderType { get; set; } = GeoProviderTypes.Google;

    [Required, StringLength(100), Display(Name = "Name")]
    public string Name { get; set; } = "";

    /// <summary>Plain-text key. Empty on the edit form means "keep the stored key".</summary>
    [Display(Name = "API key")]
    public string? ApiKey { get; set; }

    [StringLength(30), Display(Name = "AWS region")]
    public string? Region { get; set; }

    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    /// <summary>Masked key for display, e.g. "AIza••••k9Qz". Never the full key.</summary>
    public string? ApiKeyMasked { get; set; }

    public string TypeDisplay => GeoProviderTypes.Display(ProviderType);
}
