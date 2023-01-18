// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.WebHost.Management;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration.WebHostEndToEnd
{
    /// <summary>
    /// Class to run tests for Multi Language Runtime
    /// </summary>
    public class MultiLanguageEndToEndTests : IDisposable
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="MultiLanguageEndToEndTests"/> class.
        /// </summary>
        public MultiLanguageEndToEndTests()
        {
            EnvironmentExtensions.ClearCache();
        }

        /// <summary>
        /// Runs tests with multiple language provider function.
        /// </summary>
        [Fact]
        public async Task CodelessFunction_CanUse_MultipleLanguageProviders()
        {
            var sourceFunctionApp = Path.Combine(Environment.CurrentDirectory, "TestScripts", "Node");
            var settings = new Dictionary<string, string>()
            {
                [EnvironmentSettingNames.AppKind] = "workflowApp",
            };
            var testEnvironment = new TestEnvironment(settings);

            using (var baseTestDir = new TempDirectory())
            {
                string baseTestPath = baseTestDir.Path;

                var cSharpMetadataList = new List<FunctionMetadata>() { CodelessEndToEndTests_Data.GetCSharpSampleMetadata("InProcCSFunction") };
                var javascriptMetadataList = new List<FunctionMetadata>() { CodelessEndToEndTests_Data.GetJavascriptSampleMetadata("JavascriptFunction") };
                var javaMetadataList = new List<FunctionMetadata>() { CodelessEndToEndTests_Data.GetJavaSampleMetadata("JavaFunction") };

                var cSharpFunctionProvider = new TestCodelessFunctionProvider(cSharpMetadataList, null);
                var javascriptFunctionProvider = new TestCodelessFunctionProvider(javascriptMetadataList, null);
                var javaFunctionProvider = new TestCodelessFunctionProvider(javaMetadataList, null);

                var functions = new[] { "InProcCSFunction", "JavascriptFunction", "JavaFunction" };
                using (var host = StartLocalHost(baseTestPath, sourceFunctionApp, functions, new List<IFunctionProvider>() { cSharpFunctionProvider, javascriptFunctionProvider, javaFunctionProvider }, testEnvironment))
                {
                    var cSharpFunctionKey = await host.GetFunctionSecretAsync("InProcCSFunction");
                    using var cSharpHttpTriggerResponse = await host.HttpClient.GetAsync($"http://localhost/api/InProcCSFunction?name=Azure&code={cSharpFunctionKey}");
                    Assert.Equal(HttpStatusCode.OK, cSharpHttpTriggerResponse.StatusCode);
                    Assert.Equal("Hello, Azure! Codeless Provider ran a function successfully.", await cSharpHttpTriggerResponse.Content.ReadAsStringAsync());

                    var javaFunctionKey = await host.GetFunctionSecretAsync("JavaFunction");
                    using var javaHttpTriggerResponse = await host.HttpClient.GetAsync($"http://localhost/api/JavaFunction?code={javaFunctionKey}");
                    Assert.Equal(HttpStatusCode.BadRequest, javaHttpTriggerResponse.StatusCode);
                    Assert.Equal("Please pass a name on the query string or in the request body", await javaHttpTriggerResponse.Content.ReadAsStringAsync());

                    var javascriptFunctionKey = await host.GetFunctionSecretAsync("JavascriptFunction");
                    using var javascriptHttpTriggerResponse = await host.HttpClient.GetAsync($"http://localhost/api/JavascriptFunction?name=Azure&code={javascriptFunctionKey}");
                    Assert.Equal(HttpStatusCode.OK, javascriptHttpTriggerResponse.StatusCode);
                    Assert.Equal("Hello Azure", await javascriptHttpTriggerResponse.Content.ReadAsStringAsync());
                }
            }
        }

        /// <summary>
        /// Runs tests with Java language provider function.
        /// </summary>
        [Fact]
        public async Task CodelessFunction_CanUse_SingleJavaLanguageProviders()
        {
            var sourceFunctionApp = Path.Combine(Environment.CurrentDirectory, "TestScripts", "NoFunction");
            var settings = new Dictionary<string, string>()
            {
                [EnvironmentSettingNames.AppKind] = "workflowApp",
            };
            var testEnvironment = new TestEnvironment(settings);

            using (var baseTestDir = new TempDirectory())
            {
                string baseTestPath = baseTestDir.Path;

                var cSharpMetadataList = new List<FunctionMetadata>() { CodelessEndToEndTests_Data.GetCSharpSampleMetadata("InProcCSFunction") };
                var javaMetadataList = new List<FunctionMetadata>() { CodelessEndToEndTests_Data.GetJavaSampleMetadata("JavaFunction") };

                var cSharpFunctionProvider = new TestCodelessFunctionProvider(cSharpMetadataList, null);
                var javaFunctionProvider = new TestCodelessFunctionProvider(javaMetadataList, null);

                var functions = new[] { "InProcCSFunction", "JavaFunction" };
                using (var host = StartLocalHost(baseTestPath, sourceFunctionApp, functions, new List<IFunctionProvider>() { cSharpFunctionProvider, javaFunctionProvider }, testEnvironment))
                {
                    var cSharpFunctionKey = await host.GetFunctionSecretAsync("InProcCSFunction");
                    using var cSharpHttpTriggerResponse = await host.HttpClient.GetAsync($"http://localhost/api/InProcCSFunction?name=Azure&code={cSharpFunctionKey}");
                    Assert.Equal(HttpStatusCode.OK, cSharpHttpTriggerResponse.StatusCode);
                    Assert.Equal("Hello, Azure! Codeless Provider ran a function successfully.", await cSharpHttpTriggerResponse.Content.ReadAsStringAsync());

                    var javaFunctionKey = await host.GetFunctionSecretAsync("JavaFunction");
                    using var javaHttpTriggerResponse = await host.HttpClient.GetAsync($"http://localhost/api/JavaFunction?code={javaFunctionKey}");
                    Assert.Equal(HttpStatusCode.BadRequest, javaHttpTriggerResponse.StatusCode);
                    Assert.Equal("Please pass a name on the query string or in the request body", await javaHttpTriggerResponse.Content.ReadAsStringAsync());
                }
            }
        }

        /// <summary>
        /// Runs tests with Node language provider function.
        /// </summary>
        [Fact]
        public async Task CodelessFunction_CanUse_SingleJavascriptLanguageProviders()
        {
            var sourceFunctionApp = Path.Combine(Environment.CurrentDirectory, "TestScripts", "NoFunction");
            var settings = new Dictionary<string, string>()
            {
                [EnvironmentSettingNames.AppKind] = "workflowApp",
            };
            var testEnvironment = new TestEnvironment(settings);

            using (var baseTestDir = new TempDirectory())
            {
                string baseTestPath = baseTestDir.Path;

                var cSharpMetadataList = new List<FunctionMetadata>() { CodelessEndToEndTests_Data.GetCSharpSampleMetadata("InProcCSFunction") };
                var javascriptMetadataList = new List<FunctionMetadata>() { CodelessEndToEndTests_Data.GetJavascriptSampleMetadata("JavascriptFunction") };

                var cSharpFunctionProvider = new TestCodelessFunctionProvider(cSharpMetadataList, null);
                var javascriptFunctionProvider = new TestCodelessFunctionProvider(javascriptMetadataList, null);

                var functions = new[] { "InProcCSFunction", "JavascriptFunction" };
                using (var host = StartLocalHost(baseTestPath, sourceFunctionApp, functions, new List<IFunctionProvider>() { cSharpFunctionProvider, javascriptFunctionProvider }, testEnvironment))
                {
                    var cSharpFunctionKey = await host.GetFunctionSecretAsync("InProcCSFunction");
                    using var cSharpHttpTriggerResponse = await host.HttpClient.GetAsync($"http://localhost/api/InProcCSFunction?name=Azure&code={cSharpFunctionKey}");
                    Assert.Equal(HttpStatusCode.OK, cSharpHttpTriggerResponse.StatusCode);
                    Assert.Equal("Hello, Azure! Codeless Provider ran a function successfully.", await cSharpHttpTriggerResponse.Content.ReadAsStringAsync());

                    var javascriptFunctionKey = await host.GetFunctionSecretAsync("JavascriptFunction");
                    using var javascriptHttpTriggerResponse = await host.HttpClient.GetAsync($"http://localhost/api/JavascriptFunction?name=Azure&code={javascriptFunctionKey}");
                    Assert.Equal(HttpStatusCode.OK, javascriptHttpTriggerResponse.StatusCode);
                    Assert.Equal("Hello Azure", await javascriptHttpTriggerResponse.Content.ReadAsStringAsync());
                }
            }
        }

        /// <summary>
        /// Runs tests with no language provider function.
        /// </summary>
        [Fact]
        public async Task CodelessFunction_CanUse_NoLanguageProviders()
        {
            var sourceFunctionApp = Path.Combine(Environment.CurrentDirectory, "TestScripts", "NoFunction");
            var settings = new Dictionary<string, string>()
            {
                [EnvironmentSettingNames.AppKind] = "workflowApp",
            };
            var testEnvironment = new TestEnvironment(settings);

            using (var baseTestDir = new TempDirectory())
            {
                string baseTestPath = baseTestDir.Path;

                var cSharpMetadataList = new List<FunctionMetadata>() { CodelessEndToEndTests_Data.GetCSharpSampleMetadata("InProcCSFunction") };

                var cSharpFunctionProvider = new TestCodelessFunctionProvider(cSharpMetadataList, null);

                var functions = new[] { "InProcCSFunction" };
                using (var host = StartLocalHost(baseTestPath, sourceFunctionApp, functions, new List<IFunctionProvider>() { cSharpFunctionProvider }, testEnvironment))
                {
                    var cSharpFunctionKey = await host.GetFunctionSecretAsync("InProcCSFunction");
                    using var cSharpHttpTriggerResponse = await host.HttpClient.GetAsync($"http://localhost/api/InProcCSFunction?name=Azure&code={cSharpFunctionKey}");
                    Assert.Equal(HttpStatusCode.OK, cSharpHttpTriggerResponse.StatusCode);
                    Assert.Equal("Hello, Azure! Codeless Provider ran a function successfully.", await cSharpHttpTriggerResponse.Content.ReadAsStringAsync());
                }
            }
        }

        /// <summary>
        /// Starts a function host
        /// </summary>
        /// <param name="baseTestPath">Base path of test project.</param>
        /// <param name="sourceFunctionApp">Sorce path of function app.</param>
        /// <param name="allowedList">Allowed functions list.</param>
        /// <param name="providers">List of function providers.</param>
        /// <param name="testEnvironment">Environment settings.</param>
        private TestFunctionHost StartLocalHost(string baseTestPath, string sourceFunctionApp, string[] allowedList, IList<IFunctionProvider> providers, IEnvironment testEnvironment)
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

                    builder.Services.AddSingleton(testEnvironment);
                },
                configureScriptHostServices: service =>
                {
                    service.AddSingleton(syncTriggerMock.Object);
                },
                configureWebHostServices: service =>
                {
                    service.AddSingleton(testEnvironment);
                });

            return host;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            EnvironmentExtensions.ClearCache();
        }

        /// <summary>
        /// Class to hold codeless function providers.
        /// </summary>
        public class TestCodelessFunctionProvider : IFunctionProvider
        {
            /// <summary>
            /// List of function metadata.
            /// </summary>
            private ImmutableArray<FunctionMetadata> _functionMetadata;

            /// <summary>
            /// Initializes a new instance of the <see cref="TestCodelessFunctionProvider"/> class.
            /// </summary>
            /// <param name="metadata">Funtion metadata.</param>
            /// <param name="errors">Funtion errors.</param>
            public TestCodelessFunctionProvider(IList<FunctionMetadata> metadata, ImmutableDictionary<string, ImmutableArray<string>> errors)
            {
                _functionMetadata = metadata.ToImmutableArray();
                FunctionErrors = errors;
            }

            /// <summary>
            /// Funtion host errors.
            /// </summary>
            public ImmutableDictionary<string, ImmutableArray<string>> FunctionErrors { get; }

            /// <summary>
            /// Gets the function metadata.
            /// </summary>
            /// <returns></returns>
            public Task<ImmutableArray<FunctionMetadata>> GetFunctionMetadataAsync()
            {
                return Task.FromResult(_functionMetadata);
            }
        }
    }
}
