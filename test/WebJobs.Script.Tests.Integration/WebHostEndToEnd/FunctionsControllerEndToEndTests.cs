using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.WebJobs.Script.Tests;
using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.EndToEnd
{
    [Trait(TestTraits.Category, TestTraits.EndToEnd)]
    [Trait(TestTraits.Group, TestTraits.FunctionsControllerEndToEnd)]
    public class FunctionsControllerEndToEndTests : FunctionsControllerTestFixture
    {
        [Fact]
        public async Task FunctionsController_GetAllFunctions_ReturnsOk()
        {
            // Capture original instance ID
            var originalInstanceId = this.HostInstanceId;

            // Validate ability to call HttpTrigger without issues
            var response = await SamplesTestHelpers.InvokeHttpTrigger(this, "HttpTrigger");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            // GET admin/functions
            response = await SamplesTestHelpers.InvokeEndpointGet(this, "admin/functions");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            // Validate HttpTrigger function is still working
            response = await SamplesTestHelpers.InvokeHttpTrigger(this, "HttpTrigger");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            // Validate the instance ID is still the same
            Assert.Equal(originalInstanceId, this.HostInstanceId);
        }

        [Fact]
        public async Task FunctionsController_GetSpecificFunction_ReturnsOk()
        {
            // Capture original instance ID
            var originalInstanceId = this.HostInstanceId;

            // Validate ability to call HttpTrigger without issues
            var response = await SamplesTestHelpers.InvokeHttpTrigger(this, "HttpTrigger");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            // GET admin/functions/{funcName}
            response = await SamplesTestHelpers.InvokeEndpointGet(this, "admin/functions/HttpTrigger");
            var responseString = response.Content.ReadAsStringAsync().Result;
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            // Validate HttpTrigger function is still working
            response = await SamplesTestHelpers.InvokeHttpTrigger(this, "HttpTrigger");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            // Validate the instance ID is still the same
            Assert.Equal(originalInstanceId, this.HostInstanceId);
        }

        [Fact]
        public async Task FunctionsController_GetSpecificFunctionStatus_ReturnsOk()
        {
            // Capture original instance ID
            var originalInstanceId = this.HostInstanceId;

            // Validate ability to call HttpTrigger without issues
            var response = await SamplesTestHelpers.InvokeHttpTrigger(this, "HttpTrigger");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            // GET admin/functions/{funcName}
            response = await SamplesTestHelpers.InvokeEndpointGet(this, "admin/functions/HttpTrigger/status");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            // Validate HttpTrigger function is still working
            response = await SamplesTestHelpers.InvokeHttpTrigger(this, "HttpTrigger");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            // Validate the instance ID is still the same
            Assert.Equal(originalInstanceId, this.HostInstanceId);
        }

        [Fact]
        public async Task FunctionsController_CreateUpdate_NoFileChange_ReturnsCreated_NoRestart()
        {
            // Capture original instance ID
            var originalInstanceId = this.HostInstanceId;

            // Validate ability to call HttpTrigger without issues
            var response = await SamplesTestHelpers.InvokeHttpTrigger(this, "HttpTrigger");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            // PUT admin/functions/{funcName}
            response = await SamplesTestHelpers.InvokeEndpointPut(this, "admin/functions/HttpTrigger", TestData());
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

            // Validate HttpTrigger function is still working
            response = await SamplesTestHelpers.InvokeHttpTrigger(this, "HttpTrigger");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            // Validate the instance ID is still the same
            Assert.Equal(originalInstanceId, this.HostInstanceId);
        }

        [Fact]
        public async Task FunctionsController_CreateUpdate_FileChange_ReturnsCreated_RestartsJobHost()
        {
            // Capture pre-restart instance ID
            var originalInstanceId = this.HostInstanceId;

            // Validate ability to call HttpTrigger without issues
            var response = await SamplesTestHelpers.InvokeHttpTrigger(this, "HttpTrigger");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            // PUT admin/functions/{funcName}
            response = await SamplesTestHelpers.InvokeEndpointPut(this, "admin/functions/HttpTrigger", TestData("change"));
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

            // Validate host is running
            HostStatus hostStatus = await Host.GetHostStatusAsync();
            Assert.Equal("Running", hostStatus.State);

            // Validate HttpTrigger function is still working
            response = await SamplesTestHelpers.InvokeHttpTrigger(this, "HttpTrigger");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            // Validate the instance ID has changed
            Assert.NotEqual(originalInstanceId, this.HostInstanceId);

            // Reset config
            response = await SamplesTestHelpers.InvokeEndpointPut(this, "admin/functions/HttpTrigger", TestData());
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        private object TestData(string inputName = "req")
        {
            return new
            {
                name = "HttpTrigger",
                script_root_path_href = "",
                script_href = "",
                config_href = "",
                test_data_href = "",
                secrets_file_href = "",
                href = "https://localhost/admin/functions/HttpTrigger",
                invoke_url_template = "https://localhost/api/httptrigger",
                language = "node",
                config = new
                {
                    bindings = new object[] {
                        new {
                            type = "httpTrigger",
                            direction = "in",
                            name = inputName,
                            methods = new[] { "get", "post" }
                        },
                        new {
                            type = "http",
                            direction = "out",
                            name = "res",
                        }
                    }
                },
                test_data = "hello",
                isDisabled = false,
                isDirect = false,
                isProxy = false
            };
        }
    }

    public class FunctionsControllerTestFixture : EndToEndTestFixture
    {
        static FunctionsControllerTestFixture()
        {
        }

        public FunctionsControllerTestFixture()
            : base(Path.Combine(Environment.CurrentDirectory, "..", "..", "..", "..", "..", "sample", "NodeResume"), "samples", RpcWorkerConstants.NodeLanguageWorkerName)
        {
            Environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsFeatureFlags, ScriptConstants.FeatureFlagEnableWorkerIndexing);
        }

        public override void ConfigureScriptHost(IWebJobsBuilder webJobsBuilder)
        {
            base.ConfigureScriptHost(webJobsBuilder);
        }
    }
}
