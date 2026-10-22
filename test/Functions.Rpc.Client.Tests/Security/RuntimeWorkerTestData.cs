// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Azure.Functions.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Azure.WebJobs.Script.Grpc;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Azure.Functions.Rpc.Client.Tests;

internal static class RuntimeWorkerTestData
{
    public static readonly string WorkerKey = Convert.ToBase64String(SHA256.HashData("test-worker-key"u8));
    public static readonly string AppKey = Convert.ToHexString(SHA256.HashData("test-app-key"u8));

    public static IConfiguration CreateConfiguration() => new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string>
        {
            [RuntimeWorkerAuthentication.AppNameSetting] = "test-app",
            [RuntimeWorkerAuthentication.AppKeySetting] = AppKey
        }).Build();

    public static RuntimeWorkerCredentials CreateCredentials(string workerId = "worker")
        => new(workerId, WorkerKey, CreateConfiguration(), TimeProvider.System);

    public static string CreateAppProof(string workerId)
        => RuntimeWorkerAuthentication.CreateAppProof(
            Convert.FromHexString(AppKey), "test-app", workerId, DateTimeOffset.UtcNow);

    public static WorkerInitResponse Authenticate(WorkerInitResponse response, string workerId)
    {
        RuntimeWorkerAuthentication.SetAppProof(response.Capabilities, CreateAppProof(workerId));

        return response;
    }

    // Existing lifecycle tests use these authenticated fixture overloads, never an anonymous product path.
    public static Task<WorkerLinkResult> LinkAsync(
        this IWorkerChannelRegistry registry, string workerId, Uri endpoint, CancellationToken cancellationToken = default)
        => registry.LinkAsync(workerId, endpoint, WorkerKey, cancellationToken: cancellationToken);

    public static Task<DuplexChannel<T>> ConnectAsync<T>(
        this IDuplexChannelFactory<T> factory, Uri endpoint, CancellationToken cancellationToken = default)
        where T : class
        => factory.ConnectAsync(endpoint, CreateCredentials(), cancellationToken);

    public static void ConfigureTlsListener(WebApplicationBuilder builder, DateTimeOffset? expires = null)
    {
        builder.WebHost.UseKestrelHttpsConfiguration();
        builder.Services.AddSingleton(_ => new TestCertificate(expires));
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0, listener =>
        {
            listener.Protocols = HttpProtocols.Http2;
            listener.UseHttps(options.ApplicationServices.GetRequiredService<TestCertificate>().Certificate);
        }));
    }

    private sealed class TestCertificate : IDisposable
    {
        public TestCertificate(DateTimeOffset? expires)
        {
            using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            CertificateRequest request = new("CN=test-worker", key, HashAlgorithmName.SHA256);
            DateTimeOffset end = expires ?? DateTimeOffset.UtcNow.AddDays(1);
            Certificate = request.CreateSelfSigned(end.AddDays(-2), end);
            if (OperatingSystem.IsWindows())
            {
                using X509Certificate2 ephemeral = Certificate;
                byte[] pkcs12 = ephemeral.Export(X509ContentType.Pkcs12);
                try
                {
                    Certificate = X509CertificateLoader.LoadPkcs12(pkcs12, password: null);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(pkcs12);
                }
            }
        }

        public X509Certificate2 Certificate { get; }

        public void Dispose() => Certificate.Dispose();
    }
}
