// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
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
                var activeHostConfiguration = _configurationSource?.ServiceProvider?.GetService(typeof(IConfiguration)) as IConfigurationRoot;

                if (activeHostConfiguration != null)
                {
                    value = activeHostConfiguration.GetValue<string>(key);
                    return value != null;
                }

                value = default;
                return false;
            }
        }
    }
}
