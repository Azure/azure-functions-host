// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Azure.Functions.Authentication;
using Azure.Functions.WorkerProxy.State;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Azure.Functions.WorkerProxy.Authentication;

/// <summary>
/// Supplies private, trusted worker and application authentication state.
/// </summary>
internal interface IRuntimeAuthenticationKeyProvider
{
    RuntimeAuthenticationKeys? GetKeys();
}

internal sealed class RuntimeAuthenticationKeys(
    string workerId, string appName, ReadOnlyMemory<byte> workerKey, ReadOnlyMemory<byte> appKey)
{
    public string WorkerId { get; } = workerId;

    public string AppName { get; } = appName;

    public ReadOnlyMemory<byte> WorkerKey { get; } = workerKey;

    public ReadOnlyMemory<byte> AppKey { get; } = appKey;
}

internal sealed class RuntimeAuthenticationKeyProvider : IRuntimeAuthenticationKeyProvider
{
    private readonly WorkerPodStateManager _manager;
    private readonly string _workerId;
    private readonly byte[] _workerKey;

    public RuntimeAuthenticationKeyProvider(
        IConfiguration configuration, IOptions<WorkerProxyOptions> options, WorkerPodStateManager manager)
    {
        _manager = manager;
        _workerId = options.Value.PodName;
        RuntimeWorkerAuthentication.TryDecodeWorkerKey(configuration[RuntimeWorkerAuthentication.WorkerKeySetting], out _workerKey);
    }

    public RuntimeAuthenticationKeys? GetKeys()
    {
        if (_workerKey.Length < 32 || _manager.RuntimeAuthentication is not { } application || application.Key.IsEmpty)
        {
            return null;
        }

        return new(_workerId, application.AppName, _workerKey, application.Key);
    }
}
