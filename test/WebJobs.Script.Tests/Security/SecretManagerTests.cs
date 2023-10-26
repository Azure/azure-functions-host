// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Azure.WebJobs.Script.WebHost.Properties;
using Microsoft.Azure.WebJobs.Script.WebHost.Security;
using Microsoft.Extensions.Logging;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WebJobs.Script.Tests;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Azure.WebJobs.Script.Tests.Security
{
    public class SecretManagerTests
    {
        private const int TestSentinelWatcherInitializationDelayMS = 50;
        private const string TestEncryptionKey = "/a/vXvWJ3Hzgx4PFxlDUJJhQm5QVyGiu0NNLFm/ZMMg=";
        private readonly HostNameProvider _hostNameProvider;
        private readonly TestEnvironment _testEnvironment;
        private readonly TestLoggerProvider _loggerProvider;
        private readonly ILogger _logger;
        private readonly StartupContextProvider _startupContextProvider;

        public SecretManagerTests(ITestOutputHelper outputHelper)
        {
            _testEnvironment = new TestEnvironment();
            _testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteHostName, "test.azurewebsites.net");

            var loggerFactory = new LoggerFactory()
                .AddTestOutputHelperLoggerProvider(outputHelper)
                .AddTestLoggerProvider(out TestLoggerProvider loggerProvider);

            _loggerProvider = loggerProvider;

            _logger = loggerFactory.CreateLogger(LogCategories.CreateFunctionCategory("test"));

            _hostNameProvider = new HostNameProvider(_testEnvironment);
            _startupContextProvider = new StartupContextProvider(_testEnvironment, loggerFactory.CreateLogger<StartupContextProvider>());
        }

        [Fact]
        public async Task SecretManager_NewlyGeneratedKeysAreIdentifiable()
        {
            using (var directory = new TempDirectory())
            {
                string startupContextPath = Path.Combine(directory.Path, Guid.NewGuid().ToString());
                _testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteStartupContextCache, startupContextPath);
                _testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.WebSiteAuthEncryptionKey, TestEncryptionKey);

                // Because we don't initialize any disk context, we can configure
                // the secret manager to allocate fresh keys on start-up.
                using (var secretManager = CreateSecretManager(directory.Path, createHostSecretsIfMissing: true))
                {
                    var hostSecrets = await secretManager.GetHostSecretsAsync();
                    ValidateHostSecrets(hostSecrets);
                }
            }
        }

        private bool ValidateHostSecrets(HostSecretsInfo hostSecrets)
        {
            // Here and elsewhere we elide the '!' character which is prepended to every
            // generated secret value, as a clue that actual encryption is simulated in
            // the test case.
            string normalizedKey = NormalizeKey(hostSecrets.MasterKey);
            SecretGeneratorTests.ValidateSecret(normalizedKey, SecretGenerator.MasterKeySeed);

            // Create host secrets if missing knob does not allocate a system
            // key. The system key creation/validation test is done in the
            // DefaultScriptWebHookProvider tests.
            Assert.True(hostSecrets.SystemKeys.Count == 0);

            foreach (string key in hostSecrets.FunctionKeys.Values)
            {
                normalizedKey = NormalizeKey(key);
                SecretGeneratorTests.ValidateSecret(normalizedKey, SecretGenerator.FunctionKeySeed);
            }

            return true;
        }

        private string NormalizeKey(string key)
        {
            // Elide the '!' appended to secrets which are simulated as encrypted.
            return key.StartsWith("!") ? key.Substring(1) : key;
        }

        [Fact]
        public async Task CachedSecrets_UsedWhenPresent()
        {
            using (var directory = new TempDirectory())
            {
                string startupContextPath = Path.Combine(directory.Path, Guid.NewGuid().ToString());
                _testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteStartupContextCache, startupContextPath);
                _testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.WebSiteAuthEncryptionKey, TestEncryptionKey);

                WriteStartContextCache(startupContextPath);

                using (var secretManager = CreateSecretManager(directory.Path))
                {
                    var functionSecrets = await secretManager.GetFunctionSecretsAsync("function1", true);

                    Assert.Equal(4, functionSecrets.Count);
                    Assert.Equal("function1value1", functionSecrets["test-function1-1"]);
                    Assert.Equal("function1value2", functionSecrets["test-function1-2"]);
                    Assert.Equal("hostfunction1value", functionSecrets["test-host-function-1"]);
                    Assert.Equal("hostfunction2value", functionSecrets["test-host-function-2"]);

                    var hostSecrets = await secretManager.GetHostSecretsAsync();

                    Assert.Equal("test-master-key", hostSecrets.MasterKey);
                    Assert.Equal(2, hostSecrets.FunctionKeys.Count);
                    Assert.Equal("hostfunction1value", hostSecrets.FunctionKeys["test-host-function-1"]);
                    Assert.Equal("hostfunction2value", hostSecrets.FunctionKeys["test-host-function-2"]);
                    Assert.Equal(2, hostSecrets.SystemKeys.Count);
                    Assert.Equal("system1value", hostSecrets.SystemKeys["test-system-1"]);
                    Assert.Equal("system2value", hostSecrets.SystemKeys["test-system-2"]);
                }

                var logs = _loggerProvider.GetAllLogMessages();
                Assert.Equal($"Sentinel watcher initialized for path {directory.Path}", logs[0].FormattedMessage);
                Assert.Equal($"Loading startup context from {startupContextPath}", logs[1].FormattedMessage);
                Assert.Equal($"Loaded keys for 2 functions from startup context", logs[2].FormattedMessage);
                Assert.Equal($"Loaded host keys from startup context", logs[3].FormattedMessage);
            }
        }

        [Theory]
        [InlineData("function1value1", "test-function1-1", "function1", AuthorizationLevel.Function)]
        [InlineData("function1value2", "test-function1-2", "function1", AuthorizationLevel.Function)]
        [InlineData("function2value1", "test-function2-1", "function2", AuthorizationLevel.Function)]
        [InlineData("function2value2", "test-function2-2", "function2", AuthorizationLevel.Function)]
        [InlineData("function2value1", null, "function1", AuthorizationLevel.Anonymous)]
        [InlineData("function1value1", null, "function2", AuthorizationLevel.Anonymous)]
        [InlineData("invalid", null, "function1", AuthorizationLevel.Anonymous)]
        [InlineData("invalid", null, "function2", AuthorizationLevel.Anonymous)]
        [InlineData("hostfunction1value", "test-host-function-1", "function1", AuthorizationLevel.Function)]
        [InlineData("hostfunction2value", "test-host-function-2", "function1", AuthorizationLevel.Function)]
        [InlineData("hostfunction1value", "test-host-function-1", "function2", AuthorizationLevel.Function)]
        [InlineData("hostfunction2value", "test-host-function-2", "function2", AuthorizationLevel.Function)]
        [InlineData("test-master-key", "master", "function1", AuthorizationLevel.Admin)]
        [InlineData("test-master-key", "master", "function2", AuthorizationLevel.Admin)]
        [InlineData("test-master-key", "master", null, AuthorizationLevel.Admin)]
        [InlineData("system1value", "test-system-1", null, AuthorizationLevel.System)]
        [InlineData("system2value", "test-system-2", null, AuthorizationLevel.System)]
        public async Task GetAuthorizationLevelOrNullAsync_ReturnsExpectedResult(string keyValue, string expectedKeyName, string functionName, AuthorizationLevel expectedLevel)
        {
            using (var directory = new TempDirectory())
            {
                string startupContextPath = Path.Combine(directory.Path, Guid.NewGuid().ToString());
                _testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteStartupContextCache, startupContextPath);
                _testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.WebSiteAuthEncryptionKey, TestEncryptionKey);

                WriteStartContextCache(startupContextPath);

                using (var secretManager = CreateSecretManager(directory.Path))
                {
                    for (int i = 0; i < 3; i++)
                    {
                        (string, AuthorizationLevel) result = await secretManager.GetAuthorizationLevelOrNullAsync(keyValue, functionName);
                        Assert.Equal(result.Item2, expectedLevel);
                        Assert.Equal(result.Item1, expectedKeyName);
                    }
                }
            }
        }

        private FunctionAppSecrets WriteStartContextCache(string path)
        {
            var secrets = new FunctionAppSecrets();
            secrets.Host = new FunctionAppSecrets.HostSecrets
            {
                Master = "test-master-key"
            };
            secrets.Host.Function = new Dictionary<string, string>
            {
                { "test-host-function-1", "hostfunction1value" },
                { "test-host-function-2", "hostfunction2value" }
            };
            secrets.Host.System = new Dictionary<string, string>
            {
                { "test-system-1", "system1value" },
                { "test-system-2", "system2value" }
            };
            secrets.Function = new FunctionAppSecrets.FunctionSecrets[]
            {
                new FunctionAppSecrets.FunctionSecrets
                {
                    Name = "function1",
                    Secrets = new Dictionary<string, string>
                    {
                        { "test-function1-1", "function1value1" },
                        { "test-function1-2", "function1value2" }
                    }
                },
                new FunctionAppSecrets.FunctionSecrets
                {
                    Name = "function2",
                    Secrets = new Dictionary<string, string>
                    {
                        { "test-function2-1", "function2value1" },
                        { "test-function2-2", "function2value2" }
                    }
                }
            };

            var context = new JObject
            {
                { "secrets", JObject.FromObject(secrets) }
            };

            string json = JsonConvert.SerializeObject(context);
            var encryptionKey = Convert.FromBase64String(TestEncryptionKey);
            string encryptedJson = SimpleWebTokenHelper.Encrypt(json, encryptionKey);

            File.WriteAllText(path, encryptedJson);

            return secrets;
        }

        [Fact]
        public async Task MergedSecrets_PrioritizesFunctionSecrets()
        {
            using (var directory = new TempDirectory())
            {
                string hostSecrets =
                    @"{
    'masterKey': {
        'name': 'master',
        'value': '1234',
        'encrypted': false
    },
    'functionKeys': [
        {
            'name': 'Key1',
            'value': 'HostValue1',
            'encrypted': false
        },
        {
            'name': 'Key3',
            'value': 'HostValue3',
            'encrypted': false
        }
    ]
}";
                string functionSecrets =
                    @"{
    'keys': [
        {
            'name': 'Key1',
            'value': 'FunctionValue1',
            'encrypted': false
        },
        {
            'name': 'Key2',
            'value': 'FunctionValue2',
            'encrypted': false
        }
    ]
}";
                File.WriteAllText(Path.Combine(directory.Path, ScriptConstants.HostMetadataFileName), hostSecrets);
                File.WriteAllText(Path.Combine(directory.Path, "testfunction.json"), functionSecrets);

                IDictionary<string, string> result;
                using (var secretManager = CreateSecretManager(directory.Path))
                {
                    result = await secretManager.GetFunctionSecretsAsync("testfunction", true);
                }

                Assert.Contains("Key1", result.Keys);
                Assert.Contains("Key2", result.Keys);
                Assert.Contains("Key3", result.Keys);
                Assert.Equal("FunctionValue1", result["Key1"]);
                Assert.Equal("FunctionValue2", result["Key2"]);
                Assert.Equal("HostValue3", result["Key3"]);
            }
        }

        [Fact]
        public async Task GetFunctionSecrets_UpdatesStaleSecrets()
        {
            using (var directory = new TempDirectory())
            {
                string functionName = "testfunction";
                string expectedTraceMessage = string.Format(Resources.TraceStaleFunctionSecretRefresh, functionName);
                string functionSecretsJson =
                 @"{
    'keys': [
        {
            'name': 'Key1',
            'value': 'FunctionValue1',
            'encrypted': false
        },
        {
            'name': 'Key2',
            'value': 'FunctionValue2',
            'encrypted': false
        }
    ]
}";
                File.WriteAllText(Path.Combine(directory.Path, functionName + ".json"), functionSecretsJson);

                IDictionary<string, string> functionSecrets;
                using (var secretManager = CreateSecretManager(directory.Path))
                {
                    functionSecrets = await secretManager.GetFunctionSecretsAsync(functionName);
                }

                // Read the persisted content
                var result = JsonConvert.DeserializeObject<FunctionSecrets>(File.ReadAllText(Path.Combine(directory.Path, functionName + ".json")));
                bool functionSecretsConverted = functionSecrets.Values.Zip(result.Keys, (r1, r2) => string.Equals("!" + r1, r2.Value)).All(r => r);

                Assert.Equal(2, result.Keys.Count);
                Assert.True(functionSecretsConverted, "Function secrets were not persisted");
            }
        }

        [Fact]
        public async Task GetHostSecrets_UpdatesStaleSecrets()
        {
            using (var directory = new TempDirectory())
            {
                string expectedTraceMessage = Resources.TraceStaleHostSecretRefresh;
                string hostSecretsJson =
                    @"{
    'masterKey': {
        'name': 'master',
        'value': '1234',
        'encrypted': false
    },
    'functionKeys': [
        {
            'name': 'Key1',
            'value': 'HostValue1',
            'encrypted': false
        },
        {
            'name': 'Key3',
            'value': 'HostValue3',
            'encrypted': false
        }
    ],
    'systemKeys': [
        {
            'name': 'SystemKey1',
            'value': 'SystemHostValue1',
            'encrypted': false
        },
        {
            'name': 'SystemKey2',
            'value': 'SystemHostValue2',
            'encrypted': false
        }
    ]
}";
                File.WriteAllText(Path.Combine(directory.Path, ScriptConstants.HostMetadataFileName), hostSecretsJson);

                HostSecretsInfo hostSecrets;
                using (var secretManager = CreateSecretManager(directory.Path))
                {
                    hostSecrets = await secretManager.GetHostSecretsAsync();
                }

                // Read the persisted content
                var result = JsonConvert.DeserializeObject<HostSecrets>(File.ReadAllText(Path.Combine(directory.Path, ScriptConstants.HostMetadataFileName)));
                bool functionSecretsConverted = hostSecrets.FunctionKeys.Values.Zip(result.FunctionKeys, (r1, r2) => string.Equals("!" + r1, r2.Value)).All(r => r);
                bool systemSecretsConverted = hostSecrets.SystemKeys.Values.Zip(result.SystemKeys, (r1, r2) => string.Equals("!" + r1, r2.Value)).All(r => r);

                Assert.Equal(2, result.FunctionKeys.Count);
                Assert.Equal(2, result.SystemKeys.Count);
                Assert.Equal("!" + hostSecrets.MasterKey, result.MasterKey.Value);
                Assert.True(functionSecretsConverted, "Function secrets were not persisted");
                Assert.True(systemSecretsConverted, "System secrets were not persisted");
            }
        }

        [Fact]
        public async Task GetFunctionSecretsAsync_SecretGenerationIsSerialized()
        {
            var mockValueConverterFactory = GetConverterFactoryMock(false, false);
            var metricsLogger = new TestMetricsLogger();
            var testRepository = new TestSecretsRepository(true);
            string testFunctionName = $"TestFunction";

            using (var secretManager = new SecretManager(testRepository, mockValueConverterFactory.Object, _logger, metricsLogger, _hostNameProvider, _startupContextProvider))
            {
                var tasks = new List<Task<IDictionary<string, string>>>();
                for (int i = 0; i < 10; i++)
                {
                    tasks.Add(secretManager.GetFunctionSecretsAsync(testFunctionName));
                }

                await Task.WhenAll(tasks);

                // verify all calls return the same result
                Assert.Equal(1, testRepository.FunctionSecrets.Count);
                var functionSecrets = (FunctionSecrets)testRepository.FunctionSecrets[testFunctionName];
                string defaultKeyValue = functionSecrets.Keys.Where(p => p.Name == "default").Single().Value;
                SecretGeneratorTests.ValidateSecret(defaultKeyValue, SecretGenerator.FunctionKeySeed);
                Assert.True(tasks.Select(p => p.Result).All(t => t["default"] == defaultKeyValue));
            }
        }

        [Fact]
        public async Task GetHostSecretsAsync_SecretGenerationIsSerialized()
        {
            var mockValueConverterFactory = GetConverterFactoryMock(false, false);
            var metricsLogger = new TestMetricsLogger();
            var testRepository = new TestSecretsRepository(true);

            using (var secretManager = new SecretManager(testRepository, mockValueConverterFactory.Object, _logger, metricsLogger, _hostNameProvider, _startupContextProvider))
            {
                var tasks = new List<Task<HostSecretsInfo>>();
                for (int i = 0; i < 10; i++)
                {
                    tasks.Add(secretManager.GetHostSecretsAsync());
                }

                await Task.WhenAll(tasks);

                // verify all calls return the same result
                var masterKey = tasks.First().Result.MasterKey;
                var functionKey = tasks.First().Result.FunctionKeys.First();
                Assert.True(tasks.Select(p => p.Result).All(q => q.MasterKey == masterKey));
                Assert.True(tasks.Select(p => p.Result).All(q => q.FunctionKeys.First().Value == functionKey.Value));

                // verify generated master and function keys are valid
                tasks.Select(p => p.Result).All(q => ValidateHostSecrets(q));
            }
        }

        [Fact]
        public async Task SecretsRepository_SimultaneousCreates_Throws_Conflict()
        {
            var mockValueConverterFactory = GetConverterFactoryMock(false, false);
            var metricsLogger = new TestMetricsLogger();

            // Test repository that will fail on WriteAsync() due to a conflict, but will replicate a success when LoadSecretsAsync() is called
            // indicating that there was race condition
            var testRepository = new TestSecretsRepository(true, true, true);
            string testFunctionName = "host";

            using (var secretManager = new SecretManager(testRepository, mockValueConverterFactory.Object, _logger, metricsLogger, _hostNameProvider, _startupContextProvider))
            {
                var tasks = new List<Task<HostSecretsInfo>>();
                for (int i = 0; i < 2; i++)
                {
                    tasks.Add(secretManager.GetHostSecretsAsync());
                }

                // Ensure nothing is there.
                HostSecrets secretsContent = await testRepository.ReadAsync(ScriptSecretsType.Host, testFunctionName) as HostSecrets;
                Assert.Null(secretsContent);
                await Task.WhenAll(tasks);

                // verify all calls return the same result
                var masterKey = tasks.First().Result.MasterKey;
                var functionKey = tasks.First().Result.FunctionKeys.First();
                Assert.True(tasks.Select(p => p.Result).All(q => q.MasterKey == masterKey));
                Assert.True(tasks.Select(p => p.Result).All(q => q.FunctionKeys.First().Value == functionKey.Value));

                // verify generated master and function keys are valid
                tasks.Select(p => p.Result).All(q => ValidateHostSecrets(q));
            }
        }

        [Fact]
        public async Task FunctionSecrets_SimultaneousCreates_Throws_Conflict()
        {
            var mockValueConverterFactory = GetConverterFactoryMock(false, false);
            var metricsLogger = new TestMetricsLogger();
            var testRepository = new TestSecretsRepository(true, true, true);
            string testFunctionName = $"TestFunction";

            using (var secretManager = new SecretManager(testRepository, mockValueConverterFactory.Object, _logger, metricsLogger, _hostNameProvider, _startupContextProvider))
            {
                var tasks = new List<Task<IDictionary<string, string>>>();
                for (int i = 0; i < 2; i++)
                {
                    tasks.Add(secretManager.GetFunctionSecretsAsync(testFunctionName));
                }

                await Task.WhenAll(tasks);

                // verify all calls return the same result
                Assert.Equal(1, testRepository.FunctionSecrets.Count);
                var functionSecrets = (FunctionSecrets)testRepository.FunctionSecrets[testFunctionName];
                string defaultKeyValue = functionSecrets.Keys.Where(p => p.Name == "default").Single().Value;
                SecretGeneratorTests.ValidateSecret(defaultKeyValue, SecretGenerator.FunctionKeySeed);
                Assert.True(tasks.Select(p => p.Result).All(t => t["default"] == defaultKeyValue));
            }
        }

        [Fact]
        public async Task GetHostSecrets_WhenNoHostSecretFileExists_GeneratesSecretsAndPersistsFiles()
        {
            using (var directory = new TempDirectory())
            {
                string expectedTraceMessage = Resources.TraceHostSecretGeneration;
                HostSecretsInfo hostSecrets;

                using (var secretManager = CreateSecretManager(directory.Path, simulateWriteConversion: false, setStaleValue: false))
                {
                    hostSecrets = await secretManager.GetHostSecretsAsync();
                }

                string secretsJson = File.ReadAllText(Path.Combine(directory.Path, ScriptConstants.HostMetadataFileName));
                HostSecrets persistedSecrets = ScriptSecretSerializer.DeserializeSecrets<HostSecrets>(secretsJson);

                Assert.NotNull(hostSecrets);
                Assert.NotNull(persistedSecrets);
                Assert.Equal(1, hostSecrets.FunctionKeys.Count);
                Assert.NotNull(hostSecrets.MasterKey);
                Assert.NotNull(hostSecrets.SystemKeys);
                Assert.Equal(0, hostSecrets.SystemKeys.Count);
                Assert.Equal(persistedSecrets.MasterKey.Value, hostSecrets.MasterKey);
                Assert.Equal(persistedSecrets.FunctionKeys.First().Value, hostSecrets.FunctionKeys.First().Value);
            }
        }

        [Fact]
        public async Task GetFunctionSecrets_WhenNoSecretFileExists_CreatesDefaultSecretAndPersistsFile()
        {
            using (var directory = new TempDirectory())
            {
                string functionName = "TestFunction";
                string expectedTraceMessage = string.Format(Resources.TraceFunctionSecretGeneration, functionName);

                IDictionary<string, string> functionSecrets;
                using (var secretManager = CreateSecretManager(directory.Path, simulateWriteConversion: false, setStaleValue: false))
                {
                    functionSecrets = await secretManager.GetFunctionSecretsAsync(functionName);
                }

                bool functionSecretsExists = File.Exists(Path.Combine(directory.Path, "testfunction.json"));

                Assert.NotNull(functionSecrets);
                Assert.True(functionSecretsExists);
                Assert.Equal(1, functionSecrets.Count);
                Assert.Equal(ScriptConstants.DefaultFunctionKeyName, functionSecrets.Keys.First());
            }
        }

        [Fact]
        public async Task AddOrUpdateFunctionSecrets_WithFunctionNameAndNoSecret_GeneratesFunctionSecretsAndPersistsFile()
        {
            using (var directory = new TempDirectory())
            {
                string secretName = "TestSecret";
                string functionName = "TestFunction";
                string expectedTraceMessage = string.Format(Resources.TraceAddOrUpdateFunctionSecret, "Function", secretName, functionName, "Created");

                KeyOperationResult result;
                using (var secretManager = CreateSecretManager(directory.Path, simulateWriteConversion: false))
                {
                    result = await secretManager.AddOrUpdateFunctionSecretAsync(secretName, null, functionName, ScriptSecretsType.Function);
                }

                string secretsJson = File.ReadAllText(Path.Combine(directory.Path, "testfunction.json"));
                FunctionSecrets persistedSecrets = ScriptSecretSerializer.DeserializeSecrets<FunctionSecrets>(secretsJson);

                Assert.Equal(OperationResult.Created, result.Result);
                Assert.NotNull(result.Secret);
                Assert.NotNull(persistedSecrets);
                Assert.Equal(result.Secret, persistedSecrets.Keys.First().Value);
                Assert.Equal(secretName, persistedSecrets.Keys.First().Name, StringComparer.Ordinal);
            }
        }

        [Fact]
        public async Task AddOrUpdateFunctionSecret_WhenStorageWriteError_ThrowsException()
        {
            using (var directory = new TempDirectory())
            {
                CreateTestSecrets(directory.Path);

                KeyOperationResult result;

                ISecretsRepository repository = new TestSecretsRepository(false, true, false, HttpStatusCode.InternalServerError);
                using (var secretManager = CreateSecretManager(directory.Path, simulateWriteConversion: false, secretsRepository: repository))
                {
                    try
                    {
                        result = await secretManager.AddOrUpdateFunctionSecretAsync("function-key-3", "9876", "TestFunction", ScriptSecretsType.Function);
                    }
                    catch (RequestFailedException ex)
                    {
                        Assert.Equal(ex.Status, (int)HttpStatusCode.InternalServerError);
                    }
                }
            }
        }

        [Fact]
        public async Task AddOrUpdateFunctionSecret_ClearsCache_WhenFunctionSecretAdded()
        {
            using (var directory = new TempDirectory())
            {
                CreateTestSecrets(directory.Path);

                KeyOperationResult result;
                using (var secretManager = CreateSecretManager(directory.Path, simulateWriteConversion: false))
                {
                    var keys = await secretManager.GetFunctionSecretsAsync("testfunction");
                    Assert.Equal(2, keys.Count);

                    // add a new key
                    result = await secretManager.AddOrUpdateFunctionSecretAsync("function-key-3", "9876", "TestFunction", ScriptSecretsType.Function);
                }

                string secretsJson = File.ReadAllText(Path.Combine(directory.Path, "testfunction.json"));
                var persistedSecrets = ScriptSecretSerializer.DeserializeSecrets<FunctionSecrets>(secretsJson);

                Assert.Equal(OperationResult.Created, result.Result);
                Assert.Equal(result.Secret, "9876");

                var logs = _loggerProvider.GetAllLogMessages();
                Assert.True(logs.Any(p => string.Equals(p.FormattedMessage, "Function keys change detected. Clearing cache for function 'TestFunction'.", StringComparison.OrdinalIgnoreCase)));
                Assert.Equal(1, logs.Count(p => p.FormattedMessage == "Function secret 'function-key-3' for 'TestFunction' Created."));
            }
        }

        [Fact]
        public async Task AddOrUpdateFunctionSecret_ClearsCache_WhenHostLevelFunctionSecretAdded()
        {
            using (var directory = new TempDirectory())
            {
                CreateTestSecrets(directory.Path);

                KeyOperationResult result;
                using (var secretManager = CreateSecretManager(directory.Path, simulateWriteConversion: false))
                {
                    var hostKeys = await secretManager.GetHostSecretsAsync();
                    Assert.Equal(2, hostKeys.FunctionKeys.Count);

                    // add a new key
                    result = await secretManager.AddOrUpdateFunctionSecretAsync("function-host-3", "9876", HostKeyScopes.FunctionKeys, ScriptSecretsType.Host);
                }

                string secretsJson = File.ReadAllText(Path.Combine(directory.Path, "host.json"));
                var persistedSecrets = ScriptSecretSerializer.DeserializeSecrets<HostSecrets>(secretsJson);

                Assert.Equal(OperationResult.Created, result.Result);
                Assert.Equal(result.Secret, "9876");

                var logs = _loggerProvider.GetAllLogMessages();
                Assert.Equal(1, logs.Count(p => p.FormattedMessage == "Host keys change detected. Clearing cache."));
                Assert.Equal(1, logs.Count(p => p.FormattedMessage == "Host secret 'function-host-3' for 'functionkeys' Created."));
            }
        }

        [Fact]
        public async Task AddOrUpdateFunctionSecret_ClearsCache_WhenHostSystemSecretAdded()
        {
            using (var directory = new TempDirectory())
            {
                CreateTestSecrets(directory.Path);

                KeyOperationResult result;
                using (var secretManager = CreateSecretManager(directory.Path, simulateWriteConversion: false))
                {
                    var hostKeys = await secretManager.GetHostSecretsAsync();
                    Assert.Equal(2, hostKeys.SystemKeys.Count);

                    // add a new key
                    result = await secretManager.AddOrUpdateFunctionSecretAsync("host-system-3", "123", HostKeyScopes.SystemKeys, ScriptSecretsType.Host);
                }

                string secretsJson = File.ReadAllText(Path.Combine(directory.Path, "host.json"));
                var persistedSecrets = ScriptSecretSerializer.DeserializeSecrets<HostSecrets>(secretsJson);

                Assert.Equal(OperationResult.Created, result.Result);
                Assert.Equal(result.Secret, "123");

                var logs = _loggerProvider.GetAllLogMessages();
                Assert.Equal(1, logs.Count(p => p.FormattedMessage == "Host keys change detected. Clearing cache."));
                Assert.Equal(1, logs.Count(p => p.FormattedMessage == "Host secret 'host-system-3' for 'systemkeys' Created."));
            }
        }

        [Fact]
        public async Task AddOrUpdateFunctionSecrets_WithFunctionNameAndNoSecret_EncryptsSecretAndPersistsFile()
        {
            using (var directory = new TempDirectory())
            {
                string secretName = "TestSecret";
                string functionName = "TestFunction";
                string expectedTraceMessage = string.Format(Resources.TraceAddOrUpdateFunctionSecret, "Function", secretName, functionName, "Created");

                KeyOperationResult result;
                using (var secretManager = CreateSecretManager(directory.Path))
                {
                    result = await secretManager.AddOrUpdateFunctionSecretAsync(secretName, null, functionName, ScriptSecretsType.Function);
                }

                string secretsJson = File.ReadAllText(Path.Combine(directory.Path, "testfunction.json"));
                FunctionSecrets persistedSecrets = ScriptSecretSerializer.DeserializeSecrets<FunctionSecrets>(secretsJson);

                Assert.Equal(OperationResult.Created, result.Result);
                Assert.NotNull(result.Secret);
                Assert.NotNull(persistedSecrets);
                Assert.Equal("!" + result.Secret, persistedSecrets.Keys.First().Value);
                Assert.Equal(secretName, persistedSecrets.Keys.First().Name, StringComparer.Ordinal);
                Assert.True(persistedSecrets.Keys.First().IsEncrypted);
            }
        }

        [Fact]
        public async Task AddOrUpdateFunctionSecrets_WithFunctionNameAndProvidedSecret_UsesSecretAndPersistsFile()
        {
            using (var directory = new TempDirectory())
            {
                string secretName = "TestSecret";
                string functionName = "TestFunction";
                string expectedTraceMessage = string.Format(Resources.TraceAddOrUpdateFunctionSecret, "Function", secretName, functionName, "Created");

                KeyOperationResult result;
                using (var secretManager = CreateSecretManager(directory.Path, simulateWriteConversion: false))
                {
                    result = await secretManager.AddOrUpdateFunctionSecretAsync(secretName, "TestSecretValue", functionName, ScriptSecretsType.Function);
                }

                string secretsJson = File.ReadAllText(Path.Combine(directory.Path, "testfunction.json"));
                FunctionSecrets persistedSecrets = ScriptSecretSerializer.DeserializeSecrets<FunctionSecrets>(secretsJson);

                Assert.Equal(OperationResult.Created, result.Result);
                Assert.Equal("TestSecretValue", result.Secret, StringComparer.Ordinal);
                Assert.NotNull(persistedSecrets);
                Assert.Equal(result.Secret, persistedSecrets.Keys.First().Value);
                Assert.Equal(secretName, persistedSecrets.Keys.First().Name, StringComparer.Ordinal);
            }
        }

        [Fact]
        public async Task AddOrUpdateFunctionSecrets_WithNoFunctionNameAndProvidedSecret_UsesSecretAndPersistsHostFile()
        {
            await AddOrUpdateFunctionSecrets_WithScope_UsesSecretandPersistsHostFile(HostKeyScopes.FunctionKeys, h => h.FunctionKeys);
        }

        [Fact]
        public async Task AddOrUpdateFunctionSecrets_WithSystemSecretScopeAndProvidedSecret_UsesSecretAndPersistsHostFile()
        {
            await AddOrUpdateFunctionSecrets_WithScope_UsesSecretandPersistsHostFile(HostKeyScopes.SystemKeys, h => h.SystemKeys);
        }

        [Fact]
        public async Task AddOrUpdateFunctionSecrets_WithExistingHostFileAndSystemSecretScope_PersistsHostFileWithSecret()
        {
            using (var directory = new TempDirectory())
            {
                var hostSecret = new HostSecrets();
                hostSecret.MasterKey = new Key("_master", "master");
                hostSecret.FunctionKeys = new List<Key> { };

                var hostJson = JsonConvert.SerializeObject(hostSecret);
                await FileUtility.WriteAsync(Path.Combine(directory.Path, ScriptConstants.HostMetadataFileName), hostJson);
                await AddOrUpdateFunctionSecrets_WithScope_UsesSecretandPersistsHostFile(HostKeyScopes.SystemKeys, h => h.SystemKeys, directory);
            }
        }

        public async Task AddOrUpdateFunctionSecrets_WithScope_UsesSecretandPersistsHostFile(string scope, Func<HostSecrets, IList<Key>> keySelector)
        {
            using (var directory = new TempDirectory())
            {
                await AddOrUpdateFunctionSecrets_WithScope_UsesSecretandPersistsHostFile(scope, keySelector, directory);
            }
        }

        public async Task AddOrUpdateFunctionSecrets_WithScope_UsesSecretandPersistsHostFile(string scope, Func<HostSecrets, IList<Key>> keySelector, TempDirectory directory)
        {
            string secretName = "TestSecret";
            string expectedTraceMessage = string.Format(Resources.TraceAddOrUpdateFunctionSecret, "Host", secretName, scope, "Created");

            KeyOperationResult result;
            using (var secretManager = CreateSecretManager(directory.Path, simulateWriteConversion: false))
            {
                result = await secretManager.AddOrUpdateFunctionSecretAsync(secretName, "TestSecretValue", scope, ScriptSecretsType.Host);
            }

            string secretsJson = File.ReadAllText(Path.Combine(directory.Path, ScriptConstants.HostMetadataFileName));
            HostSecrets persistedSecrets = ScriptSecretSerializer.DeserializeSecrets<HostSecrets>(secretsJson);
            Key newSecret = keySelector(persistedSecrets).FirstOrDefault(k => string.Equals(k.Name, secretName, StringComparison.Ordinal));

            Assert.Equal(OperationResult.Created, result.Result);
            Assert.Equal("TestSecretValue", result.Secret, StringComparer.Ordinal);
            Assert.NotNull(persistedSecrets);
            Assert.NotNull(newSecret);
            Assert.Equal(result.Secret, newSecret.Value);
            Assert.Equal(secretName, newSecret.Name, StringComparer.Ordinal);
            Assert.NotNull(persistedSecrets.MasterKey);
        }

        [Fact]
        public async Task SetMasterKey_WithProvidedKey_UsesProvidedKeyAndPersistsFile()
        {
            string testSecret = "abcde0123456789abcde0123456789abcde0123456789";
            using (var directory = new TempDirectory())
            {
                KeyOperationResult result;
                using (var secretManager = CreateSecretManager(directory.Path, simulateWriteConversion: false))
                {
                    result = await secretManager.SetMasterKeyAsync(testSecret);
                }

                bool functionSecretsExists = File.Exists(Path.Combine(directory.Path, "testfunction.json"));

                string secretsJson = File.ReadAllText(Path.Combine(directory.Path, ScriptConstants.HostMetadataFileName));
                HostSecrets persistedSecrets = ScriptSecretSerializer.DeserializeSecrets<HostSecrets>(secretsJson);

                Assert.NotNull(persistedSecrets);
                Assert.NotNull(persistedSecrets.MasterKey);
                Assert.Equal(OperationResult.Updated, result.Result);
                Assert.Equal(testSecret, result.Secret);
            }
        }

        [Fact]
        public async Task SetMasterKey_WithoutProvidedKey_GeneratesKeyAndPersistsFile()
        {
            using (var directory = new TempDirectory())
            {
                KeyOperationResult result;
                using (var secretManager = CreateSecretManager(directory.Path, simulateWriteConversion: false))
                {
                    result = await secretManager.SetMasterKeyAsync();
                }

                bool functionSecretsExists = File.Exists(Path.Combine(directory.Path, "testfunction.json"));

                string secretsJson = File.ReadAllText(Path.Combine(directory.Path, ScriptConstants.HostMetadataFileName));
                HostSecrets persistedSecrets = ScriptSecretSerializer.DeserializeSecrets<HostSecrets>(secretsJson);

                Assert.NotNull(persistedSecrets);
                Assert.NotNull(persistedSecrets.MasterKey);
                Assert.Equal(OperationResult.Created, result.Result);
                Assert.Equal(result.Secret, persistedSecrets.MasterKey.Value);
            }
        }

        [Fact]
        public async Task Constructor_WithCreateHostSecretsIfMissingSet_CreatesHostSecret()
        {
            var secretsPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            var hostSecretPath = Path.Combine(secretsPath, ScriptConstants.HostMetadataFileName);
            try
            {
                string expectedTraceMessage = Resources.TraceHostSecretGeneration;
                bool preExistingFile = File.Exists(hostSecretPath);
                var secretManager = CreateSecretManager(secretsPath, createHostSecretsIfMissing: true, simulateWriteConversion: false, setStaleValue: false);
                bool fileCreated = File.Exists(hostSecretPath);

                Assert.False(preExistingFile);
                Assert.True(fileCreated);

                await Task.Delay(TestSentinelWatcherInitializationDelayMS);

                var logs = _loggerProvider.GetAllLogMessages().ToArray();
                Assert.Single(logs.Where(t => t.Level == LogLevel.Debug && t.FormattedMessage.StartsWith("Sentinel watcher initialized")));
            }
            finally
            {
                Directory.Delete(secretsPath, true);
            }
        }

        [Fact]
        public async Task GetHostSecrets_WhenNonDecryptedHostSecrets_SavesAndRefreshes()
        {
            using (var directory = new TempDirectory())
            {
                string hostSecretsJson =
                    @"{
    'masterKey': {
        'name': 'master',
        'value': 'cryptoError',
        'encrypted': true
    },
    'functionKeys': [],
    'systemKeys': []
}";
                File.WriteAllText(Path.Combine(directory.Path, ScriptConstants.HostMetadataFileName), hostSecretsJson);
                HostSecretsInfo hostSecrets;
                using (var secretManager = CreateSecretManager(directory.Path, simulateWriteConversion: true, setStaleValue: false))
                {
                    hostSecrets = await secretManager.GetHostSecretsAsync();
                }

                Assert.NotNull(hostSecrets);
                Assert.NotEqual(hostSecrets.MasterKey, "cryptoError");
                var result = JsonConvert.DeserializeObject<HostSecrets>(File.ReadAllText(Path.Combine(directory.Path, ScriptConstants.HostMetadataFileName)));
                Assert.Equal(result.MasterKey.Value, "!" + hostSecrets.MasterKey);
                Assert.Equal(1, Directory.GetFiles(directory.Path, $"host.{ScriptConstants.Snapshot}*").Length);
            }
        }

        [Fact]
        public async Task GetFunctiontSecrets_WhenNonDecryptedSecrets_SavesAndRefreshes()
        {
            string key = TestHelpers.GenerateKeyHexString();
            using (new TestScopedEnvironmentVariable(EnvironmentSettingNames.WebSiteAuthEncryptionKey, key))
            {
                using (var directory = new TempDirectory())
                {
                    string functionName = "testfunction";
                    string functionSecretsJson =
                         @"{
    'keys': [
        {
            'name': 'Key1',
            'value': 'cryptoError',
            'encrypted': true
        },
        {
            'name': 'Key2',
            'value': '1234',
            'encrypted': false
        }
    ]
}";
                    File.WriteAllText(Path.Combine(directory.Path, functionName + ".json"), functionSecretsJson);
                    IDictionary<string, string> functionSecrets;
                    using (var secretManager = CreateSecretManager(directory.Path, simulateWriteConversion: true, setStaleValue: false))
                    {
                        functionSecrets = await secretManager.GetFunctionSecretsAsync(functionName);
                    }

                    Assert.NotNull(functionSecrets);
                    Assert.NotEqual(functionSecrets["Key1"], "cryptoError");
                    var result = JsonConvert.DeserializeObject<FunctionSecrets>(File.ReadAllText(Path.Combine(directory.Path, functionName + ".json")));
                    Assert.Equal(result.GetFunctionKey("Key1", functionName).Value, "!" + functionSecrets["Key1"]);
                    Assert.Equal(1, Directory.GetFiles(directory.Path, $"{functionName}.{ScriptConstants.Snapshot}*").Length);

                    result = JsonConvert.DeserializeObject<FunctionSecrets>(File.ReadAllText(Path.Combine(directory.Path, functionName + ".json")));
                    string snapShotFileName = Directory.GetFiles(directory.Path, $"{functionName}.{ScriptConstants.Snapshot}*")[0];
                    result = JsonConvert.DeserializeObject<FunctionSecrets>(File.ReadAllText(Path.Combine(directory.Path, snapShotFileName)));
                    Assert.NotEqual(result.DecryptionKeyId, key);
                }
            }
        }

        [Fact]
        public async Task GetHostSecrets_WhenTooManyBackups_ThrowsException()
        {
            using (var directory = new TempDirectory())
            {
                string functionName = "testfunction";
                string expectedTraceMessage = string.Format(Resources.ErrorTooManySecretBackups, ScriptConstants.MaximumSecretBackupCount, functionName,
                    string.Format(Resources.ErrorSameSecrets, "test0,test1"));
                string functionSecretsJson =
                     @"{
    'keys': [
        {
            'name': 'Key1',
            'value': 'cryptoError',
            'encrypted': true
        },
        {
            'name': 'Key2',
            'value': 'FunctionValue2',
            'encrypted': false
        }
    ]
}";
                IDictionary<string, string> functionSecrets;
                using (var secretManager = CreateSecretManager(directory.Path))
                {
                    InvalidOperationException ioe = null;
                    try
                    {
                        for (int i = 0; i < ScriptConstants.MaximumSecretBackupCount + 20; i++)
                        {
                            File.WriteAllText(Path.Combine(directory.Path, functionName + ".json"), functionSecretsJson);

                            // If we haven't hit the exception yet, pause to ensure the file contents are being flushed.
                            if (i >= ScriptConstants.MaximumSecretBackupCount)
                            {
                                await Task.Delay(500);
                            }

                            // reset hostname provider and set a new hostname to force another backup
                            _hostNameProvider.Reset();
                            string hostName = "test" + (i % 2).ToString();
                            _testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteHostName, hostName);

                            functionSecrets = await secretManager.GetFunctionSecretsAsync(functionName);
                        }
                    }
                    catch (InvalidOperationException ex)
                    {
                        ioe = ex;
                    }
                }

                int backupCount = Directory.GetFiles(directory.Path, $"{functionName}.{ScriptConstants.Snapshot}*").Length;
                Assert.True(backupCount >= ScriptConstants.MaximumSecretBackupCount);

                Assert.True(Directory.GetFiles(directory.Path, $"{functionName}.{ScriptConstants.Snapshot}*").Length >= ScriptConstants.MaximumSecretBackupCount);

                // There will be two log entries of the expectedTraceMessage:
                // 1) Debug level log entry
                Assert.True(_loggerProvider.GetAllLogMessages().Any(
                    t => t.Level == LogLevel.Debug && t.FormattedMessage.IndexOf(expectedTraceMessage, StringComparison.OrdinalIgnoreCase) > -1),
                    "Expected Trace message not found");

                // 2) Diagnostic event with LogLevel.Error, indicating that the app will not be able to start; this will be shown to the user in the Portal.
                var expectedDiagnosticEvent = _loggerProvider.GetAllLogMessages().LastOrDefault();
                Assert.True(expectedDiagnosticEvent != null &&
                            expectedDiagnosticEvent.Level == LogLevel.Error &&
                            expectedDiagnosticEvent.FormattedMessage.IndexOf(expectedTraceMessage, StringComparison.OrdinalIgnoreCase) > -1,
                            "Expected Diagnostic event log entry not found");

                // Validate diagnostic event MS_HelpLink and MS_ErrorCode
                var myDictionary = (Dictionary<string, object>)expectedDiagnosticEvent.State;
                Assert.Equal(myDictionary.GetValueOrDefault("MS_HelpLink").ToString(), DiagnosticEventConstants.MaximumSecretBackupCountHelpLink);
                Assert.Equal(myDictionary.GetValueOrDefault("MS_ErrorCode").ToString(), DiagnosticEventConstants.MaximumSecretBackupCountErrorCode);
            }
        }

        [Fact]
        public async Task GetHostSecretsAsync_WaitsForNewSecrets()
        {
            using (var directory = new TempDirectory())
            {
                string hostSecretsJson = @"{
    'masterKey': {
        'name': 'master',
        'value': '1234',
        'encrypted': false
    },
    'functionKeys': [],
    'systemKeys': []
}";
                string filePath = Path.Combine(directory.Path, ScriptConstants.HostMetadataFileName);
                File.WriteAllText(filePath, hostSecretsJson);

                HostSecretsInfo hostSecrets = null;
                using (var secretManager = CreateSecretManager(directory.Path))
                {
                    await Task.WhenAll(
                        Task.Run(async () =>
                        {
                            // Lock the file
                            using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Write, FileShare.None))
                            {
                                await Task.Delay(500);
                            }
                        }),
                        Task.Run(async () =>
                        {
                            await Task.Delay(100);
                            hostSecrets = await secretManager.GetHostSecretsAsync();
                        }));

                    Assert.Equal(hostSecrets.MasterKey, "1234");
                }

                using (var secretManager = CreateSecretManager(directory.Path))
                {
                    await Assert.ThrowsAsync<IOException>(async () =>
                    {
                        await Task.WhenAll(
                            Task.Run(async () =>
                            {
                                // Lock the file
                                using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Write, FileShare.None))
                                {
                                    await Task.Delay(3000);
                                }
                            }),
                            Task.Run(async () =>
                            {
                                await Task.Delay(100);
                                hostSecrets = await secretManager.GetHostSecretsAsync();
                            }));
                    });
                }
            }
        }

        [Fact]
        public async Task GetFunctionSecretsAsync_WaitsForNewSecrets()
        {
            using (var directory = new TempDirectory())
            {
                string functionName = "testfunction";
                string functionSecretsJson =
                 @"{
    'keys': [
        {
            'name': 'Key1',
            'value': 'FunctionValue1',
            'encrypted': false
        },
        {
            'name': 'Key2',
            'value': 'FunctionValue2',
            'encrypted': false
        }
    ]
}";
                string filePath = Path.Combine(directory.Path, functionName + ".json");
                File.WriteAllText(filePath, functionSecretsJson);

                IDictionary<string, string> functionSecrets = null;
                using (var secretManager = CreateSecretManager(directory.Path))
                {
                    await Task.WhenAll(
                        Task.Run(async () =>
                        {
                            // Lock the file
                            using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Write, FileShare.None))
                            {
                                await Task.Delay(500);
                            }
                        }),
                        Task.Run(async () =>
                        {
                            await Task.Delay(100);
                            functionSecrets = await secretManager.GetFunctionSecretsAsync(functionName);
                        }));

                    Assert.Equal(functionSecrets["Key1"], "FunctionValue1");
                }

                using (var secretManager = CreateSecretManager(directory.Path))
                {
                    await Assert.ThrowsAsync<IOException>(async () =>
                    {
                        await Task.WhenAll(
                            Task.Run(async () =>
                            {
                                // Lock the file
                                using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Write, FileShare.None))
                                {
                                    await Task.Delay(3000);
                                }
                            }),
                            Task.Run(async () =>
                            {
                                await Task.Delay(100);
                                functionSecrets = await secretManager.GetFunctionSecretsAsync(functionName);
                            }));
                    });
                }
            }
        }

        [Fact]
        public async Task GetHostSecrets_AddMetrics()
        {
            using (var directory = new TempDirectory())
            {
                string expectedTraceMessage = Resources.TraceNonDecryptedHostSecretRefresh;
                string hostSecretsJson =
                    @"{
    'masterKey': {
        'name': 'master',
        'value': 'cryptoError',
        'encrypted': true
    },
    'functionKeys': [],
    'systemKeys': []
}";
                File.WriteAllText(Path.Combine(directory.Path, ScriptConstants.HostMetadataFileName), hostSecretsJson);
                HostSecretsInfo hostSecrets;
                TestMetricsLogger metricsLogger = new TestMetricsLogger();

                using (var secretManager = CreateSecretManager(directory.Path, metricsLogger: metricsLogger, simulateWriteConversion: true, setStaleValue: false))
                {
                    hostSecrets = await secretManager.GetHostSecretsAsync();
                }

                string eventName = string.Format(MetricEventNames.SecretManagerGetHostSecrets, typeof(FileSystemSecretsRepository).Name.ToLower());
                metricsLogger.EventsBegan.Single(e => string.Equals(e, eventName));
                metricsLogger.EventsEnded.Single(e => string.Equals(e.ToString(), eventName));
            }
        }

        [Fact]
        public async Task GetFunctiontSecrets_AddsMetrics()
        {
            using (var directory = new TempDirectory())
            {
                string functionName = "testfunction";
                string functionSecretsJson =
                     @"{
    'keys': [
        {
            'name': 'Key1',
            'value': 'cryptoError',
            'encrypted': true
        },
        {
            'name': 'Key2',
            'value': '1234',
            'encrypted': false
        }
    ]
}";
                File.WriteAllText(Path.Combine(directory.Path, functionName + ".json"), functionSecretsJson);
                IDictionary<string, string> functionSecrets;
                TestMetricsLogger metricsLogger = new TestMetricsLogger();

                using (var secretManager = CreateSecretManager(directory.Path, metricsLogger: metricsLogger, simulateWriteConversion: true, setStaleValue: false))
                {
                    functionSecrets = await secretManager.GetFunctionSecretsAsync(functionName);
                }

                string eventName = string.Format(MetricEventNames.SecretManagerGetFunctionSecrets, typeof(FileSystemSecretsRepository).Name.ToLower());
                metricsLogger.EventsBegan.Single(e => e.StartsWith(eventName));
                metricsLogger.EventsBegan.Single(e => e.Contains("testfunction"));
                metricsLogger.EventsEnded.Single(e => e.ToString().StartsWith(eventName));
                metricsLogger.EventsEnded.Single(e => e.ToString().Contains("testfunction"));
            }
        }

        private Mock<IKeyValueConverterFactory> GetConverterFactoryMock(bool simulateWriteConversion = true, bool setStaleValue = true)
        {
            var mockValueReader = new Mock<IKeyValueReader>();
            mockValueReader.Setup(r => r.ReadValue(It.IsAny<Key>()))
                .Returns<Key>(k =>
                {
                    if (k.Value.StartsWith("cryptoError"))
                    {
                        throw new CryptographicException();
                    }
                    return new Key(k.Name, k.Value) { IsStale = setStaleValue ? true : k.IsStale };
                });

            var mockValueWriter = new Mock<IKeyValueWriter>();
            mockValueWriter.Setup(r => r.WriteValue(It.IsAny<Key>()))
                .Returns<Key>(k =>
                {
                    return new Key(k.Name, simulateWriteConversion ? "!" + k.Value : k.Value) { IsEncrypted = simulateWriteConversion };
                });

            var mockValueConverterFactory = new Mock<IKeyValueConverterFactory>();
            mockValueConverterFactory.Setup(f => f.GetValueReader(It.IsAny<Key>()))
                .Returns(mockValueReader.Object);
            mockValueConverterFactory.Setup(f => f.GetValueWriter(It.IsAny<Key>()))
                .Returns(mockValueWriter.Object);

            return mockValueConverterFactory;
        }

        private SecretManager CreateSecretManager(string secretsPath, ILogger logger = null, IMetricsLogger metricsLogger = null, IKeyValueConverterFactory keyConverterFactory = null, bool createHostSecretsIfMissing = false, bool simulateWriteConversion = true, bool setStaleValue = true, ISecretsRepository secretsRepository = null)
        {
            logger = logger ?? _logger;
            metricsLogger = metricsLogger ?? new TestMetricsLogger();

            if (keyConverterFactory == null)
            {
                Mock<IKeyValueConverterFactory> mockValueConverterFactory = GetConverterFactoryMock(simulateWriteConversion, setStaleValue);
                keyConverterFactory = mockValueConverterFactory.Object;
            }

            ISecretsRepository repository = secretsRepository ?? new FileSystemSecretsRepository(secretsPath, logger, _testEnvironment);
            var secretManager = new SecretManager(repository, keyConverterFactory, logger, metricsLogger, _hostNameProvider, _startupContextProvider);

            if (createHostSecretsIfMissing)
            {
                secretManager.GetHostSecretsAsync().GetAwaiter().GetResult();
            }

            return secretManager;
        }

        private void CreateTestSecrets(string path)
        {
            string hostSecrets =
                    @"{
    'masterKey': {
        'name': 'master',
        'value': '1234',
        'encrypted': false
    },
    'functionKeys': [
        {
            'name': 'function-host-1',
            'value': '456',
            'encrypted': false
        },
        {
            'name': 'function-host-2',
            'value': '789',
            'encrypted': false
        }
    ],
    'systemKeys': [
        {
            'name': 'host-system-1',
            'value': '654',
            'encrypted': false
        },
        {
            'name': 'host-system-2',
            'value': '321',
            'encrypted': false
        }
    ]
}";
            string functionSecrets =
                @"{
    'keys': [
        {
            'name': 'function-key-1',
            'value': '1234',
            'encrypted': false
        },
        {
            'name': 'function-key-2',
            'value': '5678',
            'encrypted': false
        }
    ]
}";
            File.WriteAllText(Path.Combine(path, ScriptConstants.HostMetadataFileName), hostSecrets);
            File.WriteAllText(Path.Combine(path, "testfunction.json"), functionSecrets);
        }

        private class TestSecretsRepository : ISecretsRepository
        {
            private int _writeCount = 0;
            private Random _rand = new Random();
            private bool _enforceSerialWrites = false;
            private bool _forceWriteErrors = false;
            private bool _shouldSuceedAfterFailing = false;
            private HttpStatusCode _httpstaus;

            public TestSecretsRepository(bool enforceSerialWrites)
            {
                _enforceSerialWrites = enforceSerialWrites;
            }

            public TestSecretsRepository(bool enforceSerialWrites, bool forceWriteErrors, bool shouldSucceedAfterFailing = false, HttpStatusCode httpstaus = HttpStatusCode.InternalServerError)
                : this(enforceSerialWrites)
            {
                _forceWriteErrors = forceWriteErrors;
                _shouldSuceedAfterFailing = shouldSucceedAfterFailing;
                _httpstaus = httpstaus;
            }

            public event EventHandler<SecretsChangedEventArgs> SecretsChanged;

            public ConcurrentDictionary<string, ScriptSecrets> FunctionSecrets { get; } = new ConcurrentDictionary<string, ScriptSecrets>(StringComparer.OrdinalIgnoreCase);

            public ScriptSecrets HostSecrets { get; private set; }

            public bool IsEncryptionSupported => throw new NotImplementedException();

            public string Name => nameof(TestSecretsRepository);

            public Task<string[]> GetSecretSnapshots(ScriptSecretsType type, string functionName)
            {
                return Task.FromResult(new string[0]);
            }

            public Task PurgeOldSecretsAsync(IList<string> currentFunctions, ILogger logger)
            {
                return Task.CompletedTask;
            }

            public Task<ScriptSecrets> ReadAsync(ScriptSecretsType type, string functionName)
            {
                ScriptSecrets secrets = null;

                if (type == ScriptSecretsType.Function)
                {
                    FunctionSecrets.TryGetValue(functionName, out secrets);
                }
                else
                {
                    secrets = HostSecrets;
                }

                return Task.FromResult(secrets);
            }

            public async Task WriteAsync(ScriptSecretsType type, string functionName, ScriptSecrets secrets)
            {
                if (_forceWriteErrors)
                {
                    // Replicate making the first write fail, but succeed on the second attempt
                    if (_shouldSuceedAfterFailing)
                    {
                        await WriteAsyncHelper(type, functionName, secrets);
                    }
                    throw new RequestFailedException((int)_httpstaus, "Error");
                }

                if (_enforceSerialWrites && _writeCount > 1)
                {
                    throw new Exception("Concurrent writes detected!");
                }

                await WriteAsyncHelper(type, functionName, secrets);
            }

            private async Task WriteAsyncHelper(ScriptSecretsType type, string functionName, ScriptSecrets secrets)
            {
                Interlocked.Increment(ref _writeCount);

                await Task.Delay(_rand.Next(100, 300));

                if (type == ScriptSecretsType.Function)
                {
                    FunctionSecrets[functionName] = secrets;
                }
                else
                {
                    HostSecrets = secrets;
                }

                Interlocked.Decrement(ref _writeCount);

                if (SecretsChanged != null)
                {
                    SecretsChanged(this, new SecretsChangedEventArgs { SecretsType = type, Name = functionName });
                }
            }

            public Task WriteSnapshotAsync(ScriptSecretsType type, string functionName, ScriptSecrets secrets)
            {
                return Task.CompletedTask;
            }
        }
    }
}
