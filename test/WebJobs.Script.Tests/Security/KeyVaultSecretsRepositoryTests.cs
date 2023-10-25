// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Extensions.Logging;
using Moq;
using WebJobs.Script.Tests;
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
        [MemberData(nameof(HostSecretsDataProvider.TestCases), MemberType = typeof(HostSecretsDataProvider))]
        public async Task ReadHostKeys(List<KeyVaultSecret> keyVaultSecrets, HostSecrets expectedHostSecrets)
        {
            using var secretSentinelDirectory = new TempDirectory();
            Mock<SecretClient> mockClient = ConfigureSecretClientMock(keyVaultSecrets);
            var loggerFactory = MockNullLoggerFactory.CreateLoggerFactory();
            var repository = new KeyVaultSecretsRepository(mockClient.Object, secretSentinelDirectory.Path, loggerFactory.CreateLogger<KeyVaultSecretsRepository>(), new TestEnvironment());

            var secrets = await repository.ReadAsync(ScriptSecretsType.Host, string.Empty);
            var hostSecrets = secrets as HostSecrets;

            Assert.Equal(expectedHostSecrets.MasterKey.Name, hostSecrets?.MasterKey.Name);
            Assert.Equal(expectedHostSecrets.MasterKey.Value, hostSecrets?.MasterKey.Value);

            Assert.Equal(expectedHostSecrets.SystemKeys.Count, hostSecrets?.SystemKeys.Count);
            foreach (var expectedKey in expectedHostSecrets.SystemKeys)
            {
                var keys = hostSecrets?.SystemKeys.Where(k => k.Name == expectedKey.Name);
                Assert.Single(keys);
                var key = keys.First();
                Assert.Equal(expectedKey.Name, key.Name);
                Assert.Equal(expectedKey.Value, key.Value);
            }

            Assert.Equal(expectedHostSecrets.FunctionKeys.Count, hostSecrets?.FunctionKeys.Count);
            foreach (var expectedKey in expectedHostSecrets.SystemKeys)
            {
                var keys = hostSecrets?.FunctionKeys.Where(k => k.Name == expectedKey.Name);
                Assert.Single(keys);
                var key = keys.First();
                Assert.Equal(expectedKey.Name, key.Name);
                Assert.Equal(expectedKey.Value, key.Value);
            }
        }

        [Theory]
        [MemberData(nameof(HostSecretsDataProvider.TestCases), MemberType = typeof(HostSecretsDataProvider))]
        public async Task WriteHostKeys(List<KeyVaultSecret> keyVaultSecrets, HostSecrets hostSecrets)
        {
            Mock<SecretClient> mockClient = ConfigureSecretClientMock(keyVaultSecrets);
            var loggerFactory = MockNullLoggerFactory.CreateLoggerFactory();
            using var secretSentinelDirectory = new TempDirectory();
            var repository = new KeyVaultSecretsRepository(mockClient.Object, secretSentinelDirectory.Path, loggerFactory.CreateLogger<KeyVaultSecretsRepository>(), new TestEnvironment());

            await repository.WriteAsync(ScriptSecretsType.Host, string.Empty, hostSecrets);
            mockClient.Verify(client => client.StartDeleteSecretAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

            int numberOfSecrets = 0;
            if (hostSecrets.MasterKey != null)
            {
                numberOfSecrets++;
            }

            if (hostSecrets.SystemKeys != null)
            {
                numberOfSecrets += hostSecrets.SystemKeys.Count;
            }

            if (hostSecrets.FunctionKeys != null)
            {
                numberOfSecrets += hostSecrets.FunctionKeys.Count;
            }

            mockClient.Verify(client => client.SetSecretAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(numberOfSecrets));
        }

        [Theory]
        [MemberData(nameof(FunctionSecretsDataProvider.TestCases), MemberType = typeof(FunctionSecretsDataProvider))]
        public async Task ReadFunctionKeys(string functionName, List<KeyVaultSecret> keyVaultSecrets, FunctionSecrets expectedFunctionSecrets)
        {
            using var secretSentinelDirectory = new TempDirectory();
            Mock<SecretClient> mockClient = ConfigureSecretClientMock(keyVaultSecrets);
            var loggerFactory = MockNullLoggerFactory.CreateLoggerFactory();
            var repository = new KeyVaultSecretsRepository(mockClient.Object, secretSentinelDirectory.Path, loggerFactory.CreateLogger<KeyVaultSecretsRepository>(), new TestEnvironment());

            var secrets = await repository.ReadAsync(ScriptSecretsType.Function, functionName);
            var functionSecrets = secrets as FunctionSecrets;

            foreach (var secret in expectedFunctionSecrets.Keys)
            {
                var keys = functionSecrets?.Keys?.Where(k => k.Name == secret.Name);
                Assert.Single(keys);
                var key = keys.First();
                Assert.Equal(secret.Name, key.Name);
                Assert.Equal(secret.Value, key.Value);
            }
        }

        [Theory]
        [MemberData(nameof(FunctionSecretsDataProvider.TestCases), MemberType = typeof(FunctionSecretsDataProvider))]
        public async Task WriteFunctionKeys(string functionName, List<KeyVaultSecret> keyVaultSecrets, FunctionSecrets functionSecrets)
        {
            Mock<SecretClient> mockClient = ConfigureSecretClientMock(keyVaultSecrets);
            var loggerFactory = MockNullLoggerFactory.CreateLoggerFactory();
            using var secretSentinelDirectory = new TempDirectory();
            var repository = new KeyVaultSecretsRepository(mockClient.Object, secretSentinelDirectory.Path, loggerFactory.CreateLogger<KeyVaultSecretsRepository>(), new TestEnvironment());

            await repository.WriteAsync(ScriptSecretsType.Function, functionName, functionSecrets);
            mockClient.Verify(client => client.StartDeleteSecretAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            mockClient.Verify(client => client.SetSecretAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(functionSecrets.Keys.Count));
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

        private AsyncPageable<SecretProperties> GetPageableSecretProperties(IReadOnlyList<SecretProperties> secretProperties)
        {
            return AsyncPageable<SecretProperties>.FromPages(new[] { Page<SecretProperties>.FromValues(secretProperties, default, null) });
        }

        private Mock<SecretClient> ConfigureSecretClientMock(List<KeyVaultSecret> keyVaultSecrets)
        {
            var mockClient = new Mock<SecretClient>(MockBehavior.Loose);
            List<SecretProperties> properties = keyVaultSecrets.Select(kvSecret => kvSecret.Properties).ToList();
            mockClient.Setup(client => client.GetPropertiesOfSecretsAsync(It.IsAny<CancellationToken>())).Returns(GetPageableSecretProperties(properties));

            foreach (var keyVaultSecret in keyVaultSecrets)
            {
                mockClient.Setup(client => client.GetSecretAsync(keyVaultSecret.Name, It.IsAny<string>(), It.IsAny<CancellationToken>()))
                          .ReturnsAsync(Response.FromValue(keyVaultSecret, Mock.Of<Response>()));
            }

            return mockClient;
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

        public class HostSecretsDataProvider
        {
            public static IEnumerable<object[]> TestCases
            {
                get
                {
                    var secrets = new List<KeyVaultSecret>()
                    {
                        SecretModelFactory.KeyVaultSecret(new SecretProperties("host--masterKey--master"), "test"),
                        SecretModelFactory.KeyVaultSecret(new SecretProperties("host--systemKey--test"), "test"),
                        SecretModelFactory.KeyVaultSecret(new SecretProperties("host--functionKey--test"), "test")
                    };

                    HostSecrets hostSecrets = new HostSecrets()
                    {
                        MasterKey = new Key("master", "test"),
                        FunctionKeys = new List<Key>() { new Key("test", "test") },
                        SystemKeys = new List<Key>() { new Key("test", "test") }
                    };

                    yield return new object[] { secrets, hostSecrets };

                    secrets = new List<KeyVaultSecret>()
                    {
                        SecretModelFactory.KeyVaultSecret(new SecretProperties("host--masterkey--master"), "test"),
                        SecretModelFactory.KeyVaultSecret(new SecretProperties("host--systemkey--test"), "test"),
                        SecretModelFactory.KeyVaultSecret(new SecretProperties("host--functionkey--test"), "test"),
                    };

                    hostSecrets = new HostSecrets()
                    {
                        MasterKey = new Key("master", "test"),
                        FunctionKeys = new List<Key>() { new Key("test", "test") },
                        SystemKeys = new List<Key>() { new Key("test", "test") }
                    };

                    yield return new object[] { secrets, hostSecrets };

                    secrets = new List<KeyVaultSecret>()
                    {
                        SecretModelFactory.KeyVaultSecret(new SecretProperties("hosT--masterkEy--master"), "test"),
                        SecretModelFactory.KeyVaultSecret(new SecretProperties("hoSt--sysTemkey--test"), "test"),
                        SecretModelFactory.KeyVaultSecret(new SecretProperties("Host--functIonkey--test"), "test"),
                    };

                    hostSecrets = new HostSecrets()
                    {
                        MasterKey = new Key("master", "test"),
                        FunctionKeys = new List<Key>() { new Key("test", "test") },
                        SystemKeys = new List<Key>() { new Key("test", "test") }
                    };

                    yield return new object[] { secrets, hostSecrets };
                }
            }
        }

        public class FunctionSecretsDataProvider
        {
            public static IEnumerable<object[]> TestCases
            {
                get
                {
                    var functionName = "Function1";

                    var keyVaultSecrets = new List<KeyVaultSecret>()
                    {
                        SecretModelFactory.KeyVaultSecret(new SecretProperties($"function--{functionName}--test"), "test"),
                        SecretModelFactory.KeyVaultSecret(new SecretProperties($"function--{functionName}--{KeyVaultSecretsRepository.Normalize("te--%st-te^s-t")}"), "test"),
                    };

                    var functionSecrets = new FunctionSecrets()
                    {
                        Keys = new List<Key>()
                        {
                            new Key("test", "test"),
                            new Key("te--%st-te^s-t", "test")
                        }
                    };

                    yield return new object[] { functionName, keyVaultSecrets, functionSecrets };

                    functionName = "GreatFuNcTiOn";

                    keyVaultSecrets = new List<KeyVaultSecret>()
                    {
                        SecretModelFactory.KeyVaultSecret(new SecretProperties($"fUnction--{functionName}--test"), "test"),
                        SecretModelFactory.KeyVaultSecret(new SecretProperties($"Function--{functionName.ToUpperInvariant()}--{KeyVaultSecretsRepository.Normalize("te--%st-te^s-t")}"), "test"),
                    };

                    functionSecrets = new FunctionSecrets()
                    {
                        Keys = new List<Key>()
                        {
                            new Key("test", "test"),
                            new Key("te--%st-te^s-t", "test")
                        }
                    };

                    yield return new object[] { functionName, keyVaultSecrets, functionSecrets };
                }
            }
        }
    }
}