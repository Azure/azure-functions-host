// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.Workers.Http;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Configuration
{
    internal class ScriptHostWorkerOptionsSetup : IConfigureOptions<ScriptHostWorkerOptions>
    {
        private readonly IOptions<HttpWorkerOptions> _httpWorkerOptions;

        public ScriptHostWorkerOptionsSetup(IOptions<HttpWorkerOptions> httpWorkerOptions)
        {
            _httpWorkerOptions = httpWorkerOptions ?? throw new ArgumentNullException(nameof(httpWorkerOptions));
        }

        public void Configure(ScriptHostWorkerOptions options)
        {
            // Enforcing sequential host restarts when a user-specified custom handler port is configured to prevent multiple processes from attempting to bind to the same port concurrently.
            options.SequentialHostRestartRequired = _httpWorkerOptions.Value?.IsConfigSpecifiedPort ?? false;
        }
    }
}
