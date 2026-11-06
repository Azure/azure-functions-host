// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#nullable enable

using System;
using Azure.Functions.Authentication;
using Microsoft.Extensions.Options;

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Contains application authentication values validated when a worker supplies its reverse proof.
/// </summary>
internal sealed class RuntimeApplicationAuthenticationOptions
{
    public string? AppName { get; set; }

    public string? EncodedAppKey { get; set; }

    internal byte[] AppKey { get; set; } = [];
}

internal sealed class RuntimeApplicationAuthenticationOptionsValidator : IValidateOptions<RuntimeApplicationAuthenticationOptions>
{
    public ValidateOptionsResult Validate(string? name, RuntimeApplicationAuthenticationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.AppName))
        {
            return ValidateOptionsResult.Fail(nameof(AuthenticationFailureReason.MissingIdentity));
        }

        if (!RuntimeWorkerAuthentication.TryDecodeAppKey(options.EncodedAppKey, out byte[] key))
        {
            return ValidateOptionsResult.Fail(string.IsNullOrWhiteSpace(options.EncodedAppKey)
                ? nameof(AuthenticationFailureReason.MissingKey) : nameof(AuthenticationFailureReason.InvalidKey));
        }

        options.AppKey = key;

        return ValidateOptionsResult.Success;
    }
}
