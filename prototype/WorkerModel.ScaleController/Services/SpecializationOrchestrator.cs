using System.Text;
using System.Text.Json;
using WorkerModel.Contracts;
using WorkerModel.ScaleController.Models;

namespace WorkerModel.ScaleController.Services;

/// <summary>
/// Coordinates the specialization flow between Runtime and Worker.
/// </summary>
public class SpecializationOrchestrator
{
    private readonly ApplicationService _appService;
    private readonly RuntimeService _runtimeService;
    private readonly WorkerService _workerService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SpecializationOrchestrator> _logger;

    public SpecializationOrchestrator(
        ApplicationService appService,
        RuntimeService runtimeService,
        WorkerService workerService,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<SpecializationOrchestrator> logger)
    {
        _appService = appService;
        _runtimeService = runtimeService;
        _workerService = workerService;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Triggers specialization for a specific worker with a specific app.
    /// Late-binding: Matches Runtime + Worker at specialization time.
    /// </summary>
    public async Task SpecializeWorkerAsync(string workerId, string appId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[Specialization] Starting specialization for worker '{WorkerId}' with app '{AppId}'",
            workerId, appId);

        // 1. Get the app metadata
        var app = await _appService.GetAsync(appId);
        if (app is null)
        {
            throw new InvalidOperationException($"Application '{appId}' not found");
        }

        if (string.IsNullOrEmpty(app.CodeVersion) || string.IsNullOrEmpty(app.BlobPath))
        {
            throw new InvalidOperationException($"Application '{appId}' has no deployed code");
        }

        // 2. Get the worker
        var worker = await _workerService.GetAsync(workerId);
        if (worker is null)
        {
            throw new InvalidOperationException($"Worker '{workerId}' not found");
        }

        if (worker.Status != WorkerStatus.Placeholder)
        {
            throw new InvalidOperationException($"Worker '{workerId}' is not in placeholder status");
        }

        // 3. Find an available placeholder Runtime (late-binding)
        var availableRuntimes = await _runtimeService.GetAvailablePlaceholdersAsync();
        if (availableRuntimes.Count == 0)
        {
            throw new InvalidOperationException("No available placeholder Runtimes");
        }

        var runtime = availableRuntimes.First();
        _logger.LogInformation("[Specialization] Selected runtime '{RuntimeId}' for worker '{WorkerId}'",
            runtime.Id, workerId);

        // 4. Update statuses to Specializing
        await _runtimeService.UpdateStatusAsync(runtime.Id, RuntimeStatus.Specializing, appId);
        await _workerService.UpdateStatusAsync(workerId, WorkerStatus.Specializing, appId, app.CodeVersion, runtime.Id);

        try
        {
            // 5. Get the package download URL
            var packageUrl = await _appService.GetDownloadUrlAsync(appId, app.CodeVersion);

            // 6. Build the HostAssignmentContext
            var hostContext = BuildHostAssignmentContext(app);

            // 7. Specialize the Runtime first (pass packageUrl for mount)
            await SpecializeRuntimeAsync(runtime, hostContext, packageUrl, cancellationToken);

            // 7b. Persist the actual mount path back to the app so scale-out workers can find it
            var actualScriptRoot = hostContext.Environment.GetValueOrDefault("AzureWebJobsScriptRoot", "/home/site/wwwroot");
            app.Environment["AzureWebJobsScriptRoot"] = actualScriptRoot;
            _logger.LogInformation("[Specialization] Persisted AzureWebJobsScriptRoot='{ScriptRoot}' to app '{AppId}'",
                actualScriptRoot, appId);

            // 8. Specialize the Worker (with Runtime endpoint)
            await SpecializeWorkerSidecarAsync(worker, runtime, hostContext, cancellationToken);

            // 9. Update statuses to Specialized
            await _runtimeService.UpdateStatusAsync(runtime.Id, RuntimeStatus.Specialized, appId);
            await _workerService.UpdateStatusAsync(workerId, WorkerStatus.Specialized, appId, app.CodeVersion, runtime.Id);

            _logger.LogInformation("[Specialization] Completed for worker '{WorkerId}' with runtime '{RuntimeId}'",
                workerId, runtime.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Specialization] Failed for worker '{WorkerId}'", workerId);

            // Revert to placeholder on failure
            await _runtimeService.UpdateStatusAsync(runtime.Id, RuntimeStatus.Placeholder);
            await _workerService.UpdateStatusAsync(workerId, WorkerStatus.Placeholder);
            throw;
        }
    }

