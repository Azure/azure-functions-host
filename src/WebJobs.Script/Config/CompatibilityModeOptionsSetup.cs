// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Configuration
{
    internal class CompatibilityModeOptionsSetup : IConfigureOptions<CompatibilityModeOptions>
    {
        private readonly IEnvironment _environment;

        public CompatibilityModeOptionsSetup(IEnvironment environment)
        {
            _environment = environment;
        }

        public void Configure(CompatibilityModeOptions options)
        {
            options.IsV2CompatibilityModeEnabled = _environment.IsV2CompatibilityMode();
        }
    }
}
