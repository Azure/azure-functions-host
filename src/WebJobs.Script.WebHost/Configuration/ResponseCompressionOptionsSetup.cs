// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Configuration
{
    internal sealed class ResponseCompressionOptionsSetup(IEnvironment environment) : IConfigureOptions<ResponseCompressionOptions>
    {
        public void Configure(ResponseCompressionOptions options)
        {
            options.EnableResponseCompression = FeatureFlags.IsEnabled(ScriptConstants.FeatureFlagEnableResponseCompression, environment);
        }
    }
}
