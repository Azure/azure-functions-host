// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class ScriptSecretSerializerV1Tests
    {
        [Fact]
        public void SerializeFunctionSecrets_ReturnsExpectedResult()
        {
            var serializer = new ScriptSecretSerializerV1();

            var secrets = new FunctionSecrets
            {
                Keys = new List<Key>
                {
                    new Key
                    {
                        Name = "Key1",
                        Value = "Value1",
                        IsEncrypted = false
                    },
                    new Key
                    {
                        Name = "Key2",
                        Value = "Value2",
                        IsEncrypted = true
                    }
                }
            };

            string serializedSecret = serializer.SerializeSecrets(secrets);

            var jsonObject = JObject.Parse(serializedSecret);
            var serializedSecrets = jsonObject.Property("keys")?.Value?.ToObject<List<Key>>();
            var source = jsonObject.Property("source")?.Value;
            var hostName = jsonObject.Property("hostName")?.Value;
            var instanceId = jsonObject.Property("instanceId")?.Value;

            Assert.NotNull(serializedSecret);
            AssertKeyCollectionsEquality(secrets.Keys, serializedSecrets);
            Assert.Equal(source, secrets.Source);
            Assert.Equal(hostName, secrets.HostName);
            Assert.Equal(instanceId, secrets.InstanceId);
        }

        [Theory]
        [InlineData("{ 'keys': [ { 'name': 'Key1', 'value': 'Value1', 'encrypted': false }, { 'name': 'Key2', 'value': 'Value2', 'encrypted': true } ] }", null)]
        [InlineData("{ 'keys': [ { 'name': 'Key1', 'value': 'Value1', 'encrypted': false }, { 'name': 'Key2', 'value': 'Value2', 'encrypted': true } ], 'hostName': 'test', 'source': 'runtime'}", "test")]
        public void DeserializeFunctionSecrets_ReturnsExpectedResult(string serializedSecret, string hostName)
        {
            var serializer = new ScriptSecretSerializerV1();
            var expected = new List<Key>
            {
                new Key
                {
                    Name = "Key1",
                    Value = "Value1",
                    IsEncrypted = false
                },
                new Key
                {
                    Name = "Key2",
                    Value = "Value2",
                    IsEncrypted = true
                }
            };

            FunctionSecrets actual = serializer.DeserializeSecrets<FunctionSecrets>(JObject.Parse(serializedSecret));
            Assert.Equal(hostName, actual.HostName);
            AssertKeyCollectionsEquality(expected, actual.Keys);
        }

        [Theory]
        [InlineData("{'masterKey':{'name':'master','value':'1234','encrypted':false},'functionKeys':[{'name':'Key1','value':'Value1','encrypted':false},{'name':'Key2','value':'Value2','encrypted':true}]}", null)]
        [InlineData("{'masterKey':{'name':'master','value':'1234','encrypted':false},'functionKeys':[{'name':'Key1','value':'Value1','encrypted':false},{'name':'Key2','value':'Value2','encrypted':true}],  'hostName': 'test', 'source': 'runtime' }", "test")]
        public void DeserializeHostSecrets_ReturnsExpectedResult(string serializedSecret, string hostName)
        {
            var serializer = new ScriptSecretSerializerV1();
            var expected = new HostSecrets
            {
                MasterKey = new Key { Name = "master", Value = "1234" },
                FunctionKeys = new List<Key>
                {
                    new Key
                    {
                        Name = "Key1",
                        Value = "Value1",
                        IsEncrypted = false
                    },
                    new Key
                    {
                        Name = "Key2",
                        Value = "Value2",
                        IsEncrypted = true
                    }
                },
                HostName = hostName
            };

            HostSecrets actual = serializer.DeserializeSecrets<HostSecrets>(JObject.Parse(serializedSecret));

            Assert.NotNull(actual);
            Assert.Equal(expected.MasterKey, actual.MasterKey);
            Assert.Equal(actual.HostName, hostName);
            Assert.Equal(expected.Source, ScriptConstants.Runtime);
            AssertKeyCollectionsEquality(expected.FunctionKeys, actual.FunctionKeys);
        }

        [Fact]
        public void SerializeHostSecrets_ReturnsExpectedResult()
        {
            var serializer = new ScriptSecretSerializerV1();

            var secrets = new HostSecrets
            {
                MasterKey = new Key { Name = "master", Value = "1234" },
                FunctionKeys = new List<Key>
                {
                    new Key
                    {
                        Name = "Key1",
                        Value = "Value1",
                        IsEncrypted = false
                    },
                    new Key
                    {
                        Name = "Key2",
                        Value = "Value2",
                        IsEncrypted = true
                    }
                }
            };

            string serializedSecret = serializer.SerializeSecrets(secrets);

            var jsonObject = JObject.Parse(serializedSecret);
            var functionSecrets = jsonObject.Property("functionKeys")?.Value?.ToObject<List<Key>>();
            var masterKey = jsonObject.Property("masterKey")?.Value?.ToObject<Key>();
            var instanceId = jsonObject.Property("instanceId")?.Value;

            Assert.NotNull(serializedSecret);
            Assert.Equal(secrets.MasterKey, masterKey);
            AssertKeyCollectionsEquality(secrets.FunctionKeys, functionSecrets);
            Assert.Equal(instanceId, secrets.InstanceId);
        }

        [Theory]
        [InlineData(typeof(HostSecrets), false, "{'masterKey': 'masterKeySecretString','functionKey': 'functionKeySecretString'}")]
        [InlineData(typeof(FunctionSecrets), false, "{'key':'functionKeySecretString'}")]
        [InlineData(typeof(HostSecrets), true, "{'masterKey': {'name': 'master','value': '1234','encrypted': false},'functionKeys': [{'name': 'Key1','value': 'Value1','encrypted': false},{'name': 'Key2','value': 'Value2','encrypted': true}]}")]
        [InlineData(typeof(FunctionSecrets), true, "{'keys': [{'name': 'Key1','value': 'Value1','encrypted': false},{'name': 'Key2','value': 'Value2','encrypted': true}]}")]
        [InlineData(typeof(HostSecrets), false, "{'masterKey': 'masterKeySecretString','functionKey': 'functionKeySecretString', 'hostName': 'test1', 'instanceId': 'test2', 'source': 'test3'}")]
        public void CanSerialize_WithValidHostPayload_ReturnsTrue(Type type, bool expectedResult, string input)
        {
            var serializer = new ScriptSecretSerializerV1();

            bool result = serializer.CanSerialize(JObject.Parse(input), type);

            Assert.Equal(expectedResult, result);
        }

        private void AssertKeyCollectionsEquality(IList<Key> expected, IList<Key> actual)
        {
            Assert.NotNull(actual);
            Assert.Equal(expected.Count, actual.Count);
            Assert.True(expected.Zip(actual, (k1, k2) => k1.Equals(k2)).All(r => r));
        }
    }
}
