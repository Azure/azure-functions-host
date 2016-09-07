// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Security
{
    public class SecretManagerTests
    {
        [Fact]
        public void MergedSecrets_PrioritizesFunctionSecrets()
        {
            var secretsPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            try
            {
                Directory.CreateDirectory(secretsPath);
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
                File.WriteAllText(Path.Combine(secretsPath, ScriptConstants.HostMetadataFileName), hostSecrets);
                File.WriteAllText(Path.Combine(secretsPath, "testfunction.json"), functionSecrets);

                IDictionary<string, string> result;
                using (var secretManager = new SecretManager(secretsPath))
                {
                    result = secretManager.GetFunctionSecrets("testfunction", true);
                }

                Assert.Contains("Key1", result.Keys);
                Assert.Contains("Key2", result.Keys);
                Assert.Contains("Key3", result.Keys);
                Assert.Equal("FunctionValue1", result["Key1"]);
                Assert.Equal("FunctionValue2", result["Key2"]);
                Assert.Equal("HostValue3", result["Key3"]);
            }
            finally
            {
                Directory.Delete(secretsPath, true);
            }
        }

        [Fact]
        public void GetFunctionSecrets_UpdatesStaleSecrets()
        {
            var secretsPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            try
            {
                using (var variables = new TestScopedEnvironmentVariables("AzureWebJobsEnableMultiKey", "true"))
                {
                    Directory.CreateDirectory(secretsPath);
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
                    File.WriteAllText(Path.Combine(secretsPath, "testfunction.json"), functionSecretsJson);

                    Mock<IKeyValueConverterFactory> mockValueConverterFactory = GetConverterFactoryMock();

                    IDictionary<string, string> functionSecrets;
                    using (var secretManager = new SecretManager(secretsPath, mockValueConverterFactory.Object))
                    {
                        functionSecrets = secretManager.GetFunctionSecrets("testfunction");
                    }
                    // Read the persisted content
                    var result = JsonConvert.DeserializeObject<FunctionSecrets>(File.ReadAllText(Path.Combine(secretsPath, "testfunction.json")));
                    bool functionSecretsConverted = functionSecrets.Values.Zip(result.Keys, (r1, r2) => string.Equals("!" + r1, r2.Value)).All(r => r);

                    Assert.Equal(2, result.Keys.Count);
                    Assert.True(functionSecretsConverted, "Function secrets were not persisted");
                }
            }
            finally
            {
                Directory.Delete(secretsPath, true);
            }
        }

        [Fact]
        public void GetHostSecrets_UpdatesStaleSecrets()
        {
            var secretsPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            try
            {
                using (var variables = new TestScopedEnvironmentVariables("AzureWebJobsEnableMultiKey", "true"))
                {
                    Directory.CreateDirectory(secretsPath);
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
    ]
}";
                    File.WriteAllText(Path.Combine(secretsPath, ScriptConstants.HostMetadataFileName), hostSecretsJson);

                    Mock<IKeyValueConverterFactory> mockValueConverterFactory = GetConverterFactoryMock();

                    HostSecretsInfo hostSecrets;
                    using (var secretManager = new SecretManager(secretsPath, mockValueConverterFactory.Object))
                    {
                        hostSecrets = secretManager.GetHostSecrets();
                    }

                    // Read the persisted content
                    var result = JsonConvert.DeserializeObject<HostSecrets>(File.ReadAllText(Path.Combine(secretsPath, ScriptConstants.HostMetadataFileName)));
                    bool functionSecretsConverted = hostSecrets.FunctionKeys.Values.Zip(result.FunctionKeys, (r1, r2) => string.Equals("!" + r1, r2.Value)).All(r => r);

                    Assert.Equal(2, result.FunctionKeys.Count);
                    Assert.Equal("!" + hostSecrets.MasterKey, result.MasterKey.Value);
                    Assert.True(functionSecretsConverted, "Function secrets were not persisted");
                }
            }
            finally
            {
                Directory.Delete(secretsPath, true);
            }
        }

        [Fact]
        public void GetHostSecrets_WhenNoHostSecretFileExists_GeneratesSecretsAndPersistsFiles()
        {
            var secretsPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            try
            {
                using (var variables = new TestScopedEnvironmentVariables("AzureWebJobsEnableMultiKey", "true"))
                {
                    Mock<IKeyValueConverterFactory> mockValueConverterFactory = GetConverterFactoryMock(false);

                    HostSecretsInfo hostSecrets;
                    using (var secretManager = new SecretManager(secretsPath, mockValueConverterFactory.Object))
                    {
                        hostSecrets = secretManager.GetHostSecrets();
                    }

                    string secretsJson = File.ReadAllText(Path.Combine(secretsPath, ScriptConstants.HostMetadataFileName));
                    HostSecrets persistedSecrets = ScriptSecretSerializer.DeserializeSecrets<HostSecrets>(secretsJson);

                    Assert.NotNull(hostSecrets);
                    Assert.NotNull(persistedSecrets);
                    Assert.Equal(1, hostSecrets.FunctionKeys.Count);
                    Assert.NotNull(hostSecrets.MasterKey);
                    Assert.Equal(persistedSecrets.MasterKey.Value, hostSecrets.MasterKey);
                    Assert.Equal(persistedSecrets.FunctionKeys.First().Value, hostSecrets.FunctionKeys.First().Value);
                }
            }
            finally
            {
                Directory.Delete(secretsPath, true);
            }
        }

        [Fact]
        public void GetFunctionSecrets_WhenNoSecretFileExists_ReturnsEmptySecretsAndDoesNotPersistsFile()
        {
            var secretsPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            try
            {
                using (var variables = new TestScopedEnvironmentVariables("AzureWebJobsEnableMultiKey", "true"))
                {
                    Mock<IKeyValueConverterFactory> mockValueConverterFactory = GetConverterFactoryMock(false);

                    IDictionary<string, string> functionSecrets;
                    using (var secretManager = new SecretManager(secretsPath, mockValueConverterFactory.Object))
                    {
                        functionSecrets = secretManager.GetFunctionSecrets("TestFunction");
                    }

                    bool functionSecretsExists = File.Exists(Path.Combine(secretsPath, "testfunction.json"));

                    Assert.NotNull(functionSecrets);
                    Assert.False(functionSecretsExists);
                    Assert.Equal(0, functionSecrets.Count);
                }
            }
            finally
            {
                Directory.Delete(secretsPath, true);
            }
        }

        [Fact]
        public void AddOrUpdateFunctionSecrets_WithFunctionNameAndNoSecret_GeneratesFunctionSecretsAndPersistsFile()
        {
            var secretsPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            try
            {
                using (var variables = new TestScopedEnvironmentVariables("AzureWebJobsEnableMultiKey", "true"))
                {
                    Mock<IKeyValueConverterFactory> mockValueConverterFactory = GetConverterFactoryMock(false);

                    KeyOperationResult result;
                    using (var secretManager = new SecretManager(secretsPath, mockValueConverterFactory.Object))
                    {
                        result = secretManager.AddOrUpdateFunctionSecret("TestSecret", null, "TestFunction");
                    }

                    string secretsJson = File.ReadAllText(Path.Combine(secretsPath, "testfunction.json"));
                    FunctionSecrets persistedSecrets = ScriptSecretSerializer.DeserializeSecrets<FunctionSecrets>(secretsJson);

                    Assert.Equal(OperationResult.Created, result.Result);
                    Assert.NotNull(result.Secret);
                    Assert.NotNull(persistedSecrets);
                    Assert.Equal(result.Secret, persistedSecrets.Keys.First().Value);
                    Assert.Equal("TestSecret", persistedSecrets.Keys.First().Name, StringComparer.Ordinal);
                }
            }
            finally
            {
                Directory.Delete(secretsPath, true);
            }
        }

        [Fact]
        public void AddOrUpdateFunctionSecrets_WithFunctionNameAndProvidedSecret_UsesSecretAndPersistsFile()
        {
            var secretsPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            try
            {
                using (var variables = new TestScopedEnvironmentVariables("AzureWebJobsEnableMultiKey", "true"))
                {
                    Mock<IKeyValueConverterFactory> mockValueConverterFactory = GetConverterFactoryMock(false);

                    KeyOperationResult result;
                    using (var secretManager = new SecretManager(secretsPath, mockValueConverterFactory.Object))
                    {
                        result = secretManager.AddOrUpdateFunctionSecret("TestSecret", "TestSecretValue", "TestFunction");
                    }

                    string secretsJson = File.ReadAllText(Path.Combine(secretsPath, "testfunction.json"));
                    FunctionSecrets persistedSecrets = ScriptSecretSerializer.DeserializeSecrets<FunctionSecrets>(secretsJson);

                    Assert.Equal(OperationResult.Created, result.Result);
                    Assert.Equal("TestSecretValue", result.Secret, StringComparer.Ordinal);
                    Assert.NotNull(persistedSecrets);
                    Assert.Equal(result.Secret, persistedSecrets.Keys.First().Value);
                    Assert.Equal("TestSecret", persistedSecrets.Keys.First().Name, StringComparer.Ordinal);
                }
            }
            finally
            {
                Directory.Delete(secretsPath, true);
            }
        }

        [Fact]
        public void AddOrUpdateFunctionSecrets_WithNoFunctionNameAndProvidedSecret_UsesSecretAndPersistsHostFile()
        {
            var secretsPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            try
            {
                using (var variables = new TestScopedEnvironmentVariables("AzureWebJobsEnableMultiKey", "true"))
                {
                    Mock<IKeyValueConverterFactory> mockValueConverterFactory = GetConverterFactoryMock(false);

                    KeyOperationResult result;
                    using (var secretManager = new SecretManager(secretsPath, mockValueConverterFactory.Object))
                    {
                        result = secretManager.AddOrUpdateFunctionSecret("TestSecret", "TestSecretValue");
                    }

                    string secretsJson = File.ReadAllText(Path.Combine(secretsPath, ScriptConstants.HostMetadataFileName));
                    HostSecrets persistedSecrets = ScriptSecretSerializer.DeserializeSecrets<HostSecrets>(secretsJson);
                    Key newSecret = persistedSecrets.FunctionKeys.FirstOrDefault(k => string.Equals(k.Name, "TestSecret", StringComparison.Ordinal));

                    Assert.Equal(OperationResult.Created, result.Result);
                    Assert.Equal("TestSecretValue", result.Secret, StringComparer.Ordinal);
                    Assert.NotNull(persistedSecrets);
                    Assert.NotNull(newSecret);
                    Assert.Equal(result.Secret, newSecret.Value);
                    Assert.Equal("TestSecret", newSecret.Name, StringComparer.Ordinal);
                    Assert.NotNull(persistedSecrets.MasterKey);
                }
            }
            finally
            {
                Directory.Delete(secretsPath, true);
            }
        }

        [Fact]
        public void SetMasterKey_WithProvidedKey_UsesProvidedKeyAndPersistsFile()
        {
            var secretsPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            string testSecret = "abcde0123456789abcde0123456789abcde0123456789";
            try
            {
                using (var variables = new TestScopedEnvironmentVariables("AzureWebJobsEnableMultiKey", "true"))
                {
                    Mock<IKeyValueConverterFactory> mockValueConverterFactory = GetConverterFactoryMock(false);

                    KeyOperationResult result;
                    using (var secretManager = new SecretManager(secretsPath, mockValueConverterFactory.Object))
                    {
                        result = secretManager.SetMasterKey(testSecret);
                    }

                    bool functionSecretsExists = File.Exists(Path.Combine(secretsPath, "testfunction.json"));

                    string secretsJson = File.ReadAllText(Path.Combine(secretsPath, ScriptConstants.HostMetadataFileName));
                    HostSecrets persistedSecrets = ScriptSecretSerializer.DeserializeSecrets<HostSecrets>(secretsJson);

                    Assert.NotNull(persistedSecrets);
                    Assert.NotNull(persistedSecrets.MasterKey);
                    Assert.Equal(OperationResult.Updated, result.Result);
                    Assert.Equal(testSecret, result.Secret);
                }
            }
            finally
            {
                Directory.Delete(secretsPath, true);
            }
        }

        [Fact]
        public void SetMasterKey_WithoutProvidedKey_GeneratesKeyAndPersistsFile()
        {
            var secretsPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            try
            {
                using (var variables = new TestScopedEnvironmentVariables("AzureWebJobsEnableMultiKey", "true"))
                {
                    Mock<IKeyValueConverterFactory> mockValueConverterFactory = GetConverterFactoryMock(false);

                    KeyOperationResult result;
                    using (var secretManager = new SecretManager(secretsPath, mockValueConverterFactory.Object))
                    {
                        result = secretManager.SetMasterKey();
                    }

                    bool functionSecretsExists = File.Exists(Path.Combine(secretsPath, "testfunction.json"));

                    string secretsJson = File.ReadAllText(Path.Combine(secretsPath, ScriptConstants.HostMetadataFileName));
                    HostSecrets persistedSecrets = ScriptSecretSerializer.DeserializeSecrets<HostSecrets>(secretsJson);

                    Assert.NotNull(persistedSecrets);
                    Assert.NotNull(persistedSecrets.MasterKey);
                    Assert.Equal(OperationResult.Created, result.Result);
                    Assert.Equal(result.Secret, persistedSecrets.MasterKey.Value);
                }
            }
            finally
            {
                Directory.Delete(secretsPath, true);
            }
        }

        private Mock<IKeyValueConverterFactory> GetConverterFactoryMock(bool simulateWriteConversion = true)
        {
            var mockValueReader = new Mock<IKeyValueReader>();
            mockValueReader.Setup(r => r.ReadValue(It.IsAny<Key>()))
                .Returns<Key>(k => new Key(k.Name, k.Value) { IsStale = true });

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
