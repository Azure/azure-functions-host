// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Azure.WebJobs.Script.ExtensionBundle;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Newtonsoft.Json;
using NuGet.Versioning;
using Xunit;
using static Microsoft.Azure.WebJobs.Script.EnvironmentSettingNames;

namespace Microsoft.Azure.WebJobs.Script.Tests.ExtensionBundle
{
    public class ExtensionBundleManagerTests : IDisposable
    {
        private const string BundleId = "Microsoft.Azure.Functions.ExtensionBundle";
        private string _downloadPath;

        public ExtensionBundleManagerTests()
        {
            // using temp path because not all windows build machines would have d drive
            _downloadPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "ExtensionBundles", "Microsoft.Azure.Functions.ExtensionBundle");

            if (Directory.Exists(_downloadPath))
            {
                Directory.Delete(_downloadPath, true);
            }
        }

        [Fact]
        public void TryLocateExtensionBundle_BundleDoesNotMatch_ReturnsFalse()
        {
            var options = GetTestExtensionBundleOptions(BundleId, "[1.*, 2.0.0)");
            var fileSystemTuple = CreateFileSystem();
            var directoryBase = fileSystemTuple.Item2;

            string firstDefaultProbingPath = options.ProbingPaths.ElementAt(0);
            directoryBase.Setup(d => d.Exists(firstDefaultProbingPath)).Returns(true);
            directoryBase.Setup(d => d.EnumerateDirectories(firstDefaultProbingPath)).Returns(new[] { Path.Combine(firstDefaultProbingPath, "3.0.2") });

            FileUtility.Instance = fileSystemTuple.Item1.Object;
            var manager = GetExtensionBundleManager(options, GetTestAppServiceEnvironment());
            Assert.False(manager.TryLocateExtensionBundle(out string path));
            Assert.Null(path);
        }

        [Fact]
        public async Task GetExtensionBundleDetails_InvalidBundle_ReturnsVersionAsNull()
        {
            var options = GetTestExtensionBundleOptions("InvalidBundleId", "[1.*, 2.0.0)");
            var fileSystemTuple = CreateFileSystem();
            var directoryBase = fileSystemTuple.Item2;

            string firstDefaultProbingPath = options.ProbingPaths.ElementAt(0);
            directoryBase.Setup(d => d.Exists(firstDefaultProbingPath)).Returns(true);
            directoryBase.Setup(d => d.EnumerateDirectories(firstDefaultProbingPath)).Returns(new[] { Path.Combine(firstDefaultProbingPath, "3.0.2") });

            FileUtility.Instance = fileSystemTuple.Item1.Object;
            var manager = GetExtensionBundleManager(options, GetTestAppServiceEnvironment());
            var bundleInfo = await manager.GetExtensionBundleDetails();
            Assert.Equal(bundleInfo.Id, "InvalidBundleId");
            Assert.Null(bundleInfo.Version);
        }

        [Fact]
        public void TryLocateExtensionBundle_BundleNotPersent_ReturnsFalse()
        {
            var options = GetTestExtensionBundleOptions(BundleId, "[1.*, 2.0.0)");
            var fileSystemTuple = CreateFileSystem();
            FileUtility.Instance = fileSystemTuple.Item1.Object;
            var manager = GetExtensionBundleManager(options, GetTestAppServiceEnvironment());
            Assert.False(manager.TryLocateExtensionBundle(out string path));
            Assert.Null(path);
        }

