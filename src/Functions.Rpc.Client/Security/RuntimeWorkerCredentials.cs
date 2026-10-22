// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using Azure.Functions.Authentication;
using Microsoft.Extensions.Configuration;

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Binds a linked worker's key to trusted worker and specialized application identities.
/// </summary>
internal sealed class RuntimeWorkerCredentials
{
    private readonly IConfiguration _configuration;
    private readonly TimeProvider _timeProvider;
    private readonly byte[] _workerKey;
    private readonly string _workerId;

    public RuntimeWorkerCredentials(string workerId, string runtimeAccessKey, IConfiguration configuration, TimeProvider timeProvider)
    {
        if (!RuntimeWorkerAuthentication.TryDecodeWorkerKey(runtimeAccessKey, out _workerKey))
        {
            throw new ArgumentException("A base64 worker key containing at least 256 bits is required.", nameof(runtimeAccessKey));
        }

        _workerId = workerId;
        _configuration = configuration;
        _timeProvider = timeProvider;
    }

    public bool MatchesKey(string runtimeAccessKey)
        => RuntimeWorkerAuthentication.TryDecodeWorkerKey(runtimeAccessKey, out byte[] key)
            && CryptographicOperations.FixedTimeEquals(_workerKey, key);

    public IReadOnlyDictionary<string, string> CreateHeaders()
    {
        string appName = _configuration[RuntimeWorkerAuthentication.AppNameSetting];
        if (string.IsNullOrWhiteSpace(appName)
            || !RuntimeWorkerAuthentication.TryDecodeAppKey(_configuration[RuntimeWorkerAuthentication.AppKeySetting], out byte[] appKey))
        {
            throw new WorkerLinkException(WorkerLinkFailureReason.AuthenticationUnavailable,
                "The specialized application authentication configuration is unavailable.");
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();

        return RuntimeWorkerAuthentication.CreateHeaders(_workerKey, _workerId, appKey, appName, now);
    }
}
