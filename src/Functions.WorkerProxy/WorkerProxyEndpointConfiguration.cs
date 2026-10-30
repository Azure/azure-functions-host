// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Net;
using Azure.Functions.WorkerProxy.Authentication;
using Azure.Functions.WorkerProxy.Identity;
using Azure.Functions.WorkerProxy.Rpc;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Options;

namespace Azure.Functions.WorkerProxy;

/// <summary>
/// Configures the WorkerProxy Kestrel listeners and resolves requests to their listener roles.
/// </summary>
internal sealed class WorkerProxyEndpointConfiguration(
    IOptions<WorkerProxyOptions> workerProxyOptions, WorkerProxyCertificate certificate,
    IOptions<TokenServiceProxyWorkerOptions> tokenServiceProxyOptions) : IConfigureOptions<KestrelServerOptions>
{
    private ListenOptions? _management;
    private ListenOptions? _runtime;
    private ListenOptions? _worker;
    private ListenOptions? _http;
    private ListenOptions? _tokenServiceProxy;

    /// <inheritdoc />
    public void Configure(KestrelServerOptions options)
    {
        WorkerProxyOptions configuredOptions = workerProxyOptions.Value;

        if (_management is not null || _runtime is not null || _worker is not null || _http is not null || _tokenServiceProxy is not null)
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
            listener.UseHttps(certificate.Certificate);
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
            listener.UseHttps(certificate.Certificate);
            _http = listener;
        });
        TokenServiceProxyWorkerOptions tokenServiceProxy = tokenServiceProxyOptions.Value;
        if (tokenServiceProxy.Enabled)
        {
            if (tokenServiceProxy.Port != 0 && (tokenServiceProxy.Port == configuredOptions.ManagementPort
                || tokenServiceProxy.Port == configuredOptions.RuntimeGrpcPort || tokenServiceProxy.Port == configuredOptions.WorkerGrpcPort
                || tokenServiceProxy.Port == configuredOptions.HttpPort))
            {
                throw new OptionsValidationException(TokenServiceProxyWorkerOptions.SectionName, typeof(TokenServiceProxyWorkerOptions),
                    ["The Token Service listener must use a distinct local port."]);
            }

            options.Listen(IPAddress.Loopback, tokenServiceProxy.Port, listener =>
            {
                listener.Protocols = HttpProtocols.Http1;
                _tokenServiceProxy = listener;
            });
        }
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

    internal bool IsTokenServiceProxyPort(int localPort) => GetPort(_tokenServiceProxy) == localPort;

    internal Uri GetTokenServiceProxyAddress() => GetAddress(_tokenServiceProxy, "Token Service proxy");

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
        return GetAddress(_http, "HTTP forwarding", useHttps: true);
    }

    internal Uri GetRelayAddress(FunctionRpcRelaySide side)
    {
        return side switch
        {
            FunctionRpcRelaySide.Runtime => GetAddress(_runtime, "runtime", useHttps: true),
            FunctionRpcRelaySide.Worker => GetAddress(_worker, "worker"),
            _ => throw new ArgumentOutOfRangeException(nameof(side), side, "Unknown relay side.")
        };
    }

    private static Uri GetAddress(ListenOptions? listener, string listenerName, bool useHttps = false)
    {
        IPEndPoint? endpoint = listener?.IPEndPoint;
        if (endpoint is null || endpoint.Port == 0)
        {
            throw new InvalidOperationException($"The WorkerProxy {listenerName} listener has not started.");
        }

        return new UriBuilder(useHttps ? Uri.UriSchemeHttps : Uri.UriSchemeHttp, endpoint.Address.ToString(), endpoint.Port).Uri;
    }

    private static int GetPort(ListenOptions? listener)
    {
        return listener?.IPEndPoint?.Port ?? -1;
    }
}
