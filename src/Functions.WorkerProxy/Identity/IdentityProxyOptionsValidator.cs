// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Script.Grpc.HttpProxy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Azure.Functions.WorkerProxy.Identity;

internal sealed class IdentityProxyOptionsValidator(IConfiguration configuration) : IValidateOptions<IdentityProxyOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, IdentityProxyOptions options)
    {
        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        List<string> failures = [];
        if (options.Port is < 0 or > 65535)
        {
            failures.Add($"The identity proxy {nameof(options.Port)} must be between 0 and 65535.");
        }

        if (!HttpProxyProtocol.IsTokenPath(options.TokenPath))
        {
            failures.Add($"The identity proxy {nameof(options.TokenPath)} must specify a valid token-acquisition path.");
        }

        if (string.IsNullOrWhiteSpace(configuration[IdentityProxyOptions.SecretSettingName]))
        {
            failures.Add($"The enabled identity proxy requires the platform-injected {IdentityProxyOptions.SecretSettingName}.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
