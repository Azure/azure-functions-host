// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#nullable enable

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using Azure.Functions.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Binds a linked worker's key to trusted worker and specialized application identities.
/// </summary>
internal sealed partial class RuntimeWorkerCredentials
{
    private readonly TimeProvider _timeProvider;
    private readonly byte[] _workerKey;
    private readonly string _workerId;
    private readonly ILogger _logger;

    public RuntimeWorkerCredentials(
        string workerId, string runtimeAccessKey, TimeProvider timeProvider, ILogger? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
        ArgumentNullException.ThrowIfNull(timeProvider);

        if (!RuntimeWorkerAuthentication.TryDecodeWorkerKey(runtimeAccessKey, out _workerKey))
        {
            throw new WorkerLinkException(WorkerLinkFailureReason.InvalidRuntimeAccessKey,
                "A hexadecimal or base64 WorkerProxy container key containing at least 256 bits is required.");
        }

        _workerId = workerId;
        _timeProvider = timeProvider;
        _logger = logger ?? NullLogger.Instance;
    }

    public bool MatchesKey(RuntimeWorkerCredentials credentials)
        => CryptographicOperations.FixedTimeEquals(_workerKey, credentials._workerKey);

    public IEnumerable<KeyValuePair<string, string>> CreateHeaders()
        => [new(RuntimeWorkerAuthentication.WorkerTokenHeader, RuntimeWorkerAuthentication.CreateToken(
            _workerKey, RuntimeWorkerAuthentication.WorkerAudience(_workerId), RuntimeWorkerAuthentication.WorkerPurpose, _timeProvider.GetUtcNow()))];

    public void ValidateAppProof(string? proof, string appName, ReadOnlySpan<byte> appKey)
    {
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
