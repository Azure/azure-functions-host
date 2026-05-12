// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.Functions.WorkerProxy.Configuration;

internal sealed class WorkerProxyEnvironmentOptions
{
    internal const string FileLoggingEnabledSettingName = "FUNCTIONS_WORKER_PROXY_FILE_LOGGING_ENABLED";

    public string ContainerName { get; set; } = string.Empty;

    public string StampName { get; set; } = string.Empty;

    public string TenantId { get; set; } = string.Empty;

    public string LegionServiceHost { get; set; } = string.Empty;

    public string ComputerName { get; set; } = string.Empty;

    public bool IsFileLoggingEnabled { get; set; }

    public bool IsFlexOrLegion =>
        !string.IsNullOrWhiteSpace(ContainerName) || !string.IsNullOrWhiteSpace(LegionServiceHost);
}
