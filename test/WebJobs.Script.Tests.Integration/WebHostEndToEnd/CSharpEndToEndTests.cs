// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.WebJobs.Script.Tests;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.EndToEnd
{
    [Trait(TestTraits.Category, TestTraits.EndToEnd)]
    [Trait(TestTraits.Group, nameof(CSharpEndToEndTests))]
    public class CSharpEndToEndTests : EndToEndTestsBase<CSharpEndToEndTests.TestFixture>
    {
        public CSharpEndToEndTests(TestFixture fixture) : base(fixture)
        {
        }

        [Fact]
        public async Task ManualTrigger_Invoke_Succeeds()
        {
            await ManualTrigger_Invoke_SucceedsTest();
        }

        [Fact]
        public async Task QueueTriggerToBlob()
        {
            await QueueTriggerToBlobTest();
        }

        [Fact(Skip = "Not yet enabled.")]
        public void MobileTables()
        {
            // await MobileTablesTest(isDotNet: true);
        }

        [Fact(Skip = "Not yet enabled.")]
        public void NotificationHub()
        {
            // await NotificationHubTest("NotificationHubOut");
        }

        [Fact(Skip = "Not yet enabled.")]
        public void NotificationHub_Out_Notification()
        {
            // await NotificationHubTest("NotificationHubOutNotification");
        }

        [Fact(Skip = "Not yet enabled.")]
        public void NotificationHubNative()
        {
            // await NotificationHubTest("NotificationHubNative");
        }

        [Fact(Skip = "Not yet enabled.")]
        public void MobileTablesTable()
        {
            //var id = Guid.NewGuid().ToString();
            //Dictionary<string, object> arguments = new Dictionary<string, object>()
            //{
            //    { "input",  id }
            //};

            //await Fixture.Host.CallAsync("MobileTableTable", arguments);

            //await WaitForMobileTableRecordAsync("Item", id);
        }

        [Fact]
        public async Task FunctionLogging_Succeeds()
        {
            Fixture.Host.ClearLogMessages();

            string functionName = "Scenarios";
            string guid1 = Guid.NewGuid().ToString();
            string guid2 = Guid.NewGuid().ToString();

            var inputObject = new JObject
            {
                { "Scenario", "logging" },
                { "Container", "scenarios-output" },
                { "Value", $"{guid1};{guid2}" }
            };
            await Fixture.Host.BeginFunctionAsync(functionName, inputObject);

            IList<string> logs = null;
            await TestHelpers.Await(() =>
            {
                logs = Fixture.Host.GetScriptHostLogMessages().Select(p => p.FormattedMessage).Where(p => p != null).ToArray();
                return logs.Any(p => p.Contains(guid2));
            });

            logs.Single(p => p.EndsWith($"From TraceWriter: {guid1}"));
            logs.Single(p => p.EndsWith($"From ILogger: {guid2}"));

            // Make sure we've gotten a log from the aggregator
            IEnumerable<LogMessage> getAggregatorLogs() => Fixture.Host.GetScriptHostLogMessages().Where(p => p.Category == LogCategories.Aggregator);

            await TestHelpers.Await(() => getAggregatorLogs().Any());

            var aggregatorLogs = getAggregatorLogs();
            Assert.Equal(1, aggregatorLogs.Count());

            // Make sure that no user logs made it to the EventGenerator (which the SystemLogger writes to)
            IEnumerable<FunctionTraceEvent> allLogs = Fixture.EventGenerator.GetFunctionTraceEvents();
            Assert.False(allLogs.Any(l => l.Summary.Contains("From ")));
            Assert.False(allLogs.Any(l => l.Source.EndsWith(".User")));
            Assert.False(allLogs.Any(l => l.Source == WorkerConstants.FunctionConsoleLogCategoryName));
            Assert.NotEmpty(allLogs);
        }

        [Fact]
        public async Task VerifyHostHeader()
        {
            const string actualHost = "actual-host";
            const string actualProtocol = "https";
            const string path = "api/httptrigger-scenarios";
            var protocolHeaders = new[] { "https", "http" };
            var request = new HttpRequestMessage
            {
                RequestUri = new Uri(string.Format($"http://localhost/{path}")),
                Method = HttpMethod.Post
            };

            request.Headers.TryAddWithoutValidation("DISGUISED-HOST", actualHost);
            request.Headers.Add("X-Forwarded-Proto", protocolHeaders);

            var input = new JObject
            {
                { "scenario", "appServiceFixupMiddleware" }
            };
            request.Content = new StringContent(input.ToString(), Encoding.UTF8, "application/json");
            var response = await Fixture.Host.HttpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var url = await response.Content.ReadAsStringAsync();
            Assert.Equal($"{actualProtocol}://{actualHost}/{path}", url);
        }

        [Fact]
        public async Task VerifyResultRedirect()
        {
            const string path = "api/httptrigger-redirect";
            var request = new HttpRequestMessage
            {
                RequestUri = new Uri(string.Format($"http://localhost/{path}")),
                Method = HttpMethod.Get
            };

            var response = await Fixture.Host.HttpClient.SendAsync(request);
            Assert.Equal(response.StatusCode, HttpStatusCode.Redirect);
        }

        [Fact]
        public async Task MultipleOutputs()
        {
            string id1 = Guid.NewGuid().ToString();
            string id2 = Guid.NewGuid().ToString();
            string id3 = Guid.NewGuid().ToString();

            JObject input = new JObject
            {
                { "Id1", id1 },
                { "Id2", id2 },
                { "Id3", id3 }
            };

            await Fixture.Host.BeginFunctionAsync("MultipleOutputs", input);

            // verify all 3 output blobs were written
            var blob = Fixture.TestOutputContainer.GetBlockBlobReference(id1);
            await TestHelpers.WaitForBlobAsync(blob);
            string blobContent = await blob.DownloadTextAsync();
            Assert.Equal("Test Blob 1", Utility.RemoveUtf8ByteOrderMark(blobContent));

            blob = Fixture.TestOutputContainer.GetBlockBlobReference(id2);
            await TestHelpers.WaitForBlobAsync(blob);
            blobContent = await blob.DownloadTextAsync();
            Assert.Equal("Test Blob 2", Utility.RemoveUtf8ByteOrderMark(blobContent));

            blob = Fixture.TestOutputContainer.GetBlockBlobReference(id3);
            await TestHelpers.WaitForBlobAsync(blob);
            blobContent = await blob.DownloadTextAsync();
            Assert.Equal("Test Blob 3", Utility.RemoveUtf8ByteOrderMark(blobContent));
        }

        [Fact]
        public async Task ScriptReference_LoadsScript()
        {
            HttpResponseMessage response = await Fixture.Host.HttpClient.GetAsync($"api/LoadScriptReference");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("TestClass", await response.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task MissingAssemblies_ShowsHelpfulMessage()
        {
            HttpResponseMessage response = await Fixture.Host.HttpClient.GetAsync($"api/MissingAssemblies");
            var logs = Fixture.Host.GetScriptHostLogMessages().Select(p => p.FormattedMessage).Where(p => p != null).ToArray();
            var hasWarning = logs.Any(p => p.Contains("project.json' should not be used to reference NuGet packages. Try creating a 'function.proj' file instead. Learn more: https://go.microsoft.com/fwlink/?linkid=2091419"));
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.Equal(true, hasWarning);
        }

        [Fact]
        public async Task ExecutionContext_IsPopulated()
        {
            string functionName = "FunctionExecutionContext";
            HttpResponseMessage response = await Fixture.Host.HttpClient.GetAsync($"api/{functionName}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            ExecutionContext context = await response.Content.ReadAsAsync<ExecutionContext>();

            Assert.NotNull(context);
            Assert.Equal(functionName, context.FunctionName);
            Assert.Equal(Path.Combine(Fixture.Host.ScriptOptions.RootScriptPath, functionName), context.FunctionDirectory);
        }

        [Fact]
        public async Task SharedAssemblyDependenciesAreLoaded()
        {
            HttpResponseMessage response = await Fixture.Host.HttpClient.GetAsync("api/AssembliesFromSharedLocation");
            Assert.Equal("secondary type value", await response.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task RandGuidBinding_GeneratesRandomIDs()
        {
            var blobs = await Scenario_RandGuidBinding_GeneratesRandomIDs();

            foreach (var blob in blobs)
            {
                string content = await blob.DownloadTextAsync();
                int blobInt = int.Parse(content.Trim(new char[] { '\uFEFF', '\u200B' }));
                Assert.True(blobInt >= 0 && blobInt <= 3);
            }
        }

        [Fact]
        public async Task HttpTrigger_Post_Dynamic()
        {
            var input = new JObject
            {
                { "name", "Mathew Charles" },
                { "location", "Seattle" }
            };

            HttpRequestMessage request = new HttpRequestMessage
            {
                RequestUri = new Uri(string.Format("http://localhost/api/httptrigger-dynamic")),
                Method = HttpMethod.Post,
                Content = new StringContent(input.ToString())
            };
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            HttpResponseMessage response = await Fixture.Host.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            string body = await response.Content.ReadAsStringAsync();
            Assert.Equal("Name: Mathew Charles, Location: Seattle", body);
        }

        [Fact]
        public async Task HttpTrigger_Model_Binding()
        {
            (JObject req, JObject res) = await MakeModelRequest(Fixture.Host.HttpClient);
            Assert.True(JObject.DeepEquals(req, res), res.ToString());
        }

        [Fact]
        public async Task HttpTrigger_Model_Binding_V2CompatMode()
        {
            // We need a custom host to set this to v2 compat mode.
            using (var host = new TestFunctionHost(@"TestScripts\CSharp", Path.Combine(Path.GetTempPath(), "Functions"),
                configureWebHostServices: webHostServices =>
                {
                    var environment = new TestEnvironment();
                    environment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionsV2CompatibilityModeKey, "true");
                    webHostServices.AddSingleton<IEnvironment>(_ => environment);
                },
                configureScriptHostWebJobsBuilder: webJobsBuilder =>
                {
                    webJobsBuilder.Services.Configure<ScriptJobHostOptions>(o =>
                    {
                        // Only load the functions we care about
                        o.Functions = new[]
                        {
                            "HttpTrigger-Model-v2",
                        };
                    });
                }))
            {

                (JObject req, JObject res) = await MakeModelRequest(host.HttpClient, "-v2");

                // in v2, we expect the response to have a null customEnumerable property.
                req["customEnumerable"] = null;

                Assert.True(JObject.DeepEquals(req, res), res.ToString());
            }
        }

        private static async Task<(JObject requestContent, JObject responseContent)> MakeModelRequest(HttpClient httpClient, string suffix = null)
        {
            var payload = new
            {
                custom = new { customProperty = "value" },
                customEnumerable = new[] { new { customProperty = "value1" }, new { customProperty = "value2" } }
            };

            var jObject = JObject.FromObject(payload);
            var json = jObject.ToString();

            HttpRequestMessage request = new HttpRequestMessage
            {
                RequestUri = new Uri($"http://localhost/api/httptrigger-model{suffix}"),
                Method = HttpMethod.Post,
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            request.Content.Headers.ContentLength = json.Length;

            HttpResponseMessage response = await httpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            return (jObject, JObject.Parse(await response.Content.ReadAsStringAsync()));
        }

        [Fact]
        public async Task HttpTriggerToBlob()
        {
            var request = new HttpRequestMessage
            {
                RequestUri = new Uri($"http://localhost/api/HttpTriggerToBlob?Suffix=TestSuffix"),
                Method = HttpMethod.Post,
            };
            request.Headers.Add("Prefix", "TestPrefix");
            request.Headers.Add("Value", "TestValue");

            var id = Guid.NewGuid().ToString();
            var metadata = new JObject()
            {
                { "M1", "AAA" },
                { "M2", "BBB" }
            };
            var input = new JObject()
            {
                { "Id", id },
                { "Value", "TestInput" },
                { "Metadata", metadata }
            };

            string content = input.ToString();
            request.Content = new StringContent(content, Encoding.UTF8, "application/json");
            request.Content.Headers.ContentLength = content.Length;
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));

            HttpResponseMessage response = await Fixture.Host.HttpClient.SendAsync(request);

            string body = await response.Content.ReadAsStringAsync();
            string expectedValue = $"TestInput{id}TestValue";
            Assert.Equal(expectedValue, body);

            // verify blob was written
            string blobName = $"TestPrefix-{id}-TestSuffix-BBB";
            var outBlob = Fixture.TestOutputContainer.GetBlockBlobReference(blobName);
            string result = await TestHelpers.WaitForBlobAndGetStringAsync(outBlob);
            Assert.Equal(expectedValue, Utility.RemoveUtf8ByteOrderMark(result));
        }

        [Fact(Skip = "Failing due to a change in connection string validation in the WebJobs SDK. Tracking issue: https://github.com/Azure/azure-webjobs-sdk/issues/2415")]
        public async Task FunctionWithIndexingError_ReturnsError()
        {
            FunctionStatus status = await Fixture.Host.GetFunctionStatusAsync("FunctionIndexingError");
            string error = status.Errors.Single();
            Assert.Equal("Microsoft.Azure.WebJobs.Host: Error indexing method 'Functions.FunctionIndexingError'. Microsoft.Azure.WebJobs.Extensions.Storage: Storage account connection string 'setting_does_not_exist' does not exist. Make sure that it is a defined App Setting.", error);
        }

        //[Theory(Skip = "Not yet enabled.")]
        //[InlineData("application/json", "\"Name: Fabio Cavalcante, Location: Seattle\"")]
        //[InlineData("application/xml", "<string xmlns=\"http://schemas.microsoft.com/2003/10/Serialization/\">Name: Fabio Cavalcante, Location: Seattle</string>")]
        //[InlineData("text/plain", "Name: Fabio Cavalcante, Location: Seattle")]
        //public async Task HttpTrigger_GetWithAccept_NegotiatesContent(string accept, string expectedBody)
        //{
        //var input = new JObject
        //{
        //    { "name", "Fabio Cavalcante" },
        //    { "location", "Seattle" }
        //};

        //HttpRequestMessage request = new HttpRequestMessage
        //{
        //    RequestUri = new Uri(string.Format("http://localhost/api/httptrigger-dynamic")),
        //    Method = HttpMethod.Post,
        //    Content = new StringContent(input.ToString())
        //};
        //request.SetConfiguration(Fixture.RequestConfiguration);
        //request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
        //request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        //Dictionary<string, object> arguments = new Dictionary<string, object>
        //{
        //    { "input", request },
        //    { ScriptConstants.SystemTriggerParameterName, request }
        //};

        //await Fixture.Host.CallAsync("HttpTrigger-Dynamic", arguments);

        //HttpResponseMessage response = (HttpResponseMessage)request.Properties[ScriptConstants.AzureFunctionsHttpResponseKey];
        //Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        //Assert.Equal(accept, response.Content.Headers.ContentType.MediaType);

        //string body = await response.Content.ReadAsStringAsync();
        //Assert.Equal(expectedBody, body);
        //}

        public class TestFixture : EndToEndTestFixture
        {
            private const string ScriptRoot = @"TestScripts\CSharp";

            static TestFixture()
            {
                CreateSharedAssemblies();
            }

            public TestFixture() : base(ScriptRoot, "csharp", RpcWorkerConstants.DotNetLanguageWorkerName)
            {
            }

            private static void CreateSharedAssemblies()
            {
                string sharedAssembliesPath = Path.Combine(ScriptRoot, "SharedAssemblies");

                if (Directory.Exists(sharedAssembliesPath))
                {
                    Directory.Delete(sharedAssembliesPath, true);
                }

                Directory.CreateDirectory(sharedAssembliesPath);

                string secondaryDependencyPath = Path.Combine(sharedAssembliesPath, "SecondaryDependency.dll");

                string primaryReferenceSource = @"
using SecondaryDependency;

namespace PrimaryDependency
{
    public class Primary
    {
        public string GetValue()
        {
            var secondary = new Secondary();
            return secondary.GetSecondaryValue();
        }
    }
}";
                string secondaryReferenceSource = @"
namespace SecondaryDependency
{
    public class Secondary
    {
        public string GetSecondaryValue()
        {
            return ""secondary type value"";
        }
    }
}";
                var secondarySyntaxTree = CSharpSyntaxTree.ParseText(secondaryReferenceSource);
                Compilation secondaryCompilation = CSharpCompilation.Create("SecondaryDependency", new[] { secondarySyntaxTree })
                    .WithReferences(MetadataReference.CreateFromFile(typeof(object).Assembly.Location))
                    .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
                secondaryCompilation.Emit(secondaryDependencyPath);

                var primarySyntaxTree = CSharpSyntaxTree.ParseText(primaryReferenceSource);
                Compilation primaryCompilation = CSharpCompilation.Create("PrimaryDependency", new[] { primarySyntaxTree })
                    .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
                    .WithReferences(MetadataReference.CreateFromFile(secondaryDependencyPath), MetadataReference.CreateFromFile(typeof(object).Assembly.Location));

                primaryCompilation.Emit(Path.Combine(sharedAssembliesPath, "PrimaryDependency.dll"));
            }

            public override void ConfigureScriptHost(IWebJobsBuilder webJobsBuilder)
            {
                base.ConfigureScriptHost(webJobsBuilder);

                webJobsBuilder.AddAzureStorage();
                webJobsBuilder.Services.Configure<ScriptJobHostOptions>(o =>
                {
                    // Only load the functions we care about
                    o.Functions = new[]
                    {
                        "AssembliesFromSharedLocation",
                        "HttpTrigger-Dynamic",
                        "HttpTrigger-Scenarios",
                        "HttpTrigger-Model",
                        "HttpTrigger-Redirect",
                        "HttpTriggerToBlob",
                        "FunctionExecutionContext",
                        "LoadScriptReference",
                        "ManualTrigger",
                        "MultipleOutputs",
                        "QueueTriggerToBlob",
                        "Scenarios",
                        "FunctionIndexingError",
                        "MissingAssemblies"
                    };
                });

                webJobsBuilder.Services.Configure<FunctionResultAggregatorOptions>(o =>
                {
                    o.BatchSize = 1;
                });
            }
        }

        public class TestInput
        {
            public int Id { get; set; }

            public string Value { get; set; }
        }
    }
}
