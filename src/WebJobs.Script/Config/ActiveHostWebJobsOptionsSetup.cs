// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Config
{
    internal class ActiveHostWebJobsOptionsSetup<TOptions> : IConfigureOptions<TOptions> where TOptions : class
    {
        private readonly IConfiguration _configuration;

        public ActiveHostWebJobsOptionsSetup(IScriptHostManager scriptHostManager, IConfiguration configuration)
        {
            if (scriptHostManager == null)
            {
                throw new ArgumentNullException(nameof(scriptHostManager));
            }

            _configuration = new ConfigurationBuilder()
                    .Add(new ActiveHostConfigurationSource(scriptHostManager))
                    .AddConfiguration(configuration)
                    .Build();
        }

        public void Configure(TOptions options)
        {
            IConfiguration rootSection = _configuration.GetWebJobsRootConfiguration();
            rootSection.Bind(options);
        }
    }
}
