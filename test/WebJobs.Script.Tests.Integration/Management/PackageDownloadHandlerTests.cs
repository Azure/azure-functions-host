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
        private const string HomePath = "home-path";
        private const string PackagePath = "source-package-path";
        private const string RunFromPackageOne = "1";
        private readonly Mock<IBashCommandHandler> _bashCmdHandlerMock;
        private readonly Mock<IManagedIdentityTokenProvider> _managedIdentityTokenProvider;
        private readonly TestEnvironment _environment;
        private readonly ILogger<PackageDownloadHandler> _logger;
        private readonly TestMetricsLogger _metricsLogger;
        private readonly Mock<IFileSystem> _fileSystem;


        private IHttpClientFactory _httpClientFactory;

        public PackageDownloadHandlerTests()
        {
            _httpClientFactory = TestHelpers.CreateHttpClientFactory();
            _bashCmdHandlerMock = new Mock<IBashCommandHandler>(MockBehavior.Strict);
            _managedIdentityTokenProvider = new Mock<IManagedIdentityTokenProvider>(MockBehavior.Strict);
            _environment = new TestEnvironment();
            _logger = NullLogger<PackageDownloadHandler>.Instance;
            _metricsLogger = new TestMetricsLogger();
            _fileSystem = GetFileSystem();
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("abcd")]
        [InlineData("http:/abcd")]
        public async Task ThrowsExceptionForInvalidUrls(string url)
        {
            var downloader = new PackageDownloadHandler(_httpClientFactory, _managedIdentityTokenProvider.Object,
                _bashCmdHandlerMock.Object, _environment, _fileSystem.Object, _logger, _metricsLogger);
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

            _httpClientFactory = TestHelpers.CreateHttpClientFactory(handlerMock.Object);

            var downloader = new PackageDownloadHandler(_httpClientFactory, _managedIdentityTokenProvider.Object,
                _bashCmdHandlerMock.Object, _environment, _fileSystem.Object, _logger, _metricsLogger);

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

            _httpClientFactory = TestHelpers.CreateHttpClientFactory(handlerMock.Object);

            _managedIdentityTokenProvider.Setup(p => p.GetManagedIdentityToken(UriWithNoSasToken)).Returns(Task.FromResult(BearerToken));

            var downloader = new PackageDownloadHandler(_httpClientFactory, _managedIdentityTokenProvider.Object,
                _bashCmdHandlerMock.Object, _environment, _fileSystem.Object, _logger, _metricsLogger);

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

            _httpClientFactory = TestHelpers.CreateHttpClientFactory(handlerMock.Object);

            if (expectedFetchesManagedIdentityToken)
            {
                _managedIdentityTokenProvider.Setup(p => p.GetManagedIdentityToken(url))
                    .Returns(Task.FromResult(BearerToken));
            }

            var downloader = new PackageDownloadHandler(_httpClientFactory, _managedIdentityTokenProvider.Object,
                _bashCmdHandlerMock.Object, _environment, _fileSystem.Object, _logger, _metricsLogger);

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

        [Fact]
        public async Task ShouldThrowExceptionIfSourcePackageFolderDoesNotExist()
        {
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteHomePath, HomePath);
            
            var fileSystem = GetFileSystem();
            fileSystem.Setup(x => x.Directory.Exists(_environment.GetSitePackagesPath())).Returns(false);

            var downloader = new PackageDownloadHandler(_httpClientFactory, _managedIdentityTokenProvider.Object,
                _bashCmdHandlerMock.Object, _environment, fileSystem.Object, _logger, _metricsLogger);
            var runFromPackageContext = new RunFromPackageContext(EnvironmentSettingNames.AzureWebsiteRunFromPackage, RunFromPackageOne, 0, false);
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await downloader.Download(runFromPackageContext));
        }

        [Fact]
        public async Task ShouldThrowExceptionIfSourcePackageNameFileDoesNotExist()
        {
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteHomePath, HomePath);

            var fileSystem = GetFileSystem();
            fileSystem.Setup(x => x.Directory.Exists(_environment.GetSitePackagesPath())).Returns(true);
            fileSystem.Setup(x => x.File.Exists(_environment.GetSitePackageNameTxtPath())).Returns(false);

            var downloader = new PackageDownloadHandler(_httpClientFactory, _managedIdentityTokenProvider.Object,
                _bashCmdHandlerMock.Object, _environment, fileSystem.Object, _logger, _metricsLogger);
            var runFromPackageContext = new RunFromPackageContext(EnvironmentSettingNames.AzureWebsiteRunFromPackage, RunFromPackageOne, 0, false);
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await downloader.Download(runFromPackageContext));
        }

        [Fact]
        public async Task ShouldThrowExceptionIfSourcePackageNameFileIsEmpty()
        {
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteHomePath, HomePath);

            var fileSystem = GetFileSystem();
            fileSystem.Setup(x => x.Directory.Exists(_environment.GetSitePackagesPath())).Returns(true);
            fileSystem.Setup(x => x.File.Exists(_environment.GetSitePackageNameTxtPath())).Returns(true);
            fileSystem.Setup(x => x.File.ReadAllText(_environment.GetSitePackageNameTxtPath())).Returns(string.Empty);

            var downloader = new PackageDownloadHandler(_httpClientFactory, _managedIdentityTokenProvider.Object,
                _bashCmdHandlerMock.Object, _environment, fileSystem.Object, _logger, _metricsLogger);
            var runFromPackageContext = new RunFromPackageContext(EnvironmentSettingNames.AzureWebsiteRunFromPackage, RunFromPackageOne, 0, false);
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await downloader.Download(runFromPackageContext));
        }

        [Fact]
        public async Task ShouldThrowExceptionIfSourcePakgeFileDoesNotExist()
        {
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteHomePath, HomePath);

            var fileSystem = GetFileSystem();
            fileSystem.Setup(x => x.Directory.Exists(_environment.GetSitePackagesPath())).Returns(true);
            fileSystem.Setup(x => x.File.Exists(_environment.GetSitePackageNameTxtPath())).Returns(true);
            fileSystem.Setup(x => x.File.ReadAllText(_environment.GetSitePackageNameTxtPath())).Returns(ZipFileName);
            fileSystem.Setup(x => x.Path.Combine(_environment.GetSitePackagesPath(), ZipFileName)).Returns(PackagePath);
            fileSystem.Setup(x => x.File.Exists(PackagePath)).Returns(false);

            var downloader = new PackageDownloadHandler(_httpClientFactory, _managedIdentityTokenProvider.Object,
                _bashCmdHandlerMock.Object, _environment, fileSystem.Object, _logger, _metricsLogger);
            var runFromPackageContext = new RunFromPackageContext(EnvironmentSettingNames.AzureWebsiteRunFromPackage, RunFromPackageOne, 0, false);
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await downloader.Download(runFromPackageContext));
        }

        [Fact]
        public async Task ShouldCopySourcePakgeToTempLocation()
        {
            var tempPath = "temp-path";
            var destinationPath = "dest-path";
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteHomePath, HomePath);

            var fileSystem = GetFileSystem();
            fileSystem.Setup(x => x.Directory.Exists(_environment.GetSitePackagesPath())).Returns(true);
            fileSystem.Setup(x => x.File.Exists(_environment.GetSitePackageNameTxtPath())).Returns(true);
            fileSystem.Setup(x => x.File.ReadAllText(_environment.GetSitePackageNameTxtPath())).Returns(ZipFileName);
            fileSystem.Setup(x => x.Path.Combine(_environment.GetSitePackagesPath(), ZipFileName)).Returns(PackagePath);
            fileSystem.Setup(x => x.File.Exists(PackagePath)).Returns(true);
            var fileInfo = new Mock<FileInfoBase>(MockBehavior.Strict);
            fileInfo.SetupGet(f => f.Length).Returns(1);
            fileSystem.Setup(f => f.FileInfo.FromFileName(PackagePath)).Returns(fileInfo.Object);

            fileSystem.Setup(x => x.Path.GetTempPath()).Returns(tempPath);
            fileSystem.Setup(x => x.Path.GetFileName(ZipFileName)).Returns(ZipFileName);
            fileSystem.Setup(x => x.Path.Combine(tempPath, ZipFileName)).Returns(destinationPath);
            fileSystem.Setup(x => x.File.Copy(PackagePath, destinationPath, true)).Verifiable();


            var downloader = new PackageDownloadHandler(_httpClientFactory, _managedIdentityTokenProvider.Object,
                _bashCmdHandlerMock.Object, _environment, fileSystem.Object, _logger, _metricsLogger);
            var runFromPackageContext = new RunFromPackageContext(EnvironmentSettingNames.AzureWebsiteRunFromPackage, RunFromPackageOne, 0, false);
            var returnedDestPath = await downloader.Download(runFromPackageContext);
            fileSystem.Verify(x => x.File.Copy(PackagePath, destinationPath, true), Times.Once);
            Assert.Equal(destinationPath, returnedDestPath);
            
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

            var fileSystem = GetFileSystem();
            FileUtility.Instance = fileSystem.Object;
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

                _httpClientFactory = TestHelpers.CreateHttpClientFactory(handlerMock.Object);
            }
            else
            {
                _bashCmdHandlerMock.Setup(b => b.RunBashCommand(It.Is<string>(s => s.StartsWith(PackageDownloadHandler.Aria2CExecutable)),
                    expectedMetricName)).Returns(("", "", 0));
            }

            var runFromPackageContext = new RunFromPackageContext(EnvironmentSettingNames.AzureWebsiteRunFromPackage, url, fileSize, isWarmupRequest);

            var packageDownloadHandler = new PackageDownloadHandler(_httpClientFactory, _managedIdentityTokenProvider.Object,
                _bashCmdHandlerMock.Object, _environment, fileSystem.Object, _logger, _metricsLogger);

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
            var fileSystem = GetFileSystem();
            FileUtility.Instance = fileSystem.Object;
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

            _httpClientFactory = TestHelpers.CreateHttpClientFactory(handlerMock.Object);

            var packageDownloadHandler = new PackageDownloadHandler(_httpClientFactory, _managedIdentityTokenProvider.Object,
                _bashCmdHandlerMock.Object, _environment, fileSystem.Object, _logger, _metricsLogger);

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
