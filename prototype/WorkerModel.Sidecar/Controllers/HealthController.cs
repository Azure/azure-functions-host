using Microsoft.AspNetCore.Mvc;
using WorkerModel.Sidecar.Services;

namespace WorkerModel.Sidecar.Controllers;

/// <summary>
/// Health check endpoint for container orchestrators (k8s, Docker).
/// </summary>
[ApiController]
[Route("[controller]")]
public class HealthController : ControllerBase
{
    private readonly WorkerState _workerState;
    private readonly RuntimeConnectionManager _runtimeConnection;

    public HealthController(WorkerState workerState, RuntimeConnectionManager runtimeConnection)
    {
        _workerState = workerState;
        _runtimeConnection = runtimeConnection;
    }

    /// <summary>
    /// Liveness probe - is the sidecar process running?
    /// </summary>
    [HttpGet("live")]
    public IActionResult Live()
    {
        return Ok(new { status = "alive" });
    }

    /// <summary>
    /// Readiness probe - is the sidecar ready to accept work?
    /// In placeholder mode, we're ready to receive /assign.
    /// In specialized mode, we need to be connected to Runtime.
    /// </summary>
    [HttpGet("ready")]
    public IActionResult Ready()
    {
        if (_workerState.IsPlaceholder)
        {
            return Ok(new
            {
                status = "ready",
                mode = "placeholder",
                warm = _workerState.IsPlaceholderReady,
                workerId = _workerState.Context.WorkerId
            });
        }

        // Specialized workers need to be connected to Runtime
        if (!_runtimeConnection.IsConnected)
        {
            return StatusCode(503, new
            {
                status = "not_ready",
                mode = "specialized",
                reason = "Not connected to Runtime"
            });
        }

        return Ok(new
        {
            status = "ready",
            mode = "specialized",
            workerId = _workerState.Context.WorkerId,
            applicationId = _workerState.Context.Application?.ApplicationId
        });
    }

    /// <summary>
    /// Detailed status endpoint.
    /// </summary>
    [HttpGet]
    public IActionResult Status()
    {
        return Ok(new
        {
            workerId = _workerState.Context.WorkerId,
            isPlaceholder = _workerState.IsPlaceholder,
            isPlaceholderReady = _workerState.IsPlaceholderReady,
            isConnectedToRuntime = _runtimeConnection.IsConnected,
            eventStreamCallCount = Services.SidecarRpcService.EventStreamCallCount,
            eventStreamLastError = Services.SidecarRpcService.EventStreamLastError,
            application = _workerState.Context.Application is not null ? new
            {
                id = _workerState.Context.Application.ApplicationId,
                codeVersion = _workerState.Context.Application.CodeVersion,
                scriptRoot = _workerState.Context.Application.ScriptRoot
            } : null,
            runtimeEndpoint = _workerState.RuntimeEndpoint,
            language = _workerState.Context.Language,
            languageVersion = _workerState.Context.LanguageVersion
        });
    }
}
