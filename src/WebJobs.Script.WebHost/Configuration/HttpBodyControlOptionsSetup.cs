// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Configuration
{
    internal class HttpBodyControlOptionsSetup : IConfigureOptions<HttpBodyControlOptions>
    {
        private readonly IEnvironment _environment;

        public HttpBodyControlOptionsSetup(IEnvironment environment)
        {
            _environment = environment;
        }

        public void Configure(HttpBodyControlOptions options)
        {
            options.AllowSynchronousIO = _environment.IsV2CompatibilityMode()
                || FeatureFlags.IsEnabled(ScriptConstants.FeatureFlagAllowSynchronousIO, _environment);
        }
    }
}