// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Security.Cryptography;
using Azure.Functions.Authentication;
using Azure.Functions.WorkerProxy.Authentication;
using Grpc.Core;

namespace Azure.Functions.WorkerProxy.Tests;

internal static class RuntimeAuthenticationTestData
{
    public const string WorkerId = "test-worker-pod";
    public const string AppName = "test-app";
    public static readonly byte[] WorkerKey = SHA256.HashData("test-worker-key"u8);
    public static readonly byte[] AppKey = SHA256.HashData("test-app-key"u8);
    public static readonly string EncodedWorkerKey = Convert.ToBase64String(WorkerKey);
    public static readonly string EncodedAppKey = Convert.ToHexString(AppKey);

    public static RuntimeAuthenticationKeys Keys => new(WorkerId, AppName, WorkerKey, AppKey);

    public static (string WorkerToken, string AppToken) CreateTokens(DateTimeOffset? now = null)
        => (
            RuntimeWorkerAuthentication.CreateToken(WorkerKey, RuntimeWorkerAuthentication.WorkerAudience(WorkerId),
                RuntimeWorkerAuthentication.WorkerPurpose, now ?? DateTimeOffset.UtcNow),
            RuntimeWorkerAuthentication.CreateToken(AppKey, RuntimeWorkerAuthentication.AppAudience(AppName),
                RuntimeWorkerAuthentication.AppPurpose, now ?? DateTimeOffset.UtcNow));

    public static Metadata CreateHeaders()
    {
        (string workerToken, string appToken) = CreateTokens();

        return new()
        {
            { RuntimeWorkerAuthentication.WorkerTokenHeader, workerToken },
            { RuntimeWorkerAuthentication.AppTokenHeader, appToken }
        };
    }
}
