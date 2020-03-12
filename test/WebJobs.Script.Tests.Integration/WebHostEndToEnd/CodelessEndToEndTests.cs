using Microsoft.Azure.WebJobs.Script.Abstractions.Description;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Management.Models;
using Microsoft.Azure.WebJobs.Script.WebHost.Authentication;
using Microsoft.Azure.WebJobs.Script.WebHost.Management;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration.WebHostEndToEnd
{
    public class CodelessEndToEndTests
    {
        [Theory]
        [InlineData("Node", "node", "HttpTrigger")]
        [InlineData("NoFunction", "node", null)]
        public async Task CodelessFunction_Invokes_HttpTrigger(string path, string workerRuntime, string allowedList)
        {
            var sourceFunctionApp = Path.Combine(Environment.CurrentDirectory, "TestScripts", path);
            Environment.SetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeSettingName, workerRuntime);

            using (var baseTestDir = new TempDirectory())
            {
                string baseTestPath = baseTestDir.Path;

                var metadata = new List<FunctionMetadata>() { CodelessEndToEndTests_Data.GetSampleMetadata("testFn") };
                var provider = new TestCodelessFunctionProvider(metadata, null);

                var functions = allowedList != null ? new[] { allowedList, "testFn" } : null;
                var host = StartLocalHost(baseTestPath, sourceFunctionApp, functions, new List<IFunctionProvider>() { provider });

                // Make sure unauthorized does not work
                var unauthResponse = await host.HttpClient.GetAsync("http://localhost/api/testFn?name=Ankit");
                Assert.Equal(HttpStatusCode.Unauthorized, unauthResponse.StatusCode);

                var testFnKey = await host.GetFunctionSecretAsync("testFn");
                var responseName = await host.HttpClient.GetAsync($"http://localhost/api/testFn?code={testFnKey}&name=Ankit");
                var responseNoName = await host.HttpClient.GetAsync($"http://localhost/api/testFn?code={testFnKey}");

                Assert.Equal(HttpStatusCode.OK, responseName.StatusCode);
                Assert.Equal(HttpStatusCode.OK, responseNoName.StatusCode);

                Assert.Equal("Codeless Provider ran a function successfully with no name parameter.", await responseNoName.Content.ReadAsStringAsync());
                Assert.Equal("Hello, Ankit! Codeless Provider ran a function successfully.", await responseName.Content.ReadAsStringAsync());

                // Regular functions should work as expected
                if (allowedList != null)
                {
                    string key = await host.GetFunctionSecretAsync(allowedList);
                    var notCodeless = await host.HttpClient.GetAsync($"http://localhost/api/{allowedList}?code={key}");
                    Assert.Equal(HttpStatusCode.OK, notCodeless.StatusCode);
                }
            }
        }

        [Theory]
        [InlineData("Node", "node", "HttpTrigger")]
        [InlineData("NoFunction", "node", null)]
        public async Task CodelessFunction_Honors_allowedList(string path, string workerRuntime, string allowedList)
        {
            var sourceFunctionApp = Path.Combine(Environment.CurrentDirectory, "TestScripts", path);
            Environment.SetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeSettingName, workerRuntime);

            using (var baseTestDir = new TempDirectory())
            {
                string baseTestPath = baseTestDir.Path;

                var metadata = new List<FunctionMetadata>() { CodelessEndToEndTests_Data.GetSampleMetadata("testFn1"), CodelessEndToEndTests_Data.GetSampleMetadata("testFn2") };
                var provider = new TestCodelessFunctionProvider(metadata, null);

                var functions = allowedList != null ? new[] { "testFn2", allowedList } : new[] { "testFn2" };
                var host = StartLocalHost(baseTestPath, sourceFunctionApp, functions, new List<IFunctionProvider>() { provider });

                // Make sure unauthorized does not work
                var unauthResponse = await host.HttpClient.GetAsync("http://localhost/api/testFn2?name=Ankit");
                Assert.Equal(HttpStatusCode.Unauthorized, unauthResponse.StatusCode);

                var testFn1Key = await host.GetFunctionSecretAsync("testFn1");
                var testFn2Key = await host.GetFunctionSecretAsync("testFn2");
                var test1 = await host.HttpClient.GetAsync($"http://localhost/api/testFn1?name=Ankit&code={testFn1Key}");
                var test2 = await host.HttpClient.GetAsync($"http://localhost/api/testFn2?name=Ankit&code={testFn2Key}");

                Assert.Equal(HttpStatusCode.NotFound, test1.StatusCode);
                Assert.Equal(HttpStatusCode.OK, test2.StatusCode);

                Assert.Equal("Hello, Ankit! Codeless Provider ran a function successfully.", await test2.Content.ReadAsStringAsync());

                // Regular functions should work as expected
                if (allowedList != null)
                {
                    string key = await host.GetFunctionSecretAsync(allowedList);
                    var notCodeless = await host.HttpClient.GetAsync($"http://localhost/api/{allowedList}?code={key}");
                    Assert.Equal(HttpStatusCode.OK, notCodeless.StatusCode);
                }
            }
        }

        [Theory]
        [InlineData("Node", "node", "HttpTrigger")]
        [InlineData("NoFunction", "node", null)]
        public async Task CodelessFunction_CanUse_MultipleProviders(string path, string workerRuntime, string allowedList)
        {
            var sourceFunctionApp = Path.Combine(Environment.CurrentDirectory, "TestScripts", path);
            Environment.SetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeSettingName, workerRuntime);

            using (var baseTestDir = new TempDirectory())
            {
                string baseTestPath = baseTestDir.Path;

                var metadataList1 = new List<FunctionMetadata>() { CodelessEndToEndTests_Data.GetSampleMetadata("testFn1") };
                var metadataList2 = new List<FunctionMetadata>() { CodelessEndToEndTests_Data.GetSampleMetadata("testFn2") };

                var providerOne = new TestCodelessFunctionProvider(metadataList1, null);
                var providerTwo = new TestCodelessFunctionProvider(metadataList2, null);

                var functions = allowedList != null ? new[] { allowedList, "testFn2", "testFn1" } : null;
                var host = StartLocalHost(baseTestPath, sourceFunctionApp, functions, new List<IFunctionProvider>() { providerOne, providerTwo });

                var testFn1Key = await host.GetFunctionSecretAsync("testFn1");
                var testFn2Key = await host.GetFunctionSecretAsync("testFn2");
                var test1 = await host.HttpClient.GetAsync($"http://localhost/api/testFn1?name=Ankit&code={testFn1Key}");
                var test2 = await host.HttpClient.GetAsync($"http://localhost/api/testFn2?name=Ankit&code={testFn2Key}");

                Assert.Equal(HttpStatusCode.OK, test1.StatusCode);
                Assert.Equal(HttpStatusCode.OK, test2.StatusCode);

                Assert.Equal("Hello, Ankit! Codeless Provider ran a function successfully.", await test1.Content.ReadAsStringAsync());
                Assert.Equal("Hello, Ankit! Codeless Provider ran a function successfully.", await test2.Content.ReadAsStringAsync());

                // Regular functions should work as expected
                if (allowedList != null)
                {
                    string key = await host.GetFunctionSecretAsync(allowedList);
                    var notCodeless = await host.HttpClient.GetAsync($"http://localhost/api/{allowedList}?code={key}");
                    Assert.Equal(HttpStatusCode.OK, notCodeless.StatusCode);
                }
            }
        }

        [Theory]
        [InlineData("Node", "node", "HttpTrigger", 33)]
        [InlineData("NoFunction", "node", null, 0)]
        public async Task CodelessFunction_DoesNot_ListFunctions(string path, string workerRuntime, string allowedList, int listCount)
        {
            // Note: admin/functions call includes all functions, regardless of the allowed list (whitelist)
            var sourceFunctionApp = Path.Combine(Environment.CurrentDirectory, "TestScripts", path);
            Environment.SetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeSettingName, workerRuntime);

            using (var baseTestDir = new TempDirectory())
            {
                string baseTestPath = baseTestDir.Path;

                var metadataList1 = new List<FunctionMetadata>() { CodelessEndToEndTests_Data.GetSampleMetadata("testFn1") };
                var metadataList2 = new List<FunctionMetadata>() { CodelessEndToEndTests_Data.GetSampleMetadata("testFn2") };

                var providerOne = new TestCodelessFunctionProvider(metadataList1, null);
                var providerTwo = new TestCodelessFunctionProvider(metadataList2, null);

                var functions = allowedList != null ? new[] { allowedList, "testFn2", "testFn1" } : null;
                var host = StartLocalHost(baseTestPath, sourceFunctionApp, functions, new List<IFunctionProvider>() { providerOne, providerTwo });

                var masterKey = await host.GetMasterKeyAsync();
                var listFunctionsResponse = await host.HttpClient.GetAsync($"http://localhost/admin/functions?code={masterKey}");

                // List Functions test
                string uri = "admin/functions";
                var request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.Add(AuthenticationLevelHandler.FunctionsKeyHeaderName, "1234");
                var response = await host.HttpClient.SendAsync(request);
                var metadata = (await response.Content.ReadAsAsync<IEnumerable<FunctionMetadataResponse>>()).ToArray();

                Assert.Equal(listCount, metadata.Length);
            }
        }

        [Theory]
        [InlineData("Node", "node", "HttpTrigger")]
        [InlineData("NoFunction", "node", null)]
        public async Task CodelessFunction_SyncTrigger_Succeeds(string path, string workerRuntime, string allowedList)
        {
            var sourceFunctionApp = Path.Combine(Environment.CurrentDirectory, "TestScripts", path);
            Environment.SetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeSettingName, workerRuntime);

            using (var baseTestDir = new TempDirectory())
            {
                string baseTestPath = baseTestDir.Path;

                var metadataList1 = new List<FunctionMetadata>() { CodelessEndToEndTests_Data.GetSampleMetadata("testFn1") };
                var metadataList2 = new List<FunctionMetadata>() { CodelessEndToEndTests_Data.GetSampleMetadata("testFn2") };

                var providerOne = new TestCodelessFunctionProvider(metadataList1, null);
                var providerTwo = new TestCodelessFunctionProvider(metadataList2, null);

                var functions = allowedList != null ? new[] { allowedList, "testFn2", "testFn1" } : null;
                var host = StartLocalHost(baseTestPath, sourceFunctionApp, functions, new List<IFunctionProvider>() { providerOne, providerTwo });

                // Sanity check for sync triggers
                string uri = "admin/host/synctriggers";
                HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, uri);
                request.Headers.Add(AuthenticationLevelHandler.FunctionsKeyHeaderName, await host.GetMasterKeyAsync());
                HttpResponseMessage response = await host.HttpClient.SendAsync(request);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }
        }

        private TestFunctionHost StartLocalHost(string baseTestPath, string sourceFunctionApp, string[] allowedList, IList<IFunctionProvider> providers)
        {
            string appContent = Path.Combine(baseTestPath, "FunctionApp");
            string testLogPath = Path.Combine(baseTestPath, "Logs");

            var syncTriggerMock = new Mock<IFunctionsSyncManager>(MockBehavior.Strict);
            syncTriggerMock.Setup(p => p.TrySyncTriggersAsync(It.IsAny<bool>())).ReturnsAsync(new SyncTriggersResult { Success = true });

            FileUtility.CopyDirectory(sourceFunctionApp, appContent);
            var host = new TestFunctionHost(sourceFunctionApp, testLogPath,
                configureScriptHostWebJobsBuilder: builder =>
                {
                    foreach (var provider in providers)
                    {
                        builder.Services.AddSingleton(provider);
                    }

                    if (allowedList != null && allowedList.Length != 0)
                    {
                        builder.Services.Configure<ScriptJobHostOptions>(o =>
                        {
                            o.Functions = allowedList;
                        });
                    }
                },
                configureScriptHostServices: s =>
                {
                    s.AddSingleton(syncTriggerMock.Object);
                });

            return host;
        }
    }

    public class TestCodelessFunctionProvider : IFunctionProvider
    {
        private ImmutableArray<FunctionMetadata> _functionMetadata;

        public TestCodelessFunctionProvider(IList<FunctionMetadata> metadata, ImmutableDictionary<string, ImmutableArray<string>> errors)
        {
            _functionMetadata = metadata.ToImmutableArray();
            FunctionErrors = errors;
        }

        public ImmutableDictionary<string, ImmutableArray<string>> FunctionErrors { get; }

        public Task<ImmutableArray<FunctionMetadata>> GetFunctionMetadataAsync()
        {
            return Task.FromResult(_functionMetadata);
        }
    }
}
