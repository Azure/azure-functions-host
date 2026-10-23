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
            Log.AuthenticationRejected(logger);

            return false;
        }

        if (!RuntimeWorkerAuthentication.ValidateHeaders(
            name => context.Request.Headers[name] is [string token] ? token : null,
            keys.WorkerKey.Span, keys.WorkerId, timeProvider.GetUtcNow()))
        {
            Log.AuthenticationRejected(logger);

            return false;
        }

        foreach (string name in RuntimeWorkerAuthentication.TokenHeaders)
        {
            context.Request.Headers.Remove(name);
        }

        return true;
    }

    public string CreateAppProof()
    {
        RuntimeAuthenticationKeys? keys = keyProvider.GetKeys();
        if (keys is null || keys.AppKey.Length < 32)
        {
            throw new InvalidOperationException("WorkerProxy application authentication configuration is unavailable.");
        }

        return RuntimeWorkerAuthentication.CreateAppProof(
            keys.AppKey.Span, keys.AppName, keys.WorkerId, timeProvider.GetUtcNow());
    }

    private static partial class Log
    {
        [LoggerMessage(0, LogLevel.Warning, "Runtime request authentication failed.")]
        public static partial void AuthenticationRejected(ILogger logger);
    }
}
