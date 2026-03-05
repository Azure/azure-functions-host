using System.Net.Http.Json;

namespace WorkerModel.RuntimeSidecar.Services;

/// <summary>
/// Background service that registers this Runtime Sidecar with the Scale Controller
/// on startup and periodically sends heartbeats.
/// </summary>
public class ScaleControllerRegistration : BackgroundService
{
    private readonly ILogger<ScaleControllerRegistration> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly IHostApplicationLifetime _lifetime;

    private string? _runtimeId;
    private string? _scaleControllerEndpoint;

    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);
    private const int MaxRegistrationRetries = 10;

    public ScaleControllerRegistration(
        ILogger<ScaleControllerRegistration> logger,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        IHostApplicationLifetime lifetime)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _lifetime = lifetime;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _runtimeId = _configuration["RUNTIME_SIDECAR_ID"] ?? $"runtime-{Guid.NewGuid():N}";

        // Aspire service discovery injects the SC URL via configuration
        _scaleControllerEndpoint = _configuration["services:scalecontroller:http:0"]
            ?? _configuration["services:scalecontroller:https:0"]
            ?? _configuration["SCALE_CONTROLLER_ENDPOINT"];

        if (string.IsNullOrEmpty(_scaleControllerEndpoint))
        {
            _logger.LogWarning("[RuntimeRegistration] Scale Controller endpoint not configured, skipping registration");
            return;
        }

        _logger.LogInformation("[RuntimeRegistration] Runtime ID: {RuntimeId}, SC endpoint: {Endpoint}",
            _runtimeId, _scaleControllerEndpoint);

        // Wait for the application to finish starting before registering
        var startupTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _lifetime.ApplicationStarted.Register(() => startupTcs.TrySetResult());
        await Task.WhenAny(startupTcs.Task, Task.Delay(Timeout.Infinite, stoppingToken));
        stoppingToken.ThrowIfCancellationRequested();

        // Small extra delay so SC is ready
        await Task.Delay(InitialDelay, stoppingToken);

        // Register with retry
        await RegisterWithRetryAsync(stoppingToken);

        // Periodic heartbeats
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(HeartbeatInterval, stoppingToken);
            await SendHeartbeatAsync(stoppingToken);
        }
    }

    private async Task RegisterWithRetryAsync(CancellationToken cancellationToken)
    {
        for (int attempt = 1; attempt <= MaxRegistrationRetries; attempt++)
        {
            try
            {
                await RegisterAsync(cancellationToken);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[RuntimeRegistration] Registration attempt {Attempt}/{Max} failed",
                    attempt, MaxRegistrationRetries);

                if (attempt < MaxRegistrationRetries)
                {
                    await Task.Delay(RetryDelay, cancellationToken);
                }
            }
        }

        _logger.LogError("[RuntimeRegistration] Failed to register after {Max} attempts", MaxRegistrationRetries);
    }

    private async Task RegisterAsync(CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient();

        // Build our own accessible endpoints from Aspire-assigned URLs
        var urls = _configuration["ASPNETCORE_URLS"] ?? _configuration["urls"] ?? "http://localhost:5000";
        var httpEndpoint = urls.Split(';').FirstOrDefault(u => u.StartsWith("http://")) ?? urls.Split(';').First();

        // Get the WebHost (runtime) endpoint from Aspire service discovery
        // Check named "webhost" endpoint first (non-proxied), then fall back to default
        var webHostEndpoint = _configuration["services:runtime:webhost:0"]
            ?? _configuration["services:runtime:http:0"]
            ?? string.Empty;

        _logger.LogInformation("[RuntimeRegistration] WebHost endpoint from service discovery: {WebHostEndpoint}", 
            string.IsNullOrEmpty(webHostEndpoint) ? "(not available)" : webHostEndpoint);

        var request = new
        {
            runtimeId = _runtimeId,
            grpcEndpoint = webHostEndpoint, // Use grpcEndpoint field for WebHost URL
            httpEndpoint = httpEndpoint     // RuntimeSidecar's own HTTP endpoint
        };

        _logger.LogInformation("[RuntimeRegistration] Registering runtime '{RuntimeId}' with SC at {Endpoint}...",
            _runtimeId, _scaleControllerEndpoint);

        var response = await client.PostAsJsonAsync(
            $"{_scaleControllerEndpoint}/api/runtimes/register",
            request,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogInformation("[RuntimeRegistration] Registered successfully: {Result}", result);
    }

    private async Task SendHeartbeatAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_scaleControllerEndpoint) || string.IsNullOrEmpty(_runtimeId))
        {
            return;
        }

        try
        {
            var client = _httpClientFactory.CreateClient();

            // Get the current WebHost endpoint - it may have started since registration
            // Check named "webhost" endpoint first (non-proxied), then fall back to default
            var webHostEndpoint = _configuration["services:runtime:webhost:0"]
                ?? _configuration["services:runtime:http:0"]
                ?? string.Empty;

            // Check if WebHost is actually reachable (not just configured)
            if (!string.IsNullOrEmpty(webHostEndpoint))
            {
                try
                {
                    var probe = await client.GetAsync($"{webHostEndpoint}/", cancellationToken);
                    // If successful, include endpoint in heartbeat to update SC
                }
                catch
                {
                    // WebHost not ready yet, don't send endpoint
                    webHostEndpoint = string.Empty;
                }
            }

            var heartbeat = new
            {
                webHostEndpoint = webHostEndpoint
            };

            var response = await client.PostAsJsonAsync(
                $"{_scaleControllerEndpoint}/api/runtimes/{_runtimeId}/heartbeat",
                heartbeat,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug("[RuntimeRegistration] Heartbeat response: {Status}", response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[RuntimeRegistration] Error sending heartbeat");
        }
    }
}
