// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.IO.Abstractions;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.WebHost.Management.LinuxSpecialization;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Moq.Protected;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration.Management
{
    [Trait(TestTraits.Category, TestTraits.EndToEnd)]
    [Trait(TestTraits.Group, TestTraits.ContainerInstanceTests)]
    public class PackageDownloadHandlerTests : IDisposable
    {
        private const string BearerToken = "bearer-token";
        private const string UriWithSasToken = "https://storageaccount.blob.core.windows.net/funcs/hello.zip?sv=secret";
        private const string UriWithNoSasToken = "https://storageaccount.blob.core.windows.net/funcs/hello.zip";
        private const string ZipFileName = "zip-file.zip";
        private readonly Mock<IBashCommandHandler> _bashCmdHandlerMock;
        private readonly Mock<IManagedIdentityTokenProvider> _managedIdentityTokenProvider;
        private readonly ILogger<PackageDownloadHandler> _logger;
        private readonly TestMetricsLogger _metricsLogger;

        private HttpClient _httpClient;

        public PackageDownloadHandlerTests()
        {
            _httpClient = new Mock<HttpClient>().Object;
            _bashCmdHandlerMock = new Mock<IBashCommandHandler>(MockBehavior.Strict);
            _managedIdentityTokenProvider = new Mock<IManagedIdentityTokenProvider>(MockBehavior.Strict);
            _logger = NullLogger<PackageDownloadHandler>.Instance;
            _metricsLogger = new TestMetricsLogger();
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("abcd")]
        [InlineData("http:/abcd")]
        public async Task ThrowsExceptionForInvalidUrls(string url)
        {
            var downloader = new PackageDownloadHandler(_httpClient, _managedIdentityTokenProvider.Object,
                _bashCmdHandlerMock.Object, _logger, _metricsLogger);
            var runFromPackageContext = new RunFromPackageContext(EnvironmentSettingNames.AzureWebsiteRunFromPackage, url, null, true);
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await downloader.Download(runFromPackageContext));
        }

        [Theory]
        [InlineData(true, UriWithNoSasToken, null, false, false)] // warmup requests will never use token
        [InlineData(true, UriWithSasToken, null, false, false)]
        [InlineData(true, UriWithNoSasToken, 0, false, false)]
        [InlineData(true, UriWithSasToken, 0, false, false)]
        [InlineData(true, UriWithNoSasToken, PackageDownloadHandler.AriaDownloadThreshold - 1, false, false)]
        [InlineData(true, UriWithSasToken, PackageDownloadHandler.AriaDownloadThreshold - 1, false, false)]
        [InlineData(true, UriWithNoSasToken, PackageDownloadHandler.AriaDownloadThreshold + 1, false, false)]
        [InlineData(true, UriWithSasToken, PackageDownloadHandler.AriaDownloadThreshold + 1, false, false)]
        [InlineData(false, UriWithNoSasToken, null, false, true)]
        [InlineData(false, UriWithSasToken, null, false, false)] // url has sas token. so no token.
        [InlineData(false, UriWithNoSasToken, PackageDownloadHandler.AriaDownloadThreshold - 1, false, true)]
        [InlineData(false, UriWithSasToken, PackageDownloadHandler.AriaDownloadThreshold - 1, false, false)]
        [InlineData(false, UriWithNoSasToken, PackageDownloadHandler.AriaDownloadThreshold + 1, false, true)]
        [InlineData(false, UriWithSasToken, PackageDownloadHandler.AriaDownloadThreshold + 1, true, false)]
        public async Task DownloadsPackage(bool isWarmupRequest, string url, long? fileSize, bool expectedUsesAriaDownload, bool expectedDownloadUsingManagedIdentityToken)
        {
            var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);

            handlerMock.Protected().Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(s => MatchesVerb(s, HttpMethod.Head)),
                ItExpr.IsAny<CancellationToken>()).ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.Unauthorized,
                Content = new ReadOnlyMemoryContent(ReadOnlyMemory<byte>.Empty)
            });

            if (expectedDownloadUsingManagedIdentityToken)
            {
                handlerMock.Protected().Setup<Task<HttpResponseMessage>>("SendAsync",
                    ItExpr.Is<HttpRequestMessage>(s => MatchesVerb(s, HttpMethod.Get) && HasBearerToken(s) && MatchesTargetUri(s, url)),
                    ItExpr.IsAny<CancellationToken>()).ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new ReadOnlyMemoryContent(ReadOnlyMemory<byte>.Empty)
                });

                _managedIdentityTokenProvider.Setup(p => p.GetManagedIdentityToken(url)).Returns(Task.FromResult(BearerToken));
            }
            else
            {
                if (expectedUsesAriaDownload)
                {
                    _bashCmdHandlerMock.Setup(b =>
                        b.RunBashCommand(
                            It.Is<string>(s =>
                                s.StartsWith(PackageDownloadHandler.Aria2CExecutable) && s.Contains(url)),
                            MetricEventNames.LinuxContainerSpecializationZipDownload)).Returns(("", "", 0));
                }
                else
                {
                    handlerMock.Protected().Setup<Task<HttpResponseMessage>>("SendAsync",
                        ItExpr.Is<HttpRequestMessage>(s => MatchesVerb(s, HttpMethod.Get) && !HasBearerToken(s) && MatchesTargetUri(s, url)),
                        ItExpr.IsAny<CancellationToken>()).ReturnsAsync(new HttpResponseMessage
                    {
                        StatusCode = HttpStatusCode.OK,
                        Content = new ReadOnlyMemoryContent(ReadOnlyMemory<byte>.Empty)
                    });
                }
            }

            _httpClient = new HttpClient(handlerMock.Object);

            var downloader = new PackageDownloadHandler(_httpClient, _managedIdentityTokenProvider.Object,
                _bashCmdHandlerMock.Object, _logger, _metricsLogger);

            var runFromPackageContext = new RunFromPackageContext(EnvironmentSettingNames.AzureWebsiteRunFromPackage, url, fileSize, isWarmupRequest);
            await downloader.Download(runFromPackageContext);

            if (expectedDownloadUsingManagedIdentityToken)
            {
                handlerMock.Protected().Verify<Task<HttpResponseMessage>>("SendAsync", Times.Once(),
                    ItExpr.Is<HttpRequestMessage>(s => MatchesVerb(s, HttpMethod.Head)),
                    ItExpr.IsAny<CancellationToken>());

                handlerMock.Protected().Verify<Task<HttpResponseMessage>>("SendAsync", Times.Once(),
                    ItExpr.Is<HttpRequestMessage>(s => MatchesVerb(s, HttpMethod.Get) && HasBearerToken(s) && MatchesTargetUri(s, url)),
                    ItExpr.IsAny<CancellationToken>());

                _managedIdentityTokenProvider.Verify(p => p.GetManagedIdentityToken(url), Times.Once);
            }
            else
            {
                handlerMock.Protected().Verify<Task<HttpResponseMessage>>("SendAsync", Times.Never(),
                    ItExpr.Is<HttpRequestMessage>(s => MatchesVerb(s, HttpMethod.Head)),
                    ItExpr.IsAny<CancellationToken>());

                if (expectedUsesAriaDownload)
                {
                    _bashCmdHandlerMock.Verify(b =>
                        b.RunBashCommand(
                            It.Is<string>(s =>
                                s.StartsWith(PackageDownloadHandler.Aria2CExecutable) && s.Contains(url)),
                            MetricEventNames.LinuxContainerSpecializationZipDownload), Times.Once);
                }
                else
                {
                    handlerMock.Protected().Verify<Task<HttpResponseMessage>>("SendAsync", Times.Never(),
                        ItExpr.Is<HttpRequestMessage>(s => MatchesVerb(s, HttpMethod.Get) && HasBearerToken(s) && MatchesTargetUri(s, url)),
                        ItExpr.IsAny<CancellationToken>());

                    _bashCmdHandlerMock.Verify(b =>
                        b.RunBashCommand(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
                }

                _managedIdentityTokenProvider.Verify(p => p.GetManagedIdentityToken(It.IsAny<string>()), Times.Never);
            }
        }

        [Fact]
        public async Task DownloadsFileUsingManagedIdentity()
        {
            var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);

            handlerMock.Protected().Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(s => MatchesVerb(s, HttpMethod.Head)),
                ItExpr.IsAny<CancellationToken>()).ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.Unauthorized,
                Content = new ReadOnlyMemoryContent(ReadOnlyMemory<byte>.Empty)
            });


            handlerMock.Protected().Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(s => MatchesVerb(s, HttpMethod.Get) && HasBearerToken(s) && MatchesTargetUri(s, UriWithNoSasToken)),
                ItExpr.IsAny<CancellationToken>()).ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new ReadOnlyMemoryContent(ReadOnlyMemory<byte>.Empty)
            });

            _httpClient = new HttpClient(handlerMock.Object);

            _managedIdentityTokenProvider.Setup(p => p.GetManagedIdentityToken(UriWithNoSasToken)).Returns(Task.FromResult(BearerToken));

            var downloader = new PackageDownloadHandler(_httpClient, _managedIdentityTokenProvider.Object,
                _bashCmdHandlerMock.Object, _logger, _metricsLogger);

            var runFromPackageContext = new RunFromPackageContext(EnvironmentSettingNames.AzureWebsiteRunFromPackage, UriWithNoSasToken, 0, false);
            await downloader.Download(runFromPackageContext);

            handlerMock.Protected().Verify<Task<HttpResponseMessage>>("SendAsync", Times.Once(),
                ItExpr.Is<HttpRequestMessage>(s => MatchesVerb(s, HttpMethod.Head)),
                ItExpr.IsAny<CancellationToken>());

            handlerMock.Protected().Verify<Task<HttpResponseMessage>>("SendAsync", Times.Once(),
                ItExpr.Is<HttpRequestMessage>(s => MatchesVerb(s, HttpMethod.Get) && HasBearerToken(s) && MatchesTargetUri(s, UriWithNoSasToken)),
                ItExpr.IsAny<CancellationToken>());

            _managedIdentityTokenProvider.Verify(p => p.GetManagedIdentityToken(UriWithNoSasToken), Times.Once);
        }

        [Theory]
        [InlineData(UriWithSasToken, true, false)]
        [InlineData(UriWithSasToken, false, false)]
        [InlineData(UriWithNoSasToken, true, false)]
        [InlineData(UriWithNoSasToken, false, true)]
        public async Task FetchesManagedIdentityToken(string url, bool isPublicUrl, bool expectedFetchesManagedIdentityToken)
        {
            var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);

            // urls with sas tokens are always publicly accessible
            if (url == UriWithSasToken)
            {
                isPublicUrl = true;
            }

            var statusCode = isPublicUrl ? HttpStatusCode.OK : HttpStatusCode.Unauthorized;

            handlerMock.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync",
                    ItExpr.Is<HttpRequestMessage>(s => MatchesVerb(s, HttpMethod.Head)),
                    ItExpr.IsAny<CancellationToken>()).ReturnsAsync(new HttpResponseMessage
                    {StatusCode = statusCode, Content = new ReadOnlyMemoryContent(ReadOnlyMemory<byte>.Empty)});

            if (expectedFetchesManagedIdentityToken)
            {
                handlerMock.Protected().Setup<Task<HttpResponseMessage>>("SendAsync",
                    ItExpr.Is<HttpRequestMessage>(s => MatchesVerb(s, HttpMethod.Get) && HasBearerToken(s) && MatchesTargetUri(s, url)),
                    ItExpr.IsAny<CancellationToken>()).ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new ReadOnlyMemoryContent(ReadOnlyMemory<byte>.Empty)
                });
            }
            else
            {
                handlerMock.Protected().Setup<Task<HttpResponseMessage>>("SendAsync",
                    ItExpr.Is<HttpRequestMessage>(s => MatchesVerb(s, HttpMethod.Get) && !HasBearerToken(s) && MatchesTargetUri(s, url)),
                    ItExpr.IsAny<CancellationToken>()).ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new ReadOnlyMemoryContent(ReadOnlyMemory<byte>.Empty)
                });
            }

            _httpClient = new HttpClient(handlerMock.Object);

            if (expectedFetchesManagedIdentityToken)
            {
                _managedIdentityTokenProvider.Setup(p => p.GetManagedIdentityToken(url))
                    .Returns(Task.FromResult(BearerToken));
            }

            var downloader = new PackageDownloadHandler(_httpClient, _managedIdentityTokenProvider.Object,
                _bashCmdHandlerMock.Object, _logger, _metricsLogger);

            var runFromPackageContext = new RunFromPackageContext(EnvironmentSettingNames.AzureWebsiteRunFromPackage, url, 0, false);
            await downloader.Download(runFromPackageContext);

            if (expectedFetchesManagedIdentityToken)
            {
                _managedIdentityTokenProvider.Verify(p => p.GetManagedIdentityToken(url), Times.Once);
                handlerMock.Protected().Verify<Task<HttpResponseMessage>>("SendAsync", Times.Once(),
                    ItExpr.Is<HttpRequestMessage>(s => MatchesVerb(s, HttpMethod.Get) && HasBearerToken(s) && MatchesTargetUri(s, url)),
                    ItExpr.IsAny<CancellationToken>());
            }
            else
            {
                _managedIdentityTokenProvider.Verify(p => p.GetManagedIdentityToken(It.IsAny<string>()), Times.Never);
                handlerMock.Protected().Verify<Task<HttpResponseMessage>>("SendAsync", Times.Once(),
                    ItExpr.Is<HttpRequestMessage>(s => MatchesVerb(s, HttpMethod.Get) && !HasBearerToken(s) && MatchesTargetUri(s, url)),
                    ItExpr.IsAny<CancellationToken>());
            }
        }

        private static bool MatchesVerb(HttpRequestMessage s, HttpMethod httpMethod)
        {
            return s.Method == httpMethod;
        }

        private static bool HasBearerToken(HttpRequestMessage r)
        {
            return r.Headers.Authorization != null && r.Headers.Authorization.Parameter == BearerToken;
        }

        private static bool MatchesTargetUri(HttpRequestMessage r, string url)
        {
            return r.RequestUri.AbsoluteUri.Equals(url);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task DownloadsLargeZipsUsingAria2c(bool isWarmupRequest)
        {
            var url = $"http://url/{ZipFileName}";

            FileUtility.Instance = GetFileSystem().Object;
            const int fileSize = PackageDownloadHandler.AriaDownloadThreshold + 1;

            var expectedMetricName = isWarmupRequest
                ? MetricEventNames.LinuxContainerSpecializationZipDownloadWarmup
                : MetricEventNames.LinuxContainerSpecializationZipDownload;
            
            var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);

            if (isWarmupRequest)
            {
                handlerMock.Protected().Setup<Task<HttpResponseMessage>>("SendAsync",
                    ItExpr.Is<HttpRequestMessage>(s => MatchesVerb(s, HttpMethod.Get) && MatchesTargetUri(s, url)),
                    ItExpr.IsAny<CancellationToken>()).ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new ReadOnlyMemoryContent(ReadOnlyMemory<byte>.Empty)
                });

                _httpClient = new HttpClient(handlerMock.Object);
            }
            else
            {
                _bashCmdHandlerMock.Setup(b => b.RunBashCommand(It.Is<string>(s => s.StartsWith(PackageDownloadHandler.Aria2CExecutable)),
                    expectedMetricName)).Returns(("", "", 0));
            }

            var runFromPackageContext = new RunFromPackageContext(EnvironmentSettingNames.AzureWebsiteRunFromPackage, url, fileSize, isWarmupRequest);

            var packageDownloadHandler = new PackageDownloadHandler(_httpClient, _managedIdentityTokenProvider.Object,
                _bashCmdHandlerMock.Object, _logger, _metricsLogger);

            var filePath = await packageDownloadHandler.Download(runFromPackageContext);

            Assert.Equal(ZipFileName, Path.GetFileName(filePath), StringComparer.Ordinal);

            if (isWarmupRequest)
            {
                handlerMock.Protected().Verify<Task<HttpResponseMessage>>("SendAsync", Times.Once(),
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>());
            }
            else
            {
                _bashCmdHandlerMock.Verify(b => b.RunBashCommand(It.Is<string>(s => s.StartsWith(PackageDownloadHandler.Aria2CExecutable)),
                    expectedMetricName), Times.Once);
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task DownloadsZipsUsingHttpClient(bool isWarmupRequest)
        {
            FileUtility.Instance = GetFileSystem().Object;
            var url = $"http://url/{ZipFileName}";
            const int fileSize = PackageDownloadHandler.AriaDownloadThreshold - 1;

            var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);

            handlerMock.Protected().Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()).ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new ReadOnlyMemoryContent(ReadOnlyMemory<byte>.Empty)
            });

            _httpClient = new HttpClient(handlerMock.Object);

            var packageDownloadHandler = new PackageDownloadHandler(_httpClient, _managedIdentityTokenProvider.Object,
                _bashCmdHandlerMock.Object, _logger, _metricsLogger);

            var runFromPackageContext = new RunFromPackageContext(EnvironmentSettingNames.AzureWebsiteRunFromPackage, url, fileSize, isWarmupRequest);
            var filePath = await packageDownloadHandler.Download(runFromPackageContext);;

            Assert.Equal(ZipFileName, Path.GetFileName(filePath), StringComparer.Ordinal);

            handlerMock.Protected().Verify<Task<HttpResponseMessage>>("SendAsync", Times.Once(),
                ItExpr.Is<HttpRequestMessage>(r => IsZipDownloadRequest(r, url)),
                ItExpr.IsAny<CancellationToken>());
        }

        public void Dispose()
        {
            FileUtility.Instance = null;
        }

        private static bool IsZipDownloadRequest(HttpRequestMessage httpRequestMessage, string filePath)
        {
            return httpRequestMessage.Method == HttpMethod.Get &&
                   string.Equals(filePath, httpRequestMessage.RequestUri.AbsoluteUri);
        }

        private static Mock<IFileSystem> GetFileSystem()
        {
            var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
            var fileInfo = new Mock<FileInfoBase>(MockBehavior.Strict);
            fileInfo.SetupGet(f => f.Length).Returns(0);
            fileSystem.Setup(f => f.FileInfo.FromFileName(It.IsAny<string>())).Returns(fileInfo.Object);
            return fileSystem;
        }
    }
}
