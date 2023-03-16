// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.Azure.WebJobs.Script.Properties;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration.Host
{
    public class HostIdValidatorTests
    {
        private readonly string _testHostId = "test-host-id";
        private readonly string _testHostname = "test-host.net";
        private readonly HostIdValidator _hostIdValidator;
        private readonly TestEnvironment _environment;
        private readonly TestLoggerProvider _loggerProvider;
        private readonly Mock<IApplicationLifetime> _mockApplicationLifetime;
        private BlobContainerClient _blobContainerClient;
        private bool _storageConfigured;

        public HostIdValidatorTests()
        {
            var options = new ScriptApplicationHostOptions();
            _environment = new TestEnvironment(new Dictionary<string, string>
            {
                { EnvironmentSettingNames.AzureWebsiteHostName, _testHostname }
            });

            _loggerProvider = new TestLoggerProvider();
            var loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(_loggerProvider);
            var logger = loggerFactory.CreateLogger<HostIdValidator>();

            var config = new ConfigurationBuilder()
                .AddEnvironmentVariables()
                .Build();
            var azureBlobStorageProvider = TestHelpers.GetAzureBlobStorageProvider(config);
            azureBlobStorageProvider.TryCreateHostingBlobContainerClient(out _blobContainerClient);

            var mockBlobStorageProvider = new Mock<IAzureBlobStorageProvider>(MockBehavior.Strict);
            mockBlobStorageProvider.Setup(p => p.TryCreateHostingBlobContainerClient(out _blobContainerClient)).Returns(() => _storageConfigured);

            var hostNameProvider = new HostNameProvider(_environment);
            _mockApplicationLifetime = new Mock<IApplicationLifetime>(MockBehavior.Strict);
            _hostIdValidator = new HostIdValidator(_environment, mockBlobStorageProvider.Object, _mockApplicationLifetime.Object, hostNameProvider, logger);

            _storageConfigured = true;
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(true, true)]
        public async Task ScheduleValidation_Succeeds(bool storageConfigured, bool expectHostIdInfo)
        {
            await ClearHostIdInfoAsync();

            _storageConfigured = storageConfigured;

            Utility.ColdStartDelayMS = 1000;

            _hostIdValidator.ScheduleValidation(_testHostId);

            await Task.Delay(2000);
            HostIdValidator.HostIdInfo hostIdInfo = await _hostIdValidator.ReadHostIdInfoAsync(_testHostId, _blobContainerClient);

            var logs = _loggerProvider.GetAllLogMessages();
            if (expectHostIdInfo)
            {
                Assert.Equal(_testHostname, hostIdInfo.Hostname);

                var log = logs.Single();
                Assert.Equal(LogLevel.Debug, log.Level);
                Assert.Equal("Host ID record written (ID:test-host-id, HostName:test-host.net)", log.FormattedMessage);
            }
            else
            {
                Assert.Null(hostIdInfo);
                Assert.Empty(logs);
            }
        }

        [Fact]
        public async Task ValidateHostIdUsageAsync_ExistingHostIdInfoMatches_Succeeds()
        {
            await ClearHostIdInfoAsync();

            HostIdValidator.HostIdInfo hostIdInfo = new HostIdValidator.HostIdInfo
            {
                Hostname = _testHostname
            };
            await _hostIdValidator.WriteHostIdAsync(_testHostId, hostIdInfo, _blobContainerClient);

            _loggerProvider.ClearAllLogMessages();

            await _hostIdValidator.ValidateHostIdUsageAsync(_testHostId);

            var logs = _loggerProvider.GetAllLogMessages();
            Assert.Empty(logs);
        }

        [Fact]
        public async Task ValidateHostIdUsageAsync_Collision_WarningLevel_Logs()
        {
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionsHostIdCheckLevel, LogLevel.Warning.ToString());

            await ClearHostIdInfoAsync();

            HostIdValidator.HostIdInfo hostIdInfo = new HostIdValidator.HostIdInfo
            {
                Hostname = "test-host2.net"
            };
            await _hostIdValidator.WriteHostIdAsync(_testHostId, hostIdInfo, _blobContainerClient);

            _loggerProvider.ClearAllLogMessages();

            await _hostIdValidator.ValidateHostIdUsageAsync(_testHostId);

            var logs = _loggerProvider.GetAllLogMessages();
            Assert.Equal(logs.Count, 2);
            var log = logs[0];
            Assert.Equal(LogLevel.Warning, log.Level);
            Assert.Equal(string.Format(Resources.HostIdCollisionFormat, _testHostId), log.FormattedMessage);

            var diagLog = logs[1];
            Assert.Equal(string.Format(Resources.HostIdCollisionFormat, _testHostId), diagLog.FormattedMessage);

            _mockApplicationLifetime.Verify(p => p.StopApplication(), Times.Never);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("Error")]
        [InlineData("None")]
        public async Task ValidateHostIdUsageAsync_Collision_ErrorLevel_LogsAndStopsApplication(string level)
        {
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionsHostIdCheckLevel, level);
            _mockApplicationLifetime.Setup(p => p.StopApplication());

            await ClearHostIdInfoAsync();

            HostIdValidator.HostIdInfo hostIdInfo = new HostIdValidator.HostIdInfo
            {
                Hostname = "test-host2.net"
            };
            await _hostIdValidator.WriteHostIdAsync(_testHostId, hostIdInfo, _blobContainerClient);

            _loggerProvider.ClearAllLogMessages();

            await _hostIdValidator.ValidateHostIdUsageAsync(_testHostId);

            var logs = _loggerProvider.GetAllLogMessages();
            Assert.Equal(logs.Count, 2);
            var log = logs[0];
            Assert.Equal(LogLevel.Error, log.Level);
            Assert.Equal(string.Format(Resources.HostIdCollisionFormat, _testHostId), log.FormattedMessage);

            var diagLog = logs[1];
            Assert.Equal(string.Format(Resources.HostIdCollisionFormat, _testHostId), diagLog.FormattedMessage);

            _mockApplicationLifetime.Verify(p => p.StopApplication(), Times.Once);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task WriteHostIdAsync_ExistingBlob_HandlesCollision(bool collision)
        {
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionsHostIdCheckLevel, LogLevel.Error.ToString());
            _mockApplicationLifetime.Setup(p => p.StopApplication());

            await ClearHostIdInfoAsync();

            // simulate a race condition where another instance has written the blob between the
            // time when we initially checked and when we attempted to write
            HostIdValidator.HostIdInfo hostIdInfo = new HostIdValidator.HostIdInfo
            {
                Hostname = collision ? "another-test-host.net" : _testHostname
            };
            await _hostIdValidator.WriteHostIdAsync(_testHostId, hostIdInfo, _blobContainerClient);

            _loggerProvider.ClearAllLogMessages();

            hostIdInfo = new HostIdValidator.HostIdInfo
            {
                Hostname = _testHostname
            };
            await _hostIdValidator.WriteHostIdAsync(_testHostId, hostIdInfo, _blobContainerClient);

            var logs = _loggerProvider.GetAllLogMessages();
            if (collision)
            {
                Assert.Equal(logs.Count, 2);
                var log = logs[0];
                Assert.Equal(LogLevel.Error, log.Level);
                Assert.Equal(string.Format(Resources.HostIdCollisionFormat, _testHostId), log.FormattedMessage);

                var diagLog = logs[1];
                Assert.Equal(string.Format(Resources.HostIdCollisionFormat, _testHostId), diagLog.FormattedMessage);

                _mockApplicationLifetime.Verify(p => p.StopApplication(), Times.Once);
            }
            else
            {
                Assert.Empty(logs);
            }
        }

        private async Task ClearHostIdInfoAsync()
        {
            string blobPath = string.Format(HostIdValidator.BlobPathFormat, _testHostId);
            BlobClient blobClient = _blobContainerClient.GetBlobClient(blobPath);

            await blobClient.DeleteIfExistsAsync();

            await TestHelpers.Await(async () =>
            {
                bool exists = await blobClient.ExistsAsync();
                return !exists;
            });
        }
    }
}
