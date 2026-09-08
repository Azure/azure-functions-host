// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.AppCapabilities;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Azure.WebJobs.Script.Http;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.SharedMemoryDataTransfer;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Azure.Functions.Rpc.Client.Tests;

internal sealed class ClientWorkerChannelTestHarness : IAsyncDisposable
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    private ClientWorkerChannelTestHarness(RpcClientWorkerChannel channel, TestDuplexChannel<StreamingMessage> transport)
    {
        Channel = channel;
        Transport = transport;
    }

    internal RpcClientWorkerChannel Channel { get; }

    internal TestDuplexChannel<StreamingMessage> Transport { get; }

    internal static async Task<ClientWorkerChannelTestHarness> CreateAsync(string workerId)
    {
        TestDuplexChannel<StreamingMessage> transport = new();
        RpcClientWorkerChannel channel = CreateFactory().Create(workerId, transport);
        Task start = channel.StartAsync(CancellationToken.None);

        await transport.SendResponseAsync(new()
        {
            StartStream = new() { WorkerId = workerId },
        });

        StreamingMessage initRequest = await transport.Requests.ReadAsync().AsTask().WaitAsync(TestTimeout);
        if (initRequest.ContentCase is not StreamingMessage.ContentOneofCase.WorkerInitRequest)
        {
            throw new InvalidOperationException($"Expected a worker init request, but received {initRequest.ContentCase}.");
        }

        await transport.SendResponseAsync(new()
        {
            WorkerInitResponse = new()
            {
                Result = new() { Status = StatusResult.Types.Status.Success },
            },
        });
        await start.WaitAsync(TestTimeout);

        return new(channel, transport);
    }

    internal async Task<StreamingMessage> ReadRequestAsync(StreamingMessage.ContentOneofCase contentCase)
    {
        StreamingMessage request = await Transport.Requests.ReadAsync().AsTask().WaitAsync(TestTimeout);
        if (request.ContentCase != contentCase)
        {
            throw new InvalidOperationException($"Expected {contentCase}, but received {request.ContentCase}.");
        }

        return request;
    }

    internal ValueTask SendFunctionLoadResponseAsync(string functionId, bool succeeded = true)
        => Transport.SendResponseAsync(new()
        {
            FunctionLoadResponse = new()
            {
                FunctionId = functionId,
                Result = new()
                {
                    Status = succeeded ? StatusResult.Types.Status.Success : StatusResult.Types.Status.Failure,
                    Exception = succeeded ? null : new() { Message = "function load failed" },
                },
            },
        });

    internal ValueTask SendInvocationResponseAsync(string invocationId, bool succeeded = true)
        => Transport.SendResponseAsync(new()
        {
            InvocationResponse = new()
            {
                InvocationId = invocationId,
                Result = new()
                {
                    Status = succeeded ? StatusResult.Types.Status.Success : StatusResult.Types.Status.Failure,
                    Exception = succeeded ? null : new() { Message = "invocation failed" },
                },
            },
        });

    internal ValueTask SendFunctionMetadataResponseAsync(
        IEnumerable<RpcFunctionMetadata> functions = null,
        bool useDefaultMetadataIndexing = false)
    {
        FunctionMetadataResponse response = new()
        {
            Result = new() { Status = StatusResult.Types.Status.Success },
            UseDefaultMetadataIndexing = useDefaultMetadataIndexing,
        };
        response.FunctionMetadataResults.Add(functions ?? Enumerable.Empty<RpcFunctionMetadata>());

        return Transport.SendResponseAsync(new() { FunctionMetadataResponse = response });
    }

    public ValueTask DisposeAsync() => Channel.DisposeAsync();

    private static RpcClientWorkerChannelFactory CreateFactory()
    {
        Mock<IScriptHostManager> hostManager = new();
        hostManager.As<IServiceProvider>()
            .Setup(provider => provider.GetService(typeof(IOptions<ScriptJobHostOptions>)))
            .Returns(Options.Create(new ScriptJobHostOptions { RootScriptPath = "c:\\test" }));
        Mock<IOptionsMonitor<ScriptApplicationHostOptions>> applicationHostOptions = new();
        applicationHostOptions.SetupGet(options => options.CurrentValue)
            .Returns(new ScriptApplicationHostOptions { ScriptPath = "c:\\test" });
        Mock<IAppCapabilitiesStore> appCapabilitiesStore = new();
        appCapabilitiesStore.Setup(store => store.TrySetAll(It.IsAny<IEnumerable<KeyValuePair<string, string>>>()))
            .Returns(true);

        return new(
            new ScriptEventManager(),
            hostManager.Object,
            Mock.Of<IEnvironment>(),
            NullLoggerFactory.Instance,
            applicationHostOptions.Object,
            Mock.Of<ISharedMemoryManager>(),
            Options.Create(new WorkerConcurrencyOptions()),
            Options.Create(new FunctionsHostingConfigOptions()),
            appCapabilitiesStore.Object,
            Mock.Of<IHttpProxyService>(),
            Mock.Of<IMetricsLogger>());
    }
}
