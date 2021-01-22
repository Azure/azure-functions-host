// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.IO.Abstractions;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Extensions.Logging;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Moq.Protected;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class SimpleKubernetesClientTests : IDisposable
    {
        [Theory]
        [InlineData(HttpStatusCode.OK, "{}", 0)]
        [InlineData(HttpStatusCode.OK, "{'data': {}}", 0)]
        [InlineData(HttpStatusCode.OK, "{'data': {'key': 'dmFsdWU='}}", 1)]
        public async Task Get_From_ApiServer_No_Data(HttpStatusCode statusCode, string content, int length)
        {
            var environment = new TestEnvironment();
            environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsKubernetesSecretName, "test");
            environment.SetEnvironmentVariable(EnvironmentSettingNames.KubernetesServiceHost, "127.0.0.1");
            environment.SetEnvironmentVariable(EnvironmentSettingNames.KubernetesServiceHttpsPort, "443");

            var fullFileSystem = new FileSystem();
            var fileSystem = new Mock<IFileSystem>();
            var fileBase = new Mock<FileBase>();
            var directoryBase = new Mock<DirectoryBase>();

            fileSystem.SetupGet(f => f.Path).Returns(fullFileSystem.Path);
            fileSystem.SetupGet(f => f.File).Returns(fileBase.Object);
            fileSystem.SetupGet(f => f.Directory).Returns(directoryBase.Object);
            fileBase.Setup(f => f.Exists("/run/secrets/kubernetes.io/serviceaccount/namespace")).Returns(true);
            fileBase.Setup(f => f.Exists("/run/secrets/kubernetes.io/serviceaccount/token")).Returns(true);
            fileBase.Setup(f => f.Exists("/run/secrets/kubernetes.io/serviceaccount/ca.crt")).Returns(true);

            fileBase
                .Setup(f => f.Open("/run/secrets/kubernetes.io/serviceaccount/token", It.IsAny<FileMode>(), It.IsAny<FileAccess>(), It.IsAny<FileShare>()))
                .Returns(() =>
                {
                    var token = new MemoryStream(Encoding.UTF8.GetBytes("test_token"));
                    token.Position = 0;
                    return token;
                });
            fileBase
                .Setup(f => f.Open("/run/secrets/kubernetes.io/serviceaccount/namespace", It.IsAny<FileMode>(), It.IsAny<FileAccess>(), It.IsAny<FileShare>()))
                .Returns(() =>
                {
                    var ns = new MemoryStream(Encoding.UTF8.GetBytes("namespace"));
                    ns.Position = 0;
                    return ns;
                });

            FileUtility.Instance = fileSystem.Object;

            var loggerFactory = new LoggerFactory();
            var loggerProvider = new TestLoggerProvider();
            loggerFactory.AddProvider(loggerProvider);

            var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
            handlerMock.Protected().Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>()).ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = statusCode,

                Content = new StringContent(content, Encoding.UTF8, "application/json")
            });

            var client = new SimpleKubernetesClient(environment, new HttpClient(handlerMock.Object), loggerFactory.CreateLogger<SimpleKubernetesClient>());
            var secrets = await client.GetSecrets();

            Assert.NotNull(secrets);
            Assert.Equal(secrets.Count, length);
        }

        public void Dispose()
        {
            FileUtility.Instance = null;
        }
    }
}