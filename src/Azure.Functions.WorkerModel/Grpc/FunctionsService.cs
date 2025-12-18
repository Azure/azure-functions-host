using Grpc.Core;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using OutOfProcModel.FunctionsHost.Grpc;

namespace Microsoft.Azure.Functions.WorkerModel.Grpc;

internal sealed class FunctionsService : FunctionRpc.FunctionRpcBase
{
    private readonly GrpcWorkerStreamFactory _streamFactory;

    // TODO -- should this be here?
    private readonly IList<GrpcWorkerStream> _activeStreams = [];

    public FunctionsService(GrpcWorkerStreamFactory streamFactory)
    {
        _streamFactory = streamFactory;
    }

    public override async Task EventStream(IAsyncStreamReader<StreamingMessage> requestStream, IServerStreamWriter<StreamingMessage> responseStream, ServerCallContext context)
    {
        var cancelSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cts = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
        CancellationTokenRegistration ctr = cts.Token.Register(static state => ((TaskCompletionSource<bool>)state).TrySetResult(false), cancelSource);

        var stream = _streamFactory.CreateStream();
        _activeStreams.Add(stream);

        await foreach (var msg in stream.StartAsync(requestStream.ReadAllAsync()))
        {
            await responseStream.WriteAsync(msg);
        }

        await stream.StopAsync();
    }
}
