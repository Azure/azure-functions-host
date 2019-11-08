// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.WebHost.Management;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration.Management
{
    public class MeshInitServiceClientTests
    {
        private const string MeshInitUri = "http://localhost:8954/";
        private readonly IMeshInitServiceClient _meshInitServiceClient;
        private readonly Mock<HttpMessageHandler> _handlerMock;

        public MeshInitServiceClientTests()
        {
            _handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
            var environment = new TestEnvironment();
            environment.SetEnvironmentVariable(EnvironmentSettingNames.MeshInitURI, MeshInitUri);
            _meshInitServiceClient = new MeshInitServiceClient(new HttpClient(_handlerMock.Object), environment, NullLogger<MeshInitServiceClient>.Instance);
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

            await _meshInitServiceClient.MountCifs(connectionString, "sharename", "/data");

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
            await _meshInitServiceClient.MountFuse("squashfs", filePath, targetPath);

            await Task.Delay(500);

            _handlerMock.Protected().Verify<Task<HttpResponseMessage>>("SendAsync", Times.Once(),
                ItExpr.Is<HttpRequestMessage>(r => IsMountFuseRequest(r, filePath, targetPath)),
                ItExpr.IsAny<CancellationToken>());
        }
    }
}
