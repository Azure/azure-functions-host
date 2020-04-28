// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Script.Binding;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Configuration
{
    public class HttpOptionsSetup : IConfigureOptions<HttpOptions>
    {
        internal const int DefaultMaxConcurrentRequests = 100;
        internal const int DefaultMaxOutstandingRequests = 200;
        private readonly IEnvironment _environment;

        public HttpOptionsSetup(IEnvironment environment)
        {
            _environment = environment;
        }

        public void Configure(HttpOptions options)
        {
            if (_environment.IsWindowsConsumption())
            {
                // when running under dynamic, we choose some default
                // throttle settings.
                // these can be overridden by the user in host.json
                if (_environment.IsAppService())
                {
                    // dynamic throttles are based on sandbox counters
                    // which only exist in AppService
                    options.DynamicThrottlesEnabled = true;
                }
                options.MaxConcurrentRequests = DefaultMaxConcurrentRequests;
                options.MaxOutstandingRequests = DefaultMaxOutstandingRequests;
            }

            if (_environment.IsV2CompatibilityMode())
            {
                options.SetResponse = HttpBinding.LegacySetResponse;
            }
            else
            {
                options.SetResponse = HttpBinding.SetResponse;
            }
        }
    }
}
