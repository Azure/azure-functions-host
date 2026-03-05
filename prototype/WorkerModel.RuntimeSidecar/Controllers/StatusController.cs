using Microsoft.AspNetCore.Mvc;
using WorkerModel.RuntimeSidecar.Services;

namespace WorkerModel.RuntimeSidecar.Controllers;

/// <summary>
/// Status endpoint for inspecting current mount state.
/// </summary>
[ApiController]
[Route("[controller]")]
public class StatusController : ControllerBase
{
    private readonly MountManager _mountManager;

    public StatusController(MountManager mountManager)
    {
        _mountManager = mountManager;
    }

    /// <summary>
    /// Gets the current mount status and info.
    /// </summary>
    [HttpGet]
    public IActionResult GetStatus()
    {
        var info = _mountManager.GetMountInfo();
        if (info is null)
        {
            return Ok(new
            {
                state = "idle",
                mounted = false,
            });
        }

        return Ok(new
        {
            state = info.State.ToString(),
            mounted = info.IsReady,
            applicationId = info.ApplicationId,
            codeVersion = info.CodeVersion,
            mountPoint = info.MountPoint,
            cachedPackagePath = info.CachedPackagePath,
            mountedAt = info.MountedAt,
        });
    }
}
