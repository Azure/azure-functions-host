// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Security.Cryptography;
using System.Text;
using Google.Protobuf;

namespace Microsoft.Azure.WebJobs.Script.Grpc.Messages;

/// <summary>
/// Authenticates WorkerProxy-originated managed identity token requests.
/// </summary>
internal static class ManagedIdentityTokenRequestAuthenticator
{
    public const string RuntimeAccessKeyEnvironmentVariable = "FUNCTIONS_WORKER_RUNTIME_ACCESS_KEY";

    public static void Sign(
        string runtimeAccessKey,
        string requestId,
        ManagedIdentityTokenRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeAccessKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        ArgumentNullException.ThrowIfNull(request);

        request.RuntimeAccessKeySignature = ByteString.CopyFrom(ComputeSignature(runtimeAccessKey, requestId, request));
    }

    public static bool IsValid(
        string runtimeAccessKey,
        string requestId,
        ManagedIdentityTokenRequest request)
    {
        if (string.IsNullOrWhiteSpace(runtimeAccessKey)
            || string.IsNullOrWhiteSpace(requestId)
            || request is null
            || request.RuntimeAccessKeySignature.IsEmpty)
        {
            return false;
        }

        byte[] expected = ComputeSignature(runtimeAccessKey, requestId, request);
        return CryptographicOperations.FixedTimeEquals(
            expected,
            request.RuntimeAccessKeySignature.Span);
    }

    private static byte[] ComputeSignature(
        string runtimeAccessKey,
        string requestId,
        ManagedIdentityTokenRequest request)
    {
        ManagedIdentityTokenRequest unsignedRequest = request.Clone();
        unsignedRequest.RuntimeAccessKeySignature = ByteString.Empty;

        byte[] key = Encoding.UTF8.GetBytes(runtimeAccessKey);
        byte[] requestIdBytes = Encoding.UTF8.GetBytes(requestId);
        byte[] requestBytes = unsignedRequest.ToByteArray();
        byte[] payload = new byte[requestIdBytes.Length + 1 + requestBytes.Length];
        requestIdBytes.CopyTo(payload, 0);
        requestBytes.CopyTo(payload, requestIdBytes.Length + 1);

        try
        {
            using HMACSHA256 hmac = new(key);
            return hmac.ComputeHash(payload);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(payload);
        }
    }
}
