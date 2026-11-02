// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Script.Grpc.TokenServiceProxy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Azure.Functions.WorkerProxy.Identity;

internal sealed class TokenServiceProxyWorkerOptionsValidator(IConfiguration configuration) : IValidateOptions<TokenServiceProxyWorkerOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, TokenServiceProxyWorkerOptions options)
    {
        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        List<string> failures = [];
        if (options.Port is < 0 or > 65535)
        {
            failures.Add($"The Token Service proxy {nameof(options.Port)} must be between 0 and 65535.");
        }

        if (!TokenServiceProxyProtocol.IsTokenPath(options.TokenPath))
        {
            failures.Add($"The Token Service proxy {nameof(options.TokenPath)} must specify a valid token-acquisition path.");
        }

        if (string.IsNullOrWhiteSpace(configuration[TokenServiceProxyWorkerOptions.SecretSettingName]))
        {
            failures.Add($"The enabled Token Service proxy requires the platform-injected {TokenServiceProxyWorkerOptions.SecretSettingName}.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
