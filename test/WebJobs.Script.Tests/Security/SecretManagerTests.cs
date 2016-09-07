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

                var secretManager = new SecretManager(secretsPath);
                Dictionary<string, string> result = secretManager.GetMergedFunctionSecrets("testfunction");

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

                    var mockValueReader = new Mock<IKeyValueReader>();
                    mockValueReader.Setup(r => r.ReadValue(It.IsAny<Key>()))
                        .Returns<Key>(k => new Key(k.Name, k.Value) { IsStale = true });

                    var mockValueWriter = new Mock<IKeyValueWriter>();
                    mockValueWriter.Setup(r => r.WriteValue(It.IsAny<Key>()))
                        .Returns<Key>(k => new Key(k.Name, "!" + k.Value));

                    var mockValueConverterFactory = new Mock<IKeyValueConverterFactory>();
                    mockValueConverterFactory.Setup(f => f.GetValueReader(It.IsAny<Key>()))
                        .Returns(mockValueReader.Object);
                    mockValueConverterFactory.Setup(f => f.GetValueWriter(It.IsAny<Key>()))
                        .Returns(mockValueWriter.Object);

                    var secretManager = new SecretManager(secretsPath, mockValueConverterFactory.Object);

                    var functionSecrets = secretManager.GetFunctionSecrets("testfunction");

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

                    var mockValueReader = new Mock<IKeyValueReader>();
                    mockValueReader.Setup(r => r.ReadValue(It.IsAny<Key>()))
                        .Returns<Key>(k => new Key(k.Name, k.Value) { IsStale = true });

                    var mockValueWriter = new Mock<IKeyValueWriter>();
                    mockValueWriter.Setup(r => r.WriteValue(It.IsAny<Key>()))
                        .Returns<Key>(k => new Key(k.Name, "!" + k.Value));

                    var mockValueConverterFactory = new Mock<IKeyValueConverterFactory>();
                    mockValueConverterFactory.Setup(f => f.GetValueReader(It.IsAny<Key>()))
                        .Returns(mockValueReader.Object);
                    mockValueConverterFactory.Setup(f => f.GetValueWriter(It.IsAny<Key>()))
                        .Returns(mockValueWriter.Object);

                    var secretManager = new SecretManager(secretsPath, mockValueConverterFactory.Object);

                    var hostSecrets = secretManager.GetHostSecrets();

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
    }
}
