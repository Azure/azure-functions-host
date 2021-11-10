// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Scale;
using Microsoft.Azure.WebJobs.Script.ExtensionBundle;
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

        public HostControllerTests()
        {
            _scriptPath = Path.GetTempPath();
            var applicationHostOptions = new ScriptApplicationHostOptions();
            applicationHostOptions.ScriptPath = _scriptPath;
            var optionsWrapper = new OptionsWrapper<ScriptApplicationHostOptions>(applicationHostOptions);
            var loggerProvider = new TestLoggerProvider();
            var loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(loggerProvider);
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
            _hostController = new HostController(optionsWrapper, loggerFactory, _mockEnvironment.Object, _mockScriptHostManager.Object, _functionsSyncManager.Object, _mockHostPerformanceManager.Object);

            _appOfflineFilePath = Path.Combine(_scriptPath, ScriptConstants.AppOfflineFileName);
            if (File.Exists(_appOfflineFilePath))
            {
                File.Delete(_appOfflineFilePath);
            }
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
            var scaleManagerMock = new Mock<FunctionsScaleManager>(MockBehavior.Strict);
            var scaleStatusResult = new ScaleStatusResult { Vote = ScaleVote.ScaleOut };
            scaleManagerMock.Setup(p => p.GetScaleStatusAsync(context)).ReturnsAsync(scaleStatusResult);
            var scriptHostManagerMock = new Mock<IScriptHostManager>(MockBehavior.Strict);
            var serviceProviderMock = scriptHostManagerMock.As<IServiceProvider>();
            serviceProviderMock.Setup(p => p.GetService(typeof(FunctionsScaleManager))).Returns(scaleManagerMock.Object);
            var result = (ObjectResult)(await _hostController.GetScaleStatus(context, scriptHostManagerMock.Object));
            Assert.Same(result.Value, scaleStatusResult);
        }

        [Fact]
        public async Task GetScaleStatus_FunctionsScaleManager_Null_ReturnsServiceUnavailable()
        {
            _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.FunctionsRuntimeScaleMonitoringEnabled)).Returns("1");

            var context = new ScaleStatusContext
            {
                WorkerCount = 5
            };
            var scriptHostManagerMock = new Mock<IScriptHostManager>(MockBehavior.Strict);
            var serviceProviderMock = scriptHostManagerMock.As<IServiceProvider>();
            serviceProviderMock.Setup(p => p.GetService(typeof(FunctionsScaleManager))).Returns(null);
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
            var scaleManagerMock = new Mock<FunctionsScaleManager>(MockBehavior.Strict);
            var scriptHostManagerMock = new Mock<IScriptHostManager>(MockBehavior.Strict);
            var serviceProviderMock = scriptHostManagerMock.As<IServiceProvider>();
            serviceProviderMock.Setup(p => p.GetService(typeof(FunctionsScaleManager))).Returns(scaleManagerMock.Object);
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
            Assert.Equal((result.Value as DrainModeStatus).State, expectedState);
        }

        [Fact]
        public void GetDrain_HostNotRunning_ReturnsServiceUnavailable()
        {
            var scriptHostManagerMock = new Mock<IScriptHostManager>(MockBehavior.Strict);

            var result = (StatusCodeResult)_hostController.Drain(scriptHostManagerMock.Object);
            Assert.Equal(result.StatusCode, StatusCodes.Status503ServiceUnavailable);
        }

        [Fact]
        public void GetDrainStatus_HostNotRunning_ReturnsServiceUnavailable()
        {
            var scriptHostManagerMock = new Mock<IScriptHostManager>(MockBehavior.Strict);

            var result = (StatusCodeResult)_hostController.DrainStatus(scriptHostManagerMock.Object);
            Assert.Equal(result.StatusCode, StatusCodes.Status503ServiceUnavailable);
        }
    }
}
