// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Azure.Functions.Host.HostState;
using Azure.Functions.Rpc.Client;
using Microsoft.Azure.WebJobs.Script.ExtensionBundle;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Azure.Functions.Host.Tests.HostState;

public class AppServerHostStateClientTests
{
    private static readonly DateTimeOffset CreatedTime = new(2026, 10, 2, 12, 34, 56, 123, TimeSpan.Zero);
    private readonly RecordingHandler _handler = new();

    [Fact]
    public void Constructor_DoesNotReadMeshInitUri()
    {
        Mock<IConfiguration> configuration = new(MockBehavior.Strict);

        _ = CreateClient(configuration.Object);

        configuration.VerifyGet(config => config["MESH_INIT_URI"], Times.Never());
    }

    [Theory]
    [InlineData(36L, 3, """{"createdTime":"2026-10-02T12:34:56.123+00:00","httpCapacity":36,"linkedWorkerCount":3}""")]
    [InlineData(4294967296L, 268435457, """{"createdTime":"2026-10-02T12:34:56.123+00:00","httpCapacity":4294967296,"linkedWorkerCount":268435457}""")]
    [InlineData(long.MaxValue, int.MaxValue, """{"createdTime":"2026-10-02T12:34:56.123+00:00","httpCapacity":9223372036854775807,"linkedWorkerCount":2147483647}""")]
    public async Task PublishAsync_SendsCreatedTimeCapacityAndLinkedWorkerCount(long httpCapacity, int workerCount, string expectedBody)
    {
        AppServerHostStateClient client = CreateClient();

        HttpStatusCode statusCode = await client.PublishAsync(new(CreatedTime, httpCapacity, workerCount), CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, statusCode);
        Assert.Equal(HttpMethod.Put, _handler.Method);
        Assert.Equal(new Uri("http://localhost:6060/admin/infra/host/state"), _handler.RequestUri);
        Assert.Equal("application/json", _handler.ContentType);
        Assert.Equal(expectedBody, _handler.Body);
    }

    [Fact]
    public async Task PublishAsync_RetryPreservesCapturedCreatedTime()
    {
        AppServerHostStateClient client = CreateClient();
        RuntimeState state = new(CreatedTime, 36, 3);
        _handler.StatusCode = HttpStatusCode.InternalServerError;
        Assert.Equal(HttpStatusCode.InternalServerError, await client.PublishAsync(state, CancellationToken.None));
        string? firstBody = _handler.Body;

        _handler.StatusCode = HttpStatusCode.OK;
        Assert.Equal(HttpStatusCode.OK, await client.PublishAsync(state, CancellationToken.None));

        Assert.Equal("""{"createdTime":"2026-10-02T12:34:56.123+00:00","httpCapacity":36,"linkedWorkerCount":3}""", firstBody);
        Assert.Equal(firstBody, _handler.Body);
    }

