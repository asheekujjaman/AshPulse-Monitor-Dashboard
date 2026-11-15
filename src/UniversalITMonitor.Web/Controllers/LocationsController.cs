using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniversalITMonitor.Application.Devices;
using UniversalITMonitor.Application.Inventory;
using UniversalITMonitor.Core.Models;
using UniversalITMonitor.Core.Persistence;
using UniversalITMonitor.Web.ViewModels;

namespace UniversalITMonitor.Web.Controllers;

[Authorize]
public sealed class LocationsController(IInventoryStructureService structure, IDeviceService devices) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        View(new LocationsIndexViewModel { Locations = await structure.GetLocationSummariesAsync(cancellationToken) });

    [HttpGet]
    public async Task<IActionResult> Details(Guid id, CancellationToken cancellationToken)
    {
        var summary = (await structure.GetLocationSummariesAsync(cancellationToken)).FirstOrDefault(item => item.Location.Id == id);
        if (summary is null) return NotFound();
        var ids = summary.DeviceIds.ToHashSet();
        var matching = (await devices.GetAllAsync(cancellationToken)).Where(device => ids.Contains(device.Id)).ToArray();
        return View(new LocationDetailsViewModel { Summary = summary, Devices = matching });
    }

    [HttpGet]
    [Authorize(Policy = "CanManageDevices")]
    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        var model = new LocationEditViewModel();
        await PopulateChoicesAsync(model, cancellationToken);
        return View("Edit", model);
    }

    [HttpPost]
    [Authorize(Policy = "CanManageDevices")]
    public async Task<IActionResult> Create(LocationEditViewModel model, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(model.Kind)) ModelState.AddModelError(nameof(model.Kind), "Choose a supported location type.");
        if (!ModelState.IsValid)
        {
            await PopulateChoicesAsync(model, cancellationToken);
            return View("Edit", model);
        }

        var result = await structure.CreateLocationAsync(new ManagedLocation
        {
            Name = model.Name,
            Kind = model.Kind,
            ParentLocationId = model.ParentLocationId
        }, cancellationToken);
        if (result != StructureWriteResult.Success)
        {
            AddWriteError(result);
            await PopulateChoicesAsync(model, cancellationToken);
            return View("Edit", model);
        }

        TempData["Success"] = $"{model.Name.Trim()} was added.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    [Authorize(Policy = "CanManageDevices")]
    public async Task<IActionResult> Edit(Guid id, CancellationToken cancellationToken)
    {
        var location = (await structure.GetStructureAsync(cancellationToken)).Locations.FirstOrDefault(item => item.Id == id);
        if (location is null) return NotFound();
        var model = new LocationEditViewModel
        {
            Id = location.Id,
            Name = location.Name,
            Kind = location.Kind,
            ParentLocationId = location.ParentLocationId
        };
        await PopulateChoicesAsync(model, cancellationToken);
        return View(model);
    }

    [HttpPost]
    [Authorize(Policy = "CanManageDevices")]
    public async Task<IActionResult> Edit(LocationEditViewModel model, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(model.Kind)) ModelState.AddModelError(nameof(model.Kind), "Choose a supported location type.");
        if (!ModelState.IsValid)
        {
            await PopulateChoicesAsync(model, cancellationToken);
            return View(model);
        }

        var result = await structure.UpdateLocationAsync(new ManagedLocation
        {
            Id = model.Id ?? Guid.Empty,
            Name = model.Name,
            Kind = model.Kind,
            ParentLocationId = model.ParentLocationId
        }, cancellationToken);
        if (result != StructureWriteResult.Success)
        {
            AddWriteError(result);
            await PopulateChoicesAsync(model, cancellationToken);
            return View(model);
        }

        TempData["Success"] = $"{model.Name.Trim()} was updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    [Authorize(Policy = "CanManageDevices")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var summary = (await structure.GetLocationSummariesAsync(cancellationToken)).FirstOrDefault(item => item.Location.Id == id);
        if (summary is null) return NotFound();
        return View(new LocationDeleteViewModel
        {
            Id = id,
            Name = summary.Location.Name,
            DeviceCount = summary.DeviceCount,
            Choices = await LocationChoicesAsync(id, cancellationToken)
        });
    }

    [HttpPost]
    [Authorize(Policy = "CanManageDevices")]
    public async Task<IActionResult> Delete(LocationDeleteViewModel model, CancellationToken cancellationToken)
    {
        var result = await structure.DeleteLocationAsync(model.Id, model.ReassignToLocationId, model.ConfirmUnassignDevices, cancellationToken);
        if (result.Result != StructureWriteResult.Success)
        {
            var location = (await structure.GetStructureAsync(cancellationToken)).Locations.FirstOrDefault(item => item.Id == model.Id);
            if (location is null) return NotFound();
            model.Name = location.Name;
            model.DeviceCount = result.AffectedDeviceCount;
            model.Error = result.Error ?? WriteError(result.Result);
            model.Choices = await LocationChoicesAsync(model.Id, cancellationToken);
            return View(model);
        }

        TempData["Success"] = result.AffectedDeviceCount == 0
            ? "The location was removed."
            : $"The location was removed; {result.AffectedDeviceCount} device assignment(s) were updated.";
        return RedirectToAction(nameof(Index));
    }

    private async Task PopulateChoicesAsync(LocationEditViewModel model, CancellationToken cancellationToken)
    {
        var all = await structure.GetStructureAsync(cancellationToken);
        model.ParentChoices = all.Locations
            .Where(item => item.Id != model.Id)
            .OrderBy(item => InventoryStructureService.BuildLocationPath(all.Locations, item.Id), StringComparer.OrdinalIgnoreCase)
            .Select(item => new HierarchyChoice(item.Id, InventoryStructureService.BuildLocationPath(all.Locations, item.Id)))
            .ToArray();
    }

    private async Task<IReadOnlyList<HierarchyChoice>> LocationChoicesAsync(Guid excludedId, CancellationToken cancellationToken)
    {
        var all = await structure.GetStructureAsync(cancellationToken);
        return all.Locations
            .Where(item => item.Id != excludedId && item.Kind != LocationKind.Building)
            .OrderBy(item => InventoryStructureService.BuildLocationPath(all.Locations, item.Id), StringComparer.OrdinalIgnoreCase)
            .Select(item => new HierarchyChoice(item.Id, InventoryStructureService.BuildLocationPath(all.Locations, item.Id)))
            .ToArray();
    }

    private void AddWriteError(StructureWriteResult result) => ModelState.AddModelError(string.Empty, WriteError(result));

    private static string WriteError(StructureWriteResult result) => result switch
    {
        StructureWriteResult.InvalidName => "Enter a location name up to 120 characters long.",
        StructureWriteResult.DuplicateName => "A location with this name already exists under the selected parent.",
        StructureWriteResult.InvalidParent => "Choose a valid parent location. A location cannot be moved beneath one of its descendants.",
        StructureWriteResult.HasChildren => "Move or delete child locations before deleting this location.",
        StructureWriteResult.InUse => "Reassign devices or confirm unassigning them before deleting this location.",
        _ => "The location could not be saved."
    };
}
