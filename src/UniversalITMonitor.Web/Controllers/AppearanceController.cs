using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniversalITMonitor.Application.Presentation;
using UniversalITMonitor.Core.Models;
using UniversalITMonitor.Web.ViewModels;

namespace UniversalITMonitor.Web.Controllers;

[Authorize(Policy = "CanManageSettings")]
public sealed class AppearanceController(IAppPresentationService presentation) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var current = (await presentation.GetAsync(cancellationToken)).Theme;
        var hasCustomTheme = !ThemePresetCatalog.IsPreset(current.Name);
        return View(new AppearanceSettingsViewModel
        {
            ThemeName = hasCustomTheme ? ThemePresetCatalog.Custom : current.Name,
            HasCustomTheme = hasCustomTheme
        });
    }

    [HttpPost]
    public async Task<IActionResult> Index(AppearanceSettingsViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid ||
            model.ThemeName != ThemePresetCatalog.Custom && !ThemePresetCatalog.IsPreset(model.ThemeName))
        {
            ModelState.AddModelError(nameof(model.ThemeName), "Choose one of the available themes.");
            return View(model);
        }

        if (!await presentation.SetDefaultThemeAsync(model.ThemeName, cancellationToken))
        {
            ModelState.AddModelError(nameof(model.ThemeName), "The selected theme could not be saved.");
            return View(model);
        }

        TempData["Success"] = model.ThemeName == ThemePresetCatalog.Custom
            ? "The current custom theme and branding were preserved."
            : $"{model.ThemeName} is now the application default theme.";
        return RedirectToAction(nameof(Index));
    }
}
