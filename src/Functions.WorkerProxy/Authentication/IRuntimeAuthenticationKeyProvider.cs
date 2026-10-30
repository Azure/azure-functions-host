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
    private readonly string _workerId;
    private readonly byte[] _workerKey;

    public RuntimeAuthenticationKeyProvider(
        IConfiguration configuration, IOptions<WorkerProxyOptions> options, WorkerPodStateManager manager,
        ILogger<RuntimeAuthenticationKeyProvider> logger)
    {
        _manager = manager;
        _configuration = configuration;
        _workerId = options.Value.PodName;
        ValidateKey(configuration[RuntimeWorkerAuthentication.WorkerKeySetting], RuntimeWorkerAuthentication.WorkerKeySetting, logger, out _workerKey);
        ValidateKey(configuration[RuntimeWorkerAuthentication.AppKeySetting], RuntimeWorkerAuthentication.AppKeySetting, logger, out _);
    }

    public RuntimeAuthenticationKeys? GetKeys()
    {
        WorkerPodState state = _manager.State;
        if (_workerKey.Length < 32 || state.AssignmentState is not WorkerAssignmentState.Ready
            || string.IsNullOrWhiteSpace(state.FunctionAppName))
        {
            return null;
        }

        RuntimeWorkerAuthentication.TryDecodeAppKey(_configuration[RuntimeWorkerAuthentication.AppKeySetting], out byte[] appKey);

        return new(_workerId, state.FunctionAppName, _workerKey, appKey);
    }

    private static void ValidateKey(string? encoded, string setting, ILogger logger, out byte[] key)
    {
        if (!RuntimeWorkerAuthentication.TryDecodeWorkerKey(encoded, out key))
        {
            Log.KeyUnavailable(logger, setting,
                string.IsNullOrWhiteSpace(encoded) ? AuthenticationFailureReason.MissingKey : AuthenticationFailureReason.InvalidKey);
        }
    }

    private static partial class Log
    {
        [LoggerMessage(0, LogLevel.Error, "WorkerProxy authentication configuration is unavailable: {Setting}, {Reason}.")]
        public static partial void KeyUnavailable(ILogger logger, string setting, AuthenticationFailureReason reason);
    }
}
