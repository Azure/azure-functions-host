// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Azure.Functions.Authentication;
using Azure.Functions.WorkerProxy.State;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
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

internal sealed partial class RuntimeAuthenticationKeyProvider : IRuntimeAuthenticationKeyProvider
{
    private readonly WorkerPodStateManager _manager;
    private readonly IConfiguration _configuration;
    private readonly ContainerManagementAuthenticationHandler.Credentials _container;

    public RuntimeAuthenticationKeyProvider(
        IConfiguration configuration, IOptionsMonitor<ContainerManagementAuthenticationHandler.Credentials> options,
        WorkerPodStateManager manager,
        ILogger<RuntimeAuthenticationKeyProvider> logger)
    {
        _manager = manager;
        _configuration = configuration;
        _container = options.Get(ContainerManagementAuthenticationHandler.SchemeName);
        string? encodedAppKey = configuration[RuntimeWorkerAuthentication.AppKeySetting];
        if (!RuntimeWorkerAuthentication.TryDecodeAppKey(encodedAppKey, out _))
        {
            Log.KeyUnavailable(logger, RuntimeWorkerAuthentication.AppKeySetting,
                string.IsNullOrWhiteSpace(encodedAppKey) ? AuthenticationFailureReason.MissingKey : AuthenticationFailureReason.InvalidKey);
        }
    }

    public RuntimeAuthenticationKeys? GetKeys()
    {
        WorkerPodState state = _manager.State;
        if (_container.Failure is not AuthenticationFailureReason.None || state.AssignmentState is not WorkerAssignmentState.Ready
            || string.IsNullOrWhiteSpace(state.FunctionAppName))
        {
            return null;
        }

        RuntimeWorkerAuthentication.TryDecodeAppKey(_configuration[RuntimeWorkerAuthentication.AppKeySetting], out byte[] appKey);

        return new(_container.PodName, state.FunctionAppName, _container.Key, appKey);
    }

    private static partial class Log
    {
        [LoggerMessage(0, LogLevel.Error, "WorkerProxy authentication configuration is unavailable: {Setting}, {Reason}.")]
        public static partial void KeyUnavailable(ILogger logger, string setting, AuthenticationFailureReason reason);
    }
}
