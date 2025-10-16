// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.Workers.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Configuration
{
    internal sealed class ScriptHostRecycleOptionsSetup : IConfigureOptions<ScriptHostRecycleOptions>
    {
        private readonly IOptions<HttpWorkerOptions> _httpWorkerOptions;
        private readonly IConfiguration _configuration;

        public ScriptHostRecycleOptionsSetup(
            IOptions<HttpWorkerOptions> httpWorkerOptions, IConfiguration configuration)
        {
            _httpWorkerOptions = httpWorkerOptions ?? throw new ArgumentNullException(nameof(httpWorkerOptions));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }

        public void Configure(ScriptHostRecycleOptions options)
        {
            options.Configure(_configuration);

            // Enforcing sequential host restarts when a user-specified custom handler port is configured to prevent multiple processes from attempting to bind to the same port concurrently.
            if (_httpWorkerOptions.Value.IsPortManuallySet)
            {
                options.SequentialHostRestartRequired = true;
            }
        }
    }
}
