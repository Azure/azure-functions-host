// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Azure.WebJobs.Script.Tests.Security;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Security;
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

        [Theory]
        [InlineData("eventgrid-095extension", true)] // canonical: underscore encoded as -095
        [InlineData("simplekey", true)] // no escapes at all
        [InlineData("key-045name", true)] // canonical: hyphen encoded as -045
        [InlineData("-069ventgrid-095extension", false)] // non-canonical: -069 is 'E' (letter, should not be escaped)
        [InlineData("-048key", false)] // non-canonical: -048 is '0' (digit, should not be escaped)
        [InlineData("-097bc", false)] // non-canonical: -097 is 'a' (letter, should not be escaped)
        public void IsCanonicalToken_ValidatesRoundTrip(string token, bool expected)
        {
            Assert.Equal(expected, KeyVaultSecretsRepository.IsCanonicalToken(token));
        }

        [Fact]
        public async Task ReadHostKeys_SkipsNonCanonicalSystemKey()
        {
            // Arrange: one canonical and one non-canonical system key
            var keyVaultSecrets = new List<KeyVaultSecret>
            {
                SecretModelFactory.KeyVaultSecret(new SecretProperties("host--masterKey--master"), "masterVal"),
                SecretModelFactory.KeyVaultSecret(new SecretProperties("host--systemKey--eventgrid-095extension"), "canonicalVal"),
                SecretModelFactory.KeyVaultSecret(new SecretProperties("host--systemKey---069ventgrid-095extension"), "forgedVal"),
                SecretModelFactory.KeyVaultSecret(new SecretProperties("host--functionKey--default"), "funcVal"),
            };

            using var secretSentinelDirectory = new TempDirectory();
            Mock<SecretClient> mockClient = ConfigureSecretClientMock(keyVaultSecrets);
            var loggerFactory = MockNullLoggerFactory.CreateLoggerFactory();
            var repository = new KeyVaultSecretsRepository(mockClient.Object, secretSentinelDirectory.Path, loggerFactory.CreateLogger<KeyVaultSecretsRepository>(), new TestEnvironment());

            // Act
            var secrets = await repository.ReadAsync(ScriptSecretsType.Host, string.Empty);
            var hostSecrets = secrets as HostSecrets;

            // Assert: canonical system key is present
            Assert.Single(hostSecrets.SystemKeys);
            Assert.Equal("eventgrid_extension", hostSecrets.SystemKeys[0].Name);
            Assert.Equal("canonicalVal", hostSecrets.SystemKeys[0].Value);

            // Assert: forged non-canonical key was skipped
            Assert.DoesNotContain(hostSecrets.SystemKeys, k => string.Equals(k.Name, "Eventgrid_extension", StringComparison.Ordinal));
        }

        [Fact]
        public async Task ReadHostKeys_SkipsNonCanonicalFunctionKey()
        {
            var keyVaultSecrets = new List<KeyVaultSecret>
            {
                SecretModelFactory.KeyVaultSecret(new SecretProperties("host--masterKey--master"), "masterVal"),
                SecretModelFactory.KeyVaultSecret(new SecretProperties("host--functionKey--default"), "funcVal"),
                SecretModelFactory.KeyVaultSecret(new SecretProperties("host--functionKey---068efault"), "forgedFuncVal"),
            };

            using var secretSentinelDirectory = new TempDirectory();
            Mock<SecretClient> mockClient = ConfigureSecretClientMock(keyVaultSecrets);
            var loggerFactory = MockNullLoggerFactory.CreateLoggerFactory();
            var repository = new KeyVaultSecretsRepository(mockClient.Object, secretSentinelDirectory.Path, loggerFactory.CreateLogger<KeyVaultSecretsRepository>(), new TestEnvironment());

            var secrets = await repository.ReadAsync(ScriptSecretsType.Host, string.Empty);
            var hostSecrets = secrets as HostSecrets;

            Assert.Single(hostSecrets.FunctionKeys);
            Assert.Equal("default", hostSecrets.FunctionKeys[0].Name);
            Assert.Equal("funcVal", hostSecrets.FunctionKeys[0].Value);
        }

        [Fact]
        public async Task ReadFunctionKeys_SkipsNonCanonicalKey()
        {
            string functionName = "Function1";
            string prefix = $"function--{functionName}--";

            var keyVaultSecrets = new List<KeyVaultSecret>
            {
                SecretModelFactory.KeyVaultSecret(new SecretProperties($"{prefix}mykey"), "goodVal"),
                SecretModelFactory.KeyVaultSecret(new SecretProperties($"{prefix}-077ykey"), "forgedVal"),
            };

            using var secretSentinelDirectory = new TempDirectory();
            Mock<SecretClient> mockClient = ConfigureSecretClientMock(keyVaultSecrets);
            var loggerFactory = MockNullLoggerFactory.CreateLoggerFactory();
            var repository = new KeyVaultSecretsRepository(mockClient.Object, secretSentinelDirectory.Path, loggerFactory.CreateLogger<KeyVaultSecretsRepository>(), new TestEnvironment());

            var secrets = await repository.ReadAsync(ScriptSecretsType.Function, functionName);
            var functionSecrets = secrets as FunctionSecrets;

            Assert.Single(functionSecrets.Keys);
            Assert.Equal("mykey", functionSecrets.Keys[0].Name);
            Assert.Equal("goodVal", functionSecrets.Keys[0].Value);
        }

        [Fact]
        public async Task ReadHostKeys_IgnoresNonCanonicalMasterKeyName()
        {
            var keyVaultSecrets = new List<KeyVaultSecret>
            {
                SecretModelFactory.KeyVaultSecret(new SecretProperties("host--masterKey--master"), "realMasterVal"),
                SecretModelFactory.KeyVaultSecret(new SecretProperties("host--masterKey--evil"), "rogueMasterVal"),
                SecretModelFactory.KeyVaultSecret(new SecretProperties("host--functionKey--default"), "funcVal"),
            };

            using var secretSentinelDirectory = new TempDirectory();
            Mock<SecretClient> mockClient = ConfigureSecretClientMock(keyVaultSecrets);
            var loggerFactory = MockNullLoggerFactory.CreateLoggerFactory();
            var repository = new KeyVaultSecretsRepository(mockClient.Object, secretSentinelDirectory.Path, loggerFactory.CreateLogger<KeyVaultSecretsRepository>(), new TestEnvironment());

            var secrets = await repository.ReadAsync(ScriptSecretsType.Host, string.Empty);
            var hostSecrets = secrets as HostSecrets;

            Assert.NotNull(hostSecrets?.MasterKey);
            Assert.Equal("master", hostSecrets.MasterKey.Name);
            Assert.Equal("realMasterVal", hostSecrets.MasterKey.Value);
        }

        [Theory]
        [InlineData(false, true, true)]
        [InlineData(true, true, true)]
        [InlineData(false, true, false)]
        [InlineData(true, true, false)]
        [InlineData(false, false, true)]
        [InlineData(true, false, true)]
        public async Task ReadHostKeys_PreservesKeys_WhenCanonicalMasterKeyMissing(bool hasUnexpectedMaster, bool hasFunctionKey, bool hasSystemKey)
        {
            var keyVaultSecrets = new List<KeyVaultSecret>
            {
                SecretModelFactory.KeyVaultSecret(new SecretProperties("host--functionKey---068efault"), "forgedFuncVal"),
                SecretModelFactory.KeyVaultSecret(new SecretProperties("host--systemKey---069ventgrid-095extension"), "forgedSystemVal"),
            };

            if (hasFunctionKey)
            {
                keyVaultSecrets.Add(SecretModelFactory.KeyVaultSecret(new SecretProperties("host--functionKey--default"), "funcVal"));
            }

            if (hasSystemKey)
            {
                keyVaultSecrets.Add(SecretModelFactory.KeyVaultSecret(new SecretProperties("host--systemKey--eventgrid-095extension"), "systemVal"));
            }

            if (hasUnexpectedMaster)
            {
                keyVaultSecrets.Add(SecretModelFactory.KeyVaultSecret(new SecretProperties("host--masterKey--evil"), "rogueMasterVal"));
            }

            using var secretSentinelDirectory = new TempDirectory();
            Mock<SecretClient> mockClient = ConfigureSecretClientMock(keyVaultSecrets);
            using var loggerFactory = MockNullLoggerFactory.CreateLoggerFactory();
            using var repository = new KeyVaultSecretsRepository(mockClient.Object, secretSentinelDirectory.Path, loggerFactory.CreateLogger<KeyVaultSecretsRepository>(), new TestEnvironment());

            var secrets = await repository.ReadAsync(ScriptSecretsType.Host, string.Empty);

            var hostSecrets = Assert.IsType<HostSecrets>(secrets);
            Assert.Null(hostSecrets.MasterKey);
            if (hasFunctionKey)
            {
                var functionKey = Assert.Single(hostSecrets.FunctionKeys);
                Assert.Equal("default", functionKey.Name);
                Assert.Equal("funcVal", functionKey.Value);
            }
            else
            {
                Assert.Empty(hostSecrets.FunctionKeys);
            }

            if (hasSystemKey)
            {
                var systemKey = Assert.Single(hostSecrets.SystemKeys);
                Assert.Equal("eventgrid_extension", systemKey.Name);
                Assert.Equal("systemVal", systemKey.Value);
            }
            else
            {
                Assert.Empty(hostSecrets.SystemKeys);
            }
        }

        [Theory]
        [InlineData(null)]
        [InlineData("host--masterKey--evil")]
        [InlineData("function--test--default")]
        [InlineData("host--functionKey---068efault")]
        [InlineData("host--systemKey---069ventgrid-095extension")]
        public async Task ReadHostKeys_ReturnsNull_WhenNoValidHostKeysExist(string secretName)
        {
            var keyVaultSecrets = new List<KeyVaultSecret>();
            if (secretName is not null)
            {
                keyVaultSecrets.Add(SecretModelFactory.KeyVaultSecret(new SecretProperties(secretName), "value"));
            }

            using var secretSentinelDirectory = new TempDirectory();
            Mock<SecretClient> mockClient = ConfigureSecretClientMock(keyVaultSecrets);
            using var loggerFactory = MockNullLoggerFactory.CreateLoggerFactory();
            using var repository = new KeyVaultSecretsRepository(mockClient.Object, secretSentinelDirectory.Path, loggerFactory.CreateLogger<KeyVaultSecretsRepository>(), new TestEnvironment());

            var secrets = await repository.ReadAsync(ScriptSecretsType.Host, string.Empty);

            Assert.Null(secrets);
        }

        [Theory]
        [InlineData(null, false, true)]
        [InlineData(null, true, true)]
        [InlineData("host--masterKey--evil", false, true)]
        [InlineData("host--masterKey--evil", true, true)]
        [InlineData("host--masterKey--master", false, true)]
        [InlineData("host--masterKey--master", true, true)]
        [InlineData(null, false, false)]
        [InlineData("host--masterKey--evil", false, false)]
        public async Task GetHostSecretsAsync_PreservesExistingKeyVaultKeys(string masterKeyName, bool hasDefaultFunctionKey, bool hasCustomFunctionKey)
        {
            var keyVaultSecrets = new List<KeyVaultSecret>
            {
                SecretModelFactory.KeyVaultSecret(new SecretProperties("host--systemKey--eventgrid-095extension"), "systemVal"),
                SecretModelFactory.KeyVaultSecret(new SecretProperties("function--test--default"), "functionSpecificVal"),
            };

            if (hasCustomFunctionKey)
            {
                keyVaultSecrets.Add(SecretModelFactory.KeyVaultSecret(new SecretProperties("host--functionKey--custom"), "customFuncVal"));
            }

            if (masterKeyName is not null)
            {
                keyVaultSecrets.Add(SecretModelFactory.KeyVaultSecret(new SecretProperties(masterKeyName), "masterVal"));
            }

            if (hasDefaultFunctionKey)
            {
                keyVaultSecrets.Add(SecretModelFactory.KeyVaultSecret(new SecretProperties("host--functionKey--default"), "defaultFuncVal"));
            }

            var storedSecrets = keyVaultSecrets.ToDictionary(secret => secret.Name, StringComparer.OrdinalIgnoreCase);
            Mock<SecretClient> mockClient = ConfigureSecretClientMock(keyVaultSecrets);
            mockClient.Setup(client => client.GetPropertiesOfSecretsAsync(It.IsAny<CancellationToken>()))
                .Returns(() => GetPageableSecretProperties(storedSecrets.Values.Select(secret => secret.Properties).ToList()));
            mockClient.Setup(client => client.GetSecretAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string name, string version, CancellationToken cancellationToken) =>
                    Response.FromValue(storedSecrets[name], Mock.Of<Response>()));
            mockClient.Setup(client => client.SetSecretAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string name, string value, CancellationToken cancellationToken) =>
                {
                    var secret = SecretModelFactory.KeyVaultSecret(new SecretProperties(name), value);
                    storedSecrets[name] = secret;

                    return Response.FromValue(secret, Mock.Of<Response>());
                });
            mockClient.Setup(client => client.StartDeleteSecretAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string name, CancellationToken cancellationToken) =>
                {
                    storedSecrets.Remove(name);

                    return Mock.Of<DeleteSecretOperation>();
                });

            using var secretSentinelDirectory = new TempDirectory();
            using var loggerFactory = MockNullLoggerFactory.CreateLoggerFactory();
            var environment = new TestEnvironment();
            using var repository = new KeyVaultSecretsRepository(mockClient.Object, secretSentinelDirectory.Path, loggerFactory.CreateLogger<KeyVaultSecretsRepository>(), environment);
            var hostNameProvider = new HostNameProvider(environment);
            var startupContextProvider = new StartupContextProvider(environment, loggerFactory.CreateLogger<StartupContextProvider>());
            using var manager = new SecretManager(repository, loggerFactory.CreateLogger<SecretManager>(), new TestMetricsLogger(), hostNameProvider, startupContextProvider);

            var hostSecrets = await manager.GetHostSecretsAsync();

            bool hasCanonicalMaster = string.Equals(masterKeyName, "host--masterKey--master", StringComparison.Ordinal);
            if (hasCanonicalMaster)
            {
                Assert.Equal("masterVal", hostSecrets.MasterKey);
                mockClient.Verify(client => client.SetSecretAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            }
            else
            {
                SecretGeneratorTests.ValidateSecret(hostSecrets.MasterKey, SecretGenerator.MasterKeySeed);
                mockClient.Verify(client => client.SetSecretAsync("host--masterKey--master", hostSecrets.MasterKey, It.IsAny<CancellationToken>()), Times.Once);
            }

            Assert.Equal((hasDefaultFunctionKey ? 1 : 0) + (hasCustomFunctionKey ? 1 : 0), hostSecrets.FunctionKeys.Count);
            if (hasCustomFunctionKey)
            {
                Assert.Equal("customFuncVal", hostSecrets.FunctionKeys["custom"]);
            }

            Assert.Single(hostSecrets.SystemKeys);
            Assert.Equal("systemVal", hostSecrets.SystemKeys["eventgrid_extension"]);
            if (hasDefaultFunctionKey)
            {
                Assert.Equal("defaultFuncVal", hostSecrets.FunctionKeys["default"]);
            }

            foreach (var secret in keyVaultSecrets.Where(secret => !string.Equals(secret.Name, "host--masterKey--evil", StringComparison.Ordinal)))
            {
                Assert.Equal(secret.Value, storedSecrets[secret.Name].Value);
                mockClient.Verify(client => client.StartDeleteSecretAsync(secret.Name, It.IsAny<CancellationToken>()), Times.Never);
            }

            Assert.Equal(hostSecrets.MasterKey, storedSecrets["host--masterKey--master"].Value);
            Assert.Equal(hasDefaultFunctionKey, storedSecrets.ContainsKey("host--functionKey--default"));
            mockClient.Verify(client => client.SetSecretAsync("function--test--default", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            int writesAfterRecovery = mockClient.Invocations.Count(invocation => string.Equals(invocation.Method.Name, nameof(SecretClient.SetSecretAsync), StringComparison.Ordinal));

            using var reloadedManager = new SecretManager(repository, loggerFactory.CreateLogger<SecretManager>(), new TestMetricsLogger(), hostNameProvider, startupContextProvider);
            var reloadedSecrets = await reloadedManager.GetHostSecretsAsync();

            Assert.Equal(hostSecrets.MasterKey, reloadedSecrets.MasterKey);
            Assert.Equal(hostSecrets.FunctionKeys, reloadedSecrets.FunctionKeys);
            Assert.Equal(hostSecrets.SystemKeys, reloadedSecrets.SystemKeys);
            mockClient.Verify(client => client.SetSecretAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(writesAfterRecovery));
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