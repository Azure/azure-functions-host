// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using Azure.Functions.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Binds a linked worker's key to trusted worker and specialized application identities.
/// </summary>
internal sealed partial class RuntimeWorkerCredentials
{
    private readonly IConfiguration _configuration;
    private readonly TimeProvider _timeProvider;
    private readonly byte[] _workerKey;
    private readonly string _workerId;
    private readonly ILogger _logger;

    public RuntimeWorkerCredentials(string workerId, string runtimeAccessKey, IConfiguration configuration, TimeProvider timeProvider,
        ILogger logger = null)
    {
        if (!RuntimeWorkerAuthentication.TryDecodeWorkerKey(runtimeAccessKey, out _workerKey))
        {
            throw new ArgumentException("A hexadecimal or base64 WorkerProxy container key containing at least 256 bits is required.", nameof(runtimeAccessKey));
        }

        _workerId = workerId;
        _configuration = configuration;
        _timeProvider = timeProvider;
        _logger = logger ?? NullLogger.Instance;
    }

    public bool MatchesKey(string runtimeAccessKey)
        => RuntimeWorkerAuthentication.TryDecodeWorkerKey(runtimeAccessKey, out byte[] key)
            && CryptographicOperations.FixedTimeEquals(_workerKey, key);

    public IReadOnlyDictionary<string, string> CreateHeaders()
        => RuntimeWorkerAuthentication.CreateHeaders(_workerKey, _workerId, _timeProvider.GetUtcNow());

    public void ValidateAppProof(string proof)
    {
        string appName = _configuration[RuntimeWorkerAuthentication.AppNameSetting];
        if (string.IsNullOrWhiteSpace(appName)
            || !RuntimeWorkerAuthentication.TryDecodeAppKey(_configuration[RuntimeWorkerAuthentication.AppKeySetting], out byte[] appKey))
        {
            string key = _configuration[RuntimeWorkerAuthentication.AppKeySetting];
            AuthenticationFailureReason configurationFailure = string.IsNullOrWhiteSpace(appName) ? AuthenticationFailureReason.MissingIdentity
                : string.IsNullOrWhiteSpace(key) ? AuthenticationFailureReason.MissingKey : AuthenticationFailureReason.InvalidKey;
            Log.AppProofRejected(_logger, configurationFailure);
            throw new WorkerLinkException(WorkerLinkFailureReason.AuthenticationUnavailable,
                "The specialized application authentication configuration is unavailable.");
        }

        if (!RuntimeWorkerAuthentication.ValidateAppProof(proof, appKey, appName, _workerId, _timeProvider.GetUtcNow(), out AuthenticationFailureReason reason))
        {
            Log.AppProofRejected(_logger, reason);
            throw new WorkerLinkException(WorkerLinkFailureReason.Unavailable, "WorkerProxy application authentication failed.");
        }
    }

    private static partial class Log
    {
        [LoggerMessage(0, LogLevel.Warning, "WorkerProxy application proof authentication failed: {Reason}.")]
        public static partial void AppProofRejected(ILogger logger, AuthenticationFailureReason reason);
    }
}
