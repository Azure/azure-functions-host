// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.Functions.WorkerProxy.Supervisor;

internal sealed class WorkerProxySupervisorLogContext
{
    public WorkerProxySupervisorLogContext(string hostVersion, string containerName, string stampName, string tenantId)
    {
        HostVersion = hostVersion ?? string.Empty;
        ContainerName = containerName ?? string.Empty;
        StampName = stampName ?? string.Empty;
        TenantId = tenantId ?? string.Empty;
    }

    public string HostVersion { get; }

    public string ContainerName { get; }

    public string StampName { get; }

    public string TenantId { get; }

    public static WorkerProxySupervisorLogContext FromEnvironment()
        => new(
            Environment.GetEnvironmentVariable("HOST_VERSION") ?? string.Empty,
            (Environment.GetEnvironmentVariable("CONTAINER_NAME") ?? string.Empty).ToUpperInvariant(),
            (Environment.GetEnvironmentVariable("WEBSITE_HOME_STAMPNAME") ?? string.Empty).ToLowerInvariant(),
            (Environment.GetEnvironmentVariable("WEBSITE_STAMP_DEPLOYMENT_ID") ?? string.Empty).ToLowerInvariant());
}
