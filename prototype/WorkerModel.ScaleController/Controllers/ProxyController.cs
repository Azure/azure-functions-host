using System.Diagnostics;
using System.Threading;
using Microsoft.AspNetCore.Mvc;
using WorkerModel.ScaleController.Models;
using WorkerModel.ScaleController.Services;

namespace WorkerModel.ScaleController.Controllers;

/// <summary>
/// Reverse proxy controller that handles incoming HTTP requests for function apps.
/// Routes: /proxy/{appId}/{**path} → Runtime HTTP endpoint
/// Triggers specialization on cold start.
/// </summary>
[ApiController]
[Route("proxy")]
public class ProxyController : ControllerBase
{
    private readonly ApplicationService _appService;
    private readonly RuntimeService _runtimeService;
    private readonly WorkerService _workerService;
    private readonly SpecializationOrchestrator _orchestrator;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ProxyController> _logger;

    // Track apps currently being specialized to avoid duplicate specializations
    private static readonly Dictionary<string, TaskCompletionSource<RuntimeInfo>> _specializingApps = new();
    private static readonly object _lock = new();

    // Global request counter for scale-out simulation
    private static int _totalRequestCount;
    private static int _scaleOutTriggered;

    public ProxyController(
        ApplicationService appService,
        RuntimeService runtimeService,
        WorkerService workerService,
        SpecializationOrchestrator orchestrator,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<ProxyController> logger)
    {
        _appService = appService;
        _runtimeService = runtimeService;
        _workerService = workerService;
        _orchestrator = orchestrator;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Proxy endpoint for function app requests.
    /// URL format: /{appId}/{**path}
    /// Example: /customer/api/hello → forwards to Runtime as /api/hello
    /// </summary>
    [HttpGet("{appId}/{**path}")]
    [HttpPost("{appId}/{**path}")]
    [HttpPut("{appId}/{**path}")]
    [HttpDelete("{appId}/{**path}")]
    [HttpPatch("{appId}/{**path}")]
    public async Task<IActionResult> ProxyRequest(string appId, string? path)
    {
        var stopwatch = Stopwatch.StartNew();
        var isColdStart = false;
        var requestNum = Interlocked.Increment(ref _totalRequestCount);

        _logger.LogInformation("[Proxy] Request #{RequestNum} for app '{AppId}', path '/{Path}'",
            requestNum, appId, path ?? "");

        // 1. Check if app exists
        var app = await _appService.GetAsync(appId);
        if (app is null)
        {
            return NotFound(new { error = $"Application '{appId}' not found" });
        }

        if (string.IsNullOrEmpty(app.CodeVersion) || string.IsNullOrEmpty(app.BlobPath))
        {
            return BadRequest(new { error = $"Application '{appId}' has no deployed code" });
        }

        // 2. Find a specialized runtime for this app, or specialize one
        var runtime = await _runtimeService.GetSpecializedForAppAsync(appId);

        if (runtime is null)
        {
            isColdStart = true;
            _logger.LogInformation("[Proxy] Cold start for app '{AppId}' - triggering specialization", appId);

            try
            {
                runtime = await EnsureSpecializedAsync(appId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Proxy] Specialization failed for app '{AppId}'", appId);
                return StatusCode(503, new { error = $"Specialization failed: {ex.Message}" });
            }
        }

        // 3. Get the WebHost endpoint for forwarding
        // The RuntimeSidecar registers the WebHost endpoint in GrpcEndpoint field
        // Use fixed port fallback for prototype to avoid Aspire service discovery issues
        var webHostEndpoint = runtime.GrpcEndpoint;
        
        // Fallback to fixed port for prototype (7071)
        if (string.IsNullOrEmpty(webHostEndpoint) || webHostEndpoint.Contains("62514"))
        {
            webHostEndpoint = "http://localhost:7071";
            _logger.LogInformation("[Proxy] Using fixed WebHost endpoint: {Endpoint}", webHostEndpoint);
        }

        if (string.IsNullOrEmpty(webHostEndpoint))
        {
            _logger.LogError("[Proxy] WebHost endpoint not available");
            return StatusCode(503, new { error = "WebHost endpoint not configured" });
        }

        // 4. Forward request to Runtime (WebHost)
        var targetPath = string.IsNullOrEmpty(path) ? "/" : $"/{path}";
        var targetUrl = $"{webHostEndpoint}{targetPath}{Request.QueryString}";

        _logger.LogInformation("[Proxy] Forwarding to WebHost: {Method} {Url}", Request.Method, targetUrl);

        try
        {
            var (statusCode, content, contentType) = await ForwardRequestAsync(targetUrl);
            
            stopwatch.Stop();
            
            // Add timing headers to response
            Response.Headers.Append("X-Cold-Start", isColdStart.ToString());
            Response.Headers.Append("X-Request-Duration-Ms", stopwatch.ElapsedMilliseconds.ToString());
            Response.Headers.Append("X-SC-Request-Count", requestNum.ToString());
            
            if (isColdStart)
            {
                _logger.LogInformation("[Proxy] Cold start completed for app '{AppId}' in {Duration}ms",
                    appId, stopwatch.ElapsedMilliseconds);
            }

            // Scale-out: after 10 total requests, specialize worker-2 (fire-and-forget)
            if (requestNum >= 10 && Interlocked.CompareExchange(ref _scaleOutTriggered, 1, 0) == 0)
            {
                _logger.LogInformation("[Proxy] *** SCALE-OUT TRIGGERED *** Request #{RequestNum} — specializing worker-2 for app '{AppId}'",
                    requestNum, appId);
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _orchestrator.ScaleOutWorkerAsync("worker-2", appId);
                        _logger.LogInformation("[Proxy] *** SCALE-OUT COMPLETE *** worker-2 is now specialized");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "[Proxy] *** SCALE-OUT FAILED *** for worker-2");
                        Interlocked.Exchange(ref _scaleOutTriggered, 0); // Allow retry
                    }
                });
            }

            return new ContentResult
            {
                StatusCode = statusCode,
                Content = content,
                ContentType = contentType
            };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "[Proxy] Failed to forward request to Runtime");
            return StatusCode(502, new { error = $"Failed to reach Runtime: {ex.Message}" });
        }
    }

    /// <summary>
    /// Ensures an app is specialized, handling concurrent requests.
    /// If specialization is already in progress, waits for it to complete.
    /// </summary>
    private async Task<RuntimeInfo> EnsureSpecializedAsync(string appId)
    {
        TaskCompletionSource<RuntimeInfo>? tcs;
        bool shouldSpecialize = false;

        lock (_lock)
        {
            if (_specializingApps.TryGetValue(appId, out tcs))
            {
                // Another request is already specializing this app, wait for it
                _logger.LogInformation("[Proxy] Waiting for ongoing specialization of app '{AppId}'", appId);
            }
            else
            {
                // We're the first, start specialization
                tcs = new TaskCompletionSource<RuntimeInfo>();
                _specializingApps[appId] = tcs;
                shouldSpecialize = true;
            }
        }

        if (shouldSpecialize)
        {
            try
            {
                var runtime = await SpecializeAppAsync(appId);
                tcs!.SetResult(runtime);
                return runtime;
            }
            catch (Exception ex)
            {
                tcs!.SetException(ex);
                throw;
            }
            finally
            {
                lock (_lock)
                {
                    _specializingApps.Remove(appId);
                }
            }
        }

        // Wait for the other request to complete specialization
        return await tcs!.Task;
    }

    /// <summary>
    /// Performs the actual specialization: picks Runtime + Worker, calls assign endpoints.
    /// </summary>
    private async Task<RuntimeInfo> SpecializeAppAsync(string appId)
    {
        // 1. Find available placeholder Runtime
        var availableRuntimes = await _runtimeService.GetAvailablePlaceholdersAsync();
        if (availableRuntimes.Count == 0)
        {
            throw new InvalidOperationException("No available placeholder Runtimes");
        }
        var runtime = availableRuntimes.First();

        // 2. Find available placeholder Worker
        var availableWorkers = await _workerService.GetAvailablePlaceholdersAsync();
        if (availableWorkers.Count == 0)
        {
            throw new InvalidOperationException("No available placeholder Workers");
        }
        var worker = availableWorkers.First();

        _logger.LogInformation("[Proxy] Selected Runtime '{RuntimeId}' and Worker '{WorkerId}' for app '{AppId}'",
            runtime.Id, worker.Id, appId);

        // 3. Use the orchestrator to perform specialization
        await _orchestrator.SpecializeWorkerAsync(worker.Id, appId);

        // 4. Get the updated runtime info (now specialized)
        var specializedRuntime = await _runtimeService.GetAsync(runtime.Id);
        return specializedRuntime!;
    }

    /// <summary>
    /// Forwards the current HTTP request to the target URL.
    /// </summary>
    private async Task<(int StatusCode, string Content, string ContentType)> ForwardRequestAsync(string targetUrl)
    {
        var client = _httpClientFactory.CreateClient("proxy");

        // Build the request
        var requestMessage = new HttpRequestMessage
        {
            Method = new HttpMethod(Request.Method),
            RequestUri = new Uri(targetUrl)
        };

        // Copy headers (excluding host)
        foreach (var header in Request.Headers)
        {
            if (!header.Key.Equals("Host", StringComparison.OrdinalIgnoreCase) &&
                !header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase) &&
                !header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
            {
                requestMessage.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
            }
        }

        // Copy body for POST/PUT/PATCH
        if (Request.ContentLength > 0 || Request.Headers.ContainsKey("Transfer-Encoding"))
        {
            requestMessage.Content = new StreamContent(Request.Body);
            if (Request.ContentType is not null)
            {
                requestMessage.Content.Headers.ContentType = 
                    System.Net.Http.Headers.MediaTypeHeaderValue.Parse(Request.ContentType);
            }
        }

        // Send request
        var response = await client.SendAsync(requestMessage);

        // Build response
        var content = await response.Content.ReadAsStringAsync();
        var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/json";
        
        return ((int)response.StatusCode, content, contentType);
    }
}
