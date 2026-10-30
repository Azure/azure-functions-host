// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Azure.Functions.Authentication;
using Azure.Functions.Tests;
using Azure.Functions.WorkerProxy.State;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Azure.Functions.WorkerProxy.Tests;

public class AuthenticationDiagnosticsTests
{
    [Theory]
    [InlineData("CONTAINER_ENCRYPTION_KEY", null, "MissingKey")]
    [InlineData("CONTAINER_ENCRYPTION_KEY", "invalid-private-key", "InvalidKey")]
    [InlineData("WEBSITE_AUTH_ENCRYPTION_KEY", null, "MissingKey")]
    [InlineData("WEBSITE_AUTH_ENCRYPTION_KEY", "invalid-private-key", "InvalidKey")]
    public async Task Startup_FlagsUnavailableExpectedKeyWithoutLoggingItsValue(string setting, string? value, string reason)
    {
        LogCapture capture = new();
        await using WorkerProxyWebApplicationFactory factory = new(
            new Dictionary<string, string?> { [setting] = value },
            services => services.AddLogging(logging => logging.AddProvider(capture.Provider)), useTestAuthentication: false);

        _ = factory.Services;

        Assert.Contains(capture.Entries, entry => entry.Level is LogLevel.Error
            && entry.Message.Contains(setting, StringComparison.Ordinal)
            && entry.Message.Contains(reason, StringComparison.Ordinal));
        capture.AssertNoCredentials();
        Assert.DoesNotContain("invalid-private-key", capture.Text, StringComparison.Ordinal);
        if (string.Equals(setting, RuntimeWorkerAuthentication.AppKeySetting, StringComparison.Ordinal))
        {
            using HttpClient client = factory.CreateWorkerProxyClient();
            using StringContent assignment = new(
                """{"startupMode":"Preconfigured","functionAppName":"test-app","functionGroupName":"group","isAlwaysReady":false,"environment":{},"functionAppDirectory":""}""",
                Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await client.PutAsync("/admin/worker/assignment", assignment);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.False(factory.Services.GetRequiredService<WorkerPodStateManager>().State.IsWorkerAttached);
        }
    }

    [Fact]
    public async Task MissingUnrelatedSiteHeader_IsQuietButProtectedManagementChallengeIdentifiesMissingToken()
    {
        LogCapture capture = new();
        await using WorkerProxyWebApplicationFactory factory = CreateFactory(capture);
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        DefaultHttpContext unrelated = new() { RequestServices = scope.ServiceProvider };

        AuthenticateResult result = await unrelated.AuthenticateAsync(ContainerManagementAuthenticationHandler.SchemeName);

        Assert.True(result.None);
        Assert.DoesNotContain(capture.Entries, entry => entry.Level >= LogLevel.Warning);
        using HttpClient client = factory.CreateWorkerProxyClient(authenticate: false);
        using HttpResponseMessage response = await client.PostAsync("/admin/infra/instanceState", content: null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(capture.Entries, entry => entry.Message.Contains("MissingToken", StringComparison.Ordinal));
        capture.AssertNoCredentials();
    }

    [Theory]
    [InlineData("malformed", "MalformedToken")]
    [InlineData("wrong-key", "Signature")]
    [InlineData("runtime-key", "Signature")]
    [InlineData("caller-token", "Issuer")]
    [InlineData("wrong-algorithm", "Algorithm")]
    [InlineData("wrong-issuer", "Issuer")]
    [InlineData("wrong-pod", "Audience")]
    [InlineData("expired", "Lifetime")]
    [InlineData("duplicate", "DuplicateToken")]
    public async Task ManagementRejection_LogsOnlySafeCategory(string scenario, string reason)
    {
        LogCapture capture = new();
        await using WorkerProxyWebApplicationFactory factory = CreateFactory(capture);
        using HttpClient client = factory.CreateWorkerProxyClient(authenticate: false);
        string token = scenario switch
        {
            "malformed" => "private-malformed-token",
            "wrong-key" => ManagementTokenTestData.Create(RuntimeAuthenticationTestData.EncodedAppKey, RuntimeAuthenticationTestData.WorkerId),
            "runtime-key" => ManagementTokenTestData.Create(ManagementTokenTestData.RuntimeKey, RuntimeAuthenticationTestData.WorkerId),
            "caller-token" => RuntimeAuthenticationTestData.CreateCallerToken(),
            "wrong-algorithm" => ManagementTokenTestData.Create(RuntimeAuthenticationTestData.EncodedWorkerKey, RuntimeAuthenticationTestData.WorkerId,
                algorithm: "HS512"),
            "wrong-issuer" => ManagementTokenTestData.Create(RuntimeAuthenticationTestData.EncodedWorkerKey, RuntimeAuthenticationTestData.WorkerId,
                issuer: "https://private-customer-issuer"),
            "wrong-pod" => ManagementTokenTestData.Create(RuntimeAuthenticationTestData.EncodedWorkerKey, "private-customer-pod"),
            "expired" => ManagementTokenTestData.Create(RuntimeAuthenticationTestData.EncodedWorkerKey, RuntimeAuthenticationTestData.WorkerId,
                notBefore: DateTimeOffset.UtcNow.AddMinutes(-20), expires: DateTimeOffset.UtcNow.AddMinutes(-6)),
            _ => ManagementTokenTestData.Create(RuntimeAuthenticationTestData.EncodedWorkerKey, RuntimeAuthenticationTestData.WorkerId)
        };
        client.DefaultRequestHeaders.Add(ManagementTokenTestData.HeaderName,
            string.Equals(scenario, "duplicate", StringComparison.Ordinal) ? [token, token] : [token]);

        using StringContent assignment = new(
            """{"startupMode":"Preconfigured","functionAppName":"test-app","functionGroupName":"group","isAlwaysReady":false,"environment":{},"functionAppDirectory":""}""",
            Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await client.PutAsync("/admin/worker/assignment", assignment);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, factory.Services.GetRequiredService<WorkerPodStateManager>().State.Revision);
        Assert.Equal(WorkerAssignmentState.Unassigned, factory.Services.GetRequiredService<WorkerPodStateManager>().State.AssignmentState);
        Assert.Empty(await response.Content.ReadAsStringAsync());
        Assert.Contains(capture.Entries, entry => entry.Level is LogLevel.Warning
            && entry.Message.Contains($"authentication failed: {reason}", StringComparison.Ordinal));
        Assert.DoesNotContain(token, capture.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("private-customer", capture.Text, StringComparison.Ordinal);
        capture.AssertNoCredentials();
    }

    [Theory]
    [InlineData("missing", "MissingToken")]
    [InlineData("malformed", "MalformedToken")]
    [InlineData("wrong-key", "Signature")]
    [InlineData("wrong-purpose", "Purpose")]
    [InlineData("wrong-worker", "Audience")]
    [InlineData("expired", "Lifetime")]
    public async Task CallerRejection_LogsOnlySafeCategory(string scenario, string reason)
    {
        LogCapture capture = new();
        await using WorkerProxyWebApplicationFactory factory = CreateFactory(capture);
        factory.Services.GetRequiredService<WorkerPodStateManager>().Assign(new(
            WorkerStartupMode.Preconfigured, RuntimeAuthenticationTestData.AppName, "group", false,
            new Dictionary<string, string>(), string.Empty));
        using HttpClient client = factory.CreateHttpForwardingClient(authenticate: false);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string token = scenario switch
        {
            "malformed" => "private-malformed-token",
            "wrong-key" => RuntimeWorkerAuthentication.CreateToken(RuntimeAuthenticationTestData.AppKey,
                RuntimeWorkerAuthentication.WorkerAudience(RuntimeAuthenticationTestData.WorkerId), "worker", now),
            "wrong-purpose" => RuntimeWorkerAuthentication.CreateToken(RuntimeAuthenticationTestData.WorkerKey,
                RuntimeWorkerAuthentication.WorkerAudience(RuntimeAuthenticationTestData.WorkerId), "private-customer-purpose", now),
            "wrong-worker" => RuntimeWorkerAuthentication.CreateToken(RuntimeAuthenticationTestData.WorkerKey,
                RuntimeWorkerAuthentication.WorkerAudience("private-customer-worker"), "worker", now),
            "expired" => RuntimeAuthenticationTestData.CreateCallerToken(now.AddMinutes(-6)),
            _ => RuntimeAuthenticationTestData.CreateCallerToken(now)
        };
        if (!string.Equals(scenario, "missing", StringComparison.Ordinal))
        {
            client.DefaultRequestHeaders.Add(RuntimeWorkerAuthentication.WorkerTokenHeader, token);
        }

        using HttpResponseMessage response = await client.GetAsync("/invoke");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(capture.Entries, entry => entry.Level is LogLevel.Warning
            && entry.Message.Contains($"authentication failed: {reason}", StringComparison.Ordinal));
        Assert.DoesNotContain(token, capture.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("private-customer", capture.Text, StringComparison.Ordinal);
        capture.AssertNoCredentials();
    }

    private static WorkerProxyWebApplicationFactory CreateFactory(LogCapture capture) =>
        new(configureServices: services => services.AddLogging(logging => logging.AddProvider(capture.Provider)), useTestAuthentication: false);

    private sealed class LogCapture
    {
        public LogCapture()
        {
            Mock<ILoggerProvider> provider = new();
            provider.Setup(value => value.CreateLogger(It.IsAny<string>())).Returns(() =>
            {
                Mock<ILogger> logger = new();
                logger.Setup(value => value.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
                logger.Setup(value => value.Log(It.IsAny<LogLevel>(), It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(),
                    It.IsAny<Exception?>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()))
                    .Callback(new InvocationAction(call => Entries.Enqueue(((LogLevel)call.Arguments[0],
                        (string)((Delegate)call.Arguments[4]).DynamicInvoke(call.Arguments[2], call.Arguments[3])!))));
                return logger.Object;
            });
            Provider = provider.Object;
        }

        public ILoggerProvider Provider { get; }

        public ConcurrentQueue<(LogLevel Level, string Message)> Entries { get; } = new();

        public string Text => string.Join(Environment.NewLine, Entries.Select(entry => entry.Message));

        public void AssertNoCredentials()
        {
            Assert.DoesNotContain(RuntimeAuthenticationTestData.EncodedWorkerKey, Text, StringComparison.Ordinal);
            Assert.DoesNotContain(RuntimeAuthenticationTestData.EncodedAppKey, Text, StringComparison.Ordinal);
        }
    }
}
