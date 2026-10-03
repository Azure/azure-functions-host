// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Azure.Functions.Host.HostState;
using Azure.Functions.Rpc.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Azure.Functions.Host.Tests.HostState;

public class AppServerHostStateClientTests
{
    private static readonly DateTimeOffset CreatedTime = new(2026, 10, 2, 12, 34, 56, 123, TimeSpan.Zero);
    private readonly RecordingHandler _handler = new();

    [Theory]
    [InlineData(16L, 2, 1, """{"createdTime":"2026-10-02T12:34:56.123+00:00","httpCapacity":16}""")]
    [InlineData(4294967296L, 268435457, 268435456, """{"createdTime":"2026-10-02T12:34:56.123+00:00","httpCapacity":4294967296}""")]
    public async Task PublishAsync_SendsOnlyCreatedTimeAndCapacityToDefaultAppServerEndpoint(
        long httpCapacity, int workerCount, int httpWorkerCount, string expectedBody)
    {
        AppServerHostStateClient client = CreateClient();

        HttpStatusCode statusCode = await client.PublishAsync(new(CreatedTime, httpCapacity, workerCount, httpWorkerCount), CancellationToken.None);

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
        ComputeRuntimeState state = new(CreatedTime, 16, 2, 1);
        _handler.StatusCode = HttpStatusCode.InternalServerError;
        Assert.Equal(HttpStatusCode.InternalServerError, await client.PublishAsync(state, CancellationToken.None));
        string? firstBody = _handler.Body;

        _handler.StatusCode = HttpStatusCode.OK;
        Assert.Equal(HttpStatusCode.OK, await client.PublishAsync(state, CancellationToken.None));

        Assert.Equal("""{"createdTime":"2026-10-02T12:34:56.123+00:00","httpCapacity":16}""", firstBody);
        Assert.Equal(firstBody, _handler.Body);
    }

    [Theory]
    [InlineData("http://appserver:7070", "http://appserver:7070/admin/infra/host/state")]
    [InlineData("http://appserver:7070/", "http://appserver:7070/admin/infra/host/state")]
    [InlineData("http://appserver:7070/base", "http://appserver:7070/base/admin/infra/host/state")]
    [InlineData("http://appserver:7070/base/", "http://appserver:7070/base/admin/infra/host/state")]
    [InlineData(" ", "http://localhost:6060/admin/infra/host/state")]
    public async Task PublishAsync_UsesConfiguredMeshInitUri(string endpoint, string expectedUri)
    {
        AppServerHostStateClient client = CreateClient(new Dictionary<string, string?>
        {
            ["MESH_INIT_URI"] = endpoint,
        });

        await client.PublishAsync(new(CreatedTime, 0, 0, 0), CancellationToken.None);

        Assert.Equal(new Uri(expectedUri), _handler.RequestUri);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task PublishAsync_ReturnsUnsuccessfulStatusCodeWithoutThrowing(HttpStatusCode responseStatusCode)
    {
        _handler.StatusCode = responseStatusCode;
        AppServerHostStateClient client = CreateClient();

        HttpStatusCode statusCode = await client.PublishAsync(new(CreatedTime, 0, 0, 0), CancellationToken.None);

        Assert.Equal(responseStatusCode, statusCode);
    }

    [Fact]
    public async Task PublishAsync_ReadsMeshInitUriReloadedAtSpecialization()
    {
        IConfigurationRoot configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        AppServerHostStateClient client = CreateClient(configuration);
        await client.PublishAsync(new(CreatedTime, 0, 0, 0), CancellationToken.None);
        Uri? beforeSpecialization = _handler.RequestUri;

        configuration["MESH_INIT_URI"] = "http://appserver:7070";
        await client.PublishAsync(new(CreatedTime, 0, 1, 1), CancellationToken.None);

        Assert.Equal(new Uri("http://localhost:6060/admin/infra/host/state"), beforeSpecialization);
        Assert.Equal(new Uri("http://appserver:7070/admin/infra/host/state"), _handler.RequestUri);
    }

    [Fact]
    public void AddAppServerHostStatePublisher_BoundsEachRequestToFiveSeconds()
    {
        using ServiceProvider services = new ServiceCollection().AddAppServerHostStatePublisher().BuildServiceProvider();

        using HttpClient client = services.GetRequiredService<IHttpClientFactory>().CreateClient(AppServerHostStateClient.HttpClientName);

        Assert.Equal(TimeSpan.FromSeconds(5), client.Timeout);
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

        public HttpMethod? Method { get; private set; }

        public Uri? RequestUri { get; private set; }

        public string? ContentType { get; private set; }

        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Method = request.Method;
            RequestUri = request.RequestUri;
            ContentType = request.Content?.Headers.ContentType?.MediaType;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(StatusCode);
        }
    }
}
