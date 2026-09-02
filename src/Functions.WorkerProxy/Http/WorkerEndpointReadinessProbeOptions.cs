// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Azure.Functions.WorkerProxy.DataAnnotations;

namespace Azure.Functions.WorkerProxy.Http;

/// <summary>
/// Configures worker HTTP endpoint readiness probing.
/// </summary>
internal sealed class WorkerEndpointReadinessProbeOptions
{
    /// <summary>
    /// The configuration section containing readiness probe settings.
    /// </summary>
    public const string SectionName = WorkerProxyOptions.SectionName + ":EndpointReadinessProbe";

    /// <summary>
    /// Gets or sets the delay between readiness attempts.
    /// </summary>
    [PositiveTimeSpan]
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromMilliseconds(25);

    /// <summary>
    /// Gets or sets the total readiness deadline.
    /// </summary>
    [PositiveTimeSpan]
    public TimeSpan TotalTimeout { get; set; } = TimeSpan.FromSeconds(5);
}
