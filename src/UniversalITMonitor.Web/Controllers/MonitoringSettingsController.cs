using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniversalITMonitor.Application.Monitoring;
using UniversalITMonitor.Application.Inventory;
using UniversalITMonitor.Core.Models;
using UniversalITMonitor.Core.Persistence;
using UniversalITMonitor.Web.ViewModels;

namespace UniversalITMonitor.Web.Controllers;

[Authorize(Policy = "CanManageSettings")]
public sealed class MonitoringSettingsController(
    IJsonDocumentStore documents,
    IMonitoringCycleTrigger cycleTrigger,
    InventoryReferenceReconciler reconciler) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var settings = await documents.ReadAsync("settings.json", static () => new ApplicationSettings(), cancellationToken);
        return View(MonitoringSettingsViewModel.FromSettings(settings));
    }

    [HttpPost]
    public async Task<IActionResult> Index(MonitoringSettingsViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        await documents.WriteAsync("settings.json", model.ToSettings(), cancellationToken);
        cycleTrigger.RequestImmediateCycle();
        TempData["Success"] = "Monitoring settings saved. A monitoring cycle is starting with the updated settings.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Integrity(CancellationToken cancellationToken) =>
        View(await reconciler.GetReportAsync(cancellationToken));
}
