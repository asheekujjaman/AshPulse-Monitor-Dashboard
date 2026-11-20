using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using UniversalITMonitor.Application.Accounts;
using UniversalITMonitor.Application.Notifications;
using UniversalITMonitor.Application.Reports;
using UniversalITMonitor.Core.Models;
using UniversalITMonitor.Core.Persistence;
using UniversalITMonitor.Storage.Json;

namespace UniversalITMonitor.Tests;

[TestClass]
public sealed class Phase4Tests
{
    [TestMethod]
    public async Task UserAdministrationHashesPasswordsAndProtectsLastActiveAdministrator()
    {
        using var directory = new TemporaryTestDirectory();
        var documents = new FileJsonDocumentStore(directory.Path);
        var repository = new JsonUserRepository(documents);
        var service = new UserAdministrationService(repository, new PasswordHasher<LocalUser>());
        var firstAdmin = await service.CreateAsync(new UserAdministrationInput
        {
            UserName = "admin-one",
            DisplayName = "Primary Admin",
            Role = SystemRoles.Administrator,
            Password = "AdminPassword!2026"
        });
        Assert.IsTrue(firstAdmin.Succeeded);
        var currentAdmin = (await repository.GetAllAsync()).Single();
        Assert.AreNotEqual("AdminPassword!2026", currentAdmin.PasswordHash);
        Assert.AreEqual(PasswordVerificationResult.Success,
            new PasswordHasher<LocalUser>().VerifyHashedPassword(currentAdmin, currentAdmin.PasswordHash, "AdminPassword!2026"));

        var disableLast = await service.UpdateAsync(currentAdmin.Id, new UserAdministrationInput
        {
            UserName = currentAdmin.UserName,
            DisplayName = currentAdmin.DisplayName,
            Role = SystemRoles.Administrator,
            IsEnabled = false
        });
        Assert.AreEqual(UserWriteResult.LastActiveAdministrator, disableLast.Result);

        var deleteLast = await service.DeleteAsync(currentAdmin.Id);
        Assert.AreEqual(UserWriteResult.LastActiveAdministrator, deleteLast.Result);

        var secondAdmin = await service.CreateAsync(new UserAdministrationInput
        {
            UserName = "admin-two",
            DisplayName = "Backup Admin",
            Role = SystemRoles.Administrator,
            Password = "AnotherSecure!2026"
        });
        Assert.IsTrue(secondAdmin.Succeeded);
        var secondAdminRecord = (await repository.GetAllAsync()).Single(user => user.UserName == "admin-two");
        var passwordReset = await service.UpdateAsync(secondAdminRecord.Id, new UserAdministrationInput
        {
            UserName = secondAdminRecord.UserName,
            DisplayName = secondAdminRecord.DisplayName,
            Role = SystemRoles.Administrator,
            IsEnabled = true,
            Password = "ResetPassword!2026"
        });
        Assert.IsTrue(passwordReset.Succeeded);
        var resetAdminRecord = (await repository.GetAllAsync()).Single(user => user.Id == secondAdminRecord.Id);
        var hasher = new PasswordHasher<LocalUser>();
        Assert.AreEqual(PasswordVerificationResult.Success,
            hasher.VerifyHashedPassword(resetAdminRecord, resetAdminRecord.PasswordHash, "ResetPassword!2026"));
        Assert.AreEqual(PasswordVerificationResult.Failed,
            hasher.VerifyHashedPassword(resetAdminRecord, resetAdminRecord.PasswordHash, "AnotherSecure!2026"));

        var disabledFirst = await service.UpdateAsync(currentAdmin.Id, new UserAdministrationInput
        {
            UserName = currentAdmin.UserName,
            DisplayName = currentAdmin.DisplayName,
            Role = SystemRoles.ReadOnly,
            IsEnabled = false
        });
        Assert.IsTrue(disabledFirst.Succeeded);
        var remainingAdmin = (await repository.GetAllAsync()).Single(user => user.UserName == "admin-two");
        Assert.AreEqual(UserWriteResult.LastActiveAdministrator, (await repository.DeleteAsync(remainingAdmin.Id)));
    }

