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
    private const string WebsitePodName = "WEBSITE_POD_NAME";
    private const string ContainerName = "CONTAINER_NAME";

    /// <inheritdoc />
    public void Configure(WorkerProxyOptions options)
    {
        string? platformPodName = configuration[WebsitePodName];
        options.PodName = string.IsNullOrWhiteSpace(platformPodName)
            ? configuration[ContainerName] ?? string.Empty
            : platformPodName;
    }
}