        [Theory]
        [InlineData("[2.*, 3.0.0)")]
        [InlineData("[2.0.0, 3.0.0)")]
        public async Task GetExtensionBundleDetails_BundlePresentAtProbingLocation_ExpectedValue(string versionRange)
        {
            var options = GetTestExtensionBundleOptions(BundleId, versionRange);
            var fileSystemTuple = CreateFileSystem();
            var directoryBase = fileSystemTuple.Item2;
            var fileBase = fileSystemTuple.Item3;

            string firstDefaultProbingPath = options.ProbingPaths.ElementAt(0);
            directoryBase.Setup(d => d.Exists(firstDefaultProbingPath)).Returns(true);
            directoryBase.Setup(d => d.EnumerateDirectories(firstDefaultProbingPath)).Returns(
            new[]
            {
                    Path.Combine(firstDefaultProbingPath, "1.0.0"),
                    Path.Combine(firstDefaultProbingPath, "2.0.0"),
                    Path.Combine(firstDefaultProbingPath, "2.0.1"),
                    Path.Combine(firstDefaultProbingPath, "2.0.2"),
                    Path.Combine(firstDefaultProbingPath, "3.0.2"),
                    Path.Combine(firstDefaultProbingPath, "invalidVersion")
            });

            string defaultPath = Path.Combine(firstDefaultProbingPath, "2.0.2");
            fileBase.Setup(f => f.Exists(Path.Combine(defaultPath, "bundle.json"))).Returns(true);

            FileUtility.Instance = fileSystemTuple.Item1.Object;
            var manager = GetExtensionBundleManager(options, GetTestAppServiceEnvironment());
            var bundleInfo = await manager.GetExtensionBundleDetails();
            Assert.Equal(bundleInfo.Id, BundleId);
            Assert.Equal(bundleInfo.Version, "2.0.2");
        }

        [Theory]
        [InlineData("[2.*, 3.0.0)")]
        [InlineData("[2.0.0, 3.0.0)")]
        public async Task GetExtensionBundle_BundlePresentAtProbingLocation_ReturnsTrue(string versionRange)
        {
            var options = GetTestExtensionBundleOptions(BundleId, versionRange);
            var fileSystemTuple = CreateFileSystem();
            var directoryBase = fileSystemTuple.Item2;
            var fileBase = fileSystemTuple.Item3;

            string firstDefaultProbingPath = options.ProbingPaths.ElementAt(0);
            directoryBase.Setup(d => d.Exists(firstDefaultProbingPath)).Returns(true);
            directoryBase.Setup(d => d.EnumerateDirectories(firstDefaultProbingPath)).Returns(
            new[]
            {
                    Path.Combine(firstDefaultProbingPath, "1.0.0"),
                    Path.Combine(firstDefaultProbingPath, "2.0.0"),
                    Path.Combine(firstDefaultProbingPath, "2.0.1"),
                    Path.Combine(firstDefaultProbingPath, "2.0.2"),
                    Path.Combine(firstDefaultProbingPath, "3.0.2"),
                    Path.Combine(firstDefaultProbingPath, "invalidVersion")
            });

            string defaultPath = Path.Combine(firstDefaultProbingPath, "2.0.2");
            fileBase.Setup(f => f.Exists(Path.Combine(defaultPath, "bundle.json"))).Returns(true);

            FileUtility.Instance = fileSystemTuple.Item1.Object;
            var manager = GetExtensionBundleManager(options, GetTestAppServiceEnvironment());
            string path = await manager.GetExtensionBundlePath();
            Assert.NotNull(path);
            Assert.Equal(defaultPath, path);
        }

        [Fact]
        public async Task GetExtensionBundleDetails_BundlePresentAtDownloadLocation_ReturnsCorrectPathAync()
        {
            var options = GetTestExtensionBundleOptions(BundleId, "[1.*, 2.0.0)");
            var fileSystemTuple = CreateFileSystem();
            var directoryBase = fileSystemTuple.Item2;
            var fileBase = fileSystemTuple.Item3;

            string firstDefaultProbingPath = options.ProbingPaths.ElementAt(0);
            directoryBase.Setup(d => d.Exists(firstDefaultProbingPath)).Returns(true);
            directoryBase.Setup(d => d.EnumerateDirectories(firstDefaultProbingPath)).Returns(new List<string>());

            string downloadPath = Path.Combine(options.DownloadPath, "1.0.2");
            directoryBase.Setup(d => d.Exists(options.DownloadPath)).Returns(true);
            directoryBase.Setup(d => d.EnumerateDirectories(options.DownloadPath)).Returns(new[] { downloadPath });
            fileBase.Setup(f => f.Exists(Path.Combine(downloadPath, "bundle.json"))).Returns(true);

            FileUtility.Instance = fileSystemTuple.Item1.Object;
            var manager = GetExtensionBundleManager(options, GetTestAppServiceEnvironment());
            var bundleInfo = await manager.GetExtensionBundleDetails();
            Assert.Equal(bundleInfo.Id, BundleId);
            Assert.Equal(bundleInfo.Version, "1.0.2");
        }

