// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Globalization;
using System.Security.Cryptography;
using Azure.Functions.ComputeSeparation.AppHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

const string ManagementEndpointName = "management";
const string RuntimeGrpcEndpointName = "runtime-grpc";
const string WorkerGrpcEndpointName = "worker-grpc";
const string HttpEndpointName = "http";

IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(args);
string mode = builder.Configuration["ComputeSeparation:Mode"] ?? "project";
bool useContainers = string.Equals(mode, "container", StringComparison.OrdinalIgnoreCase);
if (!useContainers && !string.Equals(mode, "project", StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException("ComputeSeparation:Mode must be 'project' or 'container'.");
}

bool multipleWorkers = builder.Configuration.GetValue("ComputeSeparation:MultipleWorkers", false);
bool autoLink = builder.Configuration.GetValue("ComputeSeparation:AutoLink", true);
bool verifyQueue = builder.Configuration.GetValue("ComputeSeparation:VerifyQueueTrigger", false);
if (verifyQueue && (!useContainers || !autoLink))
{
    throw new InvalidOperationException("QueueTrigger acceptance requires container mode with automatic linking.");
}

var storage = builder.AddAzureStorage("queue-storage").RunAsEmulator();
var queues = storage.AddQueues("queues");
var blobs = storage.AddBlobs("blobs");
string inputQueue = verifyQueue ? $"payload-input-{Guid.NewGuid():N}" : "sample-input";
string outputQueue = verifyQueue ? $"payload-output-{Guid.NewGuid():N}" : "sample-output";
if (multipleWorkers && (useContainers || autoLink))
{
    throw new InvalidOperationException("Multiple workers require project mode with ComputeSeparation:AutoLink=false.");
}

string workerId = builder.Configuration["ComputeSeparation:WorkerId"] ?? $"aspire-{Guid.NewGuid():N}";
string podName = builder.Configuration["ComputeSeparation:PodName"] ?? "worker-pod-1";
using HarnessRunDirectory? runDirectory = useContainers ? null : new();
string repositoryRoot = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "..", ".."));
await using ContainerTopology? topology = useContainers ? new(repositoryRoot) : null;
EndpointReference hostEndpoint;
EndpointReference? platformEndpoint = null;
ReferenceExpression workerGrpcEndpoint;
string[] dependencies;

