using UserManagement.Models;

namespace UserManagement.Services;

/// <summary>
/// One address API (Google, Amazon Location, ...). AddressController only talks to this interface,
/// so a provider can be swapped from the Providers admin screen without touching the form code.
/// </summary>
public interface IAddressProvider
{
    /// <summary>Short name shown in the UI, e.g. "Google" or "Amazon".</summary>
    string Name { get; }

    Task<IReadOnlyList<AddressSuggestion>> AutocompleteAsync(string input, string? sessionToken, CancellationToken ct);
    Task<AddressDto?> GetPlaceAddressAsync(string placeId, string? sessionToken, CancellationToken ct);
    Task<AddressValidationResponse> ValidateAsync(AddressDto entered, CancellationToken ct);
}
