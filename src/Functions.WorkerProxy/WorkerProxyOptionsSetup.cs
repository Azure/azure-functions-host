// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Azure.Functions.WorkerProxy;

/// <summary>
/// Configures the platform-provided WorkerProxy pod identity.
/// </summary>
internal sealed class WorkerProxyOptionsSetup(IConfiguration configuration) : IConfigureOptions<WorkerProxyOptions>
{
    /// <inheritdoc />
    public void Configure(WorkerProxyOptions options)
    {
        string? platformPodName = configuration["WEBSITE_POD_NAME"];
        options.PodName = string.IsNullOrWhiteSpace(platformPodName)
            ? configuration["CONTAINER_NAME"] ?? string.Empty
            : platformPodName;
    }
}
