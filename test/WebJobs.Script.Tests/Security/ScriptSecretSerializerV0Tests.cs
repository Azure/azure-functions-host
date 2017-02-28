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
    public class ScriptSecretSerializerV0Tests
    {
        [Fact]
        public void SerializeFunctionSecrets_ReturnsExpectedResult()
        {
            var serializer = new ScriptSecretSerializerV0();

            var secrets = new FunctionSecrets
            {
                Keys = new List<Key>
                {
                    new Key
                    {
                        Name = string.Empty,
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

            Assert.NotNull(serializedSecret);

            var jsonObject = JObject.Parse(serializedSecret);
            var serializedSecretValue = jsonObject.Value<string>("key");

            Assert.Equal("Value1", serializedSecretValue);
        }

        [Fact]
        public void DeserializeFunctionSecrets_ReturnsExpectedResult()
        {
            var serializer = new ScriptSecretSerializerV0();
            var serializedSecret = "{ 'key': 'TestValue' }";

            var expected = new List<Key>
            {
                new Key
                {
                    Name = ScriptConstants.DefaultFunctionKeyName,
                    Value = "TestValue",
                    IsEncrypted = false
                }
            };

            FunctionSecrets actual = serializer.DeserializeSecrets<FunctionSecrets>(JObject.Parse(serializedSecret));
            AssertKeyCollectionsEquality(expected, actual.Keys);
        }

        [Fact]
        public void DeserializeHostSecrets_ReturnsExpectedResult()
        {
            var serializer = new ScriptSecretSerializerV0();
            var serializedSecret = "{'masterKey': 'master', 'functionKey': 'master'}";
            var expected = new HostSecrets
            {
                MasterKey = new Key { Name = ScriptConstants.DefaultMasterKeyName, Value = "master" },
                FunctionKeys = new List<Key>
                {
                    new Key
                    {
                        Name = ScriptConstants.DefaultFunctionKeyName,
                        Value = "master",
                        IsEncrypted = false
                    }
                }
            };

            HostSecrets actual = serializer.DeserializeSecrets<HostSecrets>(JObject.Parse(serializedSecret));

            Assert.NotNull(actual);
            Assert.Equal(expected.MasterKey, actual.MasterKey);
            AssertKeyCollectionsEquality(expected.FunctionKeys, actual.FunctionKeys);
        }

        [Fact]
        public void SerializeHostSecrets_ReturnsExpectedResult()
        {
            var serializer = new ScriptSecretSerializerV0();

            var secrets = new HostSecrets
            {
                MasterKey = new Key { Name = "master", Value = "mastervalue" },
                FunctionKeys = new List<Key>
                {
                    new Key
                    {
                        Name = string.Empty,
                        Value = "functionKeyValue",
                        IsEncrypted = false,
                    }
                }
            };

            string serializedSecret = serializer.SerializeSecrets(secrets);

            Assert.NotNull(serializedSecret);

            var jsonObject = JObject.Parse(serializedSecret);
            var functionKey = jsonObject.Value<string>("functionKey");
            var masterKey = jsonObject.Value<string>("masterKey");

            Assert.Equal("mastervalue", masterKey);
            Assert.Equal("functionKeyValue", functionKey);
        }

        [Theory]
        [InlineData(typeof(HostSecrets), true, "{'masterKey': 'masterKeySecretString','functionKey': 'functionKeySecretString'}")]
        [InlineData(typeof(FunctionSecrets), true, "{'key':'functionKeySecretString'}")]
        [InlineData(typeof(HostSecrets), false, "{'masterKey': {'name': 'master','value': '1234','encrypted': false},'functionKeys': [{'name': 'Key1','value': 'Value1','encrypted': false},{'name': 'Key2','value': 'Value2','encrypted': true}]}")]
        [InlineData(typeof(FunctionSecrets), false, "{'keys': [{'name': 'Key1','value': 'Value1','encrypted': false},{'name': 'Key2','value': 'Value2','encrypted': true}]}")]
        public void CanSerialize_WithValidHostPayload_ReturnsTrue(Type type, bool expectedResult, string input)
        {
            var serializer = new ScriptSecretSerializerV0();

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
