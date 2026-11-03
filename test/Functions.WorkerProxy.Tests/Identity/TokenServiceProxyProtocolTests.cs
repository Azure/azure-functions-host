// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Google.Protobuf;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Azure.WebJobs.Script.Grpc.TokenServiceProxy;
using Microsoft.Azure.WebJobs.Script.Grpc.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Azure.Functions.WorkerProxy.Tests.Identity;

public sealed class TokenServiceProxyProtocolTests
{
    [Theory]
    [InlineData("Logging:Console:LogLevel:Default")]
    [InlineData("Logging:Console:LogLevel:Microsoft.AspNetCore.Hosting.Diagnostics")]
    public async Task EnabledTokenServiceProxy_DoesNotEmitFrameworkRequestUriLogs(string providerOverride)
    {
        Dictionary<string, string?> configuration = new()
        {
            ["WorkerProxy:TokenServiceProxy:Enabled"] = "true",
            ["WorkerProxy:TokenServiceProxy:Port"] = "0",
            ["IDENTITY_HEADER"] = "local-test-secret",
            [providerOverride] = "Information"
        };
        await using WorkerProxyWebApplicationFactory factory = new(configuration);
        ILogger logger = factory.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Microsoft.AspNetCore.Hosting.Diagnostics");
        Assert.False(logger.IsEnabled(LogLevel.Information));
        Assert.True(logger.IsEnabled(LogLevel.Warning));
    }

    [Theory]
    [InlineData("/msi/token?a=%2f&a=2", true)]
    [InlineData("/MSI/token?a=%2f&a=2", true)]
    [InlineData("/MsI/ToKeN?a=%2f&a=2", true)]
    [InlineData("http://evil.test/MSI/token", false)]
    [InlineData("//evil.test/MSI/token", false)]
    [InlineData("/MSI/%74oken", false)]
    [InlineData("/MSI/token/../admin", false)]
    [InlineData("/MSI/token#fragment", false)]
    [InlineData("/msi/token/extra", false)]
    [InlineData("/msi/tokenized", false)]
    public void Target_RequiresConfiguredPathIgnoringCase(string target, bool valid)
    {
        HttpProxyLimits limits = new TokenServiceProxyOptions().ToLimits();
        if (valid)
        {
            TokenServiceProxyProtocol.ValidateTarget(target, "/msi/token", limits);
        }
        else
        {
            Assert.Throws<TokenServiceProxyException>(() => TokenServiceProxyProtocol.ValidateTarget(target, "/msi/token", limits));
        }
    }

    [Theory]
    [InlineData("/local/token", "/local/token", "")]
    [InlineData("/local/token?", "/local/token", "?")]
    [InlineData("/local/token?future=%2f&future=%2F&resource=a%20b", "/local/token", "?future=%2f&future=%2F&resource=a%20b")]
    public void Target_SeparatesPathAndQueryWithoutInterpretingParameters(string target, string expectedPath, string expectedQuery)
    {
        var (path, query) = TokenServiceProxyProtocol.ParseTarget(target, new TokenServiceProxyOptions().ToLimits());

        Assert.Equal(expectedPath, path);
        Assert.Equal(expectedQuery, query);
    }

    [Theory]
    [InlineData("http://other.test/token?resource=a")]
    [InlineData("//other.test/token?resource=a")]
    [InlineData("/local/token#fragment")]
    [InlineData("/local/token?resource=a\r\nInjected: header")]
    [InlineData("/local/../admin?resource=a")]
    public void Target_RejectsNonOriginOrAmbiguousTargets(string target)
    {
        Assert.Throws<TokenServiceProxyException>(() => TokenServiceProxyProtocol.ParseTarget(target, new TokenServiceProxyOptions().ToLimits()));
    }

    [Fact]
    public void ConfiguredRootTokenPath_DoesNotPermitOtherServicePaths()
    {
        HttpProxyLimits limits = new TokenServiceProxyOptions().ToLimits();
        Assert.True(TokenServiceProxyProtocol.IsTokenPath("/"));
        TokenServiceProxyProtocol.ValidateTarget("/?api-version=2019-08-01", "/", limits);
        Assert.Throws<TokenServiceProxyException>(() => TokenServiceProxyProtocol.ValidateTarget("/api/specialize", "/", limits));
    }

