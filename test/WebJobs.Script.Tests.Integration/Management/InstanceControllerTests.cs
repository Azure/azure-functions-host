// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Controllers;
using Microsoft.Azure.WebJobs.Script.WebHost.Management;
using Microsoft.Azure.WebJobs.Script.WebHost.Management.LinuxSpecialization;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Azure.WebJobs.Script.WebHost.Security;
using Microsoft.Extensions.Logging;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Moq.Protected;
using Newtonsoft.Json;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Managment
{
    [Trait(TestTraits.Category, TestTraits.EndToEnd)]
    [Trait(TestTraits.Group, TestTraits.ContainerInstanceTests)]
    public class InstanceControllerTests
    {
        private readonly TestOptionsFactory<ScriptApplicationHostOptions> _optionsFactory = new TestOptionsFactory<ScriptApplicationHostOptions>(new ScriptApplicationHostOptions());
        private readonly Mock<IRunFromPackageHandler> _runFromPackageHandler;

        public InstanceControllerTests()
        {
            _runFromPackageHandler = new Mock<IRunFromPackageHandler>(MockBehavior.Strict);
        }

        [Fact]
        public async Task Assign_MSISpecializationFailure_ReturnsError()
        {
            var environment = new TestEnvironment();
            environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");

            var scriptWebEnvironment = new ScriptWebHostEnvironment(environment);

            var loggerFactory = new LoggerFactory();
            var loggerProvider = new TestLoggerProvider();
            loggerFactory.AddProvider(loggerProvider);

            var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);

            handlerMock.Protected().Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()).ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.BadRequest
                });

            var meshServiceClient = new Mock<IMeshServiceClient>(MockBehavior.Strict);
            meshServiceClient.Setup(c => c.NotifyHealthEvent(ContainerHealthEventType.Fatal,
                It.Is<Type>(t => t == typeof(AtlasInstanceManager)), "Failed to specialize MSI sidecar")).Returns(Task.CompletedTask);

            var instanceManager = new AtlasInstanceManager(_optionsFactory, TestHelpers.CreateHttpClientFactory(handlerMock.Object),
                scriptWebEnvironment, environment, loggerFactory.CreateLogger<AtlasInstanceManager>(),
                new TestMetricsLogger(), meshServiceClient.Object, _runFromPackageHandler.Object, new Mock<IPackageDownloadHandler>(MockBehavior.Strict).Object);
            var startupContextProvider = new StartupContextProvider(environment, loggerFactory.CreateLogger<StartupContextProvider>());

            instanceManager.Reset();

            var instanceController = new InstanceController(environment, instanceManager, loggerFactory, startupContextProvider);

            const string containerEncryptionKey = "/a/vXvWJ3Hzgx4PFxlDUJJhQm5QVyGiu0NNLFm/ZMMg=";
            var hostAssignmentContext = new HostAssignmentContext
            {
                Environment = new Dictionary<string, string>(),
                MSIContext = new MSIContext()
            };

            hostAssignmentContext.Environment[EnvironmentSettingNames.MsiEndpoint] = "http://localhost:8081";
            hostAssignmentContext.Environment[EnvironmentSettingNames.MsiSecret] = "secret";

            var encryptedHostAssignmentValue = SimpleWebTokenHelper.Encrypt(JsonConvert.SerializeObject(hostAssignmentContext), containerEncryptionKey.ToKeyBytes());

            var encryptedHostAssignmentContext = new EncryptedHostAssignmentContext()
            {
                EncryptedContext = encryptedHostAssignmentValue
            };

            environment.SetEnvironmentVariable(EnvironmentSettingNames.ContainerEncryptionKey, containerEncryptionKey);

            IActionResult result = await instanceController.Assign(encryptedHostAssignmentContext);

            var objectResult = result as ObjectResult;

            Assert.Equal(objectResult.StatusCode, 500);
            Assert.Equal(objectResult.Value, "Specialize MSI sidecar call failed. StatusCode=BadRequest");

            meshServiceClient.Verify(c => c.NotifyHealthEvent(ContainerHealthEventType.Fatal,
                It.Is<Type>(t => t == typeof(AtlasInstanceManager)), "Failed to specialize MSI sidecar"), Times.Once);
        }

        [Fact]
        public void Http_Health_Status_Returns_Ok()
        {
            var loggerFactory = new LoggerFactory();
            var loggerProvider = new TestLoggerProvider();
            loggerFactory.AddProvider(loggerProvider);

            var instanceController = new InstanceController(null, null, loggerFactory, null);
        
            var actionResult = instanceController.GetHttpHealthStatus();
            var okResult = actionResult as OkResult;

            Assert.NotNull(okResult);
            Assert.Equal(200, okResult.StatusCode);
        }

        [Fact]
        public async Task Assignment_Sets_Secrets_Context()
        {
            var environment = new TestEnvironment();
            environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");

            var scriptWebEnvironment = new ScriptWebHostEnvironment(environment);

            var loggerFactory = new LoggerFactory();
            var loggerProvider = new TestLoggerProvider();
            loggerFactory.AddProvider(loggerProvider);

            var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);

            handlerMock.Protected().Setup<Task<HttpResponseMessage>>("SendAsync",
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>()).ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK
            });

            var instanceManager = new AtlasInstanceManager(_optionsFactory, TestHelpers.CreateHttpClientFactory(handlerMock.Object),
                scriptWebEnvironment, environment, loggerFactory.CreateLogger<AtlasInstanceManager>(),
                new TestMetricsLogger(), null, _runFromPackageHandler.Object, new Mock<IPackageDownloadHandler>(MockBehavior.Strict).Object);
            var startupContextProvider = new StartupContextProvider(environment, loggerFactory.CreateLogger<StartupContextProvider>());

            instanceManager.Reset();

            var instanceController = new InstanceController(environment, instanceManager, loggerFactory, startupContextProvider);

            const string containerEncryptionKey = "/a/vXvWJ3Hzgx4PFxlDUJJhQm5QVyGiu0NNLFm/ZMMg=";
            var hostAssignmentContext = new HostAssignmentContext
            {
                Environment = new Dictionary<string, string>()
                {
                    [EnvironmentSettingNames.AzureWebsiteRunFromPackage] = "http://localhost:1234"
                }
            };
            hostAssignmentContext.Secrets = new FunctionAppSecrets();
            hostAssignmentContext.IsWarmupRequest = false; // non-warmup Request

            var encryptedHostAssignmentValue = SimpleWebTokenHelper.Encrypt(JsonConvert.SerializeObject(hostAssignmentContext), containerEncryptionKey.ToKeyBytes());

            var encryptedHostAssignmentContext = new EncryptedHostAssignmentContext()
            {
                EncryptedContext = encryptedHostAssignmentValue
            };

            environment.SetEnvironmentVariable(EnvironmentSettingNames.ContainerEncryptionKey, containerEncryptionKey);

            await instanceController.Assign(encryptedHostAssignmentContext);
            Assert.NotNull(startupContextProvider.Context);
        }

        [Fact]
        public async Task Assignment_Does_Not_Set_Secrets_Context_For_Warmup_Request()
        {
            var environment = new TestEnvironment();
            environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");

            var scriptWebEnvironment = new ScriptWebHostEnvironment(environment);

            var loggerFactory = new LoggerFactory();
            var loggerProvider = new TestLoggerProvider();
            loggerFactory.AddProvider(loggerProvider);

            var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);

            handlerMock.Protected().Setup<Task<HttpResponseMessage>>("SendAsync",
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>()).ReturnsAsync(new HttpResponseMessage
            {
            StatusCode = HttpStatusCode.OK
            });

            var instanceManager = new AtlasInstanceManager(_optionsFactory, TestHelpers.CreateHttpClientFactory(handlerMock.Object),
                scriptWebEnvironment, environment, loggerFactory.CreateLogger<AtlasInstanceManager>(),
                new TestMetricsLogger(), null, _runFromPackageHandler.Object, new Mock<IPackageDownloadHandler>(MockBehavior.Strict).Object);
            var startupContextProvider = new StartupContextProvider(environment, loggerFactory.CreateLogger<StartupContextProvider>());

            instanceManager.Reset();

            var instanceController = new InstanceController(environment, instanceManager, loggerFactory, startupContextProvider);

            const string containerEncryptionKey = "/a/vXvWJ3Hzgx4PFxlDUJJhQm5QVyGiu0NNLFm/ZMMg=";
            var hostAssignmentContext = new HostAssignmentContext
            {
                Environment = new Dictionary<string, string>()
                    {
                        [EnvironmentSettingNames.AzureWebsiteRunFromPackage] = "http://localhost:1234"
                    }
            };
            hostAssignmentContext.Secrets = new FunctionAppSecrets();
            hostAssignmentContext.IsWarmupRequest = true; // Warmup Request

            var encryptedHostAssignmentValue = SimpleWebTokenHelper.Encrypt(JsonConvert.SerializeObject(hostAssignmentContext), containerEncryptionKey.ToKeyBytes());

            var encryptedHostAssignmentContext = new EncryptedHostAssignmentContext()
            {
                EncryptedContext = encryptedHostAssignmentValue
            };

            environment.SetEnvironmentVariable(EnvironmentSettingNames.ContainerEncryptionKey, containerEncryptionKey);

            await instanceController.Assign(encryptedHostAssignmentContext);
            Assert.Null(startupContextProvider.Context);
        }

        [Theory]
        [InlineData(true, true)]
        [InlineData(false, true)]
        public async Task Assignment_Invokes_InstanceManager_Methods_For_Warmup_Requests_Also(bool isWarmupRequest, bool shouldInvokeMethod)
        {
            var environment = new TestEnvironment();
            environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");
            
            var loggerFactory = new LoggerFactory();
            var loggerProvider = new TestLoggerProvider();
            loggerFactory.AddProvider(loggerProvider);

            var instanceManager = new Mock<IInstanceManager>();
            var startupContextProvider = new StartupContextProvider(environment, loggerFactory.CreateLogger<StartupContextProvider>());

            instanceManager.Reset();

            var instanceController = new InstanceController(environment, instanceManager.Object, loggerFactory,
                startupContextProvider);

            const string containerEncryptionKey = "/a/vXvWJ3Hzgx4PFxlDUJJhQm5QVyGiu0NNLFm/ZMMg=";
            var hostAssignmentContext = new HostAssignmentContext
            {
                Environment = new Dictionary<string, string>()
            };
            hostAssignmentContext.IsWarmupRequest = isWarmupRequest;

            var encryptedHostAssignmentValue =
                SimpleWebTokenHelper.Encrypt(JsonConvert.SerializeObject(hostAssignmentContext),
                    containerEncryptionKey.ToKeyBytes());

            var encryptedHostAssignmentContext = new EncryptedHostAssignmentContext()
            {
                EncryptedContext = encryptedHostAssignmentValue
            };

            environment.SetEnvironmentVariable(EnvironmentSettingNames.ContainerEncryptionKey, containerEncryptionKey);
            
            await instanceController.Assign(encryptedHostAssignmentContext);

            instanceManager.Verify(i => i.ValidateContext(It.IsAny<HostAssignmentContext>()),
                shouldInvokeMethod ? Times.Once() : Times.Never());
            instanceManager.Verify(i => i.SpecializeMSISidecar(It.IsAny<HostAssignmentContext>()),
                shouldInvokeMethod ? Times.Once() : Times.Never());
            instanceManager.Verify(i => i.StartAssignment(It.IsAny<HostAssignmentContext>()),
                shouldInvokeMethod ? Times.Once() : Times.Never());
        }
    }
}
