// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Configuration
{
    internal sealed class ScriptHostRecycleOptionsSetup : IConfigureOptions<ScriptHostRecycleOptions>
    {
        private readonly IConfiguration _configuration;

        public ScriptHostRecycleOptionsSetup(IConfiguration configuration)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }

        public void Configure(ScriptHostRecycleOptions options)
        {
            options.Configure(_configuration);
        }
    }
}
