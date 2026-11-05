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
    RuntimeWorkerAuthenticationKeys? GetWorkerKeys();

    RuntimeAppAuthenticationKeys? GetAppKeys();
}

internal sealed class RuntimeWorkerAuthenticationKeys(
    string workerId,
    ReadOnlyMemory<byte> workerKey)
{
    public string WorkerId { get; } = workerId;

    public ReadOnlyMemory<byte> WorkerKey { get; } = workerKey;
}

internal sealed class RuntimeAppAuthenticationKeys(
    string workerId,
    string appName,
    ReadOnlyMemory<byte> appKey)
{
    public string WorkerId { get; } = workerId;

    public string AppName { get; } = appName;

    public ReadOnlyMemory<byte> AppKey { get; } = appKey;
}

internal sealed partial class RuntimeAuthenticationKeyProvider : IRuntimeAuthenticationKeyProvider
{
    private readonly WorkerPodStateManager _manager;
    private readonly IConfiguration _configuration;
    private readonly ContainerManagementAuthenticationHandler.Credentials _container;
    private readonly ILogger<RuntimeAuthenticationKeyProvider> _logger;

    public RuntimeAuthenticationKeyProvider(
        IConfiguration configuration,
        IOptionsMonitor<ContainerManagementAuthenticationHandler.Credentials> options,
        WorkerPodStateManager manager,
        ILogger<RuntimeAuthenticationKeyProvider> logger)
    {
        _manager = manager;
        _configuration = configuration;
        _container = options.Get(ContainerManagementAuthenticationHandler.SchemeName);
        _logger = logger;
        _ = ReadAppKey();
    }

    public RuntimeWorkerAuthenticationKeys? GetWorkerKeys()
        => GetAssignedState() is null ? null : new(_container.PodName, _container.Key);

    public RuntimeAppAuthenticationKeys? GetAppKeys()
    {
        if (GetAssignedState() is not { FunctionAppName: string appName })
        {
            return null;
        }

        byte[] appKey = ReadAppKey();
        if (appKey.Length < 32)
        {
            throw new InvalidOperationException("WorkerProxy application authentication configuration is unavailable.");
        }

        return new(_container.PodName, appName, appKey);
    }

    private byte[] ReadAppKey()
    {
        string? encodedAppKey = _configuration[RuntimeWorkerAuthentication.AppKeySetting];
        bool hasAppKey = RuntimeWorkerAuthentication.TryDecodeAppKey(encodedAppKey, out byte[] appKey);
        _manager.SetAuthenticationConfigurationError(!hasAppKey);
        if (!hasAppKey)
        {
            Log.KeyUnavailable(_logger, RuntimeWorkerAuthentication.AppKeySetting,
                string.IsNullOrWhiteSpace(encodedAppKey) ? AuthenticationFailureReason.MissingKey : AuthenticationFailureReason.InvalidKey);
        }

        return appKey;
    }

    private WorkerPodState? GetAssignedState()
    {
        WorkerPodState state = _manager.State;
        if (state.AssignmentState is not WorkerAssignmentState.Ready
            || string.IsNullOrWhiteSpace(state.FunctionAppName))
        {
            return null;
        }

        return state;
    }

    private static partial class Log
    {
        [LoggerMessage(0, LogLevel.Error, "WorkerProxy authentication configuration is unavailable: {Setting}, {Reason}.")]
        public static partial void KeyUnavailable(ILogger logger, string setting, AuthenticationFailureReason reason);
    }
}
