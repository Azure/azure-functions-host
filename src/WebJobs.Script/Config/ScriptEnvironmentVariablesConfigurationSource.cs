// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;

namespace Microsoft.Azure.WebJobs.Script
{
    /// <summary>
    /// This source exists to replace the default <see cref="EnvironmentVariablesConfigurationSource"/> to allow
    /// us to override caching behavior.
    /// </summary>
    /// <param name="liveEnvironmentLoading">
    /// If true, the provider will use the live environment variables when fetching values.
    /// If false, it will cache environment variables one time on load.
    /// </param>
    /// <remarks>
    /// Live environment fetching is the existing behavior we use in the WebHost so that post-specialization
    /// changes to the environment are reflected in the configuration.
    /// </remarks>
    public class ScriptEnvironmentVariablesConfigurationSource(bool liveEnvironmentLoading = true) : IConfigurationSource
    {
        public IConfigurationProvider Build(IConfigurationBuilder builder)
        {
            return liveEnvironmentLoading
                ? new WebHostEnvironmentVariablesProvider() : new CompatEnvironmentVariablesProvider();
        }

        private class CompatEnvironmentVariablesProvider : EnvironmentVariablesConfigurationProvider
        {
            // Values taken from https://github.com/dotnet/runtime/blob/46e21499a8f230988fac59b694af407a86e0f46c/src/libraries/Microsoft.Extensions.Configuration.EnvironmentVariables/src/EnvironmentVariablesConfigurationProvider.cs#L18
            // These should be kept in sync with any values added AFTER .NET8
            private const string PostgreSqlServerPrefix = "POSTGRESQLCONNSTR_";
            private const string ApiHubPrefix = "APIHUBCONNSTR_";
            private const string DocDbPrefix = "DOCDBCONNSTR_";
            private const string EventHubPrefix = "EVENTHUBCONNSTR_";
            private const string NotificationHubPrefix = "NOTIFICATIONHUBCONNSTR_";
            private const string RedisCachePrefix = "REDISCACHECONNSTR_";
            private const string ServiceBusPrefix = "SERVICEBUSCONNSTR_";

            public CompatEnvironmentVariablesProvider() : base()
            {
            }

            public override void Load()
            {
                base.Load();

                static string Normalize(string key)
                {
                    return key.Replace("__", ConfigurationPath.KeyDelimiter);
                }

                static bool IsPrefixedString(string key)
                {
                    return key.StartsWith(PostgreSqlServerPrefix, StringComparison.OrdinalIgnoreCase) ||
                           key.StartsWith(ApiHubPrefix, StringComparison.OrdinalIgnoreCase) ||
                           key.StartsWith(DocDbPrefix, StringComparison.OrdinalIgnoreCase) ||
                           key.StartsWith(EventHubPrefix, StringComparison.OrdinalIgnoreCase) ||
                           key.StartsWith(NotificationHubPrefix, StringComparison.OrdinalIgnoreCase) ||
                           key.StartsWith(RedisCachePrefix, StringComparison.OrdinalIgnoreCase) ||
                           key.StartsWith(ServiceBusPrefix, StringComparison.OrdinalIgnoreCase);
                }

                // .NET 10 changed to special case some prefixes, inserting it into the "ConnectionStrings" section.
                // For backwards compatibility, we still want to include these in the configuration.
                foreach (DictionaryEntry kvp in Environment.GetEnvironmentVariables())
                {
                    if (kvp.Key is string key && kvp.Value is string value)
                    {
                        if (IsPrefixedString(key))
                        {
                            Data[Normalize(key)] = value;
                        }
                    }
                }
            }
        }

        private class WebHostEnvironmentVariablesProvider : CompatEnvironmentVariablesProvider
        {
            public WebHostEnvironmentVariablesProvider() : base()
            {
            }

            public override bool TryGet(string key, out string value)
            {
                return
                    (value = Environment.GetEnvironmentVariable(key)) != null ||
                    (value = Environment.GetEnvironmentVariable(NormalizeKey(key))) != null;
            }

            public override void Set(string key, string value)
            {
                Environment.SetEnvironmentVariable(key, value);
            }

            private static string NormalizeKey(string key)
            {
                // For hierarchical config values specified in environment variables,
                // a colon(:) may not work on all platforms. Double underscore(__) is
                // supported by all platforms.
                return key.Replace(ConfigurationPath.KeyDelimiter, "__");
            }
        }
    }
}
