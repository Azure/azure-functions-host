using Microsoft.AspNetCore.Mvc;
using WorkerModel.Contracts;
using WorkerModel.Sidecar.Services;

namespace WorkerModel.Sidecar.Controllers;

/// <summary>
/// HTTP endpoint for Scale Controller to assign this worker to a Runtime.
/// POST /assign - receives WorkerAssignmentRequest with RuntimeEndpoint + HostAssignmentContext
/// </summary>
[ApiController]
[Route("[controller]")]
public class AssignController : ControllerBase
{
    private readonly SpecializationService _specializationService;
    private readonly WorkerState _workerState;
    private readonly ILogger<AssignController> _logger;

    public AssignController(
        SpecializationService specializationService,
        WorkerState workerState,
        ILogger<AssignController> logger)
    {
        _specializationService = specializationService;
        _workerState = workerState;
        _logger = logger;
    }

    /// <summary>
    /// Assigns this worker to a Runtime and triggers specialization.
    /// Called by Scale Controller when matching a placeholder worker to a runtime.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Assign([FromBody] WorkerAssignmentRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("[AssignController] Received assignment request for site '{SiteName}'", 
            request.HostAssignmentContext.SiteName);

        if (!_workerState.IsPlaceholder)
        {
            _logger.LogWarning("[AssignController] Worker already specialized, rejecting assignment");
            return Conflict(new { error = "Worker is already specialized" });
        }

        if (string.IsNullOrEmpty(request.RuntimeEndpoint))
        {
            _logger.LogWarning("[AssignController] Missing RuntimeEndpoint in assignment request");
            return BadRequest(new { error = "RuntimeEndpoint is required" });
        }

        try
        {
            await _specializationService.SpecializeAsync(request, cancellationToken);

            _logger.LogInformation("[AssignController] Specialization completed successfully");

            return Ok(new
            {
                status = "specialized",
                workerId = _workerState.Context.WorkerId,
                applicationId = _workerState.Context.Application?.ApplicationId,
                runtimeEndpoint = _workerState.RuntimeEndpoint
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AssignController] Specialization failed");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Gets the current assignment status of this worker.
    /// </summary>
    [HttpGet]
    public IActionResult GetAssignment()
    {
        return Ok(new
        {
            workerId = _workerState.Context.WorkerId,
            isPlaceholder = _workerState.IsPlaceholder,
            applicationId = _workerState.Context.Application?.ApplicationId,
            codeVersion = _workerState.Context.Application?.CodeVersion,
            runtimeEndpoint = _workerState.RuntimeEndpoint,
            language = _workerState.Context.Language,
            languageVersion = _workerState.Context.LanguageVersion
        });
    }
}
