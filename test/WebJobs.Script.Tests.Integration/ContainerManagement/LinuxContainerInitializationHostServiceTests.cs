﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.ContainerManagement;
using Microsoft.Azure.WebJobs.Script.WebHost.Management;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Azure.WebJobs.Script.WebHost.Security;
using Microsoft.Extensions.Logging;
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
        private readonly StartupContextProvider _startupContextProvider;
        private readonly TestEnvironment _environment;

        public LinuxContainerInitializationHostServiceTests()
        {
            _instanceManagerMock = new Mock<IInstanceManager>(MockBehavior.Strict);

            _environment = new TestEnvironment();
            var loggerFactory = new LoggerFactory();
            var loggerProvider = new TestLoggerProvider();
            loggerFactory.AddProvider(loggerProvider);

            _startupContextProvider = new StartupContextProvider(_environment, loggerFactory.CreateLogger<StartupContextProvider>());
        }

        [Fact]
        public async Task Runs_In_Linux_Container_Mode_Only()
        {
            _environment.SetEnvironmentVariable(ContainerName, null);
            _environment.SetEnvironmentVariable(AzureWebsiteInstanceId, null);
            Assert.False(_environment.IsLinuxConsumption());

            var initializationHostService = new LinuxContainerInitializationHostService(_environment, _instanceManagerMock.Object, NullLogger<LinuxContainerInitializationHostService>.Instance, _startupContextProvider);
            await initializationHostService.StartAsync(CancellationToken.None);
        }

        [Fact]
        public async Task Assigns_Context_From_CONTAINER_START_CONTEXT()
        {
            var containerEncryptionKey = TestHelpers.GenerateKeyHexString();
            var hostAssignmentContext = GetHostAssignmentContext();
            var secrets = new FunctionAppSecrets();
            secrets.Host = new FunctionAppSecrets.HostSecrets
            {
                Master = "test-key",
                Function = new Dictionary<string, string>
                {
                    { "host-function-key-1", "test-key" }
                },
                System = new Dictionary<string, string>
                {
                    { "host-system-key-1", "test-key" }
                }
            };
            hostAssignmentContext.Secrets = secrets;
            hostAssignmentContext.MSIContext = new MSIContext();

            var encryptedHostAssignmentContext = GetEncryptedHostAssignmentContext(hostAssignmentContext, containerEncryptionKey);
            var serializedContext = JsonConvert.SerializeObject(new { encryptedContext = encryptedHostAssignmentContext });

            _environment.SetEnvironmentVariable(ContainerStartContext, serializedContext);
            _environment.SetEnvironmentVariable(ContainerEncryptionKey, containerEncryptionKey);
            AddLinuxConsumptionSettings(_environment);

            _instanceManagerMock.Setup(m =>
                m.SpecializeMSISidecar(It.Is<HostAssignmentContext>(context =>
                    hostAssignmentContext.Equals(context) && !context.IsWarmupRequest))).Returns(Task.FromResult(string.Empty));

            _instanceManagerMock.Setup(manager => manager.StartAssignment(It.Is<HostAssignmentContext>(context => hostAssignmentContext.Equals(context) && !context.IsWarmupRequest))).Returns(true);

            var initializationHostService = new LinuxContainerInitializationHostService(_environment, _instanceManagerMock.Object, NullLogger<LinuxContainerInitializationHostService>.Instance, _startupContextProvider);
            await initializationHostService.StartAsync(CancellationToken.None);

            _instanceManagerMock.Verify(m =>
                m.SpecializeMSISidecar(It.Is<HostAssignmentContext>(context =>
                    hostAssignmentContext.Equals(context) && !context.IsWarmupRequest)), Times.Once);

            _instanceManagerMock.Verify(manager => manager.StartAssignment(It.Is<HostAssignmentContext>(context => hostAssignmentContext.Equals(context) && !context.IsWarmupRequest)), Times.Once);

            var hostSecrets = _startupContextProvider.GetHostSecretsOrNull();
            Assert.Equal("test-key", hostSecrets.MasterKey);
        }

        [Fact]
        public async Task Assigns_Context_From_CONTAINER_START_CONTEXT_SAS_URI_If_CONTAINER_START_CONTEXT_Absent()
        {
            var containerEncryptionKey = TestHelpers.GenerateKeyHexString();
            var hostAssignmentContext = GetHostAssignmentContext();
            hostAssignmentContext.MSIContext = new MSIContext();
            var encryptedHostAssignmentContext = GetEncryptedHostAssignmentContext(hostAssignmentContext, containerEncryptionKey);
            var serializedContext = JsonConvert.SerializeObject(new { encryptedContext = encryptedHostAssignmentContext });

            _environment.SetEnvironmentVariable(ContainerStartContextSasUri, ContainerStartContextUri);
            _environment.SetEnvironmentVariable(ContainerEncryptionKey, containerEncryptionKey);
            AddLinuxConsumptionSettings(_environment);

            var initializationHostService = new Mock<LinuxContainerInitializationHostService>(MockBehavior.Strict, _environment, _instanceManagerMock.Object, NullLogger<LinuxContainerInitializationHostService>.Instance, _startupContextProvider);

            initializationHostService.Setup(service => service.Read(ContainerStartContextUri))
                .Returns(Task.FromResult(serializedContext));

            _instanceManagerMock.Setup(m =>
                m.SpecializeMSISidecar(It.Is<HostAssignmentContext>(context =>
                    hostAssignmentContext.Equals(context) && !context.IsWarmupRequest))).Returns(Task.FromResult(string.Empty));

            _instanceManagerMock.Setup(manager => manager.StartAssignment(It.Is<HostAssignmentContext>(context => hostAssignmentContext.Equals(context) && !context.IsWarmupRequest))).Returns(true);

            await initializationHostService.Object.StartAsync(CancellationToken.None);

            _instanceManagerMock.Verify(m =>
                m.SpecializeMSISidecar(It.Is<HostAssignmentContext>(context =>
                    hostAssignmentContext.Equals(context) && !context.IsWarmupRequest)), Times.Once);

            _instanceManagerMock.Verify(manager => manager.StartAssignment(It.Is<HostAssignmentContext>(context => hostAssignmentContext.Equals(context) && !context.IsWarmupRequest)), Times.Once);
        }

        [Fact]
        public async Task Does_Not_Assign_If_Context_Not_Available()
        {
            var initializationHostService = new LinuxContainerInitializationHostService(_environment, _instanceManagerMock.Object, NullLogger<LinuxContainerInitializationHostService>.Instance, _startupContextProvider);
            await initializationHostService.StartAsync(CancellationToken.None);

            _instanceManagerMock.Verify(m => m.SpecializeMSISidecar(It.IsAny<HostAssignmentContext>()), Times.Never);
            _instanceManagerMock.Verify(manager => manager.StartAssignment(It.IsAny<HostAssignmentContext>()), Times.Never);
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

        private static void AddLinuxConsumptionSettings(IEnvironment environment)
        {
            environment.SetEnvironmentVariable(AzureWebsiteInstanceId, string.Empty);
            environment.SetEnvironmentVariable(ContainerName, "ContainerName");
        }

        public void Dispose()
        {
            _instanceManagerMock.Reset();
        }
    }
}
