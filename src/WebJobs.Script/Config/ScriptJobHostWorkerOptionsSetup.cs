// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.Workers.Http;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Configuration
{
    internal class ScriptJobHostWorkerOptionsSetup : IConfigureOptions<ScriptJobHostWorkerOptions>
    {
        private readonly IOptions<HttpWorkerOptions> _httpWorkerOptions;

        public ScriptJobHostWorkerOptionsSetup(IOptions<HttpWorkerOptions> httpWorkerOptions)
        {
            _httpWorkerOptions = httpWorkerOptions;
        }

        public void Configure(ScriptJobHostWorkerOptions options)
        {
            options.SequentialHostRestartRequired = _httpWorkerOptions.Value.IsUserSpecifiedPort;
        }
    }
}