    [TestMethod]
    public async Task NotificationTransitionsAreQueuedOnceAndDeliveryResultsPersistWithoutSecrets()
    {
        using var fixture = await DeviceServiceFixture.CreateAsync();
        var telegram = new TestTelegramSender();
        var notifications = CreateNotificationService(fixture, telegram);
        const string token = "123456789:private-token-value";
        var saved = await notifications.SaveSettingsAsync(new NotificationSettingsInput
        {
            TelegramEnabled = true,
            TelegramChatId = "12345",
            TelegramBotToken = token
        });
        Assert.IsTrue(saved.Success, saved.Error);

        var device = new NetworkDevice { Name = "Alerts router", IpAddress = "192.0.2.160" };
        Assert.AreEqual(DeviceWriteResult.Success, await fixture.Repository.CreateAsync(device));
        var firstDown = await fixture.Repository.RecordChecksAsync([Check(device, DeviceStatus.Down, DateTimeOffset.UtcNow.AddMinutes(-3), "probe timed out")]);
        Assert.HasCount(1, firstDown.Transitions);
        await notifications.QueueTransitionAsync(firstDown.Transitions[0]);
        var continuousDown = await fixture.Repository.RecordChecksAsync([Check(device, DeviceStatus.Down, DateTimeOffset.UtcNow.AddMinutes(-2), "probe timed out")]);
        Assert.IsEmpty(continuousDown.Transitions);
        foreach (var transition in continuousDown.Transitions) await notifications.QueueTransitionAsync(transition);

        var recovered = await fixture.Repository.RecordChecksAsync([Check(device, DeviceStatus.Up, DateTimeOffset.UtcNow.AddMinutes(-1), null, 14)]);
        Assert.HasCount(1, recovered.Transitions);
        await notifications.QueueTransitionAsync(recovered.Transitions[0]);
        await notifications.ProcessPendingAsync();

        var recent = await notifications.GetRecentAsync();
        Assert.HasCount(2, recent);
        Assert.AreEqual(DeviceStatus.Down, recent.Single(record => record.Status == DeviceStatus.Down).Status);
        Assert.AreEqual(DeviceStatus.Up, recent.Single(record => record.Status == DeviceStatus.Up).Status);
        Assert.HasCount(2, telegram.Messages);
        StringAssert.Contains(telegram.Messages[0], "Alerts router");
        StringAssert.Contains(telegram.Messages[0], "192.0.2.160");
        StringAssert.Contains(telegram.Messages[0], "DOWN");
        StringAssert.Contains(telegram.Messages[0], "probe timed out");
        StringAssert.Contains(telegram.Messages[1], "UP");
        Assert.IsTrue(recent.All(record => record.Deliveries.Single().Succeeded));

        var settingsJson = await File.ReadAllTextAsync(Path.Combine(fixture.DataDirectory, "notification-settings.json"));
        Assert.IsFalse(settingsJson.Contains(token, StringComparison.Ordinal));
        var safeSettings = await notifications.GetSettingsAsync();
        Assert.IsTrue(safeSettings.HasTelegramBotToken);
        Assert.IsFalse(JsonSerializer.Serialize(safeSettings).Contains(token, StringComparison.Ordinal));

        var testSend = await notifications.TestSendAsync(NotificationChannel.Telegram);
        Assert.IsTrue(testSend.Success, testSend.Message);
        StringAssert.Contains(telegram.Messages[^1], "AshPulse notification test");
    }

