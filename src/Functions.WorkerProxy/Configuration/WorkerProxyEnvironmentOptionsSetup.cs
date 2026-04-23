// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.Functions.WorkerProxy.Configuration;

internal sealed class WorkerProxyEnvironmentOptionsSetup : IConfigureOptions<WorkerProxyEnvironmentOptions>
{
    private readonly IConfiguration _configuration;

    public WorkerProxyEnvironmentOptionsSetup(IConfiguration configuration)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    public void Configure(WorkerProxyEnvironmentOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.ContainerName = NormalizeContainerName(_configuration["CONTAINER_NAME"]);
        options.StampName = NormalizeStampName(_configuration["WEBSITE_HOME_STAMPNAME"]);
        options.TenantId = NormalizeTenantId(_configuration["WEBSITE_STAMP_DEPLOYMENT_ID"]);
        options.LegionServiceHost = _configuration["LEGION_SERVICE_HOST"] ?? string.Empty;
        options.ComputerName = _configuration["COMPUTERNAME"] ?? string.Empty;
    }

    internal static string NormalizeContainerName(string? containerName) => containerName?.ToUpperInvariant() ?? string.Empty;

    internal static string NormalizeStampName(string? stampName) => stampName?.ToLowerInvariant() ?? string.Empty;

    internal static string NormalizeTenantId(string? tenantId) => tenantId?.ToLowerInvariant() ?? string.Empty;
}