    [Fact]
    public void Headers_RemoveHopByHopAndCredentials_PreserveRepeatedValues()
    {
        HttpProxyLimits limits = new TokenServiceProxyOptions().ToLimits();
        var filtered = TokenServiceProxyProtocol.FilterHeaders(
        [
            TokenServiceProxyProtocol.Header("Connection", ["X-Hop"]),
            TokenServiceProxyProtocol.Header("X-Hop", ["remove"]),
            TokenServiceProxyProtocol.Header("Authorization", ["remove"]),
            TokenServiceProxyProtocol.Header("X-IDENTITY-HEADER", ["remove"]),
            TokenServiceProxyProtocol.Header("Secret", ["remove"]),
            TokenServiceProxyProtocol.Header("Content-Length", ["5"]),
            TokenServiceProxyProtocol.Header("X-Repeated", ["one", "two"])
        ], limits);

        HttpProxyHeader retained = Assert.Single(filtered);
        Assert.Equal("X-Repeated", retained.Name);
        Assert.Equal(new[] { "one", "two" }, retained.Values);
    }

    [Fact]
    public void Headers_RejectProtectedConnectionNomination_AndCountLimit()
    {
        HttpProxyLimits limits = new TokenServiceProxyOptions().ToLimits();
        Assert.Throws<TokenServiceProxyException>(() => TokenServiceProxyProtocol.FilterHeaders(
            [TokenServiceProxyProtocol.Header("Connection", ["X-IDENTITY-HEADER"])], limits));
        Assert.Throws<TokenServiceProxyException>(() => TokenServiceProxyProtocol.FilterHeaders(
            [TokenServiceProxyProtocol.Header("Connection", ["Secret"])], limits));
        limits.MaxHeaderCount = 1;
        Assert.Single(TokenServiceProxyProtocol.FilterHeaders([TokenServiceProxyProtocol.Header("X-One", ["a"])], limits));
        TokenServiceProxyException error = Assert.Throws<TokenServiceProxyException>(() => TokenServiceProxyProtocol.FilterHeaders(
            [TokenServiceProxyProtocol.Header("X-One", ["a", "b"])], limits));
        Assert.Equal(HttpProxyFailure.Types.Code.HeadersTooLarge, error.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(1024)]
    public async Task BufferedBody_EnforcesLimitWithoutTruncation(int length)
    {
        byte[] bytes = new byte[length];
        using MemoryStream stream = new(bytes);
        Assert.Equal(ByteString.CopyFrom(bytes), await TokenServiceProxyProtocol.ReadBodyAsync(stream, 1024, false, CancellationToken.None));
        using MemoryStream oversized = new(new byte[1025]);
        TokenServiceProxyException error = await Assert.ThrowsAsync<TokenServiceProxyException>(() =>
            TokenServiceProxyProtocol.ReadBodyAsync(oversized, 1024, false, CancellationToken.None));
        Assert.Equal(HttpProxyFailure.Types.Code.RequestTooLarge, error.Code);
    }

    [Fact]
    public void Admission_RetainedTransportLeaseOutlivesHttpCaller()
    {
        TokenServiceProxyAdmission admission = new(1, 10);
        TokenServiceProxyAdmission.Lease caller = Assert.IsType<TokenServiceProxyAdmission.Lease>(admission.TryAcquire(10));
        TokenServiceProxyAdmission.Lease transport = caller.Retain();
        caller.Dispose();
        caller.Dispose();
        Assert.Null(admission.TryAcquire(1));
        transport.Dispose();
        using TokenServiceProxyAdmission.Lease? next = admission.TryAcquire(10);
        Assert.NotNull(next);
    }

    [Fact]
    public async Task TrackedWrite_CanceledQueuedMessage_IsDiscardedOnlyByPump()
    {
        Channel<object> channel = Channel.CreateUnbounded<object>();
        TrackedMessageWriter<object> writes = new();
        using CancellationTokenSource cancellation = new();
        object message = new();
        Task<bool> sent = writes.WriteAsync(channel.Writer, message, cancellation.Token);
        cancellation.Cancel();
        Assert.False(sent.IsCompleted);
        Assert.Same(message, await channel.Reader.ReadAsync());
        Assert.False(writes.Prepare(message));
        Assert.False(await sent.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task TrackedWrite_CancellationDuringWrite_DoesNotPretendOwnershipEnded()
    {
        Channel<object> channel = Channel.CreateUnbounded<object>();
        TrackedMessageWriter<object> writes = new();
        using CancellationTokenSource cancellation = new();
        object message = new();
        Task<bool> sent = writes.WriteAsync(channel.Writer, message, cancellation.Token);
        Assert.Same(message, await channel.Reader.ReadAsync());
        Assert.True(writes.Prepare(message));
        cancellation.Cancel();
        Assert.False(sent.IsCompleted);
        writes.Written(message);
        Assert.True(await sent.WaitAsync(TimeSpan.FromSeconds(5)));
    }
}
