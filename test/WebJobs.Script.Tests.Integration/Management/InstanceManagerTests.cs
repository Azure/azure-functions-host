// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Management;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Moq.Protected;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Managment
{
    [Trait(TestTraits.Category, TestTraits.EndToEnd)]
    [Trait(TestTraits.Group, TestTraits.ContainerInstanceTests)]
    public class InstanceManagerTests : IDisposable
    {
        private const int assignmentWaitPeriod = 2000; //ms

        private readonly TestLoggerProvider _loggerProvider;
        private readonly TestEnvironmentEx _environment;
        private readonly ScriptWebHostEnvironment _scriptWebEnvironment;
        private readonly InstanceManager _instanceManager;
        private readonly HttpClient _httpClient;
        private readonly LoggerFactory _loggerFactory = new LoggerFactory();
        private readonly TestOptionsFactory<ScriptApplicationHostOptions> _optionsFactory = new TestOptionsFactory<ScriptApplicationHostOptions>(new ScriptApplicationHostOptions() { ScriptPath = Path.GetTempPath() });

        public InstanceManagerTests()
        {
            _httpClient = new HttpClient();

            _loggerProvider = new TestLoggerProvider();
            _loggerFactory.AddProvider(_loggerProvider);

            _environment = new TestEnvironmentEx();
            _scriptWebEnvironment = new ScriptWebHostEnvironment(_environment);

            _instanceManager = new InstanceManager(_optionsFactory, _httpClient, _scriptWebEnvironment, _environment, _loggerFactory.CreateLogger<InstanceManager>(), new TestMetricsLogger());

            InstanceManager.Reset();
        }

        [Fact]
        public async Task StartAssignment_AppliesAssignmentContext()
        {
            var envValue = new
            {
                Name = Path.GetTempFileName().Replace(".", string.Empty),
                Value = Guid.NewGuid().ToString()
            };

            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");
            var context = new HostAssignmentContext
            {
                Environment = new Dictionary<string, string>
                {
                    { envValue.Name, envValue.Value }
                }
            };
            bool result = _instanceManager.StartAssignment(context, isWarmup: false);
            Assert.True(result);
            Assert.True(_scriptWebEnvironment.InStandbyMode);

            // specialization is done in the background
            await Task.Delay(500);

            var value = _environment.GetEnvironmentVariable(envValue.Name);
            Assert.Equal(value, envValue.Value);

            // verify logs
            var logs = _loggerProvider.GetAllLogMessages().Select(p => p.FormattedMessage).ToArray();
            Assert.Collection(logs,
                p => Assert.StartsWith("Starting Assignment", p),
                p => Assert.StartsWith("Applying 1 app setting(s)", p),
                p => Assert.StartsWith("Triggering specialization", p));

            // calling again should return false, since we're no longer
            // in placeholder mode
            _loggerProvider.ClearAllLogMessages();
            result = _instanceManager.StartAssignment(context, isWarmup: false);
            Assert.False(result);

            logs = _loggerProvider.GetAllLogMessages().Select(p => p.FormattedMessage).ToArray();
            Assert.Collection(logs,
                p => Assert.StartsWith("Assign called while host is not in placeholder mode", p));
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
                }
            };
            bool result = _instanceManager.StartAssignment(context, isWarmup: false);
            Assert.True(result);
            Assert.True(_scriptWebEnvironment.InStandbyMode);

            await TestHelpers.Await(() => !_scriptWebEnvironment.InStandbyMode, timeout: 5000);

            var error = _loggerProvider.GetAllLogMessages().First(p => p.Level == LogLevel.Error);
            Assert.Equal("Assign failed", error.FormattedMessage);
            Assert.Equal("Kaboom!", error.Exception.Message);
        }

        [Fact]
        public async Task StartAssignment_Succeeds_With_No_RunFromPackage_AppSetting()
        {
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");
            var context = new HostAssignmentContext
            {
                Environment = new Dictionary<string, string>()
            };
            bool result = _instanceManager.StartAssignment(context, isWarmup: false);
            Assert.True(result);
            Assert.True(_scriptWebEnvironment.InStandbyMode);

            await TestHelpers.Await(() => !_scriptWebEnvironment.InStandbyMode, timeout: 5000);

            var logs = _loggerProvider.GetAllLogMessages().Select(p => p.FormattedMessage).ToArray();
            Assert.Collection(logs,
                p => Assert.StartsWith("Starting Assignment", p),
                p => Assert.StartsWith("Applying 0 app setting(s)", p),
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
                }
            };
            bool result = _instanceManager.StartAssignment(context, isWarmup: false);
            Assert.True(result);

            Thread.Sleep(assignmentWaitPeriod);

            Assert.False(_scriptWebEnvironment.InStandbyMode);

            var logs = _loggerProvider.GetAllLogMessages().Select(p => p.FormattedMessage).ToArray();
            Assert.Collection(logs,
                p => Assert.StartsWith("Starting Assignment", p),
                p => Assert.StartsWith("Applying 1 app setting(s)", p),
                p => Assert.StartsWith("Downloading zip contents from", p),
                p => Assert.EndsWith(" bytes downloaded", p),
                p => Assert.EndsWith(" bytes written", p),
                p => Assert.StartsWith("Running: bash ", p),
                p => Assert.StartsWith("Output:", p),
                p => Assert.StartsWith("error:", p),
                p => Assert.StartsWith("exitCode:", p),
                p => Assert.StartsWith("Triggering specialization", p));
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
                }
            };
            bool result = _instanceManager.StartAssignment(context, isWarmup: false);
            Assert.True(result);

            Thread.Sleep(assignmentWaitPeriod);

            Assert.False(_scriptWebEnvironment.InStandbyMode);

            var logs = _loggerProvider.GetAllLogMessages().Select(p => p.FormattedMessage).ToArray();
            Assert.Collection(logs,
                p => Assert.StartsWith("Starting Assignment", p),
                p => Assert.StartsWith("Applying 1 app setting(s)", p),
                p => Assert.StartsWith("Triggering specialization", p));
        }

        [Fact]
        public void StartAssignment_ReturnsFalse_WhenNotInStandbyMode()
        {
            Assert.False(SystemEnvironment.Instance.IsPlaceholderModeEnabled());

            var context = new HostAssignmentContext();
            context.Environment = new Dictionary<string, string>();
            bool result = _instanceManager.StartAssignment(context, isWarmup: false);
            Assert.False(result);
        }

        [Fact]
        public async Task ValidateContext_InvalidZipUrl_WebsiteUseZip_ReturnsError()
        {
            var environment = new Dictionary<string, string>()
            {
                { EnvironmentSettingNames.AzureWebsiteZipDeployment, "http://invalid.com/invalid/dne" }
            };
            var assignmentContext = new HostAssignmentContext
            {
                SiteId = 1234,
                SiteName = "TestSite",
                Environment = environment
            };

            string error = await _instanceManager.ValidateContext(assignmentContext, isWarmup: false);
            Assert.Equal("Invalid zip url specified (StatusCode: NotFound)", error);

            var logs = _loggerProvider.GetAllLogMessages().Select(p => p.FormattedMessage).ToArray();
            Assert.Collection(logs,
                p => Assert.StartsWith("Validating host assignment context (SiteId: 1234, SiteName: 'TestSite')", p),
                p => Assert.StartsWith($"Will be using {EnvironmentSettingNames.AzureWebsiteZipDeployment} app setting as zip url", p),
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
                Environment = environment
            };

            string error = await _instanceManager.ValidateContext(assignmentContext, isWarmup: false);
            Assert.Null(error);

            string[] expectedOutputLines =
            {
                "Validating host assignment context (SiteId: 1234, SiteName: 'TestSite')",
                $"Will be using  app setting as zip url"
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
                Environment = environment
            };

            string error = await _instanceManager.ValidateContext(assignmentContext, isWarmup: false);
            Assert.Null(error);

            string[] expectedOutputLines =
            {
                "Validating host assignment context (SiteId: 1234, SiteName: 'TestSite')",
                $"Will be using {EnvironmentSettingNames.AzureWebsiteZipDeployment} app setting as zip url"
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
                Environment = environment
            };

            string error = await _instanceManager.ValidateContext(assignmentContext, isWarmup: false);
            Assert.Null(error);

            string[] expectedOutputLines =
            {
                "Validating host assignment context (SiteId: 1234, SiteName: 'TestSite')",
                $"Will be using {EnvironmentSettingNames.ScmRunFromPackage} app setting as zip url"
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
                Environment = environment
            };

            string error = await _instanceManager.ValidateContext(assignmentContext, isWarmup: false);
            Assert.Null(error);

            string[] expectedOutputLines =
            {
                "Validating host assignment context (SiteId: 1234, SiteName: 'TestSite')",
                $"Will be using {EnvironmentSettingNames.AzureWebsiteZipDeployment} app setting as zip url"
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
                Environment = environment
            };

            string error = await _instanceManager.SpecializeMSISidecar(assignmentContext, isWarmup: false);
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
                Environment = environment
            };

            var instanceManager = GetInstanceManagerForMSISpecialization(assignmentContext, HttpStatusCode.OK);

            string error = await instanceManager.SpecializeMSISidecar(assignmentContext, isWarmup: false);
            Assert.Null(error);

            var logs = _loggerProvider.GetAllLogMessages().Select(p => p.FormattedMessage).ToArray();
            Assert.Collection(logs,
                p => Assert.StartsWith("MSI enabled status: True", p),
                p => Assert.StartsWith("Specializing sidecar at http://localhost:8081", p),
                p => Assert.StartsWith("Specialize MSI sidecar returned OK", p));
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
                Environment = environment
            };

            var instanceManager = GetInstanceManagerForMSISpecialization(assignmentContext, HttpStatusCode.BadRequest);

            string error = await instanceManager.SpecializeMSISidecar(assignmentContext, isWarmup: false);
            Assert.NotNull(error);

            var logs = _loggerProvider.GetAllLogMessages().Select(p => p.FormattedMessage).ToArray();
            Assert.Collection(logs,
                p => Assert.StartsWith("MSI enabled status: True", p),
                p => Assert.StartsWith("Specializing sidecar at http://localhost:8081", p),
                p => Assert.StartsWith("Specialize MSI sidecar returned BadRequest", p),
                p => Assert.StartsWith("Specialize MSI sidecar call failed. StatusCode=BadRequest", p));
        }

        private InstanceManager GetInstanceManagerForMSISpecialization(HostAssignmentContext hostAssignmentContext, HttpStatusCode httpStatusCode)
        {
            var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);

            var msiEndpoint = hostAssignmentContext.Environment[EnvironmentSettingNames.MsiEndpoint] + ScriptConstants.LinuxMSISpecializationStem;

            handlerMock.Protected().Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(request => request.Method == HttpMethod.Post
                                                         && request.RequestUri.AbsoluteUri.Equals(msiEndpoint)
                                                         && request.Content != null),
                ItExpr.IsAny<CancellationToken>()).ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = httpStatusCode
                });

            InstanceManager.Reset();

            return new InstanceManager(_optionsFactory, new HttpClient(handlerMock.Object), _scriptWebEnvironment, _environment,
                _loggerFactory.CreateLogger<InstanceManager>(), new TestMetricsLogger());
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
