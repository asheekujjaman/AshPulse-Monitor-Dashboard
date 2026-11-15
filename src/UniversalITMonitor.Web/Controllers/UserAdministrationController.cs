using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniversalITMonitor.Application.Accounts;
using UniversalITMonitor.Core.Models;
using UniversalITMonitor.Core.Persistence;
using UniversalITMonitor.Web.ViewModels;

namespace UniversalITMonitor.Web.Controllers;

[Authorize(Policy = "CanManageSettings")]
public sealed class UserAdministrationController(IUserAdministrationService users) : Controller
{
    private static readonly IReadOnlyList<UserRoleChoice> RoleChoices =
    [
        new(SystemRoles.Administrator, "Administrator"),
        new(SystemRoles.ReadOnly, "Read-only"),
        new(SystemRoles.Operator, "Operator (existing role)")
    ];

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        View(new UserAdministrationIndexViewModel { Users = await users.GetAllAsync(cancellationToken) });

    [HttpGet]
    public IActionResult Create() => View("Edit", Prepare(new UserAdministrationEditViewModel { IsCreate = true }));

    [HttpPost]
    public async Task<IActionResult> Create(UserAdministrationEditViewModel model, CancellationToken cancellationToken)
    {
        model.IsCreate = true;
        if (string.IsNullOrWhiteSpace(model.Password)) ModelState.AddModelError(nameof(model.Password), "Enter a password for the new user.");
        if (!ModelState.IsValid) return View("Edit", Prepare(model));

        var result = await users.CreateAsync(ToInput(model), cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Error ?? "The user could not be created.");
            ClearPassword(model);
            return View("Edit", Prepare(model));
        }

        TempData["Success"] = "User created.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(string id, CancellationToken cancellationToken)
    {
        var user = (await users.GetAllAsync(cancellationToken)).FirstOrDefault(item => item.Id == id);
        return user is null ? NotFound() : View(Prepare(new UserAdministrationEditViewModel
        {
            Id = user.Id,
            UserName = user.UserName,
            DisplayName = user.DisplayName,
            Role = user.Roles.FirstOrDefault() ?? SystemRoles.ReadOnly,
            IsEnabled = user.IsEnabled
        }));
    }

    [HttpPost]
    public async Task<IActionResult> Edit(string id, UserAdministrationEditViewModel model, CancellationToken cancellationToken)
    {
        model.Id = id;
        if (!ModelState.IsValid)
        {
            ClearPassword(model);
            return View(Prepare(model));
        }

        var result = await users.UpdateAsync(id, ToInput(model), cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Error ?? "The user could not be updated.");
            ClearPassword(model);
            return View(Prepare(model));
        }

        TempData["Success"] = "User updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Delete(string id, CancellationToken cancellationToken)
    {
        var user = (await users.GetAllAsync(cancellationToken)).FirstOrDefault(item => item.Id == id);
        return user is null ? NotFound() : View(user);
    }

    [HttpPost]
    public async Task<IActionResult> DeleteConfirmed(string id, CancellationToken cancellationToken)
    {
        var result = await users.DeleteAsync(id, cancellationToken);
        if (!result.Succeeded)
        {
            TempData["Error"] = result.Error ?? "The user could not be deleted.";
            return RedirectToAction(nameof(Index));
        }

        TempData["Success"] = "User deleted.";
        return RedirectToAction(nameof(Index));
    }

    private static UserAdministrationEditViewModel Prepare(UserAdministrationEditViewModel model)
    {
        model.Roles = RoleChoices;
        return model;
    }

    private static UserAdministrationInput ToInput(UserAdministrationEditViewModel model) => new()
    {
        UserName = model.UserName,
        DisplayName = model.DisplayName,
        Role = model.Role,
        IsEnabled = model.IsEnabled,
        Password = model.Password
    };

    private void ClearPassword(UserAdministrationEditViewModel model)
    {
        ModelState.Remove(nameof(model.Password));
        model.Password = string.Empty;
    }
}
