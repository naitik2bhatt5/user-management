using Microsoft.AspNetCore.Mvc;
using UserManagement.Models;
using UserManagement.Services;

namespace UserManagement.Controllers;

/// <summary>JSON endpoints used by address-autocomplete.js. They proxy Google so the API key stays on the server.</summary>
[Route("Address")]
public class AddressController : Controller
{
    private readonly GoogleAddressService _google;
    private readonly ILogger<AddressController> _logger;

    public AddressController(GoogleAddressService google, ILogger<AddressController> logger)
    {
        _google = google;
        _logger = logger;
    }

    // GET /Address/Autocomplete?q=350%205th&session=...
    [HttpGet("Autocomplete")]
    public async Task<IActionResult> Autocomplete(string? q, string? session, CancellationToken ct)
    {
        if (!_google.Enabled) return NotFound(new { enabled = false });
        q = q?.Trim();
        if (string.IsNullOrEmpty(q) || q.Length < 3 || q.Length > 200) return Json(Array.Empty<AddressSuggestion>());

        try
        {
            return Json(await _google.AutocompleteAsync(q, session, ct));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Address autocomplete failed.");
            return StatusCode(502, new { message = "Address suggestions are unavailable right now." });
        }
    }

    // GET /Address/Place/{placeId}?session=...
    [HttpGet("Place/{placeId}")]
    public async Task<IActionResult> Place(string placeId, string? session, CancellationToken ct)
    {
        if (!_google.Enabled) return NotFound(new { enabled = false });
        try
        {
            var address = await _google.GetPlaceAddressAsync(placeId, session, ct);
            return address is null ? NotFound(new { message = "Address not found." }) : Json(address);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Place details failed.");
            return StatusCode(502, new { message = "Could not load that address. Please type it in." });
        }
    }

    // POST /Address/Validate   body: AddressDto   (antiforgery token in RequestVerificationToken header)
    [HttpPost("Validate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Validate([FromBody] AddressDto? address, CancellationToken ct)
    {
        if (!_google.Enabled) return NotFound(new { enabled = false });
        if (address is null || string.IsNullOrWhiteSpace(address.AddressLine1))
            return BadRequest(new { message = "Address line 1 is required." });

        try
        {
            return Json(await _google.ValidateAsync(address, ct));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or KeyNotFoundException)
        {
            // Never block saving a user because Google is down or the key is misconfigured.
            _logger.LogWarning(ex, "Address validation failed.");
            return Json(new AddressValidationResponse { Verdict = AddressVerdict.Unavailable });
        }
    }
}
