// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.Functions.WorkerProxy.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.Functions.WorkerProxy.Diagnostics;

internal sealed class MsFunctionLogsLoggerProvider : ILoggerProvider
{
    private readonly string _containerName;
    private readonly string _stampName;
    private readonly string _tenantId;
    private readonly Action<string> _writeLine;

    public MsFunctionLogsLoggerProvider(IOptions<WorkerProxyEnvironmentOptions> options)
        : this(
            static message => Console.Out.WriteLine(message),
            options)
    {
    }

    internal MsFunctionLogsLoggerProvider(Action<string> writeLine, IOptions<WorkerProxyEnvironmentOptions> options)
    {
        ArgumentNullException.ThrowIfNull(writeLine);
        ArgumentNullException.ThrowIfNull(options);

        _writeLine = writeLine;
        _containerName = options.Value.ContainerName;
        _stampName = options.Value.StampName;
        _tenantId = options.Value.TenantId;
    }

    public ILogger CreateLogger(string categoryName)
    {
        ArgumentNullException.ThrowIfNull(categoryName);

        return new MsFunctionLogsLogger(categoryName, _writeLine, _containerName, _stampName, _tenantId);
    }

    public void Dispose()
    {
    }
}
