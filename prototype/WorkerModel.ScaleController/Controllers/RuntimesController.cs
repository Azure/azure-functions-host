using Microsoft.AspNetCore.Mvc;
using WorkerModel.ScaleController.Models;
using WorkerModel.ScaleController.Services;

namespace WorkerModel.ScaleController.Controllers;

/// <summary>
/// API for Runtime registration and management.
/// </summary>
[ApiController]
[Route("api/runtimes")]
public class RuntimesController : ControllerBase
{
    private readonly RuntimeService _runtimeService;
    private readonly ILogger<RuntimesController> _logger;

    public RuntimesController(RuntimeService runtimeService, ILogger<RuntimesController> logger)
    {
        _runtimeService = runtimeService;
        _logger = logger;
    }

    /// <summary>
    /// List all registered Runtimes.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<RuntimeInfo>>> GetAll()
    {
        var runtimes = await _runtimeService.GetAllAsync();
        return Ok(runtimes);
    }

    /// <summary>
    /// Get Runtime by ID.
    /// </summary>
    [HttpGet("{runtimeId}")]
    public async Task<ActionResult<RuntimeInfo>> Get(string runtimeId)
    {
        var runtime = await _runtimeService.GetAsync(runtimeId);
        if (runtime is null)
        {
            return NotFound(new { error = $"Runtime '{runtimeId}' not found" });
        }
        return Ok(runtime);
    }

    /// <summary>
    /// Register a Runtime with the SC.
    /// Called by Runtime on startup.
    /// </summary>
    [HttpPost("register")]
    public async Task<ActionResult<RuntimeInfo>> Register([FromBody] RegisterRuntimeRequest request)
    {
        if (string.IsNullOrEmpty(request.RuntimeId))
        {
            return BadRequest(new { error = "RuntimeId is required" });
        }

        var runtime = await _runtimeService.RegisterAsync(request);
        _logger.LogInformation("[RuntimesController] Registered runtime '{RuntimeId}'", request.RuntimeId);
        return Ok(runtime);
    }

    /// <summary>
    /// Update Runtime heartbeat. Can include WebHost endpoint update.
    /// </summary>
    [HttpPost("{runtimeId}/heartbeat")]
    public async Task<IActionResult> Heartbeat(string runtimeId, [FromBody] HeartbeatRequest? request = null)
    {
        var runtime = await _runtimeService.GetAsync(runtimeId);
        if (runtime is null)
        {
            return NotFound(new { error = $"Runtime '{runtimeId}' not found" });
        }

        // Update WebHost endpoint if provided (RuntimeSidecar sends this after runtime starts)
        if (!string.IsNullOrEmpty(request?.WebHostEndpoint) && runtime.GrpcEndpoint != request.WebHostEndpoint)
        {
            _logger.LogInformation("[RuntimesController] Updating runtime '{RuntimeId}' WebHost endpoint to {Endpoint}",
                runtimeId, request.WebHostEndpoint);
            runtime.GrpcEndpoint = request.WebHostEndpoint;
        }

        await _runtimeService.HeartbeatAsync(runtimeId);
        return Ok(new { status = "ok" });
    }

    /// <summary>
    /// Get available placeholder Runtimes.
    /// </summary>
    [HttpGet("available")]
    public async Task<ActionResult<List<RuntimeInfo>>> GetAvailable()
    {
        var runtimes = await _runtimeService.GetAvailablePlaceholdersAsync();
        return Ok(runtimes);
    }
}
