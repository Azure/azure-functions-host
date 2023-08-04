// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Management;
using Microsoft.Azure.WebJobs.Script.WebHost.Management.LinuxSpecialization;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Moq.Protected;
using Newtonsoft.Json;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Managment
{
    [Trait(TestTraits.Category, TestTraits.EndToEnd)]
    [Trait(TestTraits.Group, TestTraits.ContainerInstanceTests)]
    public class InstanceManagerTests : IDisposable
    {
        private readonly TestLoggerProvider _loggerProvider;
        private readonly TestEnvironmentEx _environment;
        private readonly ScriptWebHostEnvironment _scriptWebEnvironment;
        private readonly AtlasInstanceManager _instanceManager;
        private readonly Mock<IMeshServiceClient> _meshServiceClientMock;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly LoggerFactory _loggerFactory = new LoggerFactory();
        private readonly TestOptionsFactory<ScriptApplicationHostOptions> _optionsFactory = new TestOptionsFactory<ScriptApplicationHostOptions>(new ScriptApplicationHostOptions() { ScriptPath = Path.GetTempPath() });
        private readonly IRunFromPackageHandler _runFromPackageHandler;
        private readonly Mock<IPackageDownloadHandler> _packageDownloadHandler;

        public InstanceManagerTests()
        {
            _httpClientFactory = TestHelpers.CreateHttpClientFactory();

            _loggerProvider = new TestLoggerProvider();
            _loggerFactory.AddProvider(_loggerProvider);

            _environment = new TestEnvironmentEx();
            _scriptWebEnvironment = new ScriptWebHostEnvironment(_environment);
            _meshServiceClientMock = new Mock<IMeshServiceClient>(MockBehavior.Strict);
            _packageDownloadHandler = new Mock<IPackageDownloadHandler>(MockBehavior.Strict);

            var metricsLogger = new MetricsLogger();
            var bashCommandHandler = new BashCommandHandler(metricsLogger, new Logger<BashCommandHandler>(_loggerFactory));
            var zipHandler = new UnZipHandler(metricsLogger, NullLogger<UnZipHandler>.Instance);
            _runFromPackageHandler = new RunFromPackageHandler(_environment, _meshServiceClientMock.Object,
                bashCommandHandler, zipHandler, _packageDownloadHandler.Object, metricsLogger, new Logger<RunFromPackageHandler>(_loggerFactory));

            _instanceManager = new AtlasInstanceManager(_optionsFactory, _httpClientFactory, _scriptWebEnvironment, _environment,
                _loggerFactory.CreateLogger<AtlasInstanceManager>(), new TestMetricsLogger(), _meshServiceClientMock.Object, _runFromPackageHandler, _packageDownloadHandler.Object);

            _instanceManager.Reset();
        }

        [Fact]
        public async Task StartAssignment_AppliesAssignmentContext()
        {
            var envValue = new
            {
                Name = Path.GetTempFileName().Replace(".", string.Empty),
                Value = Guid.NewGuid().ToString()
            };
            var allowedOrigins = new string[]
            {
                "https://functions.azure.com",
                "https://functions-staging.azure.com",
                "https://functions-next.azure.com"
            };
            var supportCredentials = true;

            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");
            var context = new HostAssignmentContext
            {
                Environment = new Dictionary<string, string>
                {
                    { envValue.Name, envValue.Value }
                },
                CorsSettings = new CorsSettings
                {
                    AllowedOrigins = allowedOrigins,
                    SupportCredentials = supportCredentials,
                },
                IsWarmupRequest = false
            };
            bool result = _instanceManager.StartAssignment(context);
            Assert.True(result);
            Assert.True(_scriptWebEnvironment.InStandbyMode);

            // specialization is done in the background
            await Task.Delay(500);

            var value = _environment.GetEnvironmentVariable(envValue.Name);
            Assert.Equal(value, envValue.Value);

            var supportCredentialsValue = _environment.GetEnvironmentVariable(EnvironmentSettingNames.CorsSupportCredentials);
            Assert.Equal(supportCredentialsValue, supportCredentials.ToString());

            var allowedOriginsValue = _environment.GetEnvironmentVariable(EnvironmentSettingNames.CorsAllowedOrigins);
            Assert.Equal(allowedOriginsValue, JsonConvert.SerializeObject(allowedOrigins));

            // verify logs
            var logs = _loggerProvider.GetAllLogMessages().Select(p => p.FormattedMessage).ToArray();
            Assert.Collection(logs,
                p => Assert.StartsWith("Starting Assignment", p),
                p => Assert.StartsWith("Applying 1 app setting(s)", p),
                p => Assert.Equal("AzureFilesConnectionString IsNullOrEmpty: True. AzureFilesContentShare: IsNullOrEmpty True", p),
                p => Assert.StartsWith("Triggering specialization", p));

            // calling again should return false, since we have 
            // already marked the container as specialized.
            _loggerProvider.ClearAllLogMessages();
            result = _instanceManager.StartAssignment(context);
            Assert.False(result);

            logs = _loggerProvider.GetAllLogMessages().Select(p => p.FormattedMessage).ToArray();
            Assert.Collection(logs,
                p => Assert.StartsWith("Assign called while host is not in placeholder mode and start context is not present.", p));
        }

        [Fact]
        public async Task StartAssignment_Failure_ExitsPlaceholderMode()
        {
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");
            var context = new HostAssignmentContext
            {
                Environment = new Dictionary<string, string>
                {
                    // force the assignment to fail
                    { "throw", "test" }
                },
                IsWarmupRequest = false
            };

            _meshServiceClientMock.Setup(c => c.NotifyHealthEvent(ContainerHealthEventType.Fatal,
                It.Is<Type>(t => t == typeof(AtlasInstanceManager)), "Assign failed")).Returns(Task.CompletedTask);

            bool result = _instanceManager.StartAssignment(context);
            Assert.True(result);
            Assert.True(_scriptWebEnvironment.InStandbyMode);

            await TestHelpers.Await(() => !_scriptWebEnvironment.InStandbyMode, timeout: 5000);

            var error = _loggerProvider.GetAllLogMessages().First(p => p.Level == LogLevel.Error);
            Assert.Equal("Assign failed", error.FormattedMessage);
            Assert.Equal("Kaboom!", error.Exception.Message);

            _meshServiceClientMock.Verify(c => c.NotifyHealthEvent(ContainerHealthEventType.Fatal,
                It.Is<Type>(t => t == typeof(AtlasInstanceManager)), "Assign failed"), Times.Once);
        }

        [Fact]
        public async Task StartAssignment_Succeeds_With_No_RunFromPackage_AppSetting()
        {
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");
            var context = new HostAssignmentContext
            {
                Environment = new Dictionary<string, string>(),
                IsWarmupRequest = false
            };
            bool result = _instanceManager.StartAssignment(context);
            Assert.True(result);
            Assert.True(_scriptWebEnvironment.InStandbyMode);

            await TestHelpers.Await(() => !_scriptWebEnvironment.InStandbyMode, timeout: 5000);

            var logs = _loggerProvider.GetAllLogMessages().Select(p => p.FormattedMessage).ToArray();
            Assert.Collection(logs,
                p => Assert.StartsWith("Starting Assignment", p),
                p => Assert.StartsWith("Applying 0 app setting(s)", p),
                p => Assert.Equal("AzureFilesConnectionString IsNullOrEmpty: True. AzureFilesContentShare: IsNullOrEmpty True", p),
                p => Assert.StartsWith("Triggering specialization", p));
        }

        [Fact]
        public async void StartAssignment_Succeeds_With_NonEmpty_ScmRunFromPackage_Blob()
        {
            var contentRoot = Path.Combine(Path.GetTempPath(), @"FunctionsTest");
            var zipFilePath = Path.Combine(contentRoot, "content.zip");
            await TestHelpers.CreateContentZip(contentRoot, zipFilePath, Path.Combine(@"TestScripts", "DotNet"));

            IConfiguration configuration = TestHelpers.GetTestConfiguration();
            string connectionString = configuration.GetWebJobsConnectionString(ConnectionStringNames.Storage);

            Uri sasUri = await TestHelpers.CreateBlobSas(connectionString, zipFilePath, "scm-run-from-pkg-test", "NonEmpty.zip");

            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");
            var context = new HostAssignmentContext
            {
                Environment = new Dictionary<string, string>()
                {
                    { EnvironmentSettingNames.ScmRunFromPackage, sasUri.ToString() }
                },
                IsWarmupRequest = false
            };
            var options = new ScriptApplicationHostOptions()
            {
                ScriptPath = Path.GetTempPath(),
                IsScmRunFromPackage = true
            };
            var optionsFactory = new TestOptionsFactory<ScriptApplicationHostOptions>(options);

            _packageDownloadHandler.Setup(p => p.Download(It.IsAny<RunFromPackageContext>()))
                .Returns(Task.FromResult(string.Empty));

            var instanceManager = new AtlasInstanceManager(optionsFactory, _httpClientFactory, _scriptWebEnvironment,
                _environment, _loggerFactory.CreateLogger<AtlasInstanceManager>(), new TestMetricsLogger(),
                _meshServiceClientMock.Object, _runFromPackageHandler, _packageDownloadHandler.Object);

            bool result = instanceManager.StartAssignment(context);
            Assert.True(result);

            await TestHelpers.Await(() => !_scriptWebEnvironment.InStandbyMode, timeout: 5000);

            var logs = _loggerProvider.GetAllLogMessages().Select(p => p.FormattedMessage).ToArray();

            if (logs.Length == 10)
            {
                Assert.Collection(logs,
                    p => Assert.StartsWith("Starting Assignment", p),
                    p => Assert.StartsWith("Applying 1 app setting(s)", p),
                    p => Assert.EndsWith("points to an existing blob: True", p),
                    p => Assert.StartsWith("Unsquashing remote zip", p),
                    p => Assert.StartsWith("Running: ", p),
                    p => Assert.StartsWith("Output:", p),
                    p => Assert.True(true), // this line varies depending on whether WSL is on the machine; just ignore it
                    p => Assert.StartsWith("exitCode:", p),
                    p => Assert.StartsWith("Executed: ", p),
                    p => Assert.StartsWith("Triggering specialization", p));
            }
            else
            {
                Assert.Collection(logs,
                    p => Assert.StartsWith("Starting Assignment", p),
                    p => Assert.StartsWith("Applying 1 app setting(s)", p),
                    p => Assert.EndsWith("points to an existing blob: True", p),
                    p => Assert.StartsWith("Unsquashing remote zip", p),
                    p => Assert.StartsWith("Running: ", p),
                    p => Assert.StartsWith("Error running bash", p),
                    p => Assert.StartsWith("Executed: ", p),
                    p => Assert.StartsWith("Triggering specialization", p));
            }
        }

        [Fact]
        public async void StartAssignment_Does_Not_Assign_Settings_For_Warmup_Request()
        {
            var contentRoot = Path.Combine(Path.GetTempPath(), @"FunctionsTest");
            var zipFilePath = Path.Combine(contentRoot, "content.zip");
            await TestHelpers.CreateContentZip(contentRoot, zipFilePath, Path.Combine(@"TestScripts", "DotNet"));

            IConfiguration configuration = TestHelpers.GetTestConfiguration();
            string connectionString = configuration.GetWebJobsConnectionString(ConnectionStringNames.Storage);
            Uri sasUri = await TestHelpers.CreateBlobSas(connectionString, zipFilePath, "scm-run-from-pkg-test", "NonEmpty.zip");

            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");
            var context = new HostAssignmentContext
            {
                Environment = new Dictionary<string, string>()
                {
                    { EnvironmentSettingNames.ScmRunFromPackage, sasUri.ToString() }
                },
                IsWarmupRequest = true
            };
            bool result = _instanceManager.StartAssignment(context);
            Assert.True(result);

            var logs = _loggerProvider.GetAllLogMessages().Select(p => p.FormattedMessage).ToArray();
            Assert.False(logs.Any(l => l.StartsWith("Starting Assignment.")));
        }

        [Fact]
        public async void StartAssignment_Succeeds_With_Empty_ScmRunFromPackage_Blob()
        {
            IConfiguration configuration = TestHelpers.GetTestConfiguration();
            string connectionString = configuration.GetWebJobsConnectionString(ConnectionStringNames.Storage);
            Uri sasUri = await TestHelpers.CreateBlobSas(connectionString, string.Empty, "scm-run-from-pkg-test", "Empty.zip");

            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");
            var context = new HostAssignmentContext
            {
                Environment = new Dictionary<string, string>()
                {
                    { EnvironmentSettingNames.ScmRunFromPackage, sasUri.ToString() }
                },
                IsWarmupRequest = false
            };
            var options = new ScriptApplicationHostOptions()
            {
                ScriptPath = Path.GetTempPath(),
                IsScmRunFromPackage = false
            };
            var optionsFactory = new TestOptionsFactory<ScriptApplicationHostOptions>(options);
            var instanceManager = new AtlasInstanceManager(optionsFactory, _httpClientFactory, _scriptWebEnvironment,
                _environment, _loggerFactory.CreateLogger<AtlasInstanceManager>(), new TestMetricsLogger(),
                _meshServiceClientMock.Object, _runFromPackageHandler, _packageDownloadHandler.Object);

            bool result = instanceManager.StartAssignment(context);
            Assert.True(result);

            await TestHelpers.Await(() => !_scriptWebEnvironment.InStandbyMode, timeout: 4000);

            Assert.False(_scriptWebEnvironment.InStandbyMode);

            var logs = _loggerProvider.GetAllLogMessages().Select(p => p.FormattedMessage).ToArray();
            Assert.Collection(logs,
                p => Assert.StartsWith("Starting Assignment", p),
                p => Assert.StartsWith("Applying 1 app setting(s)", p),
                p => Assert.StartsWith($"{EnvironmentSettingNames.ScmRunFromPackage} points to an existing blob: False", p),
                p => Assert.Equal("AzureFilesConnectionString IsNullOrEmpty: True. AzureFilesContentShare: IsNullOrEmpty True", p),
                p => Assert.StartsWith("Triggering specialization", p));
        }

        [Fact]
        public void StartAssignment_ReturnsTrue_ForPinnedContainers()
        {
            Assert.False(SystemEnvironment.Instance.IsPlaceholderModeEnabled());

            var context = new HostAssignmentContext();
            context.Environment = new Dictionary<string, string>()
            {
                { EnvironmentSettingNames.ContainerStartContext, "startContext" }
            };
            context.IsWarmupRequest = false;
            bool result = _instanceManager.StartAssignment(context);
            Assert.True(result);
        }

        [Fact]
        public void StartAssignment_ReturnsFalse_ForNonPinnedContainersInStandbyMode()
        {
            Assert.False(SystemEnvironment.Instance.IsPlaceholderModeEnabled());

            var context = new HostAssignmentContext();
            context.Environment = new Dictionary<string, string>();
            context.IsWarmupRequest = false;
            bool result = _instanceManager.StartAssignment(context);
            Assert.False(result);
        }

        [Fact]
        public async Task ValidateContext_InvalidZipUrl_WebsiteUseZip_ReturnsError()
        {
            var environmentSettings = new Dictionary<string, string>()
            {
                { EnvironmentSettingNames.AzureWebsiteZipDeployment, "http://invalid.com/invalid/dne" }
            };

            var environment = new TestEnvironment();
            foreach (var (key, value) in environmentSettings)
            {
                environment.SetEnvironmentVariable(key, value);
            }

            var scriptWebEnvironment = new ScriptWebHostEnvironment(environment);

            var loggerFactory = new LoggerFactory();
            var loggerProvider = new TestLoggerProvider();
            loggerFactory.AddProvider(loggerProvider);

            var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);

            handlerMock.Protected().Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()).ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.NotFound
            });

            var instanceManager = new AtlasInstanceManager(_optionsFactory, TestHelpers.CreateHttpClientFactory(handlerMock.Object),
                scriptWebEnvironment, environment, loggerFactory.CreateLogger<AtlasInstanceManager>(),
                new TestMetricsLogger(), null, _runFromPackageHandler, _packageDownloadHandler.Object);

            var assignmentContext = new HostAssignmentContext
            {
                SiteId = 1234,
                SiteName = "TestSite",
                Environment = environmentSettings,
                IsWarmupRequest = false
            };

            string error = await instanceManager.ValidateContext(assignmentContext);
            Assert.Equal("Invalid zip url specified (StatusCode: NotFound)", error);

            var logs = loggerProvider.GetAllLogMessages().Select(p => p.FormattedMessage).ToArray();
            Assert.Collection(logs,
                p => Assert.StartsWith("Validating host assignment context (SiteId: 1234, SiteName: 'TestSite'. IsWarmup: 'False')", p),
                p => Assert.StartsWith($"Will be using {EnvironmentSettingNames.AzureWebsiteZipDeployment} app setting as zip url. IsWarmup: 'False'", p),
                p => Assert.StartsWith("linux.container.specialization.zip.head failed", p),
                p => Assert.StartsWith("linux.container.specialization.zip.head failed", p),
                p => Assert.StartsWith("linux.container.specialization.zip.head failed", p),
                p => Assert.StartsWith("ValidateContext failed", p));
        }

        [Fact]
        public async Task ValidateContext_EmptyZipUrl_ReturnsSuccess()
        {
            var environment = new Dictionary<string, string>();
            var assignmentContext = new HostAssignmentContext
            {
                SiteId = 1234,
                SiteName = "TestSite",
                Environment = environment,
                IsWarmupRequest = false
            };

            string error = await _instanceManager.ValidateContext(assignmentContext);
            Assert.Null(error);

            string[] expectedOutputLines =
            {
                "Validating host assignment context (SiteId: 1234, SiteName: 'TestSite'. IsWarmup: 'False')",
                $"Will be using  app setting as zip url. IsWarmup: 'False"
            };

            var logs = _loggerProvider.GetAllLogMessages().Select(p => p.FormattedMessage).ToArray();

            for (int i = 0; i < expectedOutputLines.Length; i++)
            {
                Assert.StartsWith(expectedOutputLines[i], logs[i]);
            }
        }

        [Fact]
        public async Task ValidateContext_Succeeds_For_WebsiteUseZip_Only()
        {
            var environment = new Dictionary<string, string>()
            {
                { EnvironmentSettingNames.AzureWebsiteZipDeployment, "http://microsoft.com" }
            };
            var assignmentContext = new HostAssignmentContext
            {
                SiteId = 1234,
                SiteName = "TestSite",
                Environment = environment,
                IsWarmupRequest = false
            };

            string error = await _instanceManager.ValidateContext(assignmentContext);
            Assert.Null(error);

            string[] expectedOutputLines =
            {
                "Validating host assignment context (SiteId: 1234, SiteName: 'TestSite'. IsWarmup: 'False')",
                $"Will be using {EnvironmentSettingNames.AzureWebsiteZipDeployment} app setting as zip url. IsWarmup: 'False"
            };

            var logs = _loggerProvider.GetAllLogMessages().Select(p => p.FormattedMessage).ToArray();

            for (int i = 0; i < expectedOutputLines.Length; i++)
            {
                Assert.StartsWith(expectedOutputLines[i], logs[i]);
            }
        }

        [Fact]
        public async Task ValidateContext_Succeeds_For_ScmBuildPackage_Only()
        {
            var environment = new Dictionary<string, string>()
            {
                { EnvironmentSettingNames.ScmRunFromPackage,
                    "https://notarealstorageaccount.blob.core.windows.net/releases/test.zip?st=2019-05-22T15%3A00%3A09Z&se=2099-05-23T15%3A00%3A00Z&sp=rwl&sv=2018-03-28&sr=b&sig=d%2F7gP6ZGXvv%2RfHegvbwO88HaX0URZ%2BbXR6WGK%2BpcZE4%3D" }
            };
            var assignmentContext = new HostAssignmentContext
            {
                SiteId = 1234,
                SiteName = "TestSite",
                Environment = environment,
                IsWarmupRequest = false
            };

            string error = await _instanceManager.ValidateContext(assignmentContext);
            Assert.Null(error);

            string[] expectedOutputLines =
            {
                "Validating host assignment context (SiteId: 1234, SiteName: 'TestSite'. IsWarmup: 'False')",
                $"Will be using {EnvironmentSettingNames.ScmRunFromPackage} app setting as zip url. IsWarmup: 'False"
            };

            var logs = _loggerProvider.GetAllLogMessages().Select(p => p.FormattedMessage).ToArray();

            for (int i = 0; i < expectedOutputLines.Length; i++)
            {
                Assert.StartsWith(expectedOutputLines[i], logs[i]);
            }
        }

        [Fact]
        public async Task ValidateContext_Skips_Validation_For_Urls_With_No_Sas_Token()
        {
            var urlWithNoSasToken = "https://notarealstorageaccount.blob.core.windows.net/releases/test.zip";
            var environment = new Dictionary<string, string>()
            {
                { EnvironmentSettingNames.AzureWebsiteRunFromPackage, urlWithNoSasToken }
            };
            var assignmentContext = new HostAssignmentContext
            {
                SiteId = 1234,
                SiteName = "TestSite",
                Environment = environment,
                IsWarmupRequest = false
            };

            string error = await _instanceManager.ValidateContext(assignmentContext);
            Assert.Null(error);
            Assert.Null(assignmentContext.PackageContentLength);

            string[] expectedOutputLines =
            {
                "Validating host assignment context (SiteId: 1234, SiteName: 'TestSite'. IsWarmup: 'False')",
                $"Will be using {EnvironmentSettingNames.AzureWebsiteRunFromPackage} app setting as zip url. IsWarmup: 'False",
                $"Skipping validation for '{EnvironmentSettingNames.AzureWebsiteRunFromPackage}' with no SAS token"
            };

            var logs = _loggerProvider.GetAllLogMessages().Select(p => p.FormattedMessage).ToArray();

            for (int i = 0; i < expectedOutputLines.Length; i++)
            {
                Assert.StartsWith(expectedOutputLines[i], logs[i]);
            }
        }

        [Fact]
        public async Task ValidateContext_Succeeds_For_WebsiteUseZip_With_ScmPackageDefined()
        {
            var environment = new Dictionary<string, string>()
            {
                { EnvironmentSettingNames.AzureWebsiteZipDeployment, "http://microsoft.com" },
                { EnvironmentSettingNames.ScmRunFromPackage, "http://microsoft.com" }
            };
            var assignmentContext = new HostAssignmentContext
            {
                SiteId = 1234,
                SiteName = "TestSite",
                Environment = environment,
                IsWarmupRequest = false
            };

            string error = await _instanceManager.ValidateContext(assignmentContext);
            Assert.Null(error);

            string[] expectedOutputLines =
            {
                "Validating host assignment context (SiteId: 1234, SiteName: 'TestSite'. IsWarmup: 'False')",
                $"Will be using {EnvironmentSettingNames.AzureWebsiteZipDeployment} app setting as zip url. IsWarmup: 'False"
            };

            var logs = _loggerProvider.GetAllLogMessages().Select(p => p.FormattedMessage).ToArray();

            for (int i = 0; i < expectedOutputLines.Length; i++)
            {
                Assert.StartsWith(expectedOutputLines[i], logs[i]);
            }
        }

        [Fact]
        public async Task SpecializeMSISidecar_EmptyMSIEndpoint_NoOp()
        {
            var environment = new Dictionary<string, string>()
            {
                { EnvironmentSettingNames.MsiEndpoint, "" }
            };
            var assignmentContext = new HostAssignmentContext
            {
                SiteId = 1234,
                SiteName = "TestSite",
                Environment = environment,
                IsWarmupRequest = false
            };

            string error = await _instanceManager.SpecializeMSISidecar(assignmentContext);
            Assert.Null(error);

            var logs = _loggerProvider.GetAllLogMessages().Select(p => p.FormattedMessage).ToArray();
            Assert.Collection(logs, p => Assert.StartsWith("MSI enabled status: False", p));
        }

        [Fact]
        public async Task SpecializeMSISidecar_Succeeds()
        {
            var environment = new Dictionary<string, string>()
            {
                { EnvironmentSettingNames.MsiEndpoint, "http://localhost:8081" },
                { EnvironmentSettingNames.MsiSecret, "secret" }
            };
            var assignmentContext = new HostAssignmentContext
            {
                SiteId = 1234,
                SiteName = "TestSite",
                Environment = environment,
                IsWarmupRequest = false,
                MSIContext = new MSIContext()
            };

            var instanceManager = GetInstanceManagerForMSISpecialization(assignmentContext, HttpStatusCode.OK, null);

            string error = await instanceManager.SpecializeMSISidecar(assignmentContext);
            Assert.Null(error);

            var logs = _loggerProvider.GetAllLogMessages().Select(p => p.FormattedMessage).ToArray();
            Assert.Collection(logs,
                p => Assert.StartsWith("MSI enabled status: True", p),
                p => Assert.StartsWith("Specializing sidecar at http://localhost:8081", p),
                p => Assert.StartsWith("Specialize MSI sidecar returned OK", p));
        }

        [Fact]
        public async Task SpecializeMSISidecar_Succeeds_EncryptedMSIContextWithoutProvidedEndpoint()
        {
            var environment = new Dictionary<string, string>()
            {
                { EnvironmentSettingNames.MsiEndpoint, "http://localhost:8081" },
                { EnvironmentSettingNames.MsiSecret, "secret" }
            };
            var assignmentContext = new HostAssignmentContext
            {
                SiteId = 1234,
                SiteName = "TestSite",
                Environment = environment,
                IsWarmupRequest = false,
                MSIContext = new MSIContext(),
                EncryptedTokenServiceSpecializationPayload = "TestContext"
            };

            var instanceManager = GetInstanceManagerForMSISpecialization(assignmentContext, HttpStatusCode.OK, null);

            string error = await instanceManager.SpecializeMSISidecar(assignmentContext);
            Assert.Null(error);

            var logs = _loggerProvider.GetAllLogMessages().Select(p => p.FormattedMessage).ToArray();
            Assert.Collection(logs,
                p => Assert.StartsWith("MSI enabled status: True", p),
                p => Assert.StartsWith("Using encrypted TokenService payload format", p),
                p => Assert.Equal($"Specializing sidecar at http://localhost:8081{ScriptConstants.LinuxEncryptedTokenServiceSpecializationStem}", p),
                p => Assert.StartsWith("Specialize MSI sidecar returned OK", p));
        }

        [Fact]
        public async Task SpecializeMSISidecar_Succeeds_EncryptedMSIContextWithProvidedEndpoint()
        {
            var environment = new Dictionary<string, string>()
            {
                { EnvironmentSettingNames.MsiEndpoint, "http://localhost:8081" },
                { EnvironmentSettingNames.MsiSecret, "secret" }
            };
            var assignmentContext = new HostAssignmentContext
            {
                SiteId = 1234,
                SiteName = "TestSite",
                Environment = environment,
                IsWarmupRequest = false,
                MSIContext = new MSIContext(),
                EncryptedTokenServiceSpecializationPayload = "TestContext",
                TokenServiceApiEndpoint = "/api/TestEndpoint"
            };

            var instanceManager = GetInstanceManagerForMSISpecialization(assignmentContext, HttpStatusCode.OK, null);

            string error = await instanceManager.SpecializeMSISidecar(assignmentContext);
            Assert.Null(error);

            var logs = _loggerProvider.GetAllLogMessages().Select(p => p.FormattedMessage).ToArray();
            Assert.Collection(logs,
                p => Assert.StartsWith("MSI enabled status: True", p),
                p => Assert.StartsWith("Using encrypted TokenService payload format", p),
                p => Assert.Equal($"Specializing sidecar at http://localhost:8081{assignmentContext.TokenServiceApiEndpoint}", p),
                p => Assert.StartsWith("Specialize MSI sidecar returned OK", p));
        }

        [Fact]
        public async Task SpecializeMsiSidecar_RequiredPropertiesInPayload()
        {
            var environment = new Dictionary<string, string>()
            {
                { EnvironmentSettingNames.MsiEndpoint, "http://localhost:8081" },
                { EnvironmentSettingNames.MsiSecret, "secret" }
            };
            var assignmentContext = new HostAssignmentContext
            {
                SiteId = 1234,
                SiteName = "TestSite",
                Environment = environment,
                IsWarmupRequest = false,
                MSIContext = new MSIContext()
                {
                    SiteName = "TestSite",
                    MSISecret = "TestSecret1234",
                    Identities = new[] { new ManagedServiceIdentity() { 
                        Type = ManagedServiceIdentityType.SystemAssigned, 
                        ClientId = "identityClientId",
                        TenantId = "identityTenantId",
                        Thumbprint = "identityThumbprint",
                        SecretUrl = "identitySecretUrl",
                        ResourceId = "identityResourceId",
                        Certificate = "identityCertificate",
                        PrincipalId = "identityPrincipalId",
                        AuthenticationEndpoint = "identityAuthEndpoint"
                    } },
                    SystemAssignedIdentity = new ManagedServiceIdentity()
                    {
                        Type = ManagedServiceIdentityType.SystemAssigned,
                        ClientId = "saClientId",
                        TenantId = "saTenantId",
                        Thumbprint = "saThumbprint",
                        SecretUrl = "saSecretUrl",
                        ResourceId = "saResourceId",
                        Certificate = "saCertificate",
                        PrincipalId = "saPrincipalId",
                        AuthenticationEndpoint = "saAuthEndpoint"
                    },
                    DelegatedIdentities = new[] { new ManagedServiceIdentity() {
                        Type = ManagedServiceIdentityType.SystemAssigned,
                        ClientId = "delegatedClientId",
                        TenantId = "delegatedTenantId",
                        Thumbprint = "delegatedThumbprint",
                        SecretUrl = "delegatedSecretUrl",
                        ResourceId = "delegatedResourceId",
                        Certificate = "delegatedCertificate",
                        PrincipalId = "delegatedPrincipalId",
                        AuthenticationEndpoint = "delegatedAuthEndpoint"
                    } },
                    UserAssignedIdentities = new[] { new ManagedServiceIdentity() {
                        Type = ManagedServiceIdentityType.UserAssigned,
                        ClientId = "uaClientId",
                        TenantId = "uaTenantId",
                        Thumbprint = "uaThumbprint",
                        SecretUrl = "uaSecretUrl",
                        ResourceId = "uaResourceId",
                        Certificate = "uaCertificate",
                        PrincipalId = "uaPrincipalId",
                        AuthenticationEndpoint = "uaAuthEndpoint"
                    } },
                }
            };
            
            static void verifyMSIPropertiesHelper(ManagedServiceIdentity msi)
            {
                Assert.NotNull(msi);
                Assert.NotNull(msi.Type);
                Assert.NotNull(msi.ClientId);
                Assert.NotNull(msi.TenantId);
                Assert.NotNull(msi.Thumbprint);
                Assert.NotNull(msi.SecretUrl);
                Assert.NotNull(msi.ResourceId);
                Assert.NotNull(msi.Certificate);
                Assert.NotNull(msi.PrincipalId);
                Assert.NotNull(msi.AuthenticationEndpoint);
            }

            static void verifyProperties(HttpRequestMessage request, CancellationToken token)
            {
                var requestContent = request.Content.ReadAsStringAsync(token).GetAwaiter().GetResult();
                var msiContext = JsonConvert.DeserializeObject<MSIContext>(requestContent);
                Assert.NotNull(msiContext);
                Assert.NotNull(msiContext.Identities);
                Assert.NotNull(msiContext.SystemAssignedIdentity);
                Assert.NotNull(msiContext.UserAssignedIdentities);
                Assert.NotNull(msiContext.DelegatedIdentities);

                var identityList = new List<ManagedServiceIdentity>();
                identityList.AddRange(msiContext.Identities);
                identityList.Add(msiContext.SystemAssignedIdentity);
                identityList.AddRange(msiContext.UserAssignedIdentities);
                identityList.AddRange(msiContext.DelegatedIdentities);

                foreach (ManagedServiceIdentity identity in identityList)
                {
                    verifyMSIPropertiesHelper(identity);
                }

                Assert.True(!string.IsNullOrEmpty(msiContext.MSISecret));
                Assert.True(!string.IsNullOrEmpty(msiContext.SiteName));
            }

            var instanceManager = GetInstanceManagerForMSISpecialization(assignmentContext, HttpStatusCode.OK, null, customAction: verifyProperties);

            string error = await instanceManager.SpecializeMSISidecar(assignmentContext);
            Assert.Null(error);

            var logs = _loggerProvider.GetAllLogMessages().Select(p => p.FormattedMessage).ToArray();
            Assert.Collection(logs,
                p => Assert.StartsWith("MSI enabled status: True", p),
                p => Assert.StartsWith("Specializing sidecar at http://localhost:8081", p),
                p => Assert.StartsWith("Specialize MSI sidecar returned OK", p));
        }

        [Fact]
        public async Task SpecializeMSISidecar_NoOp_ForWarmup_Request()
        {
            var environment = new Dictionary<string, string>()
            {
                { EnvironmentSettingNames.MsiEndpoint, "http://localhost:8081" },
                { EnvironmentSettingNames.MsiSecret, "secret" }
            };
            var assignmentContext = new HostAssignmentContext
            {
                SiteId = 1234,
                SiteName = "TestSite",
                Environment = environment,
                IsWarmupRequest = true
            };

            var instanceManager = GetInstanceManagerForMSISpecialization(assignmentContext, HttpStatusCode.OK, null);

            string error = await instanceManager.SpecializeMSISidecar(assignmentContext);
            Assert.Null(error);

            var logs = _loggerProvider.GetAllLogMessages().Select(p => p.FormattedMessage).ToArray();
            Assert.Empty(logs);
        }

        [Fact]
        public async Task SpecializeMSISidecar_Fails()
        {
            var environment = new Dictionary<string, string>()
            {
                { EnvironmentSettingNames.MsiEndpoint, "http://localhost:8081" },
                { EnvironmentSettingNames.MsiSecret, "secret" }
            };
            var assignmentContext = new HostAssignmentContext
            {
                SiteId = 1234,
                SiteName = "TestSite",
                Environment = environment,
                IsWarmupRequest = false,
                MSIContext = new MSIContext()
            };

            var meshServiceClient = new Mock<IMeshServiceClient>(MockBehavior.Strict);

            meshServiceClient.Setup(c => c.NotifyHealthEvent(ContainerHealthEventType.Fatal,
                It.Is<Type>(t => t == typeof(AtlasInstanceManager)), "Failed to specialize MSI sidecar")).Returns(Task.CompletedTask);

            var instanceManager = GetInstanceManagerForMSISpecialization(assignmentContext, HttpStatusCode.BadRequest, meshServiceClient.Object);

            string error = await instanceManager.SpecializeMSISidecar(assignmentContext);
            Assert.NotNull(error);

            var logs = _loggerProvider.GetAllLogMessages().Select(p => p.FormattedMessage).ToArray();
            Assert.Collection(logs,
                p => Assert.StartsWith("MSI enabled status: True", p),
                p => Assert.StartsWith("Specializing sidecar at http://localhost:8081", p),
                p => Assert.StartsWith("Specialize MSI sidecar returned BadRequest", p),
                p => Assert.StartsWith("Specialize MSI sidecar call failed. StatusCode=BadRequest", p));

            meshServiceClient.Verify(c => c.NotifyHealthEvent(ContainerHealthEventType.Fatal,
                It.Is<Type>(t => t == typeof(AtlasInstanceManager)), "Failed to specialize MSI sidecar"), Times.Once);
        }

        [Fact]
        public async Task DoesNotSpecializeMSISidecar_WhenMSIContextNull()
        {
            var environment = new Dictionary<string, string>()
            {
                { EnvironmentSettingNames.MsiEndpoint, "http://localhost:8081" },
                { EnvironmentSettingNames.MsiSecret, "secret" }
            };
            var assignmentContext = new HostAssignmentContext
            {
                SiteId = 1234,
                SiteName = "TestSite",
                Environment = environment,
                IsWarmupRequest = false,
                MSIContext = null
            };


            var meshServiceClient = new Mock<IMeshServiceClient>(MockBehavior.Strict);
            meshServiceClient.Setup(c => c.NotifyHealthEvent(ContainerHealthEventType.Fatal,
                It.Is<Type>(t => t == typeof(AtlasInstanceManager)), "Could not specialize MSI sidecar since MSIContext and EncryptedTokenServiceSpecializationPayload were empty")).Returns(Task.CompletedTask);

            var instanceManager = GetInstanceManagerForMSISpecialization(assignmentContext, HttpStatusCode.BadRequest, meshServiceClient.Object);

            string error = await instanceManager.SpecializeMSISidecar(assignmentContext);
            Assert.Null(error);

            var logs = _loggerProvider.GetAllLogMessages().Select(p => p.FormattedMessage).ToArray();
            Assert.Collection(logs,
                p => Assert.StartsWith("MSI enabled status: True", p),
                p => Assert.StartsWith("Skipping specialization of MSI sidecar since MSIContext and EncryptedTokenServiceSpecializationPayload were absent", p));

            meshServiceClient.Verify(c => c.NotifyHealthEvent(ContainerHealthEventType.Fatal,
                It.Is<Type>(t => t == typeof(AtlasInstanceManager)), "Could not specialize MSI sidecar since MSIContext and EncryptedTokenServiceSpecializationPayload were empty"), Times.Once);
        }

        [Fact]
        public async Task Mounts_Valid_BYOS_Accounts()
        {
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");

            const string account1 = "storageaccount1";
            const string share1 = "share1";
            const string accessKey1 = "key1key1key1==";
            const string targetPath1 = "/data";

            const string account2 = "storageaccount2";
            const string share2 = "share2";
            const string accessKey2 = "key2key2key2==";
            const string targetPath2 = "/data/store2";

            const string account3 = "storageaccount3";
            const string share3 = "share3";
            const string accessKey3 = "key3key3key3==";
            const string targetPath3 = "/somepath";

            var hostAssignmentContext = new HostAssignmentContext()
            {
                Environment = new Dictionary<string, string>
                {
                    [EnvironmentSettingNames.MsiSecret] = "secret",
                    ["AZUREFILESSTORAGE_storage1"] = $"{account1}|{share1}|{accessKey1}|{targetPath1}",
                    ["AZUREFILESSTORAGE_storage2"] = $"{account2}|{share2}|{accessKey2}|{targetPath2}",
                    ["AZUREBLOBSTORAGE_blob1"] = $"{account3}|{share3}|{accessKey3}|{targetPath3}",
                    [EnvironmentSettingNames.MsiEndpoint] = "endpoint",
                },
                SiteId = 1234,
                SiteName = "TestSite",
                IsWarmupRequest = false
            };

            var meshInitServiceClient = new Mock<IMeshServiceClient>(MockBehavior.Strict);
            meshInitServiceClient.Setup(client =>
                    client.MountCifs(Utility.BuildStorageConnectionString(account1, accessKey1, CloudConstants.AzureStorageSuffix), share1, targetPath1))
                .Throws(new Exception("Mount failure"));
            meshInitServiceClient.Setup(client =>
                client.MountCifs(Utility.BuildStorageConnectionString(account2, accessKey2, CloudConstants.AzureStorageSuffix), share2, targetPath2)).Returns(Task.FromResult(true));
            meshInitServiceClient.Setup(client =>
                client.MountBlob(Utility.BuildStorageConnectionString(account3, accessKey3, CloudConstants.AzureStorageSuffix), share3, targetPath3)).Returns(Task.FromResult(true));

            var instanceManager = new AtlasInstanceManager(_optionsFactory, _httpClientFactory, _scriptWebEnvironment, _environment,
                _loggerFactory.CreateLogger<AtlasInstanceManager>(), new TestMetricsLogger(), meshInitServiceClient.Object,
                _runFromPackageHandler, _packageDownloadHandler.Object);

            instanceManager.StartAssignment(hostAssignmentContext);

            await TestHelpers.Await(() => !_scriptWebEnvironment.InStandbyMode, timeout: 5000);

            meshInitServiceClient.Verify(
                client => client.MountCifs(Utility.BuildStorageConnectionString(account1, accessKey1, CloudConstants.AzureStorageSuffix), share1,
                    targetPath1), Times.Exactly(2));
            meshInitServiceClient.Verify(
                client => client.MountCifs(Utility.BuildStorageConnectionString(account2, accessKey2, CloudConstants.AzureStorageSuffix), share2,
                    targetPath2), Times.Once);
            meshInitServiceClient.Verify(
                client => client.MountBlob(Utility.BuildStorageConnectionString(account3, accessKey3, CloudConstants.AzureStorageSuffix), share3,
                    targetPath3), Times.Once);
        }

        [Fact]
        public async Task Does_Not_Mount_Invalid_BYOS_Accounts()
        {
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");

            const string account1 = "storageaccount1";
            const string share1 = "share1";
            const string accessKey1 = "key1key1key1==";
            const string targetPath1 = "/data";

            const string account2 = "storageaccount2";
            const string share2 = "share2";
            const string accessKey2 = "key2key2key2==";

            var hostAssignmentContext = new HostAssignmentContext()
            {
                Environment = new Dictionary<string, string>
                {
                    [EnvironmentSettingNames.MsiSecret] = "secret",
                    ["AZUREFILESSTORAGE_storage1"] = $"{account1}|{share1}|{accessKey1}|{targetPath1}",
                    ["AZUREFILESSTORAGE_storage2"] = $"{account2}|{share2}|{accessKey2}",
                    ["AZUREBLOBSTORAGE_blob1"] = $"",
                    [EnvironmentSettingNames.MsiEndpoint] = "endpoint",
                },
                SiteId = 1234,
                SiteName = "TestSite",
                IsWarmupRequest = false
            };

            var meshInitServiceClient = new Mock<IMeshServiceClient>(MockBehavior.Strict);

            meshInitServiceClient.Setup(client =>
                client.MountCifs(Utility.BuildStorageConnectionString(account1, accessKey1, CloudConstants.AzureStorageSuffix), share1, targetPath1)).Returns(Task.FromResult(true));

            var instanceManager = new AtlasInstanceManager(_optionsFactory, _httpClientFactory, _scriptWebEnvironment, _environment,
                _loggerFactory.CreateLogger<AtlasInstanceManager>(), new TestMetricsLogger(), meshInitServiceClient.Object,
                _runFromPackageHandler, _packageDownloadHandler.Object);

            instanceManager.StartAssignment(hostAssignmentContext);

            await Task.Delay(TimeSpan.FromSeconds(0.5));

            meshInitServiceClient.Verify(
                client => client.MountCifs(Utility.BuildStorageConnectionString(account1, accessKey1, CloudConstants.AzureStorageSuffix), share1,
                    targetPath1), Times.Once);

            meshInitServiceClient.Verify(
                client => client.MountCifs(It.IsAny<string>(), It.IsAny<string>(), It.Is<string>(s => s != targetPath1)), Times.Never());
        }

        [Theory]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(true, true)]
        [InlineData(false, false)]
        public async void Uses_Azure_Files_For_PowerShell_Apps(bool azureFilesConfigured, bool runFromPackageConfigured)
        {
            const string url = "http://url";
            const string connectionString = "AzureFiles-ConnectionString";
            const string contentShare = "Content-Share";
            const string scriptPath = "/home/site/wwwroot";

            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");
            var context = new HostAssignmentContext
            {
                IsWarmupRequest = false,
                Environment = new Dictionary<string, string>()
                {
                    [EnvironmentSettingNames.FunctionWorkerRuntime] = RpcWorkerConstants.PowerShellLanguageWorkerName,
                }
            };

            if (azureFilesConfigured)
            {
                context.Environment[EnvironmentSettingNames.AzureFilesConnectionString] = connectionString;
                context.Environment[EnvironmentSettingNames.AzureFilesContentShare] = contentShare;
            }

            if (runFromPackageConfigured)
            {
                context.Environment[EnvironmentSettingNames.AzureWebsiteRunFromPackage] = url;
            }

            var runFromPackageHandler = new Mock<IRunFromPackageHandler>(MockBehavior.Strict);
            runFromPackageHandler.Setup(r => r.MountAzureFileShare(context)).ReturnsAsync(true);
            runFromPackageHandler
                .Setup(r => r.ApplyRunFromPackageContext(It.IsAny<RunFromPackageContext>(), It.IsAny<string>(), true,
                    false)).ReturnsAsync(true);

            var optionsFactory = new TestOptionsFactory<ScriptApplicationHostOptions>(new ScriptApplicationHostOptions() { ScriptPath = scriptPath });

            var instanceManager = new AtlasInstanceManager(optionsFactory, _httpClientFactory, _scriptWebEnvironment, _environment,
                _loggerFactory.CreateLogger<AtlasInstanceManager>(), new TestMetricsLogger(), _meshServiceClientMock.Object,
                runFromPackageHandler.Object, _packageDownloadHandler.Object);

            bool result = instanceManager.StartAssignment(context);
            Assert.True(result);

            await TestHelpers.Await(() => !_scriptWebEnvironment.InStandbyMode, timeout: 5000);

            if (azureFilesConfigured)
            {
                runFromPackageHandler.Verify(r => r.MountAzureFileShare(context), Times.Once);
            }
            else
            {
                runFromPackageHandler.Verify(r => r.MountAzureFileShare(It.IsAny<HostAssignmentContext>()), Times.Never);
            }

            if (runFromPackageConfigured)
            {
                runFromPackageHandler
                    .Verify(r => r.ApplyRunFromPackageContext(It.Is<RunFromPackageContext>(c => MatchesRunFromPackageContext(c, url)), scriptPath, azureFilesConfigured,
                        false), Times.Once);
            }
            else
            {
                runFromPackageHandler
                    .Verify(
                        r => r.ApplyRunFromPackageContext(It.IsAny<RunFromPackageContext>(), It.IsAny<string>(),
                            It.IsAny<bool>(), It.IsAny<bool>()), Times.Never);
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async void Uses_Local_Disk_If_Azure_Files_Unavailable_For_PowerShell_Apps(bool azureFilesMounted)
        {
            const string url = "http://url";
            const string connectionString = "AzureFiles-ConnectionString";
            const string contentShare = "Content-Share";
            const string scriptPath = "/home/site/wwwroot";

            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");
            var context = new HostAssignmentContext
            {
                IsWarmupRequest = false,
                Environment = new Dictionary<string, string>()
                {
                    [EnvironmentSettingNames.FunctionWorkerRuntime] = RpcWorkerConstants.PowerShellLanguageWorkerName,
                    [EnvironmentSettingNames.AzureWebsiteRunFromPackage] = url
                }
            };

            // AzureFiles
            context.Environment[EnvironmentSettingNames.AzureFilesConnectionString] = connectionString;
            context.Environment[EnvironmentSettingNames.AzureFilesContentShare] = contentShare;

            var runFromPackageHandler = new Mock<IRunFromPackageHandler>(MockBehavior.Strict);
            runFromPackageHandler.Setup(r => r.MountAzureFileShare(context)).ReturnsAsync(azureFilesMounted);
            runFromPackageHandler
                .Setup(r => r.ApplyRunFromPackageContext(It.IsAny<RunFromPackageContext>(), It.IsAny<string>(), azureFilesMounted,
                    false)).ReturnsAsync(true);

            var optionsFactory = new TestOptionsFactory<ScriptApplicationHostOptions>(new ScriptApplicationHostOptions() { ScriptPath = scriptPath });

            var instanceManager = new AtlasInstanceManager(optionsFactory, _httpClientFactory, _scriptWebEnvironment, _environment,
                _loggerFactory.CreateLogger<AtlasInstanceManager>(), new TestMetricsLogger(), _meshServiceClientMock.Object,
                runFromPackageHandler.Object, _packageDownloadHandler.Object);

            bool result = instanceManager.StartAssignment(context);
            Assert.True(result);

            await TestHelpers.Await(() => !_scriptWebEnvironment.InStandbyMode, timeout: 5000);

            runFromPackageHandler.Verify(r => r.MountAzureFileShare(context), Times.Once);

            runFromPackageHandler
                .Verify(r => r.ApplyRunFromPackageContext(It.Is<RunFromPackageContext>(c => MatchesRunFromPackageContext(c, url)), scriptPath, azureFilesMounted,
                    false), Times.Once);
        }

        [Fact]
        public async void Falls_Back_To_Local_Disk_If_Azure_Files_Unavailable_For_PowerShell_Apps()
        {
            const string url = "http://url";
            const string connectionString = "AzureFiles-ConnectionString";
            const string contentShare = "Content-Share";
            const string scriptPath = "/home/site/wwwroot";

            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");
            var context = new HostAssignmentContext
            {
                IsWarmupRequest = false,
                Environment = new Dictionary<string, string>()
                {
                    [EnvironmentSettingNames.FunctionWorkerRuntime] = RpcWorkerConstants.PowerShellLanguageWorkerName,
                }
            };

            // Azure files
            context.Environment[EnvironmentSettingNames.AzureFilesConnectionString] = connectionString;
            context.Environment[EnvironmentSettingNames.AzureFilesContentShare] = contentShare;

            // Run-From-Pkg
            context.Environment[EnvironmentSettingNames.AzureWebsiteRunFromPackage] = url;

            var runFromPackageHandler = new Mock<IRunFromPackageHandler>(MockBehavior.Strict);
            runFromPackageHandler.Setup(r => r.MountAzureFileShare(context)).Returns(Task.FromResult(true));
            
            runFromPackageHandler
                .Setup(r => r.ApplyRunFromPackageContext(It.IsAny<RunFromPackageContext>(), It.IsAny<string>(), true,
                    false)).ReturnsAsync(false); // return false to trigger failure

            // 2nd attempt 
            runFromPackageHandler
                .Setup(r => r.ApplyRunFromPackageContext(It.IsAny<RunFromPackageContext>(), It.IsAny<string>(), false,
                    true)).ReturnsAsync(true);

            var optionsFactory = new TestOptionsFactory<ScriptApplicationHostOptions>(new ScriptApplicationHostOptions() { ScriptPath = scriptPath });

            var instanceManager = new AtlasInstanceManager(optionsFactory, _httpClientFactory, _scriptWebEnvironment, _environment,
                _loggerFactory.CreateLogger<AtlasInstanceManager>(), new TestMetricsLogger(), _meshServiceClientMock.Object,
                runFromPackageHandler.Object, _packageDownloadHandler.Object);

            bool result = instanceManager.StartAssignment(context);
            Assert.True(result);

            await TestHelpers.Await(() => !_scriptWebEnvironment.InStandbyMode, timeout: 5000);

            runFromPackageHandler.Verify(r => r.MountAzureFileShare(context), Times.Once);

            runFromPackageHandler
                .Verify(r => r.ApplyRunFromPackageContext(It.Is<RunFromPackageContext>(c => MatchesRunFromPackageContext(c, url)), scriptPath, true,
                    false), Times.Once);

            runFromPackageHandler
                .Verify(r => r.ApplyRunFromPackageContext(It.Is<RunFromPackageContext>(c => MatchesRunFromPackageContext(c, url)), scriptPath, false,
                    true), Times.Once);
        }

        [Fact]
        public async void Falls_Back_To_Local_Disk_If_Azure_Files_Unavailable_Only_If_Azure_Files_Mounted_For_PowerShell_Apps()
        {
            const string url = "http://url";
            const string connectionString = "AzureFiles-ConnectionString";
            const string contentShare = "Content-Share";
            const string scriptPath = "/home/site/wwwroot";

            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");
            var context = new HostAssignmentContext
            {
                IsWarmupRequest = false,
                Environment = new Dictionary<string, string>()
                {
                    [EnvironmentSettingNames.FunctionWorkerRuntime] = RpcWorkerConstants.PowerShellLanguageWorkerName,
                }
            };

            // Azure files
            context.Environment[EnvironmentSettingNames.AzureFilesConnectionString] = connectionString;
            context.Environment[EnvironmentSettingNames.AzureFilesContentShare] = contentShare;

            // Run-From-Pkg
            context.Environment[EnvironmentSettingNames.AzureWebsiteRunFromPackage] = url;

            var runFromPackageHandler = new Mock<IRunFromPackageHandler>(MockBehavior.Strict);
            runFromPackageHandler.Setup(r => r.MountAzureFileShare(context)).Returns(Task.FromResult(false)); // Failed to mount

            runFromPackageHandler
                .Setup(r => r.ApplyRunFromPackageContext(It.IsAny<RunFromPackageContext>(), It.IsAny<string>(), false,
                    false)).ReturnsAsync(false); // return false to trigger failure

            // There will be no 2nd attempt since azure files mounting failed.

            var optionsFactory = new TestOptionsFactory<ScriptApplicationHostOptions>(new ScriptApplicationHostOptions() { ScriptPath = scriptPath });

            var instanceManager = new AtlasInstanceManager(optionsFactory, _httpClientFactory, _scriptWebEnvironment, _environment,
                _loggerFactory.CreateLogger<AtlasInstanceManager>(), new TestMetricsLogger(), _meshServiceClientMock.Object,
                runFromPackageHandler.Object, _packageDownloadHandler.Object);

            bool result = instanceManager.StartAssignment(context);
            Assert.True(result);

            await TestHelpers.Await(() => !_scriptWebEnvironment.InStandbyMode, timeout: 5000);

            runFromPackageHandler.Verify(r => r.MountAzureFileShare(context), Times.Once);

            runFromPackageHandler
                .Verify(r => r.ApplyRunFromPackageContext(It.Is<RunFromPackageContext>(c => MatchesRunFromPackageContext(c, url)), scriptPath, false,
                    false), Times.Once);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async void Uses_Local_Disk_For_Non_PowerShell_Apps(bool azureFilesConfigured)
        {
            const string url = "http://url";
            const string connectionString = "AzureFiles-ConnectionString";
            const string contentShare = "Content-Share";
            const string scriptPath = "/home/site/wwwroot";

            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");
            var context = new HostAssignmentContext
            {
                IsWarmupRequest = false,
                Environment = new Dictionary<string, string>()
                {
                    [EnvironmentSettingNames.FunctionWorkerRuntime] = RpcWorkerConstants.PythonLanguageWorkerName,
                    [EnvironmentSettingNames.AzureWebsiteRunFromPackage] = url
                }
            };

            // AzureFiles
            if (azureFilesConfigured)
            {
                context.Environment[EnvironmentSettingNames.AzureFilesConnectionString] = connectionString;
                context.Environment[EnvironmentSettingNames.AzureFilesContentShare] = contentShare;
            }

            var runFromPackageHandler = new Mock<IRunFromPackageHandler>(MockBehavior.Strict);
            runFromPackageHandler
                .Setup(r => r.ApplyRunFromPackageContext(It.IsAny<RunFromPackageContext>(), It.IsAny<string>(), false,
                    false)).ReturnsAsync(true);

            var optionsFactory = new TestOptionsFactory<ScriptApplicationHostOptions>(new ScriptApplicationHostOptions() { ScriptPath = scriptPath });

            var instanceManager = new AtlasInstanceManager(optionsFactory, _httpClientFactory, _scriptWebEnvironment, _environment,
                _loggerFactory.CreateLogger<AtlasInstanceManager>(), new TestMetricsLogger(), _meshServiceClientMock.Object,
                runFromPackageHandler.Object, _packageDownloadHandler.Object);

            bool result = instanceManager.StartAssignment(context);
            Assert.True(result);

            await TestHelpers.Await(() => !_scriptWebEnvironment.InStandbyMode, timeout: 5000);

            runFromPackageHandler.Verify(r => r.MountAzureFileShare(It.IsAny<HostAssignmentContext>()), Times.Never);

            runFromPackageHandler
                .Verify(r => r.ApplyRunFromPackageContext(It.Is<RunFromPackageContext>(c => MatchesRunFromPackageContext(c, url)), scriptPath, false,
                    true), Times.Once);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async void Mounts_Azure_Files_Only_If_RunFromPkg_Not_Configured_For_Non_PowerShell_Apps(bool runFromPackageConfigured)
        {
            const string url = "http://url";
            const string connectionString = "AzureFiles-ConnectionString";
            const string contentShare = "Content-Share";
            const string scriptPath = "/home/site/wwwroot";

            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");
            var context = new HostAssignmentContext
            {
                IsWarmupRequest = false,
                Environment = new Dictionary<string, string>()
                {
                    [EnvironmentSettingNames.FunctionWorkerRuntime] = RpcWorkerConstants.PythonLanguageWorkerName,
                }
            };

            // AzureFiles
            context.Environment[EnvironmentSettingNames.AzureFilesConnectionString] = connectionString;
            context.Environment[EnvironmentSettingNames.AzureFilesContentShare] = contentShare;

            if (runFromPackageConfigured)
            {
                context.Environment[EnvironmentSettingNames.AzureWebsiteRunFromPackage] = url;
            }

            var runFromPackageHandler = new Mock<IRunFromPackageHandler>(MockBehavior.Strict);
            runFromPackageHandler
                .Setup(r => r.ApplyRunFromPackageContext(It.IsAny<RunFromPackageContext>(), It.IsAny<string>(), false,
                    true)).ReturnsAsync(true);

            runFromPackageHandler.Setup(r => r.MountAzureFileShare(context)).ReturnsAsync(true);

            var optionsFactory = new TestOptionsFactory<ScriptApplicationHostOptions>(new ScriptApplicationHostOptions() { ScriptPath = scriptPath });

            var instanceManager = new AtlasInstanceManager(optionsFactory, _httpClientFactory, _scriptWebEnvironment, _environment,
                _loggerFactory.CreateLogger<AtlasInstanceManager>(), new TestMetricsLogger(), _meshServiceClientMock.Object,
                runFromPackageHandler.Object, _packageDownloadHandler.Object);

            bool result = instanceManager.StartAssignment(context);
            Assert.True(result);

            await TestHelpers.Await(() => !_scriptWebEnvironment.InStandbyMode, timeout: 5000);

            if (runFromPackageConfigured)
            {
                runFromPackageHandler.Verify(r => r.MountAzureFileShare(It.IsAny<HostAssignmentContext>()), Times.Never);
                runFromPackageHandler
                    .Verify(r => r.ApplyRunFromPackageContext(It.Is<RunFromPackageContext>(c => MatchesRunFromPackageContext(c, url)), scriptPath, false,
                        true), Times.Once);
            }
            else
            {
                runFromPackageHandler.Verify(r => r.MountAzureFileShare(context), Times.Once);
                runFromPackageHandler
                    .Verify(r => r.ApplyRunFromPackageContext(It.IsAny<RunFromPackageContext>(), It.IsAny<string>(), It.IsAny<bool>(),
                        It.IsAny<bool>()), Times.Never);
            }
        }


        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async void Mounts_Azure_Files_When_If_RunFromPkg_Is_One(bool runFromLocalZip)
        {
            const string url = "http://url";
            const string connectionString = "AzureFiles-ConnectionString";
            const string contentShare = "Content-Share";
            const string scriptPath = "/home/site/wwwroot";

            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");
            var context = new HostAssignmentContext
            {
                IsWarmupRequest = false,
                Environment = new Dictionary<string, string>()
                {
                    [EnvironmentSettingNames.FunctionWorkerRuntime] = RpcWorkerConstants.PythonLanguageWorkerName,
                }
            };

            // AzureFiles
            context.Environment[EnvironmentSettingNames.AzureFilesConnectionString] = connectionString;
            context.Environment[EnvironmentSettingNames.AzureFilesContentShare] = contentShare;

            if (runFromLocalZip)
            {
                context.Environment[EnvironmentSettingNames.AzureWebsiteRunFromPackage] = "1";
            }
            else
            {
                context.Environment[EnvironmentSettingNames.AzureWebsiteRunFromPackage] = url;
            }

            var runFromPackageHandler = new Mock<IRunFromPackageHandler>(MockBehavior.Strict);
            runFromPackageHandler
                .Setup(r => r.ApplyRunFromPackageContext(It.IsAny<RunFromPackageContext>(), It.IsAny<string>(), false,
                    true)).ReturnsAsync(true);

            runFromPackageHandler.Setup(r => r.MountAzureFileShare(context)).ReturnsAsync(true);

            var optionsFactory = new TestOptionsFactory<ScriptApplicationHostOptions>(new ScriptApplicationHostOptions() { ScriptPath = scriptPath });

            var instanceManager = new AtlasInstanceManager(optionsFactory, _httpClientFactory, _scriptWebEnvironment, _environment,
                _loggerFactory.CreateLogger<AtlasInstanceManager>(), new TestMetricsLogger(), _meshServiceClientMock.Object,
                runFromPackageHandler.Object, _packageDownloadHandler.Object);

            bool result = instanceManager.StartAssignment(context);
            Assert.True(result);

            await TestHelpers.Await(() => !_scriptWebEnvironment.InStandbyMode, timeout: 5000);

            if (runFromLocalZip)
            {
                runFromPackageHandler.Verify(r => r.MountAzureFileShare(context), Times.Once);
                runFromPackageHandler
                    .Verify(r => r.ApplyRunFromPackageContext(It.IsAny<RunFromPackageContext>(), It.IsAny<string>(), true,
                       It.IsAny<bool>()), Times.Once);
            }
            else
            {
                runFromPackageHandler.Verify(r => r.MountAzureFileShare(It.IsAny<HostAssignmentContext>()), Times.Never);
                runFromPackageHandler
                    .Verify(r => r.ApplyRunFromPackageContext(It.Is<RunFromPackageContext>(c => MatchesRunFromPackageContext(c, url)), scriptPath, false,
                        true), Times.Once);
            }
        }

        private static bool MatchesRunFromPackageContext(RunFromPackageContext r, string expectedUrl)
        {
            return string.Equals(r.Url, expectedUrl, StringComparison.OrdinalIgnoreCase);
        }

        private AtlasInstanceManager GetInstanceManagerForMSISpecialization(HostAssignmentContext hostAssignmentContext,
            HttpStatusCode httpStatusCode, IMeshServiceClient meshServiceClient, Action<HttpRequestMessage, CancellationToken> customAction = null)
        {
            var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);

            var msiEndpoint = hostAssignmentContext.Environment[EnvironmentSettingNames.MsiEndpoint] + ScriptConstants.LinuxMSISpecializationStem;

            var defaultEncryptedMsiEndpoint = hostAssignmentContext.Environment[EnvironmentSettingNames.MsiEndpoint] + ScriptConstants.LinuxEncryptedTokenServiceSpecializationStem;

            var providedEncryptedMsiEndpoint = hostAssignmentContext.Environment[EnvironmentSettingNames.MsiEndpoint] + hostAssignmentContext.TokenServiceApiEndpoint;

            handlerMock.Protected().Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(request => request.Method == HttpMethod.Post
                                                         && (request.RequestUri.AbsoluteUri.Equals(msiEndpoint) 
                                                            || request.RequestUri.AbsoluteUri.Equals(defaultEncryptedMsiEndpoint)
                                                            || request.RequestUri.AbsoluteUri.Equals(providedEncryptedMsiEndpoint))
                                                         && request.Content != null),
                ItExpr.IsAny<CancellationToken>())
                .Callback<HttpRequestMessage, CancellationToken>((request, token) => customAction?.Invoke(request, token))
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = httpStatusCode
                });

            _instanceManager.Reset();

            return new AtlasInstanceManager(_optionsFactory, TestHelpers.CreateHttpClientFactory(handlerMock.Object), _scriptWebEnvironment,
                _environment, _loggerFactory.CreateLogger<AtlasInstanceManager>(), new TestMetricsLogger(),
                meshServiceClient, _runFromPackageHandler, _packageDownloadHandler.Object);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, null);
        }

        private class TestEnvironmentEx : TestEnvironment
        {
            public override void SetEnvironmentVariable(string name, string value)
            {
                if (name == "throw")
                {
                    throw new InvalidOperationException("Kaboom!");
                }
                base.SetEnvironmentVariable(name, value);
            }
        }
    }
}
