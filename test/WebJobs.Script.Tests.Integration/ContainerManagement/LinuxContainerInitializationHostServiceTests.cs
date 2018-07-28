// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.WebHost.ContainerManagement;
using Microsoft.Azure.WebJobs.Script.WebHost.Management;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Azure.WebJobs.Script.WebHost.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Newtonsoft.Json;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration.ContainerManagement
{
    [Trait(TestTraits.Category, TestTraits.EndToEnd)]
    [Trait(TestTraits.Group, TestTraits.ContainerInstanceTests)]
    public class LinuxContainerInitializationHostServiceTests : IDisposable
    {
        private const string Containerstartcontexturi = "https://containerstartcontexturi";
        private readonly Mock<IInstanceManager> _instanceManagerMock;
        private readonly Mock<IEnvironment> _environment;
        private readonly LinuxContainerInitializationHostService _initializationHostService;

        public LinuxContainerInitializationHostServiceTests()
        {
            _instanceManagerMock = new Mock<IInstanceManager>(MockBehavior.Strict);
            _environment = new Mock<IEnvironment>(MockBehavior.Strict);
            _initializationHostService = new LinuxContainerInitializationHostService(_environment.Object, _instanceManagerMock.Object, NullLoggerFactory.Instance);
        }

        [Fact]
        public async Task Runs_In_Linux_Container_Mode_Only()
        {
            var variables = new Dictionary<string, string>
            {
                {EnvironmentSettingNames.ContainerName, "TEST" },
                {EnvironmentSettingNames.AzureWebsiteInstanceId, "TEST" },
            };
            using (new TestScopedEnvironmentVariable(variables))
            {
                var environmentMock = new Mock<IEnvironment>(MockBehavior.Strict, null);
                var initializationHostService = new LinuxContainerInitializationHostService(environmentMock.Object, _instanceManagerMock.Object, NullLoggerFactory.Instance);                
                await initializationHostService.StartAsync(CancellationToken.None);
                environmentMock.Verify(settingsManager => settingsManager.GetEnvironmentVariable(It.IsAny<string>()), Times.Never);
            }
        }

        [Fact]
        public async Task Assigns_Context_From_CONTAINER_START_CONTEXT()
        {
            var containerEncryptionKey = TestHelpers.GenerateKeyHexString();
            var hostAssignmentContext = GetHostAssignmentContext();
            var encryptedHostAssignmentContext = GetEncryptedHostAssignmentContext(hostAssignmentContext, containerEncryptionKey);
            var serializedContext = JsonConvert.SerializeObject(new { encryptedContext = encryptedHostAssignmentContext });

            var vars = new Dictionary<string, string>
            {
                { EnvironmentSettingNames.ContainerStartContext, serializedContext },
                { EnvironmentSettingNames.ContainerEncryptionKey, containerEncryptionKey },
            };

            // Enable Linux Container
            AddLinuxContainerSettings(vars);

            _instanceManagerMock.Setup(manager => manager.StartAssignment(It.Is<HostAssignmentContext>(context => hostAssignmentContext.Equals(context)))).Returns(true);

            using (var env = new TestScopedEnvironmentVariable(vars))
            {
                await _initializationHostService.StartAsync(CancellationToken.None);
            }

            _instanceManagerMock.Verify(manager => manager.StartAssignment(It.Is<HostAssignmentContext>(context => hostAssignmentContext.Equals(context))), Times.Once);
        }

        [Fact]
        public async Task Assigns_Context_From_CONTAINER_START_CONTEXT_SAS_URI_If_CONTAINER_START_CONTEXT_Absent()
        {
            var containerEncryptionKey = TestHelpers.GenerateKeyHexString();
            var hostAssignmentContext = GetHostAssignmentContext();
            var encryptedHostAssignmentContext = GetEncryptedHostAssignmentContext(hostAssignmentContext, containerEncryptionKey);
            var serializedContext = JsonConvert.SerializeObject(new { encryptedContext = encryptedHostAssignmentContext });

            var vars = new Dictionary<string, string>
            {
                { EnvironmentSettingNames.ContainerStartContextSasUri, Containerstartcontexturi },
                { EnvironmentSettingNames.ContainerEncryptionKey, containerEncryptionKey },
            };

            AddLinuxContainerSettings(vars);

            var initializationHostService = new Mock<LinuxContainerInitializationHostService>(MockBehavior.Strict, new ScriptSettingsManager(), _instanceManagerMock.Object, NullLoggerFactory.Instance);

            initializationHostService.Setup(service => service.Read(Containerstartcontexturi))
                .Returns(Task.FromResult(serializedContext));

            _instanceManagerMock.Setup(manager => manager.StartAssignment(It.Is<HostAssignmentContext>(context => hostAssignmentContext.Equals(context)))).Returns(true);

            using (var env = new TestScopedEnvironmentVariable(vars))
            {
                await initializationHostService.Object.StartAsync(CancellationToken.None);
            }

            _instanceManagerMock.Verify(manager => manager.StartAssignment(It.Is<HostAssignmentContext>(context => hostAssignmentContext.Equals(context))), Times.Once);
        }

        [Fact]
        public async Task Does_Not_Assign_If_Context_Not_Available()
        {
            var vars = new Dictionary<string, string>();
            AddLinuxContainerSettings(vars);

            using (var env = new TestScopedEnvironmentVariable(vars))
            {
                await _initializationHostService.StartAsync(CancellationToken.None);
            }

            _instanceManagerMock.Verify(manager => manager.StartAssignment(It.IsAny<HostAssignmentContext>()), Times.Never);
        }

        private static string GetEncryptedHostAssignmentContext(HostAssignmentContext hostAssignmentContext, string containerEncryptionKey)
        {
            using (var env = new TestScopedEnvironmentVariable(EnvironmentSettingNames.WebSiteAuthEncryptionKey, containerEncryptionKey))
            {
                var serializeObject = JsonConvert.SerializeObject(hostAssignmentContext);
                return SimpleWebTokenHelper.Encrypt(serializeObject);
            }
        }

        private static HostAssignmentContext GetHostAssignmentContext()
        {
            var hostAssignmentContext = new HostAssignmentContext();
            hostAssignmentContext.SiteId = 1;
            hostAssignmentContext.SiteName = "sitename";
            hostAssignmentContext.LastModifiedTime = DateTime.UtcNow.Add(TimeSpan.FromMinutes(new Random().Next()));
            hostAssignmentContext.Environment = new Dictionary<string, string>();
            hostAssignmentContext.Environment.Add(EnvironmentSettingNames.AzureWebsiteAltZipDeployment, "https://zipurl.zip");
            return hostAssignmentContext;
        }

        private static void AddLinuxContainerSettings(IDictionary<string, string> existing)
        {
            existing[EnvironmentSettingNames.AzureWebsiteInstanceId] = string.Empty;
            existing[EnvironmentSettingNames.ContainerName] = "ContainerName";
        }

        public void Dispose()
        {
            _instanceManagerMock.Reset();
        }
    }
}