    [TestMethod]
    public async Task NotificationDeliveryFailureIsRecordedWithoutThrowingIntoMonitoring()
    {
        using var fixture = await DeviceServiceFixture.CreateAsync();
        var telegram = new TestTelegramSender { Succeeds = false };
        var notifications = CreateNotificationService(fixture, telegram);
        Assert.IsTrue((await notifications.SaveSettingsAsync(new NotificationSettingsInput
        {
            TelegramEnabled = true,
            TelegramChatId = "12345",
            TelegramBotToken = "987654321:another-private-token"
        })).Success);

        var device = new NetworkDevice { Name = "Failure router", IpAddress = "192.0.2.161" };
        Assert.AreEqual(DeviceWriteResult.Success, await fixture.Repository.CreateAsync(device));
        var transition = (await fixture.Repository.RecordChecksAsync([Check(device, DeviceStatus.Down, DateTimeOffset.UtcNow.AddMinutes(-2), "timeout")])).Transitions.Single();
        await notifications.QueueTransitionAsync(transition);
        await notifications.ProcessPendingAsync();

        var record = (await notifications.GetRecentAsync()).Single();
        Assert.IsFalse(record.Deliveries.Single().Succeeded);
        Assert.IsTrue(record.IsComplete);
        StringAssert.Contains(record.Deliveries.Single().Detail!, "rejected");
    }

    [TestMethod]
    public async Task MonitoringTransitionOutboxReplaysAfterCrashWithoutDuplicateHistoryOrNotification()
    {
        using var fixture = await DeviceServiceFixture.CreateAsync();
        var device = new NetworkDevice { Name = "Crash recovery router", IpAddress = "192.0.2.162", Location = "Recovery site" };
        Assert.AreEqual(DeviceWriteResult.Success, await fixture.Repository.CreateAsync(device));

        var transition = (await fixture.Repository.RecordChecksAsync(
            [Check(device, DeviceStatus.Down, DateTimeOffset.UtcNow, "probe timeout")])).Transitions.Single();
        var statusAfterWrite = await fixture.Repository.GetAsync(device.Id);
        Assert.AreEqual(DeviceStatus.Down, statusAfterWrite?.Status);

        // Simulate process death after the history and notification writes but before outbox acknowledgement.
        await fixture.History.AppendAsync(transition);
        var notifications = CreateNotificationService(fixture, new TestTelegramSender());
        await notifications.QueueTransitionAsync(transition);
        var reopenedRepository = new JsonDeviceRepository(new FileJsonDocumentStore(fixture.DataDirectory));
        var reopenedHistory = new JsonMonitoringHistoryStore(fixture.DataDirectory);
        var pendingAfterRestart = await reopenedRepository.GetPendingTransitionsAsync();
        Assert.HasCount(1, pendingAfterRestart);
        Assert.AreEqual(transition.Id, pendingAfterRestart[0].Id);

        await reopenedHistory.AppendAsync(pendingAfterRestart[0]);
        await notifications.QueueTransitionAsync(pendingAfterRestart[0]);
        Assert.IsTrue(await reopenedRepository.CompleteTransitionAsync(transition.Id));

        Assert.HasCount(1, await reopenedHistory.GetForDeviceAsync(device.Id, 20));
        var storedNotifications = await new JsonNotificationRepository(new FileJsonDocumentStore(fixture.DataDirectory)).GetRecentAsync();
        Assert.HasCount(1, storedNotifications);
        Assert.AreEqual(transition.Id, storedNotifications[0].Id);
        Assert.IsEmpty(await reopenedRepository.GetPendingTransitionsAsync());
    }

    [TestMethod]
    public async Task TransientNotificationFailuresRetryThenDeadLetterAndAllowAdminRetry()
    {
        using var fixture = await DeviceServiceFixture.CreateAsync();
        var sender = new TestTelegramSender { Result = new(false, "Temporary provider outage.", IsTransient: true) };
        var notifications = CreateNotificationService(fixture, sender);
        Assert.IsTrue((await notifications.SaveSettingsAsync(new NotificationSettingsInput
        {
            TelegramEnabled = true,
            TelegramChatId = "12345",
            TelegramBotToken = "987654321:retry-test-token"
        })).Success);
        var device = new NetworkDevice { Name = "Retry router", IpAddress = "192.0.2.163" };
        Assert.AreEqual(DeviceWriteResult.Success, await fixture.Repository.CreateAsync(device));
        var transition = (await fixture.Repository.RecordChecksAsync([Check(device, DeviceStatus.Down, DateTimeOffset.UtcNow.AddMinutes(-2), "timeout")])).Transitions.Single();
        await notifications.QueueTransitionAsync(transition);

        NotificationRecord? record = null;
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            await notifications.ProcessPendingAsync();
            record = (await notifications.GetRecentAsync()).Single();
            Assert.AreEqual(attempt, record.Deliveries[^1].AttemptNumber);
            if (attempt < 5)
            {
                Assert.IsFalse(record.IsComplete);
                await fixture.Documents.UpdateAsync<NotificationHistoryDocument, bool>(
                    "notification-history.json",
                    static () => new NotificationHistoryDocument(),
                    document =>
                    {
                        document.Records.Single().Deliveries[^1].NextAttemptUtc = DateTimeOffset.UtcNow.AddSeconds(-1);
                        return (document, true);
                    });
            }
        }

