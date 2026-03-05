using Microsoft.AspNetCore.Mvc;
using WorkerModel.ScaleController.Models;
using WorkerModel.ScaleController.Services;

namespace WorkerModel.ScaleController.Controllers;

/// <summary>
/// API for managing Function Apps.
/// </summary>
[ApiController]
[Route("api/apps")]
public class AppsController : ControllerBase
{
    private readonly ApplicationService _appService;
    private readonly ILogger<AppsController> _logger;

    public AppsController(ApplicationService appService, ILogger<AppsController> logger)
    {
        _appService = appService;
        _logger = logger;
    }

    /// <summary>
    /// List all applications.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<ApplicationInfo>>> GetAll()
    {
        var apps = await _appService.GetAllAsync();
        return Ok(apps);
    }

    /// <summary>
    /// Get application by ID.
    /// </summary>
    [HttpGet("{appId}")]
    public async Task<ActionResult<ApplicationInfo>> Get(string appId)
    {
        var app = await _appService.GetAsync(appId);
        if (app is null)
        {
            return NotFound(new { error = $"Application '{appId}' not found" });
        }
        return Ok(app);
    }

    /// <summary>
    /// Create a new application.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<ApplicationInfo>> Create([FromBody] CreateApplicationRequest request)
    {
        if (string.IsNullOrEmpty(request.AppId))
        {
            return BadRequest(new { error = "AppId is required" });
        }

        var existing = await _appService.GetAsync(request.AppId);
        if (existing is not null)
        {
            return Conflict(new { error = $"Application '{request.AppId}' already exists" });
        }

        var app = await _appService.CreateAsync(request);
        _logger.LogInformation("[AppsController] Created application '{AppId}'", request.AppId);
        return CreatedAtAction(nameof(Get), new { appId = app.Id }, app);
    }

    /// <summary>
    /// Deploy app code (upload zip).
    /// </summary>
    [HttpPost("{appId}/deploy")]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<DeploymentResponse>> Deploy(string appId, IFormFile file, [FromForm] string? environment = null)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(new { error = "Zip file is required" });
        }

        var app = await _appService.GetAsync(appId);
        if (app is null)
        {
            return NotFound(new { error = $"Application '{appId}' not found" });
        }

        Dictionary<string, string>? envDict = null;
        if (!string.IsNullOrEmpty(environment))
        {
            try
            {
                envDict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(environment);
            }
            catch
            {
                return BadRequest(new { error = "Invalid environment JSON" });
            }
        }

        using var stream = file.OpenReadStream();
        var result = await _appService.DeployAsync(appId, stream, envDict);

        _logger.LogInformation("[AppsController] Deployed '{AppId}' version '{Version}'",
            appId, result.CodeVersion);

        return Ok(result);
    }

    /// <summary>
    /// Download app package.
    /// </summary>
    [HttpGet("{appId}/download/{codeVersion?}")]
    public async Task<IActionResult> Download(string appId, string? codeVersion = null)
    {
        try
        {
            var stream = await _appService.DownloadAsync(appId, codeVersion);
            return File(stream, "application/zip", "app.zip");
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Get download URL for app package (with SAS token).
    /// </summary>
    [HttpGet("{appId}/download-url/{codeVersion?}")]
    public async Task<ActionResult<object>> GetDownloadUrl(string appId, string? codeVersion = null)
    {
        try
        {
            var url = await _appService.GetDownloadUrlAsync(appId, codeVersion);
            return Ok(new { url });
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }
}
