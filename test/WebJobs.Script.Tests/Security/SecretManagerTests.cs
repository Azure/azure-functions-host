// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Properties;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Newtonsoft.Json;
using WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Security
{
    public class SecretManagerTests
    {
        private ScriptSettingsManager _settingsManager = new ScriptSettingsManager();

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
                ISecretsRepository repository = new FileSystemSecretsRepository(directory.Path);
                using (var secretManager = new SecretManager(_settingsManager, repository, NullLogger.Instance))
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

                Mock<IKeyValueConverterFactory> mockValueConverterFactory = GetConverterFactoryMock();

                IDictionary<string, string> functionSecrets;
                ISecretsRepository repository = new FileSystemSecretsRepository(directory.Path);
                using (var secretManager = new SecretManager(repository, mockValueConverterFactory.Object, NullLogger.Instance))
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

                Mock<IKeyValueConverterFactory> mockValueConverterFactory = GetConverterFactoryMock();

                HostSecretsInfo hostSecrets;
                ISecretsRepository repository = new FileSystemSecretsRepository(directory.Path);
                using (var secretManager = new SecretManager(repository, mockValueConverterFactory.Object, NullLogger.Instance))
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
        public async Task GetHostSecrets_WhenNoHostSecretFileExists_GeneratesSecretsAndPersistsFiles()
        {
            using (var directory = new TempDirectory())
            {
                string expectedTraceMessage = Resources.TraceHostSecretGeneration;
                Mock<IKeyValueConverterFactory> mockValueConverterFactory = GetConverterFactoryMock(false, false);

                HostSecretsInfo hostSecrets;
                ISecretsRepository repository = new FileSystemSecretsRepository(directory.Path);
                using (var secretManager = new SecretManager(repository, mockValueConverterFactory.Object, NullLogger.Instance))
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

                Mock<IKeyValueConverterFactory> mockValueConverterFactory = GetConverterFactoryMock(false, false);

                IDictionary<string, string> functionSecrets;
                ISecretsRepository repository = new FileSystemSecretsRepository(directory.Path);
                using (var secretManager = new SecretManager(repository, mockValueConverterFactory.Object, NullLogger.Instance))
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

                Mock<IKeyValueConverterFactory> mockValueConverterFactory = GetConverterFactoryMock(false);

                KeyOperationResult result;
                ISecretsRepository repository = new FileSystemSecretsRepository(directory.Path);
                using (var secretManager = new SecretManager(repository, mockValueConverterFactory.Object, NullLogger.Instance))
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
        public async Task AddOrUpdateFunctionSecrets_WithFunctionNameAndNoSecret_EncryptsSecretAndPersistsFile()
        {
            using (var directory = new TempDirectory())
            {
                string secretName = "TestSecret";
                string functionName = "TestFunction";
                string expectedTraceMessage = string.Format(Resources.TraceAddOrUpdateFunctionSecret, "Function", secretName, functionName, "Created");

                Mock<IKeyValueConverterFactory> mockValueConverterFactory = GetConverterFactoryMock(true);

                KeyOperationResult result;
                ISecretsRepository repository = new FileSystemSecretsRepository(directory.Path);
                using (var secretManager = new SecretManager(repository, mockValueConverterFactory.Object, NullLogger.Instance))
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

                Mock<IKeyValueConverterFactory> mockValueConverterFactory = GetConverterFactoryMock(false);

                KeyOperationResult result;
                ISecretsRepository repository = new FileSystemSecretsRepository(directory.Path);
                using (var secretManager = new SecretManager(repository, mockValueConverterFactory.Object, NullLogger.Instance))
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

            Mock<IKeyValueConverterFactory> mockValueConverterFactory = GetConverterFactoryMock(false);

            KeyOperationResult result;
            ISecretsRepository repository = new FileSystemSecretsRepository(directory.Path);
            using (var secretManager = new SecretManager(repository, mockValueConverterFactory.Object, NullLogger.Instance))
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
                Mock<IKeyValueConverterFactory> mockValueConverterFactory = GetConverterFactoryMock(false);

                KeyOperationResult result;
                ISecretsRepository repository = new FileSystemSecretsRepository(directory.Path);
                using (var secretManager = new SecretManager(repository, mockValueConverterFactory.Object, NullLogger.Instance))
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
                Mock<IKeyValueConverterFactory> mockValueConverterFactory = GetConverterFactoryMock(false);

                KeyOperationResult result;
                ISecretsRepository repository = new FileSystemSecretsRepository(directory.Path);
                using (var secretManager = new SecretManager(repository, mockValueConverterFactory.Object, NullLogger.Instance))
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
        public void Constructor_WithCreateHostSecretsIfMissingSet_CreatesHostSecret()
        {
            var secretsPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            var hostSecretPath = Path.Combine(secretsPath, ScriptConstants.HostMetadataFileName);
            try
            {
                string expectedTraceMessage = Resources.TraceHostSecretGeneration;
                bool preExistingFile = File.Exists(hostSecretPath);

                Mock<IKeyValueConverterFactory> mockValueConverterFactory = GetConverterFactoryMock(false, false);

                ISecretsRepository repository = new FileSystemSecretsRepository(secretsPath);
                var secretManager = new SecretManager(repository, mockValueConverterFactory.Object, NullLogger.Instance, true);
                bool fileCreated = File.Exists(hostSecretPath);

                Assert.False(preExistingFile);
                Assert.True(fileCreated);
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
                Mock<IKeyValueConverterFactory> mockValueConverterFactory = GetConverterFactoryMock(true, false);
                HostSecretsInfo hostSecrets;
                ISecretsRepository repository = new FileSystemSecretsRepository(directory.Path);

                using (var secretManager = new SecretManager(repository, mockValueConverterFactory.Object, null))
                {
                    hostSecrets = await secretManager.GetHostSecretsAsync();
                }

                Assert.NotNull(hostSecrets);
                Assert.Equal(hostSecrets.MasterKey, "cryptoError");
                var result = JsonConvert.DeserializeObject<HostSecrets>(File.ReadAllText(Path.Combine(directory.Path, ScriptConstants.HostMetadataFileName)));
                Assert.Equal(result.MasterKey.Value, "!cryptoError");
                Assert.Equal(1, Directory.GetFiles(directory.Path, $"host.{ScriptConstants.Snapshot}*").Length);
            }
        }

        [Fact]
        public async Task GetFunctiontSecrets_WhenNonDecryptedSecrets_SavesAndRefreshes()
        {
            using (var directory = new TempDirectory())
            {
                string functionName = "testfunction";
                string expectedTraceMessage = string.Format(Resources.TraceNonDecryptedFunctionSecretRefresh, functionName);
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
                Mock<IKeyValueConverterFactory> mockValueConverterFactory = GetConverterFactoryMock(true, false);
                IDictionary<string, string> functionSecrets;
                ISecretsRepository repository = new FileSystemSecretsRepository(directory.Path);

                using (var secretManager = new SecretManager(repository, mockValueConverterFactory.Object, null))
                {
                    functionSecrets = await secretManager.GetFunctionSecretsAsync(functionName);
                }

                Assert.NotNull(functionSecrets);
                Assert.Equal(functionSecrets["Key1"], "cryptoError");
                var result = JsonConvert.DeserializeObject<FunctionSecrets>(File.ReadAllText(Path.Combine(directory.Path, functionName + ".json")));
                Assert.Equal(result.GetFunctionKey("Key1", functionName).Value, "!cryptoError");
                Assert.Equal(1, Directory.GetFiles(directory.Path, $"{functionName}.{ScriptConstants.Snapshot}*").Length);
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
            'value': 'FunctionValue1',
            'encrypted': true
        },
        {
            'name': 'Key2',
            'value': 'FunctionValue2',
            'encrypted': false
        }
    ]
}";
                ILoggerFactory loggerFactory = new LoggerFactory();
                TestLoggerProvider loggerProvider = new TestLoggerProvider();
                loggerFactory.AddProvider(loggerProvider);
                var logger = loggerFactory.CreateLogger(LogCategories.CreateFunctionCategory("Test1"));
                IDictionary<string, string> functionSecrets;
                ISecretsRepository repository = new FileSystemSecretsRepository(directory.Path);

                using (var secretManager = new SecretManager(_settingsManager, repository, logger))
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

                            string hostName = "test" + (i % 2).ToString();
                            _settingsManager.SetSetting(EnvironmentSettingNames.AzureWebsiteHostName, hostName);
                            functionSecrets = await secretManager.GetFunctionSecretsAsync(functionName);
                        }
                    }
                    catch (InvalidOperationException ex)
                    {
                        ioe = ex;
                    }
                    finally
                    {
                        _settingsManager.SetSetting(EnvironmentSettingNames.AzureWebsiteHostName, null);
                    }
                }

                Assert.True(Directory.GetFiles(directory.Path, $"{functionName}.{ScriptConstants.Snapshot}*").Length >= ScriptConstants.MaximumSecretBackupCount);
                Assert.True(loggerProvider.GetAllLogMessages().Any(
                    t => t.Level == LogLevel.Debug && t.FormattedMessage.IndexOf(expectedTraceMessage, StringComparison.OrdinalIgnoreCase) > -1),
                    "Expected Trace message not found");

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
                ISecretsRepository repository = new FileSystemSecretsRepository(directory.Path);

                using (var secretManager = new SecretManager(_settingsManager, repository, null))
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

                using (var secretManager = new SecretManager(_settingsManager, repository, null))
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
                ISecretsRepository repository = new FileSystemSecretsRepository(directory.Path);
                using (var secretManager = new SecretManager(_settingsManager, repository, null))
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

                using (var secretManager = new SecretManager(_settingsManager, repository, null))
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
                .Returns<Key>(k => new Key(k.Name, simulateWriteConversion ? "!" + k.Value : k.Value) { IsEncrypted = simulateWriteConversion });

            var mockValueConverterFactory = new Mock<IKeyValueConverterFactory>();
            mockValueConverterFactory.Setup(f => f.GetValueReader(It.IsAny<Key>()))
                .Returns(mockValueReader.Object);
            mockValueConverterFactory.Setup(f => f.GetValueWriter(It.IsAny<Key>()))
                .Returns(mockValueWriter.Object);

            return mockValueConverterFactory;
        }
    }
}
