// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Azure;
using Azure.Security.KeyVault.Secrets;
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

        [Theory]
        [MemberData(nameof(FindSecretsDataProvider.TestCases), MemberType = typeof(FindSecretsDataProvider))]
        public async Task FindSecrets(Func<SecretProperties, bool> comparison, List<string> expectedMatches)
        {
            AsyncPageable<SecretProperties> secretsPages = GetSecretProperties();
            var matches = await KeyVaultSecretsRepository.FindSecrets(secretsPages, comparison);

            Assert.Equal(expectedMatches.Count, matches.Count);
            foreach (string name in expectedMatches)
            {
                var matchingNames = matches.Where(x => x.Name == name);
                Assert.Equal(matchingNames.Count(), 1);
                Assert.Equal(matchingNames.First().Name, name);
            }
        }

        private AsyncPageable<SecretProperties> GetSecretProperties()
        {
            // Create a list of SecretProperties
            var pageOneValues = new[]
            {
                new SecretProperties(new Uri("https://testkeyvault.vault.azure.net/secrets/Atlanta")),
                new SecretProperties(new Uri("https://testkeyvault.vault.azure.net/secrets/Seattle")),
                new SecretProperties(new Uri("https://testkeyvault.vault.azure.net/secrets/NewYork")),
                new SecretProperties(new Uri("https://testkeyvault.vault.azure.net/secrets/Chicago"))
            };

            var pageTwoValues = new[]
            {
                new SecretProperties(new Uri("https://testkeyvault.vault.azure.net/secrets/Portland")),
                new SecretProperties(new Uri("https://testkeyvault.vault.azure.net/secrets/Austin")),
                new SecretProperties(new Uri("https://testkeyvault.vault.azure.net/secrets/SanDiego")),
                new SecretProperties(new Uri("https://testkeyvault.vault.azure.net/secrets/LosAngeles"))
            };

            var page1 = Page<SecretProperties>.FromValues(pageOneValues, default, null);
            var page2 = Page<SecretProperties>.FromValues(pageTwoValues, default, null);

            return AsyncPageable<SecretProperties>.FromPages(new[] { page1, page2 });
        }

        public class FindSecretsDataProvider
        {
            public static IEnumerable<object[]> TestCases
            {
                get
                {
                    yield return new object[] { (Func<SecretProperties, bool>)(x => x.Name.StartsWith("S")), new List<string>() { "Seattle", "SanDiego" } };
                    yield return new object[] { (Func<SecretProperties, bool>)(x => x.Name.EndsWith("o")), new List<string>() { "Chicago", "SanDiego" } };
                }
            }
        }
    }
}