        [Fact]
        public async Task GetExtensionBundleDetails_BundleNotConfigured_ReturnsNull()
        {
            ExtensionBundleOptions options = new ExtensionBundleOptions() { Id = null, Version = null };
            var manager = GetExtensionBundleManager(options, GetTestAppServiceEnvironment());
            var bundleInfo = await manager.GetExtensionBundleDetails();
            Assert.Null(bundleInfo);
        }

        [Fact]
        public async Task GetExtensionBundle_BundlePresentAtDownloadLocation_ReturnsCorrectPathAync()
        {
            var options = GetTestExtensionBundleOptions(BundleId, "[1.*, 2.0.0)");
            var fileSystemTuple = CreateFileSystem();
            var directoryBase = fileSystemTuple.Item2;
            var fileBase = fileSystemTuple.Item3;

            string firstDefaultProbingPath = options.ProbingPaths.ElementAt(0);
            directoryBase.Setup(d => d.Exists(firstDefaultProbingPath)).Returns(true);
            directoryBase.Setup(d => d.EnumerateDirectories(firstDefaultProbingPath)).Returns(new List<string>());

            string downloadPath = Path.Combine(options.DownloadPath, "1.0.2");
            directoryBase.Setup(d => d.Exists(options.DownloadPath)).Returns(true);
            directoryBase.Setup(d => d.EnumerateDirectories(options.DownloadPath)).Returns(new[] { downloadPath });
            fileBase.Setup(f => f.Exists(Path.Combine(downloadPath, "bundle.json"))).Returns(true);

            FileUtility.Instance = fileSystemTuple.Item1.Object;
            var manager = GetExtensionBundleManager(options, GetTestAppServiceEnvironment());
            string path = await manager.GetExtensionBundlePath();
            Assert.NotNull(path);
            Assert.Equal(downloadPath, path);
        }

        [Fact]
        public async Task GetExtensionBundle_PartialBundlePresentAtDownloadLocation_ReturnsNullPath()
        {
            var options = GetTestExtensionBundleOptions(BundleId, "[1.*, 2.0.0)");
            var fileSystemTuple = CreateFileSystem();
            var directoryBase = fileSystemTuple.Item2;
            var fileBase = fileSystemTuple.Item3;

            string firstDefaultProbingPath = options.ProbingPaths.ElementAt(0);
            directoryBase.Setup(d => d.Exists(firstDefaultProbingPath)).Returns(true);
            directoryBase.Setup(d => d.EnumerateDirectories(firstDefaultProbingPath)).Returns(new List<string>());

            string downloadPath = Path.Combine(options.DownloadPath, "1.0.1");
            directoryBase.Setup(d => d.Exists(options.DownloadPath)).Returns(true);
            directoryBase.Setup(d => d.EnumerateDirectories(options.DownloadPath)).Returns(new[] { downloadPath });
            fileBase.Setup(f => f.Exists(Path.Combine(downloadPath, "bundle.json"))).Returns(false);

            FileUtility.Instance = fileSystemTuple.Item1.Object;
            var manager = GetExtensionBundleManager(options, new TestEnvironment());
            string path = await manager.GetExtensionBundlePath();

            Assert.Null(path);
        }

