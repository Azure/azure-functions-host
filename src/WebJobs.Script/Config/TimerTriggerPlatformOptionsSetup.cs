// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Configuration;

internal sealed class TimerTriggerPlatformOptionsSetup : IConfigureOptions<TimerTriggerPlatformOptions>
{
    private readonly IEnvironment _environment;

    public TimerTriggerPlatformOptionsSetup(IEnvironment environment)
    {
        _environment = environment;
    }

    public void Configure(TimerTriggerPlatformOptions options)
    {
        if (_environment.IsLinuxConsumptionOnAtlas() || _environment.IsLinuxConsumptionOnLegion())
        {
            // These platforms technically allow constant expressions today, but the scale
            // controller does not support them. Warn customers so they can migrate to CRON.
            options.NonCronScheduleBehavior = NonCronScheduleBehavior.Warn;
        }
        else if (_environment.IsConsumptionSku())
        {
            // The scale controller on consumption plans only supports CRON expressions,
            // so constant expressions (e.g. TimeSpan) are not allowed.
            options.NonCronScheduleBehavior = NonCronScheduleBehavior.Error;
        }
    }
}
