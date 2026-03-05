using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using WorkerModel.Contracts;
using HostMessages = Microsoft.Azure.WebJobs.Script.Grpc.Messages;

namespace WorkerModel.Sidecar.Services;

/// <summary>
/// Handles the specialization flow when /assign is called by the Scale Controller.
/// </summary>
public class SpecializationService
{
    private readonly WorkerState _workerState;
    private readonly RuntimeConnectionManager _runtimeConnection;
    private readonly ILogger<SpecializationService> _logger;

    public SpecializationService(
        WorkerState workerState, 
        RuntimeConnectionManager runtimeConnection,
        ILogger<SpecializationService> logger)
    {
        _workerState = workerState;
        _runtimeConnection = runtimeConnection;
        _logger = logger;
    }

    /// <summary>
    /// Specializes the worker with the given assignment.
    /// Called when Scale Controller sends POST /assign.
    /// </summary>
    public async Task SpecializeAsync(WorkerAssignmentRequest request, CancellationToken cancellationToken)
    {
        if (!_workerState.IsPlaceholder)
        {
            throw new InvalidOperationException("Worker is already specialized");
        }

        var context = request.HostAssignmentContext;
        _logger.LogInformation("[Specialization] Starting specialization for site '{SiteName}'", context.SiteName);

        // Set up signal for SidecarRpcService to know when to transition
        _workerState.ExpectSpecializationComplete();

        // Step 1: Download and mount the app package (placeholder for now)
        var scriptRoot = await MountAppPackageAsync(context, cancellationToken);

        // Step 2: Update worker state with application info
        var appDefinition = new ApplicationDefinition(
            ApplicationId: context.SiteName,
            MetadataVersion: "1",
            CodeVersion: context.Environment.GetValueOrDefault("CODE_VERSION", "v1.0.0"),
            ScriptRoot: scriptRoot);

        _workerState.Specialize(appDefinition, request.RuntimeEndpoint);

        // Step 3: Send FunctionEnvironmentReloadRequest directly to the worker
        // (NOT to Runtime - worker needs to reload before we connect to Runtime)
        await SendEnvironmentReloadToWorkerAsync(context, scriptRoot, cancellationToken);

        // Step 4: Connect to the assigned Runtime
        await _runtimeConnection.ConnectAsync(request.RuntimeEndpoint, cancellationToken);

        // Step 5: Signal that specialization is complete (Runtime connected)
        // SidecarRpcService will pick this up and transition to relay mode
        _workerState.SignalSpecializationComplete();

        // Step 6: Wait for the Runtime to be fully ready (WorkerConnect processed,
        // JobHost started, HTTP routes registered). This ensures /assign doesn't
        // return 200 until the Runtime can actually serve HTTP traffic, preventing
        // the ScaleController from forwarding requests that would get 404.
        _logger.LogInformation("[Specialization] Waiting for Runtime to be ready...");
        await _workerState.WaitForRuntimeReadyAsync(cancellationToken);

        _logger.LogInformation("[Specialization] Completed for site '{SiteName}'", context.SiteName);
    }

    private async Task<string> MountAppPackageAsync(Contracts.HostAssignmentContext context, CancellationToken cancellationToken)
    {
        // Get the package URL from environment
        var packageUrl = context.Environment.GetValueOrDefault("WEBSITE_RUN_FROM_PACKAGE", string.Empty);
        var scriptRoot = context.Environment.GetValueOrDefault("AzureWebJobsScriptRoot", "/home/site/wwwroot");

        if (string.IsNullOrEmpty(packageUrl))
        {
            _logger.LogInformation("[Specialization] No WEBSITE_RUN_FROM_PACKAGE specified, skipping download");
            return scriptRoot;
        }

        _logger.LogInformation("[Specialization] Would download and mount package from: {PackageUrl}", packageUrl);
        _logger.LogInformation("[Specialization] Mount point: {ScriptRoot}", scriptRoot);

        // TODO: Implement actual download and SquashFS mounting
        // For prototype, we just log and return the script root
        // In production:
        // 1. Download zip from blob storage
        // 2. Mount via SquashFS (squashfuse)
        // 3. Return the mount point path

        await Task.Delay(100, cancellationToken); // Simulate download time

        return scriptRoot;
    }

    private async Task SendEnvironmentReloadToWorkerAsync(Contracts.HostAssignmentContext context, string scriptRoot, CancellationToken cancellationToken)
    {
        _logger.LogInformation("[Specialization] Sending FunctionEnvironmentReloadRequest to worker...");

        var reloadRequest = new HostMessages.StreamingMessage
        {
            RequestId = Guid.NewGuid().ToString(),
            FunctionEnvironmentReloadRequest = new HostMessages.FunctionEnvironmentReloadRequest
            {
                FunctionAppDirectory = scriptRoot
            }
        };

        // Add environment variables to the request
        foreach (var (key, value) in context.Environment)
        {
            reloadRequest.FunctionEnvironmentReloadRequest.EnvironmentVariables[key] = value;
        }

        // Ensure the worker SDK can find the app directory during FunctionLoadRequest processing
        reloadRequest.FunctionEnvironmentReloadRequest.EnvironmentVariables["FUNCTIONS_APPLICATION_DIRECTORY"] = scriptRoot;
        reloadRequest.FunctionEnvironmentReloadRequest.EnvironmentVariables["FUNCTIONS_WORKER_DIRECTORY"] = scriptRoot;

        // Set up expectation for response
        _workerState.ExpectReloadResponse();

        // Send directly to worker via the stored gRPC stream
        await _workerState.SendToWorkerAsync(reloadRequest, cancellationToken);
        _logger.LogInformation("[Specialization] FunctionEnvironmentReloadRequest sent to worker");

        // Wait for the worker to respond
        var response = await _workerState.WaitForReloadResponseAsync(cancellationToken);
        
        if (response.FunctionEnvironmentReloadResponse?.Result?.Status == HostMessages.StatusResult.Types.Status.Success)
        {
            _logger.LogInformation("[Specialization] Worker reload completed successfully");
        }
        else
        {
            var error = response.FunctionEnvironmentReloadResponse?.Result?.Exception?.Message ?? "Unknown error";
            _logger.LogWarning("[Specialization] Worker reload completed with status: {Status}, error: {Error}",
                response.FunctionEnvironmentReloadResponse?.Result?.Status, error);
        }
    }
}
