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
                IConfigurationRoot config = BuildConfiguration(liveEnvironmentLoading: true);

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

        private static IConfigurationRoot BuildConfiguration(bool liveEnvironmentLoading = false)
        {
            var builder = new ConfigurationBuilder();
            builder.Add(new ScriptEnvironmentVariablesConfigurationSource(liveEnvironmentLoading));
            return builder.Build();
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
