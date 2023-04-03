// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Scale;
using Microsoft.Azure.WebJobs.Script.ExtensionBundle;
using Microsoft.Azure.WebJobs.Script.Models;
using Microsoft.Azure.WebJobs.Script.Scale;
using Microsoft.Azure.WebJobs.Script.WebHost.Controllers;
using Microsoft.Azure.WebJobs.Script.WebHost.Management;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class HostControllerTests
    {
        private readonly string _scriptPath;
        private readonly string _appOfflineFilePath;
        private readonly HostController _hostController;
        private readonly Mock<IScriptHostManager> _mockScriptHostManager;
        private readonly Mock<IEnvironment> _mockEnvironment;
        private readonly Mock<IFunctionsSyncManager> _functionsSyncManager;
        private readonly Mock<IExtensionBundleManager> _extensionBundleManager;
        private readonly Mock<HostPerformanceManager> _mockHostPerformanceManager;
        private readonly HostHealthMonitorOptions _hostHealthMonitorOptions;
        private readonly ScriptApplicationHostOptions _applicationHostOptions;
        private readonly Mock<IScaleStatusProvider> _scaleStatusProvider;
        private readonly LoggerFactory _loggerFactory;

        public HostControllerTests()
        {
            _scriptPath = Path.GetTempPath();
            _applicationHostOptions = new ScriptApplicationHostOptions();
            _applicationHostOptions.ScriptPath = _scriptPath;
            var optionsWrapper = new OptionsWrapper<ScriptApplicationHostOptions>(_applicationHostOptions);

            var loggerProvider = new TestLoggerProvider();
            _loggerFactory = new LoggerFactory();
            _loggerFactory.AddProvider(loggerProvider);
            _mockEnvironment = new Mock<IEnvironment>(MockBehavior.Strict);
            _mockEnvironment.Setup(p => p.GetEnvironmentVariable(It.IsAny<string>())).Returns<string>(null);
            _mockScriptHostManager = new Mock<IScriptHostManager>(MockBehavior.Strict);
            _mockScriptHostManager.SetupGet(p => p.State).Returns(ScriptHostState.Running);
            _functionsSyncManager = new Mock<IFunctionsSyncManager>(MockBehavior.Strict);
            _extensionBundleManager = new Mock<IExtensionBundleManager>(MockBehavior.Strict);

            var mockServiceProvider = new Mock<IServiceProvider>(MockBehavior.Strict);
            _hostHealthMonitorOptions = new HostHealthMonitorOptions();
            var wrappedHealthMonitorOptions = new OptionsWrapper<HostHealthMonitorOptions>(_hostHealthMonitorOptions);
            _mockHostPerformanceManager = new Mock<HostPerformanceManager>(_mockEnvironment.Object, wrappedHealthMonitorOptions, mockServiceProvider.Object);
            _hostController = new HostController(optionsWrapper, _loggerFactory, _mockEnvironment.Object, _mockScriptHostManager.Object, _functionsSyncManager.Object, _mockHostPerformanceManager.Object);

            _appOfflineFilePath = Path.Combine(_scriptPath, ScriptConstants.AppOfflineFileName);
            if (File.Exists(_appOfflineFilePath))
            {
                File.Delete(_appOfflineFilePath);
            }

            _scaleStatusProvider = new Mock<IScaleStatusProvider>(MockBehavior.Strict);
            _scaleStatusProvider.Setup(p => p.GetScaleStatusAsync(It.IsAny<ScaleStatusContext>())).Returns(() =>
            {
                return Task.FromResult(new AggregateScaleStatus()
                {
                    Vote = ScaleVote.ScaleIn
                });
            });
        }

        [Theory]
        [InlineData(false, false, FunctionAppContentEditingState.NotAllowed)]
        [InlineData(false, true, FunctionAppContentEditingState.Allowed)]
        [InlineData(true, true, FunctionAppContentEditingState.NotAllowed)]
        [InlineData(true, false, FunctionAppContentEditingState.NotAllowed)]
        [InlineData(true, true, FunctionAppContentEditingState.Unknown, false)]
        public async Task GetHostStatus_TestFunctionAppContentEditable(bool isFileSystemReadOnly, bool azureFilesAppSettingsExist, FunctionAppContentEditingState isFunctionAppContentEditable, bool isLinuxConsumption = true)
        {
            _mockScriptHostManager.SetupGet(p => p.LastError).Returns((Exception)null);
            var mockHostIdProvider = new Mock<IHostIdProvider>(MockBehavior.Strict);
            mockHostIdProvider.Setup(p => p.GetHostIdAsync(CancellationToken.None)).ReturnsAsync("test123");
            var mockserviceProvider = new Mock<IServiceProvider>(MockBehavior.Strict);
            mockserviceProvider.Setup(p => p.GetService(typeof(IExtensionBundleManager))).Returns(null);

            if (isLinuxConsumption)
            {
                _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.ContainerName)).Returns("test-container");
            }

            _applicationHostOptions.IsFileSystemReadOnly = isFileSystemReadOnly;
            if (azureFilesAppSettingsExist)
            {
                _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureFilesConnectionString)).Returns("test value");
                _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureFilesContentShare)).Returns("test value");
            }

            var result = (OkObjectResult)(await _hostController.GetHostStatus(_mockScriptHostManager.Object, mockHostIdProvider.Object, mockserviceProvider.Object));
            var status = (HostStatus)result.Value;
            Assert.Equal(status.FunctionAppContentEditingState, isFunctionAppContentEditable);
        }

        [Theory]
        [InlineData("blah", ScriptHostState.Running, HttpStatusCode.BadRequest)]
        [InlineData("Stopped", ScriptHostState.Running, HttpStatusCode.BadRequest)]
        [InlineData("offline", ScriptHostState.Running, HttpStatusCode.Accepted)]
        [InlineData("running", ScriptHostState.Offline, HttpStatusCode.Accepted)]
        [InlineData("OFFLINE", ScriptHostState.Offline, HttpStatusCode.OK)]
        [InlineData("running", ScriptHostState.Running, HttpStatusCode.OK)]
        public async Task SetState_Succeeds(string desiredState, ScriptHostState currentState, HttpStatusCode statusCode)
        {
            _mockScriptHostManager.SetupGet(p => p.State).Returns(currentState);

            var result = await _hostController.SetState(desiredState);
            var resultStatus = HttpStatusCode.InternalServerError;
            if (result is StatusCodeResult)
            {
                resultStatus = (HttpStatusCode)((StatusCodeResult)result).StatusCode;
            }
            else if (result is ObjectResult)
            {
                resultStatus = (HttpStatusCode)((ObjectResult)result).StatusCode;
            }
            else
            {
                Assert.True(false);
            }
            Assert.Equal(statusCode, resultStatus);

            bool fileExists = File.Exists(_appOfflineFilePath);
            if (string.Compare("offline", desiredState) == 0 && currentState != ScriptHostState.Offline)
            {
                // verify file was created
                Assert.True(fileExists);
            }
            else
            {
                // verify file does not exist
                Assert.False(fileExists);
            }
        }

        [Fact]
        public async Task GetScaleStatus_RuntimeScaleModeEnabled_Succeeds()
        {
            _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.FunctionsRuntimeScaleMonitoringEnabled)).Returns("1");

            var context = new ScaleStatusContext
            {
                WorkerCount = 5
            };
            var scriptHostManagerMock = new Mock<IScriptHostManager>(MockBehavior.Strict);
            var serviceProviderMock = scriptHostManagerMock.As<IServiceProvider>();
            serviceProviderMock.Setup(p => p.GetService(typeof(IScaleStatusProvider))).Returns(_scaleStatusProvider.Object);
            var result = (ObjectResult)(await _hostController.GetScaleStatus(context, scriptHostManagerMock.Object));
            Assert.Equal(((AggregateScaleStatus)result.Value).Vote, ScaleVote.ScaleIn);
        }

        [Fact]
        public async Task GetScaleStatus_IScaleStatusProvider_Null_ReturnsServiceUnavailable()
        {
            _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.FunctionsRuntimeScaleMonitoringEnabled)).Returns("1");

            var context = new ScaleStatusContext
            {
                WorkerCount = 5
            };
            var scriptHostManagerMock = new Mock<IScriptHostManager>(MockBehavior.Strict);
            var serviceProviderMock = scriptHostManagerMock.As<IServiceProvider>();
            serviceProviderMock.Setup(p => p.GetService(typeof(IScaleStatusProvider))).Returns(null);
            var result = (StatusCodeResult)(await _hostController.GetScaleStatus(context, scriptHostManagerMock.Object));

            Assert.Equal(StatusCodes.Status503ServiceUnavailable, result.StatusCode);
        }

        [Fact]
        public async Task GetScaleStatus_RuntimeScaleModeNotEnabled_ReturnsBadRequest()
        {
            var context = new ScaleStatusContext
            {
                WorkerCount = 5
            };
            Mock<IServiceProvider> serviceProviderMock = new Mock<IServiceProvider>();
            serviceProviderMock.Setup(p => p.GetService(typeof(IScaleStatusProvider))).Returns(_scaleStatusProvider.Object);
            var scriptHostManagerMock = new Mock<IScriptHostManager>(MockBehavior.Strict);
            var result = (BadRequestObjectResult)(await _hostController.GetScaleStatus(context, scriptHostManagerMock.Object));
            Assert.Equal(HttpStatusCode.BadRequest, (HttpStatusCode)result.StatusCode);
            Assert.Equal("Runtime scale monitoring is not enabled.", result.Value);
        }

        [Theory]
        [InlineData("HttpScaleManager/1.0.0", null, true, null, HttpStatusCode.TooManyRequests)]
        [InlineData("HttpScaleManager/1.0.0", "1", true, null, HttpStatusCode.TooManyRequests)]
        [InlineData("ElasticScaleController", null, true, null, HttpStatusCode.TooManyRequests)]
        [InlineData("TestAgent", "1", true, null, HttpStatusCode.TooManyRequests)]
        [InlineData("TestAgent", "0", true, null, HttpStatusCode.OK)]
        [InlineData("HttpScaleManager/1.0.0", null, true, "0", HttpStatusCode.OK)]
        [InlineData("HttpScaleManager/1.0.0", "0", true, null, HttpStatusCode.OK)]
        public async Task Ping_ScalePing_ReturnsExpectedResult(string userAgent, string healthCheckQueryParam, bool underHighLoad, string healthPingEnabled, HttpStatusCode expected)
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Host = new HostString("local");
            httpContext.Request.Method = "Post";
            httpContext.Request.Path = "/admin/host/ping";
            httpContext.Request.Headers.Add("User-Agent", userAgent);
            if (healthCheckQueryParam != null)
            {
                httpContext.Request.QueryString = new QueryString($"?{ScriptConstants.HealthCheckQueryParam}={healthCheckQueryParam}");
            }
            _hostController.ControllerContext.HttpContext = httpContext;

            _mockHostPerformanceManager.Setup(p => p.IsUnderHighLoadAsync(It.IsAny<ILogger>())).ReturnsAsync(() => underHighLoad);
            if (healthPingEnabled != null)
            {
                _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.HealthPingEnabled)).Returns(healthPingEnabled);
            }

            var result = (StatusCodeResult)(await _hostController.Ping(_mockScriptHostManager.Object));
            Assert.Equal((int)expected, result.StatusCode);
        }

        [Fact]
        public async Task Ping_ReturnsOk()
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Host = new HostString("local");
            httpContext.Request.Path = "/admin/host/ping";
            httpContext.Request.Method = "Get";
            httpContext.Request.IsHttps = true;
            _hostController.ControllerContext.HttpContext = httpContext;

            var result = (StatusCodeResult)(await _hostController.Ping(_mockScriptHostManager.Object));
            Assert.Equal((int)HttpStatusCode.OK, result.StatusCode);
        }

        [Theory]
        [InlineData(0, 0, DrainModeState.Disabled)]
        [InlineData(0, 0, DrainModeState.Completed)]
        [InlineData(2, 0, DrainModeState.InProgress)]
        [InlineData(0, 10, DrainModeState.InProgress)]
        [InlineData(5, 1, DrainModeState.InProgress)]
        [InlineData(20, 30, DrainModeState.Disabled)]
        public void GetDrainStatus_HostRunning_ReturnsExpected(int outstandingRetries, int outstandingInvocations, DrainModeState expectedState)
        {
            var scriptHostManagerMock = new Mock<IScriptHostManager>(MockBehavior.Strict);
            var functionActivityStatusProvider = new Mock<IFunctionActivityStatusProvider>(MockBehavior.Strict);
            var drainModeManager = new Mock<IDrainModeManager>(MockBehavior.Strict);
            functionActivityStatusProvider.Setup(x => x.GetStatus()).Returns(new FunctionActivityStatus()
            {
                OutstandingRetries = outstandingRetries,
                OutstandingInvocations = outstandingInvocations
            });
            var serviceProviderMock = scriptHostManagerMock.As<IServiceProvider>();
            serviceProviderMock.Setup(x => x.GetService(typeof(IFunctionActivityStatusProvider))).Returns(functionActivityStatusProvider.Object);
            serviceProviderMock.Setup(x => x.GetService(typeof(IDrainModeManager))).Returns(drainModeManager.Object);
            drainModeManager.Setup(x => x.IsDrainModeEnabled).Returns(expectedState != DrainModeState.Disabled);
            var result = (OkObjectResult)_hostController.DrainStatus(scriptHostManagerMock.Object);
            var resultStatus = result.Value as DrainModeStatus;
            Assert.Equal(expectedState, resultStatus.State);
            Assert.Equal(outstandingRetries, resultStatus.OutstandingRetries);
            Assert.Equal(outstandingInvocations, resultStatus.OutstandingInvocations);
        }

        [Fact]
        public async Task GetDrain_HostNotRunning_ReturnsServiceUnavailable()
        {
            var scriptHostManagerMock = new Mock<IScriptHostManager>(MockBehavior.Strict);

            var result = (StatusCodeResult)await _hostController.Drain(scriptHostManagerMock.Object);
            Assert.Equal(StatusCodes.Status503ServiceUnavailable, result.StatusCode);
        }

        [Fact]
        public void GetDrainStatus_HostNotRunning_ReturnsServiceUnavailable()
        {
            var scriptHostManagerMock = new Mock<IScriptHostManager>(MockBehavior.Strict);

            var result = (StatusCodeResult)_hostController.DrainStatus(scriptHostManagerMock.Object);
            Assert.Equal(StatusCodes.Status503ServiceUnavailable, result.StatusCode);
        }

        [Theory]
        [InlineData(ScriptHostState.Default, true, StatusCodes.Status409Conflict)]
        [InlineData(ScriptHostState.Default, false, StatusCodes.Status409Conflict)]
        [InlineData(ScriptHostState.Starting, true, StatusCodes.Status409Conflict)]
        [InlineData(ScriptHostState.Starting, false, StatusCodes.Status409Conflict)]
        [InlineData(ScriptHostState.Initialized, true, StatusCodes.Status409Conflict)]
        [InlineData(ScriptHostState.Initialized, false, StatusCodes.Status409Conflict)]
        [InlineData(ScriptHostState.Error, true, StatusCodes.Status409Conflict)]
        [InlineData(ScriptHostState.Error, false, StatusCodes.Status409Conflict)]
        [InlineData(ScriptHostState.Stopping, true, StatusCodes.Status409Conflict)]
        [InlineData(ScriptHostState.Stopping, false, StatusCodes.Status409Conflict)]
        [InlineData(ScriptHostState.Stopped, true, StatusCodes.Status409Conflict)]
        [InlineData(ScriptHostState.Stopped, false, StatusCodes.Status409Conflict)]
        [InlineData(ScriptHostState.Offline, true, StatusCodes.Status409Conflict)]
        [InlineData(ScriptHostState.Offline, false, StatusCodes.Status409Conflict)]
        public async Task ResumeHost_HostNotRunning_ReturnsExpected(ScriptHostState hostStatus, bool drainModeEnabled, int expectedCode)
        {
            var scriptHostManagerMock = new Mock<IScriptHostManager>(MockBehavior.Strict);
            var drainModeManager = new Mock<IDrainModeManager>(MockBehavior.Strict);

            var serviceProviderMock = scriptHostManagerMock.As<IServiceProvider>();
            serviceProviderMock.Setup(x => x.GetService(typeof(IDrainModeManager))).Returns(drainModeManager.Object);
            scriptHostManagerMock.SetupGet(p => p.State).Returns(hostStatus);
            drainModeManager.Setup(x => x.IsDrainModeEnabled).Returns(drainModeEnabled);

            var result = (StatusCodeResult)await _hostController.Resume(scriptHostManagerMock.Object);
            Assert.Equal(expectedCode, result.StatusCode);
            scriptHostManagerMock.Verify(p => p.RestartHostAsync(It.IsAny<CancellationToken>()), Times.Never());
        }

        [Fact]
        public async Task ResumeHost_HostRunning_DrainModeEnabled_StartNewHostSuccessful_ReturnOK()
        {
            var scriptHostManagerMock = new Mock<IScriptHostManager>(MockBehavior.Strict);
            var serviceProviderMock = scriptHostManagerMock.As<IServiceProvider>();
            var drainModeManager = new Mock<IDrainModeManager>(MockBehavior.Strict);

            serviceProviderMock.Setup(x => x.GetService(typeof(IDrainModeManager))).Returns(drainModeManager.Object);
            scriptHostManagerMock.SetupGet(p => p.State).Returns(ScriptHostState.Running);
            scriptHostManagerMock.Setup(p => p.RestartHostAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            drainModeManager.Setup(x => x.IsDrainModeEnabled).Returns(true);

            var expectedBody = new ResumeStatus { State = ScriptHostState.Running };
            var result = (OkObjectResult)await _hostController.Resume(scriptHostManagerMock.Object);

            Assert.Equal(StatusCodes.Status200OK, result.StatusCode);
            Assert.Equal(expectedBody.State, (result.Value as ResumeStatus).State);
            scriptHostManagerMock.Verify(p => p.RestartHostAsync(It.IsAny<CancellationToken>()), Times.Once());
        }

        [Fact]
        public async Task ResumeHost_HostRunning_DrainModeNotEnabled_DoesNotStartNewHost_ReturnHostStatus()
        {
            var scriptHostManagerMock = new Mock<IScriptHostManager>(MockBehavior.Strict);
            var serviceProviderMock = scriptHostManagerMock.As<IServiceProvider>();
            var drainModeManager = new Mock<IDrainModeManager>(MockBehavior.Strict);

            serviceProviderMock.Setup(x => x.GetService(typeof(IDrainModeManager))).Returns(drainModeManager.Object);
            scriptHostManagerMock.SetupGet(p => p.State).Returns(ScriptHostState.Running);
            drainModeManager.Setup(x => x.IsDrainModeEnabled).Returns(false);

            var expectedBody = new ResumeStatus { State = ScriptHostState.Running };
            var result = (OkObjectResult)await _hostController.Resume(scriptHostManagerMock.Object);

            Assert.Equal(StatusCodes.Status200OK, result.StatusCode);
            Assert.Equal(expectedBody.State, (result.Value as ResumeStatus).State);
            scriptHostManagerMock.Verify(p => p.RestartHostAsync(It.IsAny<CancellationToken>()), Times.Never());
        }
    }
}
