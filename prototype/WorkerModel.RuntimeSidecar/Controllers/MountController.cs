using Microsoft.AspNetCore.Mvc;
using WorkerModel.RuntimeSidecar.Services;

namespace WorkerModel.RuntimeSidecar.Controllers;

/// <summary>
/// HTTP endpoint for Scale Controller to trigger app package mounting.
/// POST /mount - downloads zip from blob storage and mounts via SquashFS.
/// </summary>
[ApiController]
[Route("[controller]")]
public class MountController : ControllerBase
{
    private readonly MountManager _mountManager;
    private readonly ILogger<MountController> _logger;

    public MountController(MountManager mountManager, ILogger<MountController> logger)
    {
        _mountManager = mountManager;
        _logger = logger;
    }

    /// <summary>
    /// Triggers download and mount of a customer app package.
    /// Called by Scale Controller during specialization.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Mount([FromBody] MountRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "[MountController] Received mount request for app '{ApplicationId}' version '{CodeVersion}'",
            request.ApplicationId,
            request.CodeVersion);

        var currentMount = _mountManager.GetMountInfo();
        if (currentMount is not null && currentMount.IsReady)
        {
            _logger.LogWarning("[MountController] Already mounted, rejecting request");
            return Conflict(new
            {
                error = "Already mounted",
                applicationId = currentMount.ApplicationId,
                codeVersion = currentMount.CodeVersion,
                mountPoint = currentMount.MountPoint,
            });
        }

        if (string.IsNullOrEmpty(request.PackageUrl))
        {
            return BadRequest(new { error = "PackageUrl is required" });
        }

        try
        {
            var mountInfo = await _mountManager.MountAsync(request, cancellationToken);

            _logger.LogInformation(
                "[MountController] Mount completed: {MountPoint}",
                mountInfo.MountPoint);

            return Ok(new
            {
                status = "mounted",
                applicationId = mountInfo.ApplicationId,
                codeVersion = mountInfo.CodeVersion,
                mountPoint = mountInfo.MountPoint,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[MountController] Mount failed");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Unmounts the current app package (for redeployment or cleanup).
    /// </summary>
    [HttpDelete]
    public async Task<IActionResult> Unmount(CancellationToken cancellationToken)
    {
        var currentMount = _mountManager.GetMountInfo();
        if (currentMount is null)
        {
            return NotFound(new { error = "Nothing is mounted" });
        }

        try
        {
            await _mountManager.UnmountAsync(cancellationToken);
            return Ok(new { status = "unmounted" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[MountController] Unmount failed");
            return StatusCode(500, new { error = ex.Message });
        }
    }
}

/// <summary>
/// Request body for POST /mount.
/// </summary>
public class MountRequest
{
    /// <summary>
    /// Application identifier.
    /// </summary>
    public string ApplicationId { get; set; } = string.Empty;

    /// <summary>
    /// Version of the deployed code.
    /// </summary>
    public string CodeVersion { get; set; } = string.Empty;

    /// <summary>
    /// URL to download the zip package (blob SAS URL or SC download endpoint).
    /// </summary>
    public string PackageUrl { get; set; } = string.Empty;

    /// <summary>
    /// Target mount point. Defaults to /home/site/wwwroot.
    /// </summary>
    public string? MountPoint { get; set; }
}
