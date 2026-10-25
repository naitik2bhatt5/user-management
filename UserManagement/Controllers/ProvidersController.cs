using Microsoft.AspNetCore.Mvc;
using UserManagement.Data;
using UserManagement.Models;
using UserManagement.Services;

namespace UserManagement.Controllers;

/// <summary>
/// Master config for location / address APIs: add keys, switch the active provider, turn one off.
/// Requires the admin sign-in (AccountController); every action here is protected.
/// </summary>
[Microsoft.AspNetCore.Authorization.Authorize]
public class ProvidersController : Controller
{
    private readonly IGeoProviderRepository _providers;
    private readonly IAddressProviderResolver _resolver;

    public ProvidersController(IGeoProviderRepository providers, IAddressProviderResolver resolver)
    {
        _providers = providers;
        _resolver = resolver;
    }

    // GET /Providers
    public async Task<IActionResult> Index()
    {
        ViewBag.Active = await _resolver.GetActiveAsync();
        return View(await _providers.GetAllAsync());
    }

    // GET /Providers/Create
    public IActionResult Create() => View("Form", new GeoProvider { Region = "us-east-1" });

    // POST /Providers/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(GeoProvider provider)
    {
        provider.ProviderId = 0;
        Validate(provider, isNew: true);
        if (!ModelState.IsValid) return View("Form", provider);

        await _providers.CreateAsync(provider);
        _resolver.Invalidate();
        TempData["Message"] = $"\"{provider.Name}\" was added" + (provider.IsActive ? " and is now the active provider." : ".");
        return RedirectToAction(nameof(Index));
    }

    // GET /Providers/Edit/5
    public async Task<IActionResult> Edit(int id)
    {
        var provider = await _providers.GetAsync(id);
        if (provider is null) return NotFound();
        provider.ApiKeyMasked = string.IsNullOrEmpty(provider.ApiKey) ? null : Mask(provider.ApiKey);
        provider.ApiKey = null; // never send the key back to the browser
        return View("Form", provider);
    }

    // POST /Providers/Edit/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, GeoProvider provider)
    {
        provider.ProviderId = id;
        var existing = await _providers.GetAsync(id);
        if (existing is null) return NotFound();

        Validate(provider, isNew: false);
        if (!ModelState.IsValid)
        {
            provider.ApiKeyMasked = string.IsNullOrEmpty(existing.ApiKey) ? null : Mask(existing.ApiKey);
            provider.ApiKey = null;
            return View("Form", provider);
        }

        await _providers.UpdateAsync(provider);
        _resolver.Invalidate();
        TempData["Message"] = $"\"{provider.Name}\" was saved.";
        return RedirectToAction(nameof(Index));
    }

    // POST /Providers/Activate/5   (activating one turns every other one off)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Activate(int id)
    {
        var provider = await _providers.GetAsync(id);
        if (provider is null) return NotFound();
        if (string.IsNullOrWhiteSpace(provider.ApiKey))
        {
            TempData["Error"] = $"\"{provider.Name}\" has no readable API key. Edit it and enter the key first.";
            return RedirectToAction(nameof(Index));
        }

        await _providers.SetActiveAsync(id, true);
        _resolver.Invalidate();
        TempData["Message"] = $"\"{provider.Name}\" is now the active address provider.";
        return RedirectToAction(nameof(Index));
    }

    // POST /Providers/Deactivate/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Deactivate(int id)
    {
        var provider = await _providers.GetAsync(id);
        if (provider is null) return NotFound();

        await _providers.SetActiveAsync(id, false);
        _resolver.Invalidate();
        TempData["Message"] = $"\"{provider.Name}\" was deactivated. Address suggestions are off until you activate a provider.";
        return RedirectToAction(nameof(Index));
    }

    // POST /Providers/Delete/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var provider = await _providers.GetAsync(id);
        if (provider is null) return NotFound();

        await _providers.DeleteAsync(id);
        _resolver.Invalidate();
        TempData["Message"] = $"\"{provider.Name}\" was deleted.";
        return RedirectToAction(nameof(Index));
    }

    // POST /Providers/Test/5   makes one real autocomplete call with the saved key and reports the result
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Test(int id, CancellationToken ct)
    {
        var config = await _providers.GetAsync(id);
        if (config is null) return NotFound();
        if (string.IsNullOrWhiteSpace(config.ApiKey))
        {
            TempData["Error"] = $"\"{config.Name}\": no readable API key. Edit it and enter the key again.";
            return RedirectToAction(nameof(Index));
        }

        try
        {
            var results = await _resolver.Create(config).AutocompleteAsync("350 5th Ave New York", null, ct);
            if (results.Count > 0)
                TempData["Message"] = $"\"{config.Name}\" works: got {results.Count} suggestion(s), e.g. {results[0].MainText}, {results[0].SecondaryText}.";
            else
                TempData["Error"] = $"\"{config.Name}\": the key was accepted but returned no suggestions. Check that the right APIs/actions are allowed for this key.";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            TempData["Error"] = $"\"{config.Name}\" failed: {ex.Message} See the server log for details (usually a wrong key, region, or a missing permission).";
        }
        return RedirectToAction(nameof(Index));
    }

    // ---------------- helpers ----------------

    private void Validate(GeoProvider p, bool isNew)
    {
        ModelState.Remove(nameof(GeoProvider.ApiKey));
        if (!GeoProviderTypes.All.Contains(p.ProviderType))
            ModelState.AddModelError(nameof(GeoProvider.ProviderType), "Choose a provider.");
        if (isNew && string.IsNullOrWhiteSpace(p.ApiKey))
            ModelState.AddModelError(nameof(GeoProvider.ApiKey), "Enter the API key.");
        if (p.ProviderType == GeoProviderTypes.AmazonLocation)
        {
            if (string.IsNullOrWhiteSpace(p.Region) || !System.Text.RegularExpressions.Regex.IsMatch(p.Region.Trim(), @"^[a-z]{2}(-[a-z]+)+-\d$"))
                ModelState.AddModelError(nameof(GeoProvider.Region), "Enter the AWS region, e.g. us-east-1.");
        }
        else
        {
            p.Region = null;
        }
    }

    private static string Mask(string key) =>
        key.Length <= 8 ? new string('*', key.Length) : $"{key[..4]}******{key[^4..]}";
}
