// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Azure.Functions.Host.HostState;
using Azure.Functions.Rpc.Client;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Azure.Functions.Host.Tests;

/// <summary>
/// Exercises the hosted BYOC Runtime drain management contract.
/// </summary>
public sealed class ByocRuntimeDrainEndpointBehaviorTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task Post_EnforcesAdminContractAndPublishesSharedHostState()
    {
        using CancellationTokenSource timeout = new(TestTimeout);
        DateTimeOffset acceptedAtUtc = new(2026, 10, 31, 7, 0, 0, TimeSpan.Zero);
        Mock<IByocRuntimeDrainManager> manager = new(MockBehavior.Strict);
        manager.Setup(value => value.Accept(ByocDrainModel.Phased))
            .Returns(new ByocRuntimeDrainReceipt(ByocDrainModel.Phased, acceptedAtUtc));
        manager.Setup(value => value.Accept(ByocDrainModel.Immediate))
            .Returns(new ByocRuntimeDrainReceipt(ByocDrainModel.Immediate, acceptedAtUtc));
        await using ByocRuntimeDrainTestHost host =
            await ByocRuntimeDrainTestHost.StartAsync(
                manager.Object,
                timeout.Token);

        using (HttpResponseMessage unauthorized = await host.PostAsync(
            """{"drainModel":"Phased"}""",
            authenticated: false,
            timeout.Token))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        }

        foreach (ByocDrainModel drainModel in Enum.GetValues<ByocDrainModel>())
        {
            using HttpResponseMessage response = await host.PostAsync(
                $$"""{"drainModel":"{{drainModel}}"}""",
                authenticated: true,
                timeout.Token);

            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            using JsonDocument document = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(timeout.Token));
            Assert.Equal(
                drainModel.ToString(),
                document.RootElement.GetProperty("drainModel").GetString());
            Assert.Equal(
                acceptedAtUtc,
                document.RootElement.GetProperty("acceptedAtUtc").GetDateTimeOffset());
        }

        IReadOnlyList<(string? Body, string Code, string Target)> invalidRequests =
        [
            (null, "InvalidBody", "request"),
            (string.Empty, "InvalidBody", "request"),
            ("null", "InvalidBody", "request"),
            ("[]", "InvalidBody", "request"),
            ("{}", "Required", "drainModel"),
            ("""{"drainModel":null}""", "InvalidBody", "request"),
            ("""{"drainModel":123}""", "InvalidBody", "request"),
            ("""{"drainModel":""}""", "Required", "drainModel"),
            ("""{"drainModel":"phased"}""", "InvalidValue", "drainModel"),
            ("""{"drainModel":"Unknown"}""", "InvalidValue", "drainModel"),
            ("""{"DrainModel":"Phased"}""", "InvalidBody", "request"),
            ("""{"drainModel":"Phased","reason":"stop"}""", "InvalidBody", "request"),
            ("""{"drainModel":"Phased","drainModel":"Immediate"}""", "InvalidBody", "request"),
            ("""{"drainModel":"Phased","broken":}""", "InvalidBody", "request"),
        ];
        foreach ((string? body, string code, string target) in invalidRequests)
        {
            using HttpResponseMessage response = await host.PostAsync(
                body,
                authenticated: true,
                timeout.Token);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            using JsonDocument document = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(timeout.Token));
            JsonElement error = Assert.Single(
                document.RootElement.GetProperty("errors").EnumerateArray());
            Assert.Equal(code, error.GetProperty("code").GetString());
            Assert.Equal(target, error.GetProperty("target").GetString());
        }

        manager.Verify(value => value.Accept(ByocDrainModel.Phased), Times.Once);
        manager.Verify(value => value.Accept(ByocDrainModel.Immediate), Times.Once);
        manager.VerifyNoOtherCalls();

        await using ByocRuntimeDrainTestHost standardHost =
            await ByocRuntimeDrainTestHost.StartAsync(
                manager.Object,
                timeout.Token,
                includeCompute: false);
        using HttpResponseMessage missingRoute = await standardHost.PostAsync(
            """{"drainModel":"Phased"}""",
            authenticated: true,
            timeout.Token);
        Assert.Equal(HttpStatusCode.NotFound, missingRoute.StatusCode);

        await VerifyHostStatePublicationAsync(timeout.Token);
    }

    private static async Task VerifyHostStatePublicationAsync(
        CancellationToken cancellationToken)
    {
        FakeHostState hostState = new();
        RecordingHostStateHandler handler = new(
            HttpStatusCode.InternalServerError,
            HttpStatusCode.OK,
            HttpStatusCode.OK,
            HttpStatusCode.OK);
        using HttpClient client = new(handler);
        Mock<IHttpClientFactory> httpClientFactory =
            new(MockBehavior.Strict);
        httpClientFactory
            .Setup(factory => factory.CreateClient(
                AppServerHostStateClient.HttpClientName))
            .Returns(client);
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [EnvironmentSettingNames.ContainerName] = "runtime-1",
                [EnvironmentSettingNames.MeshInitURI] =
                    "http://appserver.test:6060/",
            })
            .Build();
        HostStatePublicationCoordinator publicationCoordinator = new(
            hostState,
            httpClientFactory.Object,
            configuration);
        using ByocRuntimeDrainHostStateObserver observer = new(
            hostState,
            hostState,
            publicationCoordinator,
            TimeProvider.System,
            NullLogger<ByocRuntimeDrainHostStateObserver>.Instance);
        await observer.StartAsync(cancellationToken);
        Assert.False(handler.TryRead(out _));

        try
        {
            HostStateSnapshot admissionState =
                hostState.CloseAdmission();
            AssertPublishedState(
                await handler.ReadAsync(cancellationToken),
                admissionState);
            AssertPublishedState(
                await handler.ReadAsync(cancellationToken),
                admissionState);

            foreach (int linkedWorkerCount in new[] { 1, 0 })
            {
                HostStateSnapshot expected =
                    hostState.PublishLinkedWorkerCount(linkedWorkerCount);
                AssertPublishedState(
                    await handler.ReadAsync(cancellationToken),
                    expected);
            }
        }
        finally
        {
            await observer.StopAsync(cancellationToken);
        }

        httpClientFactory.Verify(
            factory => factory.CreateClient(
                AppServerHostStateClient.HttpClientName),
            Times.AtLeast(4));
        httpClientFactory.VerifyNoOtherCalls();
    }

    private static void AssertPublishedState(
        PublishedHostState request,
        HostStateSnapshot expected)
    {
        Assert.Equal(
            new Uri("http://appserver.test:6060/admin/infra/host/state"),
            request.Uri);
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Equal("runtime-1", request.ContainerName);
        Assert.Equal("application/json", request.ContentType);
        Assert.False(request.RequiresCapacityFence);
        using JsonDocument document = JsonDocument.Parse(request.Body);
        Assert.Equal(
            ["createdTime", "httpCapacity", "linkedWorkerCount"],
            document.RootElement
                .EnumerateObject()
                .Select(static property => property.Name)
                .Order(StringComparer.Ordinal)
                .ToArray());
        Assert.Equal(
            expected.CreatedTime,
            document.RootElement
                .GetProperty("createdTime")
                .GetDateTimeOffset());
        Assert.Equal(
            expected.LinkedWorkerCount,
            document.RootElement
            .GetProperty("linkedWorkerCount")
            .GetInt32());
        Assert.Equal(
            0,
            document.RootElement
                .GetProperty("httpCapacity")
                .GetInt64());
    }

    private sealed class FakeHostState :
        IHostStateCoordinator,
        IRuntimeWorkerAdmissionGate
    {
        private readonly Lock _lock = new();

        private HostStateSnapshot _current = new(
            new DateTimeOffset(2026, 10, 31, 7, 0, 0, TimeSpan.Zero),
            HttpCapacity: 2,
            LinkedWorkerCount: 2,
            Version: 0);

        private TaskCompletionSource<long> _stateChanged =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private bool _isClosed;

        public HostStateSnapshot Current
        {
            get
            {
                lock (_lock)
                {
                    return _current;
                }
            }
        }

        public bool IsClosed
        {
            get
            {
                lock (_lock)
                {
                    return _isClosed;
                }
            }
        }

        public long Version => Current.Version;

        public HostStateSnapshot CloseAdmission()
        {
            Assert.True(TryClose());
            return Current;
        }

        public HostStateSnapshot PublishLinkedWorkerCount(
            int linkedWorkerCount)
        {
            lock (_lock)
            {
                Assert.True(_isClosed);
                return PublishLocked(linkedWorkerCount);
            }
        }

        public bool TryClose()
        {
            lock (_lock)
            {
                if (_isClosed)
                {
                    return false;
                }

                _isClosed = true;
                PublishLocked(_current.LinkedWorkerCount);
                return true;
            }
        }

        public Task<long> WaitForChangeAsync(
            long version,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            lock (_lock)
            {
                if (_current.Version != version)
                {
                    return Task.FromResult(_current.Version);
                }

                return _stateChanged.Task.WaitAsync(cancellationToken);
            }
        }

        private HostStateSnapshot PublishLocked(int linkedWorkerCount)
        {
            long version = _current.Version + 1;
            _current = new(
                _current.CreatedTime.AddTicks(1),
                HttpCapacity: 0,
                linkedWorkerCount,
                version);
            TaskCompletionSource<long> changed = _stateChanged;
            _stateChanged = new(
                TaskCreationOptions.RunContinuationsAsynchronously);
            changed.TrySetResult(version);

            return _current;
        }
    }

    private sealed class RecordingHostStateHandler : HttpMessageHandler
    {
        private readonly Channel<PublishedHostState> _requests =
            Channel.CreateUnbounded<PublishedHostState>();

        private readonly Queue<HttpStatusCode> _responses;

        public RecordingHostStateHandler(
            params HttpStatusCode[] responses)
        {
            _responses = new(responses);
        }

        public async Task<PublishedHostState> ReadAsync(
            CancellationToken cancellationToken)
            => await _requests.Reader.ReadAsync(cancellationToken);

        public bool TryRead(out PublishedHostState? request)
            => _requests.Reader.TryRead(out request);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string body = await request.Content!
                .ReadAsStringAsync(cancellationToken);
            string containerName = request.Headers.TryGetValues(
                ScriptConstants.ContainerInstanceHeader,
                out IEnumerable<string>? values)
                    ? values.Single()
                    : string.Empty;
            string contentType =
                request.Content.Headers.ContentType?.MediaType
                    ?? string.Empty;
            bool requiresCapacityFence = request.Headers.Contains(
                AppServerHostStateClient.CapacityFenceHeader);
            await _requests.Writer.WriteAsync(
                new(
                    request.RequestUri!,
                    request.Method,
                    containerName,
                    contentType,
                    requiresCapacityFence,
                    body),
                cancellationToken);

            return new(
                _responses.TryDequeue(out HttpStatusCode response)
                    ? response
                    : HttpStatusCode.OK);
        }
    }

    private sealed record PublishedHostState(
        Uri Uri,
        HttpMethod Method,
        string ContainerName,
        string ContentType,
        bool RequiresCapacityFence,
        string Body);
}
