// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Tests;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public class WebScriptHostManagerTests : IClassFixture<WebScriptHostManagerTests.Fixture>
    {
        private Fixture _fixture;

        public WebScriptHostManagerTests(Fixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public void FunctionLogFilesArePurgedOnStartup()
        {
            var logDirs = Directory.EnumerateDirectories(_fixture.FunctionsLogDir).Select(p => Path.GetFileName(p).ToLowerInvariant()).ToArray();

            // Even if a function is invalid an not part of the active
            // loaded functions, we don't want to purge data for it
            Assert.True(logDirs.Contains("invalid"));

            Assert.False(logDirs.Contains("foo"));
            Assert.False(logDirs.Contains("bar"));
            Assert.False(logDirs.Contains("baz"));
        }

        [Fact]
        public void SecretFilesArePurgedOnStartup()
        {
            var secretFiles = Directory.EnumerateFiles(_fixture.SecretsPath).Select(p => Path.GetFileName(p)).OrderBy(p => p).ToArray();
            Assert.Equal(4, secretFiles.Length);

            Assert.Equal(ScriptConstants.HostMetadataFileName, secretFiles[0]);
            Assert.Equal("Invalid.json", secretFiles[1]);
            Assert.Equal("QueueTriggerToBlob.json", secretFiles[2]);
            Assert.Equal("WebHookTrigger.json", secretFiles[3]);
        }

        [Fact]
        public async Task EmptyHost_StartsSuccessfully()
        {
            string functionTestDir = Path.Combine(_fixture.TestFunctionRoot, Guid.NewGuid().ToString());
            Directory.CreateDirectory(functionTestDir);

            // important for the repro that these directories no not exist
            string logDir = Path.Combine(_fixture.TestLogsRoot, Guid.NewGuid().ToString());
            string secretsDir = Path.Combine(_fixture.TestSecretsRoot, Guid.NewGuid().ToString());

            JObject hostConfig = new JObject
            {
                { "id", "123456" }
            };
            File.WriteAllText(Path.Combine(functionTestDir, ScriptConstants.HostMetadataFileName), hostConfig.ToString());

            ScriptHostConfiguration config = new ScriptHostConfiguration
            {
                RootScriptPath = functionTestDir,
                RootLogPath = logDir,
                FileLoggingEnabled = true
            };
            SecretManager secretManager = new SecretManager(secretsDir);
            WebHostSettings webHostSettings = new WebHostSettings();
            ScriptHostManager hostManager = new WebScriptHostManager(config, secretManager, webHostSettings);

            Task runTask = Task.Run(() => hostManager.RunAndBlock());

            await TestHelpers.Await(() => hostManager.IsRunning, timeout: 10000);


            hostManager.Stop();
            Assert.False(hostManager.IsRunning);

            string hostLogFilePath = Directory.EnumerateFiles(Path.Combine(logDir, "Host")).Single();
            string hostLogs = File.ReadAllText(hostLogFilePath);

            Assert.True(hostLogs.Contains("Generating 0 job function(s)"));
            Assert.True(hostLogs.Contains("No job functions found."));
            Assert.True(hostLogs.Contains("Job host started"));
            Assert.True(hostLogs.Contains("Job host stopped"));
        }

        [Fact]
        public void GetHttpFunctionOrNull_DecodesUriProperly()
        {
            WebHostSettings webHostSettings = new WebHostSettings();
            WebScriptHostManager manager = new WebScriptHostManager(new ScriptHostConfiguration(), new SecretManager(), webHostSettings);

            // Initialize the 
            FunctionMetadata metadata = new FunctionMetadata();
            metadata.Bindings.Add(new HttpTriggerBindingMetadata
            {
                Type = "HttpTrigger"
            });
            TestInvoker invoker = new TestInvoker();
            Collection<ParameterDescriptor> parameters = new Collection<ParameterDescriptor>();
            Collection<FunctionDescriptor> functions = new Collection<FunctionDescriptor>()
            {
                new FunctionDescriptor("Foo Bar", invoker, metadata, parameters),
                new FunctionDescriptor("éà  中國", invoker, metadata, parameters)
            };
            manager.InitializeHttpFunctions(functions);

            Uri uri = new Uri("http://local/api/Foo Bar");
            var result = manager.GetHttpFunctionOrNull(uri);
            Assert.Same(functions[0], result);

            uri = new Uri("http://local/api/éà  中國");
            result = manager.GetHttpFunctionOrNull(uri);
            Assert.Same(functions[1], result);
        }

        public class Fixture : IDisposable
        {
            public Fixture()
            {
                TestFunctionRoot = Path.Combine(TestHelpers.FunctionsTestDirectory, "Functions");
                TestLogsRoot = Path.Combine(TestHelpers.FunctionsTestDirectory, "Logs");
                TestSecretsRoot = Path.Combine(TestHelpers.FunctionsTestDirectory, "Secrets");

                string testRoot = Path.Combine(TestFunctionRoot, Guid.NewGuid().ToString());

                SecretsPath = Path.Combine(TestSecretsRoot, Guid.NewGuid().ToString());
                Directory.CreateDirectory(SecretsPath);
                string logRoot = Path.Combine(TestLogsRoot, Guid.NewGuid().ToString(), @"Functions");
                Directory.CreateDirectory(logRoot);
                FunctionsLogDir = Path.Combine(logRoot, @"Function");
                Directory.CreateDirectory(FunctionsLogDir);

                // Add some secret files (both old and valid)
                File.Create(Path.Combine(SecretsPath, ScriptConstants.HostMetadataFileName));
                File.Create(Path.Combine(SecretsPath, "WebHookTrigger.json"));
                File.Create(Path.Combine(SecretsPath, "QueueTriggerToBlob.json"));
                File.Create(Path.Combine(SecretsPath, "Foo.json"));
                File.Create(Path.Combine(SecretsPath, "Bar.json"));
                File.Create(Path.Combine(SecretsPath, "Invalid.json"));

                // Add some old file directories
                CreateTestFunctionLogs(FunctionsLogDir, "Foo");
                CreateTestFunctionLogs(FunctionsLogDir, "Bar");
                CreateTestFunctionLogs(FunctionsLogDir, "Baz");
                CreateTestFunctionLogs(FunctionsLogDir, "Invalid");

                ScriptHostConfiguration config = new ScriptHostConfiguration
                {
                    RootScriptPath = @"TestScripts\Node",
                    RootLogPath = logRoot,
                    FileLoggingEnabled = true
                };

                SecretManager secretManager = new SecretManager(SecretsPath);
                WebHostSettings webHostSettings = new WebHostSettings();
                HostManager = new WebScriptHostManager(config, secretManager, webHostSettings);
                Task task = Task.Run(() => { HostManager.RunAndBlock(); });

                TestHelpers.Await(() =>
                {
                    return HostManager.IsRunning;
                }).GetAwaiter().GetResult();
            }

            public WebScriptHostManager HostManager { get; private set; }

            public string FunctionsLogDir { get; private set; }

            public string SecretsPath { get; private set; }

            public string TestFunctionRoot { get; private set; }

            public string TestLogsRoot { get; private set; }

            public string TestSecretsRoot { get; private set; }

            public void Dispose()
            {
                if (HostManager != null)
                {
                    HostManager.Stop();
                    HostManager.Dispose();
                }

                try
                {
                    if (Directory.Exists(TestHelpers.FunctionsTestDirectory))
                    {
                        Directory.Delete(TestHelpers.FunctionsTestDirectory, recursive: true);
                    }
                }
                catch
                {
                    // occasionally get file in use errors
                }
            }

            private void CreateTestFunctionLogs(string logRoot, string functionName)
            {
                string functionLogPath = Path.Combine(logRoot, functionName);
                FileTraceWriter traceWriter = new FileTraceWriter(functionLogPath, TraceLevel.Verbose);
                traceWriter.Verbose("Test log message");
                traceWriter.Flush();
            }
        }
    }
}