// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.Diagnostics.OpenTelemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenTelemetry.Resources;

namespace Microsoft.Azure.WebJobs.Script.Config
{
    internal class OpenTelemetryResourceSetUp(IServiceCollection services) : IPostConfigureOptions<ScriptJobHostOptions>
    {
        public void PostConfigure(string name, ScriptJobHostOptions options)
        {
            if (options.TelemetryMode is TelemetryMode.OpenTelemetry)
            {
                var instanceId = options?.InstanceId;
                if (!string.IsNullOrWhiteSpace(instanceId))
                {
                    services.AddOpenTelemetry().ConfigureResource(r => r.AddAttributes([new(ScriptConstants.LogPropertyHostInstanceIdKey, instanceId)]));
                }
            }
        }
    }
}