    private HostAssignmentContext BuildHostAssignmentContext(ApplicationInfo app)
    {
        // Note: AzureWebJobsScriptRoot will be updated after mount completes with actual path
        // We don't set WEBSITE_RUN_FROM_PACKAGE - files are already mounted by RuntimeSidecar
        var environment = new Dictionary<string, string>(app.Environment)
        {
            ["FUNCTIONS_WORKER_RUNTIME"] = app.Language,
            ["WEBSITE_SITE_NAME"] = app.Id,
            ["CODE_VERSION"] = app.CodeVersion ?? "unknown"
        };

        return new HostAssignmentContext
        {
            SiteName = app.Id,
            SiteId = app.Id,
            Environment = environment,
        };
    }

    private async Task SpecializeRuntimeAsync(
        RuntimeInfo runtime,
        HostAssignmentContext context,
        string packageUrl,
        CancellationToken cancellationToken)
    {
        // For the prototype, we call RuntimeSidecar's /mount endpoint to download and extract the package.
        // In production Azure, this would be /admin/instance/assign to the actual Runtime.
        _logger.LogInformation("[Specialization] Calling RuntimeSidecar /mount at {Endpoint}",
            runtime.HttpEndpoint);

        var client = _httpClientFactory.CreateClient("specialization");

        // Build the request body for RuntimeSidecar's /mount endpoint
        var requestBody = new
        {
            applicationId = context.SiteName,
            codeVersion = context.Environment.GetValueOrDefault("CODE_VERSION", "unknown"),
            packageUrl = packageUrl,
            mountPoint = (string?)null // Use default mount point
        };

        var content = new StringContent(
            JsonSerializer.Serialize(requestBody),
            Encoding.UTF8,
            "application/json");

        var response = await client.PostAsync(
            $"{runtime.HttpEndpoint}/mount",
            content,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"RuntimeSidecar mount failed: {response.StatusCode} - {error}");
        }

        // Parse mount response to get actual script root path
        var mountResponseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        var mountResponse = JsonSerializer.Deserialize<JsonElement>(mountResponseJson);
        var scriptRoot = mountResponse.GetProperty("mountPoint").GetString() ?? "/home/site/wwwroot";

        _logger.LogInformation("[Specialization] RuntimeSidecar '{RuntimeId}' mount completed at {ScriptRoot}", 
            runtime.Id, scriptRoot);

        // Update AzureWebJobsScriptRoot with actual mount path
        context.Environment["AzureWebJobsScriptRoot"] = scriptRoot;

        // Step 2: Call RuntimeSidecar's /assign endpoint to specialize WebHost
        // This will proxy to WebHost's /admin/instance/assign to apply env vars, load host.json, etc.
        _logger.LogInformation("[Specialization] Calling RuntimeSidecar /assign at {Endpoint}",
            runtime.HttpEndpoint);

        var assignRequest = new
        {
            siteName = context.SiteName,
            siteId = context.SiteId,
            environment = context.Environment,
        };

        var assignContent = new StringContent(
            JsonSerializer.Serialize(assignRequest),
            Encoding.UTF8,
            "application/json");

        var assignResponse = await client.PostAsync(
            $"{runtime.HttpEndpoint}/assign",
            assignContent,
            cancellationToken);

