using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniversalITMonitor.Application.Notifications;
using UniversalITMonitor.Core.Models;
using UniversalITMonitor.Web.ViewModels;

namespace UniversalITMonitor.Web.Controllers;

[Authorize(Policy = "CanManageSettings")]
public sealed class NotificationsController(INotificationService notifications) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        View(await BuildViewModelAsync(cancellationToken));

    [HttpPost]
    public async Task<IActionResult> Save(NotificationSettingsPageViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            ClearSecrets(model);
            model.History = await notifications.GetRecentAsync(100, cancellationToken);
            return View("Index", model);
        }

        var result = await notifications.SaveSettingsAsync(model.ToInput(), cancellationToken);
        if (!result.Success)
        {
            ModelState.AddModelError(string.Empty, result.Error ?? "Notification settings could not be saved.");
            ClearSecrets(model);
            var current = await notifications.GetSettingsAsync(cancellationToken);
            model.HasTelegramBotToken = current.HasTelegramBotToken;
            model.HasSmtpUserName = current.HasSmtpUserName;
            model.HasSmtpPassword = current.HasSmtpPassword;
            model.History = await notifications.GetRecentAsync(100, cancellationToken);
            return View("Index", model);
        }

        TempData["Success"] = "Notification settings saved. Credentials are protected in local JSON storage.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Test(NotificationChannel channel, CancellationToken cancellationToken)
    {
        var result = await notifications.TestSendAsync(channel, cancellationToken);
        TempData[result.Success ? "Success" : "Error"] = result.Message;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Retry(Guid recordId, NotificationChannel channel, CancellationToken cancellationToken)
    {
        var retried = await notifications.RetryDeadLetterAsync(recordId, channel, cancellationToken);
        TempData[retried ? "Success" : "Error"] = retried
            ? "The delivery was requeued for an administrator requested retry."
            : "This delivery is not eligible for manual retry.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<NotificationSettingsPageViewModel> BuildViewModelAsync(CancellationToken cancellationToken) =>
        NotificationSettingsPageViewModel.From(
            await notifications.GetSettingsAsync(cancellationToken),
            await notifications.GetRecentAsync(100, cancellationToken));

    private void ClearSecrets(NotificationSettingsPageViewModel model)
    {
        ModelState.Remove("Input.TelegramBotToken");
        ModelState.Remove("Input.SmtpUserName");
        ModelState.Remove("Input.SmtpPassword");
        model.Input.TelegramBotToken = string.Empty;
        model.Input.SmtpUserName = string.Empty;
        model.Input.SmtpPassword = string.Empty;
    }
}
