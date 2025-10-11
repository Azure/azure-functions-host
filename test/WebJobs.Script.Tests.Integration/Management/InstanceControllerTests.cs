// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Controllers;
using Microsoft.Azure.WebJobs.Script.WebHost.Management;
using Microsoft.Azure.WebJobs.Script.WebHost.Management.LinuxSpecialization;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Azure.WebJobs.Script.WebHost.Security;
using Microsoft.Azure.WebJobs.Script.WebHost.Security.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Moq.Protected;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
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
            var testmetricslogger = new TestMetricsLogger();
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
                scriptWebEnvironment, environment, loggerFactory.CreateLogger<AtlasInstanceManager>(), testmetricslogger, meshServiceClient.Object, _runFromPackageHandler.Object, new Mock<IPackageDownloadHandler>(MockBehavior.Strict).Object);
            var startupContextProvider = new StartupContextProvider(environment, loggerFactory.CreateLogger<StartupContextProvider>());

            instanceManager.Reset();

            var instanceController = new InstanceController(environment, instanceManager, loggerFactory, startupContextProvider, testmetricslogger);

            var hostAssignmentContext = new HostAssignmentContext
            {
                Environment = new Dictionary<string, string>(),
                MSIContext = new MSIContext()
            };

            hostAssignmentContext.Environment[EnvironmentSettingNames.MsiEndpoint] = "http://localhost:8081";
            hostAssignmentContext.Environment[EnvironmentSettingNames.MsiSecret] = "secret";

            var encryptedHostAssignmentValue = EncryptionHelper.Encrypt(JsonConvert.SerializeObject(hostAssignmentContext), TestHelpers.EncryptionKey.ToKeyBytes());

            var hostAssignmentRequest = new HostAssignmentRequest()
            {
                EncryptedContext = encryptedHostAssignmentValue
            };

            environment.SetEnvironmentVariable(EnvironmentSettingNames.ContainerEncryptionKey, TestHelpers.EncryptionKey);

            IActionResult result = await instanceController.Assign(hostAssignmentRequest);

            var objectResult = result as ObjectResult;

            Assert.Equal(objectResult.StatusCode, 500);
            Assert.Equal(objectResult.Value, "Specialize MSI sidecar call failed. StatusCode=BadRequest");

            meshServiceClient.Verify(c => c.NotifyHealthEvent(ContainerHealthEventType.Fatal,
                It.Is<Type>(t => t == typeof(AtlasInstanceManager)), "Failed to specialize MSI sidecar"), Times.Once);
            Assert.True(areRequiredMetricsLogged(testmetricslogger, [MetricEventNames.LinuxContainerSpecializationAssign, MetricEventNames.LinuxContainerSpecializationMSIInit]));
        }

        [Fact]
        public void Http_Health_Status_Returns_Ok()
        {
            var loggerFactory = new LoggerFactory();
            var loggerProvider = new TestLoggerProvider();
            loggerFactory.AddProvider(loggerProvider);

            var instanceController = new InstanceController(null, null, loggerFactory, null, new TestMetricsLogger());
        
            var actionResult = instanceController.GetHttpHealthStatus();
            var okResult = actionResult as OkResult;

            Assert.NotNull(okResult);
            Assert.Equal(200, okResult.StatusCode);
        }

        [Fact]
        public async Task Assignment_Sets_Secrets_Context()
        {
            var testmetricslogger = new TestMetricsLogger();
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
                scriptWebEnvironment, environment, loggerFactory.CreateLogger<AtlasInstanceManager>(), testmetricslogger, null, _runFromPackageHandler.Object, new Mock<IPackageDownloadHandler>(MockBehavior.Strict).Object);
            var startupContextProvider = new StartupContextProvider(environment, loggerFactory.CreateLogger<StartupContextProvider>());

            instanceManager.Reset();

            var instanceController = new InstanceController(environment, instanceManager, loggerFactory, startupContextProvider, testmetricslogger);

            var hostAssignmentContext = new HostAssignmentContext
            {
                Environment = new Dictionary<string, string>()
                {
                    [EnvironmentSettingNames.AzureWebsiteRunFromPackage] = "http://localhost:1234"
                }
            };
            hostAssignmentContext.Secrets = new FunctionAppSecrets();
            hostAssignmentContext.IsWarmupRequest = false; // non-warmup Request

            var encryptedHostAssignmentValue = EncryptionHelper.Encrypt(JsonConvert.SerializeObject(hostAssignmentContext), TestHelpers.EncryptionKey.ToKeyBytes());

            var hostAssignmentRequest = new HostAssignmentRequest()
            {
                EncryptedContext = encryptedHostAssignmentValue
            };

            environment.SetEnvironmentVariable(EnvironmentSettingNames.ContainerEncryptionKey, TestHelpers.EncryptionKey);

            await instanceController.Assign(hostAssignmentRequest);
            Assert.NotNull(startupContextProvider.Context);
            Assert.True(areRequiredMetricsLogged(testmetricslogger, [MetricEventNames.LinuxContainerSpecializationAssign, MetricEventNames.LinuxContainerSpecializationZipHead]));
        }

        [Fact]
        public async Task Assignment_Does_Not_Set_Secrets_Context_For_Warmup_Request()
        {
            var testmetricslogger = new TestMetricsLogger();
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
                scriptWebEnvironment, environment, loggerFactory.CreateLogger<AtlasInstanceManager>(), testmetricslogger, null, _runFromPackageHandler.Object, new Mock<IPackageDownloadHandler>(MockBehavior.Strict).Object);
            var startupContextProvider = new StartupContextProvider(environment, loggerFactory.CreateLogger<StartupContextProvider>());

            instanceManager.Reset();

            var instanceController = new InstanceController(environment, instanceManager, loggerFactory, startupContextProvider, testmetricslogger);

            var hostAssignmentContext = new HostAssignmentContext
            {
                Environment = new Dictionary<string, string>()
                    {
                        [EnvironmentSettingNames.AzureWebsiteRunFromPackage] = "http://localhost:1234"
                    }
            };
            hostAssignmentContext.Secrets = new FunctionAppSecrets();
            hostAssignmentContext.IsWarmupRequest = true; // Warmup Request

            var encryptedHostAssignmentValue = EncryptionHelper.Encrypt(JsonConvert.SerializeObject(hostAssignmentContext), TestHelpers.EncryptionKey.ToKeyBytes());

            var hostAssignmentRequest = new HostAssignmentRequest()
            {
                EncryptedContext = encryptedHostAssignmentValue
            };

            environment.SetEnvironmentVariable(EnvironmentSettingNames.ContainerEncryptionKey, TestHelpers.EncryptionKey);

            await instanceController.Assign(hostAssignmentRequest);
            Assert.Null(startupContextProvider.Context);
            Assert.True(areRequiredMetricsLogged(testmetricslogger, [MetricEventNames.LinuxContainerSpecializationAssign, MetricEventNames.LinuxContainerSpecializationZipHeadWarmup]));
        }

        [Theory]
        [InlineData(true, true, true)]
        [InlineData(false, true, true)]
        [InlineData(true, true, false)]
        [InlineData(false, true, false)]
        public async Task Assignment_Invokes_InstanceManager_Methods_For_Warmup_Requests_Also(bool isWarmupRequest, bool shouldInvokeMethod, bool useEncryptedPayload)
        {
            var testmetricslogger = new TestMetricsLogger();
            var environment = new TestEnvironment();
            environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");
            
            var loggerFactory = new LoggerFactory();
            var loggerProvider = new TestLoggerProvider();
            loggerFactory.AddProvider(loggerProvider);

            var instanceManager = new Mock<IInstanceManager>();
            var startupContextProvider = new StartupContextProvider(environment, loggerFactory.CreateLogger<StartupContextProvider>());

            instanceManager.Reset();

            var instanceController = new InstanceController(environment, instanceManager.Object, loggerFactory,
                startupContextProvider, testmetricslogger);
            DefaultHttpContext context = new() { User = new ClaimsPrincipal(new ClaimsIdentity(new Claim[]
            {
                new Claim(SecurityConstants.AssignUnencryptedClaimType, "true")
            })) };
            instanceController.ControllerContext = new() { HttpContext = context };

            var hostAssignmentContext = new HostAssignmentContext
            {
                Environment = new Dictionary<string, string>()
            };
            hostAssignmentContext.IsWarmupRequest = isWarmupRequest;

            var encryptedHostAssignmentValue =
                EncryptionHelper.Encrypt(JsonConvert.SerializeObject(hostAssignmentContext),
                    TestHelpers.EncryptionKey.ToKeyBytes());

            var hostAssignmentRequest = new HostAssignmentRequest() { };
            if (useEncryptedPayload)
            {
                hostAssignmentRequest.EncryptedContext = encryptedHostAssignmentValue;
            }
            else
            {
                hostAssignmentRequest.AssignmentContext = hostAssignmentContext;
            }
            environment.SetEnvironmentVariable(EnvironmentSettingNames.ContainerEncryptionKey, TestHelpers.EncryptionKey);
            
            await instanceController.Assign(hostAssignmentRequest);

            instanceManager.Verify(i => i.ValidateContext(It.IsAny<HostAssignmentContext>()),
                shouldInvokeMethod ? Times.Once() : Times.Never());
            instanceManager.Verify(i => i.SpecializeMSISidecar(It.IsAny<HostAssignmentContext>()),
                shouldInvokeMethod ? Times.Once() : Times.Never());
            instanceManager.Verify(i => i.StartAssignment(It.IsAny<HostAssignmentContext>()),
                shouldInvokeMethod ? Times.Once() : Times.Never());
            Assert.True(areRequiredMetricsLogged(testmetricslogger, [MetricEventNames.LinuxContainerSpecializationAssign]));
        }

        [Fact]
        public async Task Assignment_InvalidInput_ReturnsBadRequest()
        {
            var testmetricslogger = new TestMetricsLogger();
            var environment = new TestEnvironment();
            environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");
            var loggerFactory = new LoggerFactory();
            var loggerProvider = new TestLoggerProvider();
            loggerFactory.AddProvider(loggerProvider);
            var instanceController = new InstanceController(environment, null, loggerFactory, null, testmetricslogger);

            // HostAssignmentRequest is null
            var result = await instanceController.Assign(null);
            var badRequestResult = result as BadRequestObjectResult;
            Assert.NotNull(badRequestResult);
            Assert.Equal(400, badRequestResult.StatusCode);
            Assert.Equal("hostAssignmentRequest cannot be null.", badRequestResult.Value);
            Assert.True(areRequiredMetricsLogged(testmetricslogger, [MetricEventNames.LinuxContainerSpecializationAssign]));

            // Both encrypted and unencrypted context are null
            testmetricslogger.ClearCollections();
            var hostAssignmentRequest = new HostAssignmentRequest() { };
            result = await instanceController.Assign(hostAssignmentRequest);
            badRequestResult = result as BadRequestObjectResult;
            Assert.NotNull(badRequestResult);
            Assert.Equal(400, badRequestResult.StatusCode);
            Assert.Equal("At least one of AssignmentContext or EncryptedContext must be provided.", badRequestResult.Value);
            Assert.True(areRequiredMetricsLogged(testmetricslogger, [MetricEventNames.LinuxContainerSpecializationAssign]));

            // Both encrypted and unencrypted context are set
            testmetricslogger.ClearCollections();
            hostAssignmentRequest = new HostAssignmentRequest()
            {
                EncryptedContext = "EncryptedContext",
                AssignmentContext = new HostAssignmentContext()
            };
            result = await instanceController.Assign(hostAssignmentRequest);
            badRequestResult = result as BadRequestObjectResult;
            Assert.NotNull(badRequestResult);
            Assert.Equal(400, badRequestResult.StatusCode);
            Assert.Equal("Only one of AssignmentContext or EncryptedContext may be set.", badRequestResult.Value);
            Assert.True(areRequiredMetricsLogged(testmetricslogger, [MetricEventNames.LinuxContainerSpecializationAssign]));
        }

        [Fact]
        public async Task Assignment_MissingClaims_ReturnsForbidden()
        {
            var testmetricslogger = new TestMetricsLogger();
            var environment = new TestEnvironment();
            environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");
            var loggerFactory = new LoggerFactory();
            var loggerProvider = new TestLoggerProvider();
            loggerFactory.AddProvider(loggerProvider);

            var instanceController = new InstanceController(environment, null, loggerFactory, null, testmetricslogger);
            DefaultHttpContext context = new()
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new Claim[]{}))
            };
            instanceController.ControllerContext = new() { HttpContext = context };
            
            var hostAssignmentRequest = new HostAssignmentRequest()
            {
                AssignmentContext = new HostAssignmentContext()
            };
            var result = await instanceController.Assign(hostAssignmentRequest);
            var forbiddenResult = result as ForbidResult;
            Assert.NotNull(forbiddenResult);
            Assert.True(areRequiredMetricsLogged(testmetricslogger, [MetricEventNames.LinuxContainerSpecializationAssign]));
        }

        private bool areRequiredMetricsLogged(TestMetricsLogger testmetricslogger, string[] metricEventNames)
        {
            if (metricEventNames.Length != testmetricslogger.EventsBegan.Count || 
                metricEventNames.Length != testmetricslogger.EventsEnded.Count)
            {
                return false;
            }

            return metricEventNames.All(eventName => 
                testmetricslogger.EventsBegan.Contains(eventName) && 
                testmetricslogger.EventsEnded.Contains(eventName));
        }
    }
}
