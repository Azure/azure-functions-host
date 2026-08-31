// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.WorkerProxy;

/// <summary>
/// Hosts the runtime-facing and worker-facing FunctionRpc endpoints for the relay.
/// </summary>
/// <remarks>
/// Each side runs in a dedicated HTTP/2-only child application with immutable side ownership.
/// Keeping these listeners separate preserves the management application's standard ASP.NET Core
/// endpoint configuration and prevents request metadata from selecting a relay role.
/// A single asynchronous gate serializes start, stop, and disposal so lifecycle transitions cannot overlap.
/// </remarks>
internal sealed class FunctionRpcRelayServer : IHostedService, IAsyncDisposable
{
    private const int MaxMessageLengthBytes = int.MaxValue;

    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly FunctionRpcRelay _relay;
    private readonly FunctionRpcRelayOptions _options;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<FunctionRpcRelayServer> _logger;
    private WebApplication? _runtimeApplication;
    private WebApplication? _workerApplication;
    private LifecycleState _state;

    /// <summary>
    /// Initializes a new instance of the <see cref="FunctionRpcRelayServer"/> class.
    /// </summary>
    /// <param name="relay">The singleton relay shared by both endpoint applications.</param>
    /// <param name="options">The listener configuration.</param>
    /// <param name="loggerFactory">The parent logging pipeline shared by both endpoint applications.</param>
    /// <param name="logger">The listener lifecycle logger.</param>
    public FunctionRpcRelayServer(FunctionRpcRelay relay, FunctionRpcRelayOptions options, ILoggerFactory loggerFactory,
        ILogger<FunctionRpcRelayServer> logger)
    {
        _relay = relay;
        _options = options;
        _loggerFactory = loggerFactory;
        _logger = logger;
    }

    private enum LifecycleState
    {
        Created,
        Started,
        CleanupRequired,
        Stopped,
        Disposed
    }

    /// <summary>
    /// Gets the bound address for the runtime-facing listener.
    /// </summary>
    /// <exception cref="InvalidOperationException">The listener has not started.</exception>
    internal Uri RuntimeAddress => GetAddress(_runtimeApplication, FunctionRpcRelaySide.Runtime);

    /// <summary>
    /// Gets the bound address for the worker-facing listener.
    /// </summary>
    /// <exception cref="InvalidOperationException">The listener has not started.</exception>
    internal Uri WorkerAddress => GetAddress(_workerApplication, FunctionRpcRelaySide.Worker);

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            switch (_state)
            {
                case LifecycleState.Created:
                    break;
                case LifecycleState.Started:
                    return;
                case LifecycleState.CleanupRequired:
                case LifecycleState.Stopped:
                case LifecycleState.Disposed:
                    throw new InvalidOperationException("The FunctionRpc relay listeners have already been stopped.");
                default:
                    throw new InvalidOperationException($"Unknown FunctionRpc relay lifecycle state '{_state}'.");
            }

