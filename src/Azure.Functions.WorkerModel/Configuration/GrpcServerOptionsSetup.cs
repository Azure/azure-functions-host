using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.Functions.WorkerModel.Configuration;

internal class GrpcServerOptionsSetup : IConfigureOptions<GrpcServerOptions>
{
    public void Configure(GrpcServerOptions options)
    {
        var grpcPortStr = Environment.GetEnvironmentVariable("FUNCTIONS_GRPC_PORT");
        int port = !string.IsNullOrEmpty(grpcPortStr) && int.TryParse(grpcPortStr, out var fixedPort)
            ? fixedPort
            : WorkerUtilities.GetUnusedTcpPort();
        options.ServerUri = new Uri($"http://{WorkerConstants.HostName}:{port}");
    }
}