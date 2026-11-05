// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.WebHost.Configuration;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Configuration;

public sealed class FlexConsumptionMetricsPublisherOptionsValidatorTests
{
    [Theory]
    [InlineData(true, -1, false)]
    [InlineData(true, 0, false)]
    [InlineData(true, 1, true)]
    [InlineData(false, -1, true)]
    [InlineData(false, 0, true)]
    public void Validate_RequiresPositiveRetentionOnlyForWorkerAttribution(bool workerAttribution, int seconds, bool succeeds)
    {
        var options = new FlexConsumptionMetricsPublisherOptions
        {
            UseWorkerAttribution = workerAttribution,
            WorkerMetricsFileRetentionPeriod = TimeSpan.FromSeconds(seconds)
        };
        var validator = new FlexConsumptionMetricsPublisherOptionsValidator();

        ValidateOptionsResult result = validator.Validate(Options.DefaultName, options);

        Assert.Equal(succeeds, result.Succeeded);
        if (!succeeds)
        {
            Assert.Contains(nameof(options.WorkerMetricsFileRetentionPeriod), result.FailureMessage, StringComparison.Ordinal);
        }
    }
}
