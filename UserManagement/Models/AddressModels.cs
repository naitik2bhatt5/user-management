namespace UserManagement.Models;

/// <summary>An address in the same shape as the user form.</summary>
public class AddressDto
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? StateCode { get; set; }
    public string? Zip5 { get; set; }
    public string? Zip4 { get; set; }
}

public record TextMatch(int Start, int End);

public record AddressSuggestion(string PlaceId, string MainText, string SecondaryText, IReadOnlyList<TextMatch> MainMatches);

public static class AddressVerdict
{
    public const string Verified = "verified";      // matches a known deliverable address as entered
    public const string Suggested = "suggested";    // known address, but Google corrected or completed something
    public const string Unverified = "unverified";  // could not be confirmed; user decides
    public const string Unavailable = "unavailable";// API error/timeout; never blocks saving
}

public class AddressValidationResponse
{
    public string Verdict { get; set; } = AddressVerdict.Unavailable;
    public AddressDto? Suggested { get; set; }
    public List<string> Issues { get; set; } = new();
    public bool NeedsUnitNumber { get; set; }
}
