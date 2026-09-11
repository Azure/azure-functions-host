// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Globalization;
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

string workerId = builder.Configuration["ComputeSeparation:WorkerId"] ?? $"aspire-{Guid.NewGuid():N}";
using HarnessRunDirectory? runDirectory = useContainers ? null : new();
string repositoryRoot = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "..", ".."));
await using ContainerTopology? topology = useContainers ? new(repositoryRoot) : null;
EndpointReference hostEndpoint;
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

    builder.AddExecutable("worker-pod-1", "docker", repositoryRoot,
            "compose", "--file", topology.WorkerPod.ComposeFile, "up", "--build", "--no-color")
        .WithHttpEndpoint(targetPort: GetOptionalPort(builder.Configuration, "ComputeSeparation:ManagementPort"),
            name: ManagementEndpointName, env: "WORKER_PROXY_MANAGEMENT_PORT")
        .WithHttpHealthCheck("/admin/instance/ready", endpointName: ManagementEndpointName)
        .WithEnvironment("COMPUTE_NETWORK_NAME", topology.NetworkName)
        .WithEnvironment("WORKER_PROXY_ALIAS", topology.ProxyAlias)
        .WithEnvironment("WORKER_ID", workerId)
        .WithEnvironment("WORKER_REQUEST_ID", Guid.NewGuid().ToString())
        .WithEnvironment(ComposeSession.ProjectNameVariable, topology.WorkerPod.ProjectName)
        .WithEnvironment(context => context.EnvironmentVariables[ComposeSession.GenerationVariable] = topology.WorkerPod.Generation)
        .WaitFor(runtime)
        .OnBeforeResourceStarted((_, _, cancellationToken) => topology.WorkerPod.BeforeStartAsync(cancellationToken))
        .OnResourceStopped((_, stopped, _) => topology.WorkerPod.StopRunAsync(
            stopped.ResourceEvent.Snapshot.EnvironmentVariables.FirstOrDefault(variable =>
                string.Equals(variable.Name, ComposeSession.GenerationVariable, StringComparison.Ordinal))?.Value));

    hostEndpoint = runtime.GetEndpoint(HttpEndpointName);
    workerGrpcEndpoint = ReferenceExpression.Create($"http://{topology.ProxyAlias}:50053");
    dependencies = ["runtime", "worker-pod-1"];
}
else
{
    IResourceBuilder<ProjectResource> proxyProject = builder.AddProject<Projects.Functions_WorkerProxy>("worker-proxy", launchProfileName: null)
        .WithHttpEndpoint(targetPort: GetOptionalPort(builder.Configuration, "ComputeSeparation:ManagementPort"), name: ManagementEndpointName)
        .WithHttpEndpoint(targetPort: GetOptionalPort(builder.Configuration, "ComputeSeparation:RuntimeGrpcPort"), name: RuntimeGrpcEndpointName)
        .WithHttpEndpoint(targetPort: GetOptionalPort(builder.Configuration, "ComputeSeparation:WorkerGrpcPort"), name: WorkerGrpcEndpointName)
        .WithHttpEndpoint(targetPort: GetOptionalPort(builder.Configuration, "ComputeSeparation:HttpPort"), name: HttpEndpointName)
        .WithHttpHealthCheck("/admin/instance/ready", endpointName: ManagementEndpointName);

    proxyProject.WithEnvironment(context =>
    {
        context.EnvironmentVariables["WORKERPROXY__MANAGEMENTPORT"] =
            proxyProject.GetEndpoint(ManagementEndpointName).Property(EndpointProperty.TargetPort);
        context.EnvironmentVariables["WORKERPROXY__RUNTIMEGRPCPORT"] =
            proxyProject.GetEndpoint(RuntimeGrpcEndpointName).Property(EndpointProperty.TargetPort);
        context.EnvironmentVariables["WORKERPROXY__WORKERGRPCPORT"] =
            proxyProject.GetEndpoint(WorkerGrpcEndpointName).Property(EndpointProperty.TargetPort);
        context.EnvironmentVariables["WORKERPROXY__HTTPPORT"] =
            proxyProject.GetEndpoint(HttpEndpointName).Property(EndpointProperty.TargetPort);
        context.EnvironmentVariables["WORKERPROXY__HTTPPROXYENDPOINT"] = proxyProject.GetEndpoint(HttpEndpointName);
        context.EnvironmentVariables["Logging__LogLevel__Azure.Functions.WorkerProxy.Http"] = "Debug";
    });

    builder.AddProject<Projects.SampleIsolatedApp>("isolated-worker", launchProfileName: null)
        .WithArgs(
            "--functions-uri", proxyProject.GetEndpoint(WorkerGrpcEndpointName),
            "--functions-worker-id", workerId,
            "--functions-request-id", Guid.NewGuid().ToString(),
            "--functions-grpc-max-message-length", "134217728")
        .WaitFor(proxyProject);

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
        .WithHttpHealthCheck("/admin/instance/http-health", endpointName: HttpEndpointName);

    functionsHost.WithEnvironment("ASPNETCORE_URLS",
        ReferenceExpression.Create($"http://127.0.0.1:{functionsHost.GetEndpoint(HttpEndpointName).Property(EndpointProperty.TargetPort)}"));

    hostEndpoint = functionsHost.GetEndpoint(HttpEndpointName);
    workerGrpcEndpoint = ReferenceExpression.Create($"{proxyProject.GetEndpoint(RuntimeGrpcEndpointName)}");
    dependencies = ["functions-host", "worker-proxy", "isolated-worker"];
}

if (builder.Configuration.GetValue("ComputeSeparation:AutoLink", true))
{
    builder.Services.AddHostedService(services => new HostLinkService(
        hostEndpoint,
        workerGrpcEndpoint,
        workerId,
        dependencies,
        services.GetRequiredService<ResourceNotificationService>(),
        services.GetRequiredService<Microsoft.Extensions.Logging.ILogger<HostLinkService>>()));
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
