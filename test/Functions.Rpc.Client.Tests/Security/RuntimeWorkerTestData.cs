// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Azure.Functions.Authentication;
using Azure.Functions.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Azure.WebJobs.Script.Grpc;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Azure.Functions.Rpc.Client.Tests;

internal static class RuntimeWorkerTestData
{
    public static readonly string WorkerKey = ManagementTokenTestData.WorkerKey;
    public static readonly string AppKey = Convert.ToHexString(SHA256.HashData("test-app-key"u8));

    public static IConfiguration CreateConfiguration() => new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string>
        {
            [RuntimeWorkerAuthentication.AppNameSetting] = "test-app",
            [RuntimeWorkerAuthentication.AppKeySetting] = AppKey
        }).Build();

    public static RuntimeWorkerCredentials CreateCredentials(string workerId = "worker")
        => new(workerId, WorkerKey, TimeProvider.System);

    public static IOptionsFactory<RuntimeApplicationAuthenticationOptions> CreateApplicationOptions(IConfiguration configuration = null)
    {
        configuration ??= CreateConfiguration();

        return new OptionsFactory<RuntimeApplicationAuthenticationOptions>(
            [new ConfigureNamedOptions<RuntimeApplicationAuthenticationOptions>(Options.DefaultName, options =>
            {
                options.AppName = configuration[RuntimeWorkerAuthentication.AppNameSetting];
                options.EncodedAppKey = configuration[RuntimeWorkerAuthentication.AppKeySetting];
            })],
            [],
            [new RuntimeApplicationAuthenticationOptionsValidator()]);
    }

    public static void ValidateAppProof(this RuntimeWorkerCredentials credentials, string proof)
    {
        RuntimeApplicationAuthenticationOptions options = CreateApplicationOptions().Create(Options.DefaultName);
        credentials.ValidateAppProof(proof, options.AppName, options.AppKey);
    }

    public static string CreateAppProof(string workerId, TimeProvider timeProvider = null)
        => RuntimeWorkerAuthentication.CreateAppProof(
            Convert.FromHexString(AppKey), "test-app", workerId, (timeProvider ?? TimeProvider.System).GetUtcNow());

    public static WorkerInitResponse Authenticate(WorkerInitResponse response, string workerId, TimeProvider timeProvider = null)
    {
        RuntimeWorkerAuthentication.SetAppProof(response.Capabilities, CreateAppProof(workerId, timeProvider));

        return response;
    }

    // Existing lifecycle tests use these authenticated fixture overloads, never an anonymous product path.
    public static Task<WorkerLinkResult> LinkAsync(
        this IWorkerChannelRegistry registry, string workerId, Uri endpoint, CancellationToken cancellationToken = default)
        => registry.LinkAsync(workerId, endpoint, WorkerKey, cancellationToken: cancellationToken);

    public static Task<DuplexChannel<T>> ConnectAsync<T>(
        this IDuplexChannelFactory<T> factory, Uri endpoint, CancellationToken cancellationToken = default)
        where T : class
        => factory.ConnectAsync(endpoint, CreateCredentials().CreateHeaders(), cancellationToken);

    public static void ConfigureTlsListener(WebApplicationBuilder builder, DateTimeOffset? expires = null)
    {
        builder.WebHost.UseKestrelHttpsConfiguration();
        builder.Services.AddSingleton(_ => new TestCertificateFiles(expires));
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0, listener =>
        {
            listener.Protocols = HttpProtocols.Http2;
            listener.UseHttps(options.ApplicationServices.GetRequiredService<TestCertificateFiles>().Certificate);
        }));
    }
}
