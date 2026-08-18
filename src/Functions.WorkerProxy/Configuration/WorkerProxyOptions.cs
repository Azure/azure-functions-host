// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Extensions.Configuration;

namespace Azure.Functions.WorkerProxy.Configuration;

internal sealed class WorkerProxyOptions
{
    internal const int DefaultManagementPort = 80;
    internal const string ManagementPortCommandLineName = "--management-port";
    internal const string ManagementPortConfigurationKey = "MANAGEMENT_PORT";
    internal const string AppContentRootConfigurationKey = "APP_CONTENT_ROOT";

    [ConfigurationKeyName(ManagementPortConfigurationKey)]
    public int ManagementPort { get; set; } = DefaultManagementPort;

    /// <summary>
    /// Root directory containing the customer's app content (extensions.json, .azurefunctions/, etc.).
    /// </summary>
    [ConfigurationKeyName(AppContentRootConfigurationKey)]
    public string? AppContentRoot { get; set; }
}
