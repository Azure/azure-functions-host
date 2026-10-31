// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Azure.Functions.WorkerProxy.Management;

internal enum WorkerAssignmentAuthenticationResult
{
    Unconfigured,
    Authenticated,
    Rejected
}

internal static class WorkerAssignmentAuthentication
{
    public const string HeaderName = "x-ms-functions-worker-assignment-token";

    public static WorkerAssignmentAuthenticationResult Authenticate(HttpRequest request, string? configuredToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(configuredToken))
        {
            return WorkerAssignmentAuthenticationResult.Unconfigured;
        }

        if (!request.Headers.TryGetValue(HeaderName, out StringValues suppliedTokens) ||
            suppliedTokens.Count != 1 ||
            string.IsNullOrEmpty(suppliedTokens[0]))
        {
            return WorkerAssignmentAuthenticationResult.Rejected;
        }

        byte[] expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(configuredToken));
        byte[] suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(suppliedTokens[0]!));
        return CryptographicOperations.FixedTimeEquals(expectedHash, suppliedHash)
            ? WorkerAssignmentAuthenticationResult.Authenticated
            : WorkerAssignmentAuthenticationResult.Rejected;
    }
}
