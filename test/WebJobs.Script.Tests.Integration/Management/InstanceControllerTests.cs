// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Controllers;
using Microsoft.Azure.WebJobs.Script.WebHost.Management;
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

            var instanceManager = new InstanceManager(_optionsFactory, new HttpClient(handlerMock.Object), scriptWebEnvironment, environment, loggerFactory.CreateLogger<InstanceManager>(), new TestMetricsLogger(), null);
            var startupContextProvider = new StartupContextProvider(environment, loggerFactory.CreateLogger<StartupContextProvider>());

            InstanceManager.Reset();

            var instanceController = new InstanceController(environment, instanceManager, loggerFactory, startupContextProvider);

            const string containerEncryptionKey = "/a/vXvWJ3Hzgx4PFxlDUJJhQm5QVyGiu0NNLFm/ZMMg=";
            var hostAssignmentContext = new HostAssignmentContext
            {
                Environment = new Dictionary<string, string>()
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
        }

        [Fact]
        public async Task Disable_Writes_To_DisableContainerFile_Restarts_ScriptHost()
        {
            var environment = new TestEnvironment();
            var loggerFactory = new LoggerFactory();
            var loggerProvider = new TestLoggerProvider();
            loggerFactory.AddProvider(loggerProvider);

            var startupContextProvider = new StartupContextProvider(environment, loggerFactory.CreateLogger<StartupContextProvider>());
            var instanceController = new InstanceController(environment, null, loggerFactory, startupContextProvider);
            var scriptHostManager = new Mock<IScriptHostManager>();

            var fileSystem = new Mock<IFileSystem>();
            var fileBase = new Mock<FileBase>();
            fileBase.Setup(
                    f => f.Exists(It.Is<string>(path => path.EndsWith(ScriptConstants.DisableContainerFileName))))
                .Returns(false);
            fileSystem.SetupGet(fs => fs.File).Returns(fileBase.Object);

            var memoryStream = new MemoryStream();
            fileSystem.Setup(s =>
                    s.File.Open(It.Is<string>(path => path.EndsWith(ScriptConstants.DisableContainerFileName)), FileMode.Create, FileAccess.Write, FileShare.Read))
                .Returns(memoryStream);

            FileUtility.Instance = fileSystem.Object;

            scriptHostManager.Setup(s => s.RestartHostAsync(It.IsAny<CancellationToken>()));

            var actionResult = await instanceController.Disable(scriptHostManager.Object);

            FileUtility.Instance = null;

            scriptHostManager.Verify(s => s.RestartHostAsync(It.IsAny<CancellationToken>()), Times.Once);

            // Remove BOM
            var memoryStreamContents = Encoding.UTF8.GetString(memoryStream.ToArray()).Trim(new char[] { '\uFEFF' });

            Assert.Equal("This container instance is offline", memoryStreamContents);

            var okResult = actionResult as OkResult;

            Assert.NotNull(okResult);
            Assert.Equal(200, okResult.StatusCode);
        }
    }
}