if (topology is not null)
{
    builder.Configuration["DcpPublisher:WaitForResourceCleanup"] = "true";
    builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromMinutes(2));

    IResourceBuilder<ExecutableResource> runtime = builder.AddExecutable("runtime", "docker", repositoryRoot,
            "compose", "--file", topology.Runtime.ComposeFile, "up", "--build", "--no-color")
        .WithHttpEndpoint(targetPort: GetOptionalPort(builder.Configuration, "ComputeSeparation:HostPort"),
            name: HttpEndpointName, env: "COMPUTE_HOST_PORT")
        .WithHttpHealthCheck("/admin/instance/http-health", endpointName: HttpEndpointName)
        .WithEnvironment("COMPUTE_NETWORK_NAME", topology.NetworkName)
        .WithEnvironment(ComposeSession.ProjectNameVariable, topology.Runtime.ProjectName)
        .WithEnvironment(context => context.EnvironmentVariables[ComposeSession.GenerationVariable] = topology.Runtime.Generation)
        .OnBeforeResourceStarted((_, _, cancellationToken) => topology.Runtime.BeforeStartAsync(cancellationToken))
        .OnResourceStopped((_, stopped, _) => topology.Runtime.StopRunAsync(
            stopped.ResourceEvent.Snapshot.EnvironmentVariables.FirstOrDefault(variable =>
                string.Equals(variable.Name, ComposeSession.GenerationVariable, StringComparison.Ordinal))?.Value));

    IResourceBuilder<ExecutableResource> workerPod = builder.AddExecutable("worker-pod-1", "docker", repositoryRoot,
            "compose", "--file", topology.WorkerPod.ComposeFile, "up", "--build", "--no-color")
        .WithHttpEndpoint(targetPort: GetOptionalPort(builder.Configuration, "ComputeSeparation:ManagementPort"),
            name: ManagementEndpointName, env: "WORKER_PROXY_MANAGEMENT_PORT")
        .WithHttpHealthCheck("/admin/instance/ready", endpointName: ManagementEndpointName)
        .WithEnvironment("COMPUTE_NETWORK_NAME", topology.NetworkName)
        .WithEnvironment("WORKER_PROXY_ALIAS", topology.ProxyAlias)
        .WithEnvironment("WORKER_ID", workerId)
        .WithEnvironment("WORKER_POD_NAME", workerId)
        .WithEnvironment("WORKER_REQUEST_ID", Guid.NewGuid().ToString())
        .WithEnvironment(ComposeSession.ProjectNameVariable, topology.WorkerPod.ProjectName)
        .WithEnvironment(context => context.EnvironmentVariables[ComposeSession.GenerationVariable] = topology.WorkerPod.Generation)
        .WaitFor(runtime)
        .OnBeforeResourceStarted((_, _, cancellationToken) => topology.WorkerPod.BeforeStartAsync(cancellationToken))
        .OnResourceStopped((_, stopped, _) => topology.WorkerPod.StopRunAsync(
            stopped.ResourceEvent.Snapshot.EnvironmentVariables.FirstOrDefault(variable =>
                string.Equals(variable.Name, ComposeSession.GenerationVariable, StringComparison.Ordinal))?.Value));

    foreach (IResourceBuilder<ExecutableResource> resource in new[] { runtime, workerPod })
    {
        resource.WaitFor(queues);
        resource.WaitFor(blobs);
        resource.WithEnvironment(async context =>
        {
            string connection = await QueueTriggerVerificationService.GetStorageConnectionAsync(
                queues.Resource, blobs.Resource, context.CancellationToken);
            // Compose resources run in containers although Aspire starts Compose as a host executable.
            context.EnvironmentVariables["QUEUE_STORAGE_CONNECTION"] = connection
                .Replace("127.0.0.1", "host.docker.internal", StringComparison.Ordinal)
                .Replace("localhost", "host.docker.internal", StringComparison.Ordinal);
            context.EnvironmentVariables["EXTENSION_INPUT_QUEUE"] = inputQueue;
            context.EnvironmentVariables["EXTENSION_OUTPUT_QUEUE"] = outputQueue;
        });
    }

    hostEndpoint = runtime.GetEndpoint(HttpEndpointName);
    workerGrpcEndpoint = ReferenceExpression.Create($"http://{topology.ProxyAlias}:50053");
    dependencies = ["runtime", "worker-pod-1"];
}
else
{
    IResourceBuilder<ProjectResource> functionsHost = builder.AddProject<Projects.Functions_Host>("functions-host", launchProfileName: null)
        .WithHttpEndpoint(targetPort: GetOptionalPort(builder.Configuration, "ComputeSeparation:HostPort"), name: HttpEndpointName)
        .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
        .WithEnvironment("AZURE_FUNCTIONS_ENVIRONMENT", "Development")
        .WithEnvironment("FUNCTIONS_WORKER_RUNTIME", "dotnet-isolated")
        .WithEnvironment("AzureWebJobsSecretStorageType", "Files")
        .WithEnvironment("AzureWebJobsScriptRoot", runDirectory!.ScriptPath)
        .WithEnvironment("FUNCTIONS_LOG_PATH", runDirectory.LogPath)
        .WithEnvironment("FUNCTIONS_SECRETS_PATH", runDirectory.SecretsPath)
        .WithEnvironment("AzureFunctionsWebHost__hostid", "aspire-worker")
        .WithEnvironment("AzureFunctionsWebHost__IsFileSystemReadOnly", "true")
        .WithEnvironment("AzureFunctionsJobHost__logging__logLevel__default", "Information")
        .WithEnvironment("AzureFunctionsJobHost__logging__console__isEnabled", "true")
        .WithEnvironment("ExtensionPayloadInputQueue", inputQueue)
        .WithEnvironment("ExtensionPayloadOutputQueue", outputQueue)
        .WithEnvironment(async context => context.EnvironmentVariables["AzureWebJobsStorage"] =
            await QueueTriggerVerificationService.GetStorageConnectionAsync(queues.Resource, blobs.Resource, context.CancellationToken))
        .WaitFor(queues)
        .WaitFor(blobs)
        .WithHttpHealthCheck("/admin/instance/http-health", endpointName: HttpEndpointName);

    functionsHost.WithEnvironment("ASPNETCORE_URLS",
        ReferenceExpression.Create($"http://127.0.0.1:{functionsHost.GetEndpoint(HttpEndpointName).Property(EndpointProperty.TargetPort)}"));

    IResourceBuilder<ProjectResource> fakePlatform = builder.AddProject<Projects.FakePlatform>("fake-platform", launchProfileName: null)
        .WithHttpEndpoint(targetPort: GetOptionalPort(builder.Configuration, "ComputeSeparation:PlatformPort"), name: HttpEndpointName)
        .WithHttpHealthCheck("/health", endpointName: HttpEndpointName)
        .WithEnvironment("FakePlatform__HostUri", functionsHost.GetEndpoint(HttpEndpointName));

    if (builder.Configuration.GetValue("ComputeSeparation:PlaceholderMode", false))
    {
        // The platform assigns the app once; each worker is then assigned and linked independently.
        string encryptionKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        functionsHost
            .WithEnvironment("WEBSITE_PLACEHOLDER_MODE", "1")
            .WithEnvironment("WEBSITE_SITE_NAME", "aspire-sample-app")
            .WithEnvironment("CONTAINER_ENCRYPTION_KEY", encryptionKey);
        fakePlatform.WithEnvironment("FakePlatform__EncryptionKey", encryptionKey);
    }

    IResourceBuilder<ProjectResource> proxyProject = AddProjectWorker(secondWorker: false);
    if (multipleWorkers)
    {
        AddProjectWorker(secondWorker: true);
    }

    platformEndpoint = fakePlatform.GetEndpoint(HttpEndpointName);

    hostEndpoint = functionsHost.GetEndpoint(HttpEndpointName);
    workerGrpcEndpoint = ReferenceExpression.Create($"{proxyProject.GetEndpoint(RuntimeGrpcEndpointName)}");
    dependencies = ["functions-host", "worker-proxy-1", "isolated-worker", "fake-platform"];

    IResourceBuilder<ProjectResource> AddProjectWorker(bool secondWorker)
    {
        int index = secondWorker ? 1 : 0;
        string proxyName = $"worker-proxy-{index + 1}";
        string suffix = secondWorker ? "-2" : string.Empty;
        string configurationPrefix = secondWorker ? "ComputeSeparation:SecondWorker" : "ComputeSeparation";
        string linkedWorkerId = secondWorker
            ? builder.Configuration["ComputeSeparation:SecondWorker:WorkerId"] ?? $"aspire-{Guid.NewGuid():N}"
            : workerId;
        string workerPodName = secondWorker ? "worker-pod-2" : podName;
        IResourceBuilder<ProjectResource> proxy = builder.AddProject<Projects.Functions_WorkerProxy>(proxyName, launchProfileName: null)
            .WithHttpEndpoint(targetPort: GetOptionalPort(builder.Configuration, $"{configurationPrefix}:ManagementPort"), name: ManagementEndpointName)
            .WithHttpEndpoint(targetPort: GetOptionalPort(builder.Configuration, $"{configurationPrefix}:RuntimeGrpcPort"), name: RuntimeGrpcEndpointName)
            .WithHttpEndpoint(targetPort: GetOptionalPort(builder.Configuration, $"{configurationPrefix}:WorkerGrpcPort"), name: WorkerGrpcEndpointName)
            .WithHttpEndpoint(targetPort: GetOptionalPort(builder.Configuration, $"{configurationPrefix}:HttpPort"), name: HttpEndpointName)
            .WithHttpHealthCheck("/admin/instance/ready", endpointName: ManagementEndpointName);

        proxy.WithEnvironment(context =>
        {
            // Each proxy represents a separate pod, with its own worker connection and runtime-facing endpoints.
            context.EnvironmentVariables["WEBSITE_POD_NAME"] = workerPodName;
            context.EnvironmentVariables["WORKERPROXY__MANAGEMENTPORT"] =
                proxy.GetEndpoint(ManagementEndpointName).Property(EndpointProperty.TargetPort);
            context.EnvironmentVariables["WORKERPROXY__RUNTIMEGRPCPORT"] =
                proxy.GetEndpoint(RuntimeGrpcEndpointName).Property(EndpointProperty.TargetPort);
            context.EnvironmentVariables["WORKERPROXY__WORKERGRPCPORT"] =
                proxy.GetEndpoint(WorkerGrpcEndpointName).Property(EndpointProperty.TargetPort);
            context.EnvironmentVariables["WORKERPROXY__HTTPPORT"] =
                proxy.GetEndpoint(HttpEndpointName).Property(EndpointProperty.TargetPort);
            context.EnvironmentVariables["WORKERPROXY__HTTPPROXYENDPOINT"] = proxy.GetEndpoint(HttpEndpointName);
            context.EnvironmentVariables["Logging__LogLevel__Azure.Functions.WorkerProxy.Http"] = "Debug";
        });

        builder.AddProject<Projects.SampleIsolatedApp>($"isolated-worker{suffix}", launchProfileName: null)
            .WithEnvironment("ExtensionPayloadInputQueue", inputQueue)
            .WithEnvironment("ExtensionPayloadOutputQueue", outputQueue)
            .WithEnvironment(async context => context.EnvironmentVariables["AzureWebJobsStorage"] =
                await QueueTriggerVerificationService.GetStorageConnectionAsync(queues.Resource, blobs.Resource, context.CancellationToken))
            .WithArgs(
                "--functions-uri", proxy.GetEndpoint(WorkerGrpcEndpointName),
                "--functions-worker-id", linkedWorkerId,
                "--functions-request-id", Guid.NewGuid().ToString(),
                "--functions-grpc-max-message-length", "134217728")
            .WaitFor(proxy)
            .WaitFor(queues)
            .WaitFor(blobs);

        // One platform owns the known proxy mappings; simulation requests select a mapping by resource name.
        fakePlatform
            .WithEnvironment($"FakePlatform__WorkerProxies__{index}__Name", proxyName)
            .WithEnvironment($"FakePlatform__WorkerProxies__{index}__ManagementUri", proxy.GetEndpoint(ManagementEndpointName))
            .WithEnvironment($"FakePlatform__WorkerProxies__{index}__WorkerGrpcEndpoint", proxy.GetEndpoint(RuntimeGrpcEndpointName))
            .WithEnvironment($"FakePlatform__WorkerProxies__{index}__WorkerId", linkedWorkerId);

        return proxy;
    }
}

