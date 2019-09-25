// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.WebHost.ContainerManagement;
using Microsoft.Azure.WebJobs.Script.WebHost.Management;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Azure.WebJobs.Script.WebHost.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Newtonsoft.Json;
using Xunit;
using static Microsoft.Azure.WebJobs.Script.EnvironmentSettingNames;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration.ContainerManagement
{
    [Trait(TestTraits.Category, TestTraits.EndToEnd)]
    [Trait(TestTraits.Group, TestTraits.ContainerInstanceTests)]
    public class LinuxContainerInitializationHostServiceTests : IDisposable
    {
        private const string ContainerStartContextUri = "https://containerstartcontexturi";
        private readonly Mock<IInstanceManager> _instanceManagerMock;

        public LinuxContainerInitializationHostServiceTests()
        {
            _instanceManagerMock = new Mock<IInstanceManager>(MockBehavior.Strict);
        }

        [Fact]
        public async Task Runs_In_Linux_Container_Mode_Only()
        {
            // These settings being null will cause IsLinuxContainerEnvironment to return false.
            var environmentMock = new Mock<IEnvironment>(MockBehavior.Strict);
            environmentMock.Setup(env => env.GetEnvironmentVariable(ContainerName)).Returns<string>(null);
            environmentMock.Setup(env => env.GetEnvironmentVariable(AzureWebsiteInstanceId)).Returns<string>(null);

            var initializationHostService = new LinuxContainerInitializationHostService(environmentMock.Object, _instanceManagerMock.Object, NullLogger<LinuxContainerInitializationHostService>.Instance);
            await initializationHostService.StartAsync(CancellationToken.None);

            // Make sure no other environment variables were checked
            environmentMock.Verify(env => env.GetEnvironmentVariable(It.Is<string>(p => p != ContainerName && p != AzureWebsiteInstanceId)), Times.Never);
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
                { ContainerStartContext, serializedContext },
                { ContainerEncryptionKey, containerEncryptionKey },
            };

            // Enable Linux Container
            AddLinuxContainerSettings(vars);

            _instanceManagerMock.Setup(manager => manager.StartAssignment(It.Is<HostAssignmentContext>(context => hostAssignmentContext.Equals(context)), It.Is<bool>(w => !w))).Returns(true);

            var environment = new TestEnvironment(vars);
            var initializationHostService = new LinuxContainerInitializationHostService(environment, _instanceManagerMock.Object, NullLogger<LinuxContainerInitializationHostService>.Instance);
            await initializationHostService.StartAsync(CancellationToken.None);

            _instanceManagerMock.Verify(manager => manager.StartAssignment(It.Is<HostAssignmentContext>(context => hostAssignmentContext.Equals(context)), It.Is<bool>(w => !w)), Times.Once);
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
                { ContainerStartContextSasUri, ContainerStartContextUri },
                { ContainerEncryptionKey, containerEncryptionKey },
            };

            AddLinuxContainerSettings(vars);

            var environment = new TestEnvironment(vars);

            var initializationHostService = new Mock<LinuxContainerInitializationHostService>(MockBehavior.Strict, environment, _instanceManagerMock.Object, NullLogger<LinuxContainerInitializationHostService>.Instance);

            initializationHostService.Setup(service => service.Read(ContainerStartContextUri))
                .Returns(Task.FromResult(serializedContext));

            _instanceManagerMock.Setup(manager => manager.StartAssignment(It.Is<HostAssignmentContext>(context => hostAssignmentContext.Equals(context)), It.Is<bool>(w => !w))).Returns(true);

            using (var env = new TestScopedEnvironmentVariable(vars))
            {
                await initializationHostService.Object.StartAsync(CancellationToken.None);
            }

            _instanceManagerMock.Verify(manager => manager.StartAssignment(It.Is<HostAssignmentContext>(context => hostAssignmentContext.Equals(context)), It.Is<bool>(w => !w)), Times.Once);
        }

        [Fact]
        public async Task Does_Not_Assign_If_Context_Not_Available()
        {
            var vars = new Dictionary<string, string>();
            AddLinuxContainerSettings(vars);

            var environment = new TestEnvironment(vars);
            var initializationHostService = new LinuxContainerInitializationHostService(environment, _instanceManagerMock.Object, NullLogger<LinuxContainerInitializationHostService>.Instance);
            await initializationHostService.StartAsync(CancellationToken.None);

            _instanceManagerMock.Verify(manager => manager.StartAssignment(It.IsAny<HostAssignmentContext>(), It.Is<bool>(w => !w)), Times.Never);
        }

        private static string GetEncryptedHostAssignmentContext(HostAssignmentContext hostAssignmentContext, string containerEncryptionKey)
        {
            using (var env = new TestScopedEnvironmentVariable(WebSiteAuthEncryptionKey, containerEncryptionKey))
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
            hostAssignmentContext.Environment.Add(AzureWebsiteAltZipDeployment, "https://zipurl.zip");
            return hostAssignmentContext;
        }

        private static void AddLinuxContainerSettings(IDictionary<string, string> existing)
        {
            existing[AzureWebsiteInstanceId] = string.Empty;
            existing[ContainerName] = "ContainerName";
        }

        public void Dispose()
        {
            _instanceManagerMock.Reset();
        }
    }
}