        [Fact]
        public async Task GetExtensionBundle_DownloadsMatchingVersion_ReturnsTrueAsync()
        {
            var options = GetTestExtensionBundleOptions(BundleId, "[1.*, 2.0.0)");
            var manager = GetExtensionBundleManager(options, GetTestAppServiceEnvironment());
            var httpclient = new HttpClient(new MockHttpHandler(statusCodeForIndexJson: HttpStatusCode.OK, statusCodeForZipFile: HttpStatusCode.OK, "1.0.1"));
            var path = await manager.GetExtensionBundlePath(httpclient);
            var bundleDirectory = Path.Combine(_downloadPath, "1.0.1");
            Assert.True(Directory.Exists(bundleDirectory));
            Assert.Equal(bundleDirectory, path);
        }

        [Fact]
        public async Task GetExtensionBundle_DownloadsLatest_WhenEnsureLatestTrue()
        {
            var options = GetTestExtensionBundleOptions(BundleId, "[1.0.0, 1.0.1)");
            var manager = GetExtensionBundleManager(options, GetTestAppServiceEnvironment());
            var httpclient = new HttpClient(new MockHttpHandler(statusCodeForIndexJson: HttpStatusCode.OK, statusCodeForZipFile: HttpStatusCode.OK, "1.0.0"));
            var path = await manager.GetExtensionBundlePath(httpclient);
            var bundleDirectory = Path.Combine(_downloadPath, "1.0.0");
            Assert.True(Directory.Exists(bundleDirectory));
            Assert.Equal(bundleDirectory, path);

            var newOptions = options;
            newOptions.Version = VersionRange.Parse("[1.*, 2.0.0)", true);
            newOptions.EnsureLatest = true;
            manager = GetExtensionBundleManager(newOptions, GetTestAppServiceEnvironment());
            httpclient = new HttpClient(new MockHttpHandler(statusCodeForIndexJson: HttpStatusCode.OK, statusCodeForZipFile: HttpStatusCode.OK, "1.0.1"));
            path = await manager.GetExtensionBundlePath(httpclient);
            bundleDirectory = Path.Combine(_downloadPath, "1.0.1");
            Assert.True(Directory.Exists(bundleDirectory));
            Assert.Equal(bundleDirectory, path);
        }

        [Fact]
        public async Task GetExtensionBundle_DoesNotDownload_WhenPersistentFileSystemNotAvailable()
        {
            var options = GetTestExtensionBundleOptions(BundleId, "[1.0.0, 1.0.1)");
            var manager = GetExtensionBundleManager(options, new TestEnvironment());
            var path = await manager.GetExtensionBundlePath();
            Assert.Null(path);
        }

        [Fact]
        public async Task GetExtensionBundle_CannotReachIndexEndpoint_ReturnsNullAsync()
        {
            var options = GetTestExtensionBundleOptions(BundleId, "[1.*, 2.0.0)");
            var httpClient = new HttpClient(new MockHttpHandler(statusCodeForIndexJson: HttpStatusCode.NotFound, statusCodeForZipFile: HttpStatusCode.OK));
            var manager = GetExtensionBundleManager(options, GetTestAppServiceEnvironment());
            Assert.Null(await manager.GetExtensionBundlePath(httpClient));
        }

        [Fact]
        public async Task GetExtensionBundle_CannotReachZipEndpoint_ReturnsFalseAsync()
        {
            var options = GetTestExtensionBundleOptions(BundleId, "[1.*, 2.0.0)");
            var httpClient = new HttpClient(new MockHttpHandler(statusCodeForIndexJson: HttpStatusCode.OK, statusCodeForZipFile: HttpStatusCode.NotFound));
            var manager = GetExtensionBundleManager(options, GetTestAppServiceEnvironment());
            Assert.Null(await manager.GetExtensionBundlePath(httpClient));
        }

        private ExtensionBundleManager GetExtensionBundleManager(ExtensionBundleOptions bundleOptions, TestEnvironment environment = null)
        {
            environment = environment ?? new TestEnvironment();
            return new ExtensionBundleManager(bundleOptions, environment, MockNullLoggerFactory.CreateLoggerFactory());
        }

