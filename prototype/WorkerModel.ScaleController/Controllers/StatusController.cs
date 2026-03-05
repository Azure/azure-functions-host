using Microsoft.AspNetCore.Mvc;
using WorkerModel.ScaleController.Models;
using WorkerModel.ScaleController.Services;

namespace WorkerModel.ScaleController.Controllers;

/// <summary>
/// API for system status and health.
/// </summary>
[ApiController]
[Route("api")]
public class StatusController : ControllerBase
{
    private readonly ApplicationService _appService;
    private readonly RuntimeService _runtimeService;
    private readonly WorkerService _workerService;

    public StatusController(
        ApplicationService appService,
        RuntimeService runtimeService,
        WorkerService workerService)
    {
        _appService = appService;
        _runtimeService = runtimeService;
        _workerService = workerService;
    }

    /// <summary>
    /// Get overall system status.
    /// </summary>
    [HttpGet("status")]
    public async Task<ActionResult<SystemStatus>> GetStatus()
    {
        var apps = await _appService.GetAllAsync();
        var runtimes = await _runtimeService.GetAllAsync();
        var workers = await _workerService.GetAllAsync();

        return Ok(new SystemStatus
        {
            Applications = apps,
            Runtimes = runtimes,
            Workers = workers
        });
    }

    /// <summary>
    /// Health check endpoint.
    /// </summary>
    [HttpGet("health")]
    public IActionResult Health()
    {
        return Ok(new { status = "healthy", timestamp = DateTime.UtcNow });
    }
}
