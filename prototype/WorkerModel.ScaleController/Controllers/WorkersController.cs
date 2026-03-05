using Microsoft.AspNetCore.Mvc;
using WorkerModel.ScaleController.Models;
using WorkerModel.ScaleController.Services;

namespace WorkerModel.ScaleController.Controllers;

/// <summary>
/// API for Worker registration and management.
/// </summary>
[ApiController]
[Route("api/workers")]
public class WorkersController : ControllerBase
{
    private readonly WorkerService _workerService;
    private readonly SpecializationOrchestrator _orchestrator;
    private readonly ILogger<WorkersController> _logger;

    public WorkersController(
        WorkerService workerService,
        SpecializationOrchestrator orchestrator,
        ILogger<WorkersController> logger)
    {
        _workerService = workerService;
        _orchestrator = orchestrator;
        _logger = logger;
    }

    /// <summary>
    /// List all registered Workers.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<WorkerInfo>>> GetAll()
    {
        var workers = await _workerService.GetAllAsync();
        return Ok(workers);
    }

    /// <summary>
    /// Get Worker by ID.
    /// </summary>
    [HttpGet("{workerId}")]
    public async Task<ActionResult<WorkerInfo>> Get(string workerId)
    {
        var worker = await _workerService.GetAsync(workerId);
        if (worker is null)
        {
            return NotFound(new { error = $"Worker '{workerId}' not found" });
        }
        return Ok(worker);
    }

    /// <summary>
    /// Register a Worker (Sidecar) with the SC.
    /// Called by Sidecar on startup.
    /// </summary>
    [HttpPost("register")]
    public async Task<ActionResult<WorkerInfo>> Register([FromBody] RegisterWorkerRequest request)
    {
        if (string.IsNullOrEmpty(request.WorkerId))
        {
            return BadRequest(new { error = "WorkerId is required" });
        }

        var worker = await _workerService.RegisterAsync(request);
        _logger.LogInformation("[WorkersController] Registered worker '{WorkerId}'", request.WorkerId);
        return Ok(worker);
    }

    /// <summary>
    /// Trigger specialization for a Worker.
    /// </summary>
    [HttpPost("{workerId}/specialize")]
    public async Task<IActionResult> Specialize(string workerId, [FromBody] SpecializeWorkerRequest request)
    {
        if (string.IsNullOrEmpty(request.AppId))
        {
            return BadRequest(new { error = "AppId is required" });
        }

        var worker = await _workerService.GetAsync(workerId);
        if (worker is null)
        {
            return NotFound(new { error = $"Worker '{workerId}' not found" });
        }

        try
        {
            await _orchestrator.SpecializeWorkerAsync(workerId, request.AppId);
            _logger.LogInformation("[WorkersController] Triggered specialization for worker '{WorkerId}' with app '{AppId}'",
                workerId, request.AppId);
            return Accepted(new { status = "specializing", workerId, appId = request.AppId });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "[WorkersController] Specialization failed for worker '{WorkerId}'", workerId);
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Update Worker heartbeat.
    /// </summary>
    [HttpPost("{workerId}/heartbeat")]
    public async Task<IActionResult> Heartbeat(string workerId)
    {
        var worker = await _workerService.GetAsync(workerId);
        if (worker is null)
        {
            return NotFound(new { error = $"Worker '{workerId}' not found" });
        }

        await _workerService.HeartbeatAsync(workerId);
        return Ok(new { status = "ok" });
    }

    /// <summary>
    /// Get current assignment for a Worker.
    /// </summary>
    [HttpGet("{workerId}/assignment")]
    public async Task<ActionResult<object>> GetAssignment(string workerId)
    {
        var worker = await _workerService.GetAsync(workerId);
        if (worker is null)
        {
            return NotFound(new { error = $"Worker '{workerId}' not found" });
        }

        return Ok(new
        {
            workerId = worker.Id,
            status = worker.Status.ToString(),
            applicationId = worker.ApplicationId,
            codeVersion = worker.CodeVersion,
            assignedRuntimeId = worker.AssignedRuntimeId
        });
    }

    /// <summary>
    /// Get available placeholder Workers.
    /// </summary>
    [HttpGet("available")]
    public async Task<ActionResult<List<WorkerInfo>>> GetAvailable()
    {
        var workers = await _workerService.GetAvailablePlaceholdersAsync();
        return Ok(workers);
    }
}