if (autoLink)
{
    builder.Services.AddSingleton(services => new HostLinkService(
        hostEndpoint,
        workerGrpcEndpoint,
        platformEndpoint,
        workerId,
        dependencies,
        services.GetRequiredService<ResourceNotificationService>(),
        services.GetRequiredService<Microsoft.Extensions.Logging.ILogger<HostLinkService>>()));
    builder.Services.AddHostedService(services => services.GetRequiredService<HostLinkService>());
    if (verifyQueue)
    {
        builder.Services.AddHostedService(services => new QueueTriggerVerificationService(
            queues.Resource, blobs.Resource, inputQueue, outputQueue, topology!, hostEndpoint,
            Path.Combine(repositoryRoot, "out", "extension-payload", "evidence", inputQueue),
            services.GetRequiredService<HostLinkService>(),
            services.GetRequiredService<IHostApplicationLifetime>(),
            services.GetRequiredService<Microsoft.Extensions.Logging.ILogger<QueueTriggerVerificationService>>()));
    }
}

await builder.Build().RunAsync();

static int? GetOptionalPort(IConfiguration configuration, string key)
{
    string? value = configuration[key];
    if (value is null)
    {
        return null;
    }

    if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int port) || port is < 1 or > 65535)
    {
        throw new InvalidOperationException($"The {key} configuration value must be a valid TCP port. Value: '{value}'.");
    }

    return port;
}
