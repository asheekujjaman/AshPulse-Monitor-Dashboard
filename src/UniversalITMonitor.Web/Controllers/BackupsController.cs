using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using UniversalITMonitor.Application.Backups;
using UniversalITMonitor.Core.Models;
using UniversalITMonitor.Web.ViewModels;

namespace UniversalITMonitor.Web.Controllers;

[Authorize(Policy = "CanManageSettings")]
public sealed class BackupsController(IBackupService backups, IHostApplicationLifetime lifetime) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var settings = await backups.GetSettingsAsync(cancellationToken);
        var history = await backups.GetHistoryAsync(cancellationToken);
        try
        {
            return View(new BackupIndexViewModel
            {
                Settings = settings,
                History = history,
                Files = await backups.GetBackupFilesAsync(cancellationToken)
            });
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return View(new BackupIndexViewModel
            {
                Settings = settings,
                History = history,
                Error = "The configured backup directory is unavailable or invalid. Correct it below before creating or restoring backups."
            });
        }
    }

    [HttpPost]
    public async Task<IActionResult> SaveSettings([Bind(Prefix = "Settings")] BackupSettings settings, CancellationToken cancellationToken)
    {
        var result = await backups.SaveSettingsAsync(settings, cancellationToken);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded
            ? "Backup settings saved."
            : result.Error ?? "Backup settings could not be saved.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        var result = await backups.CreateManualBackupAsync(cancellationToken);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded
            ? $"Backup created: {result.FileName} ({FormatBytes(result.SizeBytes ?? 0)})."
            : result.Error ?? "Backup creation failed.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Preview(string fileName, CancellationToken cancellationToken)
    {
        try
        {
            var preview = await backups.PreviewAsync(fileName, cancellationToken);
            return View(new BackupRestoreViewModel { FileName = fileName, Preview = preview });
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or BackupArchiveException or InvalidDataException)
        {
            return View(new BackupRestoreViewModel { FileName = fileName, Error = exception.Message });
        }
    }

    [HttpGet]
    public async Task<IActionResult> Download(string fileName, CancellationToken cancellationToken)
    {
        try
        {
            var stream = await backups.OpenBackupAsync(fileName, cancellationToken);
            return File(stream, "application/zip", fileName, enableRangeProcessing: true);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return NotFound();
        }
    }

    [HttpPost]
    public async Task<IActionResult> Restore(BackupRestoreViewModel model, CancellationToken cancellationToken)
    {
        var result = await backups.RestoreAsync(new BackupRestoreRequest(
            model.FileName,
            model.FullRestore,
            model.Modules,
            model.ConfirmReplace), cancellationToken);
        if (!result.Succeeded)
        {
            try
            {
                model.Preview = await backups.PreviewAsync(model.FileName, cancellationToken);
            }
            catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or BackupArchiveException or InvalidDataException)
            {
                model.Error = exception.Message;
            }
            ModelState.AddModelError(string.Empty, result.Error ?? "Restore failed. Existing application data was preserved.");
            return View("Preview", model);
        }

        if (result.RequiresRestart)
        {
            Response.OnCompleted(() =>
            {
                lifetime.StopApplication();
                return Task.CompletedTask;
            });
            return View("RestoreComplete", model);
        }

        TempData["Success"] = "Selected modules restored. The restored inventory and history are available now.";
        return RedirectToAction(nameof(Index));
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1024L * 1024 * 1024 => $"{bytes / (1024d * 1024 * 1024):0.0} GB",
        >= 1024L * 1024 => $"{bytes / (1024d * 1024):0.0} MB",
        >= 1024 => $"{bytes / 1024d:0.0} KB",
        _ => $"{bytes} B"
    };
}
