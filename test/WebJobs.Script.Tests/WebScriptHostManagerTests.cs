// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Tests;
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

        public class Fixture : IDisposable
        {
            public Fixture()
            {
                string testRoot = Path.Combine(Path.GetTempPath(), "FunctionTests");
                if (Directory.Exists(testRoot))
                {
                    Directory.Delete(testRoot, recursive: true);
                }

                SecretsPath = Path.Combine(testRoot, "TestSecrets");
                Directory.CreateDirectory(SecretsPath);
                string logRoot = Path.Combine(testRoot, @"Functions");
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
                HostManager = new WebScriptHostManager(config, secretManager);
                Task task = Task.Run(() => { HostManager.RunAndBlock(); });

                TestHelpers.Await(() =>
                {
                    return HostManager.IsRunning;
                }).GetAwaiter().GetResult();
            }

            public WebScriptHostManager HostManager { get; private set; }

            public string FunctionsLogDir { get; private set; }

            public string SecretsPath { get; private set; }

            public void Dispose()
            {
                if (HostManager != null)
                {
                    HostManager.Stop();
                    HostManager.Dispose();
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