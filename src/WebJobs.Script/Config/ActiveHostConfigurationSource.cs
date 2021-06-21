// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Microsoft.Azure.WebJobs.Script.Configuration
{
    public class ActiveHostConfigurationSource : IConfigurationSource
    {
        private readonly IServiceProvider _serviceProvider;

        public ActiveHostConfigurationSource(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public IConfigurationProvider Build(IConfigurationBuilder builder)
        {
            return new ActiveHostConfigurationProvider(_serviceProvider);
        }

        private class ActiveHostConfigurationProvider : ConfigurationProvider
        {
            private readonly IServiceProvider _serviceProvider;

            public ActiveHostConfigurationProvider(IServiceProvider serviceProvider)
            {
                _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            }

            public override bool TryGet(string key, out string value)
            {
                if (_serviceProvider?.GetService(typeof(IConfiguration)) is IConfigurationRoot activeHostConfiguration)
                {
                    return (value = activeHostConfiguration.GetValue<string>(key)) != null;
                }

                value = default;
                return false;
            }

            public override IEnumerable<string> GetChildKeys(IEnumerable<string> earlierKeys, string parentPath)
            {
                var keys = new HashSet<string>();
                if (_serviceProvider?.GetService(typeof(IConfiguration)) is IConfigurationRoot activeHostConfiguration)
                {
                    foreach (var config in activeHostConfiguration.GetSection(parentPath).GetChildren())
                    {
                        keys.Add(config.Key);
                    }
                }

                keys.UnionWith(earlierKeys);
                return keys;
            }
        }
    }
}
