// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Azure.Functions.WorkerProxy.Rpc;
using Azure.Functions.WorkerProxy.State;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Azure.Functions.WorkerProxy.Http;

/// <summary>
/// Captures the worker's HTTP destination before advertising WorkerProxy to the runtime.
/// </summary>
internal sealed partial class WorkerHttpCapabilityProvider(
    IOptions<WorkerProxyOptions> options,
    ILogger<WorkerHttpCapabilityProvider> logger,
    WorkerPodStateManager stateManager)
    : IWorkerCapabilityFinalizer
{
    private const string HttpUriCapability = "HttpUri";

    private readonly WorkerProxyOptions _options = (options ?? throw new ArgumentNullException(nameof(options))).Value;
    private readonly ILogger<WorkerHttpCapabilityProvider> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly WorkerPodStateManager _stateManager = stateManager ?? throw new ArgumentNullException(nameof(stateManager));

    /// <summary>
    /// Finalizes the HTTP capability on a relay-owned copy of the successful initialization response.
    /// </summary>
    /// <param name="capabilities">The capabilities to advertise to the runtime.</param>
    /// <returns>The real worker destination, or <see langword="null"/> when HTTP proxying is unavailable.</returns>
    public Uri? FinalizeCapabilities(IDictionary<string, string> capabilities)
    {
        if (!capabilities.TryGetValue(HttpUriCapability, out string? advertisedEndpoint) || string.IsNullOrWhiteSpace(advertisedEndpoint))
        {
            capabilities.Remove(HttpUriCapability);
            return null;
        }

        // Dropping a nonempty HTTP capability would silently switch the runtime to gRPC HTTP payloads.
        // Fail initialization instead when the worker's HTTP transport cannot be configured.
        Uri advertisedDestination = ResolveRequiredEndpoint(advertisedEndpoint, $"Worker {HttpUriCapability} capability");
        Uri destination = string.IsNullOrWhiteSpace(_options.WorkerHttpEndpoint)
            ? advertisedDestination
            : ResolveRequiredEndpoint(_options.WorkerHttpEndpoint, $"{WorkerProxyOptions.SectionName}:{nameof(WorkerProxyOptions.WorkerHttpEndpoint)}");
        // Allocation is authoritative. An explicit configured origin remains supported for local/legacy
        // deployments without dynamic assignment; never infer an external port from another listener.
        Uri? proxyEndpoint = WorkerProxyOptionsValidator.ResolveHttpProxyOrigin(
            _stateManager.HttpProxyEndpoint?.AbsoluteUri ?? _options.HttpProxyEndpoint);
        if (proxyEndpoint is null)
        {
            throw new InvalidOperationException($"{nameof(WorkerProxyOptions.HttpProxyEndpoint)} must specify a valid allocated "
                + "or explicitly configured HTTP origin before HTTP worker initialization.");
        }

        if (Uri.Compare(destination, proxyEndpoint, UriComponents.SchemeAndServer, UriFormat.UriEscaped,
            StringComparison.OrdinalIgnoreCase) == 0)
        {
            throw new InvalidOperationException("The worker HTTP destination must not point back to the proxy HTTP origin.");
        }

        capabilities[HttpUriCapability] = proxyEndpoint.AbsoluteUri;
        Log.HttpCapabilityFinalized(_logger,
            destination.GetComponents(UriComponents.SchemeAndServer, UriFormat.UriEscaped),
            proxyEndpoint.GetComponents(UriComponents.SchemeAndServer, UriFormat.UriEscaped));
        return destination;
    }

    private Uri ResolveRequiredEndpoint(string? value, string name)
    {
        Uri? endpoint = WorkerHttpDestinationResolver.Resolve(overrideEndpoint: null, value);
        if (endpoint is null)
        {
            Log.InvalidHttpEndpoint(_logger, name);
            throw new InvalidOperationException($"{name} must specify an absolute HTTP or HTTPS endpoint with a nonzero port "
                + "and no credentials, query, or fragment.");
        }

        return endpoint;
    }

    private static partial class Log
    {
        [LoggerMessage(0, LogLevel.Error,
            "{Name} must specify an absolute HTTP or HTTPS endpoint with a nonzero port and no credentials, query, or fragment.")]
        public static partial void InvalidHttpEndpoint(ILogger logger, string name);

        [LoggerMessage(1, LogLevel.Debug, "Worker HTTP origin {WorkerOrigin} is advertised through proxy origin {ProxyOrigin}.")]
        public static partial void HttpCapabilityFinalized(ILogger logger, string workerOrigin, string proxyOrigin);
    }
}
