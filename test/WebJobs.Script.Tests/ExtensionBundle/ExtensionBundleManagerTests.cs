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
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Azure.WebJobs.Script.ExtensionBundle;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using NuGet.Versioning;
using Polly;
using Polly.Extensions.Http;
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
            var options = GetTestExtensionBundleOptions(BundleId, "[2.*, 3.0.0)");
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
            var options = GetTestExtensionBundleOptions("InvalidBundleId", "[2.*, 3.0.0)");
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
            var options = GetTestExtensionBundleOptions(BundleId, "[2.*, 3.0.0)");
            var fileSystemTuple = CreateFileSystem();
            FileUtility.Instance = fileSystemTuple.Item1.Object;
            var manager = GetExtensionBundleManager(options, GetTestAppServiceEnvironment());
            Assert.False(manager.TryLocateExtensionBundle(out string path));
            Assert.Null(path);
        }

        [Theory]
        [InlineData(BundleId, "[1.*, 2.0.0)", true)]
        [InlineData(BundleId, "[2.*, 3.0.0)", false)]
        [InlineData(BundleId, "[2.0.0, 3.0.0)", false)]
        [InlineData("TestBundleId", "[1.*, 2.0.0)", false)]
        public void IsLegacyExtensionBundle_LegacyBundleConfig_ReturnsExpectedResult(string bundleId, string bundleVersion, bool isLegacyExtensionBundle)
        {
            var options = GetTestExtensionBundleOptions(bundleId, bundleVersion);
            var manager = GetExtensionBundleManager(options, GetTestAppServiceEnvironment());
            Assert.Equal(manager.IsLegacyExtensionBundle(), isLegacyExtensionBundle);
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
                    Path.Combine(firstDefaultProbingPath, "2.0.0"),
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
                    Path.Combine(firstDefaultProbingPath, "2.0.0"),
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

        [Theory]
        [InlineData(true, true, false)]
        [InlineData(false, true, true)]
        public async Task GetExtensionBundleBinPath_ReturnsCorrectLocation(bool readyToRunPathExists, bool defaultPathExists, bool expectDefaultBinPath)
        {
            var options = GetTestExtensionBundleOptions(BundleId, "[2.*, 3.0.0)");
            string firstDefaultProbingPath = options.ProbingPaths.ElementAt(0);
            string defaultPath = Path.Combine(firstDefaultProbingPath, "2.0.2");

            var fileSystemTuple = GetDefaultBundleFileSystem(firstDefaultProbingPath, defaultPath);
            var directoryBase = fileSystemTuple.Item2;
            var fileBase = fileSystemTuple.Item3;

            var environment = GetTestAppServiceEnvironment();

            string os = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "win" : "linux";
            string bitness = Environment.Is64BitProcess ? "x64" : "x86";

            string rrBinPath = Path.Combine(defaultPath, "bin_v3", $"{os}-{bitness}");
            directoryBase.Setup(d => d.Exists(rrBinPath)).Returns(readyToRunPathExists);

            string defaultBinPath = Path.Combine(defaultPath, "bin");
            directoryBase.Setup(d => d.Exists(defaultBinPath)).Returns(defaultPathExists);

            FileUtility.Instance = fileSystemTuple.Item1.Object;
            var manager = GetExtensionBundleManager(options, environment);
            string binPath = await manager.GetExtensionBundleBinPathAsync();
            Assert.NotNull(binPath);

            Assert.Equal(expectDefaultBinPath ? defaultBinPath : rrBinPath, binPath);
        }

        [Fact]
        public async Task GetExtensionBundleBinPath_NoBinaries_ReturnsNull()
        {
            var options = GetTestExtensionBundleOptions(BundleId, "[2.*, 3.0.0)");
            string firstDefaultProbingPath = options.ProbingPaths.ElementAt(0);
            string defaultPath = Path.Combine(firstDefaultProbingPath, "2.0.2");

            var fileSystemTuple = GetDefaultBundleFileSystem(firstDefaultProbingPath, defaultPath);
            var directoryBase = fileSystemTuple.Item2;
            var fileBase = fileSystemTuple.Item3;

            var environment = GetTestAppServiceEnvironment();

            string os = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "win" : "linux";
            string bitness = Environment.Is64BitProcess ? "x64" : "x86";

            string rrBinPath = Path.Combine(defaultPath, "bin_v3", $"{os}-{bitness}");
            directoryBase.Setup(d => d.Exists(rrBinPath)).Returns(false);

            string defaultBinPath = Path.Combine(defaultPath, "bin");
            directoryBase.Setup(d => d.Exists(defaultBinPath)).Returns(false);

            FileUtility.Instance = fileSystemTuple.Item1.Object;
            var manager = GetExtensionBundleManager(options, environment);
            string binPath = await manager.GetExtensionBundleBinPathAsync();
            Assert.Null(binPath);
        }

        private Tuple<Mock<IFileSystem>, Mock<DirectoryBase>, Mock<FileBase>> GetDefaultBundleFileSystem(string firstDefaultProbingPath, string defaultPath)
        {
            var fileSystemTuple = CreateFileSystem();
            var directoryBase = fileSystemTuple.Item2;
            var fileBase = fileSystemTuple.Item3;

            directoryBase.Setup(d => d.Exists(firstDefaultProbingPath)).Returns(true);
            directoryBase.Setup(d => d.EnumerateDirectories(firstDefaultProbingPath))
                .Returns(new[] { Path.Combine(firstDefaultProbingPath, "2.0.2") });

            fileBase.Setup(f => f.Exists(Path.Combine(defaultPath, "bundle.json"))).Returns(true);
            return fileSystemTuple;
        }

        [Fact]
        public async Task GetExtensionBundleDetails_BundlePresentAtDownloadLocation_ReturnsCorrectPathAync()
        {
            var options = GetTestExtensionBundleOptions(BundleId, "[2.*, 3.0.0)");
            var fileSystemTuple = CreateFileSystem();
            var directoryBase = fileSystemTuple.Item2;
            var fileBase = fileSystemTuple.Item3;

            string firstDefaultProbingPath = options.ProbingPaths.ElementAt(0);
            directoryBase.Setup(d => d.Exists(firstDefaultProbingPath)).Returns(true);
            directoryBase.Setup(d => d.EnumerateDirectories(firstDefaultProbingPath)).Returns(new List<string>());

            string downloadPath = Path.Combine(options.DownloadPath, "2.0.2");
            directoryBase.Setup(d => d.Exists(options.DownloadPath)).Returns(true);
            directoryBase.Setup(d => d.EnumerateDirectories(options.DownloadPath)).Returns(new[] { downloadPath });
            fileBase.Setup(f => f.Exists(Path.Combine(downloadPath, "bundle.json"))).Returns(true);

            FileUtility.Instance = fileSystemTuple.Item1.Object;
            var manager = GetExtensionBundleManager(options, GetTestAppServiceEnvironment());
            var bundleInfo = await manager.GetExtensionBundleDetails();
            Assert.Equal(bundleInfo.Id, BundleId);
            Assert.Equal(bundleInfo.Version, "2.0.2");
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
            var options = GetTestExtensionBundleOptions(BundleId, "[2.*, 3.0.0)");
            var fileSystemTuple = CreateFileSystem();
            var directoryBase = fileSystemTuple.Item2;
            var fileBase = fileSystemTuple.Item3;

            string firstDefaultProbingPath = options.ProbingPaths.ElementAt(0);
            directoryBase.Setup(d => d.Exists(firstDefaultProbingPath)).Returns(true);
            directoryBase.Setup(d => d.EnumerateDirectories(firstDefaultProbingPath)).Returns(new List<string>());

            string downloadPath = Path.Combine(options.DownloadPath, "2.0.2");
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
            var options = GetTestExtensionBundleOptions(BundleId, "[2.*, 3.0.0)");
            var fileSystemTuple = CreateFileSystem();
            var directoryBase = fileSystemTuple.Item2;
            var fileBase = fileSystemTuple.Item3;

            string firstDefaultProbingPath = options.ProbingPaths.ElementAt(0);
            directoryBase.Setup(d => d.Exists(firstDefaultProbingPath)).Returns(true);
            directoryBase.Setup(d => d.EnumerateDirectories(firstDefaultProbingPath)).Returns(new List<string>());

            string downloadPath = Path.Combine(options.DownloadPath, "2.0.1");
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
            var options = GetTestExtensionBundleOptions(BundleId, "[2.*, 3.0.0)");
            var manager = GetExtensionBundleManager(options, GetTestAppServiceEnvironment());
            var httpclient = new HttpClient(new MockHttpHandler(statusCodeForIndexJson: HttpStatusCode.OK, statusCodeForZipFile: HttpStatusCode.OK, "2.0.1"));
            var path = await manager.GetExtensionBundlePath(httpclient);
            var bundleDirectory = Path.Combine(_downloadPath, "2.0.1");
            Assert.True(Directory.Exists(bundleDirectory));
            Assert.Equal(bundleDirectory, path);
        }

        [Fact]
        public async Task GetExtensionBundle_DownloadsLatest_WhenEnsureLatestTrue()
        {
            var options = GetTestExtensionBundleOptions(BundleId, "[2.0.0, 2.0.1)");
            var manager = GetExtensionBundleManager(options, GetTestAppServiceEnvironment());
            var httpclient = new HttpClient(new MockHttpHandler(statusCodeForIndexJson: HttpStatusCode.OK, statusCodeForZipFile: HttpStatusCode.OK, "2.0.0"));
            var path = await manager.GetExtensionBundlePath(httpclient);
            var bundleDirectory = Path.Combine(_downloadPath, "2.0.0");
            Assert.True(Directory.Exists(bundleDirectory));
            Assert.Equal(bundleDirectory, path);

            var newOptions = options;
            newOptions.Version = VersionRange.Parse("[2.*, 3.0.0)", true);
            newOptions.EnsureLatest = true;
            manager = GetExtensionBundleManager(newOptions, GetTestAppServiceEnvironment());
            httpclient = new HttpClient(new MockHttpHandler(statusCodeForIndexJson: HttpStatusCode.OK, statusCodeForZipFile: HttpStatusCode.OK, "2.0.1"));
            path = await manager.GetExtensionBundlePath(httpclient);
            bundleDirectory = Path.Combine(_downloadPath, "2.0.1");
            Assert.True(Directory.Exists(bundleDirectory));
            Assert.Equal(bundleDirectory, path);
        }

        [Fact]
        public async Task GetExtensionBundle_DoesNotDownload_WhenPersistentFileSystemNotAvailable()
        {
            var options = GetTestExtensionBundleOptions(BundleId, "[2.0.0, 2.0.1)");
            var manager = GetExtensionBundleManager(options, new TestEnvironment());
            var path = await manager.GetExtensionBundlePath();
            Assert.Null(path);
        }

        [Fact]
        public async Task GetExtensionBundle_CannotReachIndexEndpoint_ReturnsNullAsync()
        {
            var options = GetTestExtensionBundleOptions(BundleId, "[2.*, 3.0.0)");
            var httpClient = new HttpClient(new MockHttpHandler(statusCodeForIndexJson: HttpStatusCode.NotFound, statusCodeForZipFile: HttpStatusCode.OK));
            var manager = GetExtensionBundleManager(options, GetTestAppServiceEnvironment());
            Assert.Null(await manager.GetExtensionBundlePath(httpClient));
        }

        [Fact]
        public async Task GetExtensionBundle_CannotReachZipEndpoint_ReturnsFalseAsync()
        {
            var options = GetTestExtensionBundleOptions(BundleId, "[2.*, 3.0.0)");
            var httpClient = new HttpClient(new MockHttpHandler(statusCodeForIndexJson: HttpStatusCode.OK, statusCodeForZipFile: HttpStatusCode.NotFound));
            var manager = GetExtensionBundleManager(options, GetTestAppServiceEnvironment());
            Assert.Null(await manager.GetExtensionBundlePath(httpClient));
        }

        [Theory]
        [InlineData("[3.*, 4.0.0)", "3.19.0", "3.19.0")]
        [InlineData("[4.*, 5.0.0)", "4.2.0", "4.2.0")]
        [InlineData("[4.*, 5.0.0)", null, "4.3.0")]
        [InlineData("[3.*, 4.0.0)", null, "3.20.0")]
        public void LimitMaxVersion(string versionRange, string hostConfigVersion, string expectedVersion)
        {
            var range = VersionRange.Parse(versionRange);
            var hostingConfiguration = new FunctionsHostingConfigOptions();
            if (!string.IsNullOrEmpty(hostConfigVersion))
            {
                if (range.MinVersion.Major == 3)
                {
                    hostingConfiguration.Features.Add(ScriptConstants.MaximumBundleV3Version, hostConfigVersion);
                }

                if (range.MinVersion.Major == 4)
                {
                    hostingConfiguration.Features.Add(ScriptConstants.MaximumBundleV4Version, hostConfigVersion);
                }
            }

            var options = GetTestExtensionBundleOptions(BundleId, versionRange);
            var manager = GetExtensionBundleManager(options, GetTestAppServiceEnvironment());

            var resolvedVersion = manager.FindBestVersionMatch(range, GetLargeVersionsList(), ScriptConstants.DefaultExtensionBundleId, hostingConfiguration);

            Assert.Equal(expectedVersion, resolvedVersion);
        }

        [Theory]
        [InlineData(ScriptConstants.LatestPlatformChannelNameUpper, "[4.*, 5.0.0)", "4.2.0", "4.2.0")]
        [InlineData(ScriptConstants.StandardPlatformChannelNameUpper, "[4.*, 5.0.0)", "4.2.0", "4.2.0")]
        [InlineData(ScriptConstants.ExtendedPlatformChannelNameUpper, "[4.*, 5.0.0)", "4.2.0", "4.2.0")]
        [InlineData(ScriptConstants.StandardPlatformChannelNameUpper, "[4.*, 5.0.0)", "4.3.0", "4.2.0")]
        [InlineData(ScriptConstants.ExtendedPlatformChannelNameUpper, "[4.*, 5.0.0)", "4.3.0", "4.2.0")]
        [InlineData(ScriptConstants.StandardPlatformChannelNameUpper, "[4.*, 5.0.0)", "4.1.0", "4.1.0")]
        [InlineData(ScriptConstants.ExtendedPlatformChannelNameUpper, "[4.*, 5.0.0)", "4.1.0", "4.1.0")]
        [InlineData(ScriptConstants.LatestPlatformChannelNameUpper, "[4.*, 5.0.0)", null, "4.3.0")]
        [InlineData("latest", "[4.*, 5.0.0)", null, "4.3.0")]
        [InlineData(ScriptConstants.StandardPlatformChannelNameUpper, "[4.*, 5.0.0)", null, "4.2.0")]
        [InlineData("standard", "[4.*, 5.0.0)", null, "4.2.0")]
        [InlineData(ScriptConstants.ExtendedPlatformChannelNameUpper, "[4.*, 5.0.0)", null, "4.2.0")]
        [InlineData("extended", "[4.*, 5.0.0)", null, "4.2.0")]
        public void WhenPlatformReleaseChannelSet_ExpectedVersionChosen(string platformReleaseChannelName, string versionRange, string hostConfigMaxVersion, string expectedVersion)
        {
            var range = VersionRange.Parse(versionRange);
            var hostingConfiguration = new FunctionsHostingConfigOptions();

            if (!string.IsNullOrEmpty(hostConfigMaxVersion))
            {
                if (range.MinVersion.Major == 3)
                {
                    hostingConfiguration.Features.Add(ScriptConstants.MaximumBundleV3Version, hostConfigMaxVersion);
                }

                if (range.MinVersion.Major == 4)
                {
                    hostingConfiguration.Features.Add(ScriptConstants.MaximumBundleV4Version, hostConfigMaxVersion);
                }
            }

            var options = GetTestExtensionBundleOptions(BundleId, versionRange);
            var testEnvironment = GetTestAppServiceEnvironment(platformReleaseChannelName);
            var manager = GetExtensionBundleManager(options, testEnvironment);

            var versions = GetLargeVersionsList();

            var resolvedVersion = manager.FindBestVersionMatch(range, versions, ScriptConstants.DefaultExtensionBundleId, hostingConfiguration);

            Assert.Equal(expectedVersion, resolvedVersion);
        }

        [Theory]
        [InlineData(ScriptConstants.ExtendedPlatformChannelNameUpper)]
        [InlineData(ScriptConstants.StandardPlatformChannelNameUpper)]
        public void StandardExtendedReleaseChannel_OneBundleVersionOnDisk_Handled(string platformReleaseChannelName)
        {
            // these release channels take the version prior to the latest version
            // however, if there is only one bundle version available on disk, that bundle should be chosen and information logged

            var versions = new List<string>() { "4.20.0" }; // only one bundle version available on disk
            var versionRange = "[4.*, 5.0.0)";
            var expected = "4.20.0";

            var loggedString = $"Unable to apply platform release channel configuration {platformReleaseChannelName}. Only one matching bundle version is available. {expected} will be used";
            var mockLogger = GetVerifiableMockLogger(loggedString, LogLevel.Warning);
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(() => mockLogger.Object);

            var options = GetTestExtensionBundleOptions(BundleId, versionRange);
            var testEnvironment = GetTestAppServiceEnvironment(platformReleaseChannelName);
            var manager = GetExtensionBundleManager(options, testEnvironment, mockLoggerFactory);

            var resolvedVersion = manager.FindBestVersionMatch(VersionRange.Parse(versionRange), versions, ScriptConstants.DefaultExtensionBundleId, new FunctionsHostingConfigOptions());

            Assert.Equal(expected, resolvedVersion);
            mockLogger.Verify();
        }

        [Fact]
        public void UnknownReleaseChannel_ExpectedVersionChosen()
        {
            var versions = new List<string>() { "4.20.0" };
            var versionRange = "[4.*, 5.0.0)";
            var expected = "4.20.0";

            var incorrectChannelName = "someIncorrectReleaseChannelName";
            var loggedString = $"Unknown platform release channel name {incorrectChannelName}. The latest bundle version, {expected}, will be used.";
            var mockLogger = GetVerifiableMockLogger(loggedString, LogLevel.Warning);
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(() => mockLogger.Object);

            var options = GetTestExtensionBundleOptions(BundleId, versionRange);
            var testEnvironment = GetTestAppServiceEnvironment(incorrectChannelName);
            var manager = GetExtensionBundleManager(options, testEnvironment, mockLoggerFactory);

            var resolvedVersion = manager.FindBestVersionMatch(VersionRange.Parse(versionRange), versions, ScriptConstants.DefaultExtensionBundleId, new FunctionsHostingConfigOptions());

            Assert.Equal(expected, resolvedVersion); // unknown release channel should default to latest and log information
            mockLogger.Verify();
        }

        [Fact]
        public async Task GetExtensionBundle_RetriesZipDownload_SucceedsAfterTransientFailures()
        {
            var options = GetTestExtensionBundleOptions(BundleId, "[4.*, 5.0.0)");
            var environment = GetTestAppServiceEnvironment();
            var version = "4.2.0";

            var handler = new TransientZipFailureHandler(version, zipFailuresBeforeSuccess: 2);
            var services = new ServiceCollection();
            services.AddLogging();

            // Register named client identical to production name so manager picks up retry policy.
#pragma warning disable SA1313 // Parameter names should begin with lower-case letter
            _ = services.AddHttpClient(nameof(ExtensionBundleManager))
                .ConfigurePrimaryHttpMessageHandler(() => handler)
                .AddPolicyHandler(HttpPolicyExtensions
                    .HandleTransientHttpError()
                    .OrResult(resp => resp.StatusCode == HttpStatusCode.TooManyRequests)
                    .WaitAndRetryAsync(
                        retryCount: 4,
                        sleepDurationProvider: _ => TimeSpan.Zero,
                        onRetry: (_, __, ___, ____) => { }));
#pragma warning restore SA1313 // Parameter names should begin with lower-case letter

            var provider = services.BuildServiceProvider();
            var factory = provider.GetRequiredService<IHttpClientFactory>();
            var manager = new ExtensionBundleManager(options, environment, MockNullLoggerFactory.CreateLoggerFactory(), new FunctionsHostingConfigOptions(), factory);

            var path = await manager.GetExtensionBundlePath();

            Assert.NotNull(path);
            Assert.True(Directory.Exists(Path.Combine(options.DownloadPath, version)));
            Assert.Equal(1, handler.IndexAttempts); // index.json fetched once
            Assert.Equal(3, handler.ZipAttempts);   // 2 failures + 1 success
        }

        [Fact]
        public async Task GetExtensionBundle_RetriesZipDownload_ExhaustsAndFails()
        {
            var options = GetTestExtensionBundleOptions(BundleId, "[4.*, 5.0.0)");
            var environment = GetTestAppServiceEnvironment();
            var version = "4.2.0";

            var handler = new TransientZipFailureHandler(version, zipFailuresBeforeSuccess: 10); // exceed retry budget
            var services = new ServiceCollection();
            services.AddLogging();

#pragma warning disable SA1313 // Parameter names should begin with lower-case letter
            services.AddHttpClient(nameof(ExtensionBundleManager))
                .ConfigurePrimaryHttpMessageHandler(() => handler)
                .AddPolicyHandler(HttpPolicyExtensions
                    .HandleTransientHttpError()
                    .OrResult(resp => resp.StatusCode == HttpStatusCode.TooManyRequests)
                    .WaitAndRetryAsync(
                        retryCount: 4,
                        sleepDurationProvider: _ => TimeSpan.Zero,
                        onRetry: (_, __, ___, ____) => { }));
#pragma warning restore SA1313 // Parameter names should begin with lower-case letter

            var provider = services.BuildServiceProvider();
            var factory = provider.GetRequiredService<IHttpClientFactory>();
            var manager = new ExtensionBundleManager(options, environment, MockNullLoggerFactory.CreateLoggerFactory(), new FunctionsHostingConfigOptions(), factory);

            var path = await manager.GetExtensionBundlePath();

            Assert.Null(path);
            Assert.Equal(1, handler.IndexAttempts);
            Assert.Equal(5, handler.ZipAttempts); // initial + 4 retries
            Assert.False(Directory.Exists(Path.Combine(options.DownloadPath, version)));
        }

        [Fact]
        public async Task TryDownloadZip_HttpFailure_LogsAzureRef_AndReturnsNull()
        {
            var options = GetTestExtensionBundleOptions(BundleId, "[2.*, 3.0.0)");
            var environment = GetTestAppServiceEnvironment();

            const string version = "2.0.1";
            const string azureRef = "test-ref-http-123";

            var capturingLogger = new CapturingLogger();
            var capturingFactory = new CapturingLoggerFactory(capturingLogger);

            var manager = GetExtensionBundleManager(options, environment, new Mock<ILoggerFactory>());
            // Override with capturing factory
            manager = new ExtensionBundleManager(options, environment, capturingFactory, new FunctionsHostingConfigOptions(), new Mock<IHttpClientFactory>().Object);

            using var http = new HttpClient(new HttpFailureWithAzureRefHandler(version, azureRef));
            var path = await manager.GetExtensionBundlePath(http);

            Assert.Null(path);
            Assert.Contains(capturingLogger.Entries, e => e.Level == LogLevel.Error && e.State.TryGetValue("azureRef", out var v) && string.Equals(v?.ToString(), azureRef, StringComparison.Ordinal));
        }

        [Fact]
        public async Task TryDownloadZip_IOFailure_LogsAzureRef_AndReturnsNull()
        {
            var options = GetTestExtensionBundleOptions(BundleId, "[2.*, 3.0.0)");
            var environment = GetTestAppServiceEnvironment();

            const string version = "2.0.1";
            const string azureRef = "test-ref-io-456";

            var capturingLogger = new CapturingLogger();
            var capturingFactory = new CapturingLoggerFactory(capturingLogger);

            var manager = new ExtensionBundleManager(options, environment, capturingFactory, new FunctionsHostingConfigOptions(), new Mock<IHttpClientFactory>().Object);

            using var http = new HttpClient(new IoFailureWithAzureRefHandler(version, azureRef));
            var path = await manager.GetExtensionBundlePath(http);

            Assert.Null(path);
            Assert.Contains(capturingLogger.Entries, e => e.Level == LogLevel.Error && e.State.TryGetValue("azureRef", out var v) && string.Equals(v?.ToString(), azureRef, StringComparison.Ordinal));
        }

        [Fact]
        public async Task TryDownloadZip_UnexpectedFailure_LogsUnexpected_AndReturnsNull()
        {
            var options = GetTestExtensionBundleOptions(BundleId, "[2.*, 3.0.0)");
            var environment = GetTestAppServiceEnvironment();

            var capturingLogger = new CapturingLogger();
            var capturingFactory = new CapturingLoggerFactory(capturingLogger);

            var manager = new ExtensionBundleManager(options, environment, capturingFactory, new FunctionsHostingConfigOptions(), new Mock<IHttpClientFactory>().Object);

            using var http = new HttpClient(new UnexpectedContentHandler("2.0.1"));
            var path = await manager.GetExtensionBundlePath(http);

            Assert.Null(path);
            Assert.Contains(capturingLogger.Entries, e => e.Level == LogLevel.Error && e.Message.Contains("Unexpected error downloading extension bundle Zip content", StringComparison.Ordinal));
        }

        private ExtensionBundleManager GetExtensionBundleManager(ExtensionBundleOptions bundleOptions, TestEnvironment environment = null, Mock<ILoggerFactory> mockLoggerFactory = null)
        {
            environment = environment ?? new TestEnvironment();

            var httpClientFactory = new Mock<IHttpClientFactory>();
            httpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient());

            if (mockLoggerFactory is null)
            {
                return new ExtensionBundleManager(bundleOptions, environment, MockNullLoggerFactory.CreateLoggerFactory(), new FunctionsHostingConfigOptions(), httpClientFactory.Object);
            }
            else
            {
                return new ExtensionBundleManager(bundleOptions, environment, mockLoggerFactory.Object, new FunctionsHostingConfigOptions(), httpClientFactory.Object);
            }
        }

        private TestEnvironment GetTestAppServiceEnvironment(string platformReleaseChannel = null)
        {
            var environment = new TestEnvironment();
            environment.SetEnvironmentVariable(AntaresPlatformReleaseChannel, platformReleaseChannel);
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

        private IList<string> GetLargeVersionsList()
        {
            return new List<string>()
            { "3.7.0", "3.10.0", "3.11.0", "3.15.0", "3.14.0", "2.16.0", "3.13.0", "3.12.0", "3.9.1", "2.12.1", "2.18.0", "3.16.0", "2.19.0", "3.17.0", "4.0.2", "2.20.0", "3.18.0", "4.1.0", "4.2.0", "2.21.0", "3.19.0", "3.19.2", "4.3.0", "3.20.0" };
        }

        private Mock<ILogger> GetVerifiableMockLogger(string stringToVerify, LogLevel logLevel)
        {
            var mockLogger = new Mock<ILogger>();
            mockLogger.Setup(l => l.IsEnabled(logLevel)).Returns(true);
            mockLogger
                .Setup(x => x.Log(
                    logLevel,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains(stringToVerify)),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()))
                .Verifiable();
            return mockLogger;
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
                                       ? new StringContent("[ \"2.0.0\", \"2.0.1\", \"2.0.0\" ]")
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

        // Primary handler simulating transient zip download failures to exercise retry policy.
        private sealed class TransientZipFailureHandler(string version, int zipFailuresBeforeSuccess) : HttpMessageHandler
        {
            private readonly string _version = version;
            private int _zipFailuresRemaining = zipFailuresBeforeSuccess;

            public int IndexAttempts { get; private set; }

            public int ZipAttempts { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var path = request.RequestUri.AbsolutePath;
                if (path.EndsWith("index.json", StringComparison.OrdinalIgnoreCase))
                {
                    IndexAttempts++;
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("[ \"4.1.0\", \"4.2.0\" ]")
                    });
                }

                if (path.Contains($"{BundleId}.{_version}", StringComparison.OrdinalIgnoreCase))
                {
                    ZipAttempts++;
                    if (_zipFailuresRemaining > 0)
                    {
                        _zipFailuresRemaining--;
                        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
                    }

                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = CreateBundleZipContent()
                    });
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            private static HttpContent CreateBundleZipContent()
            {
                var ms = new MemoryStream();
                using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
                {
                    var entry = zip.CreateEntry("bundle.json");
                    using var entryStream = entry.Open();
                    using var sw = new StreamWriter(entryStream);
                    sw.Write("{ id: \"Microsoft.Azure.Functions.ExtensionBundle\" }");
                }
                ms.Seek(0, SeekOrigin.Begin);
                return new StreamContent(ms);
            }
        }

        // Insert new helper sealed classes immediately after the existing sealed class.
        private sealed class HttpFailureWithAzureRefHandler(string version, string azureRef) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var path = request.RequestUri.AbsolutePath;

                if (path.IndexOf("index.json", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("[ \"2.0.1\" ]")
                    });
                }

                if (path.Contains($"{BundleId}.{version}", StringComparison.OrdinalIgnoreCase))
                {
                    var resp = new HttpResponseMessage(HttpStatusCode.InternalServerError);
                    resp.Headers.Add(ExtensionBundleHttpExtensions.AzureRefHeaderName, azureRef);
                    return Task.FromResult(resp);
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }
        }

        private sealed class IoFailureWithAzureRefHandler(string version, string azureRef) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var path = request.RequestUri.AbsolutePath;

                if (path.EndsWith("index.json", StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("[ \"2.0.1\" ]")
                    });
                }

                if (path.Contains($"{BundleId}.{version}", StringComparison.OrdinalIgnoreCase))
                {
                    var resp = new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ThrowingContent()
                    };
                    resp.Headers.Add(ExtensionBundleHttpExtensions.AzureRefHeaderName, azureRef);
                    return Task.FromResult(resp);
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }
        }

        private sealed class ThrowingContent : HttpContent
        {
            protected override Task SerializeToStreamAsync(Stream stream, TransportContext context)
                => throw new IOException("Simulated IO failure during content copy.");

            protected override bool TryComputeLength(out long length)
            {
                length = 0;
                return false;
            }
        }

        private sealed class ThrowingContentUnexpected : HttpContent
        {
            protected override Task SerializeToStreamAsync(Stream stream, TransportContext context)
                => throw new InvalidOperationException("Simulated unexpected failure during content copy.");

            protected override bool TryComputeLength(out long length)
            {
                length = 0;
                return false;
            }
        }

        private sealed class UnexpectedContentHandler(string version) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var path = request.RequestUri.AbsolutePath;
                if (path.IndexOf("index.json", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("[ \"" + version + "\" ]")
                    });
                }

                if (path.Contains($"{BundleId}.{version}", StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ThrowingContentUnexpected()
                    });
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }
        }

        private sealed class CapturingLogger : ILogger
        {
            public List<(LogLevel Level, Dictionary<string, object> State, Exception Exception, string Message)> Entries { get; } = new();

            public IDisposable BeginScope<TState>(TState state) => NullDisposable.Instance;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
            {
                var dict = new Dictionary<string, object>(StringComparer.Ordinal);
                if (state is IEnumerable<KeyValuePair<string, object>> kvps)
                {
                    foreach (var kv in kvps)
                    {
                        dict[kv.Key] = kv.Value;
                    }
                }
                Entries.Add((logLevel, dict, exception, formatter?.Invoke(state, exception) ?? state?.ToString()));
            }

            private sealed class NullDisposable : IDisposable
            {
                public static readonly NullDisposable Instance = new NullDisposable();

                public void Dispose() { }
            }
        }

        private sealed class CapturingLoggerFactory(CapturingLogger logger) : ILoggerFactory
        {
            public void AddProvider(ILoggerProvider provider) { }

            public ILogger CreateLogger(string categoryName) => logger;

            public void Dispose() { }
        }
    }
}
