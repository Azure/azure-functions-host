// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Azure.Functions.Rpc.Client.Tests;

public class WorkerProxyCertificateValidationTests
{
    [Fact]
    public void ExpiryOnlyPolicy_RejectsAbsentAndExpiryBoundaryCertificates()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        using ECDsa key = ECDsa.Create();
        CertificateRequest request = new("CN=untrusted-name", key, HashAlgorithmName.SHA256);
        using X509Certificate2 certificate = request.CreateSelfSigned(now.AddDays(-2), now.AddDays(1));
        DateTimeOffset end = certificate.NotAfter.ToUniversalTime();

        Assert.False(RpcClientFactory.IsCertificateUnexpired(null, now));
        Assert.True(RpcClientFactory.IsCertificateUnexpired(certificate, end.AddTicks(-1)));
        Assert.False(RpcClientFactory.IsCertificateUnexpired(certificate, end));
        Assert.False(RpcClientFactory.IsCertificateUnexpired(certificate, end.AddSeconds(1)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HttpClient_UsesExpiryOnlyPolicyOnRealTls(bool expired)
    {
        await using TestFunctionRpcServer server = await TestFunctionRpcServer.StartAsync(
            expires: DateTimeOffset.UtcNow.AddMinutes(expired ? -1 : 10));
        using HttpClient client = new(RpcClientFactory.CreateHttpHandler());
        using HttpRequestMessage request = new(HttpMethod.Get, new Uri(server.Endpoint, "/probe"))
        {
            Version = HttpVersion.Version20,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact
        };
        if (expired)
        {
            await Assert.ThrowsAsync<HttpRequestException>(() => client.SendAsync(request));
        }
        else
        {
            using HttpResponseMessage response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GrpcClient_UsesTheSameExpiryOnlyPolicyOnRealTls(bool expired)
    {
        await using TestFunctionRpcServer server = await TestFunctionRpcServer.StartAsync(
            expires: DateTimeOffset.UtcNow.AddMinutes(expired ? -1 : 10));
        await using RpcClientFactory factory = new(NullLogger<RpcClientFactory>.Instance);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(15));
        FunctionRpc.FunctionRpcClient client = await factory.CreateAsync(server.Endpoint, timeout.Token);
        using var call = client.EventStream(cancellationToken: timeout.Token);
        if (expired)
        {
            Grpc.Core.RpcException failure = await Assert.ThrowsAsync<Grpc.Core.RpcException>(
                () => call.ResponseStream.MoveNext(timeout.Token));
            Assert.Contains(failure.StatusCode, new[] { StatusCode.Unavailable, StatusCode.Internal });
            Assert.Contains("SSL", failure.Status.Detail, StringComparison.OrdinalIgnoreCase);
            Assert.False(server.Service.Connected.IsCompleted);
        }
        else
        {
            await server.Service.Connected.WaitAsync(timeout.Token);
        }
    }
}
