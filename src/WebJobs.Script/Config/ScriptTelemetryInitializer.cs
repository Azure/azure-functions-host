// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Config
{
    internal class ScriptTelemetryInitializer : ITelemetryInitializer
    {
        private readonly ScriptJobHostOptions _hostOptions;

        public ScriptTelemetryInitializer(IOptions<ScriptJobHostOptions> hostOptions)
        {
            if (hostOptions == null)
            {
                throw new ArgumentNullException(nameof(hostOptions));
            }

            if (hostOptions.Value == null)
            {
                throw new ArgumentNullException(nameof(hostOptions.Value));
            }

            _hostOptions = hostOptions.Value;
        }

        public void Initialize(ITelemetry telemetry)
        {
            telemetry?.Context?.Properties?.Add(ScriptConstants.LogPropertyHostInstanceIdKey, _hostOptions.InstanceId);
        }
    }
}