        Assert.IsNotNull(record);
        Assert.IsTrue(record.IsComplete);
        Assert.IsTrue(record.Deliveries[^1].DeadLettered);
        Assert.IsTrue(await notifications.RetryDeadLetterAsync(record.Id, NotificationChannel.Telegram));
        record = (await notifications.GetRecentAsync()).Single();
        Assert.IsFalse(record.IsComplete);
        Assert.IsFalse(record.Deliveries[^1].DeadLettered);
        Assert.IsNotNull(record.Deliveries[^1].NextAttemptUtc);
    }

    [TestMethod]
    public async Task ReportsCalculatePersistedDurationsResponseSamplesAndCsvWithoutInventingOldData()
    {
        using var fixture = await DeviceServiceFixture.CreateAsync();
        var location = new ManagedLocation { Name = "Report site", Kind = LocationKind.Site };
        Assert.AreEqual(StructureWriteResult.Success, await fixture.StructureService.CreateLocationAsync(location));
        var group = new ManagedGroup { Name = "Report group" };
        Assert.AreEqual(StructureWriteResult.Success, await fixture.StructureService.CreateGroupAsync(group));
        var device = new NetworkDevice
        {
            Name = "Report router, east",
            IpAddress = "192.0.2.170",
            LocationId = location.Id,
            GroupId = group.Id,
            CreatedUtc = new DateTimeOffset(2025, 12, 1, 0, 0, 0, TimeSpan.Zero)
        };
        Assert.AreEqual(DeviceWriteResult.Success, await fixture.Repository.CreateAsync(device));

        var day = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var up = (await fixture.Repository.RecordChecksAsync([Check(device, DeviceStatus.Up, day.AddHours(6), null, 10)])).Transitions.Single();
        await fixture.History.AppendAsync(up);
        var down = (await fixture.Repository.RecordChecksAsync([Check(device, DeviceStatus.Down, day.AddHours(18), "timeout")])).Transitions.Single();
        await fixture.History.AppendAsync(down);
        await fixture.Samples.AppendAsync(new MonitoringCheckSample { DeviceId = device.Id, CheckedUtc = day.AddHours(7), Status = DeviceStatus.Up, ResponseTimeMilliseconds = 10 });
        await fixture.Samples.AppendAsync(new MonitoringCheckSample { DeviceId = device.Id, CheckedUtc = day.AddHours(18), Status = DeviceStatus.Down, Error = "timeout" });
        await fixture.IpamService.SaveRecordAsync(new IpamRecord
        {
            DeviceId = device.Id,
            IpAddress = device.IpAddress,
            DeviceName = device.Name,
            LocationId = location.Id,
            GroupId = group.Id,
            AssignmentStatus = IpAssignmentStatus.Reserved
        });

        var service = new ReportService(fixture.Service, fixture.StructureService, fixture.IpamService, fixture.History, fixture.Samples);
        var query = new MonitoringReportQuery(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 1), null, location.Id, group.Id, DeviceStatus.Down, IpAssignmentStatus.Reserved);
        var report = await service.GetAsync(query);
        Assert.IsNull(report.ValidationError);
        var row = report.Devices.Single();
        Assert.AreEqual(TimeSpan.FromHours(12), row.Up);
        Assert.AreEqual(6d, row.Down.TotalHours, 0.000001d);
        Assert.AreEqual(6d, row.Unknown.TotalHours, 0.000001d);
        Assert.AreEqual(10L, row.AverageResponseMilliseconds);
        Assert.AreEqual(1, row.ResponseSampleCount);
        StringAssert.Contains(row.StatusHistoryNote, "earlier duration is unverified");
        Assert.AreEqual(1, report.Locations.Single().DeviceCount);
        Assert.AreEqual(1, report.Groups.Single().DeviceCount);
        Assert.AreEqual(device.IpAddress, report.IpamRecords.Single().IpAddress);

        var availabilityCsv = await service.ExportCsvAsync(query, ReportExportKind.Availability);
        var csv = System.Text.Encoding.UTF8.GetString(availabilityCsv.Content);
        StringAssert.Contains(csv, "\"Report router, east\"");
        StringAssert.Contains(csv, "RecordType,DeviceName,IpAddress");
        Assert.IsTrue(csv.Split('\n', StringSplitOptions.RemoveEmptyEntries).All(line => CountCsvColumns(line) == 21));
        StringAssert.Contains(csv, "earlier duration is unverified");
        var ipamCsv = await service.ExportCsvAsync(query, ReportExportKind.Ipam);
        StringAssert.Contains(System.Text.Encoding.UTF8.GetString(ipamCsv.Content), device.IpAddress);

        var noHistoryDevice = new NetworkDevice { Name = "Unmeasured", IpAddress = "192.0.2.172" };
        Assert.AreEqual(DeviceWriteResult.Success, await fixture.Repository.CreateAsync(noHistoryDevice));
        var unavailable = await service.GetAsync(query with { DeviceId = noHistoryDevice.Id, LocationId = null, GroupId = null, Status = null });
        Assert.IsNull(unavailable.Devices.Single().AvailabilityPercent);
        StringAssert.Contains(unavailable.Devices.Single().StatusHistoryNote, "unavailable");
    }

    private static NotificationService CreateNotificationService(DeviceServiceFixture fixture, TestTelegramSender telegram)
    {
        var provider = DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(fixture.DataDirectory, "keys")));
        return new NotificationService(
            new JsonNotificationRepository(fixture.Documents),
            new NotificationSecretProtector(provider),
            fixture.Service,
            telegram,
            new TestEmailSender(),
            NullLogger<NotificationService>.Instance);
    }

    private static DeviceCheckResult Check(NetworkDevice device, DeviceStatus status, DateTimeOffset time, string? error, long? response = null) => new()
    {
        DeviceId = device.Id,
        IpAddress = device.IpAddress,
        CheckedUtc = time,
        Status = status,
        ResponseTimeMilliseconds = response,
        Error = error
    };

    private static int CountCsvColumns(string line)
    {
        var columns = 1;
        var inQuotes = false;
        for (var index = 0; index < line.Length; index++)
        {
            if (line[index] == '"')
            {
                if (inQuotes && index + 1 < line.Length && line[index + 1] == '"')
                {
                    index++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (line[index] == ',' && !inQuotes)
            {
                columns++;
            }
        }
        return columns;
    }

    private sealed class TestTelegramSender : ITelegramNotificationSender
    {
        public bool Succeeds { get; set; } = true;
        public NotificationTransportResult? Result { get; set; }
        public List<string> Messages { get; } = [];

        public Task<NotificationTransportResult> SendAsync(string botToken, string chatId, string text, CancellationToken cancellationToken = default)
        {
            Messages.Add(text);
            return Task.FromResult(Result ?? new NotificationTransportResult(Succeeds, Succeeds ? "Sent." : "Telegram rejected the test delivery."));
        }
    }

    private sealed class TestEmailSender : IEmailNotificationSender
    {
        public Task<NotificationTransportResult> SendAsync(NotificationSettings settings, string? userName, string? password, NotificationMessage message, CancellationToken cancellationToken = default) =>
            Task.FromResult(new NotificationTransportResult(true, "Sent."));
    }
}
