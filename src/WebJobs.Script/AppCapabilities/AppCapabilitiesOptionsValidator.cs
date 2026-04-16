// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Text;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.AppCapabilities
{
    internal sealed class AppCapabilitiesOptionsValidator : IValidateOptions<AppCapabilitiesOptions>
    {
        // Size-based: matches existing Antares-enforced limit for sync triggers API
        private const int MaxCapabilitiesSizeBytes = ScriptConstants.MaxTriggersStringLength; // 204800

        public ValidateOptionsResult Validate(string name, AppCapabilitiesOptions options)
        {
            IDictionary<string, string> capabilities = options;

            var sizeBytes = 0;

            foreach (var kvp in capabilities)
            {
                if (kvp.Key is not null)
                {
                    sizeBytes += Encoding.UTF8.GetByteCount(kvp.Key);
                }

                if (kvp.Value is not null)
                {
                    sizeBytes += Encoding.UTF8.GetByteCount(kvp.Value);
                }
            }

            if (sizeBytes > MaxCapabilitiesSizeBytes)
            {
                return ValidateOptionsResult.Fail(
                    $"Capabilities size ({sizeBytes} bytes) exceeds maximum ({MaxCapabilitiesSizeBytes} bytes).");
            }

            return ValidateOptionsResult.Success;
        }
    }
}
