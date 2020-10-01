// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Azure.WebJobs.Script.WebHost.Security;
using Microsoft.Extensions.Logging;
using Microsoft.WebJobs.Script.Tests;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class StartupContextProviderTests
    {
        private const string TestEncryptionKey = "/a/vXvWJ3Hzgx4PFxlDUJJhQm5QVyGiu0NNLFm/ZMMg=";

        private readonly FunctionAppSecrets _secrets;
        private readonly StartupContextProvider _startupContextProvider;
        private readonly TestEnvironment _environment;
        private readonly TestLoggerProvider _loggerProvider;

        public StartupContextProviderTests()
        {
            _secrets = new FunctionAppSecrets();
            _secrets.Host = new FunctionAppSecrets.HostSecrets
            {
                Master = "test-master-key"
            };
            _secrets.Host.Function = new Dictionary<string, string>
            {
                { "test-host-function-1", "hostfunction1value" },
                { "test-host-function-2", "hostfunction2value" }
            };
            _secrets.Host.System = new Dictionary<string, string>
            {
                { "test-system-1", "system1value" },
                { "test-system-2", "system2value" }
            };
            _secrets.Function = new FunctionAppSecrets.FunctionSecrets[]
            {
                new FunctionAppSecrets.FunctionSecrets
                {
                    Name = "function1",
                    Secrets = new Dictionary<string, string>
                    {
                        { "test-function-1", "function1value" },
                        { "test-function-2", "function2value" }
                    }
                },
                new FunctionAppSecrets.FunctionSecrets
                {
                    Name = "function2",
                    Secrets = new Dictionary<string, string>
                    {
                        { "test-function-1", "function1value" },
                        { "test-function-2", "function2value" }
                    }
                }
            };

            _environment = new TestEnvironment();
            var loggerFactory = new LoggerFactory();
            _loggerProvider = new TestLoggerProvider();
            loggerFactory.AddProvider(_loggerProvider);

            _environment.SetEnvironmentVariable(EnvironmentSettingNames.WebSiteAuthEncryptionKey, TestEncryptionKey);

            _startupContextProvider = new StartupContextProvider(_environment, loggerFactory.CreateLogger<StartupContextProvider>());
        }

        [Fact]
        public async Task GetHostSecretsOrNull_ReturnsExpectedSecrets()
        {
            var path = WriteStartupContext();

            var secrets = _startupContextProvider.GetHostSecretsOrNull();

            Assert.Equal(_secrets.Host.Master, secrets.MasterKey);
            Assert.Equal(_secrets.Host.Function, secrets.FunctionKeys);
            Assert.Equal(_secrets.Host.System, secrets.SystemKeys);

            var logs = _loggerProvider.GetAllLogMessages().Select(p => p.FormattedMessage).ToArray();
            Assert.True(logs.Contains($"Loading startup context from {path}"));
            Assert.True(logs.Contains("Loaded host keys from startup context"));

            // verify the context file is deleted after it's consumed
            await TestHelpers.Await(() =>
            {
                return !File.Exists(path);
            }, timeout: 1000);
        }

        [Fact]
        public void GetFunctionSecretsOrNull_ReturnsExpectedSecrets()
        {
            var path = WriteStartupContext();

            var secrets = _startupContextProvider.GetFunctionSecretsOrNull();

            foreach (var function in _secrets.Function)
            {
                Assert.Equal(function.Secrets, secrets[function.Name]);
            }

            var logs = _loggerProvider.GetAllLogMessages().Select(p => p.FormattedMessage).ToArray();
            Assert.True(logs.Contains($"Loading startup context from {path}"));
            Assert.True(logs.Contains($"Loaded keys for {_secrets.Function.Length} functions from startup context"));
        }

        [Fact]
        public void GetHostSecretsOrNull_NoStartupContext_ReturnsNull()
        {
            var secrets = _startupContextProvider.GetHostSecretsOrNull();
            Assert.Null(secrets);
        }

        [Fact]
        public void GetContext_InvalidJson_ReturnsNull()
        {
            WriteStartupContext("sdlkflk");
            Assert.Null(_startupContextProvider.Context);

            VerifyLastLog(LogLevel.Error, "Failed to load startup context");
        }

        [Fact]
        public void GetContext_InvalidPath_ReturnsNull()
        {
            var path = Path.Combine(Path.GetTempPath(), "dne.txt");
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteStartupContextCache, path);

            Assert.Null(_startupContextProvider.Context);

            VerifyLastLog(LogLevel.Error, "Failed to load startup context");
        }

        [Fact]
        public void GetContext_ExpandsEnvironmentVariables()
        {
            string path = Path.Combine("%TEMP%", $"{Guid.NewGuid()}.txt");
            WriteStartupContext(path: path);

            Assert.NotNull(_startupContextProvider.Context);
        }

        [Fact]
        public void GetContext_EnvVarNotSet_ReturnsNull()
        {
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteStartupContextCache, null);

            Assert.Null(_startupContextProvider.Context);
        }

        [Fact]
        public void GetFunctionSecretsOrNull_NoStartupContext_ReturnsNull()
        {
            var secrets = _startupContextProvider.GetFunctionSecretsOrNull();
            Assert.Null(secrets);
        }

        [Fact]
        public void GetFunctionSecretsOrNull_InvalidDecryptionKey_ReturnsNull()
        {
            WriteStartupContext();

            _environment.SetEnvironmentVariable(EnvironmentSettingNames.WebSiteAuthEncryptionKey, "invalid");

            var secrets = _startupContextProvider.GetFunctionSecretsOrNull();
            Assert.Null(secrets);

            var log = VerifyLastLog(LogLevel.Error, "Failed to load startup context");
            Assert.Equal(typeof(FormatException), log.Exception.GetType());
        }

        [Fact]
        public void SetContext_AppliesHostAssignmentContext()
        {
            var context = new HostAssignmentContext
            {
                Environment = new Dictionary<string, string>(),
                SiteName = "TestSite",
                Secrets = _secrets
            };
            string json = JsonConvert.SerializeObject(context);
            string encrypted = SimpleWebTokenHelper.Encrypt(json, environment: _environment);
            var encryptedContext = new EncryptedHostAssignmentContext { EncryptedContext = encrypted };

            var result = _startupContextProvider.SetContext(encryptedContext);
            Assert.Equal(context.SiteName, result.SiteName);
            Assert.Equal(_secrets.Host.Master, result.Secrets.Host.Master);

            var secrets = _startupContextProvider.GetHostSecretsOrNull();
            Assert.Equal(_secrets.Host.Master, secrets.MasterKey);
            Assert.Equal(_secrets.Host.Function, secrets.FunctionKeys);
            Assert.Equal(_secrets.Host.System, secrets.SystemKeys);
        }

        [Fact]
        public void Does_Not_SetContext_AppliesHostAssignmentContext_For_Warmup_Request()
        {
            var context = new HostAssignmentContext
            {
                Environment = new Dictionary<string, string>(),
                SiteName = "TestSite",
                Secrets = _secrets,
                IsWarmupRequest = true
            };
            string json = JsonConvert.SerializeObject(context);
            string encrypted = SimpleWebTokenHelper.Encrypt(json, environment: _environment);
            var encryptedContext = new EncryptedHostAssignmentContext { EncryptedContext = encrypted };

            var result = _startupContextProvider.SetContext(encryptedContext);
            Assert.Equal(context.SiteName, result.SiteName);
            Assert.Equal(_secrets.Host.Master, result.Secrets.Host.Master);

            var secrets = _startupContextProvider.GetHostSecretsOrNull();
            Assert.Null(secrets);
        }

        private string WriteStartupContext(string context = null, string path = null)
        {
            path = path ?? Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.txt");
            path = Environment.ExpandEnvironmentVariables(path);
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteStartupContextCache, path);

            if (context == null)
            {
                var content = new JObject
                {
                    { "secrets", JObject.FromObject(_secrets) }
                };
                context = JsonConvert.SerializeObject(content);
            }

            var encrypted = SimpleWebTokenHelper.Encrypt(context, environment: _environment);

            File.WriteAllText(path, encrypted);

            return path;
        }

        private LogMessage VerifyLastLog(LogLevel level, string message)
        {
            var logs = _loggerProvider.GetAllLogMessages();
            var log = logs.Last();
            Assert.Equal(level, log.Level);
            Assert.Equal("Failed to load startup context", log.FormattedMessage);

            return log;
        }
    }
}
