// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Configuration;

internal sealed class FlexConsumptionMetricsPublisherOptionsValidator : IValidateOptions<FlexConsumptionMetricsPublisherOptions>
{
    public ValidateOptionsResult Validate(string name, FlexConsumptionMetricsPublisherOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.UseWorkerAttribution && options.WorkerMetricsFileRetentionPeriod <= TimeSpan.Zero)
        {
            return ValidateOptionsResult.Fail($"{nameof(options.WorkerMetricsFileRetentionPeriod)} must be positive when {nameof(options.UseWorkerAttribution)} is enabled.");
        }

        return ValidateOptionsResult.Success;
    }
}