    [Theory]
    [InlineData("http://localhost:6060/", "http://localhost:6060/admin/infra/host/state")]
    [InlineData("http://appserver:7070", "http://appserver:7070/admin/infra/host/state")]
    [InlineData("http://appserver:7070/", "http://appserver:7070/admin/infra/host/state")]
    [InlineData("http://appserver:7070////", "http://appserver:7070/admin/infra/host/state")]
    [InlineData("http://appserver:7070/base", "http://appserver:7070/base/admin/infra/host/state")]
    [InlineData("http://appserver:7070/base/", "http://appserver:7070/base/admin/infra/host/state")]
    [InlineData("http://appserver:7070/base////", "http://appserver:7070/base/admin/infra/host/state")]
    [InlineData("https://appserver:7070/base/nested/", "https://appserver:7070/base/nested/admin/infra/host/state")]
    public async Task PublishAsync_UsesConfiguredMeshInitUri(string endpoint, string expectedUri)
    {
        AppServerHostStateClient client = CreateClient(new Dictionary<string, string?>
        {
            ["MESH_INIT_URI"] = endpoint,
        });

        await client.PublishAsync(new(CreatedTime, 0, 0), CancellationToken.None);

        Assert.Equal(new Uri(expectedUri), _handler.RequestUri);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\r\n")]
    public async Task PublishAsync_UsesDefaultEndpointWhenMeshInitUriIsMissingOrBlank(string? endpoint)
    {
        Dictionary<string, string?>? settings = endpoint is null ? null : new() { ["MESH_INIT_URI"] = endpoint };
        AppServerHostStateClient client = CreateClient(settings);

        await client.PublishAsync(new(CreatedTime, 0, 0), CancellationToken.None);

        Assert.Equal(new Uri("http://localhost:6060/admin/infra/host/state"), _handler.RequestUri);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task PublishAsync_ReturnsUnsuccessfulStatusCodeWithoutThrowing(HttpStatusCode responseStatusCode)
    {
        _handler.StatusCode = responseStatusCode;
        AppServerHostStateClient client = CreateClient();

        HttpStatusCode statusCode = await client.PublishAsync(new(CreatedTime, 0, 0), CancellationToken.None);

        Assert.Equal(responseStatusCode, statusCode);
    }

    [Fact]
    public async Task PublishAsync_ReadsMeshInitUriReloadedBeforeFirstPublication()
    {
        IConfigurationRoot configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        AppServerHostStateClient client = CreateClient(configuration);

        configuration["MESH_INIT_URI"] = "http://appserver:7070";
        await client.PublishAsync(new(CreatedTime, 0, 1), CancellationToken.None);

        Assert.Equal(new Uri("http://appserver:7070/admin/infra/host/state"), _handler.RequestUri);
    }

    [Fact]
    public async Task PublishAsync_CachesResolvedMeshInitUriAcrossPublications()
    {
        string endpoint = "http://appserver:7070/base";
        Mock<IConfiguration> configuration = new(MockBehavior.Strict);
        configuration.SetupGet(config => config["MESH_INIT_URI"]).Returns(() => endpoint);
        AppServerHostStateClient client = CreateClient(configuration.Object);
        await client.PublishAsync(new(CreatedTime, 0, 0), CancellationToken.None);
        Uri? firstUri = _handler.RequestUri;

        endpoint = "http://other-appserver:8080/";
        await client.PublishAsync(new(CreatedTime, 0, 1), CancellationToken.None);

        Assert.Equal(new Uri("http://appserver:7070/base/admin/infra/host/state"), firstUri);
        Assert.Same(firstUri, _handler.RequestUri);
        configuration.VerifyGet(config => config["MESH_INIT_URI"], Times.Once());
    }

    [Theory]
    [InlineData("not an absolute URI")]
    [InlineData("relative/base")]
    [InlineData("http://appserver:invalid")]
    [InlineData("http://[invalid")]
    public async Task PublishAsync_InvalidEndpoint_ThrowsInsteadOfReportingSuccess(string endpoint)
    {
        AppServerHostStateClient client = CreateClient(new Dictionary<string, string?> { ["MESH_INIT_URI"] = endpoint });

        await Assert.ThrowsAsync<UriFormatException>(() => client.PublishAsync(new(CreatedTime, 0, 0), CancellationToken.None));

        Assert.Null(_handler.Method);
    }

    [Fact]
    public async Task PublishAsync_DoesNotCacheFailedMeshInitUriResolution()
    {
        Mock<IConfiguration> configuration = new(MockBehavior.Strict);
        configuration.SetupSequence(config => config["MESH_INIT_URI"])
            .Returns("not an absolute URI")
            .Returns("http://appserver:7070/base/");
        AppServerHostStateClient client = CreateClient(configuration.Object);

        await Assert.ThrowsAsync<UriFormatException>(() => client.PublishAsync(new(CreatedTime, 0, 0), CancellationToken.None));
        Assert.Null(_handler.Method);

        Assert.Equal(HttpStatusCode.OK, await client.PublishAsync(new(CreatedTime, 0, 0), CancellationToken.None));
        Uri? resolvedUri = _handler.RequestUri;
        Assert.Equal(new Uri("http://appserver:7070/base/admin/infra/host/state"), resolvedUri);

        Assert.Equal(HttpStatusCode.OK, await client.PublishAsync(new(CreatedTime, 0, 1), CancellationToken.None));
        Assert.Same(resolvedUri, _handler.RequestUri);
        configuration.VerifyGet(config => config["MESH_INIT_URI"], Times.Exactly(2));
    }

    [Fact]
    public async Task PublishAsync_PropagatesCallerCancellation()
    {
        TaskCompletionSource requested = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _handler.OnRequest = async cancellationToken =>
        {
            requested.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        };
        using CancellationTokenSource cancellation = new();
        Task<HttpStatusCode> publication = CreateClient().PublishAsync(new(CreatedTime, 0, 0), cancellation.Token);
        await requested.Task.WaitAsync(TimeSpan.FromSeconds(10));

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => publication);
    }

    [Fact]
    public async Task PublishAsync_DoesNotBufferResponseBodyAndDisposesIt()
    {
        UnreadableContent content = new();
        _handler.ResponseContent = content;

        Assert.Equal(HttpStatusCode.OK, await CreateClient().PublishAsync(new(CreatedTime, 0, 0), CancellationToken.None));

        Assert.True(content.Disposed);
    }

    [Fact]
    public async Task AddAppServerHostStatePublisher_PreservesLegacyBundleHttpClient()
    {
        ServiceCollection collection = new();
        collection.AddAppServerHostStatePublisher().AddBundlesHttpClient();
        collection.AddHttpClient(nameof(ExtensionBundleManager)).ConfigurePrimaryHttpMessageHandler(() => _handler);
        using ServiceProvider services = collection.BuildServiceProvider();
        using HttpClient client = services.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(ExtensionBundleManager));

        using HttpResponseMessage response = await client.GetAsync("http://bundles/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public void AddAppServerHostStatePublisher_BoundsEachRequestToFiveSeconds()
    {
        using ServiceProvider services = new ServiceCollection().AddAppServerHostStatePublisher().BuildServiceProvider();
        using HttpClient client = services.GetRequiredService<IHttpClientFactory>().CreateClient(AppServerHostStateClient.HttpClientName);

        Assert.Equal(TimeSpan.FromSeconds(5), client.Timeout);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task AddAppServerHostStatePublisher_HttpHandlerDoesNotRetryResponses(HttpStatusCode statusCode)
    {
        int requests = 0;
        _handler.StatusCode = statusCode;
        _handler.OnRequest = _ =>
        {
            requests++;
            return Task.CompletedTask;
        };
        ServiceCollection collection = new();
        collection.AddAppServerHostStatePublisher();
        collection.AddHttpClient(AppServerHostStateClient.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => _handler);
        using ServiceProvider services = collection.BuildServiceProvider();
        using HttpClient client = services.GetRequiredService<IHttpClientFactory>().CreateClient(AppServerHostStateClient.HttpClientName);

        using HttpResponseMessage response = await client.GetAsync("http://appserver/");

        Assert.Equal(statusCode, response.StatusCode);
        Assert.Equal(1, requests);
    }

    private AppServerHostStateClient CreateClient(Dictionary<string, string?>? settings = null)
        => CreateClient(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());

    private AppServerHostStateClient CreateClient(IConfiguration configuration)
    {
        Mock<IHttpClientFactory> httpClientFactory = new(MockBehavior.Strict);
        httpClientFactory.Setup(factory => factory.CreateClient(AppServerHostStateClient.HttpClientName))
            .Returns(() => new HttpClient(_handler, disposeHandler: false));

        return new(httpClientFactory.Object, configuration);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;

        public HttpContent? ResponseContent { get; set; }

        public HttpMethod? Method { get; private set; }

        public Uri? RequestUri { get; private set; }

        public string? ContentType { get; private set; }

        public string? Body { get; private set; }

        public Func<CancellationToken, Task>? OnRequest { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Method = request.Method;
            RequestUri = request.RequestUri;
            ContentType = request.Content?.Headers.ContentType?.MediaType;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            if (OnRequest is not null)
            {
                await OnRequest(cancellationToken);
            }

            return new HttpResponseMessage(StatusCode) { Content = ResponseContent };
        }
    }

    private sealed class UnreadableContent : HttpContent
    {
        public bool Disposed { get; private set; }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => throw new InvalidOperationException("The response body must not be buffered.");

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
