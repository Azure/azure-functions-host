// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.IO;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs.Host.Scale;
using Microsoft.Azure.WebJobs.Script.ExtensionBundle;
using Microsoft.Azure.WebJobs.Script.Scale;
using Microsoft.Azure.WebJobs.Script.WebHost.Controllers;
using Microsoft.Azure.WebJobs.Script.WebHost.Management;
using Microsoft.Azure.WebJobs.Script.WebHost.Security;
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

        public HostControllerTests()
        {
            _scriptPath = Path.GetTempPath();
            var applicationHostOptions = new ScriptApplicationHostOptions();
            applicationHostOptions.ScriptPath = _scriptPath;
            var optionsWrapper = new OptionsWrapper<ScriptApplicationHostOptions>(applicationHostOptions);
            var hostOptions = new OptionsWrapper<JobHostOptions>(new JobHostOptions());
            var loggerProvider = new TestLoggerProvider();
            var loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(loggerProvider);
            var mockAuthorizationService = new Mock<IAuthorizationService>(MockBehavior.Strict);
            var mockWebFunctionsManager = new Mock<IWebFunctionsManager>(MockBehavior.Strict);
            _mockEnvironment = new Mock<IEnvironment>(MockBehavior.Strict);
            _mockEnvironment.Setup(p => p.GetEnvironmentVariable(It.IsAny<string>())).Returns<string>(null);
            _mockScriptHostManager = new Mock<IScriptHostManager>(MockBehavior.Strict);
            _functionsSyncManager = new Mock<IFunctionsSyncManager>(MockBehavior.Strict);
            _extensionBundleManager = new Mock<IExtensionBundleManager>(MockBehavior.Strict);

            _hostController = new HostController(optionsWrapper, hostOptions, loggerFactory, mockAuthorizationService.Object, mockWebFunctionsManager.Object, _mockEnvironment.Object, _mockScriptHostManager.Object, _functionsSyncManager.Object, _extensionBundleManager.Object);

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
        public void GetAdminToken_Succeeds()
        {
            // Arrange
            _mockEnvironment.Setup(p => p.GetEnvironmentVariable(It.Is<string>(k => k == EnvironmentSettingNames.ContainerName))).Returns<string>(v => v = "ContainerName");

            var key = TestHelpers.GenerateKeyBytes();
            var stringKey = TestHelpers.GenerateKeyHexString(key);
            using (new TestScopedEnvironmentVariable(EnvironmentSettingNames.WebSiteAuthEncryptionKey, stringKey))
            {
                // Act
                ObjectResult result = (ObjectResult)_hostController.GetAdminToken();
                HttpStatusCode resultStatus = (HttpStatusCode)result.StatusCode;
                string token = (string)result.Value;

                // Assert
                Assert.Equal(HttpStatusCode.OK, resultStatus);
                Assert.True(SimpleWebTokenHelper.ValidateToken(token, new SystemClock()));
            }
        }

        [Fact]
        public void GetAdminToken_Fails_NotLinuxContainer()
        {
            // Arrange
            _mockEnvironment.Setup(p => p.GetEnvironmentVariable(It.Is<string>(k => k == EnvironmentSettingNames.ContainerName))).Returns<string>(v => v = null);

            var key = TestHelpers.GenerateKeyBytes();
            var stringKey = TestHelpers.GenerateKeyHexString(key);
            using (new TestScopedEnvironmentVariable(EnvironmentSettingNames.WebSiteAuthEncryptionKey, stringKey))
            {
                // Act
                ObjectResult result = (ObjectResult)_hostController.GetAdminToken();
                HttpStatusCode resultStatus = (HttpStatusCode)result.StatusCode;

                // Assert
                Assert.Equal(HttpStatusCode.BadRequest, resultStatus);
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
            var result = (ObjectResult)(await _hostController.GetScaleStatus(context, scaleManagerMock.Object));
            Assert.Same(result.Value, scaleStatusResult);
        }

        [Fact]
        public async Task GetScaleStatus_RuntimeScaleModeNotEnabled_ReturnsBadRequest()
        {
            var context = new ScaleStatusContext
            {
                WorkerCount = 5
            };
            var scaleManagerMock = new Mock<FunctionsScaleManager>(MockBehavior.Strict);
            var result = (BadRequestObjectResult)(await _hostController.GetScaleStatus(context, scaleManagerMock.Object));
            Assert.Equal(HttpStatusCode.BadRequest, (HttpStatusCode)result.StatusCode);
            Assert.Equal("Runtime scale monitoring is not enabled.", result.Value);
        }
    }
}
