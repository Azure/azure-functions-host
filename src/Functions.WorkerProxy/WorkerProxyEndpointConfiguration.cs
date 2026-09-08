// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Net;
using Azure.Functions.WorkerProxy.Rpc;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Options;

namespace Azure.Functions.WorkerProxy;

/// <summary>
/// Configures the WorkerProxy Kestrel listeners and resolves requests to their listener roles.
/// </summary>
internal sealed class WorkerProxyEndpointConfiguration(IOptions<WorkerProxyOptions> workerProxyOptions) : IConfigureOptions<KestrelServerOptions>
{
    private ListenOptions? _management;
    private ListenOptions? _runtime;
    private ListenOptions? _worker;
    private ListenOptions? _http;

    /// <inheritdoc />
    public void Configure(KestrelServerOptions options)
    {
        WorkerProxyOptions configuredOptions = workerProxyOptions.Value;

        if (_management is not null || _runtime is not null || _worker is not null || _http is not null)
        {
            throw new InvalidOperationException("WorkerProxy Kestrel endpoints have already been configured.");
        }

        options.ListenAnyIP(configuredOptions.ManagementPort, listener =>
        {
            listener.Protocols = HttpProtocols.Http1;
            _management = listener;
        });
        options.ListenAnyIP(configuredOptions.RuntimeGrpcPort, listener =>
        {
            listener.Protocols = HttpProtocols.Http2;
            _runtime = listener;
        });
        options.Listen(IPAddress.Loopback, configuredOptions.WorkerGrpcPort, listener =>
        {
            listener.Protocols = HttpProtocols.Http2;
            _worker = listener;
        });
        options.ListenAnyIP(configuredOptions.HttpPort, listener =>
        {
            listener.Protocols = HttpProtocols.Http1;
            _http = listener;
        });
    }

    /// <summary>
    /// Determines whether a request arrived on the management listener.
    /// </summary>
    /// <param name="localPort">The local port that accepted the request.</param>
    /// <returns><see langword="true"/> for the management listener; otherwise, <see langword="false"/>.</returns>
    public bool IsManagementPort(int localPort)
    {
        return GetPort(_management) == localPort;
    }

    /// <summary>
    /// Determines whether a request arrived on the HTTP forwarding listener.
    /// </summary>
    /// <param name="localPort">The local port that accepted the request.</param>
    /// <returns><see langword="true"/> for the HTTP forwarding listener; otherwise, <see langword="false"/>.</returns>
    public bool IsHttpPort(int localPort)
    {
        return GetPort(_http) == localPort;
    }

    /// <summary>
    /// Resolves a local listener port to its FunctionRpc relay side.
    /// </summary>
    /// <param name="localPort">The local port that accepted the request.</param>
    /// <param name="side">The resolved relay side.</param>
    /// <returns><see langword="true"/> for a FunctionRpc listener; otherwise, <see langword="false"/>.</returns>
    public bool TryGetRelaySide(int localPort, out FunctionRpcRelaySide side)
    {
        if (GetPort(_runtime) == localPort)
        {
            side = FunctionRpcRelaySide.Runtime;
            return true;
        }

        if (GetPort(_worker) == localPort)
        {
            side = FunctionRpcRelaySide.Worker;
            return true;
        }

        side = default;
        return false;
    }

    internal Uri GetManagementAddress()
    {
        return GetAddress(_management, "management");
    }

    internal Uri GetHttpAddress()
    {
        return GetAddress(_http, "HTTP forwarding");
    }

    internal Uri GetRelayAddress(FunctionRpcRelaySide side)
    {
        return side switch
        {
            FunctionRpcRelaySide.Runtime => GetAddress(_runtime, "runtime"),
            FunctionRpcRelaySide.Worker => GetAddress(_worker, "worker"),
            _ => throw new ArgumentOutOfRangeException(nameof(side), side, "Unknown relay side.")
        };
    }

    private static Uri GetAddress(ListenOptions? listener, string listenerName)
    {
        IPEndPoint? endpoint = listener?.IPEndPoint;
        if (endpoint is null || endpoint.Port == 0)
        {
            throw new InvalidOperationException($"The WorkerProxy {listenerName} listener has not started.");
        }

        return new UriBuilder(Uri.UriSchemeHttp, endpoint.Address.ToString(), endpoint.Port).Uri;
    }

    private static int GetPort(ListenOptions? listener)
    {
        return listener?.IPEndPoint?.Port ?? -1;
    }
}
