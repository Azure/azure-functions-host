// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Azure.Functions.WorkerModel.Configuration;
using Microsoft.Azure.Functions.WorkerModel.Grpc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Workers.Rpc;

internal class RpcInitializationService : IManagedHostedService
{
    private readonly ILogger _logger;
    private readonly GrpcServerOptions _grpcOptions;
    private readonly GrpcWorkerStreamFactory _streamFactory;
    // private readonly int _rpcServerShutdownTimeoutInMilliseconds;
    // private HashSet<string> _placeholderLanguageWorkersList = new HashSet<string>();

    public RpcInitializationService(GrpcWorkerStreamFactory streamFactory, IOptions<GrpcServerOptions> grpcOptions, ILogger<RpcInitializationService> logger)
    {
        _logger = logger;
        // _rpcServerShutdownTimeoutInMilliseconds = 5000;
        _grpcOptions = grpcOptions.Value;
        _streamFactory = streamFactory;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        //if (Utility.CheckAppOffline(_applicationHostOptions.CurrentValue.ScriptPath))
        //{
        //    _logger.LogDebug("App is offline. RpcInitializationService will not be started");
        //    return;
        //}

        // TODO: https://github.com/Azure/azure-functions-host/issues/4891
        try
        {
            _logger.LogDebug("Starting Rpc Initialization Service.");
            await InitializeRpcServerAsync();
            _logger.LogDebug("Rpc Initialization Service started.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting Rpc Initialization Service. Handling error and continuing.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task OuterStopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;

        //_logger.LogDebug("Shutting down Rpc Channels Manager");
        //await _webHostRpcWorkerChannelManager.ShutdownChannelsAsync();

        //_logger.LogDebug("Shutting down RPC server");

        //try
        //{
        //    Task shutDownRpcServer = _rpcServer.ShutdownAsync();
        //    Task shutdownResult = await Task.WhenAny(shutDownRpcServer, Task.Delay(_rpcServerShutdownTimeoutInMilliseconds));

        //    if (!shutdownResult.Equals(shutDownRpcServer) || shutDownRpcServer.IsFaulted)
        //    {
        //        _logger.LogDebug("Killing RPC server");
        //        await _rpcServer.KillAsync();
        //    }
        //}
        //catch (AggregateException ae)
        //{
        //    ae.Handle(e =>
        //    {
        //        _logger.LogError(e, "Shutting down RPC server encountered exception: '{message}'", e.Message);
        //        return true;
        //    });
        //}
    }

    internal async Task InitializeRpcServerAsync()
    {
        try
        {
            _logger.LogDebug("Initializing RpcServer");

            var grpcHost = CreateFunctionsGrpcHost();
            await grpcHost.StartAsync();

            _logger.LogDebug("RpcServer initialized");
        }
        catch (Exception grpcInitEx)
        {
            throw new HostInitializationException($"Failed to start Rpc Server. Check if your app is hitting connection limits.", grpcInitEx);
        }
    }

    internal IHost CreateFunctionsGrpcHost()
    {
        var builder = WebApplication.CreateSlimBuilder();

        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Limits.MaxRequestBodySize = null;
            options.ListenLocalhost(_grpcOptions.ServerUri.Port, listenOptions =>
            {
                listenOptions.Protocols = HttpProtocols.Http2;
            });
        });

        builder.Services.AddGrpc(options =>
        {
            options.MaxReceiveMessageSize = int.MaxValue;
            options.MaxSendMessageSize = int.MaxValue;
        });

        builder.Services.AddSingleton(_streamFactory);

        var app = builder.Build();
        app.MapGrpcService<FunctionsService>();

        return app;
    }
}