            try
            {
                await StartCoreAsync(cancellationToken);
                _state = LifecycleState.Started;
            }
            catch
            {
                _state = LifecycleState.CleanupRequired;
                throw;
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// If cancellation interrupts cleanup, <see cref="DisposeAsync"/> retries it without a cancellation token.
    /// </remarks>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("FunctionRpc relay listener stop requested.");
        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            switch (_state)
            {
                case LifecycleState.Created:
                case LifecycleState.Started:
                case LifecycleState.CleanupRequired:
                    break;
                case LifecycleState.Stopped:
                case LifecycleState.Disposed:
                    return;
                default:
                    throw new InvalidOperationException($"Unknown FunctionRpc relay lifecycle state '{_state}'.");
            }

            try
            {
                await StopCoreAsync(cancellationToken);
                _state = LifecycleState.Stopped;
            }
            catch
            {
                _state = LifecycleState.CleanupRequired;
                throw;
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _lifecycleGate.WaitAsync();
        try
        {
            if (_state == LifecycleState.Disposed)
            {
                return;
            }

            try
            {
                switch (_state)
                {
                    case LifecycleState.Created:
                    case LifecycleState.Started:
                    case LifecycleState.CleanupRequired:
                        await StopCoreAsync(CancellationToken.None);
                        break;
                    case LifecycleState.Stopped:
                        break;
                    default:
                        throw new InvalidOperationException($"Unknown FunctionRpc relay lifecycle state '{_state}'.");
                }
            }
            finally
            {
                try
                {
                    await DisposeApplicationsAsync(_runtimeApplication, _workerApplication);
                }
                finally
                {
                    _runtimeApplication = null;
                    _workerApplication = null;
                    _state = LifecycleState.Disposed;
                }
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private async Task StartCoreAsync(CancellationToken cancellationToken)
    {
        WebApplication? runtimeApplication = null;
        WebApplication? workerApplication = null;

        try
        {
            runtimeApplication = BuildEndpointApplication(_options.RuntimeGrpcPort, FunctionRpcRelaySide.Runtime);
            _runtimeApplication = runtimeApplication;
            workerApplication = BuildEndpointApplication(_options.WorkerGrpcPort, FunctionRpcRelaySide.Worker);
            _workerApplication = workerApplication;

            Task runtimeStartTask = runtimeApplication.StartAsync(cancellationToken);
            Task workerStartTask = workerApplication.StartAsync(cancellationToken);
            await Task.WhenAll(runtimeStartTask, workerStartTask);
        }
        catch
        {
            try
            {
                await StopApplicationsAsync(runtimeApplication, workerApplication, CancellationToken.None);
            }
            finally
            {
                try
                {
                    await DisposeApplicationsAsync(runtimeApplication, workerApplication);
                }
                finally
                {
                    _runtimeApplication = null;
                    _workerApplication = null;
                }
            }

            throw;
        }

        _logger.LogInformation("FunctionRpc relay listening for runtime streams on {RuntimeAddress} " +
            "and worker streams on {WorkerAddress}.", RuntimeAddress, WorkerAddress);
    }

    private async Task StopCoreAsync(CancellationToken cancellationToken)
    {
        Task relayStopTask = _relay.StopAsync(cancellationToken);
        Task applicationStopTask = StopApplicationsAsync(_runtimeApplication, _workerApplication, cancellationToken);
        await Task.WhenAll(relayStopTask, applicationStopTask);
    }

    private WebApplication BuildEndpointApplication(int port, FunctionRpcRelaySide side)
    {
        WebApplicationBuilder builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions());
        builder.WebHost.UseKestrelCore();
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Limits.MaxRequestBodySize = null;
            Action<ListenOptions> configureListener = static listenOptions => listenOptions.Protocols = HttpProtocols.Http2;
            switch (side)
            {
                case FunctionRpcRelaySide.Runtime:
                    options.ListenAnyIP(port, configureListener);
                    break;
                case FunctionRpcRelaySide.Worker:
                    options.Listen(IPAddress.Loopback, port, configureListener);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(side), side, "Unknown relay side.");
            }
        });
        builder.Services.AddRoutingCore();
        builder.Services.AddGrpc(options =>
        {
            options.MaxReceiveMessageSize = MaxMessageLengthBytes;
            options.MaxSendMessageSize = MaxMessageLengthBytes;
        });
        builder.Services.AddSingleton(_loggerFactory);
        builder.Services.AddSingleton(_relay);
        builder.Services.AddSingleton(new FunctionRpcRelayEndpoint(side));
        builder.Services.AddSingleton<FunctionRpcRelayService>();

        WebApplication application = builder.Build();
        application.UseRouting();
        application.MapGrpcService<FunctionRpcRelayService>();

        return application;
    }

    private static async Task StopApplicationsAsync(WebApplication? runtimeApplication,
        WebApplication? workerApplication, CancellationToken cancellationToken)
    {
        Task runtimeStopTask = runtimeApplication?.StopAsync(cancellationToken) ?? Task.CompletedTask;
        Task workerStopTask = workerApplication?.StopAsync(cancellationToken) ?? Task.CompletedTask;
        await Task.WhenAll(runtimeStopTask, workerStopTask);
    }

    private static async Task DisposeApplicationsAsync(WebApplication? runtimeApplication, WebApplication? workerApplication)
    {
        ValueTask runtimeDisposeTask = runtimeApplication?.DisposeAsync() ?? ValueTask.CompletedTask;
        ValueTask workerDisposeTask = workerApplication?.DisposeAsync() ?? ValueTask.CompletedTask;
        await Task.WhenAll(runtimeDisposeTask.AsTask(), workerDisposeTask.AsTask());
    }

    private static Uri GetAddress(WebApplication? application, FunctionRpcRelaySide side)
    {
        if (application is null)
        {
            throw new InvalidOperationException($"The {side} FunctionRpc listener has not been created.");
        }

        IServer server = application.Services.GetRequiredService<IServer>();
        IServerAddressesFeature addresses = server.Features.Get<IServerAddressesFeature>()
            ?? throw new InvalidOperationException($"The {side} FunctionRpc listener did not publish its addresses.");
        string address = addresses.Addresses.SingleOrDefault()
            ?? throw new InvalidOperationException($"The {side} FunctionRpc listener has not started.");

        return new Uri(address);
    }
}
