using System.Text;
using Microsoft.AspNetCore.Mvc;

namespace WorkerModel.RuntimeSidecar.Controllers;

/// <summary>
/// HTTP endpoint for Scale Controller to trigger WebHost specialization.
/// POST /assign - proxies the assignment to WebHost's /admin/instance/assign.
/// 
/// In the prototype, this runs in the same pod as WebHost and can call it without auth.
/// In production, this would use internal pod networking or a shared secret.
/// </summary>
[ApiController]
[Route("[controller]")]
public class AssignController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AssignController> _logger;

    public AssignController(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<AssignController> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Triggers WebHost specialization by proxying to /admin/instance/assign.
    /// Called by Scale Controller after /mount completes.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Assign([FromBody] RuntimeAssignRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "[AssignController] Received assign request for site '{SiteName}'",
            request.SiteName);

        // Get WebHost endpoint from Aspire service discovery or configuration
        // Check named "webhost" endpoint first (non-proxied), then fall back to default
        var webHostEndpoint = _configuration["services:runtime:webhost:0"]
            ?? _configuration["services:runtime:http:0"]
            ?? "http://localhost:7071";

        _logger.LogInformation("[AssignController] Proxying assign to WebHost at {Endpoint}", webHostEndpoint);

        try
        {
            var client = _httpClientFactory.CreateClient();

            // Build the HostAssignmentRequest for WebHost
            // Using unencrypted assignmentContext for prototype
            // Note: Must use Newtonsoft.Json property names (camelCase) to match WebHost expectations
            var assignRequest = new
            {
                assignmentContext = new
                {
                    siteName = request.SiteName,
                    siteId = int.TryParse(request.SiteId, out var id) ? id : 0,
                    environment = request.Environment ?? new Dictionary<string, string>(),
                }
            };

            // Use Newtonsoft.Json for compatibility with WebHost
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(assignRequest);
            _logger.LogDebug("[AssignController] Sending assign request: {Json}", json);

            var content = new StringContent(
                json,
                Encoding.UTF8,
                "application/json");

            // Note: In prototype, WebHost may reject due to auth.
            // We try anyway and log the result.
            // First try /dev/instance/assign (available when WorkerModel__DecoupledMode=true)
            var response = await client.PostAsync(
                $"{webHostEndpoint}/dev/instance/assign",
                content,
                cancellationToken);

            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("[AssignController] WebHost assign succeeded: {Status}", response.StatusCode);
                return Ok(new
                {
                    status = "assigned",
                    webHostStatus = (int)response.StatusCode,
                });
            }

            // If auth fails, try the ?forcespecialization approach as fallback
            // (requires PLACEHOLDER_SIMULATION to be enabled at compile time)
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                _logger.LogWarning("[AssignController] WebHost auth failed ({Status}), trying ?forcespecialization fallback",
                    response.StatusCode);

                var fallbackResponse = await client.GetAsync(
                    $"{webHostEndpoint}/?forcespecialization",
                    cancellationToken);

                _logger.LogInformation("[AssignController] Fallback specialization result: {Status}",
                    fallbackResponse.StatusCode);

                return Ok(new
                {
                    status = "assigned_via_fallback",
                    webHostStatus = (int)fallbackResponse.StatusCode,
                    note = "Used ?forcespecialization fallback due to auth"
                });
            }

            _logger.LogWarning("[AssignController] WebHost assign failed: {Status} - {Body}",
                response.StatusCode, responseBody);

            return StatusCode((int)response.StatusCode, new
            {
                error = "WebHost assign failed",
                webHostStatus = (int)response.StatusCode,
                webHostResponse = responseBody,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AssignController] Failed to call WebHost");
            return StatusCode(500, new { error = ex.Message });
        }
    }
}

/// <summary>
/// Request body for POST /assign on RuntimeSidecar.
/// Simplified version of HostAssignmentContext.
/// </summary>
public class RuntimeAssignRequest
{
    public string SiteName { get; set; } = string.Empty;
    public string? SiteId { get; set; }
    public Dictionary<string, string> Environment { get; set; } = new();
}
