// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Config.Tests
{
    /// <summary>
    /// Tests for the ScriptEnvironmentVariablesConfigurationSource.
    /// </summary>
    /// <remarks>
    /// Not ideal this uses live environment variables, but unavoidable due to the nature of the system under test.
    /// </remarks>
    public class ScriptEnvironmentVariablesConfigurationSourceTests
    {
        private readonly string _key = $"TEST_KEY_{Guid.NewGuid():N}";
        private readonly string _value = $"SOME_TEST_VALUE_{Guid.NewGuid():N}";

        public static TheoryData<string> SpecialCasedPrefixes => new()
        {
            "POSTGRESQLCONNSTR_",
            "APIHUBCONNSTR_",
            "DOCDBCONNSTR_",
            "EVENTHUBCONNSTR_",
            "NOTIFICATIONHUBCONNSTR_",
            "REDISCACHECONNSTR_",
            "SERVICEBUSCONNSTR_",
        };

        [Theory]
        [MemberData(nameof(SpecialCasedPrefixes))]
        public void Load_PrefixedValue_IsAvailableInDataAsIs(string prefix)
        {
            string prefixed = $"{prefix}{_key}";
            using (new EnvironmentVariableScope(prefixed, _value))
            {
                IConfiguration config = BuildConfiguration();
                config[prefixed].Should().Be(_value);
                config.GetConnectionString(_key).Should().Be(_value);
            }
        }

        [Theory]
        [MemberData(nameof(SpecialCasedPrefixes))]
        public void Load_PrefixedValue_IsEnumeratedInDataAsIs(string prefix)
        {
            string prefixed = $"{prefix}{_key}";
            using (new EnvironmentVariableScope(prefixed, _value))
            {
                IConfigurationRoot config = BuildConfiguration();

                // AsEnumerable surfaces entries from the provider's Data dictionary.
                config.AsEnumerable()
                    .Should()
                    .Contain(kvp => kvp.Key == prefixed && kvp.Value == _value);
            }
        }

        [Theory]
        [MemberData(nameof(SpecialCasedPrefixes))]
        public void Load_PrefixedValue_IsEnumeratedInDataAsIs_LiveLoading(string prefix)
        {
            string prefixed = $"{prefix}{_key}";
            using (new EnvironmentVariableScope(prefixed, _value))
            {
                IConfigurationRoot config = BuildConfiguration(liveEnvironmentRead: true);

                // AsEnumerable surfaces entries from the provider's Data dictionary.
                config.AsEnumerable()
                    .Should()
                    .Contain(kvp => kvp.Key == prefixed && kvp.Value == _value);
            }
        }

        [Theory]
        [MemberData(nameof(SpecialCasedPrefixes))]
        public void Load_PrefixIsCaseInsensitive_KeyPreservedAsIs(string prefix)
        {
            string prefixed = $"{prefix.ToLowerInvariant()}Test_{Guid.NewGuid():N}";
            using (new EnvironmentVariableScope(prefixed, _value))
            {
                IConfigurationRoot config = BuildConfiguration();

                KeyValuePair<string, string> entry = config.AsEnumerable()
                    .FirstOrDefault(kvp => string.Equals(kvp.Key, prefixed, StringComparison.OrdinalIgnoreCase));

                entry.Key.Should().BeEquivalentTo(prefixed);
                entry.Value.Should().Be(_value);
            }
        }

        [Fact]
        public void Load_NonSpecialCasedPrefixedValue_NotAddedByOverride()
        {
            // Sanity check: variables without one of the special-cased prefixes are not added
            // by the override's logic (they may still be added by the base provider).
            string prefixed = $"NOT_SPECIAL_{Guid.NewGuid():N}";
            using (new EnvironmentVariableScope(prefixed, _value))
            {
                IConfigurationRoot config = BuildConfiguration();

                // The base provider still surfaces arbitrary env vars, so the value should be
                // resolvable through TryGet; the assertion confirms our override doesn't break that.
                config[prefixed].Should().Be(_value);
            }
        }

        [Fact]
        public void LiveRead_TryGet_LiveValuePresent_PrecedesData()
        {
            // Live read should always win over Data when the env var is present with the exact key.
            string key = $"TEST_LIVE_{Guid.NewGuid():N}";
            using (new EnvironmentVariableScope(key, "from-env"))
            {
                IConfigurationProvider provider = BuildProvider(liveEnvironmentRead: true);

                // Mutate Data to a different value via Set to prove live read wins.
                provider.Set(key, "from-data");
                Environment.SetEnvironmentVariable(key, "from-env-after-set");

                provider.TryGet(key, out string value).Should().BeTrue();
                value.Should().Be("from-env-after-set");
            }
        }

        [Fact]
        public void LiveRead_TryGet_ColonKey_FallsBackToNormalizedUnderscoreEnv()
        {
            // Caller asks via "Section:Key"; env var is set as "Section__Key".
            string suffix = Guid.NewGuid().ToString("N");
            string envKey = $"TEST_SECTION__{suffix}";
            string colonKey = $"TEST_SECTION:{suffix}";

            using (new EnvironmentVariableScope(envKey, _value))
            {
                IConfigurationProvider provider = BuildProvider(liveEnvironmentRead: true);

                provider.TryGet(colonKey, out string value).Should().BeTrue();
                value.Should().Be(_value);
            }
        }

        [Fact]
        public void LiveRead_TryGet_AbsentEverywhere_ReturnsFalse()
        {
            IConfigurationProvider provider = BuildProvider(liveEnvironmentRead: true);

            string key = $"TEST_MISSING_{Guid.NewGuid():N}";
            provider.TryGet(key, out string value).Should().BeFalse();
            value.Should().BeNull();
        }

        [Fact]
        public void LiveRead_TryGet_CaseMismatchLiveMisses_FallsBackToData()
        {
            // Regression for the Linux case-sensitivity bug. Env vars set with one case may not
            // be resolvable via a live read with different case (Linux env is case-sensitive at
            // the OS layer). The cached Data dictionary uses OrdinalIgnoreCase, so the fallback
            // should resolve the value regardless of OS.
            string suffix = Guid.NewGuid().ToString("N");
            string envKey = $"TEST_SECTION__lower_{suffix}";

            using (new EnvironmentVariableScope(envKey, _value))
            {
                IConfigurationProvider provider = BuildProvider(liveEnvironmentRead: true);

                // Request the key with mismatched casing using the colon form. On Linux this
                // simulates the production failure mode (AzureWebJobsStorage:AccountName vs
                // AzureWebJobsStorage__accountName). On Windows the live read may already hit
                // due to OS case-insensitivity; either way the value must resolve.
                string requestedKey = $"TEST_SECTION:LOWER_{suffix}";
                provider.TryGet(requestedKey, out string value).Should().BeTrue();
                value.Should().Be(_value);
            }
        }

        [Fact]
        public void LiveRead_TryGet_EnvDeletedAfterLoad_FallsBackToCachedData()
        {
            // Documents intentional staleness: once Load snapshots the env var into Data,
            // deleting the env var leaves the cached value visible via the fallback path
            // until the next Reload. Acceptable trade-off because specialization adds
            // env vars rather than removing them.
            string key = $"TEST_DELETE_{Guid.NewGuid():N}";
            Environment.SetEnvironmentVariable(key, _value);
            try
            {
                IConfigurationProvider provider = BuildProvider(liveEnvironmentRead: true);
                Environment.SetEnvironmentVariable(key, null);

                provider.TryGet(key, out string value).Should().BeTrue();
                value.Should().Be(_value);
            }
            finally
            {
                Environment.SetEnvironmentVariable(key, null);
            }
        }

        [Fact]
        public void LiveRead_Set_WritesBothEnvironmentAndData()
        {
            string key = $"TEST_SET_{Guid.NewGuid():N}";
            try
            {
                IConfigurationProvider provider = BuildProvider(liveEnvironmentRead: true);
                provider.Set(key, _value);

                Environment.GetEnvironmentVariable(key).Should().Be(_value);

                // Clear env and confirm Data still has the value (proves Data write happened).
                Environment.SetEnvironmentVariable(key, null);
                provider.TryGet(key, out string value).Should().BeTrue();
                value.Should().Be(_value);
            }
            finally
            {
                Environment.SetEnvironmentVariable(key, null);
            }
        }

        [Fact]
        public void Cached_Load_DoesNotSeeEnvChangesUntilReload()
        {
            string key = $"TEST_CACHED_{Guid.NewGuid():N}";
            Environment.SetEnvironmentVariable(key, "initial");
            try
            {
                IConfigurationRoot config = BuildConfiguration(liveEnvironmentRead: false);
                config[key].Should().Be("initial");

                Environment.SetEnvironmentVariable(key, "updated");
                config[key].Should().Be("initial", "cached provider should not see env changes without Reload");

                config.Reload();
                config[key].Should().Be("updated");
            }
            finally
            {
                Environment.SetEnvironmentVariable(key, null);
            }
        }

        [Fact]
        public void Cached_Reload_RemovesDeletedEnvVarsFromSnapshot()
        {
            string key = $"TEST_REMOVE_{Guid.NewGuid():N}";
            Environment.SetEnvironmentVariable(key, _value);
            try
            {
                IConfigurationRoot config = BuildConfiguration(liveEnvironmentRead: false);
                config[key].Should().Be(_value);

                Environment.SetEnvironmentVariable(key, null);
                config.Reload();
                config[key].Should().BeNull();
            }
            finally
            {
                Environment.SetEnvironmentVariable(key, null);
            }
        }

        private static IConfigurationRoot BuildConfiguration(bool liveEnvironmentRead = false)
        {
            var builder = new ConfigurationBuilder();
            builder.Add(new ScriptEnvironmentVariablesConfigurationSource(liveEnvironmentRead));
            return builder.Build();
        }

        private static IConfigurationProvider BuildProvider(bool liveEnvironmentRead)
        {
            var source = new ScriptEnvironmentVariablesConfigurationSource(liveEnvironmentRead);
            var provider = source.Build(new ConfigurationBuilder());
            provider.Load();
            return provider;
        }

        private sealed class EnvironmentVariableScope : IDisposable
        {
            private readonly string _key;

            public EnvironmentVariableScope(string key, string value)
            {
                _key = key;
                Environment.SetEnvironmentVariable(key, value);
            }

            public void Dispose()
            {
                Environment.SetEnvironmentVariable(_key, null);
            }
        }
    }
}
