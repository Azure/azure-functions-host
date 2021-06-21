// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Microsoft.Azure.WebJobs.Script.Configuration
{
    public class ActiveHostConfigurationSource : IConfigurationSource
    {
        public ActiveHostConfigurationSource(IServiceProvider serviceProvider)
        {
            ServiceProvider = serviceProvider;
        }

        public IServiceProvider ServiceProvider { get; set; }

        public IConfigurationProvider Build(IConfigurationBuilder builder)
        {
            return new ActiveHostConfigurationProvider(this);
        }

        private class ActiveHostConfigurationProvider : ConfigurationProvider
        {
            private readonly ActiveHostConfigurationSource _configurationSource;

            public ActiveHostConfigurationProvider(ActiveHostConfigurationSource configurationSource)
            {
                _configurationSource = configurationSource ?? throw new ArgumentNullException(nameof(configurationSource));
            }

            public override bool TryGet(string key, out string value)
            {
                if (_configurationSource?.ServiceProvider?.GetService(typeof(IConfiguration)) is IConfigurationRoot activeHostConfiguration)
                {
                    return
                        (value = activeHostConfiguration.GetValue<string>(key)) != null ||
                        (value = activeHostConfiguration.GetValue<string>(NormalizeKey(key))) != null;
                }

                value = default;
                return false;
            }

            public override IEnumerable<string> GetChildKeys(IEnumerable<string> earlierKeys, string parentPath)
            {
                var activeHostConfiguration = _configurationSource?.ServiceProvider?.GetService(typeof(IConfiguration)) as IConfigurationRoot;
                var keys = new HashSet<string>();

                if (activeHostConfiguration != null)
                {
                    foreach (var config in activeHostConfiguration.GetSection(parentPath).GetChildren())
                    {
                        keys.Add(config.Key);
                    }
                }

                keys.UnionWith(earlierKeys);
                return keys;
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
