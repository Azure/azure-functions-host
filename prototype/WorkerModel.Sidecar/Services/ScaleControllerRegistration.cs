using System.Net.Http.Json;
using System.Text.Json;

namespace WorkerModel.Sidecar.Services;

/// <summary>
/// Background service that registers this Sidecar with the Scale Controller on startup
/// and periodically sends heartbeats.
/// </summary>
public class ScaleControllerRegistration : BackgroundService
{
    private readonly ILogger<ScaleControllerRegistration> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly WorkerState _workerState;
    private readonly IConfiguration _configuration;

    private string? _workerId;
    private string? _scaleControllerEndpoint;

    public ScaleControllerRegistration(
        ILogger<ScaleControllerRegistration> logger,
        IHttpClientFactory httpClientFactory,
        WorkerState workerState,
        IConfiguration configuration)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _workerState = workerState;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _workerId = _configuration["SIDECAR_WORKER_ID"]
            ?? Environment.GetEnvironmentVariable("SIDECAR_WORKER_ID")
            ?? $"worker-{Guid.NewGuid():N}";
        
        // Try Aspire service discovery format first, then fall back to direct config
        _scaleControllerEndpoint = _configuration["services:scalecontroller:http:0"]
            ?? _configuration["services:scalecontroller:https:0"]
            ?? _configuration["SCALE_CONTROLLER_ENDPOINT"];

        if (string.IsNullOrEmpty(_scaleControllerEndpoint))
        {
            _logger.LogWarning("[Registration] Scale Controller endpoint not configured (checked services:scalecontroller:http:0 and SCALE_CONTROLLER_ENDPOINT), skipping registration");
            return;
        }
        
        _logger.LogInformation("[Registration] Worker ID: {WorkerId}, SC endpoint: {Endpoint}",
            _workerId, _scaleControllerEndpoint);

        // Wait a bit for the Scale Controller to be ready
        await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);

        // Register with retry
        for (int attempt = 1; attempt <= 10; attempt++)
        {
            try
            {
                await RegisterWithScaleControllerAsync(stoppingToken);
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Registration] Registration attempt {Attempt}/10 failed", attempt);
                if (attempt < 10)
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
                else
                {
                    _logger.LogError("[Registration] Failed to register after 10 attempts");
                }
            }
        }

        // Send periodic heartbeats
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            await SendHeartbeatAsync(stoppingToken);
        }
    }

    private async Task RegisterWithScaleControllerAsync(CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient();

        // Build our own accessible endpoint from Aspire-assigned URLs
        var urls = _configuration["ASPNETCORE_URLS"] ?? _configuration["urls"] ?? "http://localhost:5000";
        var sidecarEndpoint = urls.Split(';').FirstOrDefault(u => u.StartsWith("http://")) ?? urls.Split(';').First();

        var request = new
        {
            workerId = _workerId,
            sidecarEndpoint = sidecarEndpoint,
            isPlaceholder = true
        };

        _logger.LogInformation("[Registration] Registering worker '{WorkerId}' with SC at {Endpoint}...",
            _workerId, _scaleControllerEndpoint);

        var response = await client.PostAsJsonAsync(
            $"{_scaleControllerEndpoint}/api/workers/register",
            request,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogInformation("[Registration] Registered successfully: {Result}", result);
    }

    private async Task SendHeartbeatAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_scaleControllerEndpoint) || string.IsNullOrEmpty(_workerId))
        {
            return;
        }

        try
        {
            var client = _httpClientFactory.CreateClient();

            var context = _workerState.Context;
            var status = context.IsPlaceholder ? "Placeholder" : "Specialized";

            var request = new
            {
                id = _workerId,
                status = status,
                applicationId = context.IsPlaceholder ? null : context.Application?.ApplicationId,
                codeVersion = context.IsPlaceholder ? null : context.Application?.CodeVersion
            };

            var response = await client.PostAsJsonAsync(
                $"{_scaleControllerEndpoint}/api/workers/{_workerId}/heartbeat",
                request,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug("[Registration] Heartbeat response: {Status}", response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[Registration] Error sending heartbeat");
        }
    }
}
