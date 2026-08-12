// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Extensions.Configuration;

namespace Azure.Functions.WorkerProxy.Configuration;

internal sealed class WorkerProxyOptions
{
    internal const int DefaultManagementPort = 80;
    internal const string ManagementPortCommandLineName = "--management-port";
    internal const string ManagementPortConfigurationKey = "MANAGEMENT_PORT";

    [ConfigurationKeyName(ManagementPortConfigurationKey)]
    public int ManagementPort { get; set; } = DefaultManagementPort;
}