        private TestEnvironment GetTestAppServiceEnvironment()
        {
            var environment = new TestEnvironment();
            environment.SetEnvironmentVariable(AzureWebsiteInstanceId, Guid.NewGuid().ToString("N"));
            string downloadPath = string.Empty;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                environment.SetEnvironmentVariable(AzureWebsiteHomePath, "D:\\home");
            }
            else
            {
                environment.SetEnvironmentVariable(AzureWebsiteHomePath, "//home");
            }
            return environment;
        }

        private ExtensionBundleOptions GetTestExtensionBundleOptions(string id, string version)
        {
            var options = new ExtensionBundleOptions
            {
                Id = id,
                Version = VersionRange.Parse(version, true),
                DownloadPath = _downloadPath
            };

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                options.ProbingPaths.Add(@"C:\Program Files (x86)\FuncExtensionBundles\Microsoft.Azure.Functions.ExtensionBundle");
            }
            else
            {
                options.ProbingPaths.Add("/FuncExtensionBundles/Microsoft.Azure.Functions.ExtensionBundle");
                options.ProbingPaths.Add("/home/site/wwwroot/.azureFunctions/ExtensionBundles/Microsoft.Azure.Functions.ExtensionBundle");
            }

            return options;
        }

        private Tuple<Mock<IFileSystem>, Mock<DirectoryBase>, Mock<FileBase>> CreateFileSystem()
        {
            var fileSystem = new Mock<IFileSystem>();
            var fileBase = new Mock<FileBase>();
            var dirBase = new Mock<DirectoryBase>();
            fileSystem.SetupGet(f => f.Directory).Returns(dirBase.Object);
            fileSystem.SetupGet(f => f.File).Returns(fileBase.Object);
            return new Tuple<Mock<IFileSystem>, Mock<DirectoryBase>, Mock<FileBase>>(fileSystem, dirBase, fileBase);
        }

        public void Dispose()
        {
            FileUtility.Instance = null;
        }

        private class MockHttpHandler : HttpClientHandler
        {
            private readonly string _version;
            private HttpStatusCode _statusCodeForIndexJson;
            private HttpStatusCode _statusCodeForZipFile;

            public MockHttpHandler(HttpStatusCode statusCodeForIndexJson, HttpStatusCode statusCodeForZipFile, string version = null)
            {
                _statusCodeForIndexJson = statusCodeForIndexJson;
                _statusCodeForZipFile = statusCodeForZipFile;
                _version = version;
            }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                await Task.Yield();
                var response = new HttpResponseMessage();
                if (request.RequestUri.AbsolutePath.EndsWith("index.json"))
                {
                    response.Content = _statusCodeForIndexJson == HttpStatusCode.OK
                                       ? new StringContent("[ \"1.0.0\", \"1.0.1\", \"2.0.0\" ]")
                                       : null;
                    response.StatusCode = _statusCodeForIndexJson;
                    return response;
                }

                if (request.RequestUri.AbsolutePath.Contains($"{BundleId}.{_version}"))
                {
                    response.Content = _statusCodeForZipFile == HttpStatusCode.OK
                                       ? GetBundleZip()
                                       : null;
                    response.StatusCode = _statusCodeForZipFile;
                }
                else
                {
                    response.Content = null;
                    response.StatusCode = HttpStatusCode.NotFound;
                }

                return response;
            }

            private StreamContent GetBundleZip()
            {
                var stream = new MemoryStream();
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
                {
                    var file = zip.CreateEntry("bundle.json");
                    using (var entryStream = file.Open())
                    using (var streamWriter = new StreamWriter(entryStream))
                    {
                        streamWriter.Write(" { id: \"Microsoft.Azure.Functions.ExtensionBundle\" }");
                    }
                }
                stream.Seek(0, SeekOrigin.Begin);
                return new StreamContent(stream);
            }
        }
    }
}
