using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;
using UserManagement.Data;
using UserManagement.Models;

namespace UserManagement.Controllers;

public class UsersController : Controller
{
    private readonly IUserRepository _users;
    private readonly IStateRepository _states;

    public UsersController(IUserRepository users, IStateRepository states)
    {
        _users = users;
        _states = states;
    }

    // GET /Users
    public async Task<IActionResult> Index()
    {
        var users = await _users.GetAllAsync();
        return View(users);
    }

    // GET /Users/Create
    public async Task<IActionResult> Create()
    {
        await LoadStatesAsync();
        return View("Form", new User());
    }

    // POST /Users/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(User user)
    {
        user.UserId = 0;
        if (!await ValidateUserAsync(user))
        {
            await LoadStatesAsync(user.StateCode);
            return View("Form", user);
        }

        await _users.CreateAsync(user);
        TempData["Message"] = $"User \"{user.FullName}\" was added.";
        return RedirectToAction(nameof(Index));
    }

    // GET /Users/Edit/5
    public async Task<IActionResult> Edit(int id)
    {
        var user = await _users.GetByIdAsync(id);
        if (user is null) return NotFound();

        await LoadStatesAsync(user.StateCode);
        return View("Form", user);
    }

    // POST /Users/Edit/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, User user)
    {
        user.UserId = id;
        if (!await ValidateUserAsync(user))
        {
            await LoadStatesAsync(user.StateCode);
            return View("Form", user);
        }

        if (!await _users.UpdateAsync(user))
        {
            TempData["Error"] = "That user no longer exists. It may have been deleted by someone else.";
            return RedirectToAction(nameof(Index));
        }

        TempData["Message"] = $"User \"{user.FullName}\" was updated.";
        return RedirectToAction(nameof(Index));
    }

    // POST /Users/Delete/5   (AJAX; antiforgery token sent in the RequestVerificationToken header)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var deleted = await _users.DeleteAsync(id);
        return Json(new { success = true, deleted = deleted ? 1 : 0 });
    }

    // POST /Users/DeleteMultiple   body: { "ids": [1, 2, 3] }
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteMultiple([FromBody] DeleteUsersRequest? request)
    {
        var ids = request?.Ids?.Where(id => id > 0).Distinct().ToList() ?? new List<int>();
        if (ids.Count == 0)
            return BadRequest(new { success = false, message = "No users were selected." });
        if (ids.Count > UserRepository.MaxBatchDelete)
            return BadRequest(new { success = false, message = $"You can delete at most {UserRepository.MaxBatchDelete} users at once." });

        var deleted = await _users.DeleteManyAsync(ids);
        return Json(new { success = true, requested = ids.Count, deleted });
    }

    /// <summary>Normalizes input, re-runs model validation and checks the state against the lookup table.</summary>
    private async Task<bool> ValidateUserAsync(User user)
    {
        user.Normalize();
        ModelState.Clear();
        TryValidateModel(user);

        // MVC skips IValidatableObject.Validate when any attribute fails, so run it here too
        // to report every problem (DOB range, phone format, zip) in a single round-trip.
        if (!ModelState.IsValid)
        {
            foreach (var result in user.Validate(new ValidationContext(user)))
            foreach (var member in result.MemberNames)
            {
                if (ModelState.GetFieldValidationState(member) != ModelValidationState.Invalid)
                    ModelState.AddModelError(member, result.ErrorMessage!);
            }
        }

        if (user.StateCode is not null
            && ModelState.GetFieldValidationState(nameof(Models.User.StateCode)) != ModelValidationState.Invalid
            && !await _states.ExistsAsync(user.StateCode))
        {
            ModelState.AddModelError(nameof(Models.User.StateCode), "Select a valid state.");
        }

        return ModelState.IsValid;
    }

    private async Task LoadStatesAsync(string? selected = null)
    {
        var states = await _states.GetAllAsync();
        ViewBag.States = new SelectList(states, nameof(State.StateCode), nameof(State.StateName), selected);
    }
}
