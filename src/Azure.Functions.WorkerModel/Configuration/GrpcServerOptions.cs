namespace Microsoft.Azure.Functions.WorkerModel.Configuration;

public sealed class GrpcServerOptions
{
    public Uri ServerUri { get; set; } = default!;
}
