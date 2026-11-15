using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniversalITMonitor.Application.Devices;
using UniversalITMonitor.Application.Inventory;
using UniversalITMonitor.Core.Models;
using UniversalITMonitor.Core.Persistence;
using UniversalITMonitor.Web.ViewModels;

namespace UniversalITMonitor.Web.Controllers;

[Authorize]
public sealed class GroupsController(IInventoryStructureService structure, IDeviceService devices) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        View(new GroupsIndexViewModel { Groups = await structure.GetGroupSummariesAsync(cancellationToken) });

    [HttpGet]
    public async Task<IActionResult> Details(Guid id, CancellationToken cancellationToken)
    {
        var summary = (await structure.GetGroupSummariesAsync(cancellationToken)).FirstOrDefault(item => item.Group.Id == id);
        if (summary is null) return NotFound();
        var ids = summary.DeviceIds.ToHashSet();
        var matching = (await devices.GetAllAsync(cancellationToken)).Where(device => ids.Contains(device.Id)).ToArray();
        return View(new GroupDetailsViewModel { Summary = summary, Devices = matching });
    }

    [HttpGet]
    [Authorize(Policy = "CanManageDevices")]
    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        var model = new GroupEditViewModel();
        await PopulateChoicesAsync(model, cancellationToken);
        return View("Edit", model);
    }

    [HttpPost]
    [Authorize(Policy = "CanManageDevices")]
    public async Task<IActionResult> Create(GroupEditViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            await PopulateChoicesAsync(model, cancellationToken);
            return View("Edit", model);
        }

        var result = await structure.CreateGroupAsync(new ManagedGroup { Name = model.Name, ParentGroupId = model.ParentGroupId }, cancellationToken);
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
        var group = (await structure.GetStructureAsync(cancellationToken)).Groups.FirstOrDefault(item => item.Id == id);
        if (group is null) return NotFound();
        var model = new GroupEditViewModel { Id = group.Id, Name = group.Name, ParentGroupId = group.ParentGroupId };
        await PopulateChoicesAsync(model, cancellationToken);
        return View(model);
    }

    [HttpPost]
    [Authorize(Policy = "CanManageDevices")]
    public async Task<IActionResult> Edit(GroupEditViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            await PopulateChoicesAsync(model, cancellationToken);
            return View(model);
        }

        var result = await structure.UpdateGroupAsync(new ManagedGroup
        {
            Id = model.Id ?? Guid.Empty,
            Name = model.Name,
            ParentGroupId = model.ParentGroupId
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
        var summary = (await structure.GetGroupSummariesAsync(cancellationToken)).FirstOrDefault(item => item.Group.Id == id);
        if (summary is null) return NotFound();
        return View(new GroupDeleteViewModel
        {
            Id = id,
            Name = summary.Group.Name,
            DeviceCount = summary.DeviceCount,
            Choices = await GroupChoicesAsync(id, cancellationToken)
        });
    }

    [HttpPost]
    [Authorize(Policy = "CanManageDevices")]
    public async Task<IActionResult> Delete(GroupDeleteViewModel model, CancellationToken cancellationToken)
    {
        var result = await structure.DeleteGroupAsync(model.Id, model.ReassignToGroupId, model.ConfirmUnassignDevices, cancellationToken);
        if (result.Result != StructureWriteResult.Success)
        {
            var group = (await structure.GetStructureAsync(cancellationToken)).Groups.FirstOrDefault(item => item.Id == model.Id);
            if (group is null) return NotFound();
            model.Name = group.Name;
            model.DeviceCount = result.AffectedDeviceCount;
            model.Error = result.Error ?? WriteError(result.Result);
            model.Choices = await GroupChoicesAsync(model.Id, cancellationToken);
            return View(model);
        }

        TempData["Success"] = result.AffectedDeviceCount == 0
            ? "The group was removed."
            : $"The group was removed; {result.AffectedDeviceCount} device assignment(s) were updated.";
        return RedirectToAction(nameof(Index));
    }

    private async Task PopulateChoicesAsync(GroupEditViewModel model, CancellationToken cancellationToken)
    {
        var all = await structure.GetStructureAsync(cancellationToken);
        model.ParentChoices = all.Groups
            .Where(item => item.Id != model.Id)
            .OrderBy(item => InventoryStructureService.BuildGroupPath(all.Groups, item.Id), StringComparer.OrdinalIgnoreCase)
            .Select(item => new HierarchyChoice(item.Id, InventoryStructureService.BuildGroupPath(all.Groups, item.Id)))
            .ToArray();
    }

    private async Task<IReadOnlyList<HierarchyChoice>> GroupChoicesAsync(Guid excludedId, CancellationToken cancellationToken)
    {
        var all = await structure.GetStructureAsync(cancellationToken);
        return all.Groups
            .Where(item => item.Id != excludedId)
            .OrderBy(item => InventoryStructureService.BuildGroupPath(all.Groups, item.Id), StringComparer.OrdinalIgnoreCase)
            .Select(item => new HierarchyChoice(item.Id, InventoryStructureService.BuildGroupPath(all.Groups, item.Id)))
            .ToArray();
    }

    private void AddWriteError(StructureWriteResult result) => ModelState.AddModelError(string.Empty, WriteError(result));

    private static string WriteError(StructureWriteResult result) => result switch
    {
        StructureWriteResult.InvalidName => "Enter a group name up to 120 characters long.",
        StructureWriteResult.DuplicateName => "A group with this name already exists under the selected parent.",
        StructureWriteResult.InvalidParent => "Choose a valid parent group. A group cannot be moved beneath one of its descendants.",
        StructureWriteResult.HasChildren => "Move or delete child groups before deleting this group.",
        StructureWriteResult.InUse => "Reassign devices or confirm unassigning them before deleting this group.",
        _ => "The group could not be saved."
    };
}
