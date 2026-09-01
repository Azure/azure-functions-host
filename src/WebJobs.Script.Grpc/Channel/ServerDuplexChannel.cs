// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;

namespace Microsoft.Azure.WebJobs.Script.Grpc;

internal sealed class ServerDuplexChannel : DuplexChannel<StreamingMessage>
{
    // Flow here is:
    // 1) WorkerChannel writes requests to HostToWorker; concurrent writes are allowed.
    // 2) FunctionRpcService reads HostToWorker and writes to the gRPC response stream; multiple streams are allowed.
    // 3) FunctionRpcService writes worker responses to WorkerToHost; multiple streams are allowed.
    // 4) WorkerChannel has one dedicated WorkerToHost consumer that matches responses to in-flight operations.
    internal static readonly UnboundedChannelOptions WorkerToHostOptions = new()
    {
        SingleReader = true,
        SingleWriter = false,
        AllowSynchronousContinuations = false,
    };

    internal static readonly UnboundedChannelOptions HostToWorkerOptions = new()
    {
        SingleReader = false,
        SingleWriter = false,
        AllowSynchronousContinuations = false,
    };

    private readonly Channel<StreamingMessage> _hostToWorker;
    private readonly Channel<StreamingMessage> _workerToHost;

    internal ServerDuplexChannel()
        : this(
            Channel.CreateUnbounded<StreamingMessage>(WorkerToHostOptions),
            Channel.CreateUnbounded<StreamingMessage>(HostToWorkerOptions))
    {
    }

    internal ServerDuplexChannel(Channel<StreamingMessage> workerToHost, Channel<StreamingMessage> hostToWorker)
    {
        _workerToHost = workerToHost;
        _hostToWorker = hostToWorker;

        Reader = _workerToHost.Reader;
        Writer = _hostToWorker.Writer;
    }

    /// <summary>
    /// Gets the borrowed endpoints used by <c>FunctionRpcService</c> to bridge this channel to gRPC.
    /// </summary>
    internal FunctionRpcChannelEndpoints ServiceEndpoints =>
        new(_hostToWorker.Reader, _workerToHost.Writer);

    protected override ValueTask DisposeAsyncCore()
    {
        _hostToWorker.Writer.TryComplete();
        _workerToHost.Writer.TryComplete();

        return default;
    }
}
