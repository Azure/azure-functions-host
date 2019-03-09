// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class KeyVaultSecretsRepositoryTests
    {
        [Theory]
        [InlineData("Te-st!{rty")]
        [InlineData("Te-st!{rt*(y)(ty-")]
        [InlineData("Te--st!{rt*(y)(ty--")]
        public void Normalization_WorksAsExpected(string name)
        {
            string normalizedName = KeyVaultSecretsRepository.Normalize(name);
            string test = KeyVaultSecretsRepository.Denormalize(normalizedName);
            Assert.Equal(test, name);
        }

        [Theory]
        [InlineData("te%st-te^st-")]
        [InlineData("te--%st-te^s-t")]
        public void HostKeys(string secretName)
        {
            HostSecrets hostSecrets = new HostSecrets()
            {
                MasterKey = new Key("master", "test"),
                FunctionKeys = new List<Key>() { new Key(secretName, "test") },
                SystemKeys = new List<Key>() { new Key(secretName, "test") },
            };

            Dictionary<string, string> dictionary = KeyVaultSecretsRepository.GetDictionaryFromScriptSecrets(hostSecrets, null);

            Assert.True(dictionary["host--masterKey--master"] == "test");
            Assert.True(dictionary[$"host--functionKey--{KeyVaultSecretsRepository.Normalize(secretName)}"] == "test");
            Assert.True(dictionary[$"host--systemKey--{KeyVaultSecretsRepository.Normalize(secretName)}"] == "test");
        }

        [Theory]
        [InlineData("Func-test", "te%st-te^st-")]
        [InlineData("Func--test-", "te--%st-te^s-t")]
        public void FunctionKeys(string functionName, string secretName)
        {
            FunctionSecrets hostSecrets = new FunctionSecrets()
            {
                Keys = new List<Key> { new Key(secretName, "test") }
            };

            Dictionary<string, string> dictionary = KeyVaultSecretsRepository.GetDictionaryFromScriptSecrets(hostSecrets, functionName);

            Assert.True(dictionary[$"function--{KeyVaultSecretsRepository.Normalize(functionName)}--{KeyVaultSecretsRepository.Normalize(secretName)}"] == "test");
        }
    }
}
