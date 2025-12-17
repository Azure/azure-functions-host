using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.Functions.WorkerModel.Configuration;

internal class GrpcServerOptionsSetup : IConfigureOptions<GrpcServerOptions>
{
    public void Configure(GrpcServerOptions options)
    {
        int port = WorkerUtilities.GetUnusedTcpPort();
        options.ServerUri = new Uri($"http://{WorkerConstants.HostName}:{port}");
    }
}