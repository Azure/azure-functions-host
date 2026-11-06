// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Azure.Functions.Authentication;
using Azure.Functions.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Forwarder;
using Yarp.ReverseProxy.Transforms;

namespace Azure.Functions.Host.Tests;

/// <summary>
/// Adapts the plaintext Host transport to WorkerProxy's authenticated TLS listener.
/// </summary>
internal static class WorkerProxyTestTransport
{
    internal static async Task<WebApplication> StartAsync(
        Uri endpoint, TestCertificateFiles certificates, string workerId, CancellationToken cancellationToken = default)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options =>
            options.Listen(IPAddress.Loopback, 0, listener => listener.Protocols = HttpProtocols.Http2));
        builder.Services.AddReverseProxy().LoadFromMemory(
            [
                new RouteConfig
                {
                    RouteId = "runtime",
                    ClusterId = "worker",
                    Match = new RouteMatch { Path = "/{**path}" }
                }
            ],
            [
                new ClusterConfig
                {
                    ClusterId = "worker",
                    Destinations = new Dictionary<string, DestinationConfig>
                    {
                        ["worker"] = new() { Address = endpoint.AbsoluteUri }
                    },
                    HttpRequest = new ForwarderRequestConfig
                    {
                        Version = HttpVersion.Version20,
                        VersionPolicy = HttpVersionPolicy.RequestVersionExact
                    }
                }
            ])
            .ConfigureHttpClient((_, handler) =>
                handler.SslOptions.RemoteCertificateValidationCallback = (_, certificate, _, _) =>
                    certificate is not null && certificate.GetRawCertData().AsSpan().SequenceEqual(certificates.Certificate.RawData))
            .AddTransforms(context => context.AddRequestTransform(request =>
            {
                request.ProxyRequest.Headers.Remove(RuntimeWorkerAuthentication.WorkerTokenHeader);
                request.ProxyRequest.Headers.Add(RuntimeWorkerAuthentication.WorkerTokenHeader,
                    RuntimeWorkerAuthentication.CreateToken(
                        Convert.FromBase64String(ManagementTokenTestData.WorkerKey),
                        RuntimeWorkerAuthentication.WorkerAudience(workerId),
                        RuntimeWorkerAuthentication.WorkerPurpose, DateTimeOffset.UtcNow));
                return ValueTask.CompletedTask;
            }));
        WebApplication app = builder.Build();
        app.MapReverseProxy();
        try
        {
            await app.StartAsync(cancellationToken);
            return app;
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }
    }
}
