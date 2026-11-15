using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniversalITMonitor.Application.Reports;
using UniversalITMonitor.Core.Models;
using UniversalITMonitor.Web.ViewModels;

namespace UniversalITMonitor.Web.Controllers;

[Authorize]
public sealed class ReportsController(IReportService reports) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(
        DateOnly? startDate,
        DateOnly? endDate,
        Guid? deviceId,
        Guid? locationId,
        Guid? groupId,
        DeviceStatus? status,
        IpAssignmentStatus? ipAssignmentStatus,
        CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var query = new MonitoringReportQuery(
            startDate ?? today.AddDays(-6),
            endDate ?? today,
            deviceId,
            locationId,
            groupId,
            status,
            ipAssignmentStatus);
        return View(new ReportsViewModel { Report = await reports.GetAsync(query, cancellationToken) });
    }

    [HttpGet]
    public async Task<IActionResult> Export(
        ReportExportKind kind,
        DateOnly startDate,
        DateOnly endDate,
        Guid? deviceId,
        Guid? locationId,
        Guid? groupId,
        DeviceStatus? status,
        IpAssignmentStatus? ipAssignmentStatus,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(kind)) return BadRequest("Choose a supported report export.");
        ReportExport result;
        try
        {
            result = await reports.ExportCsvAsync(
                new MonitoringReportQuery(startDate, endDate, deviceId, locationId, groupId, status, ipAssignmentStatus),
                kind,
                cancellationToken);
        }
        catch (ReportExportTooLargeException exception)
        {
            return StatusCode(StatusCodes.Status413PayloadTooLarge, exception.Message);
        }
        return File(result.Content, "text/csv; charset=utf-8", result.FileName);
    }
}
