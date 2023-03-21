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
    public class KubernetesPodControllerTests
    {
        private readonly TestOptionsFactory<ScriptApplicationHostOptions> _optionsFactory = new TestOptionsFactory<ScriptApplicationHostOptions>(new ScriptApplicationHostOptions());

        [Fact]
        public async Task Assignment_Succeeds_With_Encryption_Key()
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
                new TestMetricsLogger(), null, new Mock<IRunFromPackageHandler>().Object,
                new Mock<IPackageDownloadHandler>(MockBehavior.Strict).Object);
            var startupContextProvider = new StartupContextProvider(environment, loggerFactory.CreateLogger<StartupContextProvider>());

            instanceManager.Reset();

            var podController = new KubernetesPodController(environment, instanceManager, loggerFactory, startupContextProvider);

            const string podEncryptionKey = "/a/vXvWJ3Hzgx4PFxlDUJJhQm5QVyGiu0NNLFm/ZMMg=";
            var hostAssignmentContext = new HostAssignmentContext
            {
                Environment = new Dictionary<string, string>()
                {
                    [EnvironmentSettingNames.AzureWebsiteRunFromPackage] = "http://localhost:1234"
                }
            };
            hostAssignmentContext.Secrets = new FunctionAppSecrets();
            hostAssignmentContext.IsWarmupRequest = false;

            var encryptedHostAssignmentValue = SimpleWebTokenHelper.Encrypt(JsonConvert.SerializeObject(hostAssignmentContext), podEncryptionKey.ToKeyBytes());

            var encryptedHostAssignmentContext = new EncryptedHostAssignmentContext()
            {
                EncryptedContext = encryptedHostAssignmentValue
            };

            environment.SetEnvironmentVariable(EnvironmentSettingNames.PodEncryptionKey, podEncryptionKey);
            environment.SetEnvironmentVariable(EnvironmentSettingNames.KubernetesServiceHost, "http://localhost:80");
            environment.SetEnvironmentVariable(EnvironmentSettingNames.PodNamespace, "k8se-apps");

            var result = await podController.Assign(encryptedHostAssignmentContext);
            Assert.NotNull(startupContextProvider.Context);
            Assert.IsType<AcceptedResult>(result);
        }

        [Fact]
        public async Task Assignment_Fails_Without_Encryption_Key()
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
                new TestMetricsLogger(), null, new Mock<IRunFromPackageHandler>().Object,
                new Mock<IPackageDownloadHandler>(MockBehavior.Strict).Object);
            var startupContextProvider = new StartupContextProvider(environment, loggerFactory.CreateLogger<StartupContextProvider>());

            instanceManager.Reset();

            const string podEncryptionKey = "/a/vXvWJ3Hzgx4PFxlDUJJhQm5QVyGiu0NNLFm/ZMMg=";
            var podController = new KubernetesPodController(environment, instanceManager, loggerFactory, startupContextProvider);

            var hostAssignmentContext = new HostAssignmentContext
            {
                Environment = new Dictionary<string, string>()
                {
                    [EnvironmentSettingNames.AzureWebsiteRunFromPackage] = "http://localhost:1234"
                }
            };
            hostAssignmentContext.Secrets = new FunctionAppSecrets();
            hostAssignmentContext.IsWarmupRequest = false;

            var encryptedHostAssignmentValue = SimpleWebTokenHelper.Encrypt(JsonConvert.SerializeObject(hostAssignmentContext), podEncryptionKey.ToKeyBytes());

            var encryptedHostAssignmentContext = new EncryptedHostAssignmentContext()
            {
                EncryptedContext = encryptedHostAssignmentValue
            };

            environment.SetEnvironmentVariable(EnvironmentSettingNames.KubernetesServiceHost, "http://localhost:80");
            environment.SetEnvironmentVariable(EnvironmentSettingNames.PodNamespace, "k8se-apps");

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await (podController.Assign(encryptedHostAssignmentContext));
            });
            Assert.Null(startupContextProvider.Context);
        }
    }
}
