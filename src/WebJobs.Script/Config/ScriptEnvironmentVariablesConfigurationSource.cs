// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;

namespace Microsoft.Azure.WebJobs.Script
{
    /// <summary>
    /// This source exists to replace the default <see cref="EnvironmentVariablesConfigurationSource"/> to allow
    /// us to override caching behavior.
    /// </summary>
    public class ScriptEnvironmentVariablesConfigurationSource : IConfigurationSource
    {
        public IConfigurationProvider Build(IConfigurationBuilder builder)
        {
            return new WebHostEnvironmentVariablesProvider();
        }

        private class WebHostEnvironmentVariablesProvider : EnvironmentVariablesConfigurationProvider
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
