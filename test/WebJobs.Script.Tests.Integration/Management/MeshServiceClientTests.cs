﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.WebHost.Management;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
using Newtonsoft.Json;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration.Management
{
    public class MeshServiceClientTests
    {
        private const string MeshInitUri = "http://localhost:8954/";
        private readonly IMeshServiceClient _meshServiceClient;
        private readonly Mock<HttpMessageHandler> _handlerMock;
        private readonly TestEnvironment _environment;

        public MeshServiceClientTests()
        {
            _handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
            _environment = new TestEnvironment();
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.MeshInitURI, MeshInitUri);
            _meshServiceClient = new MeshServiceClient(new HttpClient(_handlerMock.Object), _environment,
                NullLogger<MeshServiceClient>.Instance);
        }

        private static bool IsMountCifsRequest(HttpRequestMessage request, string targetPath)
        {
            var formData = request.Content.ReadAsFormDataAsync().Result;
            return string.Equals(MeshInitUri, request.RequestUri.AbsoluteUri) &&
                   string.Equals("cifs", formData["operation"]) &&
                   string.Equals(targetPath, formData["targetPath"]);
        }

        private static bool IsMountFuseRequest(HttpRequestMessage request, string filePath, string targetPath)
        {
            var formData = request.Content.ReadAsFormDataAsync().Result;
            return string.Equals(MeshInitUri, request.RequestUri.AbsoluteUri) &&
                   string.Equals("squashfs", formData["operation"]) &&
                   string.Equals(filePath, formData["filePath"]) &&
                   string.Equals(targetPath, formData["targetPath"]);
        }

        private static bool IsPublishExecutionStatusRequest(HttpRequestMessage request, params ContainerFunctionExecutionActivity[] expectedActivities)
        {
            var formData = request.Content.ReadAsFormDataAsync().Result;
            if (string.Equals(MeshInitUri, request.RequestUri.AbsoluteUri) &&
                string.Equals("add-fes", formData["operation"]))
            {
                var activityContent = formData["content"];
                var activities = JsonConvert.DeserializeObject<IEnumerable<ContainerFunctionExecutionActivity>>(activityContent);
                return activities.All(expectedActivities.Contains);
            }

            return false;
        }

        [Fact]
        public async Task MountsCifsShare()
        {
            _handlerMock.Protected().Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()).ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK
            });

            var connectionString =
                "DefaultEndpointsProtocol=https;AccountName=storageaccount;AccountKey=whXtW6WP8QTh84TT5wdjgzeFTj7Vc1aOiCVjTXohpE+jALoKOQ9nlQpj5C5zpgseVJxEVbaAhptP5j5DpaLgtA==";

            await _meshServiceClient.MountCifs(connectionString, "sharename", "/data");

            await Task.Delay(500);

            _handlerMock.Protected().Verify<Task<HttpResponseMessage>>("SendAsync", Times.Once(),
                ItExpr.Is<HttpRequestMessage>(r => IsMountCifsRequest(r, "/data")),
                ItExpr.IsAny<CancellationToken>());
        }

        [Fact]
        public async Task MountsFuseShare()
        {
            _handlerMock.Protected().Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()).ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK
            });

            const string filePath = "https://storage.blob.core.windows.net/functions/func-app.zip";
            const string targetPath = "/home/site/wwwroot";
            await _meshServiceClient.MountFuse("squashfs", filePath, targetPath);

            await Task.Delay(500);

            _handlerMock.Protected().Verify<Task<HttpResponseMessage>>("SendAsync", Times.Once(),
                ItExpr.Is<HttpRequestMessage>(r => IsMountFuseRequest(r, filePath, targetPath)),
                ItExpr.IsAny<CancellationToken>());
        }

        [Fact]
        public async Task PublishesFunctionExecutionStatus()
        {
            _handlerMock.Protected().Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()).ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK
            });

            var activity = new ContainerFunctionExecutionActivity(DateTime.UtcNow, "func1", ExecutionStage.InProgress,
                "QueueTrigger", false);

            var activities = new List<ContainerFunctionExecutionActivity> {activity};

            await _meshServiceClient.PublishContainerActivity(activities);

            _handlerMock.Protected().Verify<Task<HttpResponseMessage>>("SendAsync", Times.Once(),
                ItExpr.Is<HttpRequestMessage>(r => IsPublishExecutionStatusRequest(r, activity)),
                ItExpr.IsAny<CancellationToken>());
        }

        [Fact]
        public async Task PublishesAllFunctionExecutionActivities()
        {
            _handlerMock.Protected().Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()).ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK
            });

            var activity1 = new ContainerFunctionExecutionActivity(DateTime.UtcNow, "func1", ExecutionStage.InProgress,
                "QueueTrigger", false);

            var activity2 = new ContainerFunctionExecutionActivity(DateTime.UtcNow, "func2", ExecutionStage.Finished,
                "QueueTrigger", true);

            var activities = new List<ContainerFunctionExecutionActivity> {activity1, activity2};

            await _meshServiceClient.PublishContainerActivity(activities);

            _handlerMock.Protected().Verify<Task<HttpResponseMessage>>("SendAsync", Times.Exactly(1),
                ItExpr.Is<HttpRequestMessage>(r => IsPublishExecutionStatusRequest(r, activity1, activity2)),
                ItExpr.IsAny<CancellationToken>());

        }

        [Fact]
        public async Task PublishesAllFunctionExecutionActivitiesEvenInCaseOfExceptions()
        {
            _handlerMock.Protected().Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()).Throws(new Exception());

            var activity1 = new ContainerFunctionExecutionActivity(DateTime.UtcNow, "func1", ExecutionStage.InProgress,
                "QueueTrigger", false);

            var activity2 = new ContainerFunctionExecutionActivity(DateTime.UtcNow, "func2", ExecutionStage.Finished,
                "QueueTrigger", true);

            var activities = new List<ContainerFunctionExecutionActivity> { activity1, activity2 };

            await _meshServiceClient.PublishContainerActivity(activities);

            // total count = 3 (1 set of activities * 3 retries)
            _handlerMock.Protected().Verify<Task<HttpResponseMessage>>("SendAsync", Times.Exactly(3),
                ItExpr.Is<HttpRequestMessage>(r => IsPublishExecutionStatusRequest(r, activity1, activity2)),
                ItExpr.IsAny<CancellationToken>());

        }
    }
}
