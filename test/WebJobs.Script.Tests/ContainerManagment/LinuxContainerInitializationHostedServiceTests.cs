// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.ContainerManagement;
using Microsoft.Azure.WebJobs.Script.WebHost.Management;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Extensions.Logging;
using Moq;
using Newtonsoft.Json;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.ContainerManagment
{
    public class LinuxContainerInitializationHostedServiceTests
    {
        private readonly Mock<IInstanceManager> _mockInstanceManager;
        private readonly Mock<ILogger<LinuxContainerInitializationHostedService>> _mockLogger;
        private readonly Mock<ILogger<StartupContextProvider>> _mockStartupContextProviderLogger;
        private readonly Mock<StartupContextProvider> _mockStartupContextProvider;

        public LinuxContainerInitializationHostedServiceTests()
        {
            _mockInstanceManager = new Mock<IInstanceManager>();
            _mockLogger = new Mock<ILogger<LinuxContainerInitializationHostedService>>();
            _mockStartupContextProviderLogger = new Mock<ILogger<StartupContextProvider>>();
            _mockStartupContextProvider = new Mock<StartupContextProvider>(GetTestEnvironment(), _mockStartupContextProviderLogger.Object);
        }

        [Fact]
        public async Task AssignInstanceAsyncIsAwaitedTest()
        {
            TaskCompletionSource<bool> tcs = new();
            _mockInstanceManager.Setup(m => m.AssignInstanceAsync(It.IsAny<HostAssignmentContext>())).Returns(tcs.Task);

            var assignmentContext = new HostAssignmentContext();
            _mockStartupContextProvider.Setup(p => p.SetContext(It.IsAny<EncryptedHostAssignmentContext>())).Returns(assignmentContext);

            var service = new TestLinuxContainerInitializationHostedService(GetTestEnvironment(), _mockInstanceManager.Object,
                _mockLogger.Object, _mockStartupContextProvider.Object);

            Task task = service.StartAsync(default); // start the call

            Assert.False(task.IsCompleted); // verify this is not complete.
            tcs.SetResult(true);
            await task.WaitAsync(TimeSpan.FromMilliseconds(10));
        }

        private static TestEnvironment GetTestEnvironment()
        {
            var environment = new TestEnvironment();
            environment.SetEnvironmentVariable(EnvironmentSettingNames.ContainerName, "TestContainerName");

            return environment;
        }

        public class TestLinuxContainerInitializationHostedService : LinuxContainerInitializationHostedService
        {
            public TestLinuxContainerInitializationHostedService(IEnvironment environment, IInstanceManager instanceManager, ILogger logger, StartupContextProvider startupContextProvider) : base(environment, instanceManager, logger, startupContextProvider)
            {
            }

            protected override Task<(bool HasStartContext, string StartContext)> TryGetStartContextOrNullAsync(CancellationToken cancellationToken)
            {
                var encryptedAssignmentContext = new EncryptedHostAssignmentContext { EncryptedContext = "test" };

                return Task.FromResult((true, JsonConvert.SerializeObject(encryptedAssignmentContext)));
            }

            protected override Task SpecializeMSISideCar(HostAssignmentContext assignmentContext)
            {
                return Task.CompletedTask;
            }
        }
    }
}
