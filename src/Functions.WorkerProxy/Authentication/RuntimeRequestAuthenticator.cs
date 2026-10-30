// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Azure.Functions.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.WorkerProxy.Authentication;

/// <summary>
/// Authenticates newly opened runtime requests without changing customer Authorization headers.
/// </summary>
internal sealed partial class RuntimeRequestAuthenticator(
    IRuntimeAuthenticationKeyProvider keyProvider, TimeProvider timeProvider, ILogger<RuntimeRequestAuthenticator> logger)
{
    public bool TryAuthenticate(HttpContext context)
    {
        RuntimeAuthenticationKeys? keys = keyProvider.GetKeys();
        if (!context.Request.IsHttps || keys is null)
        {
            Log.AuthenticationRejected(logger, keys is null ? AuthenticationFailureReason.AssignmentUnavailable : AuthenticationFailureReason.Transport);

            return false;
        }

        AuthenticationFailureReason reason;
        if (context.Request.Headers[RuntimeWorkerAuthentication.WorkerTokenHeader].Count > 1
            || context.Request.Headers[RuntimeWorkerAuthentication.WorkerTokenHeader].ToString().Contains(',', StringComparison.Ordinal))
        {
            reason = AuthenticationFailureReason.DuplicateToken;
        }
        else if (RuntimeWorkerAuthentication.ValidateToken(
            context.Request.Headers[RuntimeWorkerAuthentication.WorkerTokenHeader].ToString(),
            keys.WorkerKey.Span, RuntimeWorkerAuthentication.WorkerAudience(keys.WorkerId),
            RuntimeWorkerAuthentication.WorkerPurpose, timeProvider.GetUtcNow(), out reason))
        {
            foreach (string name in RuntimeWorkerAuthentication.TokenHeaders)
            {
                context.Request.Headers.Remove(name);
            }

            return true;
        }

        Log.AuthenticationRejected(logger, reason);

        return false;
    }

    public string CreateAppProof()
    {
        RuntimeAuthenticationKeys? keys = keyProvider.GetKeys();
        if (keys is null || keys.AppKey.Length < 32)
        {
            Log.AppProofUnavailable(logger, keys is null ? AuthenticationFailureReason.AssignmentUnavailable : AuthenticationFailureReason.InvalidKey);
            throw new InvalidOperationException("WorkerProxy application authentication configuration is unavailable.");
        }

        return RuntimeWorkerAuthentication.CreateAppProof(
            keys.AppKey.Span, keys.AppName, keys.WorkerId, timeProvider.GetUtcNow());
    }

    private static partial class Log
    {
        [LoggerMessage(0, LogLevel.Warning, "Runtime request authentication failed: {Reason}.")]
        public static partial void AuthenticationRejected(ILogger logger, AuthenticationFailureReason reason);

        [LoggerMessage(1, LogLevel.Error, "WorkerProxy application proof cannot be created: {Reason}.")]
        public static partial void AppProofUnavailable(ILogger logger, AuthenticationFailureReason reason);
    }
}