        if (!assignResponse.IsSuccessStatusCode)
        {
            var assignError = await assignResponse.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("[Specialization] RuntimeSidecar assign returned {Status}: {Error}. " +
                "WebHost may still function if auth is blocking but specialization middleware is disabled.",
                assignResponse.StatusCode, assignError);
            // Don't fail - WebHost might work without explicit assign in some modes
        }
        else
        {
            _logger.LogInformation("[Specialization] RuntimeSidecar '{RuntimeId}' assign completed", runtime.Id);
        }
    }

    private async Task SpecializeWorkerSidecarAsync(
        WorkerInfo worker,
        RuntimeInfo runtime,
        HostAssignmentContext context,
        CancellationToken cancellationToken)
    {
        // Get the WebHost's gRPC endpoint for worker communication
        // The gRPC server runs on a separate port (7072) from the HTTP server (7071)
        var runtimeGrpcEndpoint = _configuration["services:runtime:grpc:0"]
            ?? "http://localhost:7072";  // Fixed gRPC port for prototype

        _logger.LogInformation("[Specialization] Calling Worker Sidecar /assign at {Endpoint} with RuntimeEndpoint={RuntimeEndpoint}",
            worker.SidecarEndpoint, runtimeGrpcEndpoint);

        var client = _httpClientFactory.CreateClient("specialization");

        // Build the request body for Sidecar's /assign
        var requestBody = new WorkerAssignmentRequest
        {
            RuntimeEndpoint = runtimeGrpcEndpoint,
            HostAssignmentContext = context,
        };

        var content = new StringContent(
            JsonSerializer.Serialize(requestBody, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }),
            Encoding.UTF8,
            "application/json");

        var response = await client.PostAsync(
            $"{worker.SidecarEndpoint}/assign",
            content,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Worker Sidecar specialization failed: {response.StatusCode} - {error}");
        }

        _logger.LogInformation("[Specialization] Worker Sidecar '{WorkerId}' specialization started", worker.Id);
    }

    /// <summary>
    /// Scales out by specializing an additional worker to an already-specialized runtime.
    /// Skips runtime specialization (mount/assign) since the runtime is already running the app.
    /// </summary>
    public async Task ScaleOutWorkerAsync(string workerId, string appId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[ScaleOut] Starting scale-out of worker '{WorkerId}' for app '{AppId}'",
            workerId, appId);

        // 1. Get the app metadata
        var app = await _appService.GetAsync(appId);
        if (app is null)
        {
            throw new InvalidOperationException($"Application '{appId}' not found");
        }

        // 2. Get the worker (must be a placeholder)
        var worker = await _workerService.GetAsync(workerId);
        if (worker is null)
        {
            throw new InvalidOperationException($"Worker '{workerId}' not found");
        }

        if (worker.Status != WorkerStatus.Placeholder)
        {
            _logger.LogWarning("[ScaleOut] Worker '{WorkerId}' is already {Status}, skipping", workerId, worker.Status);
            return;
        }

        // 3. Find the existing specialized runtime for this app (reuse it)
        var runtime = await _runtimeService.GetSpecializedForAppAsync(appId);
        if (runtime is null)
        {
            throw new InvalidOperationException($"No specialized runtime found for app '{appId}'");
        }

        _logger.LogInformation("[ScaleOut] Reusing runtime '{RuntimeId}' for worker '{WorkerId}'",
            runtime.Id, workerId);

        // 4. Update worker status to Specializing
        await _workerService.UpdateStatusAsync(workerId, WorkerStatus.Specializing, appId, app.CodeVersion, runtime.Id);

        try
        {
            // 5. Build HostAssignmentContext (same as original specialization)
            var hostContext = BuildHostAssignmentContext(app);

            // Use the mount path that was persisted during the first specialization.
            // If not found, query the RuntimeSidecar's status endpoint for the actual mount point.
            var scriptRoot = app.Environment.GetValueOrDefault("AzureWebJobsScriptRoot");
            if (string.IsNullOrEmpty(scriptRoot))
            {
                scriptRoot = await GetRuntimeMountPointAsync(runtime, cancellationToken)
                    ?? "/home/site/wwwroot";
                _logger.LogWarning("[ScaleOut] AzureWebJobsScriptRoot not in app env, resolved from RuntimeSidecar: {ScriptRoot}",
                    scriptRoot);
            }

            hostContext.Environment["AzureWebJobsScriptRoot"] = scriptRoot;
            _logger.LogInformation("[ScaleOut] Using AzureWebJobsScriptRoot='{ScriptRoot}' for worker '{WorkerId}'",
                scriptRoot, workerId);

            // 6. Only specialize the Worker Sidecar (runtime is already running)
            await SpecializeWorkerSidecarAsync(worker, runtime, hostContext, cancellationToken);

            // 7. Update worker status to Specialized
            await _workerService.UpdateStatusAsync(workerId, WorkerStatus.Specialized, appId, app.CodeVersion, runtime.Id);

            _logger.LogInformation("[ScaleOut] Completed for worker '{WorkerId}' with runtime '{RuntimeId}'",
                workerId, runtime.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ScaleOut] Failed for worker '{WorkerId}'", workerId);
            await _workerService.UpdateStatusAsync(workerId, WorkerStatus.Placeholder);
            throw;
        }
    }

    /// <summary>
    /// Queries the RuntimeSidecar's status endpoint to get the current mount point.
    /// </summary>
    private async Task<string?> GetRuntimeMountPointAsync(RuntimeInfo runtime, CancellationToken cancellationToken)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("specialization");
            var response = await client.GetAsync($"{runtime.HttpEndpoint}/status", cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                var doc = JsonSerializer.Deserialize<JsonElement>(json);

                if (doc.TryGetProperty("mountPoint", out var mp))
                {
                    return mp.GetString();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[ScaleOut] Failed to query RuntimeSidecar status for mount point");
        }

        return null;
    }
}
