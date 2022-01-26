// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Host.Scale;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public class ConcurrencyOptionProvider : IConcurrencyOptionProvider
    {
        private readonly IServiceProvider _serviceProvider;

        public ConcurrencyOptionProvider(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public JObject GetConcurrencyOption()
        {
            var configuration = _serviceProvider.GetService<IConfiguration>();
            var concurrencyOption = new ConcurrencyOptions();
            var key = ConfigurationSectionNames.JobHost + ConfigurationPath.KeyDelimiter
                    + "concurrency";
            configuration.Bind(key, concurrencyOption);
            return JObject.FromObject(concurrencyOption);
        }
    }
}
