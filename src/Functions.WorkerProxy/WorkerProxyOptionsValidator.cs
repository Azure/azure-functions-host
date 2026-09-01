// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace Azure.Functions.WorkerProxy;

/// <summary>
/// Validates WorkerProxy listener port ranges and uniqueness.
/// </summary>
internal sealed class WorkerProxyOptionsValidator : IValidateOptions<WorkerProxyOptions>
{
    private const int MaximumPort = 65535;

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, WorkerProxyOptions options)
    {
        List<string> failures = [];
        ValidatePort(options.ManagementPort, nameof(options.ManagementPort), failures);
        ValidatePort(options.RuntimeGrpcPort, nameof(options.RuntimeGrpcPort), failures);
        ValidatePort(options.WorkerGrpcPort, nameof(options.WorkerGrpcPort), failures);

        HashSet<int> configuredPorts = [];
        AddDistinctPort(options.ManagementPort, nameof(options.ManagementPort), configuredPorts, failures);
        AddDistinctPort(options.RuntimeGrpcPort, nameof(options.RuntimeGrpcPort), configuredPorts, failures);
        AddDistinctPort(options.WorkerGrpcPort, nameof(options.WorkerGrpcPort), configuredPorts, failures);

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void AddDistinctPort(int port, string propertyName, HashSet<int> configuredPorts, List<string> failures)
    {
        if (port != 0 && !configuredPorts.Add(port))
        {
            failures.Add($"{propertyName} must use a different port from the other WorkerProxy listeners.");
        }
    }

    private static void ValidatePort(int port, string propertyName, List<string> failures)
    {
        if (port is < 0 or > MaximumPort)
        {
            failures.Add($"{propertyName} must be between 0 and {MaximumPort}.");
        }
    }
}
