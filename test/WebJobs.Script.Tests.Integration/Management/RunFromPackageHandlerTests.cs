﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.WebHost.Management;
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
    public class RunFromPackageHandlerTests : IDisposable
    {
        private const string TargetScriptPath = "/home/site/wwwroot";
        private const string MeshInitUri = "http://localhost:6060";
        private const string ZipFileName = "zip-file.zip";
        private const string HomeDirectory = "/home";
        private const int DefaultPackageLength = 100;

        private readonly TestEnvironment _environment;
        private readonly Mock<IMeshServiceClient> _meshServiceClientMock;
        private readonly Mock<IBashCommandHandler> _bashCmdHandlerMock;
        private readonly TestMetricsLogger _metricsLogger;
        private readonly ILogger<RunFromPackageHandler> _logger;

        private RunFromPackageHandler _runFromPackageHandler;
        private HttpClient _httpClient;
        private Mock<IUnZipHandler> _zipHandler;

        public RunFromPackageHandlerTests()
        {
            _environment = new TestEnvironment();

            _meshServiceClientMock = new Mock<IMeshServiceClient>(MockBehavior.Strict);
            _bashCmdHandlerMock = new Mock<IBashCommandHandler>(MockBehavior.Strict);
            _zipHandler = new Mock<IUnZipHandler>(MockBehavior.Strict);
            _metricsLogger = new TestMetricsLogger();

            _httpClient = new Mock<HttpClient>().Object;
            _logger = NullLogger<RunFromPackageHandler>.Instance;

            _runFromPackageHandler = new RunFromPackageHandler(_environment, _httpClient, _meshServiceClientMock.Object,
                _bashCmdHandlerMock.Object, _zipHandler.Object, _metricsLogger, _logger);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task DownloadsZipsUsingHttpClient(bool isWarmupRequest)
        {
            FileUtility.Instance = GetFileSystem().Object;
            var url = $"http://url/{ZipFileName}";
            const int fileSize = RunFromPackageHandler.AriaDownloadThreshold - 1;

            var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);

            handlerMock.Protected().Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()).ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new ReadOnlyMemoryContent(ReadOnlyMemory<byte>.Empty)
            });

            _httpClient = new HttpClient(handlerMock.Object);
            _runFromPackageHandler = new RunFromPackageHandler(_environment, _httpClient, _meshServiceClientMock.Object,
                _bashCmdHandlerMock.Object, _zipHandler.Object, _metricsLogger, _logger);

            var runFromPackageContext = new RunFromPackageContext(EnvironmentSettingNames.AzureWebsiteRunFromPackage, url, fileSize, isWarmupRequest);
            var filePath = await _runFromPackageHandler.Download(runFromPackageContext);

            Assert.Equal(ZipFileName, Path.GetFileName(filePath), StringComparer.Ordinal);

            handlerMock.Protected().Verify<Task<HttpResponseMessage>>("SendAsync", Times.Once(),
                ItExpr.Is<HttpRequestMessage>(r => IsZipDownloadRequest(r, url)),
                ItExpr.IsAny<CancellationToken>());
        }

        private static bool IsZipDownloadRequest(HttpRequestMessage httpRequestMessage, string filePath)
        {
            return httpRequestMessage.Method == HttpMethod.Get &&
                   string.Equals(filePath, httpRequestMessage.RequestUri.AbsoluteUri);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task DownloadsLargeZipsUsingAria2c(bool isWarmupRequest)
        {
            var url = $"http://url/{ZipFileName}";

            FileUtility.Instance = GetFileSystem().Object;
            const int fileSize = RunFromPackageHandler.AriaDownloadThreshold + 1;

            var expectedMetricName = isWarmupRequest
                ? MetricEventNames.LinuxContainerSpecializationZipDownloadWarmup
                : MetricEventNames.LinuxContainerSpecializationZipDownload;

            _bashCmdHandlerMock.Setup(b => b.RunBashCommand(It.Is<string>(s => s.StartsWith(RunFromPackageHandler.Aria2CExecutable)),
                expectedMetricName)).Returns(("", "", 0));

            var runFromPackageContext = new RunFromPackageContext(EnvironmentSettingNames.AzureWebsiteRunFromPackage, url, fileSize, isWarmupRequest);
            var filePath = await _runFromPackageHandler.Download(runFromPackageContext);

            Assert.Equal(ZipFileName, Path.GetFileName(filePath), StringComparer.Ordinal);

            _bashCmdHandlerMock.Verify(b => b.RunBashCommand(It.Is<string>(s => s.StartsWith(RunFromPackageHandler.Aria2CExecutable)),
                expectedMetricName), Times.Once);
        }

        [Theory]
        [InlineData(true, true)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(false, false)]
        public async Task DownloadFailsForInvalidUrl(bool isWarmupRequest, bool isLargeZip)
        {
            var fileSize = isLargeZip
                ? RunFromPackageHandler.AriaDownloadThreshold + 1
                : RunFromPackageHandler.AriaDownloadThreshold - 1;

            const string url = "invalid-url";

            var runFromPackageContext = new RunFromPackageContext(EnvironmentSettingNames.AzureWebsiteRunFromPackage, url, fileSize, isWarmupRequest);
            await Assert.ThrowsAsync<UriFormatException>(async () => await _runFromPackageHandler.Download(runFromPackageContext));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task MountsAzureFileShare(bool mountResult)
        {
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteHomePath, HomeDirectory);

            var hostAssignmentContext = new HostAssignmentContext {Environment = new Dictionary<string, string>()};

            const string connectionString = "connection-string";
            hostAssignmentContext.Environment[EnvironmentSettingNames.AzureFilesConnectionString] = connectionString;
            const string contentShare = "content-share";
            hostAssignmentContext.Environment[EnvironmentSettingNames.AzureFilesContentShare] = contentShare;

            _meshServiceClientMock.Setup(m => m.MountCifs(connectionString, contentShare, HomeDirectory))
                .ReturnsAsync(mountResult);

            var actualMountResult = await _runFromPackageHandler.MountAzureFileShare(hostAssignmentContext);

            Assert.Equal(mountResult, actualMountResult);

            _meshServiceClientMock.Verify(m => m.MountCifs(connectionString, contentShare, HomeDirectory), Times.Once);
        }

        [Fact]
        public async Task MountAzureFileShareReturnsFalseOnException()
        {
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteHomePath, HomeDirectory);

            var hostAssignmentContext = new HostAssignmentContext {Environment = new Dictionary<string, string>()};

            const string connectionString = "connection-string";
            hostAssignmentContext.Environment[EnvironmentSettingNames.AzureFilesConnectionString] = connectionString;

            const string contentShare = "content-share";
            hostAssignmentContext.Environment[EnvironmentSettingNames.AzureFilesContentShare] = contentShare;

            _meshServiceClientMock.Setup(m => m.MountCifs(connectionString, contentShare, HomeDirectory))
                .ThrowsAsync(new Exception("Failure mounting CIFS"));

            var actualMountResult = await _runFromPackageHandler.MountAzureFileShare(hostAssignmentContext);

            Assert.Equal(false, actualMountResult);

            _meshServiceClientMock.Verify(m => m.MountCifs(connectionString, contentShare, HomeDirectory), Times.Once);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task UnsquashesSquashFsFileIfMountDisabled(bool azureFilesMounted)
        {
            var isWarmUpRequest = false;
            var extension = "squashfs";

            _environment.SetEnvironmentVariable(EnvironmentSettingNames.MountEnabled, "0");
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.MeshInitURI, MeshInitUri);

            // httpDownload
            var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);

            handlerMock.Protected().Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()).ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new ReadOnlyMemoryContent(ReadOnlyMemory<byte>.Empty)
            });

            _httpClient = new HttpClient(handlerMock.Object);
            _runFromPackageHandler = new RunFromPackageHandler(_environment, _httpClient, _meshServiceClientMock.Object,
                _bashCmdHandlerMock.Object, _zipHandler.Object, _metricsLogger, _logger);

            if (azureFilesMounted)
            {
                _bashCmdHandlerMock
                    .Setup(b => b.RunBashCommand(
                        It.Is<string>(s => s.StartsWith($"{RunFromPackageHandler.UnsquashFSExecutable} -f -d '{EnvironmentSettingNames.DefaultLocalSitePackagesPath}'")),
                        MetricEventNames.LinuxContainerSpecializationUnsquash)).Returns((string.Empty, string.Empty, 0));

                _meshServiceClientMock
                    .Setup(m => m.CreateBindMount(EnvironmentSettingNames.DefaultLocalSitePackagesPath, TargetScriptPath))
                    .Returns(Task.FromResult(true));
            }
            else
            {
                _bashCmdHandlerMock
                    .Setup(b => b.RunBashCommand(
                        It.Is<string>(s => s.StartsWith($"{RunFromPackageHandler.UnsquashFSExecutable} -f -d '{TargetScriptPath}'")),
                        MetricEventNames.LinuxContainerSpecializationUnsquash)).Returns((string.Empty, string.Empty, 0));
            }

            var url = $"http://url/zip-file.{extension}";
            var runFromPackageContext = new RunFromPackageContext(EnvironmentSettingNames.AzureWebsiteRunFromPackage,
                url, DefaultPackageLength, isWarmUpRequest);

            var applyBlobContextResult = await _runFromPackageHandler.ApplyBlobPackageContext(runFromPackageContext, TargetScriptPath, azureFilesMounted, true);
            Assert.True(applyBlobContextResult);

            handlerMock.Protected().Verify<Task<HttpResponseMessage>>("SendAsync", Times.Once(),
                ItExpr.Is<HttpRequestMessage>(r => IsZipDownloadRequest(r, url)),
                ItExpr.IsAny<CancellationToken>());

            if (azureFilesMounted)
            {
                _bashCmdHandlerMock
                    .Verify(b => b.RunBashCommand(
                        It.Is<string>(s =>
                            s.StartsWith(
                                $"{RunFromPackageHandler.UnsquashFSExecutable} -f -d '{EnvironmentSettingNames.DefaultLocalSitePackagesPath}'")),
                        MetricEventNames.LinuxContainerSpecializationUnsquash), Times.Once);

                _meshServiceClientMock
                    .Verify(m => m.CreateBindMount(EnvironmentSettingNames.DefaultLocalSitePackagesPath, TargetScriptPath),
                        Times.Once);
            }
            else
            {
                _bashCmdHandlerMock
                    .Verify(b => b.RunBashCommand(
                        It.Is<string>(s =>
                            s.StartsWith($"{RunFromPackageHandler.UnsquashFSExecutable} -f -d '{TargetScriptPath}'")),
                        MetricEventNames.LinuxContainerSpecializationUnsquash), Times.Once);
            }
        }

        [Theory]
        [InlineData(true, "1")]
        [InlineData(false, null)]
        public async Task MountsSquashFsFileIfMountEnabled(bool azureFilesMounted, string isMountEnabled)
        {
            var isWarmUpRequest = false;
            var extension = "squashfs";

            _environment.SetEnvironmentVariable(EnvironmentSettingNames.MountEnabled, isMountEnabled);
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.MeshInitURI, MeshInitUri);

            // httpDownload
            var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);

            handlerMock.Protected().Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()).ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new ReadOnlyMemoryContent(ReadOnlyMemory<byte>.Empty)
                });

            _httpClient = new HttpClient(handlerMock.Object);
            _runFromPackageHandler = new RunFromPackageHandler(_environment, _httpClient, _meshServiceClientMock.Object,
                _bashCmdHandlerMock.Object, _zipHandler.Object, _metricsLogger, _logger);

            var url = $"http://url/zip-file.{extension}";
            var runFromPackageContext = new RunFromPackageContext(EnvironmentSettingNames.AzureWebsiteRunFromPackage,
                url, DefaultPackageLength, isWarmUpRequest);

            _meshServiceClientMock.Setup(m => m.MountFuse(MeshServiceClient.SquashFsOperation, It.Is<string>(s => url.EndsWith(Path.GetFileName(s))), TargetScriptPath))
                .Returns(Task.FromResult(true));

            var applyBlobContextResult = await _runFromPackageHandler.ApplyBlobPackageContext(runFromPackageContext, TargetScriptPath, azureFilesMounted, true);
            Assert.True(applyBlobContextResult);

            handlerMock.Protected().Verify<Task<HttpResponseMessage>>("SendAsync", Times.Once(),
                ItExpr.Is<HttpRequestMessage>(r => IsZipDownloadRequest(r, url)),
                ItExpr.IsAny<CancellationToken>());

            _meshServiceClientMock.Verify(m => m.MountFuse(MeshServiceClient.SquashFsOperation, It.Is<string>(s => url.EndsWith(Path.GetFileName(s))), TargetScriptPath), Times.Once);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task MountsZipFileIfMountEnabled(bool azureFilesMounted)
        {
            var isWarmUpRequest = false;
            var extension = "zip";

            _environment.SetEnvironmentVariable(EnvironmentSettingNames.MountEnabled, "1");
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.MeshInitURI, MeshInitUri);

            // httpDownload
            var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);

            handlerMock.Protected().Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()).ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new ReadOnlyMemoryContent(ReadOnlyMemory<byte>.Empty)
                });

            _httpClient = new HttpClient(handlerMock.Object);
            _runFromPackageHandler = new RunFromPackageHandler(_environment, _httpClient, _meshServiceClientMock.Object,
                _bashCmdHandlerMock.Object, _zipHandler.Object, _metricsLogger, _logger);

            var url = $"http://url/zip-file.{extension}";
            var runFromPackageContext = new RunFromPackageContext(EnvironmentSettingNames.AzureWebsiteRunFromPackage,
                url, DefaultPackageLength, isWarmUpRequest);

            _meshServiceClientMock
                .Setup(m => m.MountFuse(MeshServiceClient.ZipOperation,
                    It.Is<string>(s => url.EndsWith(Path.GetFileName(s))), TargetScriptPath))
                .Returns(Task.FromResult(true));

            var applyBlobContextResult = await _runFromPackageHandler.ApplyBlobPackageContext(runFromPackageContext, TargetScriptPath, azureFilesMounted, true);
            Assert.True(applyBlobContextResult);

            handlerMock.Protected().Verify<Task<HttpResponseMessage>>("SendAsync", Times.Once(),
                ItExpr.Is<HttpRequestMessage>(r => IsZipDownloadRequest(r, url)),
                ItExpr.IsAny<CancellationToken>());

            _meshServiceClientMock
                .Setup(m => m.MountFuse(MeshServiceClient.ZipOperation,
                    It.Is<string>(s => url.EndsWith(Path.GetFileName(s))), TargetScriptPath))
                .Returns(Task.FromResult(true));
        }

        [Theory]
        [InlineData(true, "0")]
        [InlineData(false, null)]
        public async Task MountsZipFileIfMountDisabled(bool azureFilesMounted, string mountEnabled)
        {
            var isWarmUpRequest = false;
            var extension = "zip";

            _environment.SetEnvironmentVariable(EnvironmentSettingNames.MountEnabled, mountEnabled);
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.MeshInitURI, MeshInitUri);

            // httpDownload
            var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);

            handlerMock.Protected().Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()).ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new ReadOnlyMemoryContent(ReadOnlyMemory<byte>.Empty)
                });

            _httpClient = new HttpClient(handlerMock.Object);
            _runFromPackageHandler = new RunFromPackageHandler(_environment, _httpClient, _meshServiceClientMock.Object,
                _bashCmdHandlerMock.Object, _zipHandler.Object, _metricsLogger, _logger);

            var url = $"http://url/zip-file.{extension}";
            var runFromPackageContext = new RunFromPackageContext(EnvironmentSettingNames.AzureWebsiteRunFromPackage,
                url, DefaultPackageLength, isWarmUpRequest);

            if (azureFilesMounted)
            {
                _zipHandler
                    .Setup(b => b.UnzipPackage(It.Is<string>(s => url.EndsWith(Path.GetFileName(s))), EnvironmentSettingNames.DefaultLocalSitePackagesPath));

                _meshServiceClientMock
                    .Setup(m => m.CreateBindMount(EnvironmentSettingNames.DefaultLocalSitePackagesPath,
                        TargetScriptPath)).Returns(Task.FromResult(true));
            }
            else
            {
                _zipHandler.Setup(b =>
                    b.UnzipPackage(It.Is<string>(s => url.EndsWith(Path.GetFileName(s))), TargetScriptPath));
            }

            var applyBlobContextResult = await _runFromPackageHandler.ApplyBlobPackageContext(runFromPackageContext, TargetScriptPath, azureFilesMounted, true);
            Assert.True(applyBlobContextResult);

            handlerMock.Protected().Verify<Task<HttpResponseMessage>>("SendAsync", Times.Once(),
                ItExpr.Is<HttpRequestMessage>(r => IsZipDownloadRequest(r, url)),
                ItExpr.IsAny<CancellationToken>());

            if (azureFilesMounted)
            {
                _zipHandler
                    .Verify(
                        b => b.UnzipPackage(It.Is<string>(s => url.EndsWith(Path.GetFileName(s))),
                            EnvironmentSettingNames.DefaultLocalSitePackagesPath), Times.Once);

                _meshServiceClientMock
                    .Verify(m => m.CreateBindMount(EnvironmentSettingNames.DefaultLocalSitePackagesPath,
                        TargetScriptPath), Times.Once);
            }
            else
            {
                _zipHandler.Verify(b =>
                    b.UnzipPackage(It.Is<string>(s => url.EndsWith(Path.GetFileName(s))), TargetScriptPath), Times.Once);
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task ApplyContextHandlesFailures(bool throwOnFailure)
        {
            if (throwOnFailure)
            {
                await Assert.ThrowsAsync<NullReferenceException>(async () =>
                    await _runFromPackageHandler.ApplyBlobPackageContext(null, string.Empty, true, throwOnFailure));
            }
            else
            {
                Assert.False(await _runFromPackageHandler.ApplyBlobPackageContext(null, string.Empty, true, throwOnFailure));
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task MountsZipFileIfMountEnabledUsingFileCommand(bool azureFilesMounted)
        {
            var isWarmUpRequest = false;
            var extension = "unknown";

            _environment.SetEnvironmentVariable(EnvironmentSettingNames.MountEnabled, "1");
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.MeshInitURI, MeshInitUri);

            // httpDownload
            var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);

            handlerMock.Protected().Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()).ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new ReadOnlyMemoryContent(ReadOnlyMemory<byte>.Empty)
                });

            _httpClient = new HttpClient(handlerMock.Object);
            _runFromPackageHandler = new RunFromPackageHandler(_environment, _httpClient, _meshServiceClientMock.Object,
                _bashCmdHandlerMock.Object, _zipHandler.Object, _metricsLogger, _logger);

            var url = $"http://url/zip-file.{extension}";
            var runFromPackageContext = new RunFromPackageContext(EnvironmentSettingNames.AzureWebsiteRunFromPackage,
                url, DefaultPackageLength, isWarmUpRequest);

            _meshServiceClientMock
                .Setup(m => m.MountFuse(MeshServiceClient.ZipOperation,
                    It.Is<string>(s => url.EndsWith(Path.GetFileName(s))), TargetScriptPath))
                .Returns(Task.FromResult(true));

            _bashCmdHandlerMock.Setup(b =>
                b.RunBashCommand(
                    It.Is<string>(s =>
                        s.StartsWith(BashCommandHandler.FileCommand) && url.EndsWith(Path.GetFileName(s),
                            StringComparison.OrdinalIgnoreCase)),
                    MetricEventNames.LinuxContainerSpecializationFileCommand)).Returns(("zip", string.Empty, 0));

            var applyBlobContextResult = await _runFromPackageHandler.ApplyBlobPackageContext(runFromPackageContext, TargetScriptPath, azureFilesMounted, true);
            Assert.True(applyBlobContextResult);

            handlerMock.Protected().Verify<Task<HttpResponseMessage>>("SendAsync", Times.Once(),
                ItExpr.Is<HttpRequestMessage>(r => IsZipDownloadRequest(r, url)),
                ItExpr.IsAny<CancellationToken>());

            _meshServiceClientMock
                .Setup(m => m.MountFuse(MeshServiceClient.ZipOperation,
                    It.Is<string>(s => url.EndsWith(Path.GetFileName(s))), TargetScriptPath))
                .Returns(Task.FromResult(true));

            _bashCmdHandlerMock.Verify(b =>
                b.RunBashCommand(
                    It.Is<string>(s =>
                        s.StartsWith(BashCommandHandler.FileCommand) && url.EndsWith(Path.GetFileName(s),
                            StringComparison.OrdinalIgnoreCase)),
                    MetricEventNames.LinuxContainerSpecializationFileCommand), Times.Once);
        }

        [Theory]
        [InlineData(true, "1")]
        [InlineData(false, null)]
        public async Task MountsSquashFsFileIfMountEnabledUsingFileCommand(bool azureFilesMounted, string isMountEnabled)
        {
            var isWarmUpRequest = false;
            int packageLength = 100;
            var extension = "unknown";

            _environment.SetEnvironmentVariable(EnvironmentSettingNames.MountEnabled, isMountEnabled);
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.MeshInitURI, MeshInitUri);

            // httpDownload
            var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);

            handlerMock.Protected().Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()).ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new ReadOnlyMemoryContent(ReadOnlyMemory<byte>.Empty)
                });

            _httpClient = new HttpClient(handlerMock.Object);
            _runFromPackageHandler = new RunFromPackageHandler(_environment, _httpClient, _meshServiceClientMock.Object,
                _bashCmdHandlerMock.Object, _zipHandler.Object, _metricsLogger, _logger);

            var url = $"http://url/zip-file.{extension}";
            var runFromPackageContext = new RunFromPackageContext(EnvironmentSettingNames.AzureWebsiteRunFromPackage,
                url, packageLength, isWarmUpRequest);

            _bashCmdHandlerMock.Setup(b =>
                b.RunBashCommand(
                    It.Is<string>(s =>
                        s.StartsWith(BashCommandHandler.FileCommand) && url.EndsWith(Path.GetFileName(s),
                            StringComparison.OrdinalIgnoreCase)),
                    MetricEventNames.LinuxContainerSpecializationFileCommand)).Returns(("squashfs", string.Empty, 0));

            _meshServiceClientMock.Setup(m => m.MountFuse(MeshServiceClient.SquashFsOperation, It.Is<string>(s => url.EndsWith(Path.GetFileName(s))), TargetScriptPath))
                .Returns(Task.FromResult(true));

            var applyBlobContextResult = await _runFromPackageHandler.ApplyBlobPackageContext(runFromPackageContext, TargetScriptPath, azureFilesMounted, true);
            Assert.True(applyBlobContextResult);

            handlerMock.Protected().Verify<Task<HttpResponseMessage>>("SendAsync", Times.Once(),
                ItExpr.Is<HttpRequestMessage>(r => IsZipDownloadRequest(r, url)),
                ItExpr.IsAny<CancellationToken>());

            _bashCmdHandlerMock.Verify(b =>
                b.RunBashCommand(
                    It.Is<string>(s =>
                        s.StartsWith(BashCommandHandler.FileCommand) && url.EndsWith(Path.GetFileName(s),
                            StringComparison.OrdinalIgnoreCase)),
                    MetricEventNames.LinuxContainerSpecializationFileCommand), Times.Once);

            _meshServiceClientMock.Verify(m => m.MountFuse(MeshServiceClient.SquashFsOperation, It.Is<string>(s => url.EndsWith(Path.GetFileName(s))), TargetScriptPath), Times.Once);
        }

        [Theory]
        [InlineData(true, "1")]
        [InlineData(false, null)]
        public async Task ApplyContextFailsForUnknownFileTypes(bool azureFilesMounted, string isMountEnabled)
        {
            var isWarmUpRequest = false;
            int packageLength = 100;
            var extension = "unknown";

            _environment.SetEnvironmentVariable(EnvironmentSettingNames.MountEnabled, isMountEnabled);
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.MeshInitURI, MeshInitUri);

            // httpDownload
            var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);

            handlerMock.Protected().Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()).ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new ReadOnlyMemoryContent(ReadOnlyMemory<byte>.Empty)
                });

            _httpClient = new HttpClient(handlerMock.Object);
            _runFromPackageHandler = new RunFromPackageHandler(_environment, _httpClient, _meshServiceClientMock.Object,
                _bashCmdHandlerMock.Object, _zipHandler.Object, _metricsLogger, _logger);

            var url = $"http://url/zip-file.{extension}";
            var runFromPackageContext = new RunFromPackageContext(EnvironmentSettingNames.AzureWebsiteRunFromPackage,
                url, packageLength, isWarmUpRequest);

            _bashCmdHandlerMock.Setup(b =>
                b.RunBashCommand(
                    It.Is<string>(s =>
                        s.StartsWith(BashCommandHandler.FileCommand) && url.EndsWith(Path.GetFileName(s),
                            StringComparison.OrdinalIgnoreCase)),
                    MetricEventNames.LinuxContainerSpecializationFileCommand)).Returns(("unknown", string.Empty, 0));


            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await _runFromPackageHandler.ApplyBlobPackageContext(runFromPackageContext, TargetScriptPath,
                    azureFilesMounted, true));

            handlerMock.Protected().Verify<Task<HttpResponseMessage>>("SendAsync", Times.Once(),
                ItExpr.Is<HttpRequestMessage>(r => IsZipDownloadRequest(r, url)),
                ItExpr.IsAny<CancellationToken>());

            _bashCmdHandlerMock.Verify(b =>
                b.RunBashCommand(
                    It.Is<string>(s =>
                        s.StartsWith(BashCommandHandler.FileCommand) && url.EndsWith(Path.GetFileName(s),
                            StringComparison.OrdinalIgnoreCase)),
                    MetricEventNames.LinuxContainerSpecializationFileCommand), Times.Once);
        }

        private static Mock<IFileSystem> GetFileSystem()
        {
            var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
            var fileInfo = new Mock<FileInfoBase>(MockBehavior.Strict);
            fileInfo.SetupGet(f => f.Length).Returns(0);
            fileSystem.Setup(f => f.FileInfo.FromFileName(It.IsAny<string>())).Returns(fileInfo.Object);
            return fileSystem;
        }

        public void Dispose()
        {
            FileUtility.Instance = null;
        }
    }
}